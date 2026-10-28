using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Curves;
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

    [Fact]
    public async Task StoredProvider_ReturnsTheLatestVersionAsOf_AndStaysFlatForwardWhenAmbiguous()
    {
        var sofr = ValuationTestSecurities.ForwardDefinition("USD-SOFR-3M", "SOFR");
        var version = ValuationTestSecurities.Version(sofr, new DateOnly(2026, 2, 1), (new DateOnly(2026, 2, 1), 0.04m), (new DateOnly(2027, 2, 1), 0.045m)) with { Version = 2 };
        var definitions = new[]
        {
            sofr,
            ValuationTestSecurities.ForwardDefinition("EUR-EURIBOR-3M", "EURIBOR", "EUR"),
            ValuationTestSecurities.ForwardDefinition("EUR-EURIBOR-6M", "EURIBOR", "EUR")
        };
        var store = Substitute.For<ISecurityCurveStore>();
        // The store matches the index ignoring case (upper(index_name) = upper(@index_name)); the provider trims.
        store.ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => definitions
                .Where(d => d.Kind == call.ArgAt<SecurityCurveKind?>(0))
                .Where(d => string.Equals(d.IndexName, call.ArgAt<string?>(2), StringComparison.OrdinalIgnoreCase))
                .ToList());
        // Only the version dated 2026-02-01 exists: an earlier as-of finds nothing.
        store.GetLatestVersionAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Guid>(0) == sofr.CurveId && call.ArgAt<DateOnly>(1) >= version.AsOf ? version : null);
        var provider = new StoredSecurityForwardCurveProvider(store, NullLogger<StoredSecurityForwardCurveProvider>.Instance);

        var curve = await provider.GetForwardCurveAsync(" sofr ", new DateOnly(2026, 3, 1));
        curve.Should().NotBeNull();
        curve!.AsOfDate.Should().Be(new DateOnly(2026, 2, 1));
        curve.IndexName.Should().Be("SOFR");
        curve.Describe().Should().Be($"forwardCurve:SOFR@2026-02-01 (USD-SOFR-3M v2 {version.ContentSha256[..12]})");

        (await provider.GetForwardCurveAsync("SOFR", new DateOnly(2026, 1, 15))).Should().BeNull("no version is dated on or before the as-of");
        (await provider.GetForwardCurveAsync("EURIBOR", new DateOnly(2026, 3, 1))).Should().BeNull("two stored curves share the index; the leg stays flat forward rather than guessing");
        (await provider.GetForwardCurveAsync("ESTR", new DateOnly(2026, 3, 1))).Should().BeNull("no curve is stored for the index");
        (await provider.GetForwardCurveAsync(" ", new DateOnly(2026, 3, 1))).Should().BeNull();
        await store.Received(1).ListDefinitionsAsync(SecurityCurveKind.Forward, null, "sofr", Arg.Any<CancellationToken>());
        await store.DidNotReceive().GetLatestVersionAsOfAsync(Arg.Is<Guid>(id => id != sofr.CurveId), Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RateSource_CarriesCurveKeyVersionAndHash()
    {
        var (security, effectiveDate) = FloatingSwap(currentIndexRate: 0.03m);
        var sofr = ValuationTestSecurities.ForwardDefinition("USD-SOFR-3M", "SOFR");
        var version = ValuationTestSecurities.Version(sofr, effectiveDate.AddMonths(-1), (effectiveDate, 0.04m), (effectiveDate.AddYears(1), 0.04m)) with { Version = 7 };
        var store = Substitute.For<ISecurityCurveStore>();
        store.ListDefinitionsAsync(SecurityCurveKind.Forward, null, "SOFR", Arg.Any<CancellationToken>()).Returns(new[] { sofr });
        store.GetLatestVersionAsOfAsync(sofr.CurveId, Arg.Any<DateOnly>(), null, Arg.Any<CancellationToken>()).Returns(version);

        var projection = await Project(security, new StoredSecurityForwardCurveProvider(store, NullLogger<StoredSecurityForwardCurveProvider>.Instance));

        projection.BlockedReason.Should().BeNull();
        var floating = projection.LegSchedules!.Single(static leg => leg.LegId == "pay-floating");
        floating.RateSource.Should().Be($"forwardCurve:SOFR@{effectiveDate.AddMonths(-1):yyyy-MM-dd} (USD-SOFR-3M v7 {version.ContentSha256[..12]})");
        floating.Schedule.Select(static payment => payment.InterestAmount).Should().Equal(8_125m, 10_625m, 10_625m, 10_625m);
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
