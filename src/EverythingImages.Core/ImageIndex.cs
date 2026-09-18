using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EverythingImages.Core;

/// <summary>
/// Records, an inverted token index for instant search, and persistence.
/// Search: every query word must match the
/// start of an indexed word (prefix, not substring), then filter and sort.
/// Not thread-safe: the app calls it from the UI thread; the scanner hands
/// records back to it there.
/// </summary>
public sealed partial class ImageIndex(string? file = null)
{
    readonly Dictionary<string, ImageRecord> _images = [];

    /// <summary>Lowercase word -> ids, plus the words kept sorted so a prefix
    /// query is a range lookup instead of a walk over the whole vocabulary.</summary>
    readonly Dictionary<string, HashSet<string>> _tokens = new(StringComparer.Ordinal);
    readonly SortedSet<string> _words = new(StringComparer.Ordinal);

    public List<string> Folders { get; } = [];
    public int Count => _images.Count;
    public IEnumerable<ImageRecord> Records => _images.Values;
    public ImageRecord? this[string id] => _images.GetValueOrDefault(id);

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    sealed record Saved(int Format, List<string> Folders, List<ImageRecord> Images);

    public void Load()
    {
        if (file is null || !File.Exists(file)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file), Json)!;
            Folders.Clear();
            Folders.AddRange(saved.Folders);
            _images.Clear();
            foreach (var r in saved.Images) _images[r.Id] = r;
            RebuildTokens();
        }
        catch (Exception)
        {
            // A damaged file must not take the app down: keep it, start empty.
            // Nor may setting it aside - the file could be locked or read-only,
            // and losing the index is not worth failing to start over.
            _images.Clear();
            _tokens.Clear();
            _words.Clear();
            try { File.Move(file, file + ".damaged", overwrite: true); } catch (Exception) { }
        }
    }

    /// <summary>Writes a temp file and renames it into place, so a crash
    /// mid-write never leaves a half-written index.</summary>
    public void Save()
    {
        if (file is null) return;
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Saved(1, Folders, [.. _images.Values]), Json));
        File.Move(tmp, file, overwrite: true);
    }

    public void Put(ImageRecord r)
    {
        _images[r.Id] = r;
        foreach (var t in TokensOf(r)) AddToken(t, r.Id);
    }

    void AddToken(string token, string id)
    {
        if (!_tokens.TryGetValue(token, out var ids))
        {
            _tokens[token] = ids = [];
            _words.Add(token);
        }
        ids.Add(id);
    }

    /// <summary>True when <paramref name="id"/> is indexed from a file of this
    /// size and modification time, so a rescan can skip it.</summary>
    public bool IsUnchanged(string id, long size, long mtimeSecs) =>
        mtimeSecs != 0 && _images.TryGetValue(id, out var r) && r.FileSize == size && r.MtimeSecs == mtimeSecs;

    public bool AddFolder(string folder)
    {
        if (Folders.Contains(folder)) return false;
        Folders.Add(folder);
        return true;
    }

    public bool RemoveFolder(string folder)
    {
        if (!Folders.Remove(folder)) return false;
        foreach (var id in _images.Values.Where(r => IsUnderFolder(r.Path, folder)).Select(r => r.Id).ToList())
            _images.Remove(id);
        RebuildTokens();
        return true;
    }

    /// <summary>Drops records under <paramref name="inFolders"/> whose file is gone.</summary>
    public int PruneMissing(IReadOnlyList<string> inFolders)
    {
        var stale = _images.Values
            .Where(r => inFolders.Any(f => IsUnderFolder(r.Path, f)) && !File.Exists(r.Path))
            .Select(r => r.Id).ToList();
        foreach (var id in stale) _images.Remove(id);
        if (stale.Count > 0) RebuildTokens();
        return stale.Count;
    }

    /// <summary>Drops every image but keeps the folders.</summary>
    public int Clear()
    {
        var n = _images.Count;
        _images.Clear();
        _tokens.Clear();
        _words.Clear();
        return n;
    }

    public bool SetAiCaption(string id, AiCaption caption)
    {
        if (!_images.TryGetValue(id, out var r)) return false;
        r.AiCaption = caption;
        foreach (var w in WordsOf(caption.Text)) AddToken(w, id);
        return true;
    }

    public int ClearAiCaptions()
    {
        var n = 0;
        foreach (var r in _images.Values.Where(r => r.AiCaption != null))
        {
            r.AiCaption = null;
            n++;
        }
        if (n > 0) RebuildTokens();
        return n;
    }

    /// <summary>Images without an AI description, in <paramref name="screenOrder"/>
    /// (the ids as the grid shows them) so Describe all starts at the first image
    /// on screen; images the grid doesn't show follow, by path.</summary>
    public List<ImageRecord> NeedingAiCaption(IEnumerable<string>? screenOrder = null)
    {
        var pending = _images.Values.Where(r => r.AiCaption == null).OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
        if (screenOrder is null) return pending;
        var rank = new Dictionary<string, int>();
        foreach (var id in screenOrder) rank.TryAdd(id, rank.Count);
        return [.. pending.OrderBy(r => rank.TryGetValue(r.Id, out var i) ? i : int.MaxValue)]; // stable: the rest stay by path
    }

    public int AiCaptionCount => _images.Values.Count(r => r.AiCaption != null);

    public SearchResult Search(SearchFilter f, int limit = 5000)
    {
        var sw = Stopwatch.StartNew();
        IEnumerable<ImageRecord> candidates;
        var words = f.Query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            candidates = _images.Values;
        }
        else
        {
            HashSet<string>? ids = null;
            foreach (var w in words)
            {
                var hits = new HashSet<string>();
                // Every word that starts with w sorts between w and w + U+FFFF.
                foreach (var key in _words.GetViewBetween(w, w + char.MaxValue))
                    hits.UnionWith(_tokens[key]);
                ids = ids == null ? hits : [.. ids.Intersect(hits)];
                if (ids.Count == 0) break;
            }
            candidates = ids!.Select(id => _images.GetValueOrDefault(id)).OfType<ImageRecord>();
        }

        var list = candidates.Where(r => PassesQuick(r, f.Quick)).ToList();
        Comparison<ImageRecord> cmp = f.SortBy switch
        {
            SortBy.Size => (a, b) => a.FileSize.CompareTo(b.FileSize),
            SortBy.Date => (a, b) => a.MtimeSecs.CompareTo(b.MtimeSecs),
            SortBy.Dimensions => (a, b) => ((long)a.Width * a.Height).CompareTo((long)b.Width * b.Height),
            _ => (a, b) => string.Compare(a.Filename, b.Filename, StringComparison.OrdinalIgnoreCase),
        };
        list.Sort(f.Descending ? (a, b) => cmp(b, a) : cmp);
        var total = list.Count;
        if (list.Count > limit) list = list[..limit];
        return new SearchResult(list, total, sw.Elapsed.TotalMilliseconds);
    }

    static bool PassesQuick(ImageRecord r, QuickFilter q) => q switch
    {
        QuickFilter.Ocr => !string.IsNullOrWhiteSpace(r.OcrText),
        QuickFilter.Landscape => r.Width > r.Height,
        QuickFilter.Portrait => r.Height > r.Width,
        QuickFilter.Png => r.Extension == "png",
        QuickFilter.Jpg => r.Extension is "jpg" or "jpeg",
        QuickFilter.Webp => r.Extension == "webp",
        _ => true,
    };

    void RebuildTokens()
    {
        _tokens.Clear();
        _words.Clear();
        foreach (var r in _images.Values)
            foreach (var t in TokensOf(r)) AddToken(t, r.Id);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    /// <summary>Words of at least two letters/digits, lowercased.</summary>
    public static IEnumerable<string> WordsOf(string text) =>
        NonWord().Split(text.ToLowerInvariant()).Where(w => w.Length >= 2);

    /// <summary>Every searchable token of a record.</summary>
    public static HashSet<string> TokensOf(ImageRecord r)
    {
        var t = new HashSet<string>(WordsOf(System.IO.Path.GetFileNameWithoutExtension(r.Filename)))
        {
            r.Extension.ToLowerInvariant(),
            r.PrimaryColor.ToLowerInvariant(),
        };
        t.UnionWith(WordsOf(r.OcrText));
        foreach (var c in r.DominantColors) t.Add(c.Name.ToLowerInvariant());
        if (r.AiCaption != null) t.UnionWith(WordsOf(r.AiCaption.Text));
        return t;
    }

    /// <summary>True when path is the folder itself or inside it; a plain
    /// StartsWith would put C:\Photos2\a.jpg under C:\Photos.</summary>
    public static bool IsUnderFolder(string path, string folder)
    {
        var f = folder.TrimEnd('\\', '/');
        if (!path.StartsWith(f, StringComparison.OrdinalIgnoreCase)) return false;
        return path.Length == f.Length || path[f.Length] is '\\' or '/';
    }
}
