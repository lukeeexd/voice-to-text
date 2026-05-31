namespace VoiceToText.Injection;

/// <summary>
/// Default injection strategy: put the text on the clipboard as Unicode, paste
/// it with a synthesized Ctrl+V, then restore the previous clipboard contents.
/// This is fast (whole utterance inserted at once) and handles full Unicode.
///
/// Known limitation: Windows UIPI blocks synthesized input (and therefore this
/// paste) into elevated/admin windows — dictation into such windows is not
/// supported.
/// </summary>
public sealed class ClipboardTextInjector : ITextInjector
{
    public void Inject(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        string? previous = null;
        try
        {
            if (Clipboard.ContainsText())
                previous = Clipboard.GetText();
        }
        catch
        {
            // Clipboard could be locked by another process; ignore and proceed.
        }

        if (!TrySetClipboardText(text))
            return; // don't paste stale clipboard contents

        NativeInput.ReleaseModifiers();
        Thread.Sleep(40);
        NativeInput.SendPaste();
        Thread.Sleep(120);

        if (previous is not null)
            TrySetClipboardText(previous);
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }
}
