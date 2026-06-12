namespace VoiceToText.Stats;

/// <summary>
/// Shared formatting for usage stats so the dashboard and any text summary render
/// durations identically. Pure; no state.
/// </summary>
public static class StatsFormat
{
    /// <summary>Human duration: "&lt;1 min" / "N min" / "N.N hrs".</summary>
    public static string Duration(double minutes)
    {
        if (minutes < 1) return "<1 min";
        if (minutes < 90) return $"{minutes:N0} min";
        return $"{minutes / 60.0:N1} hrs";
    }
}
