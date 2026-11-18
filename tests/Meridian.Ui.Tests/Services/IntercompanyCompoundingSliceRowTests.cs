using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

/// <summary>The desktop slice row shows the server's compounded interest exactly, and "Simple" for v1 slices.</summary>
public sealed class IntercompanyCompoundingSliceRowTests
{
    [Fact]
    public void CompoundedInterestIsShownExactly_AndSimpleSlicesSaySimple()
    {
        var simple = new IntercompanyAccrualSliceDto(1, new(2026, 7, 1), new(2026, 8, 1), Guid.NewGuid(), 1, "hash", 2, 1_000_000m, 0.05m,
            "ACT/360", 0.0861111111m, 4305.56m, "principal");

        Assert.Equal("Simple", new IntercompanyInterestAccrualSliceRow(simple).CompoundedInterest);
        Assert.Equal("4166.6700000000000000000000001",
            new IntercompanyInterestAccrualSliceRow(simple with { CompoundedInterest = 4166.6700000000000000000000001m }).CompoundedInterest);
    }
}
