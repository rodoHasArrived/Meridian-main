using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Meridian.Ui.Services;
using SkiaSharp;
using Palette = Meridian.Ui.Services.Services.ColorPalette;

namespace Meridian.Wpf.ViewModels;

public sealed class ChartingPageViewModel : BindableBase
{
    private readonly IChartingDataClient _chartingClient;
    private readonly IChartingSymbolSource _symbolSource;
    private readonly bool _autoRefreshOnSetupChanges;
    private CandlestickData? _chartData;
    private string? _selectedSymbol;
    private ChartTimeframe _selectedTimeframe = ChartTimeframe.Daily;
    private ChartTimeframeOption? _selectedTimeframeOption;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private readonly List<string> _activeIndicators = new();
    private bool _initialized;

    private const double VolumeChartHeight = 100;

    // Bindable properties replacing direct UI control references
    private string _currentPrice = "--";
    private string _priceChange = "--";
    private string _priceChangePercent = "--";
    private string _openPrice = "--";
    private string _highPrice = "--";
    private string _lowPrice = "--";
    private string _volume = "--";
    private string _poc = "--";
    private string _vah = "--";
    private string _val = "--";
    private string _periodHigh = "--";
    private string _periodLow = "--";
    private string _periodVolume = "--";
    private string _candleCount = "--";
    private string _activeIndicatorsText = "";
    private string _chartSetupTitle = "Chart setup incomplete";
    private string _chartSetupDetail = "Select a symbol before loading candlesticks, volume, and indicators.";
    private string _chartSetupScopeText = "No symbol selected";
    private bool _canRefreshChart;
    private Visibility _noChartDataVisible = Visibility.Visible;
    private Visibility _noVolumeProfileVisible = Visibility.Visible;
    private Visibility _noIndicatorsVisible = Visibility.Visible;
    private Visibility _loadingVisible = Visibility.Collapsed;
    private bool _isLoading;
    private Brush _priceChangeBrush = Brushes.White;

    private static readonly SolidColorBrush BullishBrush = ToBrush(Palette.ChartPositive);
    private static readonly SolidColorBrush BearishBrush = ToBrush(Palette.ChartNegative);
    private static readonly SolidColorBrush BullishVolumeBrush = ToBrush(WithAlpha(Palette.ChartPositive, 128));
    private static readonly SolidColorBrush BearishVolumeBrush = ToBrush(WithAlpha(Palette.ChartNegative, 128));
    private static readonly SolidColorBrush PocBrush = ToBrush(Palette.ChartTertiary);
    private static readonly SolidColorBrush NormalVolumeBrush = ToBrush(WithAlpha(Palette.ChartPrimary, 128));
    private static readonly SolidColorBrush PrimaryIndicatorBrush = ToBrush(Palette.ChartPrimary);
    private static readonly SolidColorBrush SecondaryIndicatorBrush = ToBrush(Palette.ChartSecondary);
    private static readonly SolidColorBrush WarningIndicatorBrush = ToBrush(Palette.ChartTertiary);
    private static readonly SolidColorBrush NeutralIndicatorBrush = ToBrush(Palette.LightText);

    // LiveCharts2 SkiaSharp paints — reused across renders to avoid per-frame allocation.
    private static readonly SolidColorPaint SkBullishFill = new(ToSkColor(Palette.ChartPositive));
    private static readonly SolidColorPaint SkBullishStroke = new(ToSkColor(Palette.ChartPositive)) { StrokeThickness = 1 };
    private static readonly SolidColorPaint SkBearishFill = new(ToSkColor(Palette.ChartNegative));
    private static readonly SolidColorPaint SkBearishStroke = new(ToSkColor(Palette.ChartNegative)) { StrokeThickness = 1 };
    private static readonly SolidColorPaint SkAxisLabelPaint = new(ToSkColor(Palette.ChartAxis));
    private static readonly SolidColorPaint SkSeparatorPaint = new(ToSkColor(Palette.ChartGrid));

    private static Palette.ArgbColor WithAlpha(Palette.ArgbColor color, byte alpha)
        => color with { A = alpha };

    private static SolidColorBrush ToBrush(Palette.ArgbColor color)
    {
        SolidColorBrush brush = new(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static SKColor ToSkColor(Palette.ArgbColor color)
        => new(color.R, color.G, color.B, color.A);

    public ObservableCollection<object> SymbolItems { get; } = new();
    public ObservableCollection<ChartTimeframeOption> TimeframeOptions { get; } = new();
    public ObservableCollection<ChartIndicatorToggle> IndicatorToggles { get; } = new();
    public ObservableCollection<WpfVolumeBarVm> VolumeItems { get; } = new();
    public ObservableCollection<WpfVolumeProfileBarVm> VolumeProfileItems { get; } = new();
    public ObservableCollection<WpfIndicatorValueVm> IndicatorValues { get; } = new();

    // LiveCharts2 candlestick chart bindings
    private ISeries[] _candleSeries = [];
    public ISeries[] CandleSeries { get => _candleSeries; private set => SetProperty(ref _candleSeries, value); }

    private ICartesianAxis[] _candleXAxes = [new Axis { IsVisible = false }];
    public ICartesianAxis[] CandleXAxes { get => _candleXAxes; private set => SetProperty(ref _candleXAxes, value); }

    private ICartesianAxis[] _candleYAxes = [new Axis { IsVisible = false }];
    public ICartesianAxis[] CandleYAxes { get => _candleYAxes; private set => SetProperty(ref _candleYAxes, value); }

    public string CurrentPrice { get => _currentPrice; private set => SetProperty(ref _currentPrice, value); }
    public string PriceChange { get => _priceChange; private set => SetProperty(ref _priceChange, value); }
    public string PriceChangePercent { get => _priceChangePercent; private set => SetProperty(ref _priceChangePercent, value); }
    public string OpenPrice { get => _openPrice; private set => SetProperty(ref _openPrice, value); }
    public string HighPrice { get => _highPrice; private set => SetProperty(ref _highPrice, value); }
    public string LowPrice { get => _lowPrice; private set => SetProperty(ref _lowPrice, value); }
    public string Volume { get => _volume; private set => SetProperty(ref _volume, value); }
    public string Poc { get => _poc; private set => SetProperty(ref _poc, value); }
    public string Vah { get => _vah; private set => SetProperty(ref _vah, value); }
    public string Val { get => _val; private set => SetProperty(ref _val, value); }
    public string PeriodHigh { get => _periodHigh; private set => SetProperty(ref _periodHigh, value); }
    public string PeriodLow { get => _periodLow; private set => SetProperty(ref _periodLow, value); }
    public string PeriodVolume { get => _periodVolume; private set => SetProperty(ref _periodVolume, value); }
    public string CandleCount { get => _candleCount; private set => SetProperty(ref _candleCount, value); }
    public string ActiveIndicatorsText { get => _activeIndicatorsText; private set => SetProperty(ref _activeIndicatorsText, value); }
    public string ChartSetupTitle { get => _chartSetupTitle; private set => SetProperty(ref _chartSetupTitle, value); }
    public string ChartSetupDetail { get => _chartSetupDetail; private set => SetProperty(ref _chartSetupDetail, value); }
    public string ChartSetupScopeText { get => _chartSetupScopeText; private set => SetProperty(ref _chartSetupScopeText, value); }
    public bool CanRefreshChart { get => _canRefreshChart; private set => SetProperty(ref _canRefreshChart, value); }
    public Visibility NoChartDataVisible { get => _noChartDataVisible; private set => SetProperty(ref _noChartDataVisible, value); }
    public Visibility NoVolumeProfileVisible { get => _noVolumeProfileVisible; private set => SetProperty(ref _noVolumeProfileVisible, value); }
    public Visibility NoIndicatorsVisible { get => _noIndicatorsVisible; private set => SetProperty(ref _noIndicatorsVisible, value); }
    public Visibility LoadingVisible { get => _loadingVisible; private set => SetProperty(ref _loadingVisible, value); }
    public Brush PriceChangeBrush { get => _priceChangeBrush; private set => SetProperty(ref _priceChangeBrush, value); }

    public string NoChartDataMessage { get => _noChartDataMessage; private set => SetProperty(ref _noChartDataMessage, value); }
    private string _noChartDataMessage = "Select a symbol to view chart";

    public IAsyncRelayCommand RefreshChartCommand { get; }

    public string? SelectedSymbol
    {
        get => _selectedSymbol;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
            if (SetProperty(ref _selectedSymbol, normalized))
            {
                RefreshSetupState();
                QueueChartRefresh();
            }
        }
    }

    public ChartTimeframeOption? SelectedTimeframeOption
    {
        get => _selectedTimeframeOption;
        set
        {
            if (SetProperty(ref _selectedTimeframeOption, value))
            {
                _selectedTimeframe = value?.Timeframe ?? ChartTimeframe.Daily;
                RefreshSetupState();
                QueueChartRefresh();
            }
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                RefreshSetupState();
                QueueChartRefresh();
            }
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                RefreshSetupState();
                QueueChartRefresh();
            }
        }
    }

    public ChartingPageViewModel(SymbolManagementService symbolService)
        : this(new SymbolManagementChartingSymbolSource(symbolService), new ChartingDataClient(), autoRefreshOnSetupChanges: true)
    {
    }

    public ChartingPageViewModel(
        IChartingSymbolSource symbolSource,
        IChartingDataClient chartingClient,
        bool autoRefreshOnSetupChanges = true)
    {
        _symbolSource = symbolSource ?? throw new ArgumentNullException(nameof(symbolSource));
        _chartingClient = chartingClient ?? throw new ArgumentNullException(nameof(chartingClient));
        _autoRefreshOnSetupChanges = autoRefreshOnSetupChanges;

        TimeframeOptions.Add(new ChartTimeframeOption("1 Min", ChartTimeframe.Minute1));
        TimeframeOptions.Add(new ChartTimeframeOption("5 Min", ChartTimeframe.Minute5));
        TimeframeOptions.Add(new ChartTimeframeOption("15 Min", ChartTimeframe.Minute15));
        TimeframeOptions.Add(new ChartTimeframeOption("30 Min", ChartTimeframe.Minute30));
        TimeframeOptions.Add(new ChartTimeframeOption("1 Hour", ChartTimeframe.Hour1));
        TimeframeOptions.Add(new ChartTimeframeOption("4 Hour", ChartTimeframe.Hour4));
        TimeframeOptions.Add(new ChartTimeframeOption("Daily", ChartTimeframe.Daily));
        TimeframeOptions.Add(new ChartTimeframeOption("Weekly", ChartTimeframe.Weekly));
        TimeframeOptions.Add(new ChartTimeframeOption("Monthly", ChartTimeframe.Monthly));
        SelectedTimeframeOption = TimeframeOptions.First(option => option.Timeframe == ChartTimeframe.Daily);

        AddIndicatorToggle("sma", "SMA");
        AddIndicatorToggle("ema", "EMA");
        AddIndicatorToggle("rsi", "RSI");
        AddIndicatorToggle("macd", "MACD");
        AddIndicatorToggle("bb", "BB");
        AddIndicatorToggle("vwap", "VWAP");
        AddIndicatorToggle("atr", "ATR");

        RefreshChartCommand = new AsyncRelayCommand(RefreshChartAsync, () => CanRefreshChart);
        RefreshSetupState();
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return Task.CompletedTask;

        _initialized = true;
        FromDate = DateTime.Today.AddMonths(-3);
        ToDate = DateTime.Today;
        return LoadSymbolsAsync(ct);
    }

    private async Task LoadSymbolsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _symbolSource.GetAllSymbolsAsync(ct);
            if (!result.Success)
            {
                RefreshSetupState();
                return;
            }
            foreach (var symbol in result.Symbols)
                SymbolItems.Add(new SymbolItem(symbol.Symbol));
            RefreshSetupState();
        }
        catch (Exception)
        {
            ChartSetupTitle = "Symbol list unavailable";
            ChartSetupDetail = "Open Data > Symbols or retry after the local host is available.";
            RefreshSetupState();
        }
    }

    public void OnSymbolChanged(string? symbol)
    {
        SelectedSymbol = symbol;
    }

    public void OnTimeframeChanged(ChartTimeframe timeframe)
    {
        SelectedTimeframeOption = TimeframeOptions.FirstOrDefault(option => option.Timeframe == timeframe)
            ?? SelectedTimeframeOption;
    }

    public void OnDateChanged(DateOnly? from, DateOnly? to)
    {
        FromDate = from?.ToDateTime(TimeOnly.MinValue);
        ToDate = to?.ToDateTime(TimeOnly.MinValue);
    }

    public Task RefreshChartAsync() => LoadChartDataAsync();

    public void RefreshChart() => _ = RefreshChartAsync();

    public void OnIndicatorToggled(string id, bool isChecked)
    {
        if (isChecked)
        {
            if (!_activeIndicators.Contains(id))
                _activeIndicators.Add(id);
        }
        else
        {
            _activeIndicators.Remove(id);
        }
        UpdateIndicatorDisplay();
        RefreshSetupState();
    }

    private async Task LoadChartDataAsync(CancellationToken ct = default)
    {
        RefreshSetupState();
        if (!CanRefreshChart || string.IsNullOrEmpty(SelectedSymbol) || !FromDate.HasValue || !ToDate.HasValue)
            return;

        SetLoading(true);
        NoChartDataVisible = Visibility.Collapsed;

        try
        {
            _chartData = await _chartingClient.GetCandlestickDataAsync(
                SelectedSymbol,
                _selectedTimeframe,
                DateOnly.FromDateTime(FromDate.Value),
                DateOnly.FromDateTime(ToDate.Value),
                ct);
            if (_chartData.Candles.Count == 0)
            {
                ClearRenderedData(clearPriceHeader: true);
                NoChartDataMessage = $"No chart data found for {SelectedSymbol} over {FormatDateRange()}.";
                NoChartDataVisible = Visibility.Visible;
                return;
            }

            RenderCandlestickChart();
            RenderVolumeChart();
            UpdatePriceInfo();
            UpdateVolumeProfile();
            UpdateIndicatorDisplay();
            UpdateStatistics();
        }
        catch (Exception ex)
        {
            NoChartDataMessage = $"Error: {ex.Message}";
            NoChartDataVisible = Visibility.Visible;
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void AddIndicatorToggle(string id, string label)
    {
        IndicatorToggles.Add(new ChartIndicatorToggle(id, label, OnIndicatorToggled));
    }

    private void QueueChartRefresh()
    {
        if (_autoRefreshOnSetupChanges && CanRefreshChart)
            _ = LoadChartDataAsync();
    }

    private void SetLoading(bool isLoading)
    {
        _isLoading = isLoading;
        LoadingVisible = isLoading ? Visibility.Visible : Visibility.Collapsed;
        RefreshSetupState();
    }

    private void RefreshSetupState()
    {
        var errors = GetSetupErrors();
        CanRefreshChart = errors.Length == 0 && !_isLoading;
        RefreshChartCommand?.NotifyCanExecuteChanged();

        ChartSetupScopeText = FormatSetupScope();
        if (errors.Length == 0)
        {
            ChartSetupTitle = _isLoading ? "Loading chart" : "Chart ready";
            ChartSetupDetail = $"{SelectedSymbol} {_selectedTimeframeOption?.Label ?? "Daily"} candles over {FormatDateRange()} with {FormatIndicatorScope()}.";
            if (_chartData is null)
                NoChartDataMessage = "Refresh chart data to render candlesticks.";
        }
        else
        {
            ChartSetupTitle = "Chart setup incomplete";
            ChartSetupDetail = string.Join(" ", errors);
            NoChartDataMessage = ChartSetupDetail;
            NoChartDataVisible = Visibility.Visible;
        }
    }

    private string[] GetSetupErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SelectedSymbol))
            errors.Add(SymbolItems.Count == 0 ? "No configured symbols are loaded." : "Select a symbol.");
        if (!FromDate.HasValue)
            errors.Add("Choose a start date.");
        if (!ToDate.HasValue)
            errors.Add("Choose an end date.");
        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value.Date > ToDate.Value.Date)
            errors.Add("Start date must be before the end date.");

        return errors.ToArray();
    }

    private string FormatSetupScope()
    {
        var symbol = string.IsNullOrWhiteSpace(SelectedSymbol) ? "No symbol" : SelectedSymbol;
        var timeframe = _selectedTimeframeOption?.Label ?? "Daily";
        return $"{symbol} | {timeframe} | {FormatDateRange()}";
    }

    private string FormatDateRange()
    {
        if (FromDate.HasValue && ToDate.HasValue)
            return $"{FromDate:MMM dd, yyyy} - {ToDate:MMM dd, yyyy}";
        if (FromDate.HasValue)
            return $"From {FromDate:MMM dd, yyyy}";
        if (ToDate.HasValue)
            return $"Through {ToDate:MMM dd, yyyy}";

        return "No date range";
    }

    private string FormatIndicatorScope()
    {
        return _activeIndicators.Count == 0
            ? "no indicators selected"
            : $"{_activeIndicators.Count} indicator(s): {string.Join(", ", _activeIndicators.Select(i => i.ToUpperInvariant()))}";
    }

    private void ClearRenderedData(bool clearPriceHeader)
    {
        CandleSeries = [];
        CandleXAxes = [new Axis { IsVisible = false }];
        CandleYAxes = [new Axis { IsVisible = false }];
        VolumeItems.Clear();
        VolumeProfileItems.Clear();
        IndicatorValues.Clear();
        NoVolumeProfileVisible = Visibility.Visible;
        NoIndicatorsVisible = Visibility.Visible;
        Poc = "--";
        Vah = "--";
        Val = "--";
        PeriodHigh = "--";
        PeriodLow = "--";
        PeriodVolume = "--";
        CandleCount = "--";

        if (clearPriceHeader)
        {
            CurrentPrice = "--";
            PriceChange = "--";
            PriceChangePercent = "--";
            OpenPrice = "--";
            HighPrice = "--";
            LowPrice = "--";
            Volume = "--";
            PriceChangeBrush = Brushes.White;
        }
    }

    private void RenderCandlestickChart()
    {
        if (_chartData == null || _chartData.Candles.Count == 0)
            return;

        var financialPoints = _chartData.Candles
            .Select(c => new FinancialPoint(c.Timestamp, (double)c.High, (double)c.Open, (double)c.Close, (double)c.Low))
            .ToList();

        CandleSeries =
        [
            new CandlesticksSeries<FinancialPoint>
            {
                Values = financialPoints,
                UpFill = SkBullishFill,
                UpStroke = SkBullishStroke,
                DownFill = SkBearishFill,
                DownStroke = SkBearishStroke,
                Name = SelectedSymbol,
                MaxBarWidth = 14,
            }
        ];

        var labelFormat = _selectedTimeframe is ChartTimeframe.Hour1 or ChartTimeframe.Hour4
            or ChartTimeframe.Minute1 or ChartTimeframe.Minute5
            or ChartTimeframe.Minute15 or ChartTimeframe.Minute30
            ? "MM/dd HH:mm"
            : "MM/dd/yy";

        CandleXAxes =
        [
            new Axis
            {
                Labeler = value => new DateTime((long)value).ToString(labelFormat),
                LabelsPaint = SkAxisLabelPaint,
                SeparatorsPaint = SkSeparatorPaint,
                TextSize = 11,
            }
        ];

        CandleYAxes =
        [
            new Axis
            {
                Labeler = value => $"{value:F2}",
                LabelsPaint = SkAxisLabelPaint,
                SeparatorsPaint = SkSeparatorPaint,
                TextSize = 11,
                Position = AxisPosition.End,
            }
        ];
    }

    private void RenderVolumeChart()
    {
        if (_chartData == null || _chartData.Candles.Count == 0)
            return;
        var max = _chartData.Candles.Max(c => c.Volume);
        if (max == 0)
            max = 1;

        VolumeItems.Clear();
        foreach (var c in _chartData.Candles)
        {
            VolumeItems.Add(new WpfVolumeBarVm
            {
                Height = Math.Max(1, (double)(c.Volume / max) * VolumeChartHeight),
                BarBrush = c.Close >= c.Open ? BullishVolumeBrush : BearishVolumeBrush,
                Tooltip = $"{c.Timestamp:yyyy-MM-dd}: {c.Volume:N0}"
            });
        }
    }

    private void UpdatePriceInfo()
    {
        if (_chartData == null || _chartData.Candles.Count == 0)
            return;
        var last = _chartData.Candles.Last();
        var first = _chartData.Candles.First();
        CurrentPrice = $"{last.Close:F2}";
        var change = last.Close - first.Close;
        var pct = first.Close > 0 ? (change / first.Close) * 100 : 0;
        var brush = change >= 0 ? BullishBrush : BearishBrush;
        PriceChange = $"{(change >= 0 ? "+" : "")}{change:F2}";
        PriceChangePercent = $"({(pct >= 0 ? "+" : "")}{pct:F2}%)";
        PriceChangeBrush = brush;
        OpenPrice = $"{last.Open:F2}";
        HighPrice = $"{last.High:F2}";
        LowPrice = $"{last.Low:F2}";
        Volume = $"{last.Volume:N0}";
    }

    private void UpdateVolumeProfile()
    {
        if (_chartData == null || _chartData.Candles.Count == 0)
        {
            NoVolumeProfileVisible = Visibility.Visible;
            return;
        }

        var profile = _chartingClient.CalculateVolumeProfile(_chartData, 15);
        if (profile.Levels.Count == 0)
        {
            NoVolumeProfileVisible = Visibility.Visible;
            return;
        }
        NoVolumeProfileVisible = Visibility.Collapsed;

        VolumeProfileItems.Clear();
        foreach (var l in profile.Levels.OrderByDescending(l => l.PriceLevel))
        {
            var isPoc = Math.Abs(l.PriceLevel - profile.PointOfControl) < 0.01m * profile.PointOfControl;
            VolumeProfileItems.Add(new WpfVolumeProfileBarVm
            {
                PriceLabel = $"{l.PriceLevel:F2}",
                BarWidth = l.Intensity * 150,
                BarBrush = isPoc ? PocBrush : NormalVolumeBrush
            });
        }
        Poc = $"{profile.PointOfControl:F2}";
        Vah = $"{profile.ValueAreaHigh:F2}";
        Val = $"{profile.ValueAreaLow:F2}";
    }

    private void UpdateIndicatorDisplay()
    {
        if (_chartData == null)
            return;
        var values = new List<WpfIndicatorValueVm>();
        foreach (var id in _activeIndicators)
        {
            switch (id)
            {
                case "sma":
                    AddSimple(values, _chartingClient.CalculateSma(_chartData, 20), "SMA(20)", WarningIndicatorBrush);
                    break;
                case "ema":
                    AddSimple(values, _chartingClient.CalculateEma(_chartData, 20), "EMA(20)", PrimaryIndicatorBrush);
                    break;
                case "vwap":
                    AddSimple(values, _chartingClient.CalculateVwap(_chartData), "VWAP", SecondaryIndicatorBrush);
                    break;
                case "atr":
                    AddSimple(values, _chartingClient.CalculateAtr(_chartData, 14), "ATR(14)", WarningIndicatorBrush);
                    break;
                case "rsi":
                    var rsi = _chartingClient.CalculateRsi(_chartData, 14);
                    if (rsi.Values.Count > 0)
                    {
                        var v = rsi.Values.Last().Value;
                        values.Add(new WpfIndicatorValueVm { Name = "RSI(14)", Value = $"{v:F1}", ValueBrush = v > 70 ? BearishBrush : v < 30 ? BullishBrush : NeutralIndicatorBrush });
                    }
                    break;
                case "macd":
                    var macd = _chartingClient.CalculateMacd(_chartData);
                    if (macd.MacdLine.Count > 0)
                    {
                        var mv = macd.MacdLine.Last().Value;
                        var sv = macd.SignalLine.LastOrDefault()?.Value ?? 0;
                        values.Add(new WpfIndicatorValueVm { Name = "MACD", Value = $"{mv:F3}", ValueBrush = mv > sv ? BullishBrush : BearishBrush });
                        values.Add(new WpfIndicatorValueVm { Name = "Signal", Value = $"{sv:F3}", ValueBrush = WarningIndicatorBrush });
                    }
                    break;
                case "bb":
                    var bb = _chartingClient.CalculateBollingerBands(_chartData);
                    if (bb.UpperBand.Count > 0)
                    {
                        var b = SecondaryIndicatorBrush;
                        values.Add(new WpfIndicatorValueVm { Name = "BB Upper", Value = $"{bb.UpperBand.Last().Value:F2}", ValueBrush = b });
                        values.Add(new WpfIndicatorValueVm { Name = "BB Middle", Value = $"{bb.MiddleBand.Last().Value:F2}", ValueBrush = b });
                        values.Add(new WpfIndicatorValueVm { Name = "BB Lower", Value = $"{bb.LowerBand.Last().Value:F2}", ValueBrush = b });
                    }
                    break;
            }
        }

        IndicatorValues.Clear();
        foreach (var v in values)
            IndicatorValues.Add(v);
        NoIndicatorsVisible = values.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveIndicatorsText = _activeIndicators.Count > 0
            ? $"Active: {string.Join(", ", _activeIndicators.Select(i => i.ToUpper()))}"
            : "";
        RefreshSetupState();
    }

    private static void AddSimple(List<WpfIndicatorValueVm> list, IndicatorData data, string name, Brush brush)
    {
        if (data.Values.Count > 0)
            list.Add(new WpfIndicatorValueVm { Name = name, Value = $"{data.Values.Last().Value:F2}", ValueBrush = brush });
    }

    private void UpdateStatistics()
    {
        if (_chartData == null || _chartData.Candles.Count == 0)
            return;
        PeriodHigh = $"{_chartData.HighestPrice:F2}";
        PeriodLow = $"{_chartData.LowestPrice:F2}";
        PeriodVolume = $"{_chartData.TotalVolume:N0}";
        CandleCount = $"{_chartData.Candles.Count}";
    }
}

public interface IChartingSymbolSource
{
    Task<SymbolListResult> GetAllSymbolsAsync(CancellationToken ct = default);
}

public interface IChartingDataClient
{
    Task<CandlestickData> GetCandlestickDataAsync(
        string symbol,
        ChartTimeframe timeframe,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default);

    VolumeProfileData CalculateVolumeProfile(CandlestickData data, int buckets = 20);

    IndicatorData CalculateSma(CandlestickData data, int period);

    IndicatorData CalculateEma(CandlestickData data, int period);

    IndicatorData CalculateVwap(CandlestickData data);

    IndicatorData CalculateAtr(CandlestickData data, int period = 14);

    IndicatorData CalculateRsi(CandlestickData data, int period = 14);

    MacdData CalculateMacd(CandlestickData data, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9);

    BollingerBandsData CalculateBollingerBands(CandlestickData data, int period = 20, decimal stdDevMultiplier = 2);
}

internal sealed class SymbolManagementChartingSymbolSource : IChartingSymbolSource
{
    private readonly SymbolManagementService _symbolService;

    public SymbolManagementChartingSymbolSource(SymbolManagementService symbolService)
    {
        _symbolService = symbolService ?? throw new ArgumentNullException(nameof(symbolService));
    }

    public Task<SymbolListResult> GetAllSymbolsAsync(CancellationToken ct = default) =>
        _symbolService.GetAllSymbolsAsync(ct);
}

internal sealed class ChartingDataClient : IChartingDataClient
{
    private readonly ChartingService _chartingService = new();

    public Task<CandlestickData> GetCandlestickDataAsync(
        string symbol,
        ChartTimeframe timeframe,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default) =>
        _chartingService.GetCandlestickDataAsync(symbol, timeframe, fromDate, toDate, ct);

    public VolumeProfileData CalculateVolumeProfile(CandlestickData data, int buckets = 20) =>
        _chartingService.CalculateVolumeProfile(data, buckets);

    public IndicatorData CalculateSma(CandlestickData data, int period) =>
        _chartingService.CalculateSma(data, period);

    public IndicatorData CalculateEma(CandlestickData data, int period) =>
        _chartingService.CalculateEma(data, period);

    public IndicatorData CalculateVwap(CandlestickData data) =>
        _chartingService.CalculateVwap(data);

    public IndicatorData CalculateAtr(CandlestickData data, int period = 14) =>
        _chartingService.CalculateAtr(data, period);

    public IndicatorData CalculateRsi(CandlestickData data, int period = 14) =>
        _chartingService.CalculateRsi(data, period);

    public MacdData CalculateMacd(CandlestickData data, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9) =>
        _chartingService.CalculateMacd(data, fastPeriod, slowPeriod, signalPeriod);

    public BollingerBandsData CalculateBollingerBands(CandlestickData data, int period = 20, decimal stdDevMultiplier = 2) =>
        _chartingService.CalculateBollingerBands(data, period, stdDevMultiplier);
}

/// <summary>Simple wrapper so ComboBox items have a typed display value.</summary>
public sealed class SymbolItem
{
    public SymbolItem(string symbol) => Symbol = symbol;
    public string Symbol { get; }
    public override string ToString() => Symbol;
}

public sealed class ChartTimeframeOption
{
    public ChartTimeframeOption(string label, ChartTimeframe timeframe)
    {
        Label = label;
        Timeframe = timeframe;
    }

    public string Label { get; }

    public ChartTimeframe Timeframe { get; }

    public override string ToString() => Label;
}

public sealed class ChartIndicatorToggle : BindableBase
{
    private readonly Action<string, bool> _toggleChanged;
    private bool _isEnabled;

    public ChartIndicatorToggle(string id, string label, Action<string, bool> toggleChanged)
    {
        Id = id;
        Label = label;
        _toggleChanged = toggleChanged ?? throw new ArgumentNullException(nameof(toggleChanged));
    }

    public string Id { get; }

    public string Label { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
                _toggleChanged(Id, value);
        }
    }
}

public sealed class WpfVolumeBarVm
{
    public double Height { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Gray;
    public string Tooltip { get; set; } = string.Empty;
}

public sealed class WpfVolumeProfileBarVm
{
    public string PriceLabel { get; set; } = string.Empty;
    public double BarWidth { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Blue;
}

public sealed class WpfIndicatorValueVm
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public Brush ValueBrush { get; set; } = Brushes.White;
}
