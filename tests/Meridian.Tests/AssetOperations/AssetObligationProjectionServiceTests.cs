using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
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
    public void ProjectFromSecurityMaster_PaymentsReflectedByTheFactor_AreNotSubtractedAgain()
    {
        // The 0.8 factor (as of 2024-06-01) already reflects the 20 payment from 2024-03-01, so
        // seeding principal at 80 AND passing that entry to the projector would double-count it
        // (projecting 50 at maturity instead of 70). Only the 10 payment dated after the factor
        // still reduces the balance.
        var security = MakeStructuredCredit(assetTerms: new
        {
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 100m,
            couponOrIndex = "SOFR+250",
            maturity = "2031-06-15",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2024-06-01", factor = 0.8m },
            },
            principalSchedule = new object[]
            {
                new { paymentDate = "2024-03-01", amount = 20m },
                new { paymentDate = "2030-06-15", amount = 10m },
            }
        });

        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security);

        var repayment = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "PrincipalRepayment").Subject;
        repayment.Amount.Should().Be(10m, "only the payment dated after the factor's as-of still applies");
        var maturityFlow = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "Maturity").Subject;
        maturityFlow.Amount.Should().Be(70m,
            "the pre-factor payment is already inside the 0.8 factor and must not be subtracted again");
    }

    [Fact]
    public void ProjectFromSecurityMaster_CompletedPaymentsAfterTheFactor_ReduceOpeningPrincipalInsteadOfProjecting()
    {
        // The 0.8 factor (as of 2026-01-01) predates the 2026-03-01 payment, but that payment has
        // ALREADY OCCURRED relative to the projection as-of (today). Projecting it again would
        // report a completed instalment as newly due and open a MissingEvidence variance against
        // it; instead it reduces the opening principal, and only the future payment projects.
        var security = MakeStructuredCredit(assetTerms: new
        {
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 100m,
            couponOrIndex = "SOFR+250",
            maturity = "2031-06-15",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2026-01-01", factor = 0.8m },
            },
            principalSchedule = new object[]
            {
                new { paymentDate = "2026-03-01", amount = 10m },
                new { paymentDate = "2030-06-15", amount = 10m },
            }
        });

        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security);

        var repayment = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "PrincipalRepayment").Subject;
        repayment.DueDate.Should().Be(new DateOnly(2030, 6, 15),
            "the completed 2026-03-01 instalment must not re-project as newly due");
        repayment.Amount.Should().Be(10m);
        var maturityFlow = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "Maturity").Subject;
        maturityFlow.Amount.Should().Be(60m,
            "opening principal absorbs the completed post-factor payment (80 - 10) before the future 10 amortizes");
    }

    [Fact]
    public void ProjectFromSecurityMaster_NoFactor_CompletedPaymentsReduceOpeningPrincipal()
    {
        // With NO factor at all the balance starts at full face, but completed contractual
        // payments have still occurred: the 2026-03-01 instalment must reduce the opening
        // principal instead of re-projecting as newly due (which would also open a false
        // MissingEvidence variance), and only the future instalment projects.
        var security = MakeStructuredCredit(assetTerms: new
        {
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 100m,
            couponOrIndex = "SOFR+250",
            maturity = "2031-06-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2026-03-01", amount = 20m },
                new { paymentDate = "2030-06-15", amount = 10m },
            }
        });

        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security);

        var repayment = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "PrincipalRepayment").Subject;
        repayment.DueDate.Should().Be(new DateOnly(2030, 6, 15),
            "the completed 2026-03-01 instalment must not re-project as newly due");
        repayment.Amount.Should().Be(10m);
        var maturityFlow = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "Maturity").Subject;
        maturityFlow.Amount.Should().Be(70m,
            "opening principal absorbs the completed 20 (100 - 20) before the future 10 amortizes");
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

    [Fact]
    public void ProjectFromSecurityMaster_CanonicalStructuredCreditRecord_ProjectsScheduledFactorPrincipal()
    {
        // The full stored path, not a hand-built DTO: the payload goes through the C# mapper into
        // the F# domain (which now persists maturity), is re-serialized by the canonical F#
        // serializer, and THAT document drives the projection. Before maturity became a persisted
        // structured-credit term, this path produced no cash flows at all, so the factor schedule
        // had no production effect for first-class records.
        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: "StructuredCredit",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "CLO 2024-1 A-1 canonical",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                tranche = "A-1",
                collateralType = "CLO",
                originalFace = 1_000_000m,
                couponOrIndex = "SOFR+250",
                maturity = "2031-06-15",
                factorScheduleEntries = new[]
                {
                    new { asOfDate = "2024-01-01", factor = 0.5m },
                }
            }),
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "CLO-CANON-1", true, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
            ],
            EffectiveFrom: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SourceSystem: "asset-obligation-tests",
            UpdatedBy: "asset-obligation-tests",
            SourceRecordId: "clo-canonical",
            Reason: "Canonical structured-credit projection coverage");

        var result = SecurityMasterCommandFacade.Create(SecurityMasterMapping.ToCreateCommand(request));
        result.IsSuccess.Should().BeTrue(
            string.Join("; ", result.ErrorDetails.Select(error => $"[{error.Code}] {error.Message}")));
        using var canonicalTerms = JsonDocument.Parse(result.Snapshot!.AssetSpecificTermsJson);

        var security = new SecurityDetailDto(
            request.SecurityId,
            "StructuredCredit",
            SecurityStatusDto.Active,
            "CLO 2024-1 A-1 canonical",
            "USD",
            JsonSerializer.SerializeToElement(new { currency = "USD" }),
            canonicalTerms.RootElement.Clone(),
            [],
            [],
            1,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null);

        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security);

        var maturityFlow = detail.ProjectedCashFlows.Should()
            .ContainSingle(flow => flow.FlowType == "Maturity").Subject;
        maturityFlow.DueDate.Should().Be(new DateOnly(2031, 6, 15),
            "the persisted maturity term must anchor the canonical projection");
        maturityFlow.Amount.Should().Be(500_000m,
            "the scheduled factor must scale principal on the canonical stored path");
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
