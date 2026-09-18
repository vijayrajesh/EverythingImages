using System.Diagnostics;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace EverythingImages.Core;

/// <summary>
/// Which graphics cards the AI engine can use. The engine ships beside the exe
/// (llama.cpp's Vulkan build): it runs the models on any NVIDIA, AMD or Intel
/// card through the driver already on the PC, so there is nothing to download.
/// </summary>
public sealed class GpuDetector
{
    public static bool EngineReady => File.Exists(System.IO.Path.Combine(Paths.EngineDir(), EnginesLogic.MtmdExe));

    public IReadOnlyList<GpuDevice> Devices { get; private set; } = [];
    public bool HasGpu => Devices.Count > 0;
    public bool Detected => _detecting is { IsCompleted: true };

    Task? _detecting;

    /// <summary>Asks the engine once; callers arriving meanwhile await the same
    /// run instead of seeing "no GPU" before it has answered.</summary>
    public Task DetectAsync() => _detecting ??= Task.Run(async () =>
    {
        try
        {
            if (!EngineReady) return;
            var psi = new ProcessStartInfo(System.IO.Path.Combine(Paths.EngineDir(), EnginesLogic.MtmdExe))
            {
                WorkingDirectory = Paths.EngineDir(), // ggml loads its backend DLLs from here
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("--list-devices");
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await p.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                p.Kill(entireProcessTree: true);
                return;
            }
            Devices = EnginesLogic.ParseDevices(await stdout + "\n" + await stderr);
        }
        catch
        {
            Devices = [];
        }
    });
}

public sealed record DescribeResult(string Caption, double Seconds, bool GpuChosen, string Model, (int Done, int Total)? GpuLayers, string? Device);

/// <summary>Describes one image with llama.cpp's llama-mtmd-cli (never named in the UI).</summary>
public static class Describer
{
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public static async Task<DescribeResult> DescribeAsync(
        bool gpu, string modelName, string gguf, string mmproj, string imagePath, string prompt, Action<Stage, string> onStage)
    {
        var bin = Paths.EngineDir();
        var exe = System.IO.Path.Combine(bin, EnginesLogic.MtmdExe);
        if (!File.Exists(exe))
            throw new DownloadFailedException("This copy of EverythingImages is missing its AI engine. Rebuild it with build_wpf.bat.");
        var input = await PrepareImageAsync(imagePath);
        var sw = Stopwatch.StartNew();
        onStage(Stage.Start, "");

        // ggml loads its backend DLLs from the working directory, so the engine runs from its own folder.
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = bin,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[]
                 {
                     "-m", gguf, "--mmproj", mmproj, "--image", input, "-p", prompt,
                     // Sampling settings from Liquid AI's model card; 256 tokens
                     // leaves room for prompts asking for a paragraph or list.
                     "--temp", "0.1", "--min-p", "0.15", "--repeat-penalty", "1.05", "-n", "256",
                     // Room for one image, the prompt and the answer. Left out,
                     // llama.cpp sizes its cache for the model's whole 128k
                     // context: 1.5 GB of graphics memory set up for every image,
                     // most of each run's time. Measured on an RTX 3050: 48 MB,
                     // and ~3.0 s -> ~2.1 s an image. A 4000x3000 photo with the
                     // "Detailed" prompt still fits in 2048, so 4096 is headroom.
                     "-c", "4096",
                     "-lv", "4", // lowest level that prints the stage, device and offload lines
                 })
            psi.ArgumentList.Add(a);
        // CPU: no layers, no image encoder and no device on the graphics card at all.
        foreach (var a in gpu ? new[] { "-ngl", "99" } : new[] { "-ngl", "0", "--no-mmproj-offload", "-dev", "none" })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        proc.StandardInput.Close();

        var outBytes = new List<byte>();
        var readOut = Task.Run(async () =>
        {
            var buf = new byte[256];
            var last = DateTime.MinValue;
            var stream = proc.StandardOutput.BaseStream;
            int n;
            while ((n = await stream.ReadAsync(buf)) > 0)
            {
                string text;
                lock (outBytes)
                {
                    outBytes.AddRange(buf.AsSpan(0, n));
                    text = Encoding.UTF8.GetString([.. outBytes]).Trim();
                }
                if (text.Length > 0 && (DateTime.UtcNow - last).TotalMilliseconds >= 80)
                {
                    last = DateTime.UtcNow;
                    onStage(Stage.Writing, text);
                }
            }
        });
        var log = new StringBuilder();
        var readErr = Task.Run(async () =>
        {
            while (await proc.StandardError.ReadLineAsync() is { } line)
            {
                if (EnginesLogic.StageFromLogLine(line) is { } stage) onStage(stage, "");
                lock (log) log.AppendLine(line);
            }
        });

        using (var timeout = new CancellationTokenSource(Timeout))
        {
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                proc.Kill(entireProcessTree: true);
                throw new DownloadFailedException("The model took more than 3 minutes and was stopped.");
            }
        }
        await Task.WhenAll(readOut, readErr);

        string Tail() => string.Join(" | ", log.ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(3));
        if (proc.ExitCode != 0) throw new DownloadFailedException($"The model failed to run (exit {proc.ExitCode}): {Tail()}");
        var caption = Encoding.UTF8.GetString([.. outBytes]).Trim();
        if (caption.Length == 0) throw new DownloadFailedException($"The model returned no text: {Tail()}");
        var text = log.ToString();
        return new DescribeResult(caption, Math.Round(sw.Elapsed.TotalSeconds, 1), gpu, modelName, EnginesLogic.ParseOffload(text), EnginesLogic.ParseUsedDevice(text));
    }

    /// <summary>llama.cpp reads JPEG, PNG and BMP; other formats are converted
    /// to PNG with Windows' own decoder first.</summary>
    static async Task<string> PrepareImageAsync(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp") return path;
        var file = await StorageFile.GetFileFromPathAsync(System.IO.Path.GetFullPath(path));
        using var src = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(src);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var outPath = System.IO.Path.Combine(Paths.AppDataDir(), "tmp", "describe-input.png");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
        using (var ms = new InMemoryRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            ms.Seek(0); // the encoder leaves the position at the end
            await using var fs = File.Create(outPath);
            await ms.AsStreamForRead().CopyToAsync(fs);
        }
        return outPath;
    }
}
