using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using Meridian.Contracts.Api;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Options Chain page.
/// Owns all collections, status strings, visibility flags, and async data loads.
/// Code-behind delegates every action and event here.
/// </summary>
public sealed class OptionsViewModel : BindableBase
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly UiApiClient? _apiClient;

    // ── Summary stats ────────────────────────────────────────────────────
    private string _trackedContracts = "--";
    public string TrackedContracts { get => _trackedContracts; private set => SetProperty(ref _trackedContracts, value); }

    private string _trackedChains = "--";
    public string TrackedChains { get => _trackedChains; private set => SetProperty(ref _trackedChains, value); }

    private string _trackedUnderlyings = "--";
    public string TrackedUnderlyings { get => _trackedUnderlyings; private set => SetProperty(ref _trackedUnderlyings, value); }

    private string _withGreeks = "--";
    public string WithGreeks { get => _withGreeks; private set => SetProperty(ref _withGreeks, value); }

    // ── Provider status ──────────────────────────────────────────────────
    private bool _isProviderAvailable;
    public bool IsProviderAvailable { get => _isProviderAvailable; private set => SetProperty(ref _isProviderAvailable, value); }

    private string _providerStatusText = "Provider status unknown";
    public string ProviderStatusText { get => _providerStatusText; private set => SetProperty(ref _providerStatusText, value); }

    // ── Collections ──────────────────────────────────────────────────────
    public ObservableCollection<string> Underlyings { get; } = new();
    public ObservableCollection<string> Expirations { get; } = new();

    private IEnumerable? _calls;
    public IEnumerable? Calls { get => _calls; private set => SetProperty(ref _calls, value); }

    private IEnumerable? _puts;
    public IEnumerable? Puts { get => _puts; private set => SetProperty(ref _puts, value); }

    // ── Underlyings lookup state ─────────────────────────────────────────
    private string _symbolInputText = string.Empty;
    public string SymbolInputText { get => _symbolInputText; set => SetProperty(ref _symbolInputText, value); }

    private bool _noUnderlyingsVisible = true;
    public bool NoUnderlyingsVisible { get => _noUnderlyingsVisible; private set => SetProperty(ref _noUnderlyingsVisible, value); }

    // ── Expirations panel ────────────────────────────────────────────────
    private bool _expirationsPanelVisible;
    public bool ExpirationsPanelVisible { get => _expirationsPanelVisible; private set => SetProperty(ref _expirationsPanelVisible, value); }

    private string _expirationsHeader = "Expirations";
    public string ExpirationsHeader { get => _expirationsHeader; private set => SetProperty(ref _expirationsHeader, value); }

    // ── Chain panel ──────────────────────────────────────────────────────
    private bool _chainPanelVisible;
    public bool ChainPanelVisible { get => _chainPanelVisible; private set => SetProperty(ref _chainPanelVisible, value); }

    private string _chainHeader = "Option Chain";
    public string ChainHeader { get => _chainHeader; private set => SetProperty(ref _chainHeader, value); }

    private string _chainUnderlyingPrice = string.Empty;
    public string ChainUnderlyingPrice { get => _chainUnderlyingPrice; private set => SetProperty(ref _chainUnderlyingPrice, value); }

    private string _chainDte = string.Empty;
    public string ChainDte { get => _chainDte; private set => SetProperty(ref _chainDte, value); }

    private string _chainPcRatio = string.Empty;
    public string ChainPcRatio { get => _chainPcRatio; private set => SetProperty(ref _chainPcRatio, value); }

    // ── Status panel ─────────────────────────────────────────────────────
    private bool _isStatusVisible;
    public bool IsStatusVisible { get => _isStatusVisible; private set => SetProperty(ref _isStatusVisible, value); }

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private bool _isLoadingVisible;
    public bool IsLoadingVisible { get => _isLoadingVisible; private set => SetProperty(ref _isLoadingVisible, value); }

    // ── Tracking ─────────────────────────────────────────────────────────
    private string? _selectedUnderlying;

    public OptionsViewModel(WpfServices.LoggingService loggingService, UiApiClient? apiClient)
    {
        _loggingService = loggingService;
        _apiClient = apiClient;
    }

    // ── Public async actions (called from code-behind) ───────────────────

    public async Task LoadAllAsync()
    {
        await LoadSummaryAsync();
        await LoadTrackedUnderlyingsAsync();
    }

    public async Task LoadExpirationsAsync()
    {
        var symbol = SymbolInputText.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            ShowStatus("Please enter an underlying symbol.", false);
            return;
        }

        await LoadExpirationsForSymbolAsync(symbol);
    }

    public async Task SelectUnderlyingAsync(string symbol)
    {
        SymbolInputText = symbol;
        await LoadExpirationsForSymbolAsync(symbol);
    }

    public async Task SelectExpirationAsync(string expirationStr)
    {
        if (_selectedUnderlying is not null)
            await LoadChainAsync(_selectedUnderlying, expirationStr);
    }

    public async Task RefreshAsync()
    {
        await LoadSummaryAsync();
        await LoadTrackedUnderlyingsAsync();
        if (_selectedUnderlying is not null)
            await LoadExpirationsForSymbolAsync(_selectedUnderlying);
    }

    // ── Private loaders ──────────────────────────────────────────────────

    private async Task LoadSummaryAsync()
    {
        if (_apiClient is null)
        {
            SetProviderStatus(false, "API client not configured");
            return;
        }

        try
        {
            var summary = await _apiClient.GetOptionsSummaryAsync();
            if (summary is not null)
            {
                TrackedContracts = summary.TrackedContracts.ToString("N0");
                TrackedChains = summary.TrackedChains.ToString("N0");
                TrackedUnderlyings = summary.TrackedUnderlyings.ToString("N0");
                WithGreeks = summary.ContractsWithGreeks.ToString("N0");
                var providerConfigured = string.Equals(summary.ProviderMode, "Configured", StringComparison.OrdinalIgnoreCase);
                var providerName = string.IsNullOrWhiteSpace(summary.ProviderDisplayName)
                    ? "Options provider"
                    : summary.ProviderDisplayName;
                var statusText = !string.IsNullOrWhiteSpace(summary.ProviderStatusMessage)
                    ? summary.ProviderStatusMessage
                    : providerConfigured
                        ? $"{providerName} is configured."
                        : $"{providerName} is unavailable.";

                SetProviderStatus(providerConfigured, statusText);
            }
            else
            {
                SetProviderStatus(false, "Unable to retrieve summary");
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load options summary", ex);
            SetProviderStatus(false, $"Error: {ex.Message}");
        }
    }

    private async Task LoadTrackedUnderlyingsAsync()
    {
        if (_apiClient is null)
            return;

        try
        {
            var underlyings = await _apiClient.GetOptionsTrackedUnderlyingsAsync();
            Underlyings.Clear();
            if (underlyings is not null)
            {
                foreach (var symbol in underlyings)
                    Underlyings.Add(symbol);
            }

            NoUnderlyingsVisible = Underlyings.Count == 0;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load tracked underlyings", ex);
        }
    }

    private async Task LoadExpirationsForSymbolAsync(string symbol)
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
            Expirations.Clear();

            if (response is not null && response.Expirations.Count > 0)
            {
                foreach (var exp in response.Expirations)
                    Expirations.Add(exp.ToString("yyyy-MM-dd"));

                ExpirationsHeader = $"Expirations for {symbol} ({response.Count})";
                ExpirationsPanelVisible = true;
                HideStatus();
            }
            else
            {
                ExpirationsPanelVisible = false;
                ShowStatus($"No expirations found for {symbol}.", false);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load expirations for " + symbol, ex);
            ShowStatus($"Failed to load expirations: {ex.Message}", false);
        }
    }

    private async Task LoadChainAsync(string symbol, string expiration)
    {
        if (_apiClient is null)
            return;

        ShowStatus($"Loading chain for {symbol} {expiration}...", true);

        try
        {
            var chain = await _apiClient.GetOptionsChainAsync(symbol, expiration);
            if (chain is not null)
            {
                ChainHeader = $"Option Chain: {symbol} {expiration}";
                ChainUnderlyingPrice = $"Underlying: ${chain.UnderlyingPrice:N2}";
                ChainDte = $"DTE: {chain.DaysToExpiration}";
                ChainPcRatio = chain.PutCallVolumeRatio.HasValue
                    ? $"P/C Ratio: {chain.PutCallVolumeRatio:N2}"
                    : string.Empty;

                Calls = chain.Calls;
                Puts = chain.Puts;

                ChainPanelVisible = true;
                HideStatus();
            }
            else
            {
                ShowStatus($"No chain data available for {symbol} {expiration}.", false);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to load chain for {symbol} {expiration}", ex);
            ShowStatus($"Failed to load chain: {ex.Message}", false);
        }
    }

    private void SetProviderStatus(bool available, string message)
    {
        IsProviderAvailable = available;
        ProviderStatusText = message;
    }

    private void ShowStatus(string message, bool showLoading)
    {
        StatusText = message;
        IsLoadingVisible = showLoading;
        IsStatusVisible = true;
    }

    private void HideStatus()
    {
        IsStatusVisible = false;
        IsLoadingVisible = false;
    }
}
