using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EverythingImages.Core;

namespace EverythingImages;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class Command(Action<object?> run, Func<object?, bool>? can = null) : ICommand
{
    public Command(Action run) : this(_ => run()) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => can?.Invoke(p) ?? true;
    public void Execute(object? p) => run(p);
}

/// <summary>Live progress of one image being described, from llama.cpp's
/// reported stages. While writing it grows with the text actually produced,
/// easing towards the end since the final length is unknown.</summary>
public sealed record LiveDescribe(Stage Stage, string Text, bool FirstOnGpu = false)
{
    public double Fraction => Stage switch
    {
        Stage.Start => 0.05,
        Stage.Loading => 0.25,
        Stage.Reading => 0.5,
        _ => Math.Min(0.97, 0.7 + 0.27 * (1 - Math.Exp(-Text.Length / 120.0))),
    };

    public string Label => Stage switch
    {
        not Stage.Writing when FirstOnGpu => "Preparing the graphics card (first time only, up to a minute)",
        Stage.Start => "Starting",
        Stage.Loading => "Loading the model",
        Stage.Reading => "Reading the image",
        _ => "Writing",
    };
}

/// <summary>One image in the results.</summary>
public sealed class ImageItem(ImageRecord record, int thumbWidth) : Observable
{
    public ImageRecord Record { get; } = record;
    public string Id => Record.Id;
    public string Filename => Record.Filename;
    public string Dimensions => $"{Record.Width}×{Record.Height}";
    public string SizeText => Format.Bytes(Record.FileSize);
    public bool HasOcr => !string.IsNullOrWhiteSpace(Record.OcrText);
    public bool HasAi => Record.AiCaption != null && Live is null;
    public string AiText => Record.AiCaption?.Text ?? "";
    public Brush Swatch => Format.Brush(Record.DominantColors.FirstOrDefault()?.Hex ?? "#808080");
    public string ListLine => Live != null ? $"{Live.Label}... {Live.Text}"
        : Record.AiCaption?.Text.ReplaceLineEndings(" ") ?? (HasOcr ? Record.OcrText.ReplaceLineEndings(" ") : Record.Path);

    bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

    LiveDescribe? _live;
    public LiveDescribe? Live
    {
        get => _live;
        set
        {
            if (!Set(ref _live, value)) return;
            Raise(nameof(IsProcessing));
            Raise(nameof(LivePercent));
            Raise(nameof(HasAi));
            Raise(nameof(ListLine));
        }
    }

    public bool IsProcessing => Live != null;
    public string LivePercent => Live is null ? "" : $"✨ {Math.Round(Live.Fraction * 100)}%";

    /// <summary>Call after the record's caption changed.</summary>
    public void CaptionChanged()
    {
        Raise(nameof(HasAi));
        Raise(nameof(AiText));
        Raise(nameof(ListLine));
    }

    BitmapSource? _thumb;
    bool _loading;

    /// <summary>Decoded off the UI thread at display size, on first use.</summary>
    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumb != null || _loading) return _thumb;
            if (Thumbnails.TryGet(Record.Path, thumbWidth, out var cached)) return _thumb = cached;
            _loading = true;
            var path = Record.Path;
            Task.Run(() => Thumbnails.Load(path, thumbWidth)).ContinueWith(t =>
            {
                _thumb = t.Result;
                Raise(nameof(Thumbnail));
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return null;
        }
    }
}

/// <summary>A row of items: WPF has no virtualizing wrap panel, so the grid is
/// a virtualized list of rows, rebuilt when the width or thumb size changes.</summary>
public sealed class ImageRow(IReadOnlyList<ImageItem> items)
{
    public IReadOnlyList<ImageItem> Items { get; } = items;
}

/// <summary>Small LRU of decoded thumbnails, so scrolling back is instant.</summary>
public static class Thumbnails
{
    const int Capacity = 800;
    static readonly Dictionary<(string, int), LinkedListNode<((string, int) Key, BitmapSource Image)>> Map = [];
    static readonly LinkedList<((string, int) Key, BitmapSource Image)> Order = new();
    static readonly Lock Gate = new();

    public static bool TryGet(string path, int width, out BitmapSource? image)
    {
        lock (Gate)
        {
            if (Map.TryGetValue((path, width), out var node))
            {
                Order.Remove(node);
                Order.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }
        image = null;
        return false;
    }

    public static BitmapSource? Load(string path, int width)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = width;
            bmp.CacheOption = BitmapCacheOption.OnLoad; // read now, don't keep the file open
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze(); // usable from the UI thread
            lock (Gate)
            {
                var key = (path, width);
                if (Map.TryGetValue(key, out var old)) Order.Remove(old);
                Map[key] = Order.AddFirst((key, bmp));
                while (Map.Count > Capacity)
                {
                    Map.Remove(Order.Last!.Value.Key);
                    Order.RemoveLast();
                }
            }
            return bmp;
        }
        catch
        {
            return null; // the card shows the image's main colour instead
        }
    }
}

public static class Format
{
    public static string Bytes(double bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return $"{bytes.ToString(bytes < 10 && i > 0 ? "0.0" : "0")} {units[i]}";
    }

    public static Brush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

public sealed class ColorChip(HexColor c)
{
    public string Hex => c.Hex;
    public string Label => $"{c.Hex}  {c.Name} {c.Pct}%";
    public Brush Brush => Format.Brush(c.Hex);
}

public sealed class ObservableList<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
    }
}

public static class Ui
{
    /// <summary>Runs on the UI thread (callbacks from llama.cpp's reader threads).</summary>
    public static void Post(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
}
