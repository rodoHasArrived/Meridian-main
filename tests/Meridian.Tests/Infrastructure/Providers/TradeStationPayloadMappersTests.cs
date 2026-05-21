using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.TradeStation;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class TradeStationPayloadMappersTests
{
    [Fact]
    public void MapOrder_MapsKnownEnumsDeterministically()
    {
        var createdAt = new DateTimeOffset(2026, 5, 1, 10, 30, 0, TimeSpan.Zero);
        var payload = new TradeStationOrderPayload(
            OrderId: "order-1",
            ClientOrderId: "client-1",
            Symbol: "msft",
            Side: "sell_short",
            Type: "stop_limit",
            Status: "partially_filled",
            Quantity: 25m,
            FilledQuantity: 5m,
            LimitPrice: 402.10m,
            StopPrice: 401.50m,
            CreatedAt: createdAt);

        var mapped = TradeStationPayloadMappers.MapOrder(payload);

        mapped.Side.Should().Be(OrderSide.Sell);
        mapped.Type.Should().Be(OrderType.StopLimit);
        mapped.Status.Should().Be(OrderStatus.PartiallyFilled);
        mapped.Symbol.Should().Be("MSFT");
    }

    [Fact]
    public void MapStatus_DefaultsUnknownToPendingNew()
    {
        TradeStationPayloadMappers.MapStatus("future_status").Should().Be(OrderStatus.PendingNew);
    }

    [Fact]
    public void MapAccount_RequiresAccountId()
    {
        var act = () => TradeStationPayloadMappers.MapAccount(
            new TradeStationAccountPayload("", "Main", "active", "usd"),
            DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AccountId*");
    }
}
