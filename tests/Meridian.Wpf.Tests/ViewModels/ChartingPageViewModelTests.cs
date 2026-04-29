using System.Windows;
using Meridian.Ui.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ChartingPageViewModelTests
{
    [Fact]
    public async Task InitializeAsync_LoadsSymbolsAndExplainsMissingSelection()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();

        viewModel.SymbolItems.Should().HaveCount(2);
        viewModel.SelectedTimeframeOption?.Timeframe.Should().Be(ChartTimeframe.Daily);
        viewModel.FromDate.Should().NotBeNull();
        viewModel.ToDate.Should().NotBeNull();
        viewModel.CanRefreshChart.Should().BeFalse();
        viewModel.RefreshChartCommand.CanExecute(null).Should().BeFalse();
        viewModel.ChartSetupTitle.Should().Be("Chart setup incomplete");
        viewModel.ChartSetupDetail.Should().Contain("Select a symbol");
        viewModel.ChartSetupScopeText.Should().Contain("No symbol");
    }

    [Fact]
    public async Task RefreshChartAsync_WithValidSetup_ShouldRenderChartAndStats()
    {
        var viewModel = CreateViewModel(out _, out var chartingClient);

        await viewModel.InitializeAsync();
        viewModel.SelectedSymbol = "aapl";

        viewModel.CanRefreshChart.Should().BeTrue();
        viewModel.RefreshChartCommand.CanExecute(null).Should().BeTrue();
        viewModel.ChartSetupTitle.Should().Be("Chart ready");
        viewModel.ChartSetupDetail.Should().Contain("AAPL Daily candles");

        await viewModel.RefreshChartAsync();

        chartingClient.Requests.Should().Be(1);
        chartingClient.LastSymbol.Should().Be("AAPL");
        viewModel.NoChartDataVisible.Should().Be(Visibility.Collapsed);
        viewModel.CurrentPrice.Should().Be("102.00");
        viewModel.PriceChange.Should().Be("+2.00");
        viewModel.CandleCount.Should().Be("2");
        viewModel.PeriodHigh.Should().Be("103.00");
        viewModel.ChartSetupScopeText.Should().Contain("AAPL");
    }

    [Fact]
    public async Task InvalidDateRange_DisablesRefreshAndShowsValidationState()
    {
        var viewModel = CreateViewModel(out _, out var chartingClient);

        await viewModel.InitializeAsync();
        viewModel.SelectedSymbol = "MSFT";
        viewModel.FromDate = new DateTime(2026, 4, 10);
        viewModel.ToDate = new DateTime(2026, 4, 1);

        viewModel.CanRefreshChart.Should().BeFalse();
        viewModel.RefreshChartCommand.CanExecute(null).Should().BeFalse();
        viewModel.ChartSetupTitle.Should().Be("Chart setup incomplete");
        viewModel.ChartSetupDetail.Should().Contain("Start date must be before the end date");

        await viewModel.RefreshChartAsync();

        chartingClient.Requests.Should().Be(0);
    }

    [Fact]
    public async Task IndicatorToggle_UpdatesReadinessAndIndicatorValues()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        viewModel.SelectedSymbol = "AAPL";
        await viewModel.RefreshChartAsync();

        var sma = viewModel.IndicatorToggles.Single(toggle => toggle.Id == "sma");
        sma.IsEnabled = true;

        viewModel.ActiveIndicatorsText.Should().Be("Active: SMA");
        viewModel.IndicatorValues.Should().ContainSingle(value => value.Name == "SMA(20)" && value.Value == "101.00");
        viewModel.ChartSetupDetail.Should().Contain("1 indicator(s): SMA");
    }

    [Fact]
    public void ChartingPageSource_BindsToolbarStateThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ChartingPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ChartingPage.xaml.cs"));

        xaml.Should().Contain("ChartSetupReadinessCard");
        xaml.Should().Contain("{Binding ChartSetupTitle}");
        xaml.Should().Contain("{Binding ChartSetupDetail}");
        xaml.Should().Contain("{Binding ChartSetupScopeText}");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedSymbol, Mode=TwoWay");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedTimeframeOption, Mode=TwoWay}\"");
        xaml.Should().Contain("SelectedDate=\"{Binding FromDate, Mode=TwoWay");
        xaml.Should().Contain("Command=\"{Binding RefreshChartCommand}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding IndicatorToggles}\"");
        xaml.Should().NotContain("SelectionChanged=\"Symbol_SelectionChanged\"");
        xaml.Should().NotContain("SelectionChanged=\"Timeframe_SelectionChanged\"");
        xaml.Should().NotContain("SelectedDateChanged=\"DatePicker_Changed\"");
        xaml.Should().NotContain("Click=\"Refresh_Click\"");
        xaml.Should().NotContain("Click=\"Indicator_Click\"");

        codeBehind.Should().NotContain("Symbol_SelectionChanged");
        codeBehind.Should().NotContain("Timeframe_SelectionChanged");
        codeBehind.Should().NotContain("DatePicker_Changed");
        codeBehind.Should().NotContain("Refresh_Click");
        codeBehind.Should().NotContain("Indicator_Click");
    }

    private static ChartingPageViewModel CreateViewModel(
        out FakeChartingSymbolSource symbolSource,
        out FakeChartingDataClient chartingClient)
    {
        symbolSource = new FakeChartingSymbolSource();
        chartingClient = new FakeChartingDataClient();
        return new ChartingPageViewModel(symbolSource, chartingClient, autoRefreshOnSetupChanges: false);
    }

    private sealed class FakeChartingSymbolSource : IChartingSymbolSource
    {
        public Task<SymbolListResult> GetAllSymbolsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new SymbolListResult
            {
                Success = true,
                Symbols =
                [
                    new SymbolInfo { Symbol = "AAPL" },
                    new SymbolInfo { Symbol = "MSFT" }
                ],
                TotalCount = 2
            });
        }
    }

    private sealed class FakeChartingDataClient : IChartingDataClient
    {
        public int Requests { get; private set; }
        public string? LastSymbol { get; private set; }

        public Task<CandlestickData> GetCandlestickDataAsync(
            string symbol,
            ChartTimeframe timeframe,
            DateOnly fromDate,
            DateOnly toDate,
            CancellationToken ct = default)
        {
            Requests++;
            LastSymbol = symbol;

            var data = new CandlestickData
            {
                Symbol = symbol,
                Timeframe = timeframe,
                HighestPrice = 103m,
                LowestPrice = 99m,
                TotalVolume = 3000m,
                AverageVolume = 1500m
            };
            data.Candles.Add(new Candlestick
            {
                Timestamp = new DateTime(2026, 4, 1, 14, 30, 0, DateTimeKind.Utc),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1000m
            });
            data.Candles.Add(new Candlestick
            {
                Timestamp = new DateTime(2026, 4, 2, 14, 30, 0, DateTimeKind.Utc),
                Open = 101m,
                High = 103m,
                Low = 100m,
                Close = 102m,
                Volume = 2000m
            });

            return Task.FromResult(data);
        }

        public VolumeProfileData CalculateVolumeProfile(CandlestickData data, int buckets = 20)
        {
            var profile = new VolumeProfileData
            {
                PointOfControl = 101m,
                ValueAreaHigh = 103m,
                ValueAreaLow = 99m
            };
            profile.Levels.Add(new VolumePriceLevel { PriceLevel = 101m, Volume = 3000m, Intensity = 1 });
            return profile;
        }

        public IndicatorData CalculateSma(CandlestickData data, int period) => Indicator("SMA(20)", 101m);

        public IndicatorData CalculateEma(CandlestickData data, int period) => Indicator("EMA(20)", 101m);

        public IndicatorData CalculateVwap(CandlestickData data) => Indicator("VWAP", 101m);

        public IndicatorData CalculateAtr(CandlestickData data, int period = 14) => Indicator("ATR(14)", 2m);

        public IndicatorData CalculateRsi(CandlestickData data, int period = 14) => Indicator("RSI(14)", 55m);

        public MacdData CalculateMacd(CandlestickData data, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
        {
            var macd = new MacdData();
            macd.MacdLine.Add(new IndicatorValue { Timestamp = DateTime.UtcNow, Value = 1m });
            macd.SignalLine.Add(new IndicatorValue { Timestamp = DateTime.UtcNow, Value = 0.5m });
            return macd;
        }

        public BollingerBandsData CalculateBollingerBands(CandlestickData data, int period = 20, decimal stdDevMultiplier = 2)
        {
            var bands = new BollingerBandsData();
            bands.UpperBand.Add(new IndicatorValue { Timestamp = DateTime.UtcNow, Value = 103m });
            bands.MiddleBand.Add(new IndicatorValue { Timestamp = DateTime.UtcNow, Value = 101m });
            bands.LowerBand.Add(new IndicatorValue { Timestamp = DateTime.UtcNow, Value = 99m });
            return bands;
        }

        private static IndicatorData Indicator(string name, decimal value)
        {
            var data = new IndicatorData { Name = name };
            data.Values.Add(new IndicatorValue { Timestamp = DateTime.UtcNow, Value = value });
            return data;
        }
    }
}
