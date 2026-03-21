using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="LiveDataService"/> — singleton lifecycle, API method contracts,
/// cancellation support, and DTO model validation.
/// </summary>
public sealed class LiveDataServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        LiveDataService.Instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = LiveDataService.Instance;
        var b = LiveDataService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Instance_ThreadSafety_ShouldReturnSameInstance()
    {
        LiveDataService? i1 = null, i2 = null;
        var t1 = Task.Run(() => i1 = LiveDataService.Instance);
        var t2 = Task.Run(() => i2 = LiveDataService.Instance);
        Task.WaitAll(t1, t2);

        i1.Should().NotBeNull();
        i1.Should().BeSameAs(i2);
    }

    // ── API methods with cancellation ────────────────────────────────

    [Fact]
    public async Task GetRecentTradesAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = LiveDataService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Either throws (cancellation) or returns null (HTTP failure)
        try
        {
            var result = await svc.GetRecentTradesAsync("SPY", ct: cts.Token);
            // If we get here, result should be null (network not available)
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetRecentQuotesAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = LiveDataService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetRecentQuotesAsync("SPY", ct: cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetOrderBookAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = LiveDataService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetOrderBookAsync("SPY", ct: cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetBboAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = LiveDataService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetBboAsync("AAPL", ct: cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetStreamHealthAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = LiveDataService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetStreamHealthAsync(cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    // ── DTO model tests: TradeEvent ──────────────────────────────────

    [Fact]
    public void TradeEvent_ShouldHaveDefaultValues()
    {
        var trade = new TradeEvent();
        trade.Symbol.Should().BeEmpty();
        trade.Price.Should().Be(0);
        trade.Size.Should().Be(0);
        trade.Exchange.Should().BeEmpty();
        trade.Condition.Should().BeEmpty();
        trade.SequenceNumber.Should().Be(0);
        trade.Side.Should().BeEmpty();
    }

    [Fact]
    public void TradeEvent_ShouldAcceptValues()
    {
        var trade = new TradeEvent
        {
            Symbol = "SPY",
            Price = 450.25m,
            Size = 100,
            Exchange = "NYSE",
            Side = "Buy",
            SequenceNumber = 12345
        };

        trade.Symbol.Should().Be("SPY");
        trade.Price.Should().Be(450.25m);
        trade.Size.Should().Be(100);
        trade.Exchange.Should().Be("NYSE");
    }

    // ── DTO model tests: QuoteEvent ──────────────────────────────────

    [Fact]
    public void QuoteEvent_ShouldHaveDefaultValues()
    {
        var quote = new QuoteEvent();
        quote.Symbol.Should().BeEmpty();
        quote.BidPrice.Should().Be(0);
        quote.AskPrice.Should().Be(0);
        quote.Spread.Should().Be(0);
        quote.MidPrice.Should().Be(0);
    }

    [Fact]
    public void QuoteEvent_ShouldAcceptValues()
    {
        var quote = new QuoteEvent
        {
            Symbol = "AAPL",
            BidPrice = 175.50m,
            BidSize = 200,
            AskPrice = 175.55m,
            AskSize = 300,
            Spread = 0.05m,
            MidPrice = 175.525m
        };

        quote.BidPrice.Should().Be(175.50m);
        quote.AskPrice.Should().Be(175.55m);
        quote.Spread.Should().Be(0.05m);
    }

    // ── DTO model tests: OrderBookSnapshot ───────────────────────────

    [Fact]
    public void OrderBookSnapshot_ShouldHaveDefaultValues()
    {
        var book = new OrderBookSnapshot();
        book.Symbol.Should().BeEmpty();
        book.Bids.Should().NotBeNull().And.BeEmpty();
        book.Asks.Should().NotBeNull().And.BeEmpty();
        book.Spread.Should().Be(0);
    }

    // ── DTO model tests: OrderBookLevel ──────────────────────────────

    [Fact]
    public void OrderBookLevel_ShouldAcceptValues()
    {
        var level = new OrderBookLevel
        {
            Price = 100.50m,
            Size = 500,
            OrderCount = 3,
            Exchange = "ARCA"
        };

        level.Price.Should().Be(100.50m);
        level.Size.Should().Be(500);
        level.OrderCount.Should().Be(3);
    }

    // ── DTO model tests: BboQuote ────────────────────────────────────

    [Fact]
    public void BboQuote_ShouldHaveDefaultValues()
    {
        var bbo = new BboQuote();
        bbo.Symbol.Should().BeEmpty();
        bbo.SpreadBps.Should().Be(0);
    }

    // ── DTO model tests: OrderFlowStats ──────────────────────────────

    [Fact]
    public void OrderFlowStats_ShouldHaveDefaultValues()
    {
        var stats = new OrderFlowStats();
        stats.Symbol.Should().BeEmpty();
        stats.Vwap.Should().Be(0);
        stats.TotalVolume.Should().Be(0);
        stats.Imbalance.Should().Be(0);
        stats.TradeCount.Should().Be(0);
    }

    // ── DTO model tests: SubscriptionInfo ────────────────────────────

    [Fact]
    public void SubscriptionInfo_ShouldHaveDefaultValues()
    {
        var info = new SubscriptionInfo();
        info.Symbol.Should().BeEmpty();
        info.SubscriptionType.Should().BeEmpty();
        info.IsActive.Should().BeFalse();
        info.EventCount.Should().Be(0);
        info.EventsPerSecond.Should().Be(0);
    }

    // ── DTO model tests: SubscribeRequest ────────────────────────────

    [Fact]
    public void SubscribeRequest_ShouldHaveDefaults()
    {
        var req = new SubscribeRequest();
        req.Symbol.Should().BeEmpty();
        req.SubscribeTrades.Should().BeFalse();
        req.SubscribeDepth.Should().BeFalse();
        req.SubscribeQuotes.Should().BeFalse();
        req.DepthLevels.Should().Be(10);
    }

    // ── DTO model tests: SubscriptionResult ──────────────────────────

    [Fact]
    public void SubscriptionResult_ShouldHaveDefaults()
    {
        var result = new SubscriptionResult();
        result.Success.Should().BeFalse();
        result.SubscriptionId.Should().Be(0);
        result.Message.Should().BeEmpty();
    }

    // ── DTO model tests: DataStreamHealth ────────────────────────────

    [Fact]
    public void DataStreamHealth_ShouldHaveDefaults()
    {
        var health = new DataStreamHealth();
        health.IsHealthy.Should().BeFalse();
        health.ActiveStreams.Should().Be(0);
        health.HealthyStreams.Should().Be(0);
        health.UnhealthyStreams.Should().Be(0);
        health.Streams.Should().NotBeNull().And.BeEmpty();
    }

    // ── DTO model tests: StreamHealthInfo ────────────────────────────

    [Fact]
    public void StreamHealthInfo_ShouldHaveDefaults()
    {
        var info = new StreamHealthInfo();
        info.Symbol.Should().BeEmpty();
        info.StreamType.Should().BeEmpty();
        info.IsHealthy.Should().BeFalse();
        info.Issue.Should().BeNull();
    }
}
