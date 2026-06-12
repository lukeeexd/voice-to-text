using System.Text.Json;

namespace VoiceToText.History;

/// <summary>
/// Loads/saves the opt-in dictation history (%APPDATA%\VoiceToText\history.json) and records
/// entries. Wraps the pure <see cref="HistoryStore"/>; Record/Clear run on the UI thread.
/// History is non-critical — failures never throw into the dictation path.
/// </summary>
public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public HistoryStore Data { get; private set; } = new();

    /// <summary>Newest-first entries for display.</summary>
    public IReadOnlyList<HistoryEntry> Entries => Data.Entries;

    public HistoryService() : this(Path.Combine(AppPaths.DataDir, "history.json")) { }

    /// <summary>Load/save against a specific file (self-tests use a temp path).</summary>
    public HistoryService(string path)
    {
        _path = path;
        Load();
    }

    private void Load()
    {
        try
        {
            var path = _path;
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<HistoryStore>(File.ReadAllText(path), JsonOptions);
                if (loaded is not null) Data = loaded;
            }
        }
        catch
        {
            Data = new HistoryStore(); // corrupt/unreadable — start fresh
        }
    }

    private void Save()
    {
        try
        {
            var path = _path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    /// <summary>Record one dictation and persist. Called only when history is enabled.</summary>
    public void Record(string text, int words, string? app, double? transcribeSeconds = null, string? model = null)
    {
        Data.Add(new HistoryEntry
        {
            Time = DateTime.Now,
            App = string.IsNullOrWhiteSpace(app) ? "Unknown" : app!,
            Text = text,
            Words = words,
            TranscribeSeconds = transcribeSeconds,
            Model = model,
        });
        Save();
    }

    /// <summary>Erase all stored history.</summary>
    public void Clear()
    {
        Data.Clear();
        Save();
    }
}
