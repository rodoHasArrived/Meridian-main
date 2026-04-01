using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class PositionBlotterPage : Page
{
    private readonly PositionBlotterViewModel _viewModel;

    public PositionBlotterPage()
    {
        InitializeComponent();
        _viewModel = new PositionBlotterViewModel(
            ApiClientService.Instance,
            NavigationService.Instance);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (OperationCanceledException) { }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Dispose();
}
