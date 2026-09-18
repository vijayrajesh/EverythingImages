using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using EverythingImages.Core;

namespace EverythingImages;

public partial class MainWindow : Window
{
    readonly AppState _s = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _s;
        Loaded += async (_, _) => await _s.InitAsync();
        PreviewKeyDown += OnKey;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.OemQuestion && !SearchBox.IsKeyboardFocused && Keyboard.FocusedElement is not TextBox)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_s.Query.Length > 0) _s.Query = "";
            else _s.Select(null);
        }
    }

    void Card_Click(object sender, RoutedEventArgs e) => _s.Select((ImageItem)((FrameworkElement)sender).DataContext);
    void Card_DoubleClick(object sender, MouseButtonEventArgs e) => _s.OpenFile(((ImageItem)((FrameworkElement)sender).DataContext).Record.Path);

    void ListList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListList.SelectedItem is ImageItem it) _s.Select(it);
    }

    void ListList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListList.SelectedItem is ImageItem it) _s.OpenFile(it.Record.Path);
    }

    void GridList_SizeChanged(object sender, SizeChangedEventArgs e) => _s.SetGridWidth(e.NewSize.Width - SystemParameters.VerticalScrollBarWidth);

    void GridView_Checked(object sender, RoutedEventArgs e) => SetView(grid: true);
    void ListView_Checked(object sender, RoutedEventArgs e) => SetView(grid: false);

    void SetView(bool grid)
    {
        if (GridList is null) return; // fires during InitializeComponent
        GridList.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        ListList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        SizePanel.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
    }

    void Quick_Checked(object sender, RoutedEventArgs e) => _s.Quick = Enum.Parse<QuickFilter>((string)((FrameworkElement)sender).Tag);
    void Size_Checked(object sender, RoutedEventArgs e) => _s.Size = Enum.Parse<AppState.ThumbSize>((string)((FrameworkElement)sender).Tag);

    void Folders_Click(object sender, RoutedEventArgs e) => new FoldersWindow(_s) { Owner = this }.ShowDialog();
    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_s.Index.Folders.Count == 0) Folders_Click(sender, e);
        else await _s.StartScanAsync();
    }

    async void DescribeAll_Click(object sender, RoutedEventArgs e)
    {
        if (_s.ModelInUse is null) Options_Click(sender, e);
        else await _s.DescribeAllAsync();
    }

    void Options_Click(object sender, RoutedEventArgs e)
    {
        _ = _s.OnOptionsOpenedAsync();
        new OptionsWindow(_s) { Owner = this }.ShowDialog();
    }

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(_s) { Owner = this }.ShowDialog();

    void StopBanner_Click(object sender, RoutedEventArgs e) => _s.StopBanner();
    void CloseInspector_Click(object sender, RoutedEventArgs e) => _s.Select(null);
    void OpenSelected_Click(object sender, RoutedEventArgs e) { if (_s.Selected != null) _s.OpenFile(_s.Selected.Record.Path); }
    void RevealSelected_Click(object sender, RoutedEventArgs e) { if (_s.Selected != null) _s.ShowInFolder(_s.Selected.Record.Path); }
    async void DescribeSelected_Click(object sender, RoutedEventArgs e) => await _s.DescribeSelectedAsync();

    void CopyColor_Click(object sender, RoutedEventArgs e)
    {
        var hex = (string)((FrameworkElement)sender).Tag;
        if (Copy(hex)) _s.Say($"Copied {hex}");
    }

    void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_s.Selected?.Record.OcrText)) return;
        if (Copy(_s.Selected.Record.OcrText)) _s.Say("Text copied");
    }

    /// <summary>Another program can hold the clipboard open - clipboard managers
    /// and remote desktop do it often - and an unlucky moment would otherwise
    /// close the app. Saying so and carrying on is the right answer.</summary>
    bool Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            _s.Say("Another program is using the clipboard. Try again.");
            return false;
        }
    }

    // Empty-state wording, chosen by whether any folder is set up.
    public static readonly IValueConverter EmptyTitle = new Choose("Add a folder to get started", "No matching images");
    public static readonly IValueConverter EmptyHint = new Choose(
        "EverythingImages scans it right away: colours, text in images, and (optionally) AI descriptions.",
        "Try another search or filter.");

    sealed class Choose(string whenTrue, string whenFalse) : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? whenTrue : whenFalse;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }
}
