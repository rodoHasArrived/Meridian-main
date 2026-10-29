using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.AssetOperations;
using Meridian.Tests.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondCanonicalProjectionTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CanonicalZeroCoupon_ProjectsPrincipalWithoutInventingACouponRate()
    {
        var security = Canonical(new
        {
            issueDate = "2026-01-15", maturity = "2027-01-15", couponType = "ZeroCoupon",
            par = 100m, paymentFrequency = "Annual", isCallable = false, subclass = "Corporate"
        });
        security.AssetSpecificTerms.GetProperty("couponRate").ValueKind.Should().Be(JsonValueKind.Null);
        var resolved = StructuredCashFlowTermsResolver.Resolve(security);
        resolved.CouponKind.Should().Be(SecurityCouponKind.ZeroCoupon);
        resolved.CouponRate.Should().Be(0m);

        var projection = await Service(security).GetProjectionAsOfAsync(security.SecurityId, StructuredCashFlowScenario.Base, AsOf);
        projection!.BlockedReason.Should().BeNull();
        projection.Schedule.Should().ContainSingle().Which.Should().Match<StructuredCashFlowScheduleEntry>(
            row => row.PrincipalAmount == 100m && row.InterestAmount == 0m);
    }

    [Fact]
    public void MissingFixedCoupon_RemainsUnresolved()
    {
        var security = Detail(JsonSerializer.SerializeToElement(new
        {
            issueDate = "2026-01-15", maturity = "2027-01-15", couponType = "Fixed", par = 100m
        }));
        StructuredCashFlowTermsResolver.Resolve(security).CouponRate.Should().BeNull();
    }

    [Fact]
    public void GovernedEmptyFloatingFixings_DoNotResurrectPassThroughObservations()
    {
        var security = Detail(JsonSerializer.SerializeToElement(new
        {
            profileFields = new { couponType = "Floating", floatingIndex = "SOFR", resetSchedule = Array.Empty<object>() },
            resetSchedule = new[] { new { resetDate = "2026-01-15", fixingRate = 0.09m } }
        }));
        StructuredCashFlowTermsResolver.Resolve(security).Legs.Should().ContainSingle().Which.ResetSchedule.Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalFloatingCoupon_ReachesTheRegisteredForwardCurve()
    {
        var security = Canonical(new
        {
            issueDate = "2026-01-15", maturity = "2027-01-15", couponType = "Floating",
            floatingIndex = "SOFR", spreadBps = 100m, capRate = 8m, floorRate = 0m,
            dayCount = "30/360", par = 100m, paymentFrequency = "SemiAnnual",
            isCallable = false, subclass = "FloatingRate"
        });
        var curve = new InMemorySecurityForwardCurveProvider();
        curve.Publish(new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 15),
            [new(new DateOnly(2026, 1, 15), 0.04m), new(new DateOnly(2027, 1, 15), 0.04m)]));
        var resolved = StructuredCashFlowTermsResolver.Resolve(security);
        var leg = resolved.Legs.Should().ContainSingle().Subject;
        leg.LegId.Should().Be("bond-coupon");
        leg.ExchangesPrincipal.Should().BeTrue();
        leg.CapRate.Should().Be(8m);
        leg.FloorRate.Should().Be(0m);

        var projection = await Service(security, curve).GetProjectionAsOfAsync(
            security.SecurityId, StructuredCashFlowScenario.Base, AsOf);
        projection!.BlockedReason.Should().BeNull();
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule.Should().OnlyContain(row => row.InterestAmount == 2.5m);
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(100m);
    }

    [Theory]
    [InlineData("2026-01-31", "2027-01-31", "Quarterly", 0.5, 4, 0.125)]
    [InlineData("2026-01-15", "2029-01-15", "AtMaturity", 5, 1, 15)]
    public async Task DeclaredAssetOperations_UsesCanonicalAmountsAndStableIdentities(
        string issue, string maturity, string frequency, decimal coupon, int count, decimal amount)
    {
        var security = Canonical(new
        {
            issueDate = issue, maturity, couponType = "Fixed", couponRate = coupon,
            dayCount = "30/360", par = 100m, paymentFrequency = frequency,
            isCallable = false, subclass = "Corporate",
            scheduleConventions = new { accrualAdjusted = false, endOfMonthRule = true, couponRateUnit = "Percent" }
        });
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = new AssetOperationsReadService(securityMasterQueryService: query, cashFlows: Service(security));
        var context = new BondCalculationContext(AsOf);
        var first = await service.GetOperationsAsOfAsync(security.SecurityId, context);
        var again = await service.GetOperationsAsOfAsync(security.SecurityId, context);
        var coupons = first!.ProjectedCashFlows.Where(row => row.FlowType == "Coupon").ToArray();
        coupons.Should().HaveCount(count).And.OnlyContain(row => row.Amount == amount);
        first.ProjectedCashFlows.Select(row => row.ProjectedCashFlowId)
            .Should().Equal(again!.ProjectedCashFlows.Select(row => row.ProjectedCashFlowId));
        first.CashFlowProjectionRuns.Select(row => row.ProjectionRunId)
            .Should().Equal(again.CashFlowProjectionRuns.Select(row => row.ProjectionRunId));
        if (frequency == "Quarterly")
        {
            coupons.Select(row => row.DueDate).Should().Equal(
                new DateOnly(2026, 4, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 10, 31), new DateOnly(2027, 1, 31));
        }
    }

    [Fact]
    public async Task DeclaredAssetOperations_WithoutSharedCalculation_DoesNotSilentlyUseLegacyMath()
    {
        var security = Detail(JsonSerializer.SerializeToElement(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-15", maturity = "2027-01-15",
            couponType = "Fixed", couponRate = 0.5m, par = 100m, paymentFrequency = "Quarterly",
            scheduleConventions = new { couponRateUnit = "Percent" }
        }));
        var detail = new AssetObligationProjectionService().ProjectFromSecurityMaster(security, new(AsOf));
        detail.ProjectedCashFlows.Should().BeEmpty();
        detail.CashFlowProjectionRuns.Should().ContainSingle().Which.Status.Should().Be("Blocked");
        detail.Readiness.Warnings.Should().Contain(warning => warning.Contains("shared cash-flow", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveCutoverBlock_PrecedesPublishedAndLegacyAssetOperationsFallbacks()
    {
        var security = Canonical(new
        {
            issueDate = "2026-01-15", maturity = "2027-01-15", couponType = "Fixed", couponRate = 5m,
            par = 100m, dayCount = "30/360", paymentFrequency = "Annual", isCallable = false, subclass = "Corporate"
        });
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var store = Substitute.For<IAssetOperationsProjectionStore>();
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(security, Arg.Any<CancellationToken>()).Returns("Reviewed cutover blocks this legacy bond.");
        var service = new AssetOperationsReadService(store, query, bondPolicy: policy);

        var detail = await service.GetOperationsAsOfAsync(security.SecurityId, new(AsOf));
        detail!.ProjectedCashFlows.Should().BeEmpty();
        detail.CashFlowProjectionRuns.Should().ContainSingle().Which.Status.Should().Be("Blocked");
        detail.Readiness.Warnings.Should().Contain("Reviewed cutover blocks this legacy bond.");
        await store.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static SecurityMasterCashFlowService Service(SecurityDetailDto security, ISecurityForwardCurveProvider? curves = null)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(
            security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        return new(store, [], query, NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: curves);
    }

    private static SecurityDetailDto Canonical(object payload)
    {
        var request = new CreateSecurityRequest(Guid.NewGuid(), "Bond",
            JsonSerializer.SerializeToElement(new { displayName = "Canonical bond", currency = "USD" }),
            JsonSerializer.SerializeToElement(payload),
            [new(SecurityIdentifierKind.InternalCode, "BOND-CANONICAL", true, AsOf)],
            AsOf, "tests", "tests", null, "Canonical projection regression");
        var result = SecurityMasterCommandFacade.Create(SecurityMasterMapping.ToCreateCommand(request));
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => error.Message)));
        return Detail(JsonSerializer.Deserialize<JsonElement>(result.Snapshot.AssetSpecificTermsJson)) with { SecurityId = request.SecurityId };
    }

    private static SecurityDetailDto Detail(JsonElement payload)
        => new(Guid.NewGuid(), "Bond", SecurityStatusDto.Active, "Canonical bond", "USD",
            JsonSerializer.SerializeToElement(new { }), payload, [], [], 1, AsOf, null);
}
