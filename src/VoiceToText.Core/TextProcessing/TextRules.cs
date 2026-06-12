using System.Text.RegularExpressions;

namespace VoiceToText.TextProcessing;

/// <summary>
/// Pure post-transcription transform: spoken formatting commands first, then custom
/// replacements (case-insensitive, whole-word, verbatim), then trim. No I/O, no UI.
/// </summary>
public static class TextRules
{
    // Surrounding absorption uses [ \t]* (NOT \s*) so a command never eats an adjacent
    // line break produced by another command.
    private static readonly Regex ParagraphCmd =
        new(@"[ \t]*\bnew\s+paragraph\b[.,!?;:]*[ \t]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineCmd =
        new(@"[ \t]*\b(?:new\s+line|newline)\b[.,!?;:]*[ \t]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Apply(string text, IReadOnlyList<ReplacementRule>? rules, bool spokenCommands)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var s = text;

        if (spokenCommands)
        {
            s = ParagraphCmd.Replace(s, "\n\n"); // paragraph before line
            s = LineCmd.Replace(s, "\n");
        }

        if (rules is not null)
        {
            foreach (var rule in rules)
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.Find)) continue;
                // Whole-word via lookarounds (works even when Find starts/ends with a symbol).
                var pattern = @"(?<!\w)" + Regex.Escape(rule.Find) + @"(?!\w)";
                var replacement = rule.Replace ?? "";
                // MatchEvaluator => replacement is literal (no $-group substitution).
                s = Regex.Replace(s, pattern, _ => replacement, RegexOptions.IgnoreCase);
            }
        }

        return s.Trim();
    }
}
