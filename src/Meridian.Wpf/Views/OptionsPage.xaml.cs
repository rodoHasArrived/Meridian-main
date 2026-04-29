using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Contracts.Api;
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
    {
        InitializeComponent();

        UiApiClient? apiClient = null;
        try
        { apiClient = Ui.Services.ApiClientService.Instance?.UiApi; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OptionsPage] Failed to resolve API client: {ex.Message}");
        }

        _viewModel = new OptionsViewModel(WpfServices.LoggingService.Instance, apiClient);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadAllAsync();

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
}
