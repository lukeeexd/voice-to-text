namespace VoiceToText.History;

/// <summary>
/// Pure, serializable history model: a capped, newest-first list of dictations. All mutation
/// goes through <see cref="Add"/>/<see cref="Clear"/>; no I/O, fully unit-testable via --historytest.
/// </summary>
public sealed class HistoryStore
{
    public const int MaxEntries = 50;

    /// <summary>Most-recent first.</summary>
    public List<HistoryEntry> Entries { get; set; } = new();

    /// <summary>Prepend an entry and trim to the newest <see cref="MaxEntries"/>.</summary>
    public void Add(HistoryEntry entry)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
    }

    /// <summary>Remove all entries.</summary>
    public void Clear() => Entries.Clear();
}
