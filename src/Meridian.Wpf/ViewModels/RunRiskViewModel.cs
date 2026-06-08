using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.QuantScript.Plotting;
using Meridian.Strategies.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;
using CurvePt = Meridian.Contracts.Workstation.EquityCurvePoint;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the strategy run risk analytics page.
/// Presents rolling annualised volatility, portfolio exposure metrics,
/// and per-symbol P&amp;L attribution for a completed strategy run.
/// </summary>
public sealed class RunRiskViewModel : BindableBase
{
    private readonly StrategyRunWorkspaceService _workspaceService;
    private readonly StrategyRunReadService _readService;
    private readonly NavigationService _navigationService;

    private string? _runId;
    private object? _parameter;
    private bool _isLoading;
    private string _riskEmptyStateTitle = "Select a run to calculate historical risk";
    private string _riskEmptyStateDetail = "Open the run browser and choose a completed run with retained equity snapshots.";
    private string _attributionEmptyStateTitle = "Select a run to inspect attribution";
    private string _attributionEmptyStateDetail = "Open the run browser and choose a completed run with retained symbol attribution.";
    private string _runActionStateTitle = "Select a retained run";
    private string _runActionStateDetail = "Run Detail, Portfolio, and Cash Flow unlock after selecting a retained strategy run.";
    private RiskAttributionRow? _selectedAttribution;
    private InspectorPanelModel _selectedAttributionInspector = BuildEmptyAttributionInspector();

    // ── Navigation parameter ──────────────────────────────────────────────────

    public object? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value))
                _ = LoadFromParameterAsync(value);
        }
    }

    // ── Header / title ────────────────────────────────────────────────────────

    private string _title = "Portfolio Risk";
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string _statusText = "Select a strategy run from the browser.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaiseAnalysisStateChanged();
                RaiseRunActionStateChanged();
                NotifyCommandsCanExecuteChanged();
            }
        }
    }

    // ── Summary metric cards ──────────────────────────────────────────────────

    private string _positionCountText = "-";
    public string PositionCountText
    {
        get => _positionCountText;
        private set => SetProperty(ref _positionCountText, value);
    }

    private string _longExposureText = "-";
    public string LongExposureText
    {
        get => _longExposureText;
        private set => SetProperty(ref _longExposureText, value);
    }

    private string _shortExposureText = "-";
    public string ShortExposureText
    {
        get => _shortExposureText;
        private set => SetProperty(ref _shortExposureText, value);
    }

    private string _netExposureText = "-";
    public string NetExposureText
    {
        get => _netExposureText;
        private set => SetProperty(ref _netExposureText, value);
    }

    private string _totalRiskText = "-";

    /// <summary>Annualised volatility computed from daily returns (ex-post).</summary>
    public string TotalRiskText
    {
        get => _totalRiskText;
        private set => SetProperty(ref _totalRiskText, value);
    }

    private string _equityText = "-";
    public string EquityText
    {
        get => _equityText;
        private set => SetProperty(ref _equityText, value);
    }

    private string _sharpeText = "-";
    public string SharpeText
    {
        get => _sharpeText;
        private set => SetProperty(ref _sharpeText, value);
    }

    private string _maxDrawdownText = "-";
    public string MaxDrawdownText
    {
        get => _maxDrawdownText;
        private set => SetProperty(ref _maxDrawdownText, value);
    }

    // ── Chart plot requests ───────────────────────────────────────────────────

    private PlotRequest? _rollingVolatilityPlot;

    /// <summary>
    /// Rolling 21-day annualised volatility time series for the plot render behavior.
    /// </summary>
    public PlotRequest? RollingVolatilityPlot
    {
        get => _rollingVolatilityPlot;
        private set
        {
            if (SetProperty(ref _rollingVolatilityPlot, value))
                RaiseAnalysisStateChanged();
        }
    }

    private PlotRequest? _attributionPlot;

    /// <summary>
    /// Per-symbol total PnL as a multiline or bar chart.
    /// </summary>
    public PlotRequest? AttributionPlot
    {
        get => _attributionPlot;
        private set
        {
            if (SetProperty(ref _attributionPlot, value))
                RaiseAnalysisStateChanged();
        }
    }

    // ── Attribution grid ──────────────────────────────────────────────────────

    public ObservableCollection<RiskAttributionRow> Attribution { get; } = [];

    public WorkstationTableModel<RiskAttributionRow> AttributionTable { get; }

    public RiskAttributionRow? SelectedAttribution
    {
        get => _selectedAttribution;
        set
        {
            if (SetProperty(ref _selectedAttribution, value))
            {
                SelectedAttributionInspector = value is null
                    ? BuildEmptyAttributionInspector()
                    : BuildAttributionInspector(value);
                RaiseSelectedAttributionStateChanged();
            }
        }
    }

    public InspectorPanelModel SelectedAttributionInspector
    {
        get => _selectedAttributionInspector;
        private set => SetProperty(ref _selectedAttributionInspector, value);
    }

    public bool HasSelectedAttribution => SelectedAttribution is not null;

    public bool CanOpenSelectedSymbol => !string.IsNullOrWhiteSpace(SelectedAttribution?.Symbol);

    public string RunDrillInTooltip => string.IsNullOrWhiteSpace(_runId)
        ? "Select a retained strategy run before opening related run drill-ins."
        : $"Open retained run drill-ins for {_runId}.";

    public string SelectedSymbolTooltip => SelectedAttribution switch
    {
        null => "Select an attribution row before opening Security Master.",
        { Symbol: { Length: > 0 } symbol } => $"Open Security Master lookup for {symbol}.",
        _ => "Select a retained symbol attribution row before opening Security Master."
    };

    public bool HasRiskChart => RollingVolatilityPlot?.Series is { Count: > 0 };

    public bool IsRiskEmptyStateVisible => !HasRiskChart;

    public string RiskEmptyStateTitle
    {
        get => _riskEmptyStateTitle;
        private set => SetProperty(ref _riskEmptyStateTitle, value);
    }

    public string RiskEmptyStateDetail
    {
        get => _riskEmptyStateDetail;
        private set => SetProperty(ref _riskEmptyStateDetail, value);
    }

    public bool HasAttributionRows => Attribution.Count > 0;

    public bool IsAttributionEmptyStateVisible => !HasAttributionRows;

    public string AttributionEmptyStateTitle
    {
        get => _attributionEmptyStateTitle;
        private set => SetProperty(ref _attributionEmptyStateTitle, value);
    }

    public string AttributionEmptyStateDetail
    {
        get => _attributionEmptyStateDetail;
        private set => SetProperty(ref _attributionEmptyStateDetail, value);
    }

    public bool CanOpenRunDrillIns => !IsLoading && !string.IsNullOrWhiteSpace(_runId);

    public string RunActionStateTitle
    {
        get => _runActionStateTitle;
        private set => SetProperty(ref _runActionStateTitle, value);
    }

    public string RunActionStateDetail
    {
        get => _runActionStateDetail;
        private set => SetProperty(ref _runActionStateDetail, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public IRelayCommand OpenBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenCashFlowCommand { get; }
    public IRelayCommand OpenSelectedSymbolCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public RunRiskViewModel(
        StrategyRunWorkspaceService workspaceService,
        StrategyRunReadService readService,
        NavigationService navigationService)
    {
        _workspaceService = workspaceService;
        _readService = readService;
        _navigationService = navigationService;

        AttributionTable = new WorkstationTableModel<RiskAttributionRow>(
            Attribution,
            [
                new("Symbol", nameof(RiskAttributionRow.Symbol), 90),
                new("Total PnL", nameof(RiskAttributionRow.TotalPnlText), 115),
                new("Realized", nameof(RiskAttributionRow.RealizedPnlText), 115),
                new("Unrealized", nameof(RiskAttributionRow.UnrealizedPnlText), 125),
                new("Trades", nameof(RiskAttributionRow.TradeCountText), 75),
                new("Commissions", nameof(RiskAttributionRow.CommissionsText), 115)
            ],
            "Symbol attribution",
            "No symbol attribution retained",
            "Select a retained strategy run with per-symbol attribution before reviewing contributors.");

        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => CanOpenRunDrillIns);
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => CanOpenRunDrillIns);
        OpenCashFlowCommand = new RelayCommand(OpenCashFlow, () => CanOpenRunDrillIns);
        OpenSelectedSymbolCommand = new RelayCommand(OpenSelectedSymbol, () => CanOpenSelectedSymbol);
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    public Task LoadRunAsync(string? runId, CancellationToken ct = default)
        => LoadRunDataForRunIdAsync(runId, ct);

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
        => await LoadRunDataForRunIdAsync(parameter as string, ct).ConfigureAwait(false);

    private async Task LoadRunDataForRunIdAsync(string? runId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            ApplyNoRunSelectedState();
            return;
        }

        ApplyLoadingState(runId);
        try
        {
            // Await without ConfigureAwait(false) so ApplyDetail runs on the UI thread.
            var (detail, curve, attribution) = await LoadRunDataAsync(runId, ct);

            if (detail is null)
            {
                ApplyRunUnavailableState(runId);
                return;
            }

            _runId = runId;
            ApplyDetail(detail, curve, attribution);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyLoadFailedState(runId, ex);
        }
        finally
        {
            IsLoading = false;
            NotifyCommandsCanExecuteChanged();
        }
    }

    private async Task<(StrategyRunDetail?, EquityCurveSummary?, RunAttributionSummary?)> LoadRunDataAsync(
        string runId,
        CancellationToken ct)
    {
        var detailTask = _workspaceService.GetRunDetailAsync(runId, ct);
        var curveTask = _readService.GetEquityCurveAsync(runId, ct);
        var attributionTask = _readService.GetAttributionAsync(runId, ct);

        await Task.WhenAll(detailTask, curveTask, attributionTask).ConfigureAwait(false);

        return (await detailTask.ConfigureAwait(false),
                await curveTask.ConfigureAwait(false),
                await attributionTask.ConfigureAwait(false));
    }

    private void ApplyDetail(
        StrategyRunDetail detail,
        EquityCurveSummary? curve,
        RunAttributionSummary? attribution)
    {
        Title = $"{detail.Summary.StrategyName} — Risk Analytics";
        StatusText = $"{detail.Summary.Mode} {detail.Summary.Status} · {detail.Summary.StartedAt.LocalDateTime:d}";

        // ── Exposure metrics (from portfolio snapshot) ────────────────────────
        var portfolio = detail.Portfolio;
        PositionCountText = portfolio is not null
            ? portfolio.Positions.Count.ToString("N0")
            : "-";
        LongExposureText = portfolio is not null
            ? FormatCurrency(portfolio.LongMarketValue)
            : "-";
        ShortExposureText = portfolio is not null
            ? FormatCurrency(portfolio.ShortMarketValue)
            : "-";
        NetExposureText = portfolio is not null
            ? FormatCurrency(portfolio.NetExposure)
            : "-";
        EquityText = FormatCurrency(detail.Summary.FinalEquity);
        RunActionStateTitle = "Run drill-ins ready";
        RunActionStateDetail = $"Open detail, portfolio, or cash-flow review for run {detail.Summary.RunId}.";

        // ── Risk metrics ──────────────────────────────────────────────────────
        if (curve is not null)
        {
            var fullVolatility = ComputeAnnualisedVolatility(curve.Points);
            TotalRiskText = double.IsNaN(fullVolatility) ? "-" : fullVolatility.ToString("P2");
            SharpeText = curve.SharpeRatio.ToString("F2");
            MaxDrawdownText = curve.MaxDrawdownPercent != 0m
                ? curve.MaxDrawdownPercent.ToString("P2")
                : "-";

            var volatilityPlot = BuildVolatilityPlot(curve.Points);
            if (volatilityPlot.Series is { Count: > 0 })
            {
                RollingVolatilityPlot = volatilityPlot;
            }
            else
            {
                RollingVolatilityPlot = null;
                RiskEmptyStateTitle = curve.Points.Count == 0
                    ? "No equity curve retained"
                    : "Rolling volatility needs more history";
                RiskEmptyStateDetail = curve.Points.Count == 0
                    ? $"Run {detail.Summary.RunId} loaded, but it has no retained equity snapshots for the risk chart."
                    : $"Run {detail.Summary.RunId} has {curve.Points.Count:N0} retained equity point{(curve.Points.Count == 1 ? string.Empty : "s")}. The rolling volatility chart needs at least 21 observations.";
            }
        }
        else
        {
            TotalRiskText = "-";
            SharpeText = "-";
            MaxDrawdownText = "-";
            RollingVolatilityPlot = null;
            RiskEmptyStateTitle = "No equity curve retained";
            RiskEmptyStateDetail = $"Run {detail.Summary.RunId} loaded, but no equity curve snapshot is available for risk plotting.";
        }

        // ── Attribution grid & chart ──────────────────────────────────────────
        Attribution.Clear();
        if (attribution?.BySymbol is { Count: > 0 })
        {
            foreach (var entry in attribution.BySymbol)
            {
                Attribution.Add(new RiskAttributionRow(
                    Symbol: entry.Symbol,
                    TotalPnlText: FormatCurrency(entry.TotalPnl),
                    RealizedPnlText: FormatCurrency(entry.RealizedPnl),
                    UnrealizedPnlText: FormatCurrency(entry.UnrealizedPnl),
                    TradeCountText: entry.TradeCount.ToString("N0"),
                    CommissionsText: FormatCurrency(entry.Commissions)));
            }

            AttributionPlot = BuildAttributionPlot(attribution, detail);
            SelectedAttribution = Attribution.FirstOrDefault();
        }
        else
        {
            AttributionPlot = null;
            AttributionEmptyStateTitle = "No symbol attribution retained";
            AttributionEmptyStateDetail = $"Run {detail.Summary.RunId} loaded, but no per-symbol attribution is available for review.";
            SelectedAttribution = null;
        }

        RaiseAnalysisStateChanged();
        NotifyCommandsCanExecuteChanged();
    }

    private void ApplyNoRunSelectedState()
    {
        ResetAnalysisState();
        Title = "Portfolio Risk";
        StatusText = "Select a strategy run from the browser.";
        RunActionStateTitle = "Select a retained run";
        RunActionStateDetail = "Run Detail, Portfolio, and Cash Flow unlock after selecting a retained strategy run.";
        RiskEmptyStateTitle = "Select a run to calculate historical risk";
        RiskEmptyStateDetail = "Open the run browser and choose a completed run with retained equity snapshots.";
        AttributionEmptyStateTitle = "Select a run to inspect attribution";
        AttributionEmptyStateDetail = "Open the run browser and choose a completed run with retained symbol attribution.";
        IsLoading = false;
        NotifyCommandsCanExecuteChanged();
    }

    private void ApplyLoadingState(string runId)
    {
        ResetAnalysisState();
        StatusText = $"Loading risk analytics for run '{runId}'...";
        RunActionStateTitle = "Loading run evidence";
        RunActionStateDetail = "Drill-ins stay disabled until retained detail, equity history, and attribution evidence finish loading.";
        RiskEmptyStateTitle = "Loading risk history";
        RiskEmptyStateDetail = "Loading the retained equity curve before rendering rolling volatility.";
        AttributionEmptyStateTitle = "Loading attribution";
        AttributionEmptyStateDetail = "Loading retained per-symbol attribution before rendering the review grid.";
        IsLoading = true;
    }

    private void ApplyRunUnavailableState(string runId)
    {
        ResetAnalysisState();
        StatusText = $"Strategy run '{runId}' was not found.";
        RunActionStateTitle = "Run evidence unavailable";
        RunActionStateDetail = "Open the run browser and select a retained run to enable detail, portfolio, and cash-flow drill-ins.";
        RiskEmptyStateTitle = "Risk data unavailable";
        RiskEmptyStateDetail = $"No retained strategy run matched '{runId}'. Return to the run browser and select a recorded run.";
        AttributionEmptyStateTitle = "Attribution unavailable";
        AttributionEmptyStateDetail = $"No retained strategy run matched '{runId}', so symbol attribution cannot be loaded.";
    }

    private void ApplyLoadFailedState(string runId, Exception exception)
    {
        ResetAnalysisState();
        StatusText = $"Could not load risk analytics for run '{runId}'.";
        RunActionStateTitle = "Risk load failed";
        RunActionStateDetail = "Resolve the load error, then reopen a retained run before drilling into related pages.";
        RiskEmptyStateTitle = "Risk analytics failed to load";
        RiskEmptyStateDetail = $"The retained risk data could not be loaded: {exception.Message}";
        AttributionEmptyStateTitle = "Attribution failed to load";
        AttributionEmptyStateDetail = "Resolve the load error above, then reopen the run from the run browser.";
    }

    private void ResetAnalysisState()
    {
        _runId = null;
        PositionCountText = "-";
        LongExposureText = "-";
        ShortExposureText = "-";
        NetExposureText = "-";
        TotalRiskText = "-";
        EquityText = "-";
        SharpeText = "-";
        MaxDrawdownText = "-";
        RollingVolatilityPlot = null;
        AttributionPlot = null;
        Attribution.Clear();
        SelectedAttribution = null;
        RaiseAnalysisStateChanged();
    }

    // ── Volatility computation ────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="PlotRequest"/> showing the 21-day rolling annualised volatility
    /// derived from the equity curve's daily returns.
    /// </summary>
    private static PlotRequest BuildVolatilityPlot(IReadOnlyList<CurvePt> points, int window = 21)
    {
        var series = new List<(DateOnly Date, double Value)>(points.Count);

        for (var i = window - 1; i < points.Count; i++)
        {
            var vol = ComputeRollingWindow(points, i, window);
            if (!double.IsNaN(vol))
                series.Add((points[i].Date, vol * 100.0));
        }

        return new PlotRequest(
            Title: "Rolling Volatility",
            Type: PlotType.Line,
            Series: series);
    }

    private static double ComputeRollingWindow(IReadOnlyList<CurvePt> points, int endIndex, int window)
    {
        var start = endIndex - window + 1;
        var sum = 0.0;

        for (var i = start; i <= endIndex; i++)
            sum += (double)points[i].DailyReturn;

        var mean = sum / window;

        var sumSq = 0.0;
        for (var i = start; i <= endIndex; i++)
        {
            var diff = (double)points[i].DailyReturn - mean;
            sumSq += diff * diff;
        }

        if (window < 2)
            return double.NaN;

        return Math.Sqrt(sumSq / (window - 1) * 252.0);
    }

    /// <summary>
    /// Computes the full-period annualised volatility from all daily return observations
    /// using a single-pass algorithm to avoid double-iteration and intermediate allocations.
    /// </summary>
    private static double ComputeAnnualisedVolatility(IReadOnlyList<CurvePt> points)
    {
        if (points.Count < 2)
            return double.NaN;

        var n = 0;
        var mean = 0.0;
        var m2 = 0.0;

        foreach (var p in points)
        {
            n++;
            var x = (double)p.DailyReturn;
            var delta = x - mean;
            mean += delta / n;
            m2 += delta * (x - mean);
        }

        var variance = m2 / (n - 1);
        return Math.Sqrt(variance * 252.0);
    }

    // ── Attribution chart ─────────────────────────────────────────────────────

    private static PlotRequest BuildAttributionPlot(RunAttributionSummary attribution, StrategyRunDetail detail)
    {
        // Show each symbol as a two-point segment so colours distinguish contributors.
        // X-axis is anchored to the actual run start/end dates.
        var startDate = DateOnly.FromDateTime(detail.Summary.StartedAt.LocalDateTime);
        var endDate = detail.Summary.CompletedAt.HasValue
            ? DateOnly.FromDateTime(detail.Summary.CompletedAt.Value.LocalDateTime)
            : startDate.AddDays(1);

        var multiSeries = attribution.BySymbol
            .Take(10)
            .Select(entry =>
            {
                IReadOnlyList<(DateOnly Date, double Value)> values =
                [
                    (startDate, (double)entry.TotalPnl),
                    (endDate, (double)entry.TotalPnl)
                ];
                return (Label: entry.Symbol, Values: values);
            })
            .ToList();

        return new PlotRequest(
            Title: "Symbol Attribution",
            Type: PlotType.MultiLine,
            MultiSeries: multiSeries);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void NotifyCommandsCanExecuteChanged()
    {
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
        OpenSelectedSymbolCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenRunDrillIns));
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    private void RaiseAnalysisStateChanged()
    {
        OnPropertyChanged(nameof(HasRiskChart));
        OnPropertyChanged(nameof(IsRiskEmptyStateVisible));
        OnPropertyChanged(nameof(HasAttributionRows));
        OnPropertyChanged(nameof(IsAttributionEmptyStateVisible));
    }

    private void RaiseRunActionStateChanged()
    {
        OnPropertyChanged(nameof(CanOpenRunDrillIns));
        OnPropertyChanged(nameof(RunActionStateTitle));
        OnPropertyChanged(nameof(RunActionStateDetail));
    }

    private void RaiseSelectedAttributionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedAttribution));
        OnPropertyChanged(nameof(CanOpenSelectedSymbol));
        OnPropertyChanged(nameof(SelectedSymbolTooltip));
        OpenSelectedSymbolCommand.NotifyCanExecuteChanged();
    }

    internal static InspectorPanelModel BuildAttributionInspector(RiskAttributionRow selected)
        => new()
        {
            Title = selected.Symbol,
            Subtitle = "Symbol attribution",
            Detail = "Review retained PnL attribution before opening Security Master lookup for this symbol.",
            Badge = new WorkstationBadgeModel("Symbol", selected.Symbol, "\uE8A5", WorkspaceTone.Info),
            Facts =
            [
                new("Total PnL", selected.TotalPnlText),
                new("Realized PnL", selected.RealizedPnlText),
                new("Unrealized PnL", selected.UnrealizedPnlText),
                new("Trades", selected.TradeCountText),
                new("Commissions", selected.CommissionsText),
                new("Source", "Retained run attribution")
            ]
        };

    private static InspectorPanelModel BuildEmptyAttributionInspector()
        => new()
        {
            Title = "No attribution symbol selected",
            Subtitle = "Run risk inspector",
            Detail = "Select a retained symbol attribution row to review PnL contribution and open Security Master lookup."
        };

    private void OpenRunDetail()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
            _navigationService.NavigateTo("RunDetail", _runId);
    }

    private void OpenPortfolio()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
            _navigationService.NavigateTo("RunPortfolio", _runId);
    }

    private void OpenCashFlow()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
            _navigationService.NavigateTo("RunCashFlow", _runId);
    }

    private void OpenSelectedSymbol()
    {
        if (!string.IsNullOrWhiteSpace(SelectedAttribution?.Symbol))
        {
            _navigationService.NavigateTo("SecurityMaster", SelectedAttribution.Symbol);
        }
    }

    private static string FormatCurrency(decimal? value) =>
        value.HasValue ? value.Value.ToString("C2") : "-";
}

/// <summary>Row in the attribution data grid on the risk page.</summary>
public sealed record RiskAttributionRow(
    string Symbol,
    string TotalPnlText,
    string RealizedPnlText,
    string UnrealizedPnlText,
    string TradeCountText,
    string CommissionsText);
