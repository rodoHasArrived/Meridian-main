using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Order book page — thin code-behind.
/// All state, HTTP polling, and connection-status tracking live in <see cref="OrderBookViewModel"/>.
/// The PropertyChanged relay for BidBarColumn/AskBarColumn is kept here because
/// <see cref="ColumnDefinition.Width"/> does not support data binding in WPF.
/// </summary>
public partial class OrderBookPage : Page
{
    private readonly OrderBookViewModel _viewModel;

    public OrderBookPage(
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.LoggingService loggingService)
    {
        _viewModel = new OrderBookViewModel(statusService, connectionService, loggingService);

        InitializeComponent();
        DataContext = _viewModel;

        // ColumnDefinition.Width does not support data binding — relay via PropertyChanged.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ActivateAsync();
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
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Order Book page failed to load.", ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Deactivate();
        Unloaded -= OnPageUnloaded;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrderBookViewModel.BidBarWidth) or nameof(OrderBookViewModel.AskBarWidth))
        {
            BidBarColumn.Width = _viewModel.BidBarWidth;
            AskBarColumn.Width = _viewModel.AskBarWidth;
        }
    }

}
