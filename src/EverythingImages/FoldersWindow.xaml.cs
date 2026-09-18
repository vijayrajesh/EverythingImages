using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace EverythingImages;

public partial class FoldersWindow : Window
{
    readonly AppState _s;

    public FoldersWindow(AppState state)
    {
        _s = state;
        InitializeComponent();
        Refresh();
    }

    void Refresh()
    {
        FolderList.ItemsSource = _s.Index.Folders.ToList();
        NoneText.Visibility = _s.Index.Folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var busy = _s.Scanning || _s.AiRunning;
        ScanButton.IsEnabled = !busy && _s.Index.Folders.Count > 0;
        ResetButton.IsEnabled = !busy;
    }

    async Task AddAsync(string folder)
    {
        var task = _s.AddFolderAsync(folder); // scans right away
        Refresh();
        await task;
        Refresh();
    }

    async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Add a folder to EverythingImages" };
        if (dlg.ShowDialog(this) == true) await AddAsync(dlg.FolderName);
    }

    async void AddTyped_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim().Trim('"');
        if (path.Length == 0) return;
        if (!System.IO.Directory.Exists(path))
        {
            _s.Say($"{path} is not a folder on this PC.");
            return;
        }
        PathBox.Clear();
        await AddAsync(path);
    }

    void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTyped_Click(sender, e);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        _s.RemoveFolder((string)((FrameworkElement)sender).Tag);
        Refresh();
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var task = _s.StartScanAsync();
        Refresh();
        await task;
        Refresh();
    }

    async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var ok = MessageBox.Show(this,
            $"Remove all {_s.Index.Count} indexed images, including their AI descriptions?\n\nYour folders are kept, and no image files are touched.",
            "Reset the index", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.OK) return;
        _s.ResetIndex();
        await _s.StartScanAsync();
        Refresh();
    }

    void Done_Click(object sender, RoutedEventArgs e) => Close();
}
