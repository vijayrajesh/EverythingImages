using System.Text.Json;
using System.Text.Json.Serialization;

namespace EverythingImages.Core;

/// <summary>User options, saved as JSON in the app's own data folder.</summary>
public sealed class Settings
{
    public const string DefaultPrompt = "Describe this image in one sentence.";
    public const int MaxPromptChars = 1000;

    [JsonPropertyName("describe_prompt")] public string DescribePrompt { get; set; } = DefaultPrompt;
    [JsonPropertyName("ocr_enabled")] public bool OcrEnabled { get; set; } = true;

    /// <summary>"gpu" | "cpu", or null for the default (the GPU when the PC has one).</summary>
    [JsonPropertyName("processor")] public string? Processor { get; set; }

    /// <summary>The graphics card that has run a model once: the first run on a
    /// card is slow while its driver prepares, so the app says so until then.</summary>
    [JsonPropertyName("gpu_prepared")] public string? GpuPrepared { get; set; }

    /// <summary>Repo id of the model in use, or null for the first downloaded.</summary>
    [JsonPropertyName("model_id")] public string? ModelId { get; set; }

    static string FilePath => System.IO.Path.Combine(Paths.AppDataDir(), "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Trimmed prompt, or throws with why it can't be used.</summary>
    public static string ValidatePrompt(string prompt)
    {
        var t = prompt.Trim();
        if (t.Length == 0) throw new ArgumentException("The prompt can't be empty.");
        if (t.Length > MaxPromptChars) throw new ArgumentException($"The prompt is too long (max {MaxPromptChars} characters).");
        return t;
    }
}
