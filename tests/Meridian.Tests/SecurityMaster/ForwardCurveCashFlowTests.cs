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

    // D3 (decided 2026-11-05): undeclared non-zero rates block. A Security Master swap leg can declare
    // its rate unit only in a per-leg conventions block, which the engine refuses
    // (LegConventionsNotFollowed), so every projecting swap below is blocked on its undeclared fixed
    // rate until per-leg units are declarable. The amounts each test pinned before D3 are kept in its
    // comment so the coverage can be restored when that write path exists.
    private const string UndeclaredFixedLegReason =
        "Rate legs.fixedRate = 0.04 on Swap has no declared unit (Fraction, Percent or BasisPoints). "
        + "A legacy value is flagged rather than converted; its unit cannot be inferred from its magnitude.";

    [Fact]
    public async Task FloatingLeg_ResetsFutureperiodsFromTheCurve_IsBlockedUntilPerLegRateUnitsAreDeclarable()
    {
        // D3: blocked until per-leg units are declarable. Pinned before D3: the first quarter reset at
        // 3.00% + 25bp, later quarters at the 4.00% curve + 25bp: 8,125, 10,625, 10,625, 10,625.
        var (security, effectiveDate) = FloatingSwap(currentIndexRate: 0.03m);
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new SecurityForwardCurve("SOFR", effectiveDate.AddMonths(-1),
            [new(effectiveDate, 0.04m), new(effectiveDate.AddYears(1), 0.04m)], Source: "test-curve"));

        var projection = await Project(security, curves);

        ShouldBeBlockedOnTheUndeclaredFixedLeg(projection);
    }

    [Fact]
    public async Task FloatingLeg_WithoutACurve_IsBlockedUntilPerLegRateUnitsAreDeclarable()
    {
        // D3: blocked until per-leg units are declarable. Pinned before D3: flat forward at the last
        // fixing ("flatForward:lastFixing"), 8,125 a quarter.
        var (security, _) = FloatingSwap(currentIndexRate: 0.03m);

        var projection = await Project(security, new InMemorySecurityForwardCurveProvider());

        ShouldBeBlockedOnTheUndeclaredFixedLeg(projection);
    }

    [Fact]
    public async Task FloatingLeg_WithACurveButNoFixing_IsBlockedUntilPerLegRateUnitsAreDeclarable()
    {
        // D3: blocked until per-leg units are declarable. Pinned before D3: with the curve, four
        // quarters of 10,625; without it, blocked for the missing index fixing.
        var (security, effectiveDate) = FloatingSwap(currentIndexRate: null);
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new SecurityForwardCurve("SOFR", effectiveDate.AddMonths(-1),
            [new(effectiveDate, 0.04m), new(effectiveDate.AddYears(1), 0.04m)]));

        var withCurve = await Project(security, curves);
        var withoutCurve = await Project(security, new InMemorySecurityForwardCurveProvider());

        ShouldBeBlockedOnTheUndeclaredFixedLeg(withCurve);
        ShouldBeBlockedOnTheUndeclaredFixedLeg(withoutCurve);
    }

    [Fact]
    public async Task FloatingLeg_UsesTheRecordedResetFixing_IsBlockedUntilPerLegRateUnitsAreDeclarable()
    {
        // D3: blocked until per-leg units are declarable. Pinned before D3: the first quarter reset at
        // the recorded 3.50% + 25bp, later quarters flat forward at 3.00% + 25bp: 9,375, 8,125, 8,125, 8,125.
        var (security, _) = FloatingSwap(
            currentIndexRate: 0.03m,
            configureFloating: (leg, effectiveDate) => leg["resetSchedule"] = new object[]
            {
                new { resetDate = effectiveDate, fixingRate = 0.035m }
            });

        var projection = await Project(security, new InMemorySecurityForwardCurveProvider());

        ShouldBeBlockedOnTheUndeclaredFixedLeg(projection);
    }

    [Fact]
    public async Task AmortizingLeg_OnTheScheduledNotional_IsBlockedUntilPerLegRateUnitsAreDeclarable()
    {
        // D3: blocked until per-leg units are declarable. Pinned before D3: the fixed leg accrued 20,000
        // then 10,000 on the stepped-down notional and paid 500,000 of principal twice.
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

        ShouldBeBlockedOnTheUndeclaredFixedLeg(projection);
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
    public async Task RateSource_CarriesCurveKeyVersionAndHash_IsBlockedUntilPerLegRateUnitsAreDeclarable()
    {
        // D3: blocked until per-leg units are declarable. Pinned before D3: the floating leg's rate
        // source named the stored curve key, version and hash, and paid 8,125, 10,625, 10,625, 10,625.
        // The stored curve is still looked up before the leg walk blocks.
        var (security, effectiveDate) = FloatingSwap(currentIndexRate: 0.03m);
        var sofr = ValuationTestSecurities.ForwardDefinition("USD-SOFR-3M", "SOFR");
        var version = ValuationTestSecurities.Version(sofr, effectiveDate.AddMonths(-1), (effectiveDate, 0.04m), (effectiveDate.AddYears(1), 0.04m)) with { Version = 7 };
        var store = Substitute.For<ISecurityCurveStore>();
        store.ListDefinitionsAsync(SecurityCurveKind.Forward, null, "SOFR", Arg.Any<CancellationToken>()).Returns(new[] { sofr });
        store.GetLatestVersionAsOfAsync(sofr.CurveId, Arg.Any<DateOnly>(), null, Arg.Any<CancellationToken>()).Returns(version);

        var projection = await Project(security, new StoredSecurityForwardCurveProvider(store, NullLogger<StoredSecurityForwardCurveProvider>.Instance));

        ShouldBeBlockedOnTheUndeclaredFixedLeg(projection);
        await store.Received(1).GetLatestVersionAsOfAsync(sofr.CurveId, Arg.Any<DateOnly>(), null, Arg.Any<CancellationToken>());
    }

    private static void ShouldBeBlockedOnTheUndeclaredFixedLeg(StructuredCashFlowProjectionDto projection)
    {
        projection.BlockedReason.Should().Be(UndeclaredFixedLegReason);
        projection.Schedule.Should().BeEmpty();
        projection.LegSchedules.Should().BeNull();
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
