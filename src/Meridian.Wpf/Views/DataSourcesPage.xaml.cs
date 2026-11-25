using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.ProviderDiagnostics;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for editing host data source settings and reviewing activation.
/// </summary>
public partial class DataSourcesPage : Page
{
    private readonly DataSourcesViewModel _viewModel;
    private CancellationTokenSource? _load;

    public DataSourcesPage(
        ProviderConfigurationApiClient client,
        WpfServices.FirstRunService firstRunService,
        ILogger<DataSourcesViewModel> logger)
    {
        InitializeComponent();
        _viewModel = new DataSourcesViewModel(client, firstRunService, logger);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _load ??= new CancellationTokenSource();
        try
        {
            await _viewModel.LoadAsync(_load.Token);
        }
        catch (System.OperationCanceledException)
        {
            // Navigation cancelled the in-flight load before it completed; benign during teardown.
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "Page load cancelled during navigation.",
                ("page", GetType().Name));
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Data Sources page failed to load.", ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
        _viewModel.Stop();
    }
}
