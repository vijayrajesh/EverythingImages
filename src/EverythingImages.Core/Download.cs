using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace EverythingImages.Core;

public sealed class DownloadFailedException(string message) : Exception(message);

/// <summary>
/// Resumable, SHA-256-verified downloads: stream to &lt;name&gt;.part, resume
/// with an HTTP Range request
/// (re-hashing what is on disk so the file is still verified end to end),
/// check the hash, then rename into place.
/// </summary>
public static class Download
{
    public static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) })
        {
            Timeout = Timeout.InfiniteTimeSpan, // per-read stall detection below instead
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EverythingImages/0.1"); // GitHub's API requires one
        return c;
    }

    public static async Task<JsonNode> GetJsonAsync(string url, CancellationToken ct = default)
    {
        var host = new Uri(url).Host;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var res = await Http.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode) throw new DownloadFailedException($"{host} answered {(int)res.StatusCode} for {url}");
            return JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token))!;
        }
        catch (DownloadFailedException) { throw; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new DownloadFailedException($"could not reach {host} ({e.Message})");
        }
    }

    /// <summary>Downloads every file into <paramref name="dir"/>, skipping
    /// complete ones. <paramref name="progress"/> gets (done, total) bytes.
    /// Cancelling keeps the .part files for next time. A file of the right
    /// size in <paramref name="copyFrom"/> is copied instead (and checked
    /// against the same SHA-256), falling back to downloading.</summary>
    public static async Task DownloadAllAsync(IReadOnlyList<ModelFile> files, string dir, IProgress<(long Done, long Total)> progress,
        CancellationToken ct, string? copyFrom = null)
    {
        Directory.CreateDirectory(dir);
        var total = files.Sum(f => f.Size);
        long finished = 0;
        foreach (var f in files)
        {
            var target = System.IO.Path.Combine(dir, f.Name);
            var fi = new FileInfo(target);
            if (!(fi.Exists && fi.Length == f.Size))
            {
                var before = finished;
                var source = copyFrom is null ? null : new FileInfo(System.IO.Path.Combine(copyFrom, f.Name));
                if (!(source is { Exists: true } && source.Length == f.Size
                      && await CopyVerifiedAsync(source.FullName, f, target, bytes => progress.Report((before + bytes, total)), ct)))
                    await DownloadFileAsync(f, target, bytes => progress.Report((before + bytes, total)), ct);
            }
            finished += f.Size;
            progress.Report((finished, total));
        }
    }

    public static bool CanCopyAll(IReadOnlyList<ModelFile> files, string? copyFrom) =>
        copyFrom != null && files.All(f => new FileInfo(System.IO.Path.Combine(copyFrom, f.Name)) is { Exists: true } fi && fi.Length == f.Size);

    /// <summary>Copies through .part with the same checksum as a download;
    /// false (nothing kept) when the source doesn't match.</summary>
    static async Task<bool> CopyVerifiedAsync(string source, ModelFile f, string target, Action<long> onBytes, CancellationToken ct)
    {
        var part = VisionModels.PartPath(target);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
        await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            long have = 0, reported = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                hash.AppendData(buffer, 0, n);
                have += n;
                if (have - reported >= 16 << 20) onBytes(reported = have); // disk is fast: every 16 MB
            }
            onBytes(have);
        }
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (f.Sha256.Length > 0 && !actual.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            return false;
        }
        File.Move(part, target, overwrite: true);
        return true;
    }

    static async Task DownloadFileAsync(ModelFile f, string target, Action<long> onBytes, CancellationToken ct)
    {
        var part = VisionModels.PartPath(target);
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long have = 0;

        // Prime the hash with what is already on disk.
        if (File.Exists(part))
        {
            have = new FileInfo(part).Length;
            if (have > f.Size)
            {
                File.Delete(part);
                have = 0;
            }
            else
            {
                await using var existing = File.OpenRead(part);
                var buf = new byte[1 << 20];
                int n;
                while ((n = await existing.ReadAsync(buf, ct)) > 0) hash.AppendData(buf, 0, n);
            }
        }

        if (have < f.Size)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, f.Url);
            if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            FileMode mode;
            if (have > 0 && res.StatusCode == HttpStatusCode.PartialContent)
            {
                mode = FileMode.Append;
            }
            else if (res.StatusCode == HttpStatusCode.OK)
            {
                // Fresh start, or the server ignored the range: start over.
                hash.Dispose();
                hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                have = 0;
                mode = FileMode.Create;
            }
            else
            {
                throw new DownloadFailedException($"{new Uri(f.Url).Host} answered {(int)res.StatusCode} for {f.Name}");
            }
            onBytes(have);

            await using var input = await res.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(part, mode, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            while (true)
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(TimeSpan.FromSeconds(60)); // per read, not per download
                int n;
                try
                {
                    n = await input.ReadAsync(buffer, stall.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new DownloadFailedException($"{f.Name} stalled. Press Download again to resume.");
                }
                if (n == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                hash.AppendData(buffer, 0, n);
                have += n;
                onBytes(have);
            }
        }

        if (have != f.Size)
            throw new DownloadFailedException($"{f.Name} stopped at {have} of {f.Size} bytes. Press Download again to resume.");
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        hash.Dispose();
        if (f.Sha256.Length > 0 && !actual.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new DownloadFailedException($"{f.Name} failed its checksum and was deleted. Please download it again.");
        }
        File.Move(part, target, overwrite: true);
    }
}
