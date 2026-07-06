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
        : this(TryResolveOperatingContextService())
    {
    }

    public PositionBlotterPage(WorkstationOperatingContextService? operatingContextService)
    {
        InitializeComponent();
        _viewModel = new PositionBlotterViewModel(
            ApiClientService.Instance,
            Services.NavigationService.Instance,
            operatingContextService);
        DataContext = _viewModel;
    }

    private static WorkstationOperatingContextService? TryResolveOperatingContextService()
    {
        try
        {
            return App.Services?.GetService(typeof(WorkstationOperatingContextService)) as WorkstationOperatingContextService;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
