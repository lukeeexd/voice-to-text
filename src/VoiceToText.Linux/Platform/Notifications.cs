using Tmds.DBus;
using VoiceToText.Diagnostics;

namespace VoiceToText.Linux.Platform;

[DBusInterface("org.freedesktop.Notifications")]
internal interface INotifications : IDBusObject
{
    Task<uint> NotifyAsync(string appName, uint replacesId, string appIcon, string summary,
        string body, string[] actions, IDictionary<string, object> hints, int expireTimeout);
}

/// <summary>Desktop notifications via org.freedesktop.Notifications. Best-effort:
/// failures are logged, never thrown (a missing notification daemon must not break dictation).</summary>
public static class Notifications
{
    public static void Show(string summary, string body)
    {
        try
        {
            var proxy = Connection.Session.CreateProxy<INotifications>(
                "org.freedesktop.Notifications", "/org/freedesktop/Notifications");
            proxy.NotifyAsync("VoiceToText", 0, "", summary, body, [],
                new Dictionary<string, object>(), 4000).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error("Desktop notification failed", ex);
        }
    }
}
