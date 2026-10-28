using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderRiskSizingPolicyTests
{
    [Theory]
    [InlineData(OrderSide.Buy, 20, 100, "1000")]
    [InlineData(OrderSide.Sell, 20, 100, null)]
    [InlineData(OrderSide.Sell, 5, 200, "0")]
    [InlineData(OrderSide.Buy, 5, 100, "0")]
    public void AmendmentProbe_UsesOnlyProvableIncrementWhileRetainingFullQuantity(
        OrderSide side, int newQuantity, int newPrice, string? expectedIncrement)
    {
        var state = State() with { Side = side };
        var probe = OrderRiskSizingPolicy.BuildAmendmentProbe(state,
            new OrderModification { NewQuantity = newQuantity, NewLimitPrice = newPrice });

        Assert.Equal((decimal)newQuantity, probe.Quantity);
        Assert.Equal((decimal)newPrice, probe.LimitPrice);
        Assert.Equal("true", probe.Metadata?[RiskEscalationQueueService.EvaluationOnlyMetadataKey]);
        if (expectedIncrement is null)
            Assert.False(probe.Metadata!.ContainsKey(RiskEscalationQueueService.IncrementalNotionalMetadataKey));
        else
            Assert.Equal(expectedIncrement, probe.Metadata?[RiskEscalationQueueService.IncrementalNotionalMetadataKey]);
        Assert.Equal(10m, state.Quantity);
        Assert.Equal(100m, state.LimitPrice);
    }

    [Fact]
    public void NotionalAmendment_KeepsDollarSizingForPriceChangesButDropsItForUnitQuantity()
    {
        var state = State() with { Quantity = 2500m, RoutedNotional = 2500m };
        var priceChange = new OrderModification { NewLimitPrice = 90m };
        var quantityChange = new OrderModification { NewQuantity = 100m };

        var priceProbe = OrderRiskSizingPolicy.BuildAmendmentProbe(state, priceChange);
        var quantityProbe = OrderRiskSizingPolicy.BuildAmendmentProbe(state, quantityChange);

        Assert.Equal(2500m, BrokerNotionalMetadata.TryRead(priceProbe.Metadata, priceProbe.Quantity));
        Assert.Null(BrokerNotionalMetadata.TryRead(quantityProbe.Metadata, quantityProbe.Quantity));
        Assert.Equal(2500m, OrderRiskSizingPolicy.ResolveAmendedRoutedNotional(state, priceChange));
        Assert.Null(OrderRiskSizingPolicy.ResolveAmendedRoutedNotional(state, quantityChange));
        Assert.True(OrderRiskSizingPolicy.RequiresRiskRevalidation(state, quantityChange));
    }

    [Fact]
    public void DerivativeProbe_RetainsIdentityAndChargesContractSizedIncrement()
    {
        var identity = new OptionContractIdentity { Multiplier = "100", UnderlyingSymbol = "AAPL" };
        var state = State() with { LimitPrice = 5m, ContractMultiplier = 100m, OptionContract = identity };

        var probe = OrderRiskSizingPolicy.BuildAmendmentProbe(state, new OrderModification { NewQuantity = 20m });

        Assert.Same(identity, probe.OptionContract);
        Assert.Equal("5000", probe.Metadata?[RiskEscalationQueueService.IncrementalNotionalMetadataKey]);
        Assert.Equal(100m, OrderRiskSizingPolicy.ResolveContractMultiplier(probe));
        Assert.Equal(10000m, OrderRiskSizingPolicy.ResolveBrokerageOrderNotional(probe));
    }

    [Fact]
    public void FaceValueMeasurement_ScalesParPriceBeforeMultiplication()
    {
        var measured = OrderRiskSizingPolicy.MeasureOrderValue(
            decimal.MaxValue, 100m, null, null, usesFaceValuePercentageOfPar: true);
        var request = new OrderRequest
        {
            Symbol = "TREASURY",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = decimal.MaxValue,
            LimitPrice = 100m
        };
        var riskRequest = OrderRiskSizingPolicy.StampResolvedOrderSizing(request, true);

        Assert.Equal(decimal.MaxValue, measured);
        Assert.Equal(decimal.MaxValue, OrderRiskSizingPolicy.ResolveBrokerageOrderNotional(riskRequest));
        Assert.Null(request.Metadata);
        Assert.True(OrderSizingMetadata.UsesFaceValuePercentageOfPar(riskRequest.Metadata));
    }

    [Fact]
    public void BrokerageNotional_DoesNotTreatNetCombinationPriceAsGrossExposure()
    {
        var request = new OrderRequest
        {
            Symbol = "SPREAD",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 1m,
            Legs =
            [
                new OrderLeg { Symbol = "CALL-1", Side = OrderSide.Buy, RatioQuantity = 1m },
                new OrderLeg { Symbol = "CALL-2", Side = OrderSide.Sell, RatioQuantity = 1m }
            ]
        };

        Assert.Null(OrderRiskSizingPolicy.ResolveBrokerageOrderNotional(request));
    }

    [Fact]
    public void ExplicitPriceStillRequiresRisk_WhenEqualToTheInitialPrice()
    {
        var state = State();
        Assert.True(OrderRiskSizingPolicy.RequiresRiskRevalidation(state,
            new OrderModification { NewLimitPrice = state.LimitPrice }));
        Assert.False(OrderRiskSizingPolicy.RequiresRiskRevalidation(state,
            new OrderModification { NewQuantity = 5m }));
    }

    private static OrderState State() => new()
    {
        OrderId = "order-1",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m,
        Status = OrderStatus.Accepted
    };
}
