using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace EverythingImages;

public partial class AboutWindow : Window
{
    public const string RepoUrl = "https://github.com/vijayrajesh/EverythingImages";

    readonly AppState _s;

    public AboutWindow(AppState state)
    {
        _s = state;
        InitializeComponent();
        VersionText.Text = $"Version {Version}";
        TamilText.Text = Signature.Tamil;
        // Segoe UI has no Tamil glyphs; WPF falls back along this list.
        TamilText.FontFamily = new FontFamily("Nirmala UI, Latha, Segoe UI");
        MeaningText.Text = Signature.Meaning;
        CreditText.Text = Signature.Credit;
        SiteButton.Content = Signature.Site;
    }

    /// <summary>"0.1.0" from the csproj's Version, without the "+commit" the SDK appends.</summary>
    static string Version =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?")
        .Split('+')[0];

    void Site_Click(object sender, RoutedEventArgs e) => _s.OpenUrl(Signature.SiteUrl);
    void GitHub_Click(object sender, RoutedEventArgs e) => _s.OpenUrl(RepoUrl);
    void Issues_Click(object sender, RoutedEventArgs e) => _s.OpenUrl(RepoUrl + "/issues");
    void License_Click(object sender, RoutedEventArgs e) => _s.OpenUrl(RepoUrl + "/blob/main/LICENSE");
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
