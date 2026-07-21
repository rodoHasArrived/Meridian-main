using FluentAssertions;
using Meridian.Application.DataQuality;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Domain.Events;
using Xunit;

namespace Meridian.Tests.Application.DataQuality;

public sealed class QualityMonitoringPublisherTests
{
    [Fact]
    public async Task TryPublish_TradeEvent_FeedsQualityMonitoringService()
    {
        await using var quality = new DataQualityMonitoringService();
        var inner = new RecordingPublisher();
        await using var sut = new QualityMonitoringPublisher(inner, quality);

        sut.TryPublish(CreateTradeEvent("SPY", sequence: 1)).Should().BeTrue();

        await WaitUntilAsync(() => sut.TradesObserved == 1);
        inner.Events.Should().ContainSingle(evt => evt.Type == MarketEventType.Trade);
        quality.GetSymbolHealth("SPY").Should().NotBeNull();
        quality.GetRealTimeMetrics().ActiveSymbols.Should().Be(1);
    }

    [Fact]
    public async Task TryPublish_QuoteEvent_FeedsQualityMonitoringService()
    {
        await using var quality = new DataQualityMonitoringService();
        var inner = new RecordingPublisher();
        await using var sut = new QualityMonitoringPublisher(inner, quality);

        sut.TryPublish(CreateQuoteEvent("MSFT", sequence: 7)).Should().BeTrue();

        await WaitUntilAsync(() => sut.QuotesObserved == 1);
        inner.Events.Should().ContainSingle(evt => evt.Type == MarketEventType.BboQuote);
        quality.GetSymbolHealth("MSFT").Should().NotBeNull();
    }

    [Fact]
    public async Task TryPublish_NonHotPathEvent_IsForwardedButNotObserved()
    {
        await using var quality = new DataQualityMonitoringService();
        var inner = new RecordingPublisher();
        await using var sut = new QualityMonitoringPublisher(inner, quality);

        sut.TryPublish(MarketEvent.Heartbeat(DateTimeOffset.UtcNow)).Should().BeTrue();
        sut.TryPublish(CreateTradeEvent("SPY", sequence: 1)).Should().BeTrue();

        // The trade published after the heartbeat has been observed, so the heartbeat
        // has already passed through the sampling filter without being recorded.
        await WaitUntilAsync(() => sut.TradesObserved == 1);
        sut.QuotesObserved.Should().Be(0);
        inner.Events.Should().HaveCount(2);
        quality.GetSymbolHealth("SYSTEM").Should().BeNull();
    }

    [Fact]
    public async Task TryPublish_WhenPipelineRejectsEvent_PreservesResultAndStillObservesFeed()
    {
        await using var quality = new DataQualityMonitoringService();
        var inner = new RecordingPublisher { Result = false };
        await using var sut = new QualityMonitoringPublisher(inner, quality);

        sut.TryPublish(CreateTradeEvent("SPY", sequence: 1)).Should().BeFalse();

        // Feed quality describes what the provider delivered, not what storage accepted.
        await WaitUntilAsync(() => sut.TradesObserved == 1);
        quality.GetSymbolHealth("SPY").Should().NotBeNull();
    }

    [Fact]
    public async Task TryPublish_TradeWithExchangeTimestamp_RecordsLatencySample()
    {
        await using var quality = new DataQualityMonitoringService();
        var inner = new RecordingPublisher();
        await using var sut = new QualityMonitoringPublisher(inner, quality);

        var evt = CreateTradeEvent("SPY", sequence: 1)
            .StampReceiveTime(exchangeTs: DateTimeOffset.UtcNow.AddMilliseconds(-50));
        sut.TryPublish(evt).Should().BeTrue();

        await WaitUntilAsync(() => sut.TradesObserved == 1);
        quality.LatencyHistogram.GetStatistics().TotalSamples.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_DrainsPendingSamplesBeforeCompleting()
    {
        await using var quality = new DataQualityMonitoringService();
        var inner = new RecordingPublisher();
        var sut = new QualityMonitoringPublisher(inner, quality);

        for (var i = 1; i <= 100; i++)
        {
            sut.TryPublish(CreateTradeEvent("SPY", sequence: i));
        }

        await sut.DisposeAsync();

        sut.TradesObserved.Should().Be(100);
        sut.SamplesDropped.Should().Be(0);
    }

    private static MarketEvent CreateTradeEvent(string symbol, long sequence)
    {
        var now = DateTimeOffset.UtcNow;
        var trade = new Trade(
            Timestamp: now,
            Symbol: symbol,
            Price: 100.50m,
            Size: 100L,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: sequence);

        return MarketEvent.Trade(now, symbol, trade, sequence);
    }

    private static MarketEvent CreateQuoteEvent(string symbol, long sequence)
    {
        var now = DateTimeOffset.UtcNow;
        var quote = new BboQuotePayload(
            Timestamp: now,
            Symbol: symbol,
            BidPrice: 100.00m,
            BidSize: 200L,
            AskPrice: 100.05m,
            AskSize: 300L,
            MidPrice: 100.025m,
            Spread: 0.05m,
            SequenceNumber: sequence);

        return MarketEvent.BboQuote(now, symbol, quote, sequence);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5_000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue("the background quality consumer should observe the sample within {0}ms", timeoutMs);
    }

    private sealed class RecordingPublisher : IMarketEventPublisher
    {
        private readonly List<MarketEvent> _events = new();

        public bool Result { get; set; } = true;

        public IReadOnlyList<MarketEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToList();
                }
            }
        }

        public bool TryPublish(in MarketEvent evt)
        {
            lock (_events)
            {
                _events.Add(evt);
            }

            return Result;
        }
    }
}
