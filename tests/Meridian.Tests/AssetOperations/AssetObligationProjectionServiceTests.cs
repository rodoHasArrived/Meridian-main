using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

[Trait("Category", "Unit")]
public sealed class AssetObligationProjectionServiceTests
{
    [Fact]
    public void ProjectFromSecurityMaster_StructuredCredit_AppliesScheduledFactorToPrincipal()
    {
        // The record's factor exists ONLY in the typed factorScheduleEntries — no scalar
        // currentFactor. The Asset Operations projection must apply the as-of scheduled factor;
        // before the fix it received no factor at all and projected FULL principal (factor 1).
        var security = MakeStructuredCredit(assetTerms: new
        {
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 1_000_000m,
            couponOrIndex = "SOFR+250",
            maturity = "2031-06-15",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2020-01-01", factor = 0.9m },
                new { asOfDate = "2024-01-01", factor = 0.5m },
            }
        });

        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security);

        var maturityFlow = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "Maturity").Subject;
        maturityFlow.Amount.Should().Be(500_000m,
            "the outstanding principal must be scaled by the scheduled factor in effect (0.5), not projected at full face");
    }

    [Fact]
    public void ProjectFromSecurityMaster_StructuredCredit_ScheduledFactorSupersedesStaleScalar()
    {
        // The scalar currentFactor predates the newest schedule entry; the schedule's as-of factor
        // must win so amortized principal is not projected at the stale level.
        var security = MakeStructuredCredit(assetTerms: new
        {
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 1_000_000m,
            couponOrIndex = "SOFR+250",
            currentFactor = 0.8m,
            maturity = "2031-06-15",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2024-01-01", factor = 0.25m },
            }
        });

        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security);

        var maturityFlow = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "Maturity").Subject;
        maturityFlow.Amount.Should().Be(250_000m,
            "the as-of scheduled factor supersedes the stale scalar currentFactor");
    }

    [Fact]
    public void ProjectFromSecurityMaster_StructuredCredit_ZeroFactorProjectsNoPrincipal()
    {
        // Zero is a real factor — a fully amortized pool. Conflating it with a MISSING factor
        // (which falls back to 1) would project a full-face maturity flow and draftable ledger
        // support for a security with nothing outstanding.
        var security = MakeStructuredCredit(assetTerms: new
        {
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 1_000_000m,
            couponOrIndex = "SOFR+250",
            maturity = "2031-06-15",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2024-01-01", factor = 0m },
            }
        });

        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security);

        detail.ProjectedCashFlows.Should().BeEmpty(
            "a fully amortized pool (factor 0) has no outstanding principal to project");
    }

    private static SecurityDetailDto MakeStructuredCredit(object assetTerms)
        => new(
            Guid.NewGuid(),
            "StructuredCredit",
            SecurityStatusDto.Active,
            "CLO 2024-1 A-1",
            "USD",
            JsonSerializer.SerializeToElement(new { currency = "USD" }),
            JsonSerializer.SerializeToElement(assetTerms),
            [],
            [],
            3,
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);
}
