using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class ValidatedOrderTests
{
    [Theory]
    [InlineData(OrderType.Market, typeof(OrderPricing.MarketStyle))]
    [InlineData(OrderType.MarketOnOpen, typeof(OrderPricing.MarketStyle))]
    [InlineData(OrderType.MarketOnClose, typeof(OrderPricing.MarketStyle))]
    [InlineData(OrderType.Limit, typeof(OrderPricing.LimitStyle))]
    [InlineData(OrderType.LimitOnOpen, typeof(OrderPricing.LimitStyle))]
    [InlineData(OrderType.LimitOnClose, typeof(OrderPricing.LimitStyle))]
    [InlineData(OrderType.StopMarket, typeof(OrderPricing.StopMarket))]
    [InlineData(OrderType.StopLimit, typeof(OrderPricing.StopLimit))]
    [InlineData(OrderType.TrailingStop, typeof(OrderPricing.TrailingAmount))]
    public void ValidOrder_HasItsRequiredPricingVariant(OrderType type, Type variant)
    {
        var request = Request(type) with { LimitPrice = -5m, StopPrice = -4m, TrailPrice = 1m };

        ValidatedOrder.TryCreate(request, out var order, out var errors).Should().BeTrue();

        errors.Should().BeEmpty();
        order!.Pricing.Should().BeOfType(variant);
        order.Request.Should().BeEquivalentTo(request);
    }

    [Fact]
    public void PercentageTrail_CarriesRequiredPercentage()
    {
        var request = Request(OrderType.TrailingStop) with { TrailPercent = 2.5m };

        ValidatedOrder.TryCreate(request, out var order, out _).Should().BeTrue();

        order!.Pricing.Should().BeOfType<OrderPricing.TrailingPercentage>()
            .Which.Percent.Should().Be(2.5m);
    }

    [Theory]
    [InlineData(OrderType.Limit, nameof(OrderRequest.LimitPrice))]
    [InlineData(OrderType.LimitOnOpen, nameof(OrderRequest.LimitPrice))]
    [InlineData(OrderType.LimitOnClose, nameof(OrderRequest.LimitPrice))]
    [InlineData(OrderType.StopMarket, nameof(OrderRequest.StopPrice))]
    [InlineData(OrderType.StopLimit, nameof(OrderRequest.LimitPrice))]
    [InlineData(OrderType.StopLimit, nameof(OrderRequest.StopPrice))]
    public void MissingRequiredPrice_ProducesFieldError(OrderType type, string field)
    {
        var request = Request(type) with { LimitPrice = 12m, StopPrice = 11m };
        request = field == nameof(OrderRequest.LimitPrice)
            ? request with { LimitPrice = null }
            : request with { StopPrice = null };

        ValidatedOrder.TryCreate(request, out var order, out var errors).Should().BeFalse();

        order.Should().BeNull();
        errors.Should().ContainSingle(error => error.Field == field && error.Code == "REQUIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankSymbol_IsRejected(string? symbol)
    {
        ValidatedOrder.TryCreate(Request() with { Symbol = symbol! }, out var order, out var errors)
            .Should().BeFalse();

        order.Should().BeNull();
        errors.Should().ContainSingle(error => error.Field == nameof(OrderRequest.Symbol));
    }

    [Fact]
    public void UndefinedEnums_AreAllReported()
    {
        var request = Request() with
        {
            Type = (OrderType)999,
            Side = (OrderSide)999,
            TimeInForce = (TimeInForce)999
        };

        ValidatedOrder.TryCreate(request, out var order, out var errors).Should().BeFalse();

        order.Should().BeNull();
        errors.Select(error => error.Field).Should().BeEquivalentTo("Type", "Side", "TimeInForce");
        errors.Should().OnlyContain(error => error.Code == "UNDEFINED_ENUM");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(null, 0)]
    [InlineData(null, -1)]
    public void MissingOrNonpositiveTrail_IsRejected(int? amount, int? percent)
    {
        var request = Request(OrderType.TrailingStop) with { TrailPrice = amount, TrailPercent = percent };

        ValidatedOrder.TryCreate(request, out var order, out var errors).Should().BeFalse();

        order.Should().BeNull();
        errors.Should().ContainSingle(error => error.Code == "POSITIVE_TRAIL_REQUIRED");
    }

    [Fact]
    public void CompetingTrailSpecifications_AreRejected()
    {
        var request = Request(OrderType.TrailingStop) with { TrailPrice = 1m, TrailPercent = 2m };

        ValidatedOrder.TryCreate(request, out var order, out var errors).Should().BeFalse();

        order.Should().BeNull();
        errors.Should().ContainSingle(error => error.Code == "CONFLICTING_TRAIL");
    }

    [Fact]
    public void Snapshot_CollectionsCannotChangeAfterValidation()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["notional"] = "100",
            ["Notional"] = "200"
        };
        var leg = new OrderLeg { Symbol = "AAPL", Side = OrderSide.Buy, RatioQuantity = 2m };
        var legs = new List<OrderLeg> { leg };
        var request = Request() with { Metadata = metadata, Legs = legs };

        ValidatedOrder.TryCreate(request, out var order, out _).Should().BeTrue();
        metadata["notional"] = "999";
        legs.Clear();

        order!.Request.Metadata.Should().HaveCount(2);
        order.Request.Metadata!["notional"].Should().Be("100");
        order.Request.Metadata["Notional"].Should().Be("200");
        order.Request.Legs.Should().ContainSingle().Which.Should().Be(leg);
        Action changeMetadata = () => ((IDictionary<string, string>)order.Request.Metadata)["notional"] = "300";
        Action changeLegs = () => ((IList<OrderLeg>)order.Request.Legs!)[0] = leg with { RatioQuantity = 999m };
        changeMetadata.Should().Throw<NotSupportedException>();
        changeLegs.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void MarketOrder_PreservesScaffoldPriceAndExistingQuantityPolicy()
    {
        var request = Request() with { LimitPrice = 125m, Quantity = 0m };

        ValidatedOrder.TryCreate(request, out var order, out _).Should().BeTrue();

        order!.Request.LimitPrice.Should().Be(125m);
        order.Request.Quantity.Should().Be(0m);
    }

    [Fact]
    public void RequiredPrices_RetainNegativeAndZeroInstrumentPrices()
    {
        var request = Request(OrderType.StopLimit) with { LimitPrice = -5m, StopPrice = 0m };

        ValidatedOrder.TryCreate(request, out var order, out _).Should().BeTrue();

        var pricing = order!.Pricing.Should().BeOfType<OrderPricing.StopLimit>().Which;
        pricing.LimitPrice.Should().Be(-5m);
        pricing.StopPrice.Should().Be(0m);
    }

    [Fact]
    public void NullRequest_ThrowsAtTheBoundary()
    {
        Action create = () => ValidatedOrder.TryCreate(null!, out _, out _);

        create.Should().Throw<ArgumentNullException>();
    }

    private static OrderRequest Request(OrderType type = OrderType.Market) => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = type,
        Quantity = 10m
    };
}
