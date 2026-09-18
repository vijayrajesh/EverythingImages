using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EverythingImages.Core;

namespace EverythingImages;

public partial class OptionsWindow : Window
{
    readonly AppState _s;

    static readonly (string Label, string Prompt)[] Presets =
    [
        ("One sentence", "Describe this image in one sentence."),
        ("Detailed", "Describe this image in detail: the main subject, the setting, the colours, any visible text, and the mood."),
        ("Search keywords", "List 10 short keywords that describe this image, separated by commas."),
        ("Read the text", "Write out any text that appears in this image. If there is none, describe the image in one sentence."),
    ];

    public OptionsWindow(AppState state)
    {
        _s = state;
        InitializeComponent();
        OcrCheck.IsChecked = _s.Settings.OcrEnabled;
        PromptBox.MaxLength = Settings.MaxPromptChars;
        PromptBox.Text = _s.Settings.DescribePrompt;
        if (Paths.IsPortable)
            ModelsNote.Text = "Small vision-language models from Hugging Face. This portable copy keeps them in its own data folder.";
        foreach (var (label, prompt) in Presets)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3), Tag = prompt };
            b.Click += (_, _) => { PromptBox.Text = prompt; PromptBox.Focus(); };
            PresetPanel.Children.Add(b);
        }
        _s.ModelsChanged += OnModelsChanged;
        _s.GpuChanged += OnGpuChanged;
        Closed += (_, _) => { _s.ModelsChanged -= OnModelsChanged; _s.GpuChanged -= OnGpuChanged; };
        BuildProcessors();
        BuildModels();
        UpdatePromptUi();
    }

    void OnModelsChanged() => Dispatcher.BeginInvoke(BuildModels);
    void OnGpuChanged() => Dispatcher.BeginInvoke(BuildProcessors);

    // ---- helpers --------------------------------------------------------------------
    static TextBlock Text(string s, double size = 13, bool bold = false, Brush? color = null, bool wrap = true) => new()
    {
        Text = s, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = color ?? (Brush)Application.Current.Resources["Ink"], TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    static Border Pill(string s, string bg = "#F1F3F7", string fg = "#4B5563") => new()
    {
        CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 0, 0),
        Background = Format.Brush(bg), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = s, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Format.Brush(fg) },
    };

    static Button Btn(string content, RoutedEventHandler click, string? style = null)
    {
        var b = new Button { Content = content, Margin = new Thickness(6, 0, 0, 0) };
        if (style != null) b.Style = (Style)Application.Current.Resources[style];
        b.Click += click;
        return b;
    }

    // ---- GPU / CPU cards -----------------------------------------------------------------
    // Nothing is downloaded or set up here any more: the AI engine ships beside
    // the exe and reaches any card through the driver already on the PC. So
    // this is a plain choice between two things that are either there or not.
    void BuildProcessors()
    {
        ProcessorPanel.Children.Clear();
        var chosen = _s.ChosenProcessor;
        var g = _s.Gpu;
        const string noEngine = "The AI engine is missing from this copy of EverythingImages.";

        string gpuName, gpuState;
        Button? gpuAction = null;
        if (!_s.GpuLoaded)
        {
            gpuName = "Looking for a graphics card...";
            gpuState = "Asking the engine which cards it can use...";
        }
        else if (!GpuDetector.EngineReady)
        {
            gpuName = "Can't tell without the engine";
            gpuState = noEngine;
        }
        else if (g.HasGpu)
        {
            // Every card the engine offers, not just the one it will pick: on a
            // laptop with switchable graphics, which one ran is worth seeing.
            gpuName = string.Join(", ", g.Devices.Select(d => $"{d.Name} ({Math.Round(d.TotalMb / 1024.0)} GB)"));
            gpuState = "✓ Ready";
        }
        else
        {
            gpuName = "No usable graphics card";
            gpuState = "No card answered. A graphics driver that is out of date is the usual reason.";
        }

        // A portable copy made before the engine shipped may still be carrying
        // the old GPU download in its data folder. Nothing uses it; this is the
        // only way to get the space back.
        if (_s.OldGpuDownloadBytes is > 0 and var stale)
        {
            gpuState += $"\nAn older version left {Format.Bytes(stale)} of GPU support here. Nothing uses it now.";
            gpuAction = Btn($"Free {Format.Bytes(stale)}", async (_, _) =>
            {
                if (MessageBox.Show(this, $"Delete the {Format.Bytes(stale)} of GPU support left by an older version?\n\n" +
                        "Nothing uses it: this copy runs the models with the engine beside its exe.",
                        "Old GPU download", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                    await _s.DeleteOldGpuDownloadAsync();
            }, "Ghost");
        }

        ProcessorPanel.Children.Add(Card("GPU", gpuName, "Runs the models on the graphics card: several times faster.",
            gpuState, gpuAction, chosen == "gpu", g.HasGpu, () => _s.ChooseProcessor("gpu")));
        ProcessorPanel.Children.Add(Card("CPU", "Any PC", "Runs the models on the processor. Slower, but works on any PC.",
            GpuDetector.EngineReady ? "✓ Ready" : noEngine, null, chosen == "cpu", GpuDetector.EngineReady,
            () => _s.ChooseProcessor("cpu")));
    }

    Border Card(string title, string tag, string desc, string state, Button? action, bool selected, bool enabled, Action choose)
    {
        var body = new StackPanel();
        var head = new DockPanel();
        // A real radio button, so keyboard and screen-reader users can choose too.
        var radio = new RadioButton { IsChecked = selected, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center, Focusable = true };
        System.Windows.Automation.AutomationProperties.SetName(radio, $"Run AI models on {title}");
        radio.Checked += (_, _) => { if (!selected) choose(); };
        DockPanel.SetDock(radio, Dock.Right);
        head.Children.Add(radio);
        head.Children.Add(Text(title, 13.5, bold: true));
        body.Children.Add(head);
        body.Children.Add(Text(tag, 11, color: Res("Muted")));
        var d = Text(desc, 12);
        d.Margin = new Thickness(0, 6, 0, 8);
        body.Children.Add(d);
        var foot = new DockPanel();
        if (action != null)
        {
            DockPanel.SetDock(action, Dock.Right);
            foot.Children.Add(action);
        }
        foot.Children.Add(new TextBlock { Text = state, FontSize = 11.5, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(foot);

        var border = new Border
        {
            Child = body, Padding = new Thickness(12), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(selected ? 2 : 1), BorderBrush = selected ? Res("Brand") : Res("Line"),
            Background = selected ? Res("BrandSoft") : Brushes.White, Opacity = enabled ? 1 : 0.55,
            Cursor = enabled ? System.Windows.Input.Cursors.Hand : null,
        };
        if (enabled) border.MouseLeftButtonUp += (_, e) =>
        {
            if (selected || e.OriginalSource is Button || e.OriginalSource is FrameworkElement { TemplatedParent: RadioButton }) return;
            choose();
        };
        return border;
    }

    // ---- models ----------------------------------------------------------------------------
    void BuildModels()
    {
        RefreshButton.IsEnabled = !_s.ModelsLoading;
        ModelsPanel.Children.Clear();
        if (_s.Models.Count == 0)
        {
            ModelsPanel.Children.Add(Text(_s.ModelsLoading ? "Checking Hugging Face for Liquid AI models..."
                : _s.ModelsError != null ? $"Could not load the model list: {_s.ModelsError}" : "No models found.", color: Res("Muted")));
            return;
        }
        if (_s.ModelsOffline != null) ModelsPanel.Children.Add(Text("Offline: showing the list from your last check.", 12, color: Res("Muted")));
        var latest = _s.Models[0].Family;
        var inUse = _s.ModelInUse?.Id;
        foreach (var m in _s.Models) ModelsPanel.Children.Add(ModelCard(m, m.Family == latest, m.Id == inUse));
    }

    Border ModelCard(VisionModel m, bool latest, bool inUse)
    {
        var head = new DockPanel();
        var card = new Button { Content = "Model card ↗", Style = (Style)Application.Current.Resources["Link"] };
        card.Click += (_, _) => _s.OpenUrl(m.CardUrl);
        DockPanel.SetDock(card, Dock.Right);
        head.Children.Add(card);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Text(m.Name, 14, bold: true, wrap: false));
        title.Children.Add(Pill("Liquid AI"));
        if (latest) title.Children.Add(Pill("Latest"));
        if (m.IsDownloaded) title.Children.Add(Pill("Downloaded", "#DCFCE7", "#15803D"));
        head.Children.Add(title);

        var actions = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
        void Right(UIElement e) { DockPanel.SetDock(e, Dock.Right); actions.Children.Add(e); }
        if (_s.ModelProgress.TryGetValue(m.Id, out var p))
        {
            var frac = p.Total == 0 ? 0 : (double)p.Done / p.Total;
            Right(Btn("Pause", (_, _) => _s.PauseModel(m)));
            var bar = new StackPanel { Width = 480 };
            bar.Children.Add(new ProgressBar { Maximum = 1, Value = frac });
            bar.Children.Add(Text($"{Format.Bytes(p.Done)} of {Format.Bytes(p.Total)} ({Math.Floor(frac * 100)}%)" + (p.Speed > 0 ? $" • {Format.Bytes(p.Speed)}/s" : ""), 11, color: Res("Muted")));
            DockPanel.SetDock(bar, Dock.Left);
            actions.Children.Add(bar);
        }
        else if (m.IsDownloaded)
        {
            Right(inUse ? Pill("✓ In use", "#EEF2FF", "#2563EB") : Btn("Use", (_, _) => _s.UseModel(m), "Primary"));
            Right(Btn("Show files", (_, _) => _s.OpenFile(m.LocalDir)));
            Right(Btn("Delete", (_, _) => ConfirmDelete(m), "Ghost"));
        }
        else if (m.DownloadedBytes > 0)
        {
            Right(Btn($"Resume ({Format.Bytes(m.TotalBytes - m.DownloadedBytes)} left)", async (_, _) => await _s.DownloadModelAsync(m), "Primary"));
            Right(Btn("Discard", (_, _) => ConfirmDelete(m), "Ghost"));
            Right(Text($"{Format.Bytes(m.DownloadedBytes)} of {Format.Bytes(m.TotalBytes)}", 11, color: Res("Muted"), wrap: false));
        }
        else
        {
            Right(Btn(ModelCatalog.CanCopyFromPc(m) ? $"⧉ Copy from this PC ({Format.Bytes(m.TotalBytes)})" : $"⬇ Download ({Format.Bytes(m.TotalBytes)})",
                async (_, _) => await _s.DownloadModelAsync(m), "Primary"));
        }

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(Text($"{m.Params} parameters • {m.Quant} language model + vision projector (GGUF) • {Format.Bytes(m.TotalBytes)}", 12, color: Res("Muted")));
        body.Children.Add(actions);
        return new Border { Child = body, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Res("Line") };
    }

    async void ConfirmDelete(VisionModel m)
    {
        var msg = m.IsDownloaded ? $"Delete {m.Name}? This frees {Format.Bytes(m.TotalBytes)}." : $"Discard the partial download of {m.Name}?";
        if (MessageBox.Show(this, msg, "Model", MessageBoxButton.OKCancel) == MessageBoxResult.OK) await _s.DeleteModelAsync(m);
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await _s.LoadModelsAsync(true);

    // ---- OCR ----------------------------------------------------------------------------------
    void Ocr_Changed(object sender, RoutedEventArgs e)
    {
        if (_s is null) return; // fires during InitializeComponent
        _s.SetOcrEnabled(OcrCheck.IsChecked == true);
    }

    // ---- prompt ---------------------------------------------------------------------------------
    bool Dirty => PromptBox.Text.Trim() != _s.Settings.DescribePrompt;

    void Prompt_Changed(object sender, TextChangedEventArgs e)
    {
        if (_s is null) return;
        UpdatePromptUi();
    }

    void UpdatePromptUi()
    {
        var value = PromptBox.Text.Trim();
        SaveButton.IsEnabled = Dirty && value.Length > 0;
        ResetButton.IsEnabled = value != Settings.DefaultPrompt;
        (PromptStatus.Text, PromptStatus.Foreground) = value.Length == 0 ? ("The prompt can't be empty.", Res("Bad"))
            : Dirty ? ("Not saved yet", Res("Warn"))
            : (_s.Settings.DescribePrompt == Settings.DefaultPrompt ? "✓ Saved (the default prompt)" : "✓ Saved", Res("Good"));
        foreach (var b in PresetPanel.Children.OfType<Button>())
            b.BorderBrush = (string)b.Tag == value ? Res("Brand") : Format.Brush("#CBD2DC");
    }

    void SavePrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _s.SavePrompt(PromptBox.Text);
            PromptBox.Text = _s.Settings.DescribePrompt;
            UpdatePromptUi();
            _s.Say("Prompt saved. Images described from now on use it.");
        }
        catch (ArgumentException ex)
        {
            _s.Say(ex.Message);
        }
    }

    void ResetPrompt_Click(object sender, RoutedEventArgs e) => PromptBox.Text = Settings.DefaultPrompt;

    async void RedoAll_Click(object sender, RoutedEventArgs e)
    {
        if (Dirty) { _s.Say("Save the prompt first, so the redo uses it."); return; }
        if (_s.Scanning || _s.AiRunning) { _s.Say("Wait for the current scan or Describe all to finish."); return; }
        if (_s.ModelInUse is null) { _s.Say("Download a model and press Use first."); return; }
        var n = _s.Index.AiCaptionCount;
        var msg = (n > 0 ? $"Remove the {n} existing AI descriptions and describe" : "Describe") +
                  $" all {_s.Index.Count} images with this prompt?\n\n\"{_s.Settings.DescribePrompt}\"";
        if (MessageBox.Show(this, msg, "Redo all", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        Close();
        await _s.RedoAllWithPromptAsync();
    }

    // ---- closing --------------------------------------------------------------------------------
    void Done_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Dirty && MessageBox.Show(this, "Your prompt changes are not saved. Close anyway?", "Options",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
