// End to end on the real machine: scan a folder (Windows' decoders + OCR),
// search, rescan, and describe on GPU and CPU with the AI engine. Uses a
// throwaway data folder; the models come from the shared folder (read-only)
// and the engine from beside the test assembly, as the app has it beside the
// exe. Needs EVERYTHINGIMAGES_TEST_IMAGES (a folder with a few images,
// including folders_dialog.png, which has text); without it the test reports
// that it did nothing.

using EverythingImages.Core;
using Xunit.Abstractions;

namespace EverythingImages.Tests;

public class EndToEndTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ScanOcrSearchDescribeOnGpuAndCpu()
    {
        var images = Environment.GetEnvironmentVariable("EVERYTHINGIMAGES_TEST_IMAGES");
        if (images is null)
        {
            output.WriteLine("EVERYTHINGIMAGES_TEST_IMAGES not set: end-to-end test skipped.");
            return;
        }
        var tmp = Directory.CreateTempSubdirectory("everythingimages-e2e").FullName;
        Paths.AppDataOverride = tmp;
        try
        {
            var index = new ImageIndex(System.IO.Path.Combine(tmp, "index.json"));
            index.AddFolder(images);
            var scanner = new Scanner();
            var s = await scanner.ScanAsync(index, ocr: true, new Progress<(int, int, string)>());
            output.WriteLine($"scan: {s.Total} images, {s.Updated} read in {s.Seconds * 1000:F0} ms");
            Assert.True(s.Total >= 3);

            var dialog = index.Records.Single(r => r.Filename == "folders_dialog.png");
            output.WriteLine($"OCR: \"{dialog.OcrText.ReplaceLineEndings(" | ")}\"");
            Assert.Contains("folders", dialog.OcrText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(560, dialog.Width);
            Assert.NotEmpty(dialog.DominantColors);
            Assert.True(index.Records.Single(r => r.Extension == "webp").Width > 0, "WebP decodes through Windows' codec");

            Assert.Contains(index.Search(new SearchFilter("indexed")).Images, r => r.Id == dialog.Id);
            Assert.True(index.Search(new SearchFilter("white")).Total > 0);
            Assert.Equal(0, index.Search(new SearchFilter("indexed zzzz")).Total);

            var again = await scanner.ScanAsync(index, ocr: true, new Progress<(int, int, string)>());
            Assert.Equal(0, again.Updated); // unchanged files are skipped

            var catalog = new ModelCatalog();
            var model = (await catalog.ListAsync()).Models.FirstOrDefault(m => m.IsDownloaded);
            if (model is null)
            {
                output.WriteLine("no downloaded model: AI part skipped");
                return;
            }
            var (gguf, mmproj) = ModelCatalog.LocalFiles(model)!.Value;
            var stages = new List<Stage>();

            var gpu = new GpuDetector();
            await gpu.DetectAsync();
            if (gpu.HasGpu && GpuDetector.EngineReady)
            {
                output.WriteLine($"cards: {string.Join(", ", gpu.Devices.Select(d => d.Name))}");
                var g = await Describer.DescribeAsync(true, model.Name, gguf, mmproj, dialog.Path, Settings.DefaultPrompt, (st, _) => stages.Add(st));
                output.WriteLine($"GPU: {g.Seconds}s layers={g.GpuLayers} \"{g.Caption}\"");
                Assert.True((g.GpuLayers?.Done ?? 0) > 0, "llama.cpp's log shows GPU offload");
                Assert.Equal([Stage.Start, Stage.Loading, Stage.Reading, Stage.Writing], stages.Distinct());
            }

            var c = await Describer.DescribeAsync(false, model.Name, gguf, mmproj, dialog.Path, Settings.DefaultPrompt, (_, _) => { });
            output.WriteLine($"CPU: {c.Seconds}s layers={c.GpuLayers} \"{c.Caption}\"");
            Assert.Equal(0, c.GpuLayers?.Done ?? 0); // the CPU runtime must not touch the GPU

            // A WebP goes through the PNG conversion first.
            var webp = index.Records.Single(r => r.Extension == "webp");
            var w = await Describer.DescribeAsync(false, model.Name, gguf, mmproj, webp.Path, Settings.DefaultPrompt, (_, _) => { });
            output.WriteLine($"CPU (webp): \"{w.Caption}\"");

            index.SetAiCaption(dialog.Id, new AiCaption(c.Caption, c.Model));
            index.Save();
            var back = new ImageIndex(System.IO.Path.Combine(tmp, "index.json"));
            back.Load();
            Assert.Equal(c.Caption, back[dialog.Id]!.AiCaption!.Text);
        }
        finally
        {
            Paths.AppDataOverride = null;
            Directory.Delete(tmp, recursive: true);
        }
    }
}
