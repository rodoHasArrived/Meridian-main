using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Contracts.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Diagnostics page — thin code-behind.
/// All diagnostic checks, system info, and output generation live in
/// <see cref="DiagnosticsPageViewModel"/>. This file handles only visual
/// concerns that require WPF resource lookups (status dot colours).
/// </summary>
public partial class DiagnosticsPage : Page
{
    private readonly WpfServices.NotificationService _notificationService;
    private readonly DiagnosticsPageViewModel _viewModel;

    public DiagnosticsPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        IConnectivityProbeService? connectivityProbe = null,
        ICoLocationProfileActivator? coLocationProfileActivator = null,
        ProviderLatencyService? latencyService = null)
    {
        InitializeComponent();

        _notificationService = notificationService;
        _viewModel = new DiagnosticsPageViewModel(connectivityProbe, coLocationProfileActivator, latencyService);
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Bind text-block values from ViewModel (one-time population of system info fields)
        RuntimeVersionText.Text = _viewModel.RuntimeVersion;
        OsVersionText.Text = _viewModel.OsVersion;
        WorkingDirectoryText.Text = _viewModel.WorkingDirectory;
        ProcessIdText.Text = _viewModel.ProcessId;
        MemoryUsageText.Text = _viewModel.MemoryUsage;

        UpdateConnectivityStatusDot();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(DiagnosticsPageViewModel.IsOnline))
            UpdateConnectivityStatusDot();

        if (eventArgs.PropertyName == nameof(DiagnosticsPageViewModel.ConfigStatus))
            SetStatusIndicator(ConfigStatusDot, ConfigStatusText, _viewModel.ConfigStatus);

        if (eventArgs.PropertyName == nameof(DiagnosticsPageViewModel.StorageStatus))
            SetStatusIndicator(StorageStatusDot, StorageStatusText, _viewModel.StorageStatus);

        if (eventArgs.PropertyName == nameof(DiagnosticsPageViewModel.ApiStatus))
            SetStatusIndicator(ApiStatusDot, ApiStatusText, _viewModel.ApiStatus);

        if (eventArgs.PropertyName == nameof(DiagnosticsPageViewModel.ProviderStatus))
            SetStatusIndicator(ProviderStatusDot, ProviderStatusText, _viewModel.ProviderStatus);
    }

    private void UpdateConnectivityStatusDot()
    {
        ConnectivityStatusDot.Fill = _viewModel.IsOnline == true
            ? (Brush)FindResource("SuccessColorBrush")
            : _viewModel.IsOnline == false
                ? (Brush)FindResource("ErrorColorBrush")
                : (Brush)FindResource("WarningColorBrush");
    }

    private void ExportBundle_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.NotifyInfo(
            "Export Diagnostic Bundle",
            "Diagnostic bundle export is not yet implemented.");
    }

    private void RunQuickCheck_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RunQuickCheckCommand.Execute(null);

    private void RunFullDiagnostics_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RunFullDiagnosticsCommand.Execute(null);

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e) =>
        _viewModel.OpenLogsFolderCommand.Execute(null);

    /// <summary>
    /// Updates a status dot and label based on a <see cref="DiagnosticCheckResult"/>.
    /// This remains in code-behind because it requires WPF <c>FindResource</c> calls.
    /// </summary>
    private void SetStatusIndicator(Ellipse dot, TextBlock text, DiagnosticCheckResult result)
    {
        if (result.Success == true)
        {
            dot.Fill = (Brush)FindResource("SuccessColorBrush");
            text.Foreground = (Brush)FindResource("SuccessColorBrush");
        }
        else if (result.Success == false)
        {
            dot.Fill = (Brush)FindResource("ErrorColorBrush");
            text.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
        else
        {
            dot.Fill = (Brush)FindResource("WarningColorBrush");
            text.Foreground = (Brush)FindResource("WarningColorBrush");
        }

        text.Text = result.Message;
    }
}
