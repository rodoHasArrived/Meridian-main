using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class DirectLendingPage : Page
{
    private readonly DirectLendingViewModel _viewModel;

    public DirectLendingPage()
    {
        InitializeComponent();
        _viewModel = new DirectLendingViewModel(ApiClientService.Instance);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            Loaded -= OnPageLoaded;
            await _viewModel.InitializeAsync();
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Direct Lending page failed to load.", ex);
        }
    }
}
