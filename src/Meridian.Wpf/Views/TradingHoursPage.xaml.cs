using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Trading hours page — thin code-behind.
/// All market-status logic, timezone handling, and holiday data loading live in
/// <see cref="TradingHoursViewModel"/>.
/// </summary>
public partial class TradingHoursPage : Page
{
    private readonly TradingHoursViewModel _viewModel;

    public TradingHoursPage()
    {
        InitializeComponent();

        _viewModel = new TradingHoursViewModel(ApiClientService.Instance);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadAsync();
}
