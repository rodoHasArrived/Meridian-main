using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class ChartingPageViewModel : BindableBase
{
    private readonly ChartingService _chartingService = new();
    private readonly SymbolManagementService _symbolService;
    private string? _selectedSymbol;
    private ChartTimeframe _selectedTimeframe = ChartTimeframe.Daily;
    private DateOnly? _fromDate;
    private DateOnly? _toDate;
    private readonly List<string> _activeIndicators = new();

    private const double ChartHeight = 400;
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
    private Visibility _noChartDataVisible = Visibility.Visible;
    private Visibility _noVolumeProfileVisible = Visibility.Visible;
    private Visibility _noIndicatorsVisible = Visibility.Visible;
    private Visibility _loadingVisible = Visibility.Collapsed;
    private Brush _priceChangeBrush = Brushes.White;

    private static readonly SolidColorBrush BullishBrush = new(Color.FromRgb(63, 185, 80));
    private static readonly SolidColorBrush BearishBrush = new(Color.FromRgb(248, 81, 73));
    private static readonly SolidColorBrush BullishVolumeBrush = new(Color.FromArgb(128, 63, 185, 80));
    private static readonly SolidColorBrush BearishVolumeBrush = new(Color.FromArgb(128, 248, 81, 73));

    private static readonly SolidColorBrush PocBrush = new(Color.FromRgb(210, 153, 34));
    private static readonly SolidColorBrush NormalVolumeBrush = new(Color.FromArgb(128, 88, 166, 255));

    public ObservableCollection<object> SymbolItems { get; } = new();
    public ObservableCollection<WpfCandlestickVm> CandlestickItems { get; } = new();
    public ObservableCollection<WpfVolumeBarVm> VolumeItems { get; } = new();
    public ObservableCollection<WpfVolumeProfileBarVm> VolumeProfileItems { get; } = new();
    public ObservableCollection<WpfIndicatorValueVm> IndicatorValues { get; } = new();

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
    public Visibility NoChartDataVisible { get => _noChartDataVisible; private set => SetProperty(ref _noChartDataVisible, value); }
    public Visibility NoVolumeProfileVisible { get => _noVolumeProfileVisible; private set => SetProperty(ref _noVolumeProfileVisible, value); }
    public Visibility NoIndicatorsVisible { get => _noIndicatorsVisible; private set => SetProperty(ref _noIndicatorsVisible, value); }
    public Visibility LoadingVisible { get => _loadingVisible; private set => SetProperty(ref _loadingVisible, value); }
    public Brush PriceChangeBrush { get => _priceChangeBrush; private set => SetProperty(ref _priceChangeBrush, value); }

    public string NoChartDataMessage { get => _noChartDataMessage; private set => SetProperty(ref _noChartDataMessage, value); }
    private string _noChartDataMessage = "Select a symbol to view chart";

    public ChartingPageViewModel(SymbolManagementService symbolService)
    {
        _symbolService = symbolService;
    }

    public void Initialize()
    {
        _fromDate = DateOnly.FromDateTime(DateTime.Now.AddMonths(-3));
        _toDate = DateOnly.FromDateTime(DateTime.Now);
        _ = LoadSymbolsAsync();
    }

    private async Task LoadSymbolsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _symbolService.GetAllSymbolsAsync();
            if (!result.Success) return;
            foreach (var symbol in result.Symbols)
                SymbolItems.Add(new SymbolItem(symbol.Symbol));
        }
        catch (Exception ex)
        {
            // Symbols not available
            System.Diagnostics.Debug.WriteLine($"An exception occurred while loading symbols: {ex}");
        }
    }

    public void OnSymbolChanged(string? symbol)
    {
        if (!string.IsNullOrEmpty(symbol))
        {
            _selectedSymbol = symbol;
            _ = LoadChartDataAsync();
        }
    }

    public void OnTimeframeChanged(ChartTimeframe timeframe)
    {
        _selectedTimeframe = timeframe;
        _ = LoadChartDataAsync();
    }

    public void OnDateChanged(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue) _fromDate = from;
        if (to.HasValue) _toDate = to;
        if (_fromDate.HasValue && _toDate.HasValue)
            _ = LoadChartDataAsync();
    }

    public void RefreshChart() => _ = LoadChartDataAsync();

    public void OnIndicatorToggled(string id, bool isChecked)
    {
        if (isChecked) { if (!_activeIndicators.Contains(id)) _activeIndicators.Add(id); }
        else _activeIndicators.Remove(id);
        UpdateIndicatorDisplay();
    }

    private async Task LoadChartDataAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_selectedSymbol) || !_fromDate.HasValue || !_toDate.HasValue) return;
        LoadingVisible = Visibility.Visible;
        NoChartDataVisible = Visibility.Collapsed;

        try
        {
            _chartData = await _chartingService.GetCandlestickDataAsync(_selectedSymbol, _selectedTimeframe, _fromDate.Value, _toDate.Value);
            if (_chartData.Candles.Count == 0)
            {
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
            LoadingVisible = Visibility.Collapsed;
        }
    }

    private void RenderCandlestickChart()
    {
        if (_chartData == null || _chartData.Candles.Count == 0) return;
        var range = _chartData.HighestPrice - _chartData.LowestPrice;
        if (range == 0) range = 1;

        CandlestickItems.Clear();
        foreach (var c in _chartData.Candles)
        {
            var highY = (double)((_chartData.HighestPrice - c.High) / range) * ChartHeight;
            var lowY = (double)((_chartData.HighestPrice - c.Low) / range) * ChartHeight;
            var openY = (double)((_chartData.HighestPrice - c.Open) / range) * ChartHeight;
            var closeY = (double)((_chartData.HighestPrice - c.Close) / range) * ChartHeight;
            var bodyTop = Math.Min(openY, closeY);
            var bull = c.Close >= c.Open;
            var brush = bull ? BullishBrush : BearishBrush;
            CandlestickItems.Add(new WpfCandlestickVm
            {
                BodyBrush = brush, WickBrush = brush,
                BodyHeight = Math.Max(1, Math.Abs(openY - closeY)),
                WickHeight = lowY - highY,
                BodyMargin = new Thickness(2, bodyTop, 2, 0),
                WickMargin = new Thickness(5.5, highY, 5.5, 0),
                Tooltip = $"{c.Timestamp:yyyy-MM-dd}\nO: {c.Open:F2}  H: {c.High:F2}\nL: {c.Low:F2}  C: {c.Close:F2}\nVol: {c.Volume:N0}"
            });
        }
    }

    private void RenderVolumeChart()
    {
        if (_chartData == null || _chartData.Candles.Count == 0) return;
        var max = _chartData.Candles.Max(c => c.Volume);
        if (max == 0) max = 1;

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
        if (_chartData == null || _chartData.Candles.Count == 0) return;
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

        var profile = _chartingService.CalculateVolumeProfile(_chartData, 15);
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
        if (_chartData == null) return;
        var values = new List<WpfIndicatorValueVm>();
        foreach (var id in _activeIndicators)
        {
            switch (id)
            {
                case "sma": AddSimple(values, _chartingService.CalculateSma(_chartData, 20), "SMA(20)", Colors.Orange); break;
                case "ema": AddSimple(values, _chartingService.CalculateEma(_chartData, 20), "EMA(20)", Colors.Cyan); break;
                case "vwap": AddSimple(values, _chartingService.CalculateVwap(_chartData), "VWAP", Colors.Purple); break;
                case "atr": AddSimple(values, _chartingService.CalculateAtr(_chartData, 14), "ATR(14)", Colors.Yellow); break;
                case "rsi":
                    var rsi = _chartingService.CalculateRsi(_chartData, 14);
                    if (rsi.Values.Count > 0)
                    {
                        var v = rsi.Values.Last().Value;
                        values.Add(new WpfIndicatorValueVm { Name = "RSI(14)", Value = $"{v:F1}", ValueBrush = new SolidColorBrush(v > 70 ? Colors.Red : v < 30 ? Colors.Green : Colors.White) });
                    }
                    break;
                case "macd":
                    var macd = _chartingService.CalculateMacd(_chartData);
                    if (macd.MacdLine.Count > 0)
                    {
                        var mv = macd.MacdLine.Last().Value; var sv = macd.SignalLine.LastOrDefault()?.Value ?? 0;
                        values.Add(new WpfIndicatorValueVm { Name = "MACD", Value = $"{mv:F3}", ValueBrush = new SolidColorBrush(mv > sv ? Colors.Green : Colors.Red) });
                        values.Add(new WpfIndicatorValueVm { Name = "Signal", Value = $"{sv:F3}", ValueBrush = new SolidColorBrush(Colors.Orange) });
                    }
                    break;
                case "bb":
                    var bb = _chartingService.CalculateBollingerBands(_chartData);
                    if (bb.UpperBand.Count > 0)
                    {
                        var b = new SolidColorBrush(Colors.LightBlue);
                        values.Add(new WpfIndicatorValueVm { Name = "BB Upper", Value = $"{bb.UpperBand.Last().Value:F2}", ValueBrush = b });
                        values.Add(new WpfIndicatorValueVm { Name = "BB Middle", Value = $"{bb.MiddleBand.Last().Value:F2}", ValueBrush = b });
                        values.Add(new WpfIndicatorValueVm { Name = "BB Lower", Value = $"{bb.LowerBand.Last().Value:F2}", ValueBrush = b });
                    }
                    break;
            }
        }

        IndicatorValues.Clear();
        foreach (var v in values) IndicatorValues.Add(v);
        NoIndicatorsVisible = values.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveIndicatorsText = _activeIndicators.Count > 0
            ? $"Active: {string.Join(", ", _activeIndicators.Select(i => i.ToUpper()))}"
            : "";
    }

    private static void AddSimple(List<WpfIndicatorValueVm> list, IndicatorData data, string name, Color color)
    {
        if (data.Values.Count > 0)
            list.Add(new WpfIndicatorValueVm { Name = name, Value = $"{data.Values.Last().Value:F2}", ValueBrush = new SolidColorBrush(color) });
    }

    private void UpdateStatistics()
    {
        if (_chartData == null || _chartData.Candles.Count == 0) return;
        PeriodHigh = $"{_chartData.HighestPrice:F2}";
        PeriodLow = $"{_chartData.LowestPrice:F2}";
        PeriodVolume = $"{_chartData.TotalVolume:N0}";
        CandleCount = $"{_chartData.Candles.Count}";
    }
}

/// <summary>Simple wrapper so ComboBox items have a typed display value.</summary>
public sealed class SymbolItem
{
    public SymbolItem(string symbol) => Symbol = symbol;
    public string Symbol { get; }
    public override string ToString() => Symbol;
}

public sealed class WpfCandlestickVm
{
    public Brush BodyBrush { get; set; } = Brushes.White;
    public Brush WickBrush { get; set; } = Brushes.White;
    public double BodyHeight { get; set; }
    public double WickHeight { get; set; }
    public Thickness BodyMargin { get; set; }
    public Thickness WickMargin { get; set; }
    public string Tooltip { get; set; } = string.Empty;
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
