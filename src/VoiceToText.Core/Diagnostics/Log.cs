namespace VoiceToText.Diagnostics;

/// <summary>
/// A tiny, dependency-free rolling log writer. One clear unit: owns a file path, appends
/// timestamped lines, and rolls the file to "<name>.1" once it passes a size cap. Thread-safe
/// and never throws — logging must never break dictation. Records metadata only (never transcripts).
/// </summary>
public sealed class LogWriter
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly long _maxBytes;

    public LogWriter(string filePath, long maxBytes = 512 * 1024)
    {
        _filePath = filePath;
        _maxBytes = maxBytes;
    }

    public void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(_filePath) && new FileInfo(_filePath).Length > _maxBytes)
                {
                    var rolled = _filePath + ".1";
                    if (File.Exists(rolled)) File.Delete(rolled);
                    File.Move(_filePath, rolled);
                }

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null) line += Environment.NewLine + ex;
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging is best-effort; never propagate.
        }
    }
}

/// <summary>Static facade over a default <see cref="LogWriter"/> at %APPDATA%\VoiceToText\logs.</summary>
public static class Log
{
    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceToText", "logs");

    private static readonly LogWriter Writer = new(Path.Combine(LogFolder, "voicetotext.log"));

    public static void Info(string message) => Writer.Write("INFO", message, null);
    public static void Error(string message, Exception? ex = null) => Writer.Write("ERROR", message, ex);
}
