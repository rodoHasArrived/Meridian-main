using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Strategy workspace surface for editing and executing RunMat scripts.
/// </summary>
public partial class RunMatPage : Page
{
    private readonly RunMatViewModel _viewModel;

    public RunMatPage(RunMatViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Run Mat page failed to load.", ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }
}
