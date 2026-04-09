using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Backtesting.Sdk;
using Meridian.Backtesting.Sdk.Strategies.AdvancedCarry;
using Meridian.Contracts.Domain.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Carry Trade Backtest page.
/// Lets users select an arbitrary set of securities, configure carry-specific parameters, run a
/// backtest backed by <see cref="CarryTradeBacktestStrategy"/>, and view a viability assessment.
/// </summary>
public sealed class CarryTradeBacktestViewModel : BindableBase, IDisposable
{
    private readonly BacktestService _backtestService;
    private readonly NavigationService _navigationService;
    private readonly StrategyRunWorkspaceService _strategyRunWorkspaceService;

    // ── Security selection ──────────────────────────────────────────────────

    private string _newSymbol = string.Empty;
    public string NewSymbol { get => _newSymbol; set => SetProperty(ref _newSymbol, value); }

    /// <summary>Symbols added by the user to the carry portfolio.</summary>
    public ObservableCollection<string> SelectedSymbols { get; } = [];

    // ── Backtest parameters ─────────────────────────────────────────────────

    private DateTime _fromDate = DateTime.Today.AddYears(-2);
    public DateTime FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }

    private DateTime _toDate = DateTime.Today;
    public DateTime ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }

    private decimal _initialCash = 500_000m;
    public decimal InitialCash { get => _initialCash; set => SetProperty(ref _initialCash, value); }

    private string _dataRoot = "./data";
    public string DataRoot { get => _dataRoot; set => SetProperty(ref _dataRoot, value); }

    // ── Carry strategy parameters ───────────────────────────────────────────

    private CarryOptimizationMethod _optimizationMethod = CarryOptimizationMethod.MeanVariance;
    public CarryOptimizationMethod OptimizationMethod { get => _optimizationMethod; set => SetProperty(ref _optimizationMethod, value); }

    public IReadOnlyList<CarryOptimizationMethod> OptimizationMethods { get; } =
        Enum.GetValues<CarryOptimizationMethod>();

    private double _riskAversion = 3.0;
    public double RiskAversion { get => _riskAversion; set => SetProperty(ref _riskAversion, value); }

    private double _riskFreeRate = 0.04;
    public double RiskFreeRate { get => _riskFreeRate; set => SetProperty(ref _riskFreeRate, value); }

    private double _maxSinglePosition = 0.30;
    public double MaxSinglePosition { get => _maxSinglePosition; set => SetProperty(ref _maxSinglePosition, value); }

    private double _targetVolatility = 0.12;
    public double TargetVolatility { get => _targetVolatility; set => SetProperty(ref _targetVolatility, value); }

    private bool _useTargetVolatility = true;
    public bool UseTargetVolatility { get => _useTargetVolatility; set => SetProperty(ref _useTargetVolatility, value); }

    private int _rebalanceFrequencyDays = 5;
    public int RebalanceFrequencyDays { get => _rebalanceFrequencyDays; set => SetProperty(ref _rebalanceFrequencyDays, value); }

    // ── Run status ──────────────────────────────────────────────────────────

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            SetProperty(ref _isRunning, value);
            RaisePropertyChanged(nameof(CanRun));
        }
    }

    public bool CanRun => !_isRunning;

    private double _progressFraction;
    public double ProgressFraction { get => _progressFraction; set => SetProperty(ref _progressFraction, value); }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    // ── Equity curve ────────────────────────────────────────────────────────

    public ObservableCollection<EquityCurvePoint> EquityCurvePoints { get; } = [];

    // ── Performance metrics ─────────────────────────────────────────────────

    private string _totalReturn = "--";
    public string TotalReturn { get => _totalReturn; set => SetProperty(ref _totalReturn, value); }

    private string _annualisedReturn = "--";
    public string AnnualisedReturn { get => _annualisedReturn; set => SetProperty(ref _annualisedReturn, value); }

    private string _sharpeRatio = "--";
    public string SharpeRatio { get => _sharpeRatio; set => SetProperty(ref _sharpeRatio, value); }

    private string _sortinoRatio = "--";
    public string SortinoRatio { get => _sortinoRatio; set => SetProperty(ref _sortinoRatio, value); }

    private string _calmarRatio = "--";
    public string CalmarRatio { get => _calmarRatio; set => SetProperty(ref _calmarRatio, value); }

    private string _maxDrawdown = "--";
    public string MaxDrawdown { get => _maxDrawdown; set => SetProperty(ref _maxDrawdown, value); }

    private string _winRate = "--";
    public string WinRate { get => _winRate; set => SetProperty(ref _winRate, value); }

    private string _totalTrades = "--";
    public string TotalTrades { get => _totalTrades; set => SetProperty(ref _totalTrades, value); }

    private string _netPnl = "--";
    public string NetPnl { get => _netPnl; set => SetProperty(ref _netPnl, value); }

    private string _totalCommissions = "--";
    public string TotalCommissions { get => _totalCommissions; set => SetProperty(ref _totalCommissions, value); }

    private string _xirr = "--";
    public string Xirr { get => _xirr; set => SetProperty(ref _xirr, value); }

    private string _eventsProcessed = "--";
    public string EventsProcessed { get => _eventsProcessed; set => SetProperty(ref _eventsProcessed, value); }

    private string _elapsedTime = "--";
    public string ElapsedTime { get => _elapsedTime; set => SetProperty(ref _elapsedTime, value); }

    // ── Viability assessment ────────────────────────────────────────────────

    private string _viabilityGrade = "--";
    /// <summary>Letter grade: A / B / C / D / F or "--" before first run.</summary>
    public string ViabilityGrade { get => _viabilityGrade; set => SetProperty(ref _viabilityGrade, value); }

    private string _viabilityScore = "--";
    /// <summary>Composite score 0–100.</summary>
    public string ViabilityScore { get => _viabilityScore; set => SetProperty(ref _viabilityScore, value); }

    private string _viabilitySummary = "Run a backtest to assess carry trade viability.";
    public string ViabilitySummary { get => _viabilitySummary; set => SetProperty(ref _viabilitySummary, value); }

    private string _viabilityGradeColor = "#888888";
    /// <summary>Hex colour used to highlight the grade badge.</summary>
    public string ViabilityGradeColor { get => _viabilityGradeColor; set => SetProperty(ref _viabilityGradeColor, value); }

    // ── Attribution table ───────────────────────────────────────────────────

    public ObservableCollection<SymbolAttributionVm> Attribution { get; } = [];
    public ObservableCollection<FillEventVm> Fills { get; } = [];
    public ObservableCollection<CashFlowVm> CashFlows { get; } = [];

    // ── Run navigation ──────────────────────────────────────────────────────

    private string? _latestRecordedRunId;
    public string? LatestRecordedRunId { get => _latestRecordedRunId; set { SetProperty(ref _latestRecordedRunId, value); RaisePropertyChanged(nameof(HasLatestRecordedRun)); } }
    public bool HasLatestRecordedRun => !string.IsNullOrWhiteSpace(LatestRecordedRunId);

    // ── Commands ────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RunBacktestCommand { get; }
    public IRelayCommand CancelBacktestCommand { get; }
    public IRelayCommand AddSymbolCommand { get; }
    public IRelayCommand<string> RemoveSymbolCommand { get; }
    public IRelayCommand AddPresetFaangCommand { get; }
    public IRelayCommand AddPresetMagnificentSevenCommand { get; }
    public IRelayCommand AddPresetEtfsCommand { get; }
    public IRelayCommand ClearSymbolsCommand { get; }
    public IRelayCommand OpenRunBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand OpenRunPortfolioCommand { get; }

    public CarryTradeBacktestViewModel(
        BacktestService backtestService,
        NavigationService navigationService,
        StrategyRunWorkspaceService strategyRunWorkspaceService)
    {
        _backtestService = backtestService;
        _navigationService = navigationService;
        _strategyRunWorkspaceService = strategyRunWorkspaceService;

        _backtestService.BacktestCompleted += OnBacktestCompleted;
        _backtestService.BacktestCancelled += OnBacktestCancelled;

        RunBacktestCommand = new AsyncRelayCommand(RunBacktestAsync, () => CanRun);
        CancelBacktestCommand = new RelayCommand(CancelBacktest, () => IsRunning);
        AddSymbolCommand = new RelayCommand(AddSymbol, () => !string.IsNullOrWhiteSpace(NewSymbol));
        RemoveSymbolCommand = new RelayCommand<string>(RemoveSymbol);
        AddPresetFaangCommand = new RelayCommand(() => ApplyPreset(["META", "AAPL", "AMZN", "NFLX", "GOOGL"]));
        AddPresetMagnificentSevenCommand = new RelayCommand(() => ApplyPreset(["AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA"]));
        AddPresetEtfsCommand = new RelayCommand(() => ApplyPreset(["SPY", "QQQ", "IWM", "GLD", "TLT"]));
        ClearSymbolsCommand = new RelayCommand(SelectedSymbols.Clear);
        OpenRunBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(() => OpenRunSurface("RunDetail"), () => HasLatestRecordedRun);
        OpenRunPortfolioCommand = new RelayCommand(() => OpenRunSurface("RunPortfolio"), () => HasLatestRecordedRun);

        // Start with a sensible default set of symbols.
        ApplyPreset(["SPY", "QQQ", "GLD", "TLT", "IWM"]);
    }

    // ── Symbol management ───────────────────────────────────────────────────

    private void AddSymbol()
    {
        var sym = NewSymbol.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sym)) return;
        if (!SelectedSymbols.Contains(sym, StringComparer.OrdinalIgnoreCase))
            SelectedSymbols.Add(sym);
        NewSymbol = string.Empty;
    }

    private void RemoveSymbol(string? symbol)
    {
        if (symbol is null) return;
        var existing = SelectedSymbols.FirstOrDefault(s => string.Equals(s, symbol, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            SelectedSymbols.Remove(existing);
    }

    private void ApplyPreset(IEnumerable<string> symbols)
    {
        SelectedSymbols.Clear();
        foreach (var s in symbols)
            SelectedSymbols.Add(s.ToUpperInvariant());
    }

    // ── Run logic ───────────────────────────────────────────────────────────

    private async Task RunBacktestAsync(CancellationToken ct = default)
    {
        EquityCurvePoints.Clear();
        Fills.Clear();
        CashFlows.Clear();
        Attribution.Clear();
        ResetMetrics();
        StatusText = "Running…";
        ProgressFraction = 0;
        IsRunning = true;

        var symbols = SelectedSymbols.Count > 0
            ? (IReadOnlyList<string>)SelectedSymbols.ToList()
            : null;

        var request = new BacktestRequest(
            DateOnly.FromDateTime(FromDate),
            DateOnly.FromDateTime(ToDate),
            symbols,
            InitialCash,
            DataRoot: DataRoot,
            RiskFreeRate: RiskFreeRate);

        var config = BuildCarryConfiguration();
        var strategy = new CarryTradeBacktestStrategy(config, RebalanceFrequencyDays);

        var progress = new Progress<BacktestProgressEvent>(OnProgress);
        var result = await _backtestService.RunAsync(request, strategy, progress);
        if (result is not null)
            LatestRecordedRunId = await _strategyRunWorkspaceService.RecordBacktestRunAsync(request, strategy.Name, result);
    }

    private AdvancedCarryConfiguration BuildCarryConfiguration() =>
        new AdvancedCarryConfiguration(
            OptimizationMethod: OptimizationMethod,
            RiskAversion: RiskAversion,
            RiskFreeRate: RiskFreeRate,
            Risk: new AdvancedCarryRiskOptions(
                MaxSinglePosition: MaxSinglePosition,
                TargetVolatility: UseTargetVolatility ? TargetVolatility : null));

    private void CancelBacktest()
    {
        _backtestService.Cancel();
        StatusText = "Cancelling…";
    }

    // ── Progress & completion ───────────────────────────────────────────────

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
        Xirr = double.IsNaN(m.Xirr) ? "N/A" : $"{m.Xirr:P2}";
        EventsProcessed = $"{result.TotalEventsProcessed:N0}";
        ElapsedTime = $"{result.ElapsedTime.TotalSeconds:F1}s";

        foreach (var fill in result.Fills.Take(1000))
            Fills.Add(new FillEventVm(fill));

        foreach (var cf in result.CashFlows.Take(1000))
            CashFlows.Add(new CashFlowVm(cf));

        foreach (var (_, attr) in result.Metrics.SymbolAttribution)
            Attribution.Add(new SymbolAttributionVm(attr));

        ApplyViabilityAssessment(m);
    }

    // ── Viability assessment ────────────────────────────────────────────────

    /// <summary>
    /// Computes a composite viability score (0–100) and letter grade from backtest metrics,
    /// then populates the three viability display properties.
    /// </summary>
    private void ApplyViabilityAssessment(BacktestMetrics m)
    {
        // Score components (each 0–25):
        // 1. Sharpe ratio  (>1.5 → 25, >1.0 → 20, >0.5 → 12, >0 → 5, ≤0 → 0)
        var sharpeScore = m.SharpeRatio switch
        {
            >= 1.5 => 25.0,
            >= 1.0 => 20.0,
            >= 0.5 => 12.0,
            > 0.0  => 5.0,
            _      => 0.0
        };

        // 2. Max drawdown  (<10% → 25, <20% → 18, <30% → 10, <40% → 5, ≥40% → 0)
        var ddPct = Math.Abs((double)m.MaxDrawdownPercent);
        var drawdownScore = ddPct switch
        {
            < 0.10 => 25.0,
            < 0.20 => 18.0,
            < 0.30 => 10.0,
            < 0.40 => 5.0,
            _      => 0.0
        };

        // 3. Annualised return (>10% → 25, >6% → 18, >3% → 10, >0% → 5, ≤0% → 0)
        var annRet = (double)m.AnnualizedReturn;
        var returnScore = annRet switch
        {
            >= 0.10 => 25.0,
            >= 0.06 => 18.0,
            >= 0.03 => 10.0,
            > 0.0   => 5.0,
            _       => 0.0
        };

        // 4. Win rate  (>55% → 25, >50% → 18, >45% → 10, >40% → 5, ≤40% → 0)
        var winScore = m.WinRate switch
        {
            >= 0.55 => 25.0,
            >= 0.50 => 18.0,
            >= 0.45 => 10.0,
            >= 0.40 => 5.0,
            _       => 0.0
        };

        var composite = sharpeScore + drawdownScore + returnScore + winScore;
        ViabilityScore = $"{composite:F0} / 100";

        var annRetFmt = m.AnnualizedReturn.ToString("P1");
        var ddFmt = m.MaxDrawdownPercent.ToString("P1");
        var sharpeFmt = m.SharpeRatio.ToString("F2");

        var (grade, color, summary) = composite switch
        {
            >= 80 => ("A", "#22c55e", $"Excellent carry viability. Sharpe {sharpeFmt}, annualised return {annRetFmt}, max drawdown {ddFmt}."),
            >= 65 => ("B", "#84cc16", $"Good carry viability. The strategy shows solid risk-adjusted returns. Sharpe {sharpeFmt}, drawdown {ddFmt}."),
            >= 50 => ("C", "#eab308", $"Moderate carry viability. Returns are acceptable but risk-adjusted performance could be improved. Sharpe {sharpeFmt}."),
            >= 35 => ("D", "#f97316", $"Below-average carry viability. Consider adjusting the universe or risk parameters. Sharpe {sharpeFmt}, drawdown {ddFmt}."),
            _     => ("F", "#ef4444", $"Poor carry viability. The strategy did not generate sufficient risk-adjusted returns. Sharpe {sharpeFmt}.")
        };

        ViabilityGrade = grade;
        ViabilityGradeColor = color;
        ViabilitySummary = summary;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void ResetMetrics()
    {
        TotalReturn = AnnualisedReturn = SharpeRatio = SortinoRatio = CalmarRatio =
        MaxDrawdown = WinRate = TotalTrades = NetPnl = TotalCommissions = Xirr =
        EventsProcessed = ElapsedTime = "--";
        ViabilityGrade = "--";
        ViabilityScore = "--";
        ViabilityGradeColor = "#888888";
        ViabilitySummary = "Run a backtest to assess carry trade viability.";
    }

    private void OpenRunSurface(string pageTag)
    {
        if (!string.IsNullOrWhiteSpace(LatestRecordedRunId))
            _navigationService.NavigateTo(pageTag, LatestRecordedRunId);
    }

    public void Dispose()
    {
        _backtestService.BacktestCompleted -= OnBacktestCompleted;
        _backtestService.BacktestCancelled -= OnBacktestCancelled;
    }
}
