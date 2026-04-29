using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Displays netted positions across all active strategies and accounts.
/// Data is polled every 5 seconds from <c>GET /api/portfolio/aggregate</c>.
/// </summary>
public sealed class AggregatePortfolioViewModel : BindableBase, IDisposable
{
    private readonly ApiClientService _apiClient;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    // ── Exposure summary ──────────────────────────────────────────────────────

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

    private IReadOnlyList<string> _top5Concentrations = [];
    public IReadOnlyList<string> Top5Concentrations
    {
        get => _top5Concentrations;
        private set => SetProperty(ref _top5Concentrations, value);
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
                UpdatePositionsPresentation();
            }
        }
    }

    private string _positionsEmptyStateTitle = "Waiting for aggregate portfolio";
    public string PositionsEmptyStateTitle
    {
        get => _positionsEmptyStateTitle;
        private set => SetProperty(ref _positionsEmptyStateTitle, value);
    }

    private string _positionsEmptyStateDetail = "Meridian will load cross-strategy exposure and netted positions from the local workstation host.";
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

    // ── Netted positions grid ─────────────────────────────────────────────────

    public ObservableCollection<AggregatedPositionRow> Positions { get; } = [];

    private bool _hasLoadedPortfolioSnapshot;
    private bool _hasLoadError;

    public bool HasPositions => Positions.Count > 0;

    public bool IsPositionsGridVisible => HasPositions;

    public bool IsPositionsEmptyStateVisible => !HasPositions;

    // ── Commands ──────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public AggregatePortfolioViewModel(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

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

    public async Task InitializeAsync(CancellationToken ct = default)
    {
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
            // Page unload and DI container disposal can race during navigation tests.
        }

        _cts.Dispose();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsRefreshing = true;
        _hasLoadError = false;
        StatusText = "Refreshing…";

        try
        {
            var positionsTask = _apiClient.GetAsync<List<AggregatedPositionDto>>("/api/portfolio/aggregate", ct);
            var exposureTask = _apiClient.GetAsync<ExposureDto>("/api/portfolio/exposure", ct);

            await Task.WhenAll(positionsTask, exposureTask);

            var positions = await positionsTask.ConfigureAwait(false);
            var exposure = await exposureTask.ConfigureAwait(false);

            if (positions is null)
            {
                _hasLoadedPortfolioSnapshot = false;
                _hasLoadError = true;
                StatusText = "Aggregate portfolio data unavailable.";
                return;
            }

            Positions.Clear();
            foreach (var p in positions)
            {
                Positions.Add(new AggregatedPositionRow(
                    Symbol: p.Symbol ?? string.Empty,
                    TotalQuantity: p.TotalQuantity,
                    LongQuantity: p.LongQuantity,
                    ShortQuantity: p.ShortQuantity,
                    WeightedAverageCost: p.WeightedAverageCost,
                    TotalUnrealisedPnl: p.TotalUnrealisedPnl,
                    ContributingRuns: p.Contributions?.Count ?? 0));
            }

            if (exposure is not null)
            {
                GrossExposure = exposure.GrossExposure;
                NetExposure = exposure.NetExposure;
                Top5Concentrations = exposure.Top5Concentrations ?? [];
                AsOf = exposure.AsOf;
            }

            _hasLoadedPortfolioSnapshot = true;
            StatusText = $"Loaded {Positions.Count} netted position(s).";
        }
        finally
        {
            IsRefreshing = false;
            UpdatePositionsPresentation();
        }
    }

    public static AggregatePortfolioEmptyState BuildPositionsEmptyState(
        bool isRefreshing,
        bool hasLoadedPortfolioSnapshot,
        bool hasLoadError,
        int positionCount)
    {
        if (positionCount > 0)
        {
            return new AggregatePortfolioEmptyState(
                IsVisible: false,
                Title: string.Empty,
                Detail: string.Empty);
        }

        if (isRefreshing)
        {
            return new AggregatePortfolioEmptyState(
                IsVisible: true,
                Title: "Loading aggregate portfolio",
                Detail: "Waiting for the local workstation host to return cross-strategy exposure and netted position rows.");
        }

        if (hasLoadError)
        {
            return new AggregatePortfolioEmptyState(
                IsVisible: true,
                Title: "Aggregate portfolio unavailable",
                Detail: "Refresh after the local workstation host is reachable and portfolio aggregation endpoints are responding.");
        }

        if (!hasLoadedPortfolioSnapshot)
        {
            return new AggregatePortfolioEmptyState(
                IsVisible: true,
                Title: "Waiting for aggregate portfolio",
                Detail: "Meridian will load cross-strategy exposure and netted positions from the local workstation host.");
        }

        return new AggregatePortfolioEmptyState(
            IsVisible: true,
            Title: "No netted positions yet",
            Detail: "Strategy runs and account positions are loaded, but no cross-strategy position rows were returned for the aggregate view.");
    }

    private void UpdatePositionsPresentation()
    {
        var state = BuildPositionsEmptyState(
            IsRefreshing,
            _hasLoadedPortfolioSnapshot,
            _hasLoadError,
            Positions.Count);

        PositionsEmptyStateTitle = state.Title;
        PositionsEmptyStateDetail = state.Detail;
        OnPropertyChanged(nameof(HasPositions));
        OnPropertyChanged(nameof(IsPositionsGridVisible));
        OnPropertyChanged(nameof(IsPositionsEmptyStateVisible));
    }

    // ── Inner DTOs (local API projection) ─────────────────────────────────────

    private sealed class AggregatedPositionDto
    {
        public string? Symbol { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal LongQuantity { get; set; }
        public decimal ShortQuantity { get; set; }
        public decimal WeightedAverageCost { get; set; }
        public decimal TotalUnrealisedPnl { get; set; }
        public List<object>? Contributions { get; set; }
    }

    private sealed class ExposureDto
    {
        public decimal GrossExposure { get; set; }
        public decimal NetExposure { get; set; }
        public List<string>? Top5Concentrations { get; set; }
        public DateTimeOffset AsOf { get; set; }
    }
}

/// <summary>Row model for the netted positions grid in <see cref="AggregatePortfolioViewModel"/>.</summary>
public sealed record AggregatedPositionRow(
    string Symbol,
    decimal TotalQuantity,
    decimal LongQuantity,
    decimal ShortQuantity,
    decimal WeightedAverageCost,
    decimal TotalUnrealisedPnl,
    int ContributingRuns);

public sealed record AggregatePortfolioEmptyState(bool IsVisible, string Title, string Detail);
