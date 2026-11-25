using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Direct coverage of the notional every exposure, concentration and notional rule measures.
/// Each case is the dollar amount the gateway would actually route, so a mispriced branch
/// here is a limit that silently stops limiting.
/// </summary>
public sealed class OrderNotionalResolverTests
{
    private static PortfolioExposureSnapshot Snapshot(params SymbolExposure[] symbols) => new(
        GrossExposure: 0m,
        NetExposure: 0m,
        PortfolioValue: 1_000_000m,
        SymbolExposures: symbols.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase),
        AsOf: DateTimeOffset.UtcNow);

    private static PortfolioExposureSnapshot Priced(string symbol, decimal price)
        => Snapshot(new SymbolExposure(symbol, 0m, 0m, price));

    private static OrderRequest Order(
        OrderSide side,
        decimal quantity,
        string symbol = "AAPL",
        OrderType type = OrderType.Market,
        decimal? limit = null,
        decimal? stop = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new()
        {
            Symbol = symbol,
            Side = side,
            Type = type,
            Quantity = quantity,
            LimitPrice = limit,
            StopPrice = stop,
            Metadata = metadata
        };

    [Fact]
    public void MarketOrder_WithNoPriceAnywhere_IsUnmeasurable()
    {
        var order = Order(OrderSide.Buy, 10m);

        OrderNotionalResolver.Resolve(order, Snapshot()).Should().BeNull();
        OrderNotionalResolver.DescribeUnmeasurable(order, Snapshot()).Should().Contain("cannot be measured");
    }

    [Fact]
    public void MarketOrder_UsesTheLiveMarkBeforeTheSnapshotReference()
    {
        var order = Order(OrderSide.Buy, 10m);

        OrderNotionalResolver.Resolve(order, Priced("AAPL", 50m)).Should().Be(500m);
        OrderNotionalResolver.Resolve(order, Priced("AAPL", 50m), _ => 60m).Should().Be(600m);
        OrderNotionalResolver.DescribeUnmeasurable(order, Priced("AAPL", 50m)).Should().BeNull();
    }

    [Fact]
    public void BuyLimit_CapsWhatTheOrderCanPay_EvenWithoutAMarket()
    {
        var order = Order(OrderSide.Buy, 1_000m, type: OrderType.Limit, limit: 1m);

        OrderNotionalResolver.Resolve(order, Priced("AAPL", 100m)).Should().Be(1_000m,
            "a resting buy limited at $1 can never pay more than $1 a share");
        OrderNotionalResolver.Resolve(order, Snapshot()).Should().Be(1_000m);
    }

    [Fact]
    public void SellLimit_IsAFloor_SoItIsValuedAtTheHigherMarket()
    {
        var order = Order(OrderSide.Sell, 10_000m, type: OrderType.Limit, limit: 1m);

        OrderNotionalResolver.Resolve(order, Priced("AAPL", 100m)).Should().Be(1_000_000m);
        OrderNotionalResolver.Resolve(order, Snapshot()).Should().BeNull(
            "a floor is no bound on the routed amount when the market is unknown");
    }

    [Fact]
    public void BuyStop_IsATrigger_SoATriggeredStopRoutesAtTheMarket()
    {
        var order = Order(OrderSide.Buy, 1_000m, type: OrderType.StopMarket, stop: 1m);

        OrderNotionalResolver.Resolve(order, Priced("AAPL", 100m)).Should().Be(100_000m);
    }

    [Fact]
    public void BrokerNotionalMetadata_IsWhatRoutes()
    {
        var order = Order(OrderSide.Buy, 3m, metadata: new Dictionary<string, string> { ["notional"] = "2500" });

        OrderNotionalResolver.Resolve(order, Priced("AAPL", 100m)).Should().Be(2_500m);
    }

    [Fact]
    public void FaceValueFixedIncome_PricesAsAPercentageOfPar_AndIgnoresDollarNotional()
    {
        var metadata = OrderSizingMetadata.WithFaceValuePercentageOfPar(
            new Dictionary<string, string> { ["notional"] = "5" });
        var order = Order(OrderSide.Buy, 100_000m, "UST10Y", OrderType.Limit, limit: 101.25m, metadata: metadata);

        OrderNotionalResolver.Resolve(order, Snapshot()).Should().Be(101_250m,
            "100,000 face at 101.25% of par is $101,250");
    }

    [Fact]
    public void OptionOrder_IsScaledByTheContractMultiplier()
    {
        var order = Order(OrderSide.Buy, 3m, "AAPL260116C00150000", OrderType.Limit, limit: 2.5m) with
        {
            OptionContract = new OptionContractIdentity { UnderlyingSymbol = "AAPL", Multiplier = "100" }
        };

        OrderNotionalResolver.Resolve(order, Snapshot()).Should().Be(750m);
    }

    [Fact]
    public void OccSymbolWithoutContractIdentity_IsStillValuedAsAnOption()
    {
        var order = Order(OrderSide.Buy, 1m, "AAPL260116C00150000", OrderType.Limit, limit: 2m);

        OrderNotionalResolver.Resolve(order, Snapshot()).Should().Be(200m,
            "the broker infers an option from the OCC symbol, so risk must apply the 100x multiplier too");
    }

    [Fact]
    public void Spread_ValuesEachLegAtItsOwnSideOfTheBook_NotTheNetDebit()
    {
        var contract = new OptionContractIdentity { Multiplier = "100" };
        var order = Order(OrderSide.Buy, 1m, "SPREAD", OrderType.Limit, limit: 1m) with
        {
            Legs =
            [
                new OrderLeg { Symbol = "LEG-A", Side = OrderSide.Buy, RatioQuantity = 1m, OptionContract = contract },
                new OrderLeg { Symbol = "LEG-B", Side = OrderSide.Sell, RatioQuantity = 1m, OptionContract = contract }
            ]
        };
        decimal? SideAware(string symbol, OrderSide side) => symbol == "LEG-A" ? 10m : 9m;

        OrderNotionalResolver.Resolve(order, Snapshot(), sideAwarePriceLookup: SideAware).Should().Be(1_900m);
        OrderNotionalResolver.ResolveSigned(order, Snapshot(), sideAwarePriceLookup: SideAware).Should().BeNull(
            "a multi-leg order has no single direction");
    }

    [Fact]
    public void Spread_WithAnUnpricedLeg_FailsClosedOnTheWholeOrder()
    {
        var order = Order(OrderSide.Buy, 1m, "SPREAD", OrderType.Market) with
        {
            Legs =
            [
                new OrderLeg { Symbol = "LEG-A", Side = OrderSide.Buy, RatioQuantity = 1m },
                new OrderLeg { Symbol = "LEG-B", Side = OrderSide.Sell, RatioQuantity = 1m }
            ]
        };

        OrderNotionalResolver.Resolve(order, Snapshot(), symbol => symbol == "LEG-A" ? 10m : null).Should().BeNull();
    }

    [Fact]
    public void IncrementalNotional_FromTheAmendmentMetadata_WinsOnlyWhenNonNegative()
    {
        var amended = Order(OrderSide.Sell, 10m, metadata: new Dictionary<string, string>
        {
            [RiskEscalationMetadata.IncrementalNotionalMetadataKey] = "1234"
        });
        var negative = Order(OrderSide.Buy, 10m, metadata: new Dictionary<string, string>
        {
            [RiskEscalationMetadata.IncrementalNotionalMetadataKey] = "-5"
        });

        OrderNotionalResolver.ResolveIncremental(amended, Priced("AAPL", 50m)).Should().Be(1_234m);
        OrderNotionalResolver.ResolveIncrementalSigned(amended, Priced("AAPL", 50m)).Should().Be(-1_234m);
        OrderNotionalResolver.ResolveIncremental(negative, Priced("AAPL", 50m)).Should().Be(500m);
    }

    [Fact]
    public void ResolveSigned_IsPositiveForBuysAndNegativeForSells()
    {
        var snapshot = Priced("AAPL", 50m);

        OrderNotionalResolver.ResolveSigned(Order(OrderSide.Buy, 10m), snapshot).Should().Be(500m);
        OrderNotionalResolver.ResolveSigned(Order(OrderSide.Sell, 10m), snapshot).Should().Be(-500m);
    }
}
