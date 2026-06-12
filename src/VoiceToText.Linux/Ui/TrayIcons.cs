using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VoiceToText.Linux.Ui;

/// <summary>
/// Runtime-drawn tray icons (no asset pipeline): a filled circle whose color is the
/// dictation state — slate idle, red recording, amber transcribing.
/// </summary>
internal static class TrayIcons
{
    public static WindowIcon Idle { get; } = Circle(0x64, 0x74, 0x8B);        // slate
    public static WindowIcon Recording { get; } = Circle(0xE0, 0x4A, 0x3A);   // red
    public static WindowIcon Transcribing { get; } = Circle(0xE8, 0xA8, 0x33); // amber

    private static unsafe WindowIcon Circle(byte r, byte g, byte b)
    {
        const int size = 32;
        var bmp = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            var ptr = (byte*)fb.Address;
            const double cx = size / 2.0 - 0.5, cy = size / 2.0 - 0.5, radius = size / 2.0 - 1.5;
            for (var y = 0; y < size; y++)
            {
                var row = ptr + y * fb.RowBytes;
                for (var x = 0; x < size; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    // 1px anti-aliased edge so the circle doesn't look jagged in the tray.
                    var alpha = dist <= radius ? 1.0 : dist >= radius + 1.0 ? 0.0 : radius + 1.0 - dist;
                    var a = (byte)(alpha * 255);
                    var p = row + x * 4;
                    p[0] = (byte)(b * alpha); // premultiplied BGRA
                    p[1] = (byte)(g * alpha);
                    p[2] = (byte)(r * alpha);
                    p[3] = a;
                }
            }
        }
        return new WindowIcon(bmp);
    }
}
