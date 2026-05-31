using System.Drawing;
using System.Drawing.Drawing2D;

namespace VoiceToText.App;

/// <summary>
/// Generates simple coloured tray icons per state at runtime so we don't ship
/// .ico assets: blue = idle, red = recording, amber = transcribing.
/// </summary>
internal sealed class IconCache : IDisposable
{
    private readonly Dictionary<AppState, Icon> _icons = new();

    public Icon Get(AppState state)
    {
        if (!_icons.TryGetValue(state, out var icon))
        {
            icon = Create(ColorFor(state));
            _icons[state] = icon;
        }
        return icon;
    }

    private static Color ColorFor(AppState state) => state switch
    {
        AppState.Recording => Color.FromArgb(220, 50, 50),
        AppState.Transcribing => Color.FromArgb(230, 160, 30),
        _ => Color.FromArgb(80, 150, 235),
    };

    private static Icon Create(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);

            // minimalist microphone glyph
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 13, 8, 6, 11);
            g.FillEllipse(white, 13, 5, 6, 6);
            g.FillEllipse(white, 13, 14, 6, 6);
            using var pen = new Pen(Color.White, 2);
            g.DrawLine(pen, 16, 21, 16, 25);
            g.DrawLine(pen, 12, 25, 20, 25);
        }

        var hicon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hicon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeIcon.DestroyIcon(hicon);
        }
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
            icon.Dispose();
        _icons.Clear();
    }
}
