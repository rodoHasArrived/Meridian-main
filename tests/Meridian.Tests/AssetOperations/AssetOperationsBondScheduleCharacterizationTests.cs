using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// Characterization pins for the asset-operations bond projection loop, recorded against base
/// 7d072638 before the schedule kernel (ADR-025) takes it over. Every pin goes through the public
/// <see cref="AssetOperationsReadService.GetOperationsAsync"/> seam with the bond-reference
/// substitute pattern used by <c>AssetOperationsReadServiceTests</c>.
/// </summary>
/// <remarks>
/// Tests tagged <c>Category=KnownDefect</c> assert TODAY'S defective behavior from the registry in
/// <c>tests/fixtures/schedules/KNOWN-DEFECTS.md</c> and name the TARGET behavior in a comment. When a
/// fix lands the pin fails; the fixing PR flips the assertion and updates the registry in the same
/// change. The bond projection reads no wall clock when the issue date is retained, so these pins use
/// fixed dates.
/// </remarks>
public sealed class AssetOperationsBondScheduleCharacterizationTests
{
    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task MonthEndQuarterlyBond_CurrentBehavior_PeriodEndsDriftAndStubIsSuppressed_SCH_DEF_002()
    {
        // SCH-DEF-002. TARGET: the schedule stays anchored on the month end, so coupons fall due on
        // 2026-04-30, 2026-07-31, 2026-10-31 and 2027-01-31.
        var detail = await GetBondOperationsAsync(
            Guid.Parse("5c4ed002-0000-4000-8000-000000000002"),
            issueDate: new DateOnly(2026, 1, 31),
            maturityDate: new DateOnly(2027, 1, 31),
            paymentFrequency: "Quarterly",
            fixedCouponRate: 4m);

        var coupons = detail.ProjectedCashFlows.Where(static flow => flow.FlowType == "Coupon").ToArray();
        coupons.Select(static flow => flow.DueDate).Should().Equal(
            new[] { new DateOnly(2026, 4, 30), new DateOnly(2026, 7, 30), new DateOnly(2026, 10, 30), new DateOnly(2027, 1, 30) },
            "CURRENT defective behavior: each period end is the previous end plus three months, so the anchor drifts");
        coupons.Should().OnlyContain(static flow => flow.Amount == 1.0000m);
        // The stub [2027-01-30, 2027-01-31] is 0 days under 30/360, and zero coupons are suppressed.
        coupons.Should().NotContain(static flow => flow.AccrualStartDate == new DateOnly(2027, 1, 30));
        detail.ProjectedCashFlows.Single(static flow => flow.FlowType == "Maturity").Should().Match<AssetProjectedCashFlowDto>(
            static flow => flow.DueDate == new DateOnly(2027, 1, 31) && flow.Amount == 100m);
    }

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task AtMaturityBond_CurrentBehavior_ProjectsAnnualCoupons_SCH_DEF_001()
    {
        // SCH-DEF-001 (asset-operations side). TARGET: AtMaturity is one Term period, so the bond
        // projects one coupon of 15.0000 on 2029-01-15.
        var detail = await GetBondOperationsAsync(
            Guid.Parse("5c4ed001-0000-4000-8000-0000000000a5"),
            issueDate: new DateOnly(2026, 1, 15),
            maturityDate: new DateOnly(2029, 1, 15),
            paymentFrequency: "AtMaturity",
            fixedCouponRate: 5m);

        var coupons = detail.ProjectedCashFlows.Where(static flow => flow.FlowType == "Coupon").ToArray();
        coupons.Select(static flow => flow.DueDate).Should().Equal(
            new[] { new DateOnly(2027, 1, 15), new DateOnly(2028, 1, 15), new DateOnly(2029, 1, 15) },
            "CURRENT defective behavior: AtMaturity has no months-per-period and falls through to one payment a year");
        coupons.Should().OnlyContain(static flow => flow.Amount == 5.0000m);
    }

    [Fact]
    public async Task SubOneCoupon_IsReadAsPercent_ContrastWithSecurityMasterEngine()
    {
        // Contrast pin for SCH-DEF-003 (not itself a defect): asset operations reads a 0.5 coupon as
        // half a percent, so a full 30/360 quarter on par 100 pays 0.1250. The Security Master
        // engine reads the same coupon as a fraction and pays 12.5000.
        var detail = await GetBondOperationsAsync(
            Guid.Parse("5c4ed003-0000-4000-8000-0000000000a5"),
            issueDate: new DateOnly(2026, 1, 15),
            maturityDate: new DateOnly(2027, 1, 15),
            paymentFrequency: "Quarterly",
            fixedCouponRate: 0.5m);

        var coupons = detail.ProjectedCashFlows.Where(static flow => flow.FlowType == "Coupon").ToArray();
        coupons.Should().HaveCount(4);
        coupons.Should().OnlyContain(static flow => flow.Amount == 0.1250m);
    }

    private static async Task<AssetOperationsDetailDto> GetBondOperationsAsync(
        Guid securityId,
        DateOnly issueDate,
        DateOnly maturityDate,
        string paymentFrequency,
        decimal fixedCouponRate)
    {
        const string displayName = "Schedule characterization bond";
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(
                securityId,
                "Bond",
                SecurityStatusDto.Active,
                displayName,
                "USD",
                JsonSerializer.SerializeToElement(new { displayName, currency = "USD" }),
                JsonSerializer.SerializeToElement(new { maturity = maturityDate.ToString("yyyy-MM-dd"), couponRate = fixedCouponRate }),
                [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, DateTimeOffset.Parse("2026-01-01T00:00:00Z"))],
                [],
                1,
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                null));
        var bondService = Substitute.For<IBondReferenceService>();
        bondService.GetReferenceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondReferenceDto(
                securityId,
                displayName,
                "USD",
                "Meridian Funding LLC",
                "Senior",
                "CUSIP:123456789",
                new BondLifecycleDto(
                    securityId,
                    BondLifecycleStat.Active,
                    issueDate,
                    null,
                    maturityDate,
                    false,
                    1,
                    Par: 100m,
                    PaymentFrequency: paymentFrequency),
                new BondAccrualConventionDto(securityId, "30/360", 2, "US", "Fixed", fixedCouponRate, null, null, 1),
                1));
        var service = new AssetOperationsReadService(securityMasterQueryService: securityMaster, bondReferenceService: bondService);

        var detail = await service.GetOperationsAsync(securityId);

        detail.Should().NotBeNull();
        return detail!;
    }
}
