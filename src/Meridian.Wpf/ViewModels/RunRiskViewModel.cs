using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.QuantScript.Plotting;
using Meridian.Wpf.Services;
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
    /// Rolling 21-day annualised volatility time series for the <see cref="PlotRenderBehavior"/>.
    /// </summary>
    public PlotRequest? RollingVolatilityPlot
    {
        get => _rollingVolatilityPlot;
        private set => SetProperty(ref _rollingVolatilityPlot, value);
    }

    private PlotRequest? _attributionPlot;

    /// <summary>
    /// Per-symbol total PnL as a multiline or bar chart.
    /// </summary>
    public PlotRequest? AttributionPlot
    {
        get => _attributionPlot;
        private set => SetProperty(ref _attributionPlot, value);
    }

    // ── Attribution grid ──────────────────────────────────────────────────────

    public ObservableCollection<RiskAttributionRow> Attribution { get; } = [];

    // ── Commands ──────────────────────────────────────────────────────────────

    public IRelayCommand OpenBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenCashFlowCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public RunRiskViewModel(
        StrategyRunWorkspaceService workspaceService,
        StrategyRunReadService readService,
        NavigationService navigationService)
    {
        _workspaceService = workspaceService;
        _readService = readService;
        _navigationService = navigationService;

        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => !string.IsNullOrWhiteSpace(_runId));
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => !string.IsNullOrWhiteSpace(_runId));
        OpenCashFlowCommand = new RelayCommand(OpenCashFlow, () => !string.IsNullOrWhiteSpace(_runId));
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
    {
        var runId = parameter as string;
        if (string.IsNullOrWhiteSpace(runId))
        {
            StatusText = "Select a strategy run from the browser.";
            return;
        }

        _runId = runId;
        NotifyCommandsCanExecuteChanged();

        // Await without ConfigureAwait(false) so ApplyDetail runs on the UI thread.
        var (detail, curve, attribution) = await LoadRunDataAsync(runId, ct);

        if (detail is null)
        {
            StatusText = $"Strategy run '{runId}' was not found.";
            return;
        }

        ApplyDetail(detail, curve, attribution);
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

        // ── Risk metrics ──────────────────────────────────────────────────────
        if (curve is not null)
        {
            var fullVolatility = ComputeAnnualisedVolatility(curve.Points);
            TotalRiskText = double.IsNaN(fullVolatility) ? "-" : fullVolatility.ToString("P2");
            SharpeText = curve.SharpeRatio.ToString("F2");
            MaxDrawdownText = curve.MaxDrawdownPercent != 0m
                ? curve.MaxDrawdownPercent.ToString("P2")
                : "-";

            RollingVolatilityPlot = BuildVolatilityPlot(curve.Points);
        }
        else
        {
            TotalRiskText = "-";
            SharpeText = "-";
            MaxDrawdownText = "-";
        }

        // ── Attribution grid & chart ──────────────────────────────────────────
        Attribution.Clear();
        if (attribution is not null)
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
        }

        NotifyCommandsCanExecuteChanged();
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
    }

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
