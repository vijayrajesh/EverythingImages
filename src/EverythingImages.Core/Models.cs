using System.Text.Json.Serialization;

namespace EverythingImages.Core;

public sealed record HexColor(string Hex, int R, int G, int B, double Pct, string Name);

/// <summary>A caption written by a vision model ("Describe with AI").</summary>
public sealed record AiCaption(string Text, string Model);

public sealed class ImageRecord
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string Filename { get; init; }
    public required string Extension { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileSize { get; init; }

    /// <summary>Modification time in whole seconds since the epoch; a rescan
    /// skips a file whose size and mtime still match.</summary>
    public long MtimeSecs { get; init; }

    public List<HexColor> DominantColors { get; init; } = [];
    public string PrimaryColor { get; init; } = "Gray";
    public string OcrText { get; init; } = "";
    public AiCaption? AiCaption { get; set; }

    [JsonIgnore] public DateTime Modified => DateTimeOffset.FromUnixTimeSeconds(MtimeSecs).LocalDateTime;
}

public enum QuickFilter { All, Ocr, Landscape, Portrait, Png, Jpg, Webp }

public enum SortBy { Name, Date, Size, Dimensions }

public sealed record SearchFilter(
    string Query = "",
    QuickFilter Quick = QuickFilter.All,
    SortBy SortBy = SortBy.Name,
    bool Descending = false);

public sealed record SearchResult(IReadOnlyList<ImageRecord> Images, int Total, double SearchMs);
