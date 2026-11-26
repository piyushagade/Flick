using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flick.App.Views;

public sealed partial class AboutPage : Page
{
    private const string WebsiteUrl = "https://app.piyushagade.xyz/flick";
    private readonly string _configFolder;

    public AboutPage()
    {
        InitializeComponent();
        _configFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flick");
        ConfigFolderText.Text = _configFolder;
    }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(WebsiteUrl) { UseShellExecute = true });
        }
        catch
        {
            // Ignore launch failures
        }
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_configFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", _configFolder) { UseShellExecute = true });
        }
        catch
        {
            // Ignore launch failures
        }
    }
}
