using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Welcome page.
/// Owns overview data loading and navigation commands so that the code-behind is
/// thinned to constructor DI and lifecycle wiring only.
/// </summary>
public sealed class WelcomePageViewModel : BindableBase
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.StatusService _statusService;
    private readonly WpfServices.ConnectionService _connectionService;
    private readonly WpfServices.ConfigService _configService;

    // ── System overview properties ────────────────────────────────────────────────────

    private string _connectionStatusText = "Disconnected";
    public string ConnectionStatusText { get => _connectionStatusText; private set => SetProperty(ref _connectionStatusText, value); }

    private Brush _connectionStatusDotFill = Brushes.Gray;
    public Brush ConnectionStatusDotFill { get => _connectionStatusDotFill; private set => SetProperty(ref _connectionStatusDotFill, value); }

    private string _connectionProviderText = "No provider connected";
    public string ConnectionProviderText { get => _connectionProviderText; private set => SetProperty(ref _connectionProviderText, value); }

    private string _symbolsCountText = "0";
    public string SymbolsCountText { get => _symbolsCountText; private set => SetProperty(ref _symbolsCountText, value); }

    private string _storagePathText = "./data";
    public string StoragePathText { get => _storagePathText; private set => SetProperty(ref _storagePathText, value); }

    // ── Navigation commands ───────────────────────────────────────────────────────────
    public IRelayCommand NavigateToProviderCommand { get; }
    public IRelayCommand NavigateToSymbolsCommand { get; }
    public IRelayCommand NavigateToStorageCommand { get; }
    public IRelayCommand NavigateToDashboardCommand { get; }
    public IRelayCommand NavigateToDataQualityCommand { get; }
    public IRelayCommand OpenDocumentationCommand { get; }

    public WelcomePageViewModel(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.ConfigService configService)
    {
        _navigationService = navigationService;
        _notificationService = notificationService;
        _statusService = statusService;
        _connectionService = connectionService;
        _configService = configService;

        NavigateToProviderCommand     = new RelayCommand(() => _navigationService.NavigateTo("Provider"));
        NavigateToSymbolsCommand      = new RelayCommand(() => _navigationService.NavigateTo("Symbols"));
        NavigateToStorageCommand      = new RelayCommand(() => _navigationService.NavigateTo("Storage"));
        NavigateToDashboardCommand    = new RelayCommand(() => _navigationService.NavigateTo("Dashboard"));
        NavigateToDataQualityCommand  = new RelayCommand(() => _navigationService.NavigateTo("DataQuality"));
        OpenDocumentationCommand      = new RelayCommand(OpenDocumentation);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        await UpdateSystemOverviewAsync();
    }

    // ── Data loading ──────────────────────────────────────────────────────────────────

    private async Task UpdateSystemOverviewAsync()
    {
        var isConnected = _connectionService.State == ConnectionState.Connected;
        var successBrush = GetResource("SuccessColorBrush", Brushes.LimeGreen);
        var mutedBrush   = GetResource("ConsoleTextMutedBrush", Brushes.Gray);

        if (isConnected)
        {
            ConnectionStatusText = "Connected";
            ConnectionStatusDotFill = successBrush;

            try
            {
                var providerInfo = await _statusService.GetProviderStatusAsync();
                ConnectionProviderText = providerInfo?.ActiveProvider is { Length: > 0 }
                    ? providerInfo.ActiveProvider
                    : "Provider connected";
            }
            catch (Exception)
            {
                ConnectionProviderText = "Provider connected";
            }
        }
        else
        {
            ConnectionStatusText = "Disconnected";
            ConnectionStatusDotFill = mutedBrush;
            ConnectionProviderText = "No provider connected";
        }

        try
        {
            var symbols = await _configService.GetSymbolsAsync();
            SymbolsCountText = (symbols?.Length ?? 0).ToString();
        }
        catch (Exception)
        {
            SymbolsCountText = "0";
        }

        try
        {
            var config = await _configService.LoadConfigAsync();
            StoragePathText = config?.DataRoot ?? "./data";
        }
        catch (Exception)
        {
            StoragePathText = "./data";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private void OpenDocumentation()
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

    private static Brush GetResource(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
