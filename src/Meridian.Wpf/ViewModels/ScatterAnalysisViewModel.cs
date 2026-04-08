using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>A single (X, Y) observation with its date label.</summary>
public sealed record ScatterPoint(double X, double Y, DateOnly Date);

/// <summary>One row in the data-sheet tab.</summary>
public sealed record DataSheetRow(string Date, string XValue, string YValue);

/// <summary>
/// A pre-built scatter template shown in the Quickstart sidebar.
/// Clicking a template populates both symbol inputs and fires PlotAsync.
/// </summary>
public sealed record QuickstartTemplate(
    string Name,
    string Category,
    string XSymbol,
    string YSymbol,
    string XExpression,
    string YExpression,
    string TimeRange = "1Y");

/// <summary>
/// ViewModel for the Scatter Analysis page — a bivariate scatter plot with expression
/// inputs, time-range selection, linear regression, and statistics, inspired by the
/// Goldman Sachs Marquee PlotTool scatter workflow.
/// </summary>
public sealed class ScatterAnalysisViewModel : BindableBase
{
    private readonly BackfillService _backfillService;

    // ── Inputs ───────────────────────────────────────────────────────────────
    private string _xSymbol = "SPY";
    private string _ySymbol = "QQQ";
    private string _xExpression = "SPY.close()";
    private string _yExpression = "QQQ.close()";
    private string _selectedTimeRange = "1Y";

    // ── Chart / status ────────────────────────────────────────────────────────
    private bool _isBusy;
    private string _chartTitle = "Scatter Analysis";
    private Visibility _noDataVisible = Visibility.Visible;
    private Visibility _chartVisible  = Visibility.Collapsed;
    private Visibility _loadingVisible = Visibility.Collapsed;
    private string _statusMessage = "Enter symbols and click Plot to begin.";

    // ── Statistics ────────────────────────────────────────────────────────────
    private string _correlationText = "--";
    private string _rSquaredText = "--";
    private string _slopeText = "--";
    private string _interceptText = "--";
    private string _countText = "0";
    private string _xMeanText = "--";
    private string _yMeanText = "--";
    private string _xStdDevText = "--";
    private string _yStdDevText = "--";

    // ── Data ──────────────────────────────────────────────────────────────────
    private IReadOnlyList<ScatterPoint> _historyPoints = [];
    private ScatterPoint? _currentPoint;
    private (double X1, double Y1, double X2, double Y2)? _regressionLine;

    // ── Static lookup ─────────────────────────────────────────────────────────
    public static IReadOnlyList<string> TimeRanges { get; } = ["1M", "3M", "6M", "1Y", "3Y", "MAX"];

    /// <summary>
    /// Pre-built scatter templates shown in the Quickstart sidebar, modelled after the
    /// Goldman Sachs Marquee PlotTool quickstart workflow.
    /// </summary>
    public static IReadOnlyList<QuickstartTemplate> QuickstartTemplates { get; } =
    [
        new("SPY vs QQQ — Getting Started",  "Quickstart", "SPY",  "QQQ",  "SPY.close()",  "QQQ.close()"),
        new("Equity vs Rates",               "Quickstart", "SPY",  "TLT",  "SPY.close()",  "TLT.close()"),
        new("Growth vs Value",               "Quickstart", "QQQ",  "IWD",  "QQQ.close()",  "IWD.close()"),
        new("Large Cap vs Small Cap",        "Quickstart", "SPY",  "IWM",  "SPY.close()",  "IWM.close()"),
        new("US vs International Dev",       "Quickstart", "SPY",  "EFA",  "SPY.close()",  "EFA.close()"),
        new("Developed vs Emerging Mkts",    "Quickstart", "EFA",  "EEM",  "EFA.close()",  "EEM.close()"),
        new("Credit vs Equity",              "Quickstart", "HYG",  "SPY",  "HYG.close()",  "SPY.close()"),
        new("Tech vs Financials",            "Quickstart", "XLK",  "XLF",  "XLK.close()",  "XLF.close()"),
        new("Energy vs Utilities",           "Quickstart", "XLE",  "XLU",  "XLE.close()",  "XLU.close()"),
        new("Equity vs Volatility",          "Quickstart", "SPY",  "VIXY", "SPY.close()",  "VIXY.close()"),
        new("Gold vs Long Bonds",            "Quickstart", "GLD",  "TLT",  "GLD.close()",  "TLT.close()"),
        new("Momentum vs Quality",           "Quickstart", "MTUM", "QUAL", "MTUM.close()", "QUAL.close()"),
    ];

    public ScatterAnalysisViewModel(BackfillService backfillService)
    {
        _backfillService = backfillService ?? throw new ArgumentNullException(nameof(backfillService));
        PlotCommand            = new AsyncRelayCommand(PlotAsync);
        ApplyTemplateCommand   = new RelayCommand<QuickstartTemplate>(ApplyTemplate);
    }

    // ── Input properties ──────────────────────────────────────────────────────

    public string XSymbol
    {
        get => _xSymbol;
        set
        {
            if (SetProperty(ref _xSymbol, value))
                SyncExpressions();
        }
    }

    public string YSymbol
    {
        get => _ySymbol;
        set
        {
            if (SetProperty(ref _ySymbol, value))
                SyncExpressions();
        }
    }

    public string XExpression
    {
        get => _xExpression;
        set => SetProperty(ref _xExpression, value);
    }

    public string YExpression
    {
        get => _yExpression;
        set => SetProperty(ref _yExpression, value);
    }

    public string SelectedTimeRange
    {
        get => _selectedTimeRange;
        set
        {
            if (SetProperty(ref _selectedTimeRange, value))
                PlotCommand.ExecuteAsync(null);
        }
    }

    // ── Chart state properties ────────────────────────────────────────────────

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string ChartTitle
    {
        get => _chartTitle;
        private set => SetProperty(ref _chartTitle, value);
    }

    public Visibility NoDataVisible
    {
        get => _noDataVisible;
        private set => SetProperty(ref _noDataVisible, value);
    }

    public Visibility ChartVisible
    {
        get => _chartVisible;
        private set => SetProperty(ref _chartVisible, value);
    }

    public Visibility LoadingVisible
    {
        get => _loadingVisible;
        private set => SetProperty(ref _loadingVisible, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // ── Statistics properties ─────────────────────────────────────────────────

    public string CorrelationText { get => _correlationText; private set => SetProperty(ref _correlationText, value); }
    public string RSquaredText    { get => _rSquaredText;    private set => SetProperty(ref _rSquaredText, value); }
    public string SlopeText       { get => _slopeText;       private set => SetProperty(ref _slopeText, value); }
    public string InterceptText   { get => _interceptText;   private set => SetProperty(ref _interceptText, value); }
    public string CountText       { get => _countText;       private set => SetProperty(ref _countText, value); }
    public string XMeanText       { get => _xMeanText;       private set => SetProperty(ref _xMeanText, value); }
    public string YMeanText       { get => _yMeanText;       private set => SetProperty(ref _yMeanText, value); }
    public string XStdDevText     { get => _xStdDevText;     private set => SetProperty(ref _xStdDevText, value); }
    public string YStdDevText     { get => _yStdDevText;     private set => SetProperty(ref _yStdDevText, value); }

    // ── Data properties ───────────────────────────────────────────────────────

    public IReadOnlyList<ScatterPoint> HistoryPoints
    {
        get => _historyPoints;
        private set => SetProperty(ref _historyPoints, value);
    }

    public ScatterPoint? CurrentPoint
    {
        get => _currentPoint;
        private set => SetProperty(ref _currentPoint, value);
    }

    public (double X1, double Y1, double X2, double Y2)? RegressionLine
    {
        get => _regressionLine;
        private set => SetProperty(ref _regressionLine, value);
    }

    public ObservableCollection<DataSheetRow> DataSheetRows { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────

    public AsyncRelayCommand PlotCommand { get; }

    /// <summary>Loads a Quickstart template into the symbol inputs and immediately plots.</summary>
    public RelayCommand<QuickstartTemplate> ApplyTemplateCommand { get; }

    /// <summary>Raised on the UI thread whenever new chart data is ready to render.</summary>
    public event EventHandler? ChartDataReady;

    // ── Implementation ────────────────────────────────────────────────────────

    private void ApplyTemplate(QuickstartTemplate? template)
    {
        if (template is null) return;

        // Batch-update backing fields so only one PlotAsync is triggered, not one per setter.
        _xSymbol         = template.XSymbol;
        _ySymbol         = template.YSymbol;
        _xExpression     = template.XExpression;
        _yExpression     = template.YExpression;
        _selectedTimeRange = template.TimeRange;

        OnPropertyChanged(nameof(XSymbol));
        OnPropertyChanged(nameof(YSymbol));
        OnPropertyChanged(nameof(XExpression));
        OnPropertyChanged(nameof(YExpression));
        OnPropertyChanged(nameof(SelectedTimeRange));

        PlotCommand.ExecuteAsync(null);
    }

    private void SyncExpressions()
    {
        XExpression = $"{_xSymbol}.close()";
        YExpression = $"{_ySymbol}.close()";
        ChartTitle = $"{_xSymbol} vs {_ySymbol} (Scatter)";
    }

    private async Task PlotAsync(CancellationToken ct = default)
    {
        var xSym = _xSymbol.Trim();
        var ySym = _ySymbol.Trim();

        if (string.IsNullOrEmpty(xSym) || string.IsNullOrEmpty(ySym))
        {
            StatusMessage = "Enter X and Y symbols to plot.";
            return;
        }

        IsBusy = true;
        LoadingVisible = Visibility.Visible;
        NoDataVisible = Visibility.Collapsed;
        StatusMessage = "Loading…";

        try
        {
            var (from, to) = GetDateRange();

            // Fetch both series in parallel; do NOT ConfigureAwait(false) — UI properties
            // are updated after the await so we must remain on the synchronization context.
            var xTask = _backfillService.GetHistoricalBarsAsync(xSym, from, to, ct);
            var yTask = _backfillService.GetHistoricalBarsAsync(ySym, from, to, ct);
            await Task.WhenAll(xTask, yTask);

            var xBars = await xTask;
            var yBars = await yTask;

            // Align by date — take the last bar when a date has duplicates (e.g. intraday sources)
            var xDict = xBars
                .GroupBy(b => DateOnly.FromDateTime(b.ToTimestampUtc().Date))
                .ToDictionary(g => g.Key, g => g.Last());
            var yDict = yBars
                .GroupBy(b => DateOnly.FromDateTime(b.ToTimestampUtc().Date))
                .ToDictionary(g => g.Key, g => g.Last());

            var commonDates = xDict.Keys
                .Intersect(yDict.Keys)
                .OrderBy(d => d)
                .ToList();

            if (commonDates.Count == 0)
            {
                StatusMessage = "No overlapping data for the selected symbols and range.";
                NoDataVisible  = Visibility.Visible;
                ChartVisible   = Visibility.Collapsed;
                return;
            }

            var allPoints = commonDates
                .Select(d => new ScatterPoint(
                    (double)xDict[d].Close,
                    (double)yDict[d].Close,
                    d))
                .ToList();

            CurrentPoint = allPoints.Last();
            HistoryPoints = allPoints.Take(allPoints.Count - 1).ToList();

            ComputeStatistics(allPoints);
            RefreshDataSheet(allPoints);

            ChartTitle = $"{xSym} vs {ySym} (Scatter)";
            StatusMessage = $"{allPoints.Count} points · {from:MMM yyyy} – {to:MMM yyyy}";
            NoDataVisible = Visibility.Collapsed;
            ChartVisible  = Visibility.Visible;

            ChartDataReady?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage  = "Cancelled.";
            NoDataVisible  = Visibility.Visible;
            ChartVisible   = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusMessage  = $"Error: {ex.Message}";
            NoDataVisible  = Visibility.Visible;
            ChartVisible   = Visibility.Collapsed;
        }
        finally
        {
            IsBusy = false;
            LoadingVisible = Visibility.Collapsed;
        }
    }

    private (DateOnly from, DateOnly to) GetDateRange()
    {
        var to = DateOnly.FromDateTime(DateTime.Today);
        var from = _selectedTimeRange switch
        {
            "1M"  => to.AddMonths(-1),
            "3M"  => to.AddMonths(-3),
            "6M"  => to.AddMonths(-6),
            "1Y"  => to.AddYears(-1),
            "3Y"  => to.AddYears(-3),
            _     => to.AddYears(-10) // MAX
        };
        return (from, to);
    }

    private void ComputeStatistics(IReadOnlyList<ScatterPoint> points)
    {
        CountText = points.Count.ToString();

        if (points.Count < 2)
        {
            CorrelationText = "--";
            RSquaredText    = "--";
            SlopeText       = "--";
            InterceptText   = "--";
            XMeanText       = "--";
            YMeanText       = "--";
            XStdDevText     = "--";
            YStdDevText     = "--";
            RegressionLine  = null;
            return;
        }

        var xs = points.Select(p => p.X).ToArray();
        var ys = points.Select(p => p.Y).ToArray();

        var xMean = xs.Average();
        var yMean = ys.Average();

        var cov  = xs.Zip(ys, (x, y) => (x - xMean) * (y - yMean)).Sum() / (xs.Length - 1);
        var xVar = xs.Select(x => (x - xMean) * (x - xMean)).Sum() / (xs.Length - 1);
        var yVar = ys.Select(y => (y - yMean) * (y - yMean)).Sum() / (ys.Length - 1);

        var xStd = Math.Sqrt(xVar);
        var yStd = Math.Sqrt(yVar);

        var correlation = xStd > 0 && yStd > 0 ? cov / (xStd * yStd) : 0.0;
        var slope       = xVar > 0 ? cov / xVar : 0.0;
        var intercept   = yMean - slope * xMean;
        var rSquared    = correlation * correlation;

        CorrelationText = $"{correlation:F4}";
        RSquaredText    = $"{rSquared:F4}";
        SlopeText       = $"{slope:F4}";
        InterceptText   = $"{intercept:F4}";
        XMeanText       = $"{xMean:F4}";
        YMeanText       = $"{yMean:F4}";
        XStdDevText     = $"{xStd:F4}";
        YStdDevText     = $"{yStd:F4}";

        // Regression line endpoints spanning the data range
        var x1 = xs.Min();
        var x2 = xs.Max();
        RegressionLine = (x1, slope * x1 + intercept, x2, slope * x2 + intercept);
    }

    private void RefreshDataSheet(IReadOnlyList<ScatterPoint> points)
    {
        DataSheetRows.Clear();
        foreach (var p in points.OrderByDescending(p => p.Date))
            DataSheetRows.Add(new DataSheetRow(
                p.Date.ToString("yyyy-MM-dd"),
                $"{p.X:F4}",
                $"{p.Y:F4}"));
    }
}
