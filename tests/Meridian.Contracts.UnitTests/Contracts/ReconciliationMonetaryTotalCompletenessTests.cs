using FluentAssertions;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.UnitTests.Contracts;

public sealed class ReconciliationMonetaryTotalCompletenessTests
{
    [Fact]
    public void KnownMoneyCannotHideAnotherUnavailableMonetaryDimensionInTheSameBreak()
    {
        ReconciliationBreakMeasureDto known = new(ReconciliationBreakMeasureKindDto.Value, 100m, 102m, 2m, .01m, "USD");
        ReconciliationBreakMeasureDto missing = new(ReconciliationBreakMeasureKindDto.CostBasis, null, null, null, .01m,
            "EUR", "Historical cost evidence unavailable.");
        ReconciliationBreakDto item = new("synthetic", "Synthetic", ReconciliationBreakCategory.ClassificationGap,
            ReconciliationBreakStatus.Open, "source", 100m, 102m, 2m, ReconciliationBreakSeverity.Medium,
            "Missing cost evidence", null, null, Measures: [known, missing]);

        var result = ReconciliationMonetaryTotal.FromBreaks([item]);

        result.Amount.Should().BeNull();
        result.Currency.Should().BeNull();
        result.UnavailableReason.Should().NotBeNullOrWhiteSpace();
    }
}
