using Whisper.net.Ggml;

namespace VoiceToText.Stt;

/// <summary>One selectable speech model: a friendly label paired with its ggml type.
/// Pure data — the speed↔accuracy ladder shown in Settings.</summary>
public sealed record ModelOption(string Label, GgmlType Type)
{
    /// <summary>The offered models, fastest → most accurate.</summary>
    public static IReadOnlyList<ModelOption> All { get; } = new[]
    {
        new ModelOption("Small (English) — fastest", GgmlType.SmallEn),
        new ModelOption("Medium (English) — faster", GgmlType.MediumEn),
        new ModelOption("Large v3 Turbo — recommended", GgmlType.LargeV3Turbo),
        new ModelOption("Large v3 — most accurate", GgmlType.LargeV3),
    };

    /// <summary>Matches AppSettings.ModelType's default (LargeV3Turbo).</summary>
    public static ModelOption Default { get; } = All[2];

    /// <summary>Short display name for a stored GgmlType name (e.g. "LargeV3Turbo" -> "Large v3 Turbo").</summary>
    public static string ShortLabel(string ggmlTypeName) => ggmlTypeName switch
    {
        "SmallEn" => "Small (En)",
        "MediumEn" => "Medium (En)",
        "LargeV3Turbo" => "Large v3 Turbo",
        "LargeV3" => "Large v3",
        _ => ggmlTypeName,
    };

    // Combos display this via GetItemText/ToString.
    public override string ToString() => Label;
}
