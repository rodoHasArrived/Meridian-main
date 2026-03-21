using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Application.Indicators;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for technical indicator calculations.
///
/// Reference: docs/open-source-references.md #25 (Skender.Stock.Indicators)
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class IndicatorBenchmarks
{
    private TechnicalIndicatorService _indicatorService = null!;
    // Separate service instance used only by GetSnapshot_AfterProcessing.
    // Populated via a targeted IterationSetup so that ProcessTrade overhead is
    // excluded from the GetSnapshot measurement.
    private TechnicalIndicatorService _snapshotService = null!;
    private MarketTradeUpdate[] _trades = null!;
    private HistoricalBar[] _bars = null!;

    private static readonly IndicatorConfiguration AllIndicatorsConfig = new()
    {
        EnabledIndicators = new HashSet<IndicatorType>
        {
            IndicatorType.SMA,
            IndicatorType.EMA,
            IndicatorType.RSI,
            IndicatorType.MACD,
            IndicatorType.BollingerBands
        }
    };

    [Params(100, 500, 1000)]
    public int DataPoints;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        var basePrice = 450m;

        // Generate trade updates
        _trades = new MarketTradeUpdate[DataPoints];
        for (int i = 0; i < DataPoints; i++)
        {
            basePrice += (decimal)(random.NextDouble() - 0.5) * 0.5m;
            _trades[i] = new MarketTradeUpdate(
                Timestamp: DateTimeOffset.UtcNow.AddSeconds(i),
                Symbol: "SPY",
                Price: basePrice,
                Size: random.Next(100, 10000),
                Aggressor: random.Next(2) == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                SequenceNumber: i,
                StreamId: "BENCH",
                Venue: "TEST"
            );
        }

        // Generate historical bars
        basePrice = 450m;
        _bars = new HistoricalBar[DataPoints];
        for (int i = 0; i < DataPoints; i++)
        {
            var dayChange = (decimal)(random.NextDouble() - 0.5) * 5m;
            var open = basePrice;
            var close = basePrice + dayChange;

            // Ensure high is always >= max(open, close)
            var maxPrice = Math.Max(open, close);
            var high = maxPrice + (decimal)random.NextDouble() * 2m;

            // Ensure low is always <= min(open, close)
            var minPrice = Math.Min(open, close);
            var low = minPrice - (decimal)random.NextDouble() * 2m;

            _bars[i] = new HistoricalBar(
                Symbol: "SPY",
                SessionDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-DataPoints + i)),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: random.Next(1000000, 100000000),
                SequenceNumber: i
            );

            basePrice = close;
        }
    }

    [IterationSetup(Targets = new[]
    {
        nameof(ProcessTrades_Streaming),
        nameof(CalculateHistorical_AllIndicators)
    })]
    public void IterationSetup()
    {
        _indicatorService = new TechnicalIndicatorService(AllIndicatorsConfig);
    }

    /// <summary>
    /// Pre-processes all trades so that <see cref="GetSnapshot_AfterProcessing"/> only
    /// measures the snapshot retrieval, not the streaming ingestion.
    /// </summary>
    [IterationSetup(Targets = new[] { nameof(GetSnapshot_AfterProcessing) })]
    public void IterationSetupForSnapshot()
    {
        _snapshotService = new TechnicalIndicatorService(AllIndicatorsConfig);
        foreach (var trade in _trades)
            _snapshotService.ProcessTrade(trade);
    }

    [Benchmark(Baseline = true)]
    public void ProcessTrades_Streaming()
    {
        foreach (var trade in _trades)
            _indicatorService.ProcessTrade(trade);
    }

    [Benchmark]
    public HistoricalIndicatorResult CalculateHistorical_AllIndicators()
    {
        return _indicatorService.CalculateHistorical("SPY", _bars);
    }

    /// <summary>
    /// Measures only the cost of retrieving a pre-computed snapshot.
    /// Trade processing is done in the targeted <see cref="IterationSetupForSnapshot"/> method
    /// so it does not inflate this measurement.
    /// </summary>
    [Benchmark]
    public IndicatorSnapshot? GetSnapshot_AfterProcessing()
    {
        return _snapshotService.GetSnapshot("SPY");
    }
}

/// <summary>
/// Benchmarks for individual indicator calculations.
/// Each benchmark measures only <c>CalculateHistorical</c>; service creation and indicator
/// configuration are done in the targeted <c>[IterationSetup]</c> methods so that object
/// allocation inside the indicator library is not double-counted.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class SingleIndicatorBenchmarks
{
    private HistoricalBar[] _bars = null!;
    private TechnicalIndicatorService _smaService = null!;
    private TechnicalIndicatorService _rsiService = null!;
    private TechnicalIndicatorService _macdService = null!;
    private TechnicalIndicatorService _bollingerService = null!;
    private TechnicalIndicatorService _allService = null!;

    // Static indicator sets avoid per-iteration HashSet allocations.
    private static readonly IndicatorConfiguration SmaConfig = new()
    {
        EnabledIndicators = new HashSet<IndicatorType> { IndicatorType.SMA }
    };
    private static readonly IndicatorConfiguration RsiConfig = new()
    {
        EnabledIndicators = new HashSet<IndicatorType> { IndicatorType.RSI }
    };
    private static readonly IndicatorConfiguration MacdConfig = new()
    {
        EnabledIndicators = new HashSet<IndicatorType> { IndicatorType.MACD }
    };
    private static readonly IndicatorConfiguration BollingerConfig = new()
    {
        EnabledIndicators = new HashSet<IndicatorType> { IndicatorType.BollingerBands }
    };

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        var basePrice = 100m;

        _bars = new HistoricalBar[500];
        for (int i = 0; i < 500; i++)
        {
            var dayChange = (decimal)(random.NextDouble() - 0.5) * 2m;
            var open = basePrice;
            var close = basePrice + dayChange;

            // Ensure high is always >= max(open, close)
            var maxPrice = Math.Max(open, close);
            var high = maxPrice + (decimal)random.NextDouble() * 1m;

            // Ensure low is always <= min(open, close)
            var minPrice = Math.Min(open, close);
            var low = minPrice - (decimal)random.NextDouble() * 1m;

            _bars[i] = new HistoricalBar(
                Symbol: "SPY",
                SessionDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-500 + i)),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: random.Next(1000000, 100000000),
                SequenceNumber: i
            );

            basePrice = close;
        }
    }

    // Each benchmark gets its own IterationSetup so BenchmarkDotNet allocates a fresh
    // service for every iteration, keeping measurement state clean.
    [IterationSetup(Targets = new[] { nameof(Calculate_SMA_Only) })]
    public void SetupSma() => _smaService = new TechnicalIndicatorService(SmaConfig);

    [IterationSetup(Targets = new[] { nameof(Calculate_RSI_Only) })]
    public void SetupRsi() => _rsiService = new TechnicalIndicatorService(RsiConfig);

    [IterationSetup(Targets = new[] { nameof(Calculate_MACD_Only) })]
    public void SetupMacd() => _macdService = new TechnicalIndicatorService(MacdConfig);

    [IterationSetup(Targets = new[] { nameof(Calculate_BollingerBands_Only) })]
    public void SetupBollinger() => _bollingerService = new TechnicalIndicatorService(BollingerConfig);

    [IterationSetup(Targets = new[] { nameof(Calculate_AllIndicators) })]
    public void SetupAll() => _allService = new TechnicalIndicatorService();

    /// <summary>
    /// SMA — simplest indicator; provides the baseline latency floor.
    /// </summary>
    [Benchmark(Baseline = true)]
    public HistoricalIndicatorResult Calculate_SMA_Only()
        => _smaService.CalculateHistorical("TEST", _bars);

    [Benchmark]
    public HistoricalIndicatorResult Calculate_RSI_Only()
        => _rsiService.CalculateHistorical("TEST", _bars);

    [Benchmark]
    public HistoricalIndicatorResult Calculate_MACD_Only()
        => _macdService.CalculateHistorical("TEST", _bars);

    [Benchmark]
    public HistoricalIndicatorResult Calculate_BollingerBands_Only()
        => _bollingerService.CalculateHistorical("TEST", _bars);

    /// <summary>
    /// All indicators combined — shows the cumulative overhead of the default configuration.
    /// </summary>
    [Benchmark]
    public HistoricalIndicatorResult Calculate_AllIndicators()
        => _allService.CalculateHistorical("TEST", _bars);
}
