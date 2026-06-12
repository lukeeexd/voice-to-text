using VoiceToText.Diagnostics;
using VoiceToText.Injection;
using VoiceToText.Settings;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Clipboard-always text injection: the transcript is copied first (no dictation is
/// ever lost), then a paste keystroke is attempted — XTEST on X11 sessions, the
/// RemoteDesktop portal on Wayland. When neither works (or the user declined the
/// portal), a desktop notification tells the user to press Ctrl+V.
/// </summary>
public sealed class LinuxTextInjector(Func<string, Task> setClipboard, AppSettings settings) : ITextInjector
{
    private readonly PortalRemoteDesktop _portal = new(
        () => settings.PortalRestoreToken,
        token =>
        {
            settings.PortalRestoreToken = token;
            try { settings.Save(); } catch { /* non-critical */ }
        });

    /// <summary>Raised after each injection with whether the paste keystroke went out.</summary>
    public event Action<bool>? Completed;

    public void Inject(string text)
    {
        try
        {
            setClipboard(text).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error("Clipboard write failed", ex);
        }

        var pasted = false;
        try
        {
            pasted = !SessionInfo.IsWayland && SessionInfo.HasX11Display
                ? XTestPaste.PasteCtrlV()
                : _portal.PasteCtrlV();
        }
        catch (Exception ex)
        {
            Log.Error("Paste injection failed", ex);
        }

        if (!pasted)
            Notifications.Show("Transcript copied", "Press Ctrl+V to paste it.");
        Completed?.Invoke(pasted);
    }
}
