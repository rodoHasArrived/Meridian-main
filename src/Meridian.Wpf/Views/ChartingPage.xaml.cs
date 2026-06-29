using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class ChartingPage : Page
{
    private readonly ChartingPageViewModel _viewModel;

    public ChartingPage(ChartingPageViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
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
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Charting page failed to load.", ex);
        }
    }
}
