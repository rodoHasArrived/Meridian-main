using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Floating legs reset from a forward curve when one is supplied, keep the retained fixing for the
/// period already reset, and fall back to flat forward without a curve.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ForwardCurveCashFlowTests
{
    [Fact]
    public void RateAt_InterpolatesLinearly_AndHoldsFlatBeyondTheEnds()
    {
        var curve = new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1),
        [
            new SecurityForwardCurvePoint(new DateOnly(2026, 7, 1), 0.05m),
            new SecurityForwardCurvePoint(new DateOnly(2026, 1, 1), 0.03m),
        ]);

        curve.RateAt(new DateOnly(2025, 6, 1)).Should().Be(0.03m);
        curve.RateAt(new DateOnly(2027, 6, 1)).Should().Be(0.05m);
        var midpoint = DateOnly.FromDayNumber((new DateOnly(2026, 1, 1).DayNumber + new DateOnly(2026, 7, 1).DayNumber) / 2);
        curve.RateAt(midpoint).Should().BeApproximately(0.04m, 0.0002m);
        new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1), []).RateAt(midpoint).Should().BeNull();
    }

    [Fact]
    public async Task InMemoryProvider_ReturnsTheLatestCurveNotAfterTheAsOfDate()
    {
        var provider = new InMemorySecurityForwardCurveProvider();
        provider.Publish(new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1), [new(new DateOnly(2026, 1, 1), 0.03m)]));
        provider.Publish(new SecurityForwardCurve("sofr", new DateOnly(2026, 2, 1), [new(new DateOnly(2026, 2, 1), 0.04m)]));

        (await provider.GetForwardCurveAsync("SOFR", new DateOnly(2026, 1, 15)))!.AsOfDate.Should().Be(new DateOnly(2026, 1, 1));
        (await provider.GetForwardCurveAsync("Sofr", new DateOnly(2026, 3, 1)))!.AsOfDate.Should().Be(new DateOnly(2026, 2, 1));
        (await provider.GetForwardCurveAsync("SOFR", new DateOnly(2025, 12, 31))).Should().BeNull();
        (await provider.GetForwardCurveAsync("EURIBOR", new DateOnly(2026, 3, 1))).Should().BeNull();
    }

    [Fact]
    public async Task FloatingLeg_ResetsFutureperiodsFromTheCurve_AndKeepsTheCurrentFixing()
    {
        var (security, effectiveDate) = FloatingSwap(currentIndexRate: 0.03m);
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new SecurityForwardCurve("SOFR", effectiveDate.AddMonths(-1),
            [new(effectiveDate, 0.04m), new(effectiveDate.AddYears(1), 0.04m)], Source: "test-curve"));

        var projection = await Project(security, curves);

        projection.BlockedReason.Should().BeNull();
        var floating = projection.LegSchedules!.Single(static leg => leg.LegId == "pay-floating");
        floating.RateSource.Should().Be($"forwardCurve:SOFR@{effectiveDate.AddMonths(-1):yyyy-MM-dd} (test-curve)");
        // First quarter already reset at 3.00% + 25bp; later quarters reset at the 4.00% curve + 25bp.
        floating.Schedule.Select(static payment => payment.InterestAmount).Should().Equal(8_125m, 10_625m, 10_625m, 10_625m);
        projection.LegSchedules!.Single(static leg => leg.LegId == "receive-fixed").RateSource.Should().Be("fixed");
    }

    [Fact]
    public async Task FloatingLeg_WithoutACurve_StaysFlatForward()
    {
        var (security, _) = FloatingSwap(currentIndexRate: 0.03m);

        var projection = await Project(security, new InMemorySecurityForwardCurveProvider());

        var floating = projection.LegSchedules!.Single(static leg => leg.LegId == "pay-floating");
        floating.RateSource.Should().Be("flatForward:lastFixing");
        floating.Schedule.Should().OnlyContain(static payment => payment.InterestAmount == 8_125m);
    }

    [Fact]
    public async Task FloatingLeg_WithACurveButNoFixing_ProjectsInsteadOfBlocking()
    {
        var (security, effectiveDate) = FloatingSwap(currentIndexRate: null);
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new SecurityForwardCurve("SOFR", effectiveDate.AddMonths(-1),
            [new(effectiveDate, 0.04m), new(effectiveDate.AddYears(1), 0.04m)]));

        var withCurve = await Project(security, curves);
        var withoutCurve = await Project(security, new InMemorySecurityForwardCurveProvider());

        withCurve.BlockedReason.Should().BeNull();
        withCurve.LegSchedules!.Single(static leg => leg.LegId == "pay-floating").Schedule
            .Should().HaveCount(4).And.OnlyContain(static payment => payment.InterestAmount == 10_625m);
        withoutCurve.BlockedReason.Should().Contain("index fixing");
    }

    [Fact]
    public async Task FloatingLeg_UsesTheRecordedResetFixing_ForThePeriodAlreadyReset()
    {
        var (security, _) = FloatingSwap(
            currentIndexRate: 0.03m,
            configureFloating: (leg, effectiveDate) => leg["resetSchedule"] = new object[]
            {
                new { resetDate = effectiveDate, fixingRate = 0.035m }
            });

        var projection = await Project(security, new InMemorySecurityForwardCurveProvider());

        var floating = projection.LegSchedules!.Single(static leg => leg.LegId == "pay-floating");
        // The first quarter reset at the recorded 3.50% + 25bp; later quarters stay flat forward at 3.00% + 25bp.
        floating.Schedule.Select(static payment => payment.InterestAmount).Should().Equal(9_375m, 8_125m, 8_125m, 8_125m);
    }

    [Fact]
    public async Task AmortizingLeg_AccruesOnTheScheduledNotional_AndPaysTheStepDownAsPrincipal()
    {
        var (security, _) = FloatingSwap(
            currentIndexRate: 0.03m,
            configureFixed: (leg, effectiveDate) =>
            {
                leg["exchangesPrincipal"] = true;
                leg["notionalSchedule"] = new object[]
                {
                    new { effectiveDate = effectiveDate.AddMonths(6), notional = 500_000m }
                };
            });

        var projection = await Project(security, new InMemorySecurityForwardCurveProvider());

        var fixedLeg = projection.LegSchedules!.Single(static leg => leg.LegId == "receive-fixed");
        fixedLeg.Schedule.Select(static payment => payment.InterestAmount).Should().Equal(20_000m, 10_000m);
        fixedLeg.Schedule.Select(static payment => payment.PrincipalAmount).Should().Equal(500_000m, 500_000m);
    }

    private static async Task<StructuredCashFlowProjectionDto> Project(SecurityDetailDto security, ISecurityForwardCurveProvider curves)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(
                security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = new SecurityMasterCashFlowService(
            store, Array.Empty<IStructuredCashFlowProvider>(), query,
            NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: curves);

        var projection = await service.GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base);
        projection.Should().NotBeNull();
        return projection!;
    }

    private static (SecurityDetailDto Security, DateOnly EffectiveDate) FloatingSwap(
        decimal? currentIndexRate,
        Action<Dictionary<string, object?>, DateOnly>? configureFloating = null,
        Action<Dictionary<string, object?>, DateOnly>? configureFixed = null)
    {
        // First-of-month dates keep all four quarterly payments ahead of today and give exact
        // 30/360 quarter fractions.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var effectiveDate = new DateOnly(today.Year, today.Month, 1);
        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var floatingLeg = new Dictionary<string, object?>
        {
            ["legId"] = "pay-floating",
            ["legType"] = "Floating",
            ["currency"] = "USD",
            ["direction"] = "Pay",
            ["index"] = "SOFR",
            ["spreadBps"] = 25m,
            ["notional"] = 1_000_000m,
            ["paymentFrequency"] = "Quarterly",
            ["dayCount"] = "30/360",
            ["exchangesPrincipal"] = false
        };
        if (currentIndexRate is not null)
        {
            floatingLeg["currentIndexRate"] = currentIndexRate;
        }

        configureFloating?.Invoke(floatingLeg, effectiveDate);
        var fixedLeg = new Dictionary<string, object?>
        {
            ["legId"] = "receive-fixed",
            ["legType"] = "Fixed",
            ["currency"] = "USD",
            ["direction"] = "Receive",
            ["fixedRate"] = 0.04m,
            ["notional"] = 1_000_000m,
            ["paymentFrequency"] = "SemiAnnual",
            ["dayCount"] = "30/360",
            ["exchangesPrincipal"] = false
        };
        configureFixed?.Invoke(fixedLeg, effectiveDate);

        var projection = new SecurityProjectionRecord(
            SecurityId: Guid.NewGuid(),
            AssetClass: "Swap",
            Status: SecurityStatusDto.Active,
            DisplayName: "Forward curve swap",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "SWAP-FWD-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Forward curve swap", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                effectiveDate,
                maturityDate = effectiveDate.AddYears(1),
                legs = new object[] { fixedLeg, floatingLeg }
            }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "forward-curve-tests", asOf, updatedBy = "forward-curve-tests" }),
            Version: 1,
            EffectiveFrom: asOf,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SWAP-FWD-1", true, asOf)],
            Aliases: []);

        var persisted = SecurityMasterMapping.ToProjection(
            new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)));
        return (SecurityMasterMapping.ToDetail(persisted), effectiveDate);
    }
}
