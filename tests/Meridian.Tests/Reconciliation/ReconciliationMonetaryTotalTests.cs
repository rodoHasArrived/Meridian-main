using FluentAssertions;
using Meridian.Contracts.Workstation;
using Xunit;

namespace Meridian.Tests.Reconciliation;

public sealed class ReconciliationMonetaryTotalTests
{
    [Fact]
    public void QuantityBreak_NeverBecomesADollarTotal()
    {
        var total = ReconciliationMonetaryTotal.FromBreaks([Break(new(ReconciliationBreakMeasureKindDto.Quantity, 10, 12, 2, 0, "Shares"))]);
        total.Amount.Should().BeNull();
        total.UnavailableReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SameCurrencyMonetaryBreaks_SumAbsoluteResiduals()
    {
        var total = ReconciliationMonetaryTotal.FromBreaks([
            Break(new(ReconciliationBreakMeasureKindDto.Value, 10, 12, 2, .01m, "USD")),
            Break(new(ReconciliationBreakMeasureKindDto.CostBasis, 20, 17, -3, .01m, "USD"))]);
        total.Amount.Should().Be(5);
        total.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData("EUR", null, 2)]
    [InlineData("USD", "Scope unavailable", 2)]
    [InlineData("USD", null, 99)]
    [InlineData("unknown", null, 2)]
    public void MixedUnknownOrInvalidEvidence_DoesNotProduceMoney(string unit, string? reason, int variance)
    {
        var total = ReconciliationMonetaryTotal.FromBreaks([
            Break(new(ReconciliationBreakMeasureKindDto.Value, 10, 12, 2, .01m, "USD")),
            Break(new(ReconciliationBreakMeasureKindDto.Value, 10, 12, variance, .01m, unit, reason))]);
        total.Amount.Should().BeNull();
        total.Currency.Should().BeNull();
    }

    private static ReconciliationBreakDto Break(ReconciliationBreakMeasureDto measure) => new(
        "check", "Check", ReconciliationBreakCategory.AmountMismatch,
        ReconciliationBreakStatus.Open, "source", measure.Expected, measure.Actual, measure.Variance ?? 0,
        ReconciliationBreakSeverity.Medium, "Synthetic", null, null, Measures: [measure]);
}
