using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Tests;

public sealed class MultiSymbolMergeEnumeratorTests
{
    [Fact]
    public async Task MergeAsync_SingleStream_ReplaysLargeWindowInSourceOrder()
    {
        var events = Enumerable
            .Range(0, 10_000)
            .Select(i => MakeTradeEvent("SPY", DateTimeOffset.UnixEpoch.AddMilliseconds(i)))
            .ToArray();

        var merged = new List<MarketEvent>(events.Length);
        await foreach (var evt in MultiSymbolMergeEnumerator.MergeAsync([ToAsync(events)]))
        {
            merged.Add(evt);
        }

        merged.Should().Equal(events);
    }

    [Fact]
    public async Task MergeAsync_SingleStream_DisposesSourceWhenConsumerStops()
    {
        var source = new TrackingAsyncEnumerable(
            Enumerable
                .Range(0, 100)
                .Select(i => MakeTradeEvent("SPY", DateTimeOffset.UnixEpoch.AddMilliseconds(i)))
                .ToArray());

        await foreach (var _ in MultiSymbolMergeEnumerator.MergeAsync([source]))
        {
            break;
        }

        source.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task MergeAsync_MultipleStreams_PreservesChronologicalOrderWithStableTieBreak()
    {
        var first = MakeTradeEvent("AAPL", DateTimeOffset.UnixEpoch.AddMilliseconds(10));
        var tieFromFirstStream = MakeTradeEvent("AAPL", DateTimeOffset.UnixEpoch.AddMilliseconds(20));
        var tieFromSecondStream = MakeTradeEvent("MSFT", DateTimeOffset.UnixEpoch.AddMilliseconds(20));
        var last = MakeTradeEvent("MSFT", DateTimeOffset.UnixEpoch.AddMilliseconds(30));

        var merged = new List<MarketEvent>();
        await foreach (var evt in MultiSymbolMergeEnumerator.MergeAsync(
                           [
                               ToAsync([first, tieFromFirstStream]),
                               ToAsync([tieFromSecondStream, last])
                           ]))
        {
            merged.Add(evt);
        }

        merged.Should().Equal(first, tieFromFirstStream, tieFromSecondStream, last);
    }

    [Fact]
    public async Task MergeAsync_EventsWithinSameMillisecond_UsesFullUtcTicksBeforeStreamTieBreak()
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(10);
        var laterFromFirstStream = MakeTradeEvent("AAPL", timestamp.AddTicks(9));
        var earlierFromSecondStream = MakeTradeEvent("MSFT", timestamp.AddTicks(1));

        var merged = new List<MarketEvent>();
        await foreach (var evt in MultiSymbolMergeEnumerator.MergeAsync(
                           [ToAsync([laterFromFirstStream]), ToAsync([earlierFromSecondStream])]))
        {
            merged.Add(evt);
        }

        merged.Should().Equal(earlierFromSecondStream, laterFromFirstStream);
    }

    [Fact]
    public async Task MergeAsync_WhenSourceRegresses_FailsClosedWithStreamEvidence()
    {
        var later = MakeTradeEvent("SPY", DateTimeOffset.UnixEpoch.AddTicks(2));
        var earlier = MakeTradeEvent("SPY", DateTimeOffset.UnixEpoch.AddTicks(1));

        var act = async () =>
        {
            await foreach (var _ in MultiSymbolMergeEnumerator.MergeAsync([ToAsync([later, earlier])]))
            {
            }
        };

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*stream 0*chronological*");
    }

    [Fact]
    public async Task MergeAsync_WhenPrimingThrows_DisposesEveryEnumeratorCreatedSoFar()
    {
        var first = new TrackingAsyncEnumerable(
            [MakeTradeEvent("SPY", DateTimeOffset.UnixEpoch)]);
        var second = new ThrowingAsyncEnumerable();

        var act = async () =>
        {
            await foreach (var _ in MultiSymbolMergeEnumerator.MergeAsync([first, second]))
            {
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("priming failed");
        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task MergeAsync_WhenCancelledDuringReplay_ThrowsAndDisposesEveryEnumerator()
    {
        var first = new TrackingAsyncEnumerable(
            [MakeTradeEvent("AAPL", DateTimeOffset.UnixEpoch)]);
        var second = new TrackingAsyncEnumerable(
            [MakeTradeEvent("MSFT", DateTimeOffset.UnixEpoch.AddTicks(1))]);
        using var cts = new CancellationTokenSource();

        await using (var enumerator = MultiSymbolMergeEnumerator
                         .MergeAsync([first, second], cts.Token)
                         .GetAsyncEnumerator(cts.Token))
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            cts.Cancel();

            var act = async () => await enumerator.MoveNextAsync();
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ApplyCorporateActionAdjustmentsAsync_MixedStream_YieldsWithoutBufferingFutureBars()
    {
        var adjustment = new TrackingCorporateActionAdjustmentService();
        var produced = 0;

        async IAsyncEnumerable<MarketEvent> Source()
        {
            produced++;
            yield return MakeBarEvent("SPY", DateTimeOffset.UnixEpoch, 100m);
            await Task.Yield();

            produced++;
            yield return MakeTradeEvent("SPY", DateTimeOffset.UnixEpoch.AddMilliseconds(1));
            await Task.Yield();

            for (var i = 0; i < 10_000; i++)
            {
                produced++;
                yield return MakeBarEvent("SPY", DateTimeOffset.UnixEpoch.AddMilliseconds(i + 2), 101m + i);
                await Task.Yield();
            }
        }

        var consumed = new List<MarketEvent>(capacity: 2);
        await foreach (var evt in BacktestEngine.ApplyCorporateActionAdjustmentsAsync(
                           Source(),
                           "SPY",
                           adjustment,
                           CancellationToken.None))
        {
            consumed.Add(evt);
            if (consumed.Count == 2)
            {
                break;
            }
        }

        consumed.Should().HaveCount(2);
        consumed[0].Payload.Should().BeOfType<HistoricalBar>()
            .Which.Close.Should().Be(101m, "the fake adjustment increments close by one");
        consumed[1].Type.Should().Be(MarketEventType.Trade);
        produced.Should().Be(2, "the adjustment wrapper should not pre-read future bars after the consumer stops");
        adjustment.AdjustBarCallCount.Should().Be(1);
    }

    private static async IAsyncEnumerable<MarketEvent> ToAsync(IEnumerable<MarketEvent> events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }

    private static MarketEvent MakeTradeEvent(string symbol, DateTimeOffset timestamp) =>
        MarketEvent.Trade(
            timestamp,
            symbol,
            new Trade(timestamp, symbol, 100m, 10L, AggressorSide.Buy, timestamp.ToUnixTimeMilliseconds(), Venue: "test"));

    private static MarketEvent MakeBarEvent(string symbol, DateTimeOffset timestamp, decimal close) =>
        MarketEvent.HistoricalBar(
            timestamp,
            symbol,
            new HistoricalBar(
                symbol,
                DateOnly.FromDateTime(timestamp.UtcDateTime.Date),
                close - 1m,
                close + 1m,
                close - 2m,
                close,
                Volume: 1_000L,
                Source: "test",
                SequenceNumber: timestamp.ToUnixTimeMilliseconds()));

    private sealed class TrackingAsyncEnumerable(IReadOnlyList<MarketEvent> events) : IAsyncEnumerable<MarketEvent>
    {
        public int DisposeCount { get; private set; }

        public IAsyncEnumerator<MarketEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(this, events);

        private sealed class Enumerator(
            TrackingAsyncEnumerable owner,
            IReadOnlyList<MarketEvent> events) : IAsyncEnumerator<MarketEvent>
        {
            private int _index = -1;

            public MarketEvent Current { get; private set; } = null!;

            public ValueTask<bool> MoveNextAsync()
            {
                _index++;
                if (_index >= events.Count)
                    return ValueTask.FromResult(false);

                Current = events[_index];
                return ValueTask.FromResult(true);
            }

            public ValueTask DisposeAsync()
            {
                owner.DisposeCount++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ThrowingAsyncEnumerable : IAsyncEnumerable<MarketEvent>
    {
        public int DisposeCount { get; private set; }

        public IAsyncEnumerator<MarketEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(this);

        private sealed class Enumerator(ThrowingAsyncEnumerable owner) : IAsyncEnumerator<MarketEvent>
        {
            public MarketEvent Current => null!;

            public ValueTask<bool> MoveNextAsync() =>
                ValueTask.FromException<bool>(new InvalidOperationException("priming failed"));

            public ValueTask DisposeAsync()
            {
                owner.DisposeCount++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TrackingCorporateActionAdjustmentService : ICorporateActionAdjustmentService
    {
        public int AdjustBarCallCount { get; private set; }

        public Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
            IReadOnlyList<HistoricalBar> bars,
            string ticker,
            CancellationToken ct = default)
        {
            var adjusted = bars.Select(Adjust).ToArray();
            return Task.FromResult<IReadOnlyList<HistoricalBar>>(adjusted);
        }

        public Task<HistoricalBar> AdjustBarAsync(
            HistoricalBar bar,
            string ticker,
            CancellationToken ct = default)
        {
            AdjustBarCallCount++;
            return Task.FromResult(Adjust(bar));
        }

        private static HistoricalBar Adjust(HistoricalBar bar) =>
            new(
                bar.Symbol,
                bar.SessionDate,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close + 1m,
                bar.Volume,
                bar.Source,
                bar.SequenceNumber);
    }
}
