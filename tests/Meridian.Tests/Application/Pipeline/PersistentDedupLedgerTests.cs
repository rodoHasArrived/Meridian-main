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
    public async Task Initialize_Synchronous_LoadsTheSameStateAsInitializeAsync()
    {
        var seen = CreateTradeEvent("AAPL", 1);
        var unseen = CreateTradeEvent("AAPL", 2);

        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _firstLedger.InitializeAsync();
        (await _firstLedger.IsDuplicateAsync(seen, CancellationToken.None)).Should().BeFalse();
        await _firstLedger.FlushAsync(CancellationToken.None);
        await _firstLedger.DisposeAsync();
        _firstLedger = null;

        // The synchronous load is what the DI composition uses; it must restore the persisted
        // identities exactly as the asynchronous load does.
        _secondLedger = new PersistentDedupLedger(_ledgerDirectory);
        _secondLedger.Initialize();

        (await _secondLedger.IsDuplicateAsync(seen, CancellationToken.None)).Should().BeTrue();
        (await _secondLedger.IsDuplicateAsync(unseen, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public void Initialize_Synchronous_WithNoLedgerFileOrMalformedLines_StartsUsable()
    {
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        var noFile = () => _firstLedger.Initialize();
        noFile.Should().NotThrow();

        File.WriteAllLines(
            Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl"),
            ["not json", "", "{\"k\":null}"]);
        _secondLedger = new PersistentDedupLedger(_ledgerDirectory);
        var malformed = () => _secondLedger.Initialize();
        malformed.Should().NotThrow("malformed lines are skipped, as in the asynchronous load");
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

    [Fact]
    public async Task Appends_SupersededLinesReachThreshold_CompactsToOneLinePerIdentityAndKeepsDurability()
    {
        const int identities = 10;
        var events = Enumerable.Range(1, identities).Select(i => CreateTradeEvent("AAPL", i)).ToArray();
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory, compactionMinLines: identities);
        await _firstLedger.InitializeAsync();

        // A legacy line per identity, then a durability-confirmed line for the same identity:
        // twenty appended lines describing ten identities.
        foreach (var evt in events)
            (await _firstLedger.IsDuplicateAsync(evt)).Should().BeFalse();
        var reservations = new List<DedupReservation>();
        foreach (var evt in events)
        {
            var reserved = await _firstLedger.TryReserveAsync(evt, DedupLookupScope.WalRecovery);
            reserved.Status.Should().Be(DedupReservationStatus.Reserved);
            reservations.Add(reserved.Reservation);
        }
        await _firstLedger.CommitDurableAsync(reservations);

        _firstLedger.DiskLineCount.Should().Be(identities, "nothing ever called CompactAsync before; the ledger now compacts itself");
        await _firstLedger.DisposeAsync();
        _firstLedger = null;
        (await ReadLedgerLinesAsync()).Should().HaveCount(identities);

        // The compacted file must load to the same state: every identity durability-confirmed.
        _secondLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _secondLedger.InitializeAsync();
        foreach (var evt in events)
        {
            (await _secondLedger.TryReserveAsync(evt, DedupLookupScope.WalRecovery)).Status
                .Should().Be(DedupReservationStatus.Duplicate);
        }
    }

    [Fact]
    public async Task Appends_AfterRestartWithMostlyExpiredFile_CompactOnTheFirstWrite()
    {
        // A file left behind by earlier runs: every line is past the TTL.
        var expiredTicks = DateTimeOffset.UtcNow.AddDays(-7).Ticks;
        await File.WriteAllLinesAsync(
            LedgerPath,
            Enumerable.Range(0, 50).Select(i => $"{{\"k\":\"expired-{i}\",\"t\":{expiredTicks.ToString(CultureInfo.InvariantCulture)}}}"));
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory, compactionMinLines: 20);
        await _firstLedger.InitializeAsync();
        _firstLedger.DiskLineCount.Should().Be(50);

        (await _firstLedger.IsDuplicateAsync(CreateTradeEvent("SPY", 1))).Should().BeFalse();

        await _firstLedger.DisposeAsync();
        _firstLedger = null;
        (await ReadLedgerLinesAsync()).Should().ContainSingle("startup no longer re-reads lines that expired long ago");
    }

    [Fact]
    public async Task Appends_BelowThreshold_DoNotCompact()
    {
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory, compactionMinLines: 100);
        await _firstLedger.InitializeAsync();
        var evt = CreateTradeEvent("SPY", 1);

        (await _firstLedger.IsDuplicateAsync(evt)).Should().BeFalse();
        var reserved = await _firstLedger.TryReserveAsync(evt, DedupLookupScope.WalRecovery);
        await _firstLedger.CommitDurableAsync([reserved.Reservation]);

        await _firstLedger.DisposeAsync();
        _firstLedger = null;
        (await ReadLedgerLinesAsync()).Should().HaveCount(2, "both lines for the identity stay until the file is worth rewriting");
    }

    [Fact]
    public async Task Appends_LedgerNeverLoaded_DoNotCompactAwayLinesItNeverRead()
    {
        // Compaction rewrites the file from memory, so an instance that did not load the file
        // must leave lines it does not know about alone.
        var freshTicks = DateTimeOffset.UtcNow.Ticks;
        var existing = Enumerable.Range(0, 5)
            .Select(i => $"{{\"k\":\"unread-{i}\",\"t\":{freshTicks.ToString(CultureInfo.InvariantCulture)},\"v\":2}}")
            .ToArray();
        await File.WriteAllLinesAsync(LedgerPath, existing);
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory, compactionMinLines: 5);

        for (var sequence = 1; sequence <= 20; sequence++)
            (await _firstLedger.IsDuplicateAsync(CreateTradeEvent("SPY", sequence))).Should().BeFalse();

        await _firstLedger.DisposeAsync();
        _firstLedger = null;
        var lines = await ReadLedgerLinesAsync();
        lines.Should().HaveCount(existing.Length + 20);
        lines.Take(existing.Length).Should().Equal(existing);
    }

    private string LedgerPath => Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");

    private async Task<string[]> ReadLedgerLinesAsync()
        => [.. (await File.ReadAllLinesAsync(LedgerPath)).Where(static line => !string.IsNullOrWhiteSpace(line))];

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

    // The ledger identifies a trade by timestamp, price, size, aggressor and venue; the sequence
    // number is not part of it. Stamping fixtures with the wall clock therefore let two trades
    // built in the same clock tick collapse into one identity, which failed tests intermittently.
    // Each sequence gets its own fixed timestamp instead.
    private static readonly DateTimeOffset TradeFixtureEpoch = new(2026, 1, 5, 14, 30, 0, TimeSpan.Zero);

    private static MarketEvent CreateTradeEvent(string symbol, long sequence)
    {
        var trade = new Trade(
            Timestamp: TradeFixtureEpoch.AddMilliseconds(sequence),
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
