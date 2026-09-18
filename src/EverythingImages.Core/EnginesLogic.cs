using System.Text.RegularExpressions;

namespace EverythingImages.Core;

/// <summary>A graphics card the AI engine can run models on.</summary>
public sealed record GpuDevice(string Id, string Name, int TotalMb, int FreeMb);

/// <summary>Where a description run is, as llama.cpp reports it.</summary>
public enum Stage { Start, Loading, Reading, Writing }

/// <summary>Pure logic for running the models: reading llama.cpp's output and log.</summary>
public static partial class EnginesLogic
{
    public const string MtmdExe = "llama-mtmd-cli.exe";

    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex Ansi();

    [GeneratedRegex(@"^\s*(Vulkan\d+|CUDA\d+):\s*(.+?)\s*\((\d+) MiB,\s*(\d+) MiB free\)", RegexOptions.Multiline)]
    private static partial Regex DeviceLine();

    /// <summary>The GPUs in `llama-mtmd-cli --list-devices` output, e.g.
    /// "Vulkan1: NVIDIA GeForce RTX 3050 Laptop GPU (3962 MiB, 3367 MiB free)".</summary>
    public static IReadOnlyList<GpuDevice> ParseDevices(string output) =>
        [.. DeviceLine().Matches(Ansi().Replace(output, ""))
            .Select(m => new GpuDevice(m.Groups[1].Value, m.Groups[2].Value, int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)))];

    // The name may contain brackets itself ("AMD Radeon(TM) Graphics"), so it ends
    // where the optional PCI address and the free-memory figure begin.
    [GeneratedRegex(@"using device \S+ \((.+?)\)(?: \([0-9a-fA-F]{4}:[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-9a-fA-F]\))? - \d+ MiB free")]
    private static partial Regex UsedDevice();

    /// <summary>"using device Vulkan1 (NVIDIA GeForce RTX 3050 Laptop GPU) (0000:01:00.0) - 3365 MiB free" -> the card's name.</summary>
    public static string? ParseUsedDevice(string log) =>
        UsedDevice().Match(Ansi().Replace(log, "")) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The stage a llama-mtmd-cli log line (-lv 4) announces, if any.</summary>
    public static Stage? StageFromLogLine(string line) =>
        line.Contains("load_tensors: loading model") ? Stage.Loading
        : line.Contains("encoding mtmd batch") ? Stage.Reading
        : line.Contains("image decoded") ? Stage.Writing
        : null;

    [GeneratedRegex(@"offloaded (\d+)/(\d+) layers to GPU")]
    private static partial Regex Offload();

    /// <summary>"offloaded 17/17 layers to GPU" -> (17, 17): proof the GPU was used.</summary>
    public static (int Done, int Total)? ParseOffload(string log)
    {
        var m = Offload().Matches(log).LastOrDefault();
        return m is null ? null : (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }
}
