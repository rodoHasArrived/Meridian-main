using FluentAssertions;
using Meridian.Execution.Derivatives;
using Meridian.Execution.Sdk.Derivatives;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for Complex Asset Classes — Derivatives (Phase 5).
/// Validates <see cref="OptionDetails"/>, <see cref="OptionGreeks"/>,
/// <see cref="FutureDetails"/>, <see cref="OptionPosition"/>, and <see cref="FuturePosition"/>.
/// </summary>
public sealed class DerivativePositionTests
{
    // -------------------------------------------------------------------------
    // OptionDetails
    // -------------------------------------------------------------------------

    [Fact]
    public void OptionDetails_IntrinsicValue_Call_PositiveWhenITM()
    {
        var option = new OptionDetails("AAPL", Strike: 150m, Expiry: new DateOnly(2024, 6, 21),
            Right: OptionRight.Call);

        option.IntrinsicValue(underlyingPrice: 170m).Should().Be(20m);
        option.IntrinsicValue(underlyingPrice: 130m).Should().Be(0m);
    }

    [Fact]
    public void OptionDetails_IntrinsicValue_Put_PositiveWhenITM()
    {
        var option = new OptionDetails("AAPL", Strike: 150m, Expiry: new DateOnly(2024, 6, 21),
            Right: OptionRight.Put);

        option.IntrinsicValue(underlyingPrice: 120m).Should().Be(30m);
        option.IntrinsicValue(underlyingPrice: 160m).Should().Be(0m);
    }

    [Fact]
    public void OptionDetails_IsInTheMoney_ReturnsTrueWhenPositiveIntrinsic()
    {
        var call = new OptionDetails("AAPL", 150m, new DateOnly(2024, 6, 21), OptionRight.Call);

        call.IsInTheMoney(160m).Should().BeTrue();
        call.IsInTheMoney(140m).Should().BeFalse();
    }

    [Fact]
    public void OptionDetails_DaysToExpiry_CorrectFromAsOf()
    {
        var expiry = new DateOnly(2024, 12, 20);
        var option = new OptionDetails("AAPL", 150m, expiry, OptionRight.Call);
        var asOf = new DateOnly(2024, 12, 10);

        option.DaysToExpiry(asOf).Should().Be(10);
    }

    // -------------------------------------------------------------------------
    // OptionGreeks
    // -------------------------------------------------------------------------

    [Fact]
    public void OptionGreeks_DollarDelta_ScaledByContractsAndMultiplier()
    {
        var greeks = new OptionGreeks(
            Delta: 0.6, Gamma: 0.02, Theta: -0.05, Vega: 0.15, Rho: 0.01,
            ImpliedVolatility: 0.25, AsOf: DateTimeOffset.UtcNow);

        greeks.DollarDelta(contracts: 2, multiplier: 100).Should().BeApproximately(120.0, 0.0001);
    }

    [Fact]
    public void OptionGreeks_DollarTheta_IsNegativeForLongOptions()
    {
        var greeks = new OptionGreeks(0.5, 0.01, -0.04, 0.10, 0.005, 0.30, DateTimeOffset.UtcNow);

        greeks.DollarTheta(contracts: 1, multiplier: 100).Should().BeLessThan(0);
    }

    // -------------------------------------------------------------------------
    // FutureDetails
    // -------------------------------------------------------------------------

    [Fact]
    public void FutureDetails_PnlForPriceMove_PositiveForLongOnPriceRise()
    {
        var future = new FutureDetails("ES", new DateOnly(2024, 3, 15), TickSize: 0.25m,
            TickValue: 12.50m, ContractMultiplier: 50m, Exchange: "CME");

        // 2 contracts, move from 4800 to 4850 = 50 points * 50 * 2 = $5000
        future.PnlForPriceMove(4800m, 4850m, contracts: 2).Should().Be(5_000m);
    }

    [Fact]
    public void FutureDetails_TicksBetween_ReturnsCorrectCount()
    {
        var future = new FutureDetails("ES", new DateOnly(2024, 3, 15), TickSize: 0.25m,
            TickValue: 12.50m, ContractMultiplier: 50m, Exchange: "CME");

        future.TicksBetween(4800m, 4801m).Should().Be(4m); // 1.0 / 0.25 = 4 ticks
    }

    // -------------------------------------------------------------------------
    // OptionPosition
    // -------------------------------------------------------------------------

    [Fact]
    public void OptionPosition_UnrealizedPnl_ReflectsMarkVsCostBasis()
    {
        var details = new OptionDetails("AAPL", 150m, new DateOnly(2024, 6, 21), OptionRight.Call);
        var pos = new OptionPosition("AAPL240621C00150000", contracts: 5,
            averageCostBasis: 3.00m, markPrice: 5.00m, realizedPnl: 0m, details);

        // (5 - 3) * 5 contracts * 100 multiplier = 1000
        pos.UnrealizedPnl.Should().Be(1_000m);
    }

    [Fact]
    public void OptionPosition_WithMark_ReturnsNewInstanceWithUpdatedMark()
    {
        var details = new OptionDetails("AAPL", 150m, new DateOnly(2024, 6, 21), OptionRight.Call);
        var original = new OptionPosition("AAPL240621C00150000", 5, 3.00m, 5.00m, 0m, details);

        var updated = original.WithMark(newMarkPrice: 7.00m);

        updated.MarkPrice.Should().Be(7.00m);
        original.MarkPrice.Should().Be(5.00m); // original unchanged
    }

    [Fact]
    public void OptionPosition_IsInTheMoney_DelegatesToDetails()
    {
        var details = new OptionDetails("AAPL", 150m, new DateOnly(2024, 6, 21), OptionRight.Call);
        var pos = new OptionPosition("AAPL240621C00150000", 1, 3.00m, 5.00m, 0m, details);

        pos.IsInTheMoney(underlyingPrice: 160m).Should().BeTrue();
        pos.IsInTheMoney(underlyingPrice: 140m).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // FuturePosition
    // -------------------------------------------------------------------------

    [Fact]
    public void FuturePosition_UnrealizedPnl_ReflectsSettlementVsEntry()
    {
        var details = new FutureDetails("ES", new DateOnly(2024, 3, 15), 0.25m, 12.50m, 50m, "CME");
        var pos = new FuturePosition("ESH24", contracts: 2, entryPrice: 4800m,
            lastSettlementPrice: 4820m, realizedPnl: 0m, details);

        // (4820 - 4800) * 2 * 50 = 2000
        pos.UnrealizedPnl.Should().Be(2_000m);
    }

    [Fact]
    public void FuturePosition_SettleDaily_MutatesAndReturnsCashFlow()
    {
        var details = new FutureDetails("ES", new DateOnly(2024, 3, 15), 0.25m, 12.50m, 50m, "CME");
        var pos = new FuturePosition("ESH24", 1, 4800m, 4820m, 0m, details);

        var cashFlow = pos.SettleDaily(newSettlementPrice: 4850m);

        // (4850 - 4820) * 1 * 50 = 1500
        cashFlow.Should().Be(1_500m);
        pos.RealizedPnl.Should().Be(1_500m);
        pos.LastSettlementPrice.Should().Be(4850m);
    }

    [Fact]
    public void FuturePosition_WithSettlement_ReturnsNewInstance()
    {
        var details = new FutureDetails("ES", new DateOnly(2024, 3, 15), 0.25m, 12.50m, 50m, "CME");
        var original = new FuturePosition("ESH24", 1, 4800m, 4820m, 0m, details);

        var updated = original.WithSettlement(4850m);

        updated.LastSettlementPrice.Should().Be(4850m);
        original.LastSettlementPrice.Should().Be(4820m); // original unchanged
    }

    [Fact]
    public void FuturePosition_Kind_IsFuture()
    {
        var details = new FutureDetails("ES", new DateOnly(2024, 3, 15), 0.25m, 12.50m, 50m, "CME");
        var pos = new FuturePosition("ESH24", 1, 4800m, 4800m, 0m, details);

        pos.Kind.Should().Be(DerivativeKind.Future);
    }
}
