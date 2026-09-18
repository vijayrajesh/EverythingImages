using System.Diagnostics;
using System.Windows.Threading;
using EverythingImages.Core;

namespace EverythingImages;

/// <summary>
/// All app state and actions. The windows bind to it and call its methods;
/// nothing in the XAML code-behind touches files or processes. Everything
/// here runs on the UI thread (async work resumes there), so the index is
/// only ever touched from one thread.
/// </summary>
public sealed class AppState : Observable
{
    public ImageIndex Index { get; } = new(System.IO.Path.Combine(Paths.AppDataDir(), "index.json"));
    readonly Scanner _scanner = new();
    public ModelCatalog Catalog { get; } = new();
    public GpuDetector Gpu { get; } = new();
    public Settings Settings { get; } = Settings.Load();

    // ---- toast ----------------------------------------------------------------
    string _toast = "";
    public string Toast { get => _toast; private set => Set(ref _toast, value); }
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public void Say(string message)
    {
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public AppState()
    {
        _toastTimer.Tick += (_, _) => { Toast = ""; _toastTimer.Stop(); };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
    }

    // ---- search ---------------------------------------------------------------
    readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    string _query = "";
    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value)) return;
            _searchTimer.Stop();
            _searchTimer.Start(); // debounce typing
        }
    }

    QuickFilter _quick = QuickFilter.All;
    public QuickFilter Quick { get => _quick; set { if (Set(ref _quick, value)) RunSearch(); } }

    public static IReadOnlyList<KeyValuePair<string, string>> SortOptions { get; } =
    [
        new("Name_asc", "Name (A-Z)"), new("Name_desc", "Name (Z-A)"), new("Date_desc", "Modified (newest)"),
        new("Date_asc", "Modified (oldest)"), new("Size_desc", "File size (largest)"), new("Dimensions_desc", "Resolution (largest)"),
    ];

    string _sortKey = "Name_asc";
    public string SortKey { get => _sortKey; set { if (Set(ref _sortKey, value)) RunSearch(); } }

    public ObservableList<ImageItem> Items { get; } = [];
    public ObservableList<ImageRow> Rows { get; } = [];
    readonly Dictionary<string, ImageItem> _byId = [];

    int _total;
    public int ResultCount { get => _total; private set => Set(ref _total, value); }
    double _ms;
    public double SearchMs { get => _ms; private set => Set(ref _ms, value); }
    public int FolderCount => Index.Folders.Count;
    public string FolderText => FolderCount == 1 ? "1 folder" : $"{FolderCount} folders";
    public bool IsEmpty => Items.Count == 0;
    public bool NoFolders => Index.Folders.Count == 0;

    public void RunSearch()
    {
        var parts = SortKey.Split('_');
        var r = Index.Search(new SearchFilter(Query, Quick, Enum.Parse<SortBy>(parts[0]), parts[1] == "desc"));
        _byId.Clear();
        var items = r.Images.Select(rec =>
        {
            var it = new ImageItem(rec, ThumbPixels) { IsSelected = rec.Id == Selected?.Id };
            if (_live.TryGetValue(rec.Id, out var live)) it.Live = live;
            _byId[rec.Id] = it;
            return it;
        }).ToList();
        Items.ReplaceAll(items);
        RebuildRows();
        ResultCount = r.Total;
        SearchMs = r.SearchMs;
        Raise(nameof(IsEmpty));
        Raise(nameof(NoFolders));
        Raise(nameof(FolderCount)); Raise(nameof(FolderText));
        Raise(nameof(FooterStatus));
        if (Selected != null && Index[Selected.Id] is null) Select(null);
    }

    // ---- grid layout ------------------------------------------------------------
    public enum ThumbSize { Small, Medium, Large }
    ThumbSize _size = ThumbSize.Medium;
    public ThumbSize Size
    {
        get => _size;
        set
        {
            if (!Set(ref _size, value)) return;
            Raise(nameof(CardWidth));
            Raise(nameof(CardImageHeight));
            RunSearch(); // new thumbnail resolution
        }
    }

    public double CardWidth => Size switch { ThumbSize.Small => 150, ThumbSize.Large => 300, _ => 210 };
    public double CardImageHeight => Math.Round(CardWidth * 0.66);
    int ThumbPixels => (int)(CardWidth * 1.5);

    double _gridWidth = 900;
    public void SetGridWidth(double width)
    {
        if (Math.Abs(width - _gridWidth) < 1) return;
        _gridWidth = width;
        RebuildRows();
    }

    void RebuildRows()
    {
        var perRow = Math.Max(1, (int)((_gridWidth - 24) / (CardWidth + 12)));
        Rows.ReplaceAll(Items.Chunk(perRow).Select(c => new ImageRow(c)));
    }

    // ---- selection / inspector -----------------------------------------------------
    ImageItem? _selected;
    public ImageItem? Selected { get => _selected; private set => Set(ref _selected, value); }
    public IReadOnlyList<ColorChip> SelectedColors => Selected?.Record.DominantColors.Select(c => new ColorChip(c)).ToList() ?? [];

    public void Select(ImageItem? item)
    {
        if (Selected != null) Selected.IsSelected = false;
        Selected = item;
        if (item != null) item.IsSelected = true;
        Raise(nameof(SelectedColors));
        RaiseInspectorAi();
    }

    public void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public void ShowInFolder(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");
    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ---- startup ---------------------------------------------------------------------
    public async Task InitAsync()
    {
        Index.Load();
        RunSearch();
        _ = LoadModelsAsync(false);
        _ = LoadGpuAsync();
        if (Index.Folders.Count > 0) await StartScanAsync(quiet: true);
    }

    // ---- folders & scanning ------------------------------------------------------------
    bool _scanning, _scanQuiet, _rescanPending;
    int _scanDone, _scanTotal;
    string _scanCurrent = "";
    public bool Scanning { get => _scanning; private set { if (Set(ref _scanning, value)) RaiseBusy(); } }
    public string ScanButtonLabel => Scanning ? "Scanning..." : "Scan";

    public string FooterStatus => Scanning
        ? (_scanTotal > 0 ? $"Scanning folders ({_scanDone}/{_scanTotal})" : "Scanning folders...")
        : $"✓ Index up to date ({Index.Count} items)";

    public async Task AddFolderAsync(string folder)
    {
        if (!Index.AddFolder(folder))
        {
            Say($"{folder} is already in the list.");
            return;
        }
        Index.Save();
        Raise(nameof(FolderCount)); Raise(nameof(FolderText));
        Raise(nameof(NoFolders));
        Say($"Added {folder}. Scanning it now...");
        await StartScanAsync();
    }

    public void RemoveFolder(string folder)
    {
        Index.RemoveFolder(folder);
        Index.Save();
        RunSearch();
    }

    public async Task StartScanAsync(bool quiet = false)
    {
        if (Index.Folders.Count == 0) return;
        if (Scanning)
        {
            _rescanPending = true; // a folder was added mid-scan: go again after
            return;
        }
        if (AiRunning)
        {
            if (!quiet) Say("Describe all is running. Stop it first, or scan when it finishes.");
            return;
        }
        _scanQuiet = quiet;
        _scanDone = _scanTotal = 0;
        _scanCurrent = "Looking for images...";
        Scanning = true;
        RaiseBanner();
        try
        {
            var s = await _scanner.ScanAsync(Index, Settings.OcrEnabled, new Progress<(int Done, int Total, string Current)>(p =>
            {
                (_scanDone, _scanTotal, _scanCurrent) = p;
                Raise(nameof(FooterStatus));
                RaiseBanner();
            }));
            if (!quiet || s.Updated > 0 || s.Pruned > 0)
            {
                var parts = new List<string>();
                if (s.Updated > 0) parts.Add($"{s.Updated} new or changed");
                if (s.Pruned > 0) parts.Add($"{s.Pruned} removed");
                Say(s.Stopped ? "Scan stopped."
                    : parts.Count == 0 ? $"Scan finished: all {s.Total} images are up to date."
                    : $"Scan finished in {s.Seconds:0.0}s: {string.Join(", ", parts)}. {s.Total} images in total.");
            }
        }
        catch (Exception e)
        {
            Say($"Scan failed: {e.Message}");
        }
        finally
        {
            Scanning = false;
            _scanQuiet = false;
            RunSearch();
            RaiseBanner();
        }
        if (_rescanPending)
        {
            _rescanPending = false;
            await StartScanAsync();
        }
    }

    public void StopScan()
    {
        _rescanPending = false;
        _scanner.Stop();
    }

    public void ResetIndex()
    {
        Index.Clear();
        Index.Save();
        Select(null);
        RunSearch();
    }

    public void SetOcrEnabled(bool on)
    {
        Settings.OcrEnabled = on;
        Settings.Save();
        Raise(nameof(OcrFooter));
    }

    public string OcrFooter => Settings.OcrEnabled ? "Windows OCR" : "OCR off";

    // ---- models -------------------------------------------------------------------------
    public IReadOnlyList<VisionModel> Models { get; private set; } = [];
    public bool ModelsLoading { get; private set; }
    public string? ModelsError { get; private set; }
    public string? ModelsOffline { get; private set; }
    public Dictionary<string, (long Done, long Total, double Speed)> ModelProgress { get; } = [];
    bool _checkedOnline;
    public event Action? ModelsChanged;

    public async Task LoadModelsAsync(bool refresh)
    {
        ModelsLoading = true;
        ModelsChanged?.Invoke();
        try
        {
            var c = await Catalog.ListAsync(refresh);
            Models = c.Models;
            ModelsOffline = c.OfflineReason;
            ModelsError = null;
        }
        catch (Exception e)
        {
            ModelsError = e.Message;
        }
        ModelsLoading = false;
        ModelsChanged?.Invoke();
        RaiseInspectorAi();
        Raise(nameof(DescribeAllTip));
    }

    /// <summary>Opening Options checks Hugging Face once per session.</summary>
    public async Task OnOptionsOpenedAsync()
    {
        _ = LoadGpuAsync();
        await LoadModelsAsync(!_checkedOnline);
        _checkedOnline = true;
    }

    public VisionModel? ModelInUse
    {
        get
        {
            var ready = Models.Where(m => m.IsDownloaded).ToList();
            return ready.FirstOrDefault(m => m.Id == Settings.ModelId) ?? ready.FirstOrDefault();
        }
    }

    public void UseModel(VisionModel m)
    {
        Settings.ModelId = m.Id;
        Settings.Save();
        Say($"{m.Name} will describe your images.");
        ModelsChanged?.Invoke();
        RaiseInspectorAi();
    }

    public async Task DownloadModelAsync(VisionModel m)
    {
        var sw = Stopwatch.StartNew();
        long lastBytes = 0;
        double lastSecs = 0, speed = 0;
        ModelProgress[m.Id] = (m.DownloadedBytes, m.TotalBytes, 0);
        ModelsChanged?.Invoke();
        var copying = ModelCatalog.CanCopyFromPc(m);
        try
        {
            await Catalog.DownloadAsync(m, new Progress<(long Done, long Total)>(p =>
            {
                var secs = sw.Elapsed.TotalSeconds;
                if (secs - lastSecs >= 1)
                {
                    speed = (p.Done - lastBytes) / (secs - lastSecs);
                    (lastBytes, lastSecs) = (p.Done, secs);
                }
                ModelProgress[m.Id] = (p.Done, p.Total, speed);
                ModelsChanged?.Invoke();
            }));
            Say(copying ? $"{m.Name} copied from this PC and verified." : $"{m.Name} downloaded and verified.");
        }
        catch (OperationCanceledException)
        {
            Say($"{m.Name} paused. Press Resume to continue.");
        }
        catch (Exception e)
        {
            Say($"{m.Name}: {e.Message}");
        }
        ModelProgress.Remove(m.Id);
        await LoadModelsAsync(false);
    }

    public void PauseModel(VisionModel m) => Catalog.Cancel(m.Id);

    public async Task DeleteModelAsync(VisionModel m)
    {
        try
        {
            Catalog.Delete(m);
            Say($"Deleted {m.Name}.");
        }
        catch (Exception e)
        {
            Say(e.Message);
        }
        await LoadModelsAsync(false);
    }

    // ---- GPU / CPU ----------------------------------------------------------------------
    public bool GpuLoaded { get; private set; }
    public event Action? GpuChanged;

    public async Task LoadGpuAsync()
    {
        await Gpu.DetectAsync();
        GpuLoaded = true;
        GpuChanged?.Invoke();
        RaiseInspectorAi();
    }

    /// <summary>GPU when the PC has one the engine can use, unless the user chose CPU.</summary>
    public string ChosenProcessor => Settings.Processor != "cpu" && Gpu.HasGpu ? "gpu" : "cpu";

    public bool UseGpuNow => ChosenProcessor == "gpu";

    public void ChooseProcessor(string which)
    {
        if (which == "gpu" && !Gpu.HasGpu) return;
        Settings.Processor = which;
        Settings.Save();
        GpuChanged?.Invoke();
        RaiseInspectorAi();
        Say($"AI models will run on the {which.ToUpperInvariant()}.");
    }

    // ---- describing ----------------------------------------------------------------------
    readonly Dictionary<string, LiveDescribe> _live = [];
    readonly Dictionary<string, DescribeResult> _lastResult = [];
    readonly Dictionary<string, string> _lastError = [];

    void SetLive(string id, LiveDescribe? live)
    {
        if (live is null) _live.Remove(id); else _live[id] = live;
        if (_byId.TryGetValue(id, out var item)) item.Live = live;
        if (Selected?.Id == id) RaiseInspectorAi();
    }

    async Task<DescribeResult> DescribeOneAsync(ImageRecord r, VisionModel m, bool useGpu, string prompt)
    {
        var files = ModelCatalog.LocalFiles(m) ?? throw new DownloadFailedException($"{m.Name} is not downloaded yet.");
        // The first run on a graphics card is slow while its driver prepares (then cached).
        var firstOnGpu = useGpu && !Gpu.Devices.Any(dev => dev.Name == Settings.GpuPrepared);
        try
        {
            var d = await Describer.DescribeAsync(useGpu, m.Name, files.Gguf, files.Mmproj, r.Path, prompt,
                (stage, text) => Ui.Post(() => SetLive(r.Id, new LiveDescribe(stage, text, firstOnGpu))));
            if (firstOnGpu && d.Device != null && (d.GpuLayers?.Done ?? 0) > 0)
            {
                Settings.GpuPrepared = d.Device;
                Settings.Save();
            }
            Index.SetAiCaption(r.Id, new AiCaption(d.Caption, d.Model));
            if (_byId.TryGetValue(r.Id, out var item)) item.CaptionChanged();
            return d;
        }
        finally
        {
            // Queued after any stage updates still in flight, so it clears last.
            Ui.Post(() => SetLive(r.Id, null));
        }
    }

    /// <summary>The Inspector's "Describe with AI".</summary>
    public async Task DescribeSelectedAsync()
    {
        var r = Selected?.Record;
        if (r is null || _live.ContainsKey(r.Id)) return;
        var m = ModelInUse;
        if (m is null)
        {
            Say("Download a Liquid AI model first (Options), then press Use.");
            return;
        }
        _lastError.Remove(r.Id);
        SetLive(r.Id, new LiveDescribe(Stage.Start, "")); // before the await: blocks a second click
        await Gpu.DetectAsync(); // GPU or CPU depends on what this PC has
        if (ChosenProcessor == "gpu" && !UseGpuNow) Say("GPU support is still being set up, so this runs on the CPU.");
        try
        {
            _lastResult[r.Id] = await DescribeOneAsync(r, m, UseGpuNow, Settings.DescribePrompt);
            Index.Save();
        }
        catch (Exception e)
        {
            _lastError[r.Id] = e.Message;
        }
        RaiseInspectorAi();
        Raise(nameof(DescribeAllTip));
    }

    // Inspector AI section
    public LiveDescribe? SelectedLive => Selected != null && _live.TryGetValue(Selected.Id, out var l) ? l : null;
    public bool SelectedBusy => SelectedLive != null;
    public string? SelectedError => Selected != null && _lastError.TryGetValue(Selected.Id, out var e) ? e : null;
    public string? SelectedCaption => SelectedLive is null && SelectedError is null ? Selected?.Record.AiCaption?.Text : null;
    public string DescribeButtonLabel => SelectedBusy ? "Describing..." : Selected?.Record.AiCaption != null ? "↻ Describe again" : "✨ Describe with AI";
    public string? ModelChip => ModelInUse is { } m ? $"⚡ {m.Name} · {(UseGpuNow ? "GPU" : "CPU")}" : null;

    public string SelectedMeta
    {
        get
        {
            if (Selected is null || Selected.Record.AiCaption is null) return "";
            if (!_lastResult.TryGetValue(Selected.Id, out var r)) return $"{Selected.Record.AiCaption.Model} • saved, searchable";
            // Where it ran comes from the engine's own log, not from the setting -
            // r.Device is the card it actually picked, which need not be the only one.
            var where = r.GpuLayers is { Done: > 0 } l ? $"GPU{(r.Device != null ? $" ({r.Device})" : "")}, {l.Done}/{l.Total} layers"
                : r.GpuChosen ? "GPU chosen, but no work reached the GPU" : "CPU";
            return $"{r.Model} • {r.Seconds}s on {where}";
        }
    }

    void RaiseInspectorAi()
    {
        foreach (var n in new[] { nameof(SelectedLive), nameof(SelectedBusy), nameof(SelectedError), nameof(SelectedCaption), nameof(DescribeButtonLabel), nameof(ModelChip), nameof(SelectedMeta) })
            Raise(n);
    }

    // ---- Describe all ------------------------------------------------------------------------
    bool _aiRunning, _aiStop;
    int _aiDone, _aiTotal, _aiFailed;
    string _aiTitle = "", _aiCurrent = "";
    DateTime _aiStarted;
    public bool AiRunning { get => _aiRunning; private set { if (Set(ref _aiRunning, value)) RaiseBusy(); } }
    public string DescribeAllLabel => AiRunning ? $"Describing {_aiDone}/{_aiTotal}" : "Describe all";
    public string DescribeAllTip => $"Describe every image with the AI model in use ({Index.AiCaptionCount} of {Index.Count} done); descriptions become searchable";
    public bool CanScan => !Scanning && !AiRunning;

    void RaiseBusy()
    {
        Raise(nameof(CanScan));
        Raise(nameof(ScanButtonLabel));
        Raise(nameof(DescribeAllLabel));
        Raise(nameof(FooterStatus));
    }

    public async Task DescribeAllAsync()
    {
        if (AiRunning || Scanning) return;
        var m = ModelInUse;
        if (m is null)
        {
            Say("Choose an AI model first: download one under Options and press Use.");
            return;
        }
        // Starts at the first image in the grid and follows it, whatever the sort or filter.
        var todo = Index.NeedingAiCaption(Items.Select(i => i.Id));
        if (todo.Count == 0)
        {
            Say("Every image already has an AI description.");
            return;
        }
        await Gpu.DetectAsync(); // GPU or CPU depends on what this PC has
        var useGpu = UseGpuNow;
        if (ChosenProcessor == "gpu" && !useGpu) Say("GPU support is still being set up, so this runs on the CPU.");
        var prompt = Settings.DescribePrompt; // fixed for the whole run
        (_aiStop, _aiDone, _aiFailed, _aiTotal, _aiStarted) = (false, 0, 0, todo.Count, DateTime.Now);
        _aiTitle = $"Describing images with {m.Name} on the {(useGpu ? "GPU" : "CPU")}...";
        AiRunning = true;
        RaiseBanner();

        int described = 0, inARow = 0;
        string? lastErr = null;
        var gaveUp = false;
        var lastSave = DateTime.Now;
        foreach (var r in todo)
        {
            if (_aiStop) break;
            _aiCurrent = r.Filename;
            RaiseBanner();
            try
            {
                await DescribeOneAsync(r, m, useGpu, prompt);
                described++;
                inARow = 0;
                if ((DateTime.Now - lastSave).TotalSeconds >= 5)
                {
                    Index.Save();
                    lastSave = DateTime.Now;
                }
            }
            catch (Exception e)
            {
                _aiFailed++;
                inARow++;
                lastErr = $"{r.Filename}: {e.Message}";
                // The first three all failing means the setup is wrong, not the images.
                if (described == 0 && inARow >= 3)
                {
                    gaveUp = true;
                    break;
                }
            }
            _aiDone++;
            Raise(nameof(DescribeAllLabel));
            RaiseBanner();
        }
        Index.Save();
        AiRunning = false;
        RaiseBanner();
        Raise(nameof(DescribeAllTip));
        Say(gaveUp ? $"Describe all stopped: {lastErr}"
            : _aiStop ? $"Stopped. {described} images described; press Describe all to continue."
            : $"Done: {described} images described{(_aiFailed > 0 ? $", {_aiFailed} could not be read" : "")}. Their descriptions are now searchable.");
    }

    public void StopDescribeAll()
    {
        _aiStop = true;
        RaiseBanner();
    }

    // ---- banner (a scan the user asked for, or Describe all) -----------------------------------
    public bool BannerVisible => AiRunning || (Scanning && !_scanQuiet);
    public string BannerTitle => AiRunning ? _aiTitle : "Scanning your folders...";
    public string BannerDetail => AiRunning ? (_aiStop ? "Stopping after the current image..." : _aiCurrent) : _scanCurrent;
    public double BannerFraction => AiRunning ? (_aiTotal == 0 ? 0 : (double)_aiDone / _aiTotal) : (_scanTotal == 0 ? 0 : (double)_scanDone / _scanTotal);
    public bool BannerIndeterminate => !AiRunning && _scanTotal == 0;

    public string BannerCount
    {
        get
        {
            if (!AiRunning) return _scanTotal > 0 ? $"{_scanDone} of {_scanTotal} images" : "";
            var s = $"{_aiDone} of {_aiTotal}";
            if (_aiFailed > 0) s += $" • {_aiFailed} skipped";
            if (_aiDone > 0)
            {
                var left = (DateTime.Now - _aiStarted).TotalSeconds / _aiDone * (_aiTotal - _aiDone);
                s += left < 60 ? $" • about {Math.Round(left)} s left" : $" • about {Math.Round(left / 60)} min left";
            }
            return s;
        }
    }

    public void StopBanner()
    {
        if (AiRunning) StopDescribeAll(); else StopScan();
    }

    void RaiseBanner()
    {
        foreach (var n in new[] { nameof(BannerVisible), nameof(BannerTitle), nameof(BannerDetail), nameof(BannerFraction), nameof(BannerIndeterminate), nameof(BannerCount) })
            Raise(n);
    }

    // ---- prompt -----------------------------------------------------------------------------------
    public void SavePrompt(string prompt)
    {
        Settings.DescribePrompt = Settings.ValidatePrompt(prompt);
        Settings.Save();
    }

    /// <summary>Clears every AI description and runs Describe all with the saved prompt.</summary>
    public async Task RedoAllWithPromptAsync()
    {
        Index.ClearAiCaptions();
        _lastResult.Clear();
        Index.Save();
        RunSearch();
        await DescribeAllAsync();
    }
}
