using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Tests.Execution;

/// <summary>
/// A limit order becomes visible to cancel and to the background evaluator as soon as it is
/// added to the resting book, before SubmitOrderAsync finishes its first price check. Every
/// path that ends the order must claim it exactly once, so one order never yields two
/// terminal outcomes (a fill plus a cancel, or two fills). The feed below runs the competing
/// path at exactly that moment, so the interleaving is deterministic rather than timing-based.
/// </summary>
public sealed class PaperTradingGatewayConcurrencyTests
{
    private const string Symbol = "SPY";
    private const string OrderId = "race-1";

    [Fact]
    public async Task MarketableLimit_CancelledDuringFirstCheck_IsNotAlsoFilled()
    {
        var feed = new InterleavingFeed();
        await using var gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance, liveFeed: feed);
        ExecutionReport? cancel = null;
        feed.OnFirstSnapshot = () => cancel = gateway.CancelOrderAsync(OrderId).GetAwaiter().GetResult();

        var submit = await gateway.SubmitOrderAsync(Limit(limitPrice: 101m, TimeInForce.Day));

        cancel!.OrderStatus.Should().Be(OrderStatus.Cancelled, "cancel claimed the resting order first");
        submit.OrderStatus.Should().Be(OrderStatus.Accepted,
            "the submit must not report a fill for an order the cancel already ended");
        submit.FilledQuantity.Should().Be(0m);
        (await NextRestingReportAsync(gateway, TimeSpan.FromMilliseconds(300))).Should().BeNull();
    }

    [Fact]
    public async Task UnmarketableIoc_CancelledDuringFirstCheck_IsNotCancelledTwice()
    {
        var feed = new InterleavingFeed();
        await using var gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance, liveFeed: feed);
        ExecutionReport? cancel = null;
        feed.OnFirstSnapshot = () => cancel = gateway.CancelOrderAsync(OrderId).GetAwaiter().GetResult();

        var submit = await gateway.SubmitOrderAsync(Limit(limitPrice: 90m, TimeInForce.ImmediateOrCancel));

        cancel!.OrderStatus.Should().Be(OrderStatus.Cancelled);
        submit.OrderStatus.Should().Be(OrderStatus.Accepted, "only the cancel that claimed the order reports Cancelled");
    }

    [Fact]
    public async Task MarketableLimit_FilledByEvaluatorDuringFirstCheck_IsFilledOnce()
    {
        var feed = new InterleavingFeed();
        await using var gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance, liveFeed: feed);
        ExecutionReport? evaluatorFill = null;
        feed.OnFirstSnapshot = () =>
        {
            gateway.EvaluateSymbol(Symbol);
            evaluatorFill = NextRestingReportAsync(gateway, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        };

        var submit = await gateway.SubmitOrderAsync(Limit(limitPrice: 101m, TimeInForce.Day));

        evaluatorFill.Should().NotBeNull("the background evaluator claimed and filled the order");
        evaluatorFill!.OrderStatus.Should().Be(OrderStatus.Filled);
        submit.OrderStatus.Should().Be(OrderStatus.Accepted,
            "the submit must not report a second fill for the same order");
        submit.FilledQuantity.Should().Be(0m);
    }

    private static OrderRequest Limit(decimal limitPrice, TimeInForce timeInForce) => new()
    {
        Symbol = Symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        LimitPrice = limitPrice,
        Quantity = 1m,
        ClientOrderId = OrderId,
        TimeInForce = timeInForce
    };

    private static async Task<ExecutionReport?> NextRestingReportAsync(ExecutionGateway gateway, TimeSpan wait)
    {
        using var cts = new CancellationTokenSource(wait);
        try
        {
            await foreach (var report in gateway.StreamExecutionReportsAsync(cts.Token))
                return report;
        }
        catch (OperationCanceledException)
        {
        }

        return null;
    }

    /// <summary>
    /// Last trade at 100. The first snapshot request (the submit's initial check) runs the
    /// competing path before returning; later requests, such as the evaluator's, return normally.
    /// </summary>
    private sealed class InterleavingFeed : ILiveFeedAdapter
    {
        private readonly LiveMarketDataCache _cache = new();
        private int _calls;

        public InterleavingFeed()
        {
            _cache.RecordTrade(Symbol, new Trade(
                DateTimeOffset.UtcNow, Symbol, 100m, Size: 100, Aggressor: AggressorSide.Buy, SequenceNumber: 1));
        }

        public Action? OnFirstSnapshot { get; set; }

        public IReadOnlySet<string> SubscribedSymbols => ((ILiveFeedAdapter)_cache).SubscribedSymbols;
        public Trade? GetLastTrade(string symbol) => ((ILiveFeedAdapter)_cache).GetLastTrade(symbol);
        public BboQuotePayload? GetLastQuote(string symbol) => ((ILiveFeedAdapter)_cache).GetLastQuote(symbol);
        public LOBSnapshot? GetLastOrderBook(string symbol) => ((ILiveFeedAdapter)_cache).GetLastOrderBook(symbol);
        public HistoricalBar? GetLastBar(string symbol) => ((ILiveFeedAdapter)_cache).GetLastBar(symbol);

        public MarketDataSnapshot GetSnapshot(string symbol)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                OnFirstSnapshot?.Invoke();
            return ((ILiveFeedAdapter)_cache).GetSnapshot(symbol);
        }
    }
}
