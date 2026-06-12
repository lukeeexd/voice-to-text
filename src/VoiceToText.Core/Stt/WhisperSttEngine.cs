using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceToText.Stt;

/// <summary>
/// whisper.cpp backend via Whisper.net. The native runtime is auto-selected:
/// because only the Vulkan and CPU runtime packages are referenced (no CUDA),
/// Whisper.net uses the AMD/Intel GPU through Vulkan when available and falls
/// back to CPU otherwise.
/// </summary>
public sealed class WhisperSttEngine : ISttEngine
{
    // ~1s of silence is enough to compile the GPU/Vulkan inference pipeline during warm-up.
    private const int WarmupSamples = 16_000;

    private readonly GgmlType _modelType;
    private readonly string _language;
    private WhisperFactory? _factory;

    public WhisperSttEngine(GgmlType modelType, string language = "auto")
    {
        _modelType = modelType;
        _language = string.IsNullOrWhiteSpace(language) ? "auto" : language;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_factory is not null)
            return;

        var modelPath = await ModelManager.EnsureModelAsync(_modelType, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _factory = WhisperFactory.FromPath(modelPath);

        await PrimeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The FIRST transcription after the model loads pays a one-time GPU/Vulkan pipeline
    /// compilation (~1.5–2s); every later one is ~0.3s. Run a throwaway inference on silence
    /// here so that cost lands during background warm-up, not on the user's first dictation.
    /// Best-effort: a failure must never block loading or dictation.
    /// </summary>
    private async Task PrimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var processor = _factory!.CreateBuilder().WithLanguage("en").Build();
            var silence = new float[WarmupSamples];
            await foreach (var _ in processor.ProcessAsync(silence, cancellationToken).ConfigureAwait(false))
            {
            }
        }
        catch
        {
            // Warm-up is best-effort.
        }
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0)
            return string.Empty;

        await LoadAsync(cancellationToken).ConfigureAwait(false);

        await using var processor = _factory!.CreateBuilder()
            .WithLanguage(_language)
            .Build();

        var sb = new System.Text.StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
