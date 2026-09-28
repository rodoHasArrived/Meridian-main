using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Adapters;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using AdapterGateway = Meridian.Execution.Adapters.PaperTradingGateway;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Tests.Execution;

public sealed class PaperGatewayTickSizeBoundaryTests
{
    public static IEnumerable<object[]> OrderShapes()
    {
        foreach (var adapter in new[] { false, true })
        {
            foreach (var side in new[] { OrderSide.Buy, OrderSide.Sell })
            {
                foreach (var type in new[] { OrderType.Market, OrderType.Limit, OrderType.StopMarket, OrderType.StopLimit })
                {
                    yield return new object[] { adapter, side, type };
                }
            }
        }
    }

    public static IEnumerable<object[]> LimitShapes() =>
        OrderShapes().Where(shape => (OrderType)shape[2] is OrderType.Limit or OrderType.StopLimit);

    [Theory]
    [MemberData(nameof(OrderShapes))]
    public async Task TickRounding_CannotLeaveObservedEnvelope(bool adapter, OrderSide side, OrderType type)
    {
        var price = side == OrderSide.Buy ? 100.006m : 100.004m;
        var cache = new LiveMarketDataCache();
        RecordTrade(cache, price);

        var fill = await SubmitAsync(adapter, CreateOrder(side, type, price), cache);

        fill.Price.Should().Be(price, "a single observed print defines both envelope bounds");
        fill.Commission.Should().Be(side == OrderSide.Buy ? 1000.06m : 1000.04m,
            "costs must use the final admissible price");
    }

    [Theory]
    [MemberData(nameof(LimitShapes))]
    public async Task TickRounding_CannotCrossLimitEvenInsideEnvelope(bool adapter, OrderSide side, OrderType type)
    {
        var price = side == OrderSide.Buy ? 100.006m : 100.004m;
        var trade = side == OrderSide.Buy ? 101m : 99m;
        var cache = new LiveMarketDataCache();
        RecordQuote(cache, price, price);
        RecordTrade(cache, trade);
        var request = CreateOrder(side, type, price) with { StopPrice = trade };

        var fill = await SubmitAsync(adapter, request, cache);

        fill.Price.Should().Be(price, "the rounded tick is in the envelope but worse than the order's limit");
    }

    [Theory]
    [InlineData(false, OrderSide.Buy)]
    [InlineData(false, OrderSide.Sell)]
    [InlineData(true, OrderSide.Buy)]
    [InlineData(true, OrderSide.Sell)]
    public async Task TickRounding_StillAppliesWhenPriceMeetsBothBounds(bool adapter, OrderSide side)
    {
        var cache = new LiveMarketDataCache();
        RecordQuote(cache, 100.006m, 100.014m);
        var request = CreateOrder(side, OrderType.Limit, side == OrderSide.Buy ? 101m : 99m);

        var fill = await SubmitAsync(adapter, request, cache);

        fill.Price.Should().Be(100.01m);
        fill.Commission.Should().Be(1000.10m);
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderType.Limit)]
    [InlineData(OrderSide.Sell, OrderType.Limit)]
    [InlineData(OrderSide.Buy, OrderType.StopLimit)]
    [InlineData(OrderSide.Sell, OrderType.StopLimit)]
    public async Task RestingExecutionOrder_RetainsBoundsWhenReevaluated(OrderSide side, OrderType type)
    {
        var price = side == OrderSide.Buy ? 100.006m : 100.004m;
        var cache = new LiveMarketDataCache();
        await using var gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance, SecurityMaster(), liveFeed: cache);
        var report = await gateway.SubmitOrderAsync(CreateOrder(side, type, price));
        report.OrderStatus.Should().Be(OrderStatus.Accepted);

        RecordTrade(cache, price);
        gateway.EvaluateSymbol("XYZ");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var fill in gateway.StreamExecutionReportsAsync(timeout.Token))
        {
            fill.OrderStatus.Should().Be(OrderStatus.Filled);
            fill.FillPrice.Should().Be(price);
            return;
        }

        Assert.Fail("No fill arrived after the resting order became marketable.");
    }

    private static OrderRequest CreateOrder(OrderSide side, OrderType type, decimal price) => new()
    {
        Symbol = "XYZ",
        Side = side,
        Type = type,
        Quantity = 1000m,
        LimitPrice = type is OrderType.Limit or OrderType.StopLimit ? price : null,
        StopPrice = type is OrderType.StopMarket or OrderType.StopLimit ? price : null,
        TimeInForce = TimeInForce.GoodTilCancelled
    };

    private static async Task<(decimal? Price, decimal? Commission)> SubmitAsync(
        bool adapter, OrderRequest request, LiveMarketDataCache cache)
    {
        var costs = new PaperTradingCostOptions
        {
            CommissionKind = PaperCommissionKind.BasisPointsOfNotional,
            CommissionRate = 100m,
            CommissionMinimum = 0m
        };
        if (!adapter)
        {
            await using var gateway = new ExecutionGateway(
                NullLogger<ExecutionGateway>.Instance, SecurityMaster(), liveFeed: cache, costOptions: costs);
            var report = await gateway.SubmitOrderAsync(request);
            report.OrderStatus.Should().Be(OrderStatus.Filled);
            return (report.FillPrice, report.Commission);
        }

        await using var adapterGateway = new AdapterGateway(
            NullLogger<AdapterGateway>.Instance, SecurityMaster(), liveFeed: cache, costOptions: costs);
        await adapterGateway.SubmitAsync(request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var update in adapterGateway.StreamOrderUpdatesAsync(timeout.Token))
        {
            update.Status.Should().Be(Meridian.Execution.Models.OrderStatus.Filled);
            return (update.AverageFillPrice, update.Commission);
        }

        throw new InvalidOperationException("No terminal paper fill received.");
    }

    private static void RecordTrade(LiveMarketDataCache cache, decimal price) =>
        cache.RecordTrade("XYZ", new Trade(
            DateTimeOffset.UtcNow, "XYZ", price, Size: 1000,
            Aggressor: AggressorSide.Buy, SequenceNumber: 1));

    private static void RecordQuote(LiveMarketDataCache cache, decimal bid, decimal ask) =>
        cache.RecordQuote("XYZ", new BboQuotePayload(
            DateTimeOffset.UtcNow, "XYZ", bid, 1000, ask, 1000,
            MidPrice: null, Spread: null, SequenceNumber: 1));

    private static ISecurityMasterQueryService SecurityMaster()
    {
        var id = Guid.NewGuid();
        var query = new Mock<ISecurityMasterQueryService>();
        using var terms = JsonDocument.Parse("{}");
        query.Setup(service => service.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker, "XYZ", null, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(new SecurityDetailDto(
                id, "Equity", SecurityStatusDto.Active, "XYZ", "USD",
                terms.RootElement.Clone(), terms.RootElement.Clone(),
                Array.Empty<SecurityIdentifierDto>(), Array.Empty<SecurityAliasDto>(),
                1L, DateTimeOffset.UtcNow.AddYears(-1), null));
        query.Setup(service => service.GetTradingParametersAsync(
            id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradingParametersDto(
                id, LotSize: null, TickSize: 0.01m, ContractMultiplier: null,
                MarginRequirementPct: null, TradingHoursUtc: null,
                CircuitBreakerThresholdPct: null, AsOf: DateTimeOffset.UtcNow));
        return query.Object;
    }
}
