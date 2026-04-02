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
        StrategyRunWorkspaceService strategyRunWorkspaceService)
    {
        _backtestService = backtestService;
        _navigationService = navigationService;
        _strategyRunWorkspaceService = strategyRunWorkspaceService;
        RunBacktestCommand = new AsyncRelayCommand(RunBacktestAsync, () => CanRun);
        CancelBacktestCommand = new RelayCommand(CancelBacktest, () => IsRunning);
        OpenRunBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(() => OpenRunSurface("RunDetail"), () => HasLatestRecordedRun);
        OpenRunPortfolioCommand = new RelayCommand(() => OpenRunSurface("RunPortfolio"), () => HasLatestRecordedRun);
        OpenRunLedgerCommand = new RelayCommand(() => OpenRunSurface("RunLedger"), () => HasLatestRecordedRun);
        OpenRunRiskCommand = new RelayCommand(() => OpenRunSurface("RunRisk"), () => HasLatestRecordedRun);

        _backtestService.BacktestCompleted += OnBacktestCompleted;
        _backtestService.BacktestCancelled += OnBacktestCancelled;
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
        }, DispatcherPriority.Background);
    }

    private void OnBacktestCompleted(object? sender, BacktestResult result)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsRunning = false;
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
    }

    private void OpenRunSurface(string pageTag)
    {
        if (!string.IsNullOrWhiteSpace(LatestRecordedRunId))
        {
            _navigationService.NavigateTo(pageTag, LatestRecordedRunId);
        }
    }

    public void Dispose()
    {
        _backtestService.BacktestCompleted -= OnBacktestCompleted;
        _backtestService.BacktestCancelled -= OnBacktestCancelled;
    }
}

// ── View model display types ──────────────────────────────────────────────────

/// <summary>Single point on the equity curve (day number × portfolio value).</summary>
public sealed record EquityCurvePoint(int DayNumber, double Value);

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
    public string Amount => $"{CashFlow.Amount:C2}";
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
    public string RealizedPnl => $"{Attr.RealizedPnl:C2}";
    public string UnrealizedPnl => $"{Attr.UnrealizedPnl:C2}";
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
        if (_bought.Contains(bar.Symbol)) return;
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
