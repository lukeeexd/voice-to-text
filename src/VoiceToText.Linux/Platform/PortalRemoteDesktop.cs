using Tmds.DBus;
using VoiceToText.Diagnostics;

namespace VoiceToText.Linux.Platform;

// PUBLIC on purpose: Tmds.DBus emits proxy types into a separate dynamic assembly,
// which cannot implement internal interfaces (TypeLoadException at CreateProxy).
[DBusInterface("org.freedesktop.portal.RemoteDesktop")]
public interface IRemoteDesktopPortal : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> SelectDevicesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);
    Task NotifyKeyboardKeycodeAsync(ObjectPath sessionHandle, IDictionary<string, object> options, int keycode, uint state);
}

[DBusInterface("org.freedesktop.portal.Request")]
public interface IPortalRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint response, IDictionary<string, object> results)> handler);
}

/// <summary>
/// The sanctioned Wayland input-injection path: org.freedesktop.portal.RemoteDesktop.
/// One permission dialog on first use; the grant persists via restore_token
/// (across reboots on GNOME; KDE currently re-prompts after reboot). The keyboard
/// session is kept open for the daemon's lifetime and rebuilt on demand.
/// </summary>
public sealed class PortalRemoteDesktop(Func<string?> loadToken, Action<string?> saveToken)
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const int KEY_LEFTCTRL = 29; // evdev keycodes
    private const int KEY_V = 47;

    private Connection? _connection;
    private IRemoteDesktopPortal? _portal;
    private ObjectPath? _session;
    private string? _uniqueName;
    private int _token;

    /// <summary>Synthesize Ctrl+V. False on any failure (caller falls back to a notification).</summary>
    public bool PasteCtrlV()
    {
        try
        {
            return PasteAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error("Portal paste failed", ex);
            _session = null; // force a session rebuild (one re-prompt) next time
            return false;
        }
    }

    private async Task<bool> PasteAsync()
    {
        if (_session is null && !await CreateKeyboardSessionAsync().ConfigureAwait(false))
            return false;

        var portal = _portal!;
        var session = _session!.Value;
        await Task.Delay(80).ConfigureAwait(false); // let physical hotkey modifiers clear
        var none = new Dictionary<string, object>();
        await portal.NotifyKeyboardKeycodeAsync(session, none, KEY_LEFTCTRL, 1).ConfigureAwait(false);
        await portal.NotifyKeyboardKeycodeAsync(session, none, KEY_V, 1).ConfigureAwait(false);
        await portal.NotifyKeyboardKeycodeAsync(session, none, KEY_V, 0).ConfigureAwait(false);
        await portal.NotifyKeyboardKeycodeAsync(session, none, KEY_LEFTCTRL, 0).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> CreateKeyboardSessionAsync()
    {
        if (_connection is null)
        {
            // Own connection (not the static Connection.Session): we need our unique
            // bus name from ConnectAsync to precompute portal request paths.
            var connection = new Connection(Address.Session!);
            var info = await connection.ConnectAsync().ConfigureAwait(false);
            _uniqueName = info.LocalName;
            _connection = connection;
        }
        _portal ??= _connection.CreateProxy<IRemoteDesktopPortal>(PortalService, PortalPath);

        var (created, createResults) = await CallWithResponseAsync(
            token => _portal.CreateSessionAsync(new Dictionary<string, object>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = $"vtt_session_{Environment.ProcessId}",
            })).ConfigureAwait(false);
        if (!created || !createResults.TryGetValue("session_handle", out var sessionHandle))
        {
            Log.Error("RemoteDesktop portal: CreateSession refused/unavailable.");
            return false;
        }
        var session = sessionHandle switch
        {
            ObjectPath p => p,
            string s => new ObjectPath(s),
            _ => default,
        };
        if (session == default) return false;

        var selectOptions = new Dictionary<string, object>
        {
            ["types"] = 1u,        // KEYBOARD
            ["persist_mode"] = 2u, // persist across restarts
        };
        var saved = loadToken();
        if (!string.IsNullOrEmpty(saved))
            selectOptions["restore_token"] = saved;
        var (selected, _) = await CallWithResponseAsync(
            token => { selectOptions["handle_token"] = token; return _portal.SelectDevicesAsync(session, selectOptions); })
            .ConfigureAwait(false);
        if (!selected)
        {
            Log.Error("RemoteDesktop portal: SelectDevices refused.");
            return false;
        }

        var (started, startResults) = await CallWithResponseAsync(
            token => _portal.StartAsync(session, "", new Dictionary<string, object> { ["handle_token"] = token }))
            .ConfigureAwait(false);
        if (!started)
        {
            Log.Error("RemoteDesktop portal: user declined the permission dialog.");
            return false;
        }
        if (startResults.TryGetValue("restore_token", out var t) && t is string newToken)
            saveToken(newToken);

        _session = session;
        Log.Info("RemoteDesktop portal keyboard session established.");
        return true;
    }

    /// <summary>
    /// Portal Request/Response pattern: precompute the request path from our unique
    /// bus name + handle_token and subscribe BEFORE the method call, so the Response
    /// signal can never be missed.
    /// </summary>
    private async Task<(bool ok, IDictionary<string, object> results)> CallWithResponseAsync(
        Func<string, Task<ObjectPath>> call)
    {
        var token = $"vtt{Interlocked.Increment(ref _token)}";
        var sender = (_uniqueName ?? "").TrimStart(':').Replace('.', '_');
        var expectedPath = new ObjectPath($"/org/freedesktop/portal/desktop/request/{sender}/{token}");

        var tcs = new TaskCompletionSource<(uint, IDictionary<string, object>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var request = _connection!.CreateProxy<IPortalRequest>(PortalService, expectedPath);
        using var watch = await request.WatchResponseAsync(r => tcs.TrySetResult((r.response, r.results)))
            .ConfigureAwait(false);

        var actualPath = await call(token).ConfigureAwait(false);
        if (actualPath != expectedPath)
        {
            // Old portal versions return a different path; re-subscribe there.
            var fallback = _connection.CreateProxy<IPortalRequest>(PortalService, actualPath);
            using var watch2 = await fallback.WatchResponseAsync(r => tcs.TrySetResult((r.response, r.results)))
                .ConfigureAwait(false);
            var done2 = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(120))).ConfigureAwait(false);
            if (done2 != tcs.Task) return (false, new Dictionary<string, object>());
            var (code2, results2) = tcs.Task.Result;
            return (code2 == 0, results2);
        }

        // The permission dialog can sit open for a while; 120 s covers a thoughtful user.
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(120))).ConfigureAwait(false);
        if (done != tcs.Task) return (false, new Dictionary<string, object>());
        var (code, results) = tcs.Task.Result;
        return (code == 0, results);
    }
}
