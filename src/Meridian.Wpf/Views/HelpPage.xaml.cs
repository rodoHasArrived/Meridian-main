using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Help page with tutorials, documentation, and support links.
/// </summary>
public partial class HelpPage : Page
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;

    public HelpPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _notificationService = notificationService;
    }

    private void StartTutorial_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to setup wizard
        _navigationService.NavigateTo("SetupWizard");
    }

    private void OpenArchitectureDoc_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/blob/main/docs/architecture/overview.md");
    }

    private void OpenConfigurationDoc_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/blob/main/docs/guides/configuration.md");
    }

    private void OpenApiDoc_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/blob/main/docs/api/");
    }

    private void OpenLeanIntegrationDoc_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/blob/main/docs/integrations/lean-integration.md");
    }

    private void OpenIBSetupDoc_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/blob/main/docs/providers/");
    }

    private void OpenOperatorRunbookDoc_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/blob/main/docs/guides/operator-runbook.md");
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/issues");
    }

    private void OpenDiscord_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://discord.gg/example");
    }

    private void OpenEmail_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("mailto:support@example.com");
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            _notificationService.ShowNotification(
                "Error",
                "Could not open the link. Please try again.",
                NotificationType.Error);
        }
    }
}
