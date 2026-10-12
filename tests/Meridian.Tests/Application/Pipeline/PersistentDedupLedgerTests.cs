using System.Globalization;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class PersistentDedupLedgerTests : IAsyncLifetime
{
    private string _ledgerDirectory = null!;
    private PersistentDedupLedger? _firstLedger;
    private PersistentDedupLedger? _secondLedger;

    public Task InitializeAsync()
    {
        _ledgerDirectory = Path.Combine(Path.GetTempPath(), $"dedup_ledger_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_ledgerDirectory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_secondLedger is not null)
        {
            await _secondLedger.DisposeAsync();
        }

        if (_firstLedger is not null)
        {
            await _firstLedger.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(_ledgerDirectory))
            {
                Directory.Delete(_ledgerDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for transient file handles on Windows.
        }
    }

    [Fact]
    public async Task InitializeAsync_SecondIdleLedger_DoesNotBlockFirstWriter()
    {
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _firstLedger.InitializeAsync();

        _secondLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _secondLedger.InitializeAsync();

        var isDuplicate = await _firstLedger.IsDuplicateAsync(CreateTradeEvent("SPY", 1), CancellationToken.None);

        isDuplicate.Should().BeFalse();

        await _firstLedger.FlushAsync(CancellationToken.None);
        await _firstLedger.DisposeAsync();
        _firstLedger = null;

        var ledgerPath = Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");
        File.Exists(ledgerPath).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(ledgerPath);
        lines.Should().ContainSingle();
    }

    [Fact]
    public async Task IsDuplicateAsync_TradeMiss_PersistsLineAndReloadsAsDuplicate()
    {
        var evt = CreateTradeEvent("AAPL", 1);

        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _firstLedger.InitializeAsync();

        var firstSeen = await _firstLedger.IsDuplicateAsync(evt, CancellationToken.None);
        firstSeen.Should().BeFalse();

        await _firstLedger.FlushAsync(CancellationToken.None);
        await _firstLedger.DisposeAsync();
        _firstLedger = null;

        var ledgerPath = Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");
        File.Exists(ledgerPath).Should().BeTrue();

        var lines = await File.ReadAllLinesAsync(ledgerPath);
        lines.Should().ContainSingle();
        lines[0].Should().Contain("\"k\":\"TEST:AAPL:Trade:");
        lines[0].Should().Contain("\"t\":");

        _secondLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _secondLedger.InitializeAsync();

        var secondSeen = await _secondLedger.IsDuplicateAsync(evt, CancellationToken.None);
        secondSeen.Should().BeTrue();
    }

    [Fact]
    public async Task IsDuplicateAsync_AggregateBars_KeyOnWindowIdentityNotSequence()
    {
        // Streaming aggregate feeds carry no provider sequence (Polygon A/AM), so aggregate
        // dedup identity must be the bar window itself: a redelivered bar (same window, seq 0)
        // is a duplicate, while a different timeframe sharing the same start instant is not.
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _firstLedger.InitializeAsync();

        var start = new DateTimeOffset(2024, 1, 3, 14, 30, 0, TimeSpan.Zero);
        var minuteBar = CreateAggregateEvent("AAPL", start, Meridian.Domain.Models.AggregateTimeframe.Minute);
        var replayedMinuteBar = CreateAggregateEvent("AAPL", start, Meridian.Domain.Models.AggregateTimeframe.Minute);
        var secondBarSameStart = CreateAggregateEvent("AAPL", start, Meridian.Domain.Models.AggregateTimeframe.Second);
        var nextMinuteBar = CreateAggregateEvent("AAPL", start.AddMinutes(1), Meridian.Domain.Models.AggregateTimeframe.Minute);

        (await _firstLedger.IsDuplicateAsync(minuteBar, CancellationToken.None)).Should().BeFalse();
        (await _firstLedger.IsDuplicateAsync(replayedMinuteBar, CancellationToken.None))
            .Should().BeTrue("a redelivered aggregate bar for the same window is a duplicate even with sequence 0");
        (await _firstLedger.IsDuplicateAsync(secondBarSameStart, CancellationToken.None))
            .Should().BeFalse("a second-timeframe bar sharing the start instant is a distinct event");
        (await _firstLedger.IsDuplicateAsync(nextMinuteBar, CancellationToken.None))
            .Should().BeFalse("the next window is a distinct event despite the constant 0 sequence");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void ComputeKeyForBenchmark_TradeAndQuote_PreserveLegacySha256Identity(string cultureName)
    {
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var timestamp = new DateTimeOffset(2024, 1, 3, 14, 30, 0, TimeSpan.FromHours(2));
            // Independently computed legacy SHA-256 vectors cover every hex digit and
            // all 16 retained bytes. The long UTF-8 venue also exercises the pooled buffer.
            var venue = string.Concat(Enumerable.Repeat("市場", 128));
            var trade = new Trade(timestamp, "AAPL", 100.25m, 107,
                AggressorSide.Buy, 42, "TEST", venue);
            var tradeEvent = MarketEvent.Trade(timestamp, "AAPL", trade, "TEST", 42);
            _firstLedger.ComputeKeyForBenchmark(tradeEvent).Should().Be(
                "TEST:AAPL:Trade:5605e6849fa1c641313682a0d62478b0");

            var quote = new BboQuotePayload(timestamp, "AAPL", 100.25m, 133,
                100.50m, 150, 100.375m, 0.25m, 43, "TEST", "XNAS");
            var quoteEvent = MarketEvent.BboQuote(timestamp, "AAPL", quote, "TEST", 43);
            _firstLedger.ComputeKeyForBenchmark(quoteEvent).Should().Be(
                "TEST:AAPL:BboQuote:da5f2e7484eb67c84bbb99901a4438a0");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static MarketEvent CreateAggregateEvent(
        string symbol,
        DateTimeOffset start,
        Meridian.Domain.Models.AggregateTimeframe timeframe)
    {
        var end = timeframe == Meridian.Domain.Models.AggregateTimeframe.Second
            ? start.AddSeconds(1)
            : start.AddMinutes(1);
        var bar = new Meridian.Domain.Models.AggregateBar(
            Symbol: symbol,
            StartTime: start,
            EndTime: end,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1_000,
            Timeframe: timeframe,
            Source: "POLYGON",
            SequenceNumber: 0);

        return MarketEvent.AggregateBar(end, symbol, bar, source: "POLYGON");
    }

    private static MarketEvent CreateTradeEvent(string symbol, long sequence)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.25m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: sequence,
            StreamId: "TEST",
            Venue: "XNAS");

        return MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            symbol,
            trade,
            "TEST",
            sequence);
    }
}
