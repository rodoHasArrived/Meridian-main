using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Displays a single brokerage or bank account's positions, cash, and P&amp;L summary.
/// Data is polled every 5 seconds from <c>GET /api/execution/accounts/{accountId}</c>.
/// </summary>
public sealed class AccountPortfolioViewModel : BindableBase, IDisposable
{
    private readonly ApiClientService _apiClient;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    // ── Header ────────────────────────────────────────────────────────────────

    private string _accountId = string.Empty;
    public string AccountId
    {
        get => _accountId;
        private set
        {
            if (SetProperty(ref _accountId, value))
            {
                _hasLoadedAccountSnapshot = false;
                Positions.Clear();
                OnPropertyChanged(nameof(CanRefreshAccount));
                RefreshCommand.NotifyCanExecuteChanged();
                UpdatePositionsPresentation();
            }
        }
    }

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    private string _kind = string.Empty;
    public string Kind
    {
        get => _kind;
        private set => SetProperty(ref _kind, value);
    }

    // ── Summary figures ───────────────────────────────────────────────────────

    private decimal _cash;
    public decimal Cash
    {
        get => _cash;
        private set => SetProperty(ref _cash, value);
    }

    private decimal _longMarketValue;
    public decimal LongMarketValue
    {
        get => _longMarketValue;
        private set => SetProperty(ref _longMarketValue, value);
    }

    private decimal _shortMarketValue;
    public decimal ShortMarketValue
    {
        get => _shortMarketValue;
        private set => SetProperty(ref _shortMarketValue, value);
    }

    private decimal _grossExposure;
    public decimal GrossExposure
    {
        get => _grossExposure;
        private set => SetProperty(ref _grossExposure, value);
    }

    private decimal _netExposure;
    public decimal NetExposure
    {
        get => _netExposure;
        private set => SetProperty(ref _netExposure, value);
    }

    private decimal _unrealisedPnl;
    public decimal UnrealisedPnl
    {
        get => _unrealisedPnl;
        private set => SetProperty(ref _unrealisedPnl, value);
    }

    private decimal _realisedPnl;
    public decimal RealisedPnl
    {
        get => _realisedPnl;
        private set => SetProperty(ref _realisedPnl, value);
    }

    private string _statusText = "Loading…";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(CanRefreshAccount));
                RefreshCommand.NotifyCanExecuteChanged();
                UpdatePositionsPresentation();
            }
        }
    }

    private string _positionsEmptyStateTitle = "Select an account to review positions";
    public string PositionsEmptyStateTitle
    {
        get => _positionsEmptyStateTitle;
        private set => SetProperty(ref _positionsEmptyStateTitle, value);
    }

    private string _positionsEmptyStateDetail = "Open Fund Accounts or a brokerage-sync work item to load account cash, exposure, and positions.";
    public string PositionsEmptyStateDetail
    {
        get => _positionsEmptyStateDetail;
        private set => SetProperty(ref _positionsEmptyStateDetail, value);
    }

    private DateTimeOffset _asOf;
    public DateTimeOffset AsOf
    {
        get => _asOf;
        private set => SetProperty(ref _asOf, value);
    }

    // ── Positions grid ────────────────────────────────────────────────────────

    public ObservableCollection<AccountPositionRow> Positions { get; } = [];

    private bool _hasLoadedAccountSnapshot;

    public bool HasPositions => Positions.Count > 0;

    public bool IsPositionsGridVisible => HasPositions;

    public bool IsPositionsEmptyStateVisible => !HasPositions;

    public bool CanRefreshAccount => CanRefreshAccountForState(AccountId, IsRefreshing);

    // ── Navigation parameter ──────────────────────────────────────────────────

    private object? _parameter;

    /// <summary>
    /// Receives the account ID string set by NavigationService after page creation.
    /// </summary>
    public object? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value) && value is string accountId)
                _ = InitializeAsync(accountId);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public AccountPortfolioViewModel(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => CanRefreshAccount);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_isDisposed)
            {
                return;
            }

            CancellationToken token;
            try
            {
                token = _cts.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = RefreshAsync(token).ContinueWith(
                static t => { /* exceptions handled inside RefreshAsync */ },
                TaskContinuationOptions.OnlyOnFaulted);
        };

        UpdatePositionsPresentation();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync(string accountId, CancellationToken ct = default)
    {
        AccountId = accountId;
        await RefreshAsync(ct);
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _refreshTimer.Stop();

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Some test and window lifetime paths can dispose the CTS before the view model is torn down.
        }

        _cts.Dispose();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsRefreshing = true;
        StatusText = "Refreshing…";

        try
        {
            var snapshot = await _apiClient.GetAsync<AccountDetailDto>(
                $"/api/execution/accounts/{Uri.EscapeDataString(AccountId)}", ct);

            if (snapshot is null)
            {
                _hasLoadedAccountSnapshot = false;
                StatusText = "Account data unavailable.";
                return;
            }

            _hasLoadedAccountSnapshot = true;
            DisplayName = snapshot.DisplayName ?? AccountId;
            Kind = snapshot.Kind ?? string.Empty;
            Cash = snapshot.Cash;
            LongMarketValue = snapshot.LongMarketValue;
            ShortMarketValue = snapshot.ShortMarketValue;
            GrossExposure = snapshot.GrossExposure;
            NetExposure = snapshot.NetExposure;
            UnrealisedPnl = snapshot.UnrealisedPnl;
            RealisedPnl = snapshot.RealisedPnl;
            AsOf = snapshot.AsOf;

            Positions.Clear();
            if (snapshot.Positions is { Count: > 0 })
            {
                foreach (var pos in snapshot.Positions)
                {
                    Positions.Add(new AccountPositionRow(
                        Symbol: pos.Symbol ?? string.Empty,
                        Quantity: pos.Quantity,
                        Side: pos.Quantity >= 0 ? "Long" : "Short",
                        AvgCost: pos.CostBasis,
                        UnrealisedPnl: pos.UnrealisedPnl,
                        RealisedPnl: pos.RealisedPnl));
                }
            }

            StatusText = $"Loaded {Positions.Count} position(s).";
        }
        finally
        {
            IsRefreshing = false;
            UpdatePositionsPresentation();
        }
    }

    public static bool CanRefreshAccountForState(string? accountId, bool isRefreshing) =>
        !isRefreshing && !string.IsNullOrWhiteSpace(accountId);

    public static AccountPositionsEmptyState BuildPositionsEmptyState(
        bool isRefreshing,
        bool hasLoadedAccountSnapshot,
        bool hasAccountContext,
        int positionCount)
    {
        if (positionCount > 0)
        {
            return new AccountPositionsEmptyState(
                IsVisible: false,
                Title: string.Empty,
                Detail: string.Empty);
        }

        if (!hasAccountContext)
        {
            return new AccountPositionsEmptyState(
                IsVisible: true,
                Title: "Select an account to review positions",
                Detail: "Open Fund Accounts or a brokerage-sync work item to load account cash, exposure, and positions.");
        }

        if (isRefreshing)
        {
            return new AccountPositionsEmptyState(
                IsVisible: true,
                Title: "Loading account positions",
                Detail: "Waiting for the local workstation host to return this account's latest cash, exposure, and position snapshot.");
        }

        if (!hasLoadedAccountSnapshot)
        {
            return new AccountPositionsEmptyState(
                IsVisible: true,
                Title: "Account snapshot unavailable",
                Detail: "Refresh after the local workstation host and brokerage sync are reachable; no current position rows were returned.");
        }

        return new AccountPositionsEmptyState(
            IsVisible: true,
            Title: "No open positions in this account",
            Detail: "Cash and exposure are loaded, but this account has no current position rows. New fills will appear after the next brokerage sync.");
    }

    private void UpdatePositionsPresentation()
    {
        var state = BuildPositionsEmptyState(
            IsRefreshing,
            _hasLoadedAccountSnapshot,
            !string.IsNullOrWhiteSpace(AccountId),
            Positions.Count);

        PositionsEmptyStateTitle = state.Title;
        PositionsEmptyStateDetail = state.Detail;
        OnPropertyChanged(nameof(HasPositions));
        OnPropertyChanged(nameof(IsPositionsGridVisible));
        OnPropertyChanged(nameof(IsPositionsEmptyStateVisible));
    }

    // ── Inner DTOs (local API projection) ─────────────────────────────────────

    private sealed class AccountDetailDto
    {
        public string? AccountId { get; set; }
        public string? DisplayName { get; set; }
        public string? Kind { get; set; }
        public decimal Cash { get; set; }
        public decimal LongMarketValue { get; set; }
        public decimal ShortMarketValue { get; set; }
        public decimal GrossExposure { get; set; }
        public decimal NetExposure { get; set; }
        public decimal UnrealisedPnl { get; set; }
        public decimal RealisedPnl { get; set; }
        public List<PositionDto>? Positions { get; set; }
        public DateTimeOffset AsOf { get; set; }
    }

    private sealed class PositionDto
    {
        public string? Symbol { get; set; }
        public long Quantity { get; set; }
        public decimal CostBasis { get; set; }
        public decimal UnrealisedPnl { get; set; }
        public decimal RealisedPnl { get; set; }
    }
}

/// <summary>Row model for the positions grid in <see cref="AccountPortfolioViewModel"/>.</summary>
public sealed record AccountPositionRow(
    string Symbol,
    long Quantity,
    string Side,
    decimal AvgCost,
    decimal UnrealisedPnl,
    decimal RealisedPnl);

public sealed record AccountPositionsEmptyState(bool IsVisible, string Title, string Detail);
