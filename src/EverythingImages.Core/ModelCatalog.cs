using System.Text.Json;
using System.Text.Json.Nodes;

namespace EverythingImages.Core;

public sealed record Catalog(IReadOnlyList<VisionModel> Models, string? OfflineReason);

/// <summary>
/// The Liquid AI catalogue from Hugging Face, plus downloads, into
/// Paths.SharedDataDir().
/// </summary>
public sealed class ModelCatalog
{
    List<VisionModel> _models = [];
    readonly Dictionary<string, CancellationTokenSource> _downloading = [];

    static string CacheFile => System.IO.Path.Combine(Paths.SharedDataDir(), "models", "catalog.json");

    public bool IsDownloading(string id) => _downloading.ContainsKey(id);

    static async Task<List<VisionModel>> FetchAsync()
    {
        var listing = (await Download.GetJsonAsync($"{VisionModels.HfBase}/api/models?author={VisionModels.HfAuthor}&search=VL&limit=200")).AsArray();
        var ids = VisionModels.GgufRepoIds(listing);
        var trees = await Task.WhenAll(ids.Select(id => Download.GetJsonAsync($"{VisionModels.HfBase}/api/models/{id}/tree/main")));
        var models = ids.Select((id, i) => VisionModels.BuildModel(id, trees[i].AsArray())).OfType<VisionModel>().ToList();
        if (models.Count == 0) throw new DownloadFailedException("Hugging Face listed no Liquid AI vision models in GGUF format");
        VisionModels.SortCatalog(models);
        return models;
    }

    static List<VisionModel>? LoadCache()
    {
        try { return JsonSerializer.Deserialize<List<VisionModel>>(File.ReadAllText(CacheFile)); }
        catch { return null; }
    }

    /// <summary>Without refresh, uses the saved list and only goes online when
    /// there is none; with it, asks Hugging Face, falling back to the saved list.</summary>
    public async Task<Catalog> ListAsync(bool refresh = false)
    {
        string? offline = null;
        if (!refresh && _models.Count == 0) _models = LoadCache() ?? [];
        if (refresh || _models.Count == 0)
        {
            try
            {
                _models = await FetchAsync();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CacheFile)!);
                await File.WriteAllTextAsync(CacheFile, JsonSerializer.Serialize(_models, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                if (_models.Count == 0) _models = LoadCache() ?? [];
                if (_models.Count == 0) throw;
                offline = e.Message;
            }
        }
        var data = Paths.SharedDataDir();
        foreach (var m in _models) VisionModels.RefreshStatus(m, data);
        return new Catalog(_models.AsReadOnly(), offline);
    }

    public async Task DownloadAsync(VisionModel m, IProgress<(long Done, long Total)> progress)
    {
        if (_downloading.ContainsKey(m.Id)) return;
        using var cts = new CancellationTokenSource();
        _downloading[m.Id] = cts;
        try
        {
            await Download.DownloadAllAsync(m.Files, VisionModels.ModelDir(Paths.SharedDataDir(), m.Id), progress, cts.Token, CopySource(m));
        }
        finally
        {
            _downloading.Remove(m.Id);
        }
    }

    /// <summary>For a portable copy: where this PC's usual EverythingImages folder keeps the model.</summary>
    static string? CopySource(VisionModel m) => Paths.PcSharedDataDir() is { } pc ? VisionModels.ModelDir(pc, m.Id) : null;

    /// <summary>A portable copy can copy this model from the PC instead of downloading it.</summary>
    public static bool CanCopyFromPc(VisionModel m) => Download.CanCopyAll(m.Files, CopySource(m));

    public void Cancel(string id)
    {
        if (_downloading.TryGetValue(id, out var cts)) cts.Cancel();
    }

    public void Delete(VisionModel m)
    {
        if (_downloading.ContainsKey(m.Id)) throw new DownloadFailedException("Pause the download before deleting the model.");
        var dir = VisionModels.ModelDir(Paths.SharedDataDir(), m.Id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>(language model, projector) paths of a fully downloaded model.</summary>
    public static (string Gguf, string Mmproj)? LocalFiles(VisionModel m)
    {
        VisionModels.RefreshStatus(m, Paths.SharedDataDir());
        if (!m.IsDownloaded) return null;
        return (System.IO.Path.Combine(m.LocalDir, m.Files[0].Name), System.IO.Path.Combine(m.LocalDir, m.Files[1].Name));
    }
}
