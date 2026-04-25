using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the BacktestPage. Drives the three-panel layout:
/// left panel (configuration), centre panel (equity curve), right panel (results tabs).
/// </summary>
public sealed class BacktestViewModel : BindableBase, IDisposable
{
    private readonly BacktestService _backtestService;
    private readonly NavigationService _navigationService;
    private readonly StrategyRunWorkspaceService _strategyRunWorkspaceService;
    private readonly BacktestDataAvailabilityService _availabilityService;

    // ── Coverage debounce ────────────────────────────────────────────────────
    private CancellationTokenSource _coverageCts = new();
    private bool _isDisposed;

    // ── Configuration properties ─────────────────────────────────────────────

    private string _symbolsText = string.Empty;
    public string SymbolsText { get => _symbolsText; set => SetProperty(ref _symbolsText, value); }

    private DateTime _fromDate = DateTime.Today.AddYears(-1);
    public DateTime FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }

    private DateTime _toDate = DateTime.Today;
    public DateTime ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }

    private decimal _initialCash = 100_000m;
    public decimal InitialCash { get => _initialCash; set => SetProperty(ref _initialCash, value); }

    private double _annualMarginRate = 0.05;
    public double AnnualMarginRate { get => _annualMarginRate; set => SetProperty(ref _annualMarginRate, value); }

    private string _dataRoot = "./data";
    public string DataRoot { get => _dataRoot; set => SetProperty(ref _dataRoot, value); }

    // ── Status ───────────────────────────────────────────────────────────────

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set { SetProperty(ref _isRunning, value); RaisePropertyChanged(nameof(CanRun)); } }

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; set => SetProperty(ref _progressFraction, value); }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public bool CanRun => !_isRunning;

    // ── Live metrics (updated during run once ≥ 60 days have elapsed) ────────

    private bool _isLiveMetricsActive;
    public bool IsLiveMetricsActive { get => _isLiveMetricsActive; set => SetProperty(ref _isLiveMetricsActive, value); }

    private string _liveSharpe = "-";
    public string LiveSharpe { get => _liveSharpe; set => SetProperty(ref _liveSharpe, value); }

    private string _liveDrawdown = "-";
    public string LiveDrawdown { get => _liveDrawdown; set => SetProperty(ref _liveDrawdown, value); }

    private string _liveTradeCount = "-";
    public string LiveTradeCount { get => _liveTradeCount; set => SetProperty(ref _liveTradeCount, value); }

    // ── Data availability calendar ────────────────────────────────────────────

    public ObservableCollection<CoverageRowVm> CoverageRows { get; } = [];

    private bool _hasCoverageGaps;
    public bool HasCoverageGaps { get => _hasCoverageGaps; set => SetProperty(ref _hasCoverageGaps, value); }

    private bool _isCoverageLoading;
    public bool IsCoverageLoading { get => _isCoverageLoading; set => SetProperty(ref _isCoverageLoading, value); }

    public IAsyncRelayCommand FixGapsCommand { get; private set; } = null!;

    // ── Equity curve ─────────────────────────────────────────────────────────

    public ObservableCollection<EquityCurvePoint> EquityCurvePoints { get; } = [];

    // ── Summary metrics ──────────────────────────────────────────────────────

    private string _totalReturn = "-";
    public string TotalReturn { get => _totalReturn; set => SetProperty(ref _totalReturn, value); }

    private string _annualisedReturn = "-";
    public string AnnualisedReturn { get => _annualisedReturn; set => SetProperty(ref _annualisedReturn, value); }

    private string _sharpeRatio = "-";
    public string SharpeRatio { get => _sharpeRatio; set => SetProperty(ref _sharpeRatio, value); }

    private string _sortinoRatio = "-";
    public string SortinoRatio { get => _sortinoRatio; set => SetProperty(ref _sortinoRatio, value); }

    private string _calmarRatio = "-";
    public string CalmarRatio { get => _calmarRatio; set => SetProperty(ref _calmarRatio, value); }

    private string _maxDrawdown = "-";
    public string MaxDrawdown { get => _maxDrawdown; set => SetProperty(ref _maxDrawdown, value); }

    private string _sharpeText = "-";
    public string SharpeText { get => _sharpeText; set => SetProperty(ref _sharpeText, value); }

    private string _winRate = "-";
    public string WinRate { get => _winRate; set => SetProperty(ref _winRate, value); }

    private string _totalTrades = "-";
    public string TotalTrades { get => _totalTrades; set => SetProperty(ref _totalTrades, value); }

    private string _netPnl = "-";
    public string NetPnl { get => _netPnl; set => SetProperty(ref _netPnl, value); }

    private string _totalCommissions = "-";
    public string TotalCommissions { get => _totalCommissions; set => SetProperty(ref _totalCommissions, value); }

    private string _totalMarginInterest = "-";
    public string TotalMarginInterest { get => _totalMarginInterest; set => SetProperty(ref _totalMarginInterest, value); }

    private string _xirr = "-";
    public string Xirr { get => _xirr; set => SetProperty(ref _xirr, value); }

    private string _eventsProcessed = "-";
    public string EventsProcessed { get => _eventsProcessed; set => SetProperty(ref _eventsProcessed, value); }

    private string _elapsedTime = "-";
    public string ElapsedTime { get => _elapsedTime; set => SetProperty(ref _elapsedTime, value); }

    private string _universe = "-";
    public string Universe { get => _universe; set => SetProperty(ref _universe, value); }

    // ── Explorer row-count display ────────────────────────────────────────────

    private string _attributionCountText = "0 symbols";
    public string AttributionCountText { get => _attributionCountText; set => SetProperty(ref _attributionCountText, value); }

    private string _fillsCountText = "0 trades";
    public string FillsCountText { get => _fillsCountText; set => SetProperty(ref _fillsCountText, value); }

    private string _cashFlowsCountText = "0 entries";
    public string CashFlowsCountText { get => _cashFlowsCountText; set => SetProperty(ref _cashFlowsCountText, value); }

    private string? _latestRecordedRunId;
    public string? LatestRecordedRunId
    {
        get => _latestRecordedRunId;
        private set
        {
            if (SetProperty(ref _latestRecordedRunId, value))
            {
                RaisePropertyChanged(nameof(HasLatestRecordedRun));
                OpenRunDetailCommand.NotifyCanExecuteChanged();
                OpenRunPortfolioCommand.NotifyCanExecuteChanged();
                OpenRunLedgerCommand.NotifyCanExecuteChanged();
                OpenRunRiskCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasLatestRecordedRun => !string.IsNullOrWhiteSpace(LatestRecordedRunId);

    // ── Result detail collections ─────────────────────────────────────────────

    public ObservableCollection<FillEventVm> Fills { get; } = [];
    public ObservableCollection<CashFlowVm> CashFlows { get; } = [];
    public ObservableCollection<SymbolAttributionVm> Attribution { get; } = [];

    // ── Commands ─────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RunBacktestCommand { get; }
    public IRelayCommand CancelBacktestCommand { get; }
    public IRelayCommand OpenRunBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand OpenRunPortfolioCommand { get; }
    public IRelayCommand OpenRunLedgerCommand { get; }
    public IRelayCommand OpenRunRiskCommand { get; }

    public BacktestViewModel(
        BacktestService backtestService,
        NavigationService navigationService,
        StrategyRunWorkspaceService strategyRunWorkspaceService,
        BacktestDataAvailabilityService availabilityService)
    {
        _backtestService = backtestService;
        _navigationService = navigationService;
        _strategyRunWorkspaceService = strategyRunWorkspaceService;
        _availabilityService = availabilityService;
        RunBacktestCommand = new AsyncRelayCommand(RunBacktestAsync, () => CanRun);
        CancelBacktestCommand = new RelayCommand(CancelBacktest, () => IsRunning);
        FixGapsCommand = new AsyncRelayCommand(FixGapsAsync);
        OpenRunBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(() => OpenRunSurface("RunDetail"), () => HasLatestRecordedRun);
        OpenRunPortfolioCommand = new RelayCommand(() => OpenRunSurface("RunPortfolio"), () => HasLatestRecordedRun);
        OpenRunLedgerCommand = new RelayCommand(() => OpenRunSurface("RunLedger"), () => HasLatestRecordedRun);
        OpenRunRiskCommand = new RelayCommand(() => OpenRunSurface("RunRisk"), () => HasLatestRecordedRun);

        _backtestService.BacktestCompleted += OnBacktestCompleted;
        _backtestService.BacktestCancelled += OnBacktestCancelled;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SymbolsText) or nameof(FromDate) or nameof(ToDate) or nameof(DataRoot))
                ScheduleCoverageRefresh();
        };
    }

    // ── Command implementations ───────────────────────────────────────────────

    private async Task RunBacktestAsync(CancellationToken ct = default)
    {
        EquityCurvePoints.Clear();
        Fills.Clear();
        CashFlows.Clear();
        Attribution.Clear();
        StatusText = "Running…";
        ProgressFraction = 0;
        IsLiveMetricsActive = false;
        LiveSharpe = "-";
        LiveDrawdown = "-";
        LiveTradeCount = "-";
        IsRunning = true;

        var symbols = string.IsNullOrWhiteSpace(SymbolsText)
            ? null
            : SymbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var request = new BacktestRequest(
            DateOnly.FromDateTime(FromDate),
            DateOnly.FromDateTime(ToDate),
            symbols,
            InitialCash,
            AnnualMarginRate,
            DataRoot: DataRoot);

        var strategy = new BuyAndHoldStrategy();

        var progress = new Progress<BacktestProgressEvent>(OnProgress);
        var result = await _backtestService.RunAsync(request, strategy, progress);
        if (result is not null)
        {
            LatestRecordedRunId = await _strategyRunWorkspaceService.RecordBacktestRunAsync(request, strategy.Name, result);
        }
    }

    private void CancelBacktest()
    {
        _backtestService.Cancel();
        StatusText = "Cancelling…";
    }

    // ── Progress & completion ─────────────────────────────────────────────────

    private void OnProgress(BacktestProgressEvent evt)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressFraction = evt.ProgressFraction;
            StatusText = $"{evt.CurrentDate:yyyy-MM-dd} — {evt.EventsProcessed:N0} events — equity {evt.PortfolioValue:C0}";
            EquityCurvePoints.Add(new EquityCurvePoint(evt.CurrentDate.DayNumber, (double)evt.PortfolioValue));

            if (evt.LiveMetrics is { } m)
            {
                IsLiveMetricsActive = true;
                LiveSharpe = $"{m.RollingSharpe:F2} (through {evt.CurrentDate:yyyy-MM})";
                LiveDrawdown = $"{m.CurrentDrawdownPct:P1}";
                LiveTradeCount = $"{m.TradeCount:N0}";
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnBacktestCompleted(object? sender, BacktestResult result)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsRunning = false;
            IsLiveMetricsActive = false;
            ProgressFraction = 1.0;
            StatusText = $"Complete — {result.TotalEventsProcessed:N0} events in {result.ElapsedTime.TotalSeconds:F1}s";
            ApplyResult(result);
        });
    }

    private void OnBacktestCancelled(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsRunning = false;
            StatusText = "Cancelled";
        });
    }

    private void ApplyResult(BacktestResult result)
    {
        var m = result.Metrics;
        TotalReturn = $"{m.TotalReturn:P2}";
        AnnualisedReturn = $"{m.AnnualizedReturn:P2}";
        SharpeRatio = $"{m.SharpeRatio:F3}";
        SortinoRatio = $"{m.SortinoRatio:F3}";
        CalmarRatio = $"{m.CalmarRatio:F3}";
        MaxDrawdown = $"{m.MaxDrawdownPercent:P2} ({m.MaxDrawdownRecoveryDays}d recovery)";
        WinRate = $"{m.WinRate:P1}";
        TotalTrades = $"{m.TotalTrades:N0}";
        NetPnl = $"{m.NetPnl:C2}";
        TotalCommissions = $"{m.TotalCommissions:C2}";
        TotalMarginInterest = $"{m.TotalMarginInterest:C2}";
        Xirr = double.IsNaN(m.Xirr) ? "N/A" : $"{m.Xirr:P2}";
        EventsProcessed = $"{result.TotalEventsProcessed:N0}";
        ElapsedTime = $"{result.ElapsedTime.TotalSeconds:F1}s";
        Universe = string.Join(", ", result.Universe.Take(20)) + (result.Universe.Count > 20 ? "…" : "");

        foreach (var fill in result.Fills.Take(1000))
            Fills.Add(new FillEventVm(fill));

        foreach (var cf in result.CashFlows.Take(1000))
            CashFlows.Add(new CashFlowVm(cf));

        foreach (var (symbol, attr) in result.Metrics.SymbolAttribution)
            Attribution.Add(new SymbolAttributionVm(attr));

        AttributionCountText = $"{Attribution.Count} symbol{(Attribution.Count == 1 ? "" : "s")}";
        FillsCountText = $"{Fills.Count} trade{(Fills.Count == 1 ? "" : "s")}";
        CashFlowsCountText = $"{CashFlows.Count} entr{(CashFlows.Count == 1 ? "y" : "ies")}";
    }

    private void OpenRunSurface(string pageTag)
    {
        if (!string.IsNullOrWhiteSpace(LatestRecordedRunId))
        {
            _navigationService.NavigateTo(pageTag, LatestRecordedRunId);
        }
    }

    // ── Coverage calendar helpers ─────────────────────────────────────────────

    private void ScheduleCoverageRefresh()
    {
        _coverageCts.Cancel();
        _coverageCts.Dispose();
        _coverageCts = new CancellationTokenSource();
        _ = DelayThenRefreshAsync(_coverageCts.Token);
    }

    private async Task DelayThenRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            await RefreshCoverageAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshCoverageAsync(CancellationToken cancellationToken)
    {
        var rawSymbols = string.IsNullOrWhiteSpace(SymbolsText) ? [] :
            SymbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .ToList();

        if (rawSymbols.Count == 0)
            return;

        var from = DateOnly.FromDateTime(FromDate);
        var to = DateOnly.FromDateTime(ToDate);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsCoverageLoading = true;
            CoverageRows.Clear();
        });

        try
        {
            var coverage = await _availabilityService.GetCoverageAsync(rawSymbols, from, to, DataRoot, cancellationToken)
                .ConfigureAwait(false);

            // Build months list sorted ascending
            var months = coverage.Values
                .SelectMany(m => m.Keys)
                .Distinct()
                .OrderBy(k => k.Year).ThenBy(k => k.Month)
                .ToList();

            var rows = new List<CoverageRowVm>();
            var anyGaps = false;

            foreach (var (symbol, monthMap) in coverage.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var cells = months.Select(m =>
                {
                    if (!monthMap.TryGetValue(m, out var entry))
                        return new CoverageCellVm(m.Year, m.Month, CoverageLevel.None, $"{m.Year}/{m.Month}: no data");
                    var (present, trading) = entry;
                    var level = trading == 0 ? CoverageLevel.Full :
                                present >= trading ? CoverageLevel.Full :
                                present >= trading * 0.75 ? CoverageLevel.Partial :
                                present >= trading * 0.25 ? CoverageLevel.Major :
                                CoverageLevel.None;
                    if (level != CoverageLevel.Full)
                        anyGaps = true;
                    return new CoverageCellVm(m.Year, m.Month, level, $"{m.Year}/{m.Month}: {present}/{trading} days");
                }).ToList();
                rows.Add(new CoverageRowVm(symbol, cells));
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                CoverageRows.Clear();
                foreach (var r in rows)
                    CoverageRows.Add(r);
                HasCoverageGaps = anyGaps;
                IsCoverageLoading = false;
            });
        }
        catch (OperationCanceledException) { }
    }

    private async Task FixGapsAsync(CancellationToken cancellationToken = default)
    {
        // Stub: in a fully wired implementation this would call BackfillApiService
        // to request fills for amber/red cells. For now just invalidate and refresh.
        _availabilityService.InvalidateCache();
        await RefreshCoverageAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _backtestService.BacktestCompleted -= OnBacktestCompleted;
        _backtestService.BacktestCancelled -= OnBacktestCancelled;

        try
        {
            _coverageCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Coverage refreshes can dispose the CTS before the page teardown finishes.
        }

        _coverageCts.Dispose();
    }
}

// ── View model display types ──────────────────────────────────────────────────

/// <summary>Single point on the equity curve (day number × portfolio value).</summary>
public sealed record EquityCurvePoint(int DayNumber, double Value);

// ── Data availability calendar types ─────────────────────────────────────────

/// <summary>Data coverage quality for a single calendar month.</summary>
public enum CoverageLevel
{
    /// <summary>≥ 100% of expected trading days present.</summary>
    Full,
    /// <summary>75–99% of expected trading days present.</summary>
    Partial,
    /// <summary>25–74% of expected trading days present.</summary>
    Major,
    /// <summary>0–24% of expected trading days present.</summary>
    None,
}

/// <summary>A single cell in the coverage calendar (one month for one symbol).</summary>
public sealed record CoverageCellVm(int Year, int Month, CoverageLevel Level, string Tooltip);

/// <summary>One row in the coverage calendar — symbol + ordered month cells.</summary>
public sealed record CoverageRowVm(string Symbol, IReadOnlyList<CoverageCellVm> Cells);

/// <summary>UI display wrapper for a fill event.</summary>
public sealed record FillEventVm(FillEvent Fill)
{
    public string Symbol => Fill.Symbol;
    public string Quantity => $"{Fill.FilledQuantity:+#;-#;0}";
    public string Price => $"{Fill.FillPrice:C4}";
    public string Commission => $"{Fill.Commission:C2}";
    public string Time => Fill.FilledAt.ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>UI display wrapper for a cash flow entry.</summary>
public sealed record CashFlowVm(CashFlowEntry CashFlow)
{
    public string Type => CashFlow switch
    {
        TradeCashFlow => "Trade",
        MarginInterestCashFlow => "Margin Interest",
        ShortRebateCashFlow => "Short Rebate",
        CommissionCashFlow => "Commission",
        DividendCashFlow => "Dividend",
        _ => "Other"
    };
    public string Amount => $"{CashFlow.Amount:+$#,##0.00;-$#,##0.00;$0.00}";
    public bool IsAmountPositive => CashFlow.Amount >= 0;
    public string Time => CashFlow.Timestamp.ToString("yyyy-MM-dd");
    public string Symbol => CashFlow switch
    {
        TradeCashFlow t => t.Symbol,
        ShortRebateCashFlow s => s.Symbol,
        CommissionCashFlow c => c.Symbol,
        DividendCashFlow d => d.Symbol,
        _ => "-"
    };
}

/// <summary>UI display wrapper for per-symbol attribution.</summary>
public sealed record SymbolAttributionVm(SymbolAttribution Attr)
{
    public string Symbol => Attr.Symbol;
    public string RealizedPnl => $"{Attr.RealizedPnl:+$#,##0.00;-$#,##0.00;$0.00}";
    public bool IsRealizedPnlPositive => Attr.RealizedPnl >= 0;
    public string UnrealizedPnl => $"{Attr.UnrealizedPnl:+$#,##0.00;-$#,##0.00;$0.00}";
    public bool IsUnrealizedPnlPositive => Attr.UnrealizedPnl >= 0;
    public string TotalPnl => $"{Attr.RealizedPnl + Attr.UnrealizedPnl:+$#,##0.00;-$#,##0.00;$0.00}";
    public bool IsTotalPnlPositive => Attr.RealizedPnl + Attr.UnrealizedPnl >= 0;
    public string Trades => $"{Attr.TradeCount:N0}";
    public string Commissions => $"{Attr.Commissions:C2}";
}

// ── Built-in no-op strategy (passes symbol-universe check; useful for data exploration) ─

/// <summary>
/// Trivial buy-and-hold strategy: buys the full available capital in the first bar for each symbol.
/// Used as the default strategy when no plugin is loaded.
/// </summary>
internal sealed class BuyAndHoldStrategy : IBacktestStrategy
{
    public string Name => "Buy & Hold (equal-weight)";
    private readonly HashSet<string> _bought = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (_bought.Contains(bar.Symbol))
            return;
        var perSymbol = ctx.Cash / Math.Max(ctx.Universe.Count, 1);
        var qty = (long)(perSymbol / bar.Close);
        if (qty > 0)
        {
            ctx.PlaceMarketOrder(bar.Symbol, qty);
            _bought.Add(bar.Symbol);
        }
    }

    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}
