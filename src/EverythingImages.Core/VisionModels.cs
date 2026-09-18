using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EverythingImages.Core;

/// <summary>One downloadable file. JSON names are those of the cached
/// catalogue (models\catalog.json).</summary>
public sealed record ModelFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("url")] string Url);

public sealed class VisionModel
{
    [JsonPropertyName("id")] public required string Id { get; init; }            // LiquidAI/LFM2.5-VL-450M-GGUF
    [JsonPropertyName("name")] public required string Name { get; init; }        // LFM2.5-VL 450M Extract
    [JsonPropertyName("family")] public required string Family { get; init; }    // LFM2.5-VL
    [JsonPropertyName("params")] public required string Params { get; init; }    // 450M
    [JsonPropertyName("variant")] public string? Variant { get; init; }          // Extract
    [JsonPropertyName("quant")] public required string Quant { get; init; }      // Q4_K_M
    [JsonPropertyName("files")] public required List<ModelFile> Files { get; init; } // model, projector
    [JsonPropertyName("card_url")] public required string CardUrl { get; init; }

    // On-disk state, filled by VisionModels.RefreshStatus.
    [JsonIgnore] public long DownloadedBytes { get; set; }
    [JsonIgnore] public bool IsDownloaded { get; set; }
    [JsonIgnore] public string LocalDir { get; set; } = "";
    [JsonIgnore] public long TotalBytes => Files.Sum(f => f.Size);
}

/// <summary>
/// Liquid AI vision-language models on Hugging Face (GGUF): which repos
/// count, which files to download,
/// catalogue order, on-disk state. No model is named in code.
/// </summary>
public static partial class VisionModels
{
    public const string HfBase = "https://huggingface.co";
    public const string HfAuthor = "LiquidAI";
    static readonly string[] ModelQuants = ["Q4_K_M", "Q4_0", "Q5_K_M", "Q8_0"];
    static readonly string[] MmprojQuants = ["Q8_0", "F16", "BF16"];

    public static List<string> GgufRepoIds(JsonArray listing) =>
        [.. listing.Select(r => r?["id"]?.GetValue<string>()).OfType<string>()
            .Where(id => id.StartsWith(HfAuthor + "/") && id.Contains("-VL-") && id.EndsWith("-GGUF"))];

    /// <summary>"LiquidAI/LFM2.5-VL-1.6B-Extract-GGUF" -> ("LFM2.5-VL", "1.6B", "Extract").</summary>
    public static (string Family, string Params, string? Variant)? ParseRepoName(string repoId)
    {
        var name = repoId.Split('/')[^1];
        if (!name.EndsWith("-GGUF")) return null;
        var baseName = name[..^5];
        var at = baseName.IndexOf("-VL-", StringComparison.Ordinal);
        if (at < 0) return null;
        var rest = baseName[(at + 4)..];
        var dash = rest.IndexOf('-');
        var p = dash < 0 ? rest : rest[..dash];
        if (p.Length == 0) return null;
        return (baseName[..at] + "-VL", p, dash < 0 ? null : rest[(dash + 1)..]);
    }

    /// <summary>A catalogue entry from a repo's file tree; null when it lacks
    /// a language model or a vision projector.</summary>
    public static VisionModel? BuildModel(string repoId, JsonArray tree)
    {
        if (ParseRepoName(repoId) is not var (family, prms, variant)) return null;
        var ggufs = tree.OfType<JsonObject>()
            .Where(e => (string?)e["type"] == "file" && ((string?)e["path"] ?? "").EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var mmprojs = ggufs.Where(e => ((string)e["path"]!).StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)).ToList();
        var models = ggufs.Except(mmprojs).ToList();
        var model = Pick(models, ModelQuants);
        var mmproj = Pick(mmprojs, MmprojQuants);
        if (model is null || mmproj is null) return null;
        return new VisionModel
        {
            Id = repoId,
            Name = variant is null ? $"{family} {prms}" : $"{family} {prms} {variant}",
            Family = family,
            Params = prms,
            Variant = variant,
            Quant = model.Value.Quant,
            Files = [ToFile(repoId, model.Value.Entry), ToFile(repoId, mmproj.Value.Entry)],
            CardUrl = $"{HfBase}/{repoId}",
        };
    }

    static long SizeOf(JsonObject e) => e["lfs"]?["size"] is { } lfs ? ToLong(lfs) : ToLong(e["size"]);

    /// <summary>A JSON number as long, whether it was parsed or built in code
    /// (a JsonNode holding an int refuses a direct cast to long).</summary>
    internal static long ToLong(JsonNode? n) =>
        n is null ? 0 : n.AsValue().TryGetValue(out long l) ? l : n.AsValue().TryGetValue(out int i) ? i : 0;

    static (JsonObject Entry, string Quant)? Pick(List<JsonObject> candidates, string[] prefs)
    {
        if (candidates.Count == 0) return null;
        foreach (var q in prefs)
        {
            var hit = candidates.FirstOrDefault(e => ((string)e["path"]!).EndsWith($"-{q}.gguf", StringComparison.OrdinalIgnoreCase));
            if (hit != null) return (hit, q);
        }
        var smallest = candidates.MinBy(SizeOf)!;
        var path = (string)smallest["path"]!;
        return (smallest, path[..^5].Split('-')[^1]);
    }

    static ModelFile ToFile(string repoId, JsonObject e) =>
        new((string)e["path"]!, SizeOf(e), (string?)e["lfs"]?["oid"] ?? "", $"{HfBase}/{repoId}/resolve/main/{e["path"]}");

    [GeneratedRegex(@"-VL$")]
    private static partial Regex VlSuffix();

    static double FamilyVersion(string family) =>
        double.TryParse(VlSuffix().Replace(family.Replace("LFM", ""), ""), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    static double ParamBillions(string p)
    {
        if (p.Length == 0 || !double.TryParse(p[..^1], System.Globalization.CultureInfo.InvariantCulture, out var n)) return double.MaxValue;
        return char.ToUpperInvariant(p[^1]) == 'M' ? n / 1000 : n;
    }

    /// <summary>Newest generation first, then smallest, general model before variants.</summary>
    public static void SortCatalog(List<VisionModel> models) => models.Sort((a, b) =>
    {
        var c = FamilyVersion(b.Family).CompareTo(FamilyVersion(a.Family));
        if (c != 0) return c;
        c = ParamBillions(a.Params).CompareTo(ParamBillions(b.Params));
        if (c != 0) return c;
        c = (a.Variant != null).CompareTo(b.Variant != null);
        return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
    });

    /// <summary>`&lt;shared data&gt;\models\&lt;repo name&gt;`.</summary>
    public static string ModelDir(string dataDir, string repoId) => System.IO.Path.Combine(dataDir, "models", repoId.Split('/')[^1]);

    public static string PartPath(string finalPath) => finalPath + ".part";

    /// <summary>A file counts only at its exact size; a .part counts towards progress.</summary>
    public static void RefreshStatus(VisionModel m, string dataDir)
    {
        var dir = ModelDir(dataDir, m.Id);
        long done = 0;
        var complete = true;
        foreach (var f in m.Files)
        {
            var path = System.IO.Path.Combine(dir, f.Name);
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length == f.Size) { done += f.Size; continue; }
            complete = false;
            var part = new FileInfo(PartPath(path));
            if (part.Exists) done += Math.Min(part.Length, f.Size);
        }
        m.DownloadedBytes = done;
        m.IsDownloaded = complete;
        m.LocalDir = dir;
    }
}
