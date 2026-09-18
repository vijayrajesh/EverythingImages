// Unit tests for the core.

using System.Text.Json.Nodes;
using EverythingImages.Core;

namespace EverythingImages.Tests;

public class ColorTests
{
    [Fact]
    public void ClassifiesPrimaryHues()
    {
        Assert.Equal("Red", Colors.Classify(255, 0, 0));
        Assert.Equal("Green", Colors.Classify(0, 255, 0));
        Assert.Equal("Blue", Colors.Classify(0, 0, 255));
        Assert.Equal("Yellow", Colors.Classify(255, 255, 0));
        Assert.Equal("Black", Colors.Classify(0, 0, 0));
        Assert.Equal("White", Colors.Classify(255, 255, 255));
        Assert.Equal("Gray", Colors.Classify(128, 128, 128));
    }

    [Fact]
    public void MergesShadesAndRanksByShare()
    {
        // BGRA: 3 px of close reds, 1 px blue -> one red entry (75%) then blue.
        byte[] px = [0, 0, 250, 255, 5, 10, 240, 255, 0, 5, 245, 255, 255, 0, 0, 255];
        var (colors, primary) = Colors.Dominant(px);
        Assert.Equal("Red", primary);
        Assert.Equal(2, colors.Count);
        Assert.Equal(75.0, colors[0].Pct);
        Assert.Equal("Blue", colors[1].Name);
    }
}

public class IndexTests
{
    internal static ImageRecord Rec(string id, string path, long mtime = 1767225600) => new()
    {
        Id = id,
        Path = path,
        Filename = path.Split('\\')[^1],
        Extension = "jpg",
        Width = 1920,
        Height = 1080,
        FileSize = 2048,
        MtimeSecs = mtime,
        DominantColors = [new HexColor("#FF0000", 255, 0, 0, 100, "Red")],
        PrimaryColor = "Red",
        OcrText = "Two hundred rupees",
    };

    [Fact]
    public void SearchMatchesPrefixesNotSubstringsAndAndsWords()
    {
        var idx = new ImageIndex();
        idx.Put(Rec("a", @"C:\Photos\sunset.jpg"));
        int Hit(string q) => idx.Search(new SearchFilter(q)).Total;
        Assert.Equal(1, Hit("sun"));
        Assert.Equal(1, Hit("sunset"));
        Assert.Equal(1, Hit("hund")); // OCR text is indexed by prefix
        Assert.Equal(1, Hit("red"));  // colour names are tokens
        Assert.Equal(0, Hit("und"));  // a substring must not match
        Assert.Equal(0, Hit("sun nothing")); // words are ANDed
    }

    [Fact]
    public void FolderMatchRespectsPathBoundaries()
    {
        Assert.True(ImageIndex.IsUnderFolder(@"C:\Photos\a.jpg", @"C:\Photos"));
        Assert.True(ImageIndex.IsUnderFolder(@"C:\Photos\sub\a.jpg", @"C:\Photos\"));
        Assert.False(ImageIndex.IsUnderFolder(@"C:\Photos2\a.jpg", @"C:\Photos"));
    }

    [Fact]
    public void UnchangedNeedsSameSizeAndKnownMtime()
    {
        var idx = new ImageIndex();
        idx.Put(Rec("a", @"C:\P\a.jpg"));
        Assert.True(idx.IsUnchanged("a", 2048, 1767225600));
        Assert.False(idx.IsUnchanged("a", 2049, 1767225600));
        Assert.False(idx.IsUnchanged("a", 2048, 1767225601));
        idx.Put(Rec("b", @"C:\P\b.jpg", mtime: 0));
        Assert.False(idx.IsUnchanged("b", 2048, 0));
    }

    [Fact]
    public void AiCaptionsAreSearchableTrackedAndClearable()
    {
        var idx = new ImageIndex();
        idx.Put(Rec("a", @"C:\P\a.jpg"));
        idx.Put(Rec("b", @"C:\P\b.jpg"));
        Assert.Equal(2, idx.NeedingAiCaption().Count);
        Assert.True(idx.SetAiCaption("a", new AiCaption("A lighthouse in a storm", "m")));
        Assert.False(idx.SetAiCaption("gone", new AiCaption("x", "m")));
        Assert.Equal("a", Assert.Single(idx.Search(new SearchFilter("lighthouse")).Images).Id);
        Assert.Equal("b", Assert.Single(idx.NeedingAiCaption()).Id);
        Assert.Equal(1, idx.ClearAiCaptions());
        Assert.Equal(0, idx.Search(new SearchFilter("lighthouse")).Total);
    }

    [Fact]
    public void DescribeAllFollowsTheGridThenTheRestByPath()
    {
        var idx = new ImageIndex();
        idx.Put(Rec("z", @"C:\P\Zebra.jpg"));
        idx.Put(Rec("a", @"C:\P\apple.jpg"));
        idx.Put(Rec("m", @"C:\Q\mango.jpg"));
        idx.Put(Rec("d", @"C:\P\done.jpg"));
        idx.SetAiCaption("d", new AiCaption("x", "m"));
        string Order(IEnumerable<string>? grid) => string.Join("", idx.NeedingAiCaption(grid).Select(r => r.Id));
        Assert.Equal("azm", Order(null));               // by path, case ignored: apple before Zebra
        Assert.Equal("mza", Order(["m", "d", "z"])); // grid first (described ones skipped), then the rest
        Assert.Equal("zam", Order(["z"]));
    }

    [Fact]
    public void SavesAndLoadsKeepingCaptions()
    {
        var dir = Directory.CreateTempSubdirectory("cs-index");
        var file = System.IO.Path.Combine(dir.FullName, "index.json");
        var idx = new ImageIndex(file);
        idx.Folders.Add(@"C:\P");
        idx.Put(Rec("a", @"C:\P\a.jpg"));
        idx.SetAiCaption("a", new AiCaption("A red kite", "m"));
        idx.Save();
        var back = new ImageIndex(file);
        back.Load();
        Assert.Equal([@"C:\P"], back.Folders);
        Assert.Equal("A red kite", back["a"]!.AiCaption!.Text);
        Assert.Equal(1, back.Search(new SearchFilter("kite")).Total);
        dir.Delete(recursive: true);
    }
}

public class VisionModelTests
{
    static JsonObject Entry(string path, long size) => new()
    {
        ["type"] = "file", ["path"] = path, ["size"] = size,
        ["lfs"] = new JsonObject { ["oid"] = "sha-" + path, ["size"] = size },
    };

    static JsonArray Tree() =>
    [
        new JsonObject { ["type"] = "file", ["path"] = "README.md", ["size"] = 10 },
        Entry("LFM2.5-VL-450M-F16.gguf", 711),
        Entry("LFM2.5-VL-450M-Q4_0.gguf", 219),
        Entry("LFM2.5-VL-450M-Q4_K_M.gguf", 229),
        Entry("mmproj-LFM2.5-VL-450m-F16.gguf", 189),
        Entry("mmproj-LFM2.5-VL-450m-Q8_0.gguf", 102),
    ];

    [Fact]
    public void KeepsOnlyLiquidVlGgufRepos()
    {
        var listing = new JsonArray(
            new[] { "LiquidAI/LFM2.5-VL-450M-GGUF", "LiquidAI/LFM2.5-VL-450M", "LiquidAI/LFM2-1.2B-GGUF", "someone/LFM2.5-VL-450M-GGUF" }
                .Select(id => (JsonNode)new JsonObject { ["id"] = id }).ToArray());
        Assert.Equal(["LiquidAI/LFM2.5-VL-450M-GGUF"], VisionModels.GgufRepoIds(listing));
    }

    [Fact]
    public void ParsesFamilySizeAndVariant()
    {
        Assert.Equal(("LFM2.5-VL", "1.6B", "Extract"), VisionModels.ParseRepoName("LiquidAI/LFM2.5-VL-1.6B-Extract-GGUF"));
        Assert.Equal(("LFM2-VL", "450M", null), VisionModels.ParseRepoName("LiquidAI/LFM2-VL-450M-GGUF"));
        Assert.Null(VisionModels.ParseRepoName("LiquidAI/LFM2-VL-450M"));
    }

    [Fact]
    public void PicksQ4KMModelAndQ8Projector()
    {
        var m = VisionModels.BuildModel("LiquidAI/LFM2.5-VL-450M-GGUF", Tree())!;
        Assert.Equal(["LFM2.5-VL-450M-Q4_K_M.gguf", "mmproj-LFM2.5-VL-450m-Q8_0.gguf"], m.Files.Select(f => f.Name));
        Assert.Equal("Q4_K_M", m.Quant);
        Assert.Equal(229 + 102, m.TotalBytes);
        Assert.Equal("https://huggingface.co/LiquidAI/LFM2.5-VL-450M-GGUF/resolve/main/LFM2.5-VL-450M-Q4_K_M.gguf", m.Files[0].Url);
    }

    [Fact]
    public void SortsNewestGenerationThenSmallestGeneralFirst()
    {
        JsonArray T() => [Entry("m-Q4_K_M.gguf", 1), Entry("mmproj-x-Q8_0.gguf", 1)];
        var models = new[] { "LiquidAI/LFM2-VL-450M-GGUF", "LiquidAI/LFM2.5-VL-3B-GGUF", "LiquidAI/LFM2.5-VL-450M-Extract-GGUF", "LiquidAI/LFM2.5-VL-1.6B-GGUF", "LiquidAI/LFM2.5-VL-450M-GGUF" }
            .Select(id => VisionModels.BuildModel(id, T())!).ToList();
        VisionModels.SortCatalog(models);
        Assert.Equal(["LFM2.5-VL 450M", "LFM2.5-VL 450M Extract", "LFM2.5-VL 1.6B", "LFM2.5-VL 3B", "LFM2-VL 450M"], models.Select(m => m.Name));
    }
}

public class EngineTests
{
    [Fact]
    public void ReadsTheCardsTheEngineOffers()
    {
        // Real `llama-mtmd-cli --list-devices` output: a heading, then the cards,
        // and the CPU is not one of them.
        const string output = """
            ggml_vulkan: Found 2 Vulkan devices:
            Available devices:
              Vulkan0: AMD Radeon(TM) Graphics (8112 MiB, 8112 MiB free)
              Vulkan1: NVIDIA GeForce RTX 3050 Laptop GPU (3962 MiB, 3367 MiB free)
            """;
        var devices = EnginesLogic.ParseDevices(output);
        Assert.Equal(2, devices.Count);
        // The name itself may contain brackets, and must survive them.
        Assert.Equal(("Vulkan0", "AMD Radeon(TM) Graphics", 8112, 8112),
            (devices[0].Id, devices[0].Name, devices[0].TotalMb, devices[0].FreeMb));
        Assert.Equal(("Vulkan1", "NVIDIA GeForce RTX 3050 Laptop GPU", 3962, 3367),
            (devices[1].Id, devices[1].Name, devices[1].TotalMb, devices[1].FreeMb));
        Assert.Empty(EnginesLogic.ParseDevices("ggml_vulkan: no devices found"));
    }

    [Fact]
    public void ReadsWhichCardARunActuallyUsed()
    {
        // With a PCI address, without one, and coloured as llama.cpp prints it.
        Assert.Equal("NVIDIA GeForce RTX 3050 Laptop GPU", EnginesLogic.ParseUsedDevice(
            "[32mI [0mggml_vulkan: using device Vulkan1 (NVIDIA GeForce RTX 3050 Laptop GPU) (0000:01:00.0) - 3365 MiB free"));
        Assert.Equal("AMD Radeon(TM) Graphics", EnginesLogic.ParseUsedDevice(
            "ggml_vulkan: using device Vulkan0 (AMD Radeon(TM) Graphics) - 8112 MiB free"));
        Assert.Null(EnginesLogic.ParseUsedDevice("load_tensors: CPU_Mapped model buffer size = 279.63 MiB"));
    }

    [Fact]
    public async Task CopiesFilesThatPassTheChecksumAndDownloadsOnlyTheOthers()
    {
        var root = Directory.CreateTempSubdirectory("cs-copy").FullName;
        var from = Directory.CreateDirectory(System.IO.Path.Combine(root, "pc")).FullName;
        var to = System.IO.Path.Combine(root, "portable");
        byte[] good = [1, 2, 3, 4, 5];
        await File.WriteAllBytesAsync(System.IO.Path.Combine(from, "model.gguf"), good);
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(good));
        var progress = new Progress<(long, long)>(_ => { });

        // A matching file is copied: the URL is never used.
        var file = new ModelFile("model.gguf", good.Length, sha, "http://127.0.0.1:1/never");
        Assert.True(Download.CanCopyAll([file], from));
        await Download.DownloadAllAsync([file], to, progress, CancellationToken.None, from);
        Assert.Equal(good, await File.ReadAllBytesAsync(System.IO.Path.Combine(to, "model.gguf")));

        // A same-size file with the wrong content is not trusted: it falls back to downloading.
        await File.WriteAllBytesAsync(System.IO.Path.Combine(from, "bad.gguf"), [9, 9, 9, 9, 9]);
        var bad = new ModelFile("bad.gguf", 5, sha, "http://127.0.0.1:1/unreachable");
        await Assert.ThrowsAnyAsync<Exception>(() => Download.DownloadAllAsync([bad], to, progress, CancellationToken.None, from));
        Assert.False(File.Exists(System.IO.Path.Combine(to, "bad.gguf")));
        Assert.False(File.Exists(VisionModels.PartPath(System.IO.Path.Combine(to, "bad.gguf"))));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void ReadsStagesAndGpuOffloadFromRealLogLines()
    {
        Assert.Equal(Stage.Loading, EnginesLogic.StageFromLogLine("0.01.667 I load_tensors: loading model tensors, this can take a while..."));
        Assert.Equal(Stage.Reading, EnginesLogic.StageFromLogLine("0.02.049 I encoding mtmd batch, n_chunks = 1 (done = 1, total = 3)"));
        Assert.Equal(Stage.Writing, EnginesLogic.StageFromLogLine("0.02.158 I image decoded (batch 1/1) in 12 ms"));
        Assert.Null(EnginesLogic.StageFromLogLine("0.01.686 I load_tensors: offloaded 17/17 layers to GPU"));
        const string log = "\u001b[32mI \u001b[0mload_tensors: offloading 15 repeating layers to GPU\n\u001b[32mI \u001b[0mload_tensors: offloaded 17/17 layers to GPU\n";
        Assert.Equal((17, 17), EnginesLogic.ParseOffload(log));
        Assert.Null(EnginesLogic.ParseOffload("CPU_Mapped model buffer"));
    }
}
