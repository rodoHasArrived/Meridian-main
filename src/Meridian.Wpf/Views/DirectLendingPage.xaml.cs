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
        Loaded -= OnPageLoaded;
        await _viewModel.InitializeAsync();
    }
}
