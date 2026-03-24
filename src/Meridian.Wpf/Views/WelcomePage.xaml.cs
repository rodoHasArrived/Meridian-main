using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Meridian.Ui.Services.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Welcome page with project branding, quick-start steps, system overview, and recent features.
/// </summary>
public partial class WelcomePage : Page
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.ConfigService _configService;

    public WelcomePage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.ConfigService configService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _notificationService = notificationService;
        _statusService = statusService;
        _connectionService = connectionService;
        _configService = configService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await UpdateSystemOverviewAsync();
    }

    private async System.Threading.Tasks.Task UpdateSystemOverviewAsync()
    {
        // Connection status from ConnectionService
        var isConnected = _connectionService.State == ConnectionState.Connected;
        if (isConnected)
        {
            ConnectionStatusText.Text = "Connected";
            ConnectionStatusDot.Fill = (Brush)FindResource("SuccessColorBrush");

            // Try to get the active provider name from StatusService
            try
            {
                var providerInfo = await _statusService.GetProviderStatusAsync();
                ConnectionProviderText.Text = providerInfo?.ActiveProvider is { Length: > 0 }
                    ? providerInfo.ActiveProvider
                    : "Provider connected";
            }
            catch (Exception)
            {
                ConnectionProviderText.Text = "Provider connected";
            }
        }
        else
        {
            ConnectionStatusText.Text = "Disconnected";
            ConnectionStatusDot.Fill = (Brush)FindResource("ConsoleTextMutedBrush");
            ConnectionProviderText.Text = "No provider connected";
        }

        // Symbol count from ConfigService
        try
        {
            var symbols = await _configService.GetSymbolsAsync();
            SymbolsCountText.Text = (symbols?.Length ?? 0).ToString();
        }
        catch (Exception)
        {
            SymbolsCountText.Text = "0";
        }

        // Storage path from ConfigService
        try
        {
            var config = await _configService.LoadConfigAsync();
            StoragePathText.Text = config?.DataRoot ?? "./data";
        }
        catch (Exception)
        {
            StoragePathText.Text = "./data";
        }
    }

    // -- Quick-start step card click handlers (Border.MouseLeftButtonUp) --

    private void StepProvider_CardClick(object sender, MouseButtonEventArgs e)
    {
        _navigationService.NavigateTo("Provider");
    }

    private void StepSymbols_CardClick(object sender, MouseButtonEventArgs e)
    {
        _navigationService.NavigateTo("Symbols");
    }

    private void StepStorage_CardClick(object sender, MouseButtonEventArgs e)
    {
        _navigationService.NavigateTo("Storage");
    }

    private void StepDashboard_CardClick(object sender, MouseButtonEventArgs e)
    {
        _navigationService.NavigateTo("Dashboard");
    }

    private void StepDataQuality_CardClick(object sender, MouseButtonEventArgs e)
    {
        _navigationService.NavigateTo("DataQuality");
    }

    // -- Button click handlers (Button.Click) --

    private void StepProvider_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Provider");
    }

    private void StepSymbols_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Symbols");
    }

    private void StepStorage_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Storage");
    }

    private void StepDashboard_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Dashboard");
    }

    private void StepDataQuality_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("DataQuality");
    }

    private void OpenDocumentation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/example/meridian",
                UseShellExecute = true
            });
        }
        catch
        {
            _notificationService.ShowNotification(
                "Error",
                "Could not open the documentation link. Please try again.",
                NotificationType.Error);
        }
    }
}
