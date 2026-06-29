using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class ServiceManagerPage : Page
{
    private readonly ServiceManagerViewModel _viewModel;

    public ServiceManagerPage(
        BackendServiceManager serviceManager,
        LoggingService loggingService)
    {
        _viewModel = new ServiceManagerViewModel(serviceManager, loggingService);
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
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Service Manager page failed to load.", ex);
        }
    }
}
