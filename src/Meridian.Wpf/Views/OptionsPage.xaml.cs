using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Contracts.Api;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Options chain page for viewing option expirations, strikes, greeks,
/// and chain data for tracked underlying symbols.
/// </summary>
public partial class OptionsPage : Page
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly UiApiClient? _apiClient;
    private readonly ObservableCollection<string> _underlyings = new();
    private readonly ObservableCollection<string> _expirations = new();
    private string? _selectedUnderlying;

    public OptionsPage()
    {
        InitializeComponent();

        _loggingService = WpfServices.LoggingService.Instance;

        // Attempt to resolve the API client from the service locator
        try
        {
            _apiClient = Ui.Services.ApiClientService.Instance?.UiApi;
        }
        catch
        {
            // API client not configured yet — page will show appropriate status
        }

        UnderlyingsList.ItemsSource = _underlyings;
        ExpirationsList.ItemsSource = _expirations;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadSummaryAsync();
        await LoadTrackedUnderlyingsAsync();
    }

    private async System.Threading.Tasks.Task LoadSummaryAsync()
    {
        if (_apiClient is null)
        {
            UpdateProviderStatus(false, "API client not configured");
            return;
        }

        try
        {
            var summary = await _apiClient.GetOptionsSummaryAsync();
            if (summary is not null)
            {
                TrackedContractsText.Text = summary.TrackedContracts.ToString("N0");
                TrackedChainsText.Text = summary.TrackedChains.ToString("N0");
                TrackedUnderlyingsText.Text = summary.TrackedUnderlyings.ToString("N0");
                WithGreeksText.Text = summary.ContractsWithGreeks.ToString("N0");
                UpdateProviderStatus(summary.ProviderAvailable, summary.ProviderAvailable ? "Provider connected" : "No provider configured");
            }
            else
            {
                UpdateProviderStatus(false, "Unable to retrieve summary");
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load options summary", ex);
            UpdateProviderStatus(false, $"Error: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task LoadTrackedUnderlyingsAsync()
    {
        if (_apiClient is null) return;

        try
        {
            var underlyings = await _apiClient.GetOptionsTrackedUnderlyingsAsync();
            _underlyings.Clear();
            if (underlyings is not null)
            {
                foreach (var symbol in underlyings)
                    _underlyings.Add(symbol);
            }

            NoUnderlyingsText.Visibility = _underlyings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load tracked underlyings", ex);
        }
    }

    private async void LoadExpirations_Click(object sender, RoutedEventArgs e)
    {
        var symbol = SymbolInput.Text?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            ShowStatus("Please enter an underlying symbol.", false);
            return;
        }

        await LoadExpirationsForSymbolAsync(symbol);
    }

    private void SymbolInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            LoadExpirations_Click(sender, e);
        }
    }

    private async void UnderlyingsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnderlyingsList.SelectedItem is string symbol)
        {
            SymbolInput.Text = symbol;
            await LoadExpirationsForSymbolAsync(symbol);
        }
    }

    private async System.Threading.Tasks.Task LoadExpirationsForSymbolAsync(string symbol)
    {
        if (_apiClient is null)
        {
            ShowStatus("API client not configured.", false);
            return;
        }

        _selectedUnderlying = symbol;
        ShowStatus($"Loading expirations for {symbol}...", true);

        try
        {
            var response = await _apiClient.GetOptionsExpirationsAsync(symbol);
            _expirations.Clear();

            if (response is not null && response.Expirations.Count > 0)
            {
                foreach (var exp in response.Expirations)
                    _expirations.Add(exp.ToString("yyyy-MM-dd"));

                ExpirationsHeader.Text = $"Expirations for {symbol} ({response.Count})";
                ExpirationsPanel.Visibility = Visibility.Visible;
                HideStatus();
            }
            else
            {
                ExpirationsPanel.Visibility = Visibility.Collapsed;
                ShowStatus($"No expirations found for {symbol}.", false);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load expirations for " + symbol, ex);
            ShowStatus($"Failed to load expirations: {ex.Message}", false);
        }
    }

    private async void ExpirationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExpirationsList.SelectedItem is string expirationStr && _selectedUnderlying is not null)
        {
            await LoadChainAsync(_selectedUnderlying, expirationStr);
        }
    }

    private async System.Threading.Tasks.Task LoadChainAsync(string symbol, string expiration)
    {
        if (_apiClient is null) return;

        ShowStatus($"Loading chain for {symbol} {expiration}...", true);

        try
        {
            var chain = await _apiClient.GetOptionsChainAsync(symbol, expiration);
            if (chain is not null)
            {
                ChainHeader.Text = $"Option Chain: {symbol} {expiration}";
                ChainUnderlyingPrice.Text = $"Underlying: ${chain.UnderlyingPrice:N2}";
                ChainDTE.Text = $"DTE: {chain.DaysToExpiration}";
                ChainPCRatio.Text = chain.PutCallVolumeRatio.HasValue
                    ? $"P/C Ratio: {chain.PutCallVolumeRatio:N2}"
                    : string.Empty;

                CallsList.ItemsSource = chain.Calls;
                PutsList.ItemsSource = chain.Puts;

                ChainPanel.Visibility = Visibility.Visible;
                HideStatus();
            }
            else
            {
                ShowStatus($"No chain data available for {symbol} {expiration}.", false);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load chain", ex, ("Symbol", symbol), ("Expiration", expiration));
            ShowStatus($"Failed to load chain: {ex.Message}", false);
        }
    }

    private async void RefreshData_Click(object sender, RoutedEventArgs e)
    {
        await LoadSummaryAsync();
        await LoadTrackedUnderlyingsAsync();

        if (_selectedUnderlying is not null)
        {
            await LoadExpirationsForSymbolAsync(_selectedUnderlying);
        }
    }

    private void UpdateProviderStatus(bool available, string message)
    {
        ProviderStatusDot.Fill = available
            ? (System.Windows.Media.Brush)FindResource("SuccessColorBrush")
            : (System.Windows.Media.Brush)FindResource("ConsoleTextMutedBrush");
        ProviderStatusText.Text = message;
    }

    private void ShowStatus(string message, bool showProgress)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = message;
        LoadingProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideStatus()
    {
        StatusPanel.Visibility = Visibility.Collapsed;
    }
}
