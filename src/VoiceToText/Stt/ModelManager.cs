using Whisper.net.Ggml;

namespace VoiceToText.Stt;

/// <summary>
/// Resolves and (on first run) downloads ggml Whisper models into
/// %APPDATA%\VoiceToText\models. Models are large, so we never bundle them.
/// </summary>
public static class ModelManager
{
    public static string ModelDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoiceToText", "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string GetModelPath(GgmlType type)
        => Path.Combine(ModelDirectory, $"ggml-{type}.bin");

    public static bool IsModelPresent(GgmlType type)
    {
        var path = GetModelPath(type);
        return File.Exists(path) && new FileInfo(path).Length > 1_000_000;
    }

    /// <summary>
    /// Ensure the model file exists, downloading it if necessary. Returns the path.
    /// Downloads to a temp file then moves into place so a partial download is never
    /// mistaken for a valid model.
    /// </summary>
    public static async Task<string> EnsureModelAsync(
        GgmlType type,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = GetModelPath(type);
        if (IsModelPresent(type))
            return path;

        progress?.Report($"Downloading {type} model…");
        var temp = path + ".download";

        // QuantizationType defaults to the first value (no quantization).
        await using (var modelStream = await WhisperGgmlDownloader.Default
                         .GetGgmlModelAsync(type, default, cancellationToken)
                         .ConfigureAwait(false))
        await using (var fileStream = File.Create(temp))
        {
            await modelStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);

        progress?.Report($"{type} model ready.");
        return path;
    }
}
