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

    private DateTimeOffset _asOf;
    public DateTimeOffset AsOf
    {
        get => _asOf;
        private set => SetProperty(ref _asOf, value);
    }

    // ── Netted positions grid ─────────────────────────────────────────────────

    public ObservableCollection<AggregatedPositionRow> Positions { get; } = [];

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
            _ = RefreshAsync(_cts.Token).ContinueWith(
                static t => { /* exceptions handled inside RefreshAsync */ },
                TaskContinuationOptions.OnlyOnFaulted);
        };
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        StatusText = "Refreshing…";

        var positionsTask = _apiClient.GetAsync<List<AggregatedPositionDto>>("/api/portfolio/aggregate", ct);
        var exposureTask = _apiClient.GetAsync<ExposureDto>("/api/portfolio/exposure", ct);

        await Task.WhenAll(positionsTask, exposureTask);

        var positions = positionsTask.Result;
        var exposure = exposureTask.Result;

        if (positions is null)
        {
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

        StatusText = $"Loaded {Positions.Count} netted position(s).";
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
