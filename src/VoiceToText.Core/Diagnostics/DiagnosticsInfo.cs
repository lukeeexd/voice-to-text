using System.Reflection;
using System.Runtime.InteropServices;
using VoiceToText.Settings;
using VoiceToText.Stt;

namespace VoiceToText.Diagnostics;

/// <summary>
/// Pure diagnostics view-model: turns injected facts into labelled rows + a clipboard string,
/// with an acceleration flag for green/amber rendering. No I/O — fully testable via --abouttest.
/// Use <see cref="Current"/> for the live values.
/// </summary>
public sealed class DiagnosticsInfo
{
    public bool IsGpuAccelerated { get; }
    public IReadOnlyList<(string Label, string Value)> Rows { get; }

    public DiagnosticsInfo(
        string version, string runtime, string gpu, string model,
        string modelPath, long modelSizeBytes, string os, string framework, string arch)
    {
        IsGpuAccelerated =
            !runtime.Equals("Cpu", StringComparison.OrdinalIgnoreCase) &&
            !runtime.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
            runtime.Length > 0;

        var acceleration = IsGpuAccelerated
            ? $"{runtime} (GPU)"
            : runtime.Equals("Cpu", StringComparison.OrdinalIgnoreCase)
                ? "CPU (no GPU acceleration)"
                : runtime.Length > 0 ? runtime : "Unknown";

        var fileValue = modelSizeBytes > 0 ? $"{modelPath} · {FormatSize(modelSizeBytes)}" : modelPath;

        Rows = new List<(string, string)>
        {
            ("Version", version),
            ("Acceleration", acceleration),
            ("GPU", gpu),
            ("Speech model", model),
            ("Model file", fileValue),
            ("System", $"{os} · {framework} · {arch}"),
        };
    }

    public string ToClipboardText()
        => "Voice to Text diagnostics" + Environment.NewLine +
           string.Join(Environment.NewLine, Rows.Select(r => $"{r.Label}: {r.Value}"));

    private static string FormatSize(long bytes)
    {
        double gb = bytes / 1024.0 / 1024.0 / 1024.0;
        if (gb >= 1.0) return $"{gb:0.0} GB";
        double mb = bytes / 1024.0 / 1024.0;
        return $"{mb:0} MB";
    }

    /// <summary>Platform hook returning the primary GPU's display name. Set at startup
    /// by heads that can probe it (Windows: EnumDisplayDevices); default "Unknown".</summary>
    public static Func<string>? GpuNameProvider { get; set; }

    /// <summary>Gather the live values for the running app.</summary>
    public static DiagnosticsInfo Current(AppSettings settings)
    {
        // Entry assembly: the HEAD exe carries the shared Version.props version.
        var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetName().Version?.ToString(3) ?? "?";
        var runtime = RuntimeProbe.LoadedRuntime();
        var gpu = GpuNameProvider?.Invoke() ?? "Unknown";
        var modelType = settings.ModelType;
        var model = ModelOption.All.FirstOrDefault(m => m.Type == modelType)?.ToString() ?? modelType.ToString();
        var realPath = ModelManager.GetModelPath(modelType);
        long size = 0;
        try { if (File.Exists(realPath)) size = new FileInfo(realPath).Length; } catch { }
        var modelPath = CollapseAppData(realPath); // hide the Windows username when copied to clipboard
        return new DiagnosticsInfo(
            version, runtime, gpu, model, modelPath, size,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSArchitecture.ToString());
    }

    private static string CollapseAppData(string path)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return appData.Length > 0 && path.StartsWith(appData, StringComparison.OrdinalIgnoreCase)
            ? "%APPDATA%" + path[appData.Length..]
            : path;
    }
}
