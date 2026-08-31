using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Options chain page for viewing option expirations, strikes, greeks,
/// and chain data for tracked underlying symbols.
/// </summary>
public partial class OptionsPage : Page
{
    private readonly OptionsViewModel _viewModel;

    public OptionsPage()
        : this(ApiClientService.Instance)
    {
    }

    public OptionsPage(ApiClientService apiClientService)
    {
        ArgumentNullException.ThrowIfNull(apiClientService);
        InitializeComponent();
        _viewModel = new OptionsViewModel(WpfServices.LoggingService.Instance, apiClientService);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadAllAsync();
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
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Options page failed to load.", ex);
        }
    }

    private async void LoadExpirations_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadExpirationsAsync();

    private void SymbolInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = _viewModel.LoadExpirationsAsync();
        }
    }

    private async void UnderlyingsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string symbol)
            await _viewModel.SelectUnderlyingAsync(symbol);
    }

    private async void ExpirationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string expiration)
            await _viewModel.SelectExpirationAsync(expiration);
    }

    private async void RefreshData_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.StopAsync();
        }
        catch (Exception ex)
        {
            WpfServices.LoggingService.Instance.LogError(
                "Options page failed to stop in-flight loads during navigation.",
                ex);
        }
    }
}
