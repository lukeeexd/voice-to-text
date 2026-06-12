using System.Diagnostics;
using Avalonia.Threading;
using VoiceToText.Diagnostics;
using VoiceToText.Linux.Platform;

namespace VoiceToText.Linux.Ui;

/// <summary>
/// Clipboard writes with belt and braces: the app's hidden window's Avalonia
/// clipboard first (marshaled to the UI thread), then the standard CLI tools
/// (wl-copy on Wayland, xclip/xsel on X11). The transcript must reach the
/// clipboard even if one path misbehaves.
/// </summary>
internal static class ClipboardHelper
{
    public static async Task SetTextAsync(string text)
    {
        try
        {
            var clipboard = VttApp.SharedClipboard;
            if (clipboard is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => clipboard.SetTextAsync(text))
                    .ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Avalonia clipboard failed; falling back to CLI tools", ex);
        }

        var candidates = SessionInfo.IsWayland
            ? new[] { ("wl-copy", ""), ("xclip", "-selection clipboard") }
            : new[] { ("xclip", "-selection clipboard"), ("xsel", "--clipboard --input"), ("wl-copy", "") };
        foreach (var (tool, args) in candidates)
        {
            if (await TryPipeAsync(tool, args, text).ConfigureAwait(false))
                return;
        }
        Log.Error("No clipboard mechanism available (tried Avalonia, wl-copy, xclip, xsel).");
    }

    private static async Task<bool> TryPipeAsync(string tool, string args, string text)
    {
        try
        {
            var psi = new ProcessStartInfo(tool, args)
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            await p.StandardInput.WriteAsync(text).ConfigureAwait(false);
            p.StandardInput.Close();
            await p.WaitForExitAsync().ConfigureAwait(false);
            return p.ExitCode == 0;
        }
        catch
        {
            return false; // tool not installed
        }
    }
}
