using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace EverythingImages.Core;

public sealed record ScanSummary(int Total, int Updated, int Pruned, double Seconds, bool Stopped);

/// <summary>
/// Finds images under the folders, skips unchanged ones, and reads the rest
/// with Windows' own decoders (WIC): size, colours from a 48 px decode, and
/// text via Windows.Media.Ocr, all in one pass per image.
/// </summary>
public sealed class Scanner
{
    public static readonly HashSet<string> Extensions = ["png", "jpg", "jpeg", "webp", "bmp", "gif", "ico"];
    const int Parallelism = 4;

    volatile bool _cancel;
    public bool Running { get; private set; }

    public void Stop() => _cancel = true;

    /// <summary>Stable record id for a path.</summary>
    public static string RecordId(string path) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..16];

    /// <summary>Scans; call from the thread that owns <paramref name="index"/>
    /// (records are added back on the caller's context between batches).</summary>
    public async Task<ScanSummary> ScanAsync(ImageIndex index, bool ocr, IProgress<(int Done, int Total, string Current)> progress)
    {
        if (Running) throw new InvalidOperationException("A scan is already running.");
        Running = true;
        _cancel = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var updated = 0;
        try
        {
            progress.Report((0, 0, "Looking for images..."));
            var folders = index.Folders.ToList();
            var paths = await Task.Run(() => FindImages(folders));

            // What the index already has, so workers never touch the index itself.
            var known = index.Records.ToDictionary(r => r.Id, r => (r.FileSize, r.MtimeSecs));
            var engines = new ConcurrentBag<OcrEngine>();

            var done = 0;
            for (var i = 0; i < paths.Count && !_cancel; i += Parallelism * 2)
            {
                var batch = paths.Skip(i).Take(Parallelism * 2).ToList();
                var records = await Task.WhenAll(batch.Select(p => Task.Run(() => ReadIfChangedAsync(p, known, ocr, engines))));
                foreach (var r in records.OfType<ImageRecord>())
                {
                    index.Put(r);
                    updated++;
                }
                done += batch.Count;
                progress.Report((done, paths.Count, batch[^1]));
            }

            // Only a complete pass may prune: a stopped one hasn't seen everything.
            var pruned = _cancel ? 0 : index.PruneMissing(index.Folders);
            index.Save();
            return new ScanSummary(index.Count, updated, pruned, sw.Elapsed.TotalSeconds, _cancel);
        }
        finally
        {
            Running = false;
        }
    }

    List<string> FindImages(List<string> folders)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = System.IO.FileAttributes.System };
        var found = new List<string>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                if (_cancel) return found; // Stop means stop, not "finish the other folders"
                if (Extensions.Contains(System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant())) found.Add(path);
            }
        }
        return found;
    }

    static async Task<ImageRecord?> ReadIfChangedAsync(string path, Dictionary<string, (long Size, long Mtime)> known, bool ocr, ConcurrentBag<OcrEngine> engines)
    {
        try
        {
            var fi = new FileInfo(path);
            var mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds();
            var id = RecordId(path);
            if (known.TryGetValue(id, out var k) && k.Mtime != 0 && k.Size == fi.Length && k.Mtime == mtime) return null;

            var file = await StorageFile.GetFileFromPathAsync(fi.FullName);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            int w = (int)decoder.OrientedPixelWidth, h = (int)decoder.OrientedPixelHeight;

            // Colours from a decode at most 48 px on the long side.
            var scale = Math.Min(1.0, 48.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var small = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, small,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var (colors, primary) = Colors.Dominant(pixels.DetachPixelData());

            var text = ocr ? await RecognizeAsync(decoder, engines) : "";
            return new ImageRecord
            {
                Id = id,
                Path = fi.FullName,
                Filename = fi.Name,
                Extension = fi.Extension.TrimStart('.').ToLowerInvariant(),
                Width = w,
                Height = h,
                FileSize = fi.Length,
                MtimeSecs = mtime,
                DominantColors = colors,
                PrimaryColor = primary,
                OcrText = text.Trim(),
            };
        }
        catch (Exception)
        {
            return null; // unreadable file: skip it, the next scan tries again
        }
    }

    /// <summary>Text in the image via Windows.Media.Ocr; "" when there is none
    /// or no OCR language is installed. Images larger than the engine accepts
    /// are scaled down rather than losing their text.</summary>
    static async Task<string> RecognizeAsync(BitmapDecoder decoder, ConcurrentBag<OcrEngine> engines)
    {
        if (!engines.TryTake(out var engine)) engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return "";
        try
        {
            var max = OcrEngine.MaxImageDimension;
            var transform = new BitmapTransform();
            var longSide = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            if (longSide > max)
            {
                var s = (double)max / longSide;
                transform.ScaledWidth = (uint)(decoder.PixelWidth * s);
                transform.ScaledHeight = (uint)(decoder.PixelHeight * s);
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
            }
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            var result = await engine.RecognizeAsync(bitmap);
            return result.Text;
        }
        catch (Exception)
        {
            return "";
        }
        finally
        {
            engines.Add(engine);
        }
    }
}
