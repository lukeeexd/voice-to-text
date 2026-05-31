using System.Drawing;

namespace VoiceToText.App;

/// <summary>
/// An invisible top-level window. It hosts the message pump that receives
/// WM_HOTKEY for the global hotkey, and (being a Control) gives us BeginInvoke
/// to marshal work back onto the UI/STA thread (needed for clipboard access).
/// It is never shown.
/// </summary>
internal sealed class HiddenWindow : Form
{
    private const int WM_HOTKEY = 0x0312;

    /// <summary>Raised on the UI thread when a registered hotkey fires; arg is the hotkey id.</summary>
    public event Action<int>? HotkeyMessageReceived;

    public HiddenWindow()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
        Opacity = 0d;
    }

    /// <summary>Force native handle creation without showing the window.</summary>
    public void EnsureHandle() => _ = Handle;

    // Never become visible.
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
            HotkeyMessageReceived?.Invoke((int)m.WParam);
        base.WndProc(ref m);
    }
}
