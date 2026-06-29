using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for viewing and managing backfill provider settings.
/// Code-behind is thin lifecycle wiring only; all state and orchestration live in ProviderViewModel.
/// </summary>
public partial class ProviderPage : Page
{
    private readonly ProviderViewModel _viewModel;

    public ProviderPage(ProviderViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.StartAsync(CancellationToken.None);
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Provider page failed to load.", ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Stop();
    }
}

