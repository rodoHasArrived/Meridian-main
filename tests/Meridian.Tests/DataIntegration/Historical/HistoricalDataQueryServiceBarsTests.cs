using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Meridian.DataIntegration.Historical;
using Xunit;

namespace Meridian.Tests.DataIntegration.Historical;

/// <summary>
/// Tests for <see cref="HistoricalDataQueryService.GetBarsAsync"/> which aggregates
/// stored trade events into OHLCV bars used by the live-quotes price chart.
/// </summary>
public sealed class HistoricalDataQueryServiceBarsTests : IDisposable
{
    private readonly string _root;
    private readonly HistoricalDataQueryService _service;

    public HistoricalDataQueryServiceBarsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-bars-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _service = new HistoricalDataQueryService(_root);
    }

    [Fact]
    public async Task GetBarsAsync_WithNoFiles_ReturnsEmptyResult()
    {
        var result = await _service.GetBarsAsync(new HistoricalBarsQuery("AAPL", IntervalMinutes: 5));

        result.Success.Should().BeTrue();
        result.Symbol.Should().Be("AAPL");
        result.Bars.Should().BeEmpty();
        result.TotalBars.Should().Be(0);
    }

    [Fact]
    public async Task GetBarsAsync_AggregatesTradesIntoOhlcvBuckets()
    {
        var symbol = "AAPL";
        var bucketStart = new DateTimeOffset(2024, 06, 03, 14, 30, 0, TimeSpan.Zero);
        var lines = new[]
        {
            BuildTradeJson(symbol, bucketStart, 100.00m, 10),
            BuildTradeJson(symbol, bucketStart.AddMinutes(1), 101.50m, 5),
            BuildTradeJson(symbol, bucketStart.AddMinutes(2), 99.25m, 7),
            BuildTradeJson(symbol, bucketStart.AddMinutes(4), 100.75m, 3),
            // Next bucket
            BuildTradeJson(symbol, bucketStart.AddMinutes(5), 102.00m, 4),
            BuildTradeJson(symbol, bucketStart.AddMinutes(7), 103.50m, 8),
        };
        WriteJsonl(symbol, "2024-06-03", lines);

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(
            Symbol: symbol,
            IntervalMinutes: 5,
            From: new DateOnly(2024, 06, 03),
            To: new DateOnly(2024, 06, 03)));

        result.Success.Should().BeTrue();
        result.Bars.Should().HaveCount(2);

        var first = result.Bars[0];
        first.Start.Should().Be(bucketStart);
        first.Open.Should().Be(100.00m);
        first.High.Should().Be(101.50m);
        first.Low.Should().Be(99.25m);
        first.Close.Should().Be(100.75m);
        first.Volume.Should().Be(25);
        first.TradeCount.Should().Be(4);
        // VWAP = (100*10 + 101.5*5 + 99.25*7 + 100.75*3) / 25
        first.Vwap.Should().BeApproximately(100.18m, 0.001m);

        var second = result.Bars[1];
        second.Start.Should().Be(bucketStart.AddMinutes(5));
        second.Open.Should().Be(102.00m);
        second.High.Should().Be(103.50m);
        second.Low.Should().Be(102.00m);
        second.Close.Should().Be(103.50m);
        second.Volume.Should().Be(12);
        second.TradeCount.Should().Be(2);
    }

    [Fact]
    public async Task GetBarsAsync_FiltersByDateRange()
    {
        var symbol = "MSFT";
        WriteJsonl(symbol, "2024-06-01", new[]
        {
            BuildTradeJson(symbol, new DateTimeOffset(2024, 06, 01, 10, 0, 0, TimeSpan.Zero), 50m, 1),
        });
        WriteJsonl(symbol, "2024-06-05", new[]
        {
            BuildTradeJson(symbol, new DateTimeOffset(2024, 06, 05, 10, 0, 0, TimeSpan.Zero), 51m, 1),
        });
        WriteJsonl(symbol, "2024-06-10", new[]
        {
            BuildTradeJson(symbol, new DateTimeOffset(2024, 06, 10, 10, 0, 0, TimeSpan.Zero), 52m, 1),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(
            Symbol: symbol,
            IntervalMinutes: 60,
            From: new DateOnly(2024, 06, 04),
            To: new DateOnly(2024, 06, 06)));

        result.Bars.Should().HaveCount(1);
        result.Bars[0].Open.Should().Be(51m);
    }

    [Fact]
    public async Task GetBarsAsync_IgnoresNonTradeEvents()
    {
        var symbol = "TSLA";
        var ts = new DateTimeOffset(2024, 06, 03, 14, 0, 0, TimeSpan.Zero);
        WriteJsonl(symbol, "2024-06-03", new[]
        {
            BuildTradeJson(symbol, ts, 200m, 5),
            BuildHeartbeatJson(ts.AddSeconds(1)),
            BuildBboJson(symbol, ts.AddSeconds(2), bid: 199.95m, ask: 200.05m),
            BuildTradeJson(symbol, ts.AddSeconds(3), 200.10m, 2),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(symbol, IntervalMinutes: 5));

        result.Bars.Should().HaveCount(1);
        result.Bars[0].TradeCount.Should().Be(2);
        result.Bars[0].Volume.Should().Be(7);
    }

    [Fact]
    public async Task GetBarsAsync_ReadsGzippedFiles()
    {
        var symbol = "NVDA";
        var ts = new DateTimeOffset(2024, 06, 03, 13, 0, 0, TimeSpan.Zero);
        var lines = new[]
        {
            BuildTradeJson(symbol, ts, 900m, 1),
            BuildTradeJson(symbol, ts.AddMinutes(1), 905m, 1),
        };
        WriteJsonlGz(symbol, "2024-06-03", lines);

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(symbol, IntervalMinutes: 15));

        result.Bars.Should().HaveCount(1);
        result.Bars[0].High.Should().Be(905m);
        result.Bars[0].Low.Should().Be(900m);
    }

    [Fact]
    public async Task GetBarsAsync_FiltersBySymbol_IgnoresOtherSymbolsInSameFile()
    {
        var ts = new DateTimeOffset(2024, 06, 03, 12, 0, 0, TimeSpan.Zero);
        // Write a flat file with mixed symbols (Flat convention picks files by name match too).
        var path = Path.Combine(_root, "QQQ_AAPL_2024-06-03.jsonl");
        File.WriteAllLines(path, new[]
        {
            BuildTradeJson("QQQ", ts, 350m, 10),
            BuildTradeJson("AAPL", ts, 180m, 5),
            BuildTradeJson("QQQ", ts.AddSeconds(30), 351m, 4),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery("QQQ", IntervalMinutes: 1));

        result.Bars.Should().HaveCount(1);
        result.Bars[0].Volume.Should().Be(14);
        result.Bars[0].TradeCount.Should().Be(2);
    }

    [Fact]
    public async Task GetBarsAsync_RejectsInvalidIntervalMinutes()
    {
        await FluentActions
            .Invoking(() => _service.GetBarsAsync(new HistoricalBarsQuery("AAPL", IntervalMinutes: 0)))
            .Should()
            .ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetBarsAsync_SingleSourceAdjustedSeries_CarriesSourceAndRegime()
    {
        var symbol = "AAPL";
        var ts = new DateTimeOffset(2024, 06, 03, 14, 30, 0, TimeSpan.Zero);
        WriteJsonl(symbol, "2024-06-03", new[]
        {
            BuildTradeJson(symbol, ts, 100m, 10, source: "stooq", isAdjusted: true),
            BuildTradeJson(symbol, ts.AddMinutes(1), 101m, 5, source: "stooq", isAdjusted: true),
            BuildTradeJson(symbol, ts.AddMinutes(6), 102m, 4, source: "stooq", isAdjusted: true),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(symbol, IntervalMinutes: 5));

        result.Bars.Should().HaveCount(2);
        result.Bars.Should().OnlyContain(bar => bar.Source == "stooq" && bar.IsAdjusted == true);
        result.Sources.Should().Equal("stooq");
    }

    [Fact]
    public async Task GetBarsAsync_MixedSourceBucket_ReportsMixedSentinelAndNullRegime()
    {
        var symbol = "MSFT";
        var ts = new DateTimeOffset(2024, 06, 03, 15, 0, 0, TimeSpan.Zero);
        WriteJsonl(symbol, "2024-06-03", new[]
        {
            // First bucket mixes two vendors and two regimes.
            BuildTradeJson(symbol, ts, 50m, 1, source: "ALPACA", isAdjusted: false),
            BuildTradeJson(symbol, ts.AddMinutes(1), 51m, 1, source: "IB", isAdjusted: true),
            // Second bucket stays single-vendor, single-regime.
            BuildTradeJson(symbol, ts.AddMinutes(5), 52m, 1, source: "ALPACA", isAdjusted: false),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(symbol, IntervalMinutes: 5));

        result.Bars.Should().HaveCount(2);
        result.Bars[0].Source.Should().Be(HistoricalBarPoint.MixedSources);
        result.Bars[0].IsAdjusted.Should().BeNull("a bucket mixing regimes cannot honestly claim one");
        result.Bars[1].Source.Should().Be("ALPACA");
        result.Bars[1].IsAdjusted.Should().BeFalse();
        result.Sources.Should().Equal("ALPACA", "IB");
    }

    [Fact]
    public async Task GetBarsAsync_UnlabeledTrades_PropagateNullProvenance()
    {
        var symbol = "TSLA";
        var ts = new DateTimeOffset(2024, 06, 03, 14, 0, 0, TimeSpan.Zero);
        WriteJsonl(symbol, "2024-06-03", new[]
        {
            // No source stamp at all, and the at-rest UNKNOWN sentinel; neither declares a regime.
            BuildTradeJson(symbol, ts, 200m, 5),
            BuildTradeJson(symbol, ts.AddMinutes(1), 201m, 2, source: "UNKNOWN"),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(symbol, IntervalMinutes: 5));

        result.Bars.Should().HaveCount(1);
        result.Bars[0].Source.Should().BeNull("no contributing event carried provenance");
        result.Bars[0].IsAdjusted.Should().BeNull("no contributing event declared a regime");
        result.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBarsAsync_LabeledAndUnlabeledEventsInOneBucket_ReportMixed()
    {
        var symbol = "NVDA";
        var ts = new DateTimeOffset(2024, 06, 03, 13, 0, 0, TimeSpan.Zero);
        WriteJsonl(symbol, "2024-06-03", new[]
        {
            BuildTradeJson(symbol, ts, 900m, 1, source: "ALPACA"),
            BuildTradeJson(symbol, ts.AddMinutes(1), 901m, 1),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(symbol, IntervalMinutes: 5));

        result.Bars.Should().HaveCount(1);
        result.Bars[0].Source.Should().Be(
            HistoricalBarPoint.MixedSources,
            "a bucket folding labeled and unlabeled events cannot honestly claim the single label");
        result.Sources.Should().Equal("ALPACA");
    }

    [Fact]
    public async Task GetBarsAsync_ConflictingRegimesWithinSingleSource_CollapseRegimeToNull()
    {
        var symbol = "QQQ";
        var ts = new DateTimeOffset(2024, 06, 03, 12, 0, 0, TimeSpan.Zero);
        WriteJsonl(symbol, "2024-06-03", new[]
        {
            BuildTradeJson(symbol, ts, 350m, 1, source: "stooq", isAdjusted: true),
            BuildTradeJson(symbol, ts.AddMinutes(1), 351m, 1, source: "stooq", isAdjusted: false),
        });

        var result = await _service.GetBarsAsync(new HistoricalBarsQuery(symbol, IntervalMinutes: 5));

        result.Bars.Should().HaveCount(1);
        result.Bars[0].Source.Should().Be("stooq", "the vendor is uniform even though the regime is not");
        result.Bars[0].IsAdjusted.Should().BeNull();
        result.Sources.Should().Equal("stooq");
    }

    private void WriteJsonl(string symbol, string isoDate, IEnumerable<string> lines)
    {
        var dir = Path.Combine(_root, symbol);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{symbol}_{isoDate}.jsonl");
        File.WriteAllLines(path, lines);
    }

    private void WriteJsonlGz(string symbol, string isoDate, IEnumerable<string> lines)
    {
        var dir = Path.Combine(_root, symbol);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{symbol}_{isoDate}.jsonl.gz");
        var content = string.Join('\n', lines) + "\n";
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        var bytes = Encoding.UTF8.GetBytes(content);
        gz.Write(bytes, 0, bytes.Length);
    }

    private static string BuildTradeJson(
        string symbol,
        DateTimeOffset ts,
        decimal price,
        long size,
        string? source = null,
        bool? isAdjusted = null)
    {
        var iso = ts.ToString("o");
        var sourceProperty = source is null ? string.Empty : $"\"source\":\"{source}\",";
        var adjustedProperty = isAdjusted is null ? string.Empty : $",\"isAdjusted\":{(isAdjusted.Value ? "true" : "false")}";
        return $"{{\"timestamp\":\"{iso}\",\"symbol\":\"{symbol}\",\"type\":3,{sourceProperty}" +
               $"\"payload\":{{\"kind\":\"trade\",\"timestamp\":\"{iso}\",\"symbol\":\"{symbol}\"," +
               $"\"price\":{price.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
               $"\"size\":{size},\"aggressor\":0,\"sequenceNumber\":0{adjustedProperty}}}}}";
    }

    private static string BuildHeartbeatJson(DateTimeOffset ts)
    {
        var iso = ts.ToString("o");
        return $"{{\"timestamp\":\"{iso}\",\"symbol\":\"SYSTEM\",\"type\":5," +
               $"\"payload\":{{\"kind\":\"heartbeat\"}}}}";
    }

    private static string BuildBboJson(string symbol, DateTimeOffset ts, decimal bid, decimal ask)
    {
        var iso = ts.ToString("o");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"{{\"timestamp\":\"{iso}\",\"symbol\":\"{symbol}\",\"type\":2," +
               $"\"payload\":{{\"kind\":\"bbo\",\"timestamp\":\"{iso}\",\"symbol\":\"{symbol}\"," +
               $"\"bidPrice\":{bid.ToString(inv)},\"askPrice\":{ask.ToString(inv)}," +
               $"\"bidSize\":1,\"askSize\":1,\"sequenceNumber\":0}}}}";
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
