using System.Runtime.InteropServices;

namespace VoiceToText.Linux.Platform;

/// <summary>
/// Minimal libpulse-simple binding: blocking record/playback streams. The pulse
/// daemon (PulseAudio, or PipeWire's pipewire-pulse shim) does all resampling and
/// mixing, so we always ask for exactly the formats the app works in.
/// </summary>
internal static partial class PulseNative
{
    private const string LibSimple = "libpulse-simple.so.0";
    private const string LibPulse = "libpulse.so.0";

    public const int PA_STREAM_PLAYBACK = 1;
    public const int PA_STREAM_RECORD = 2;
    public const int PA_SAMPLE_S16LE = 3;
    public const int PA_SAMPLE_FLOAT32LE = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct pa_sample_spec
    {
        public int format;     // pa_sample_format_t
        public uint rate;
        public byte channels;  // 4-byte aligned => marshaled size 12, matching the C layout
    }

    /// <summary>uint.MaxValue in any field means "server default".</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct pa_buffer_attr
    {
        public uint maxlength;
        public uint tlength;
        public uint prebuf;
        public uint minreq;
        public uint fragsize;
    }

    [LibraryImport(LibSimple, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr pa_simple_new(
        string? server, string name, int dir, string? dev, string streamName,
        in pa_sample_spec ss, IntPtr channelMap, IntPtr bufferAttr, out int error);

    [LibraryImport(LibSimple, EntryPoint = "pa_simple_new", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr pa_simple_new_attr(
        string? server, string name, int dir, string? dev, string streamName,
        in pa_sample_spec ss, IntPtr channelMap, in pa_buffer_attr attr, out int error);

    [LibraryImport(LibSimple)]
    public static partial int pa_simple_read(IntPtr s, IntPtr data, nuint bytes, out int error);

    [LibraryImport(LibSimple)]
    public static partial int pa_simple_write(IntPtr s, IntPtr data, nuint bytes, out int error);

    [LibraryImport(LibSimple)]
    public static partial int pa_simple_drain(IntPtr s, out int error);

    [LibraryImport(LibSimple)]
    public static partial void pa_simple_free(IntPtr s);

    [LibraryImport(LibPulse)]
    private static partial IntPtr pa_strerror(int error);

    public static string ErrorText(int error)
    {
        try
        {
            return Marshal.PtrToStringUTF8(pa_strerror(error)) ?? $"pulse error {error}";
        }
        catch (DllNotFoundException)
        {
            return $"pulse error {error}";
        }
    }
}
