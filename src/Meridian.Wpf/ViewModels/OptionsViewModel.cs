using System.Collections;
using System.Collections.ObjectModel;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Options Chain page.
/// Owns all collections, status strings, visibility flags, and revisioned async data loads.
/// </summary>
public sealed class OptionsViewModel : BindableBase, IAsyncDisposable
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly IOptionsApiClient? _apiClient;
    private readonly object _loadGate = new();
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private LoadRun _loadRun = new();
    private CancellationTokenSource? _expirationLoadCancellation;
    private CancellationTokenSource? _chainLoadCancellation;
    private long _expirationRevision;
    private long _chainRevision;
    private long _presentationRevision;
    private string? _expirationSymbol;
    private (string Symbol, string Expiration)? _chainKey;
    private bool _disposed;

    private string _trackedContracts = "--";
    public string TrackedContracts { get => _trackedContracts; private set => SetProperty(ref _trackedContracts, value); }

    private string _trackedChains = "--";
    public string TrackedChains { get => _trackedChains; private set => SetProperty(ref _trackedChains, value); }

    private string _trackedUnderlyings = "--";
    public string TrackedUnderlyings { get => _trackedUnderlyings; private set => SetProperty(ref _trackedUnderlyings, value); }

    private string _withGreeks = "--";
    public string WithGreeks { get => _withGreeks; private set => SetProperty(ref _withGreeks, value); }

    private bool _isProviderAvailable;
    public bool IsProviderAvailable { get => _isProviderAvailable; private set => SetProperty(ref _isProviderAvailable, value); }

    private string _providerStatusText = "Provider status unknown";
    public string ProviderStatusText { get => _providerStatusText; private set => SetProperty(ref _providerStatusText, value); }

    public ObservableCollection<string> Underlyings { get; } = new();
    public ObservableCollection<string> Expirations { get; } = new();

    private IEnumerable? _calls;
    public IEnumerable? Calls { get => _calls; private set => SetProperty(ref _calls, value); }

    private IEnumerable? _puts;
    public IEnumerable? Puts { get => _puts; private set => SetProperty(ref _puts, value); }

    private string _symbolInputText = string.Empty;
    public string SymbolInputText { get => _symbolInputText; set => SetProperty(ref _symbolInputText, value); }

    private bool _noUnderlyingsVisible = true;
    public bool NoUnderlyingsVisible { get => _noUnderlyingsVisible; private set => SetProperty(ref _noUnderlyingsVisible, value); }

    private bool _expirationsPanelVisible;
    public bool ExpirationsPanelVisible { get => _expirationsPanelVisible; private set => SetProperty(ref _expirationsPanelVisible, value); }

    private string _expirationsHeader = "Expirations";
    public string ExpirationsHeader { get => _expirationsHeader; private set => SetProperty(ref _expirationsHeader, value); }

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

    private bool _isStatusVisible;
    public bool IsStatusVisible { get => _isStatusVisible; private set => SetProperty(ref _isStatusVisible, value); }

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private bool _isLoadingVisible;
    public bool IsLoadingVisible { get => _isLoadingVisible; private set => SetProperty(ref _isLoadingVisible, value); }

    private string? _selectedUnderlying;

    /// <summary>
    /// Compatibility constructor for callers that already retain a typed UI client.
    /// </summary>
    public OptionsViewModel(WpfServices.LoggingService loggingService, UiApiClient? apiClient)
        : this(
            loggingService,
            apiClient is null ? null : new RetainedUiOptionsApiClient(apiClient))
    {
    }

    /// <summary>
    /// Preferred constructor. Each new load resolves the current endpoint generation while an
    /// in-flight call remains pinned to the generation on which it began.
    /// </summary>
    public OptionsViewModel(WpfServices.LoggingService loggingService, ApiClientService apiClientService)
        : this(
            loggingService,
            new SessionOptionsApiClient(
                apiClientService ?? throw new ArgumentNullException(nameof(apiClientService))))
    {
    }

    private OptionsViewModel(
        WpfServices.LoggingService loggingService,
        IOptionsApiClient? apiClient)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _apiClient = apiClient;
    }

    internal static OptionsViewModel CreateForTesting(
        WpfServices.LoggingService loggingService,
        IOptionsApiClient? apiClient)
        => new(loggingService, apiClient);

    public Task LoadAllAsync(CancellationToken ct = default)
        => RunTrackedAsync(async token =>
        {
            await LoadSummaryAsync(token);
            await LoadTrackedUnderlyingsAsync(token);
        }, ct);

    public Task LoadExpirationsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var symbol = NormalizeSymbol(SymbolInputText);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            ShowStatus("Please enter an underlying symbol.", showLoading: false);
            return Task.CompletedTask;
        }

        return RunTrackedAsync(
            token => LoadExpirationsForSymbolAsync(symbol, token),
            ct);
    }

    public Task SelectUnderlyingAsync(string symbol, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var normalized = NormalizeSymbol(symbol);
        SymbolInputText = normalized;
        return RunTrackedAsync(
            token => LoadExpirationsForSymbolAsync(normalized, token),
            ct);
    }

    public Task SelectExpirationAsync(string expiration, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        string? symbol;
        lock (_loadGate)
            symbol = _selectedUnderlying;

        return symbol is null
            ? Task.CompletedTask
            : RunTrackedAsync(
                token => LoadChainAsync(symbol, expiration.Trim(), token),
                ct);
    }

    public Task RefreshAsync(CancellationToken ct = default)
        => RunTrackedAsync(async token =>
        {
            await LoadSummaryAsync(token);
            await LoadTrackedUnderlyingsAsync(token);

            string? symbol;
            lock (_loadGate)
                symbol = _selectedUnderlying;
            if (symbol is not null)
                await LoadExpirationsForSymbolAsync(symbol, token);
        }, ct);

    /// <summary>
    /// Cancels and drains the current page-load generation. A later page load starts a fresh
    /// generation, which keeps navigation unload/reload behavior reusable.
    /// </summary>
    public Task StopAsync()
        => StopCoreAsync(disposing: false);

    public async ValueTask DisposeAsync()
        => await StopCoreAsync(disposing: true).ConfigureAwait(false);

    private async Task LoadSummaryAsync(CancellationToken ct)
    {
        if (_apiClient is null)
        {
            SetProviderStatus(false, "API client not configured");
            return;
        }

        try
        {
            var summary = await _apiClient.GetOptionsSummaryAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (summary is null)
            {
                SetProviderStatus(false, "Unable to retrieve summary");
                return;
            }

            TrackedContracts = summary.TrackedContracts.ToString("N0");
            TrackedChains = summary.TrackedChains.ToString("N0");
            TrackedUnderlyings = summary.TrackedUnderlyings.ToString("N0");
            WithGreeks = summary.ContractsWithGreeks.ToString("N0");
            var providerConfigured = string.Equals(
                summary.ProviderMode,
                "Configured",
                StringComparison.OrdinalIgnoreCase);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load options summary", ex);
            SetProviderStatus(false, $"Error: {ex.Message}");
        }
    }

    private async Task LoadTrackedUnderlyingsAsync(CancellationToken ct)
    {
        if (_apiClient is null)
            return;

        try
        {
            var underlyings = await _apiClient.GetOptionsTrackedUnderlyingsAsync(ct);
            ct.ThrowIfCancellationRequested();
            Underlyings.Clear();
            if (underlyings is not null)
            {
                foreach (var symbol in underlyings)
                    Underlyings.Add(symbol);
            }

            NoUnderlyingsVisible = Underlyings.Count == 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load tracked underlyings", ex);
        }
    }

    private async Task LoadExpirationsForSymbolAsync(string symbol, CancellationToken runToken)
    {
        if (_apiClient is null)
        {
            ShowStatus("API client not configured.", showLoading: false);
            return;
        }

        CancellationTokenSource requestCancellation;
        CancellationTokenSource? previousExpiration;
        CancellationTokenSource? previousChain;
        long revision;
        long presentationRevision;
        lock (_loadGate)
        {
            ThrowIfDisposed();
            revision = checked(++_expirationRevision);
            presentationRevision = checked(++_presentationRevision);
            requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            previousExpiration = _expirationLoadCancellation;
            previousChain = _chainLoadCancellation;
            _expirationLoadCancellation = requestCancellation;
            _chainLoadCancellation = null;
            _expirationSymbol = symbol;
            _chainKey = null;
            checked
            { _chainRevision++; }
            _selectedUnderlying = symbol;
        }

        CancelNoThrow(previousExpiration);
        CancelNoThrow(previousChain);
        ApplyIfCurrentExpiration(revision, symbol, requestCancellation, () =>
        {
            Expirations.Clear();
            ExpirationsPanelVisible = false;
            ChainPanelVisible = false;
            Calls = null;
            Puts = null;
            ShowStatus($"Loading expirations for {symbol}...", showLoading: true);
        });

        try
        {
            var response = await _apiClient.GetOptionsExpirationsAsync(
                symbol,
                requestCancellation.Token);
            requestCancellation.Token.ThrowIfCancellationRequested();

            ApplyIfCurrentExpiration(revision, symbol, requestCancellation, () =>
            {
                Expirations.Clear();
                if (response is not null && response.Expirations.Count > 0)
                {
                    foreach (var expiration in response.Expirations)
                        Expirations.Add(expiration.ToString("yyyy-MM-dd"));

                    ExpirationsHeader = $"Expirations for {symbol} ({response.Count})";
                    ExpirationsPanelVisible = true;
                    HideStatusIfCurrent(presentationRevision);
                }
                else
                {
                    ExpirationsPanelVisible = false;
                    ShowStatusIfCurrent(
                        presentationRevision,
                        $"No expirations found for {symbol}.",
                        showLoading: false);
                }
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            // A newer selection, StopAsync, or disposal superseded this request.
        }
        catch (Exception ex)
        {
            ApplyIfCurrentExpiration(revision, symbol, requestCancellation, () =>
            {
                _loggingService.LogError($"Failed to load expirations for {symbol}", ex);
                ShowStatusIfCurrent(
                    presentationRevision,
                    $"Failed to load expirations: {ex.Message}",
                    showLoading: false);
            });
        }
        finally
        {
            lock (_loadGate)
            {
                if (ReferenceEquals(_expirationLoadCancellation, requestCancellation))
                    _expirationLoadCancellation = null;
            }

            requestCancellation.Dispose();
        }
    }

    private async Task LoadChainAsync(
        string symbol,
        string expiration,
        CancellationToken runToken)
    {
        if (_apiClient is null)
            return;

        var key = (Symbol: symbol, Expiration: expiration);
        CancellationTokenSource requestCancellation;
        CancellationTokenSource? previous;
        long revision;
        long presentationRevision;
        lock (_loadGate)
        {
            ThrowIfDisposed();
            revision = checked(++_chainRevision);
            presentationRevision = checked(++_presentationRevision);
            requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            previous = _chainLoadCancellation;
            _chainLoadCancellation = requestCancellation;
            _chainKey = key;
        }

        CancelNoThrow(previous);
        ApplyIfCurrentChain(revision, key, requestCancellation, () =>
        {
            ChainPanelVisible = false;
            Calls = null;
            Puts = null;
            ShowStatus($"Loading chain for {symbol} {expiration}...", showLoading: true);
        });

        try
        {
            var chain = await _apiClient.GetOptionsChainAsync(
                symbol,
                expiration,
                requestCancellation.Token);
            requestCancellation.Token.ThrowIfCancellationRequested();

            ApplyIfCurrentChain(revision, key, requestCancellation, () =>
            {
                if (chain is null)
                {
                    ShowStatusIfCurrent(
                        presentationRevision,
                        $"No chain data available for {symbol} {expiration}.",
                        showLoading: false);
                    return;
                }

                ChainHeader = $"Option Chain: {symbol} {expiration}";
                ChainUnderlyingPrice = $"Underlying: ${chain.UnderlyingPrice:N2}";
                ChainDte = $"DTE: {chain.DaysToExpiration}";
                ChainPcRatio = chain.PutCallVolumeRatio.HasValue
                    ? $"P/C Ratio: {chain.PutCallVolumeRatio:N2}"
                    : string.Empty;
                Calls = chain.Calls;
                Puts = chain.Puts;
                ChainPanelVisible = true;
                HideStatusIfCurrent(presentationRevision);
            });
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            // A newer expiration, symbol, StopAsync, or disposal superseded this request.
        }
        catch (Exception ex)
        {
            ApplyIfCurrentChain(revision, key, requestCancellation, () =>
            {
                _loggingService.LogError($"Failed to load chain for {symbol} {expiration}", ex);
                ShowStatusIfCurrent(
                    presentationRevision,
                    $"Failed to load chain: {ex.Message}",
                    showLoading: false);
            });
        }
        finally
        {
            lock (_loadGate)
            {
                if (ReferenceEquals(_chainLoadCancellation, requestCancellation))
                    _chainLoadCancellation = null;
            }

            requestCancellation.Dispose();
        }
    }

    private async Task RunTrackedAsync(
        Func<CancellationToken, Task> action,
        CancellationToken callerToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        LoadRun run;
        lock (_loadGate)
        {
            ThrowIfDisposed();
            run = _loadRun;
            checked
            { run.ActiveOperations++; }
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken,
            run.Cancellation.Token);
        try
        {
            await action(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            // Cancellation is navigation/selection state, not an operator-visible load failure.
        }
        finally
        {
            lock (_loadGate)
            {
                run.ActiveOperations--;
                if (run.IsStopping && run.ActiveOperations == 0)
                    run.Drained?.TrySetResult();
            }
        }
    }

    private async Task StopCoreAsync(bool disposing)
    {
        await _stopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            LoadRun run;
            Task drained;
            CancellationTokenSource? expirationCancellation;
            CancellationTokenSource? chainCancellation;
            lock (_loadGate)
            {
                if (_disposed)
                    return;

                if (disposing)
                    _disposed = true;

                run = _loadRun;
                if (!disposing)
                    _loadRun = new LoadRun();
                run.IsStopping = true;
                run.Drained = run.ActiveOperations == 0
                    ? null
                    : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                drained = run.Drained?.Task ?? Task.CompletedTask;

                expirationCancellation = _expirationLoadCancellation;
                chainCancellation = _chainLoadCancellation;
                _expirationLoadCancellation = null;
                _chainLoadCancellation = null;
                _expirationSymbol = null;
                _chainKey = null;
                checked
                {
                    _expirationRevision++;
                    _chainRevision++;
                    _presentationRevision++;
                }

                if (IsLoadingVisible)
                {
                    IsLoadingVisible = false;
                    IsStatusVisible = false;
                }
            }

            CancelNoThrow(expirationCancellation);
            CancelNoThrow(chainCancellation);
            run.Cancellation.Cancel();
            await drained.ConfigureAwait(false);
            run.Cancellation.Dispose();
        }
        finally
        {
            _stopGate.Release();
        }
    }

    private void ApplyIfCurrentExpiration(
        long revision,
        string symbol,
        CancellationTokenSource cancellation,
        Action apply)
    {
        lock (_loadGate)
        {
            if (!_disposed
                && revision == _expirationRevision
                && string.Equals(symbol, _expirationSymbol, StringComparison.Ordinal)
                && ReferenceEquals(cancellation, _expirationLoadCancellation))
            {
                apply();
            }
        }
    }

    private void ApplyIfCurrentChain(
        long revision,
        (string Symbol, string Expiration) key,
        CancellationTokenSource cancellation,
        Action apply)
    {
        lock (_loadGate)
        {
            if (!_disposed
                && revision == _chainRevision
                && key == _chainKey
                && ReferenceEquals(cancellation, _chainLoadCancellation))
            {
                apply();
            }
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

    private void ShowStatusIfCurrent(long presentationRevision, string message, bool showLoading)
    {
        if (presentationRevision == _presentationRevision)
            ShowStatus(message, showLoading);
    }

    private void HideStatusIfCurrent(long presentationRevision)
    {
        if (presentationRevision != _presentationRevision)
            return;

        IsStatusVisible = false;
        IsLoadingVisible = false;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void CancelNoThrow(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The superseded operation completed between capture and cancellation.
        }
    }

    private static string NormalizeSymbol(string? symbol)
        => symbol?.Trim().ToUpperInvariant() ?? string.Empty;

    private sealed class LoadRun
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public int ActiveOperations { get; set; }
        public bool IsStopping { get; set; }
        public TaskCompletionSource? Drained { get; set; }
    }
}

internal interface IOptionsApiClient
{
    Task<OptionsSummaryResponse?> GetOptionsSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<string>?> GetOptionsTrackedUnderlyingsAsync(CancellationToken ct);
    Task<OptionsExpirationsResponse?> GetOptionsExpirationsAsync(string symbol, CancellationToken ct);
    Task<OptionsChainResponse?> GetOptionsChainAsync(string symbol, string expiration, CancellationToken ct);
}

internal sealed class SessionOptionsApiClient(ApiClientService apiClientService) : IOptionsApiClient
{
    public Task<OptionsSummaryResponse?> GetOptionsSummaryAsync(CancellationToken ct)
        => apiClientService.UiApi.GetOptionsSummaryAsync(ct);

    public async Task<IReadOnlyList<string>?> GetOptionsTrackedUnderlyingsAsync(CancellationToken ct)
        => await apiClientService.UiApi.GetOptionsTrackedUnderlyingsAsync(ct);

    public Task<OptionsExpirationsResponse?> GetOptionsExpirationsAsync(string symbol, CancellationToken ct)
        => apiClientService.UiApi.GetOptionsExpirationsAsync(symbol, ct);

    public Task<OptionsChainResponse?> GetOptionsChainAsync(
        string symbol,
        string expiration,
        CancellationToken ct)
        => apiClientService.UiApi.GetOptionsChainAsync(symbol, expiration, ct: ct);
}

internal sealed class RetainedUiOptionsApiClient(UiApiClient apiClient) : IOptionsApiClient
{
    public Task<OptionsSummaryResponse?> GetOptionsSummaryAsync(CancellationToken ct)
        => apiClient.GetOptionsSummaryAsync(ct);

    public async Task<IReadOnlyList<string>?> GetOptionsTrackedUnderlyingsAsync(CancellationToken ct)
        => await apiClient.GetOptionsTrackedUnderlyingsAsync(ct);

    public Task<OptionsExpirationsResponse?> GetOptionsExpirationsAsync(string symbol, CancellationToken ct)
        => apiClient.GetOptionsExpirationsAsync(symbol, ct);

    public Task<OptionsChainResponse?> GetOptionsChainAsync(
        string symbol,
        string expiration,
        CancellationToken ct)
        => apiClient.GetOptionsChainAsync(symbol, expiration, ct: ct);
}
