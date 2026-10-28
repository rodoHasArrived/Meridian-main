using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Valuation;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The discounted-cash-flow model on the hand-computed golden cases: the bond (PV 1,014,000.00,
/// price 101.4000, market value 507,000.00), the swap (NPV -162.50; 0.00 undiscounted; +7,500 flat
/// forward), every blocker that stops a value being invented, and the schedule evidence a declared
/// bond's projection carries into the inputs.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DiscountedCashFlowValuationModelTests
{
    private static readonly DateOnly AsOfDate = ValuationTestSecurities.AsOfDate;
    private static readonly DateTimeOffset AsOf = ValuationTestSecurities.AsOf;

    [Fact]
    public async Task Bond_DiscountsTwoCouponsAndPrincipal_ToTheGoldenValue()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints);
        var model = Build(bond, Curves(definition, version));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(1_014_000.00m, "50,000 x 0.96 + 1,050,000 x 0.92");
        result.Price.Should().Be(101.4000m);
        result.PriceUnit.Should().Be(SecurityPriceUnit.PercentOfPar);
        result.MarketValue.Should().Be(507_000.00m, "500,000 face x 101.4 / 100");
        result.Observability.Should().Be(SecurityValuationObservability.ObservableInputs);
        result.ObservedAsOf.Should().Be(AsOf);
        result.Model.Should().Be(SecurityValuationModelKind.DiscountedCashFlow);
        result.ModelKey.Should().Be("discounted-cash-flow");
        result.Currency.Should().Be("USD");

        var curveInput = result.Inputs.Single(static input => input.Kind == "DiscountCurveVersion");
        curveInput.Reference.Should().Be("USD-DISCOUNT-TEST");
        curveInput.Id.Should().Be(version.CurveVersionId);
        curveInput.Version.Should().Be(1);
        curveInput.ContentSha256.Should().Be(version.ContentSha256);
        result.Inputs.Single(static input => input.Kind == "SecurityTerms").Version.Should().Be(bond.Version);
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("DirtyValue", StringComparison.Ordinal));

        // An undeclared projection carries no schedule evidence, so the inputs and diagnostics are
        // exactly these, member for member.
        result.Inputs.Should().Equal(
            new SecurityValuationInputDto("DiscountCurveVersion", "USD-DISCOUNT-TEST", version.CurveVersionId, 1, version.ContentSha256, AsOf, version.RecordedAt),
            new SecurityValuationInputDto("SecurityTerms", bond.Version.ToString(), bond.SecurityId, bond.Version));
        result.Diagnostics.Should().Equal(DirtyValueDiagnostic);
    }

    [Fact]
    public async Task ADeclaredBond_NamesItsScheduleEvidenceAsInputs_AndReportsTheCoverageGap()
    {
        // The golden bond declaring Modified Following payment dates on ZZTEST, whose coverage ends
        // 2027-06-30: 2028-01-01 is a Saturday, so the final payment moves to Monday 2028-01-03 and the
        // kernel asks ZZTEST about 2028-01-01 to 2028-01-03, outside its coverage. On a unit curve the
        // PV is the undiscounted 50,000 + 50,000 + 1,000,000.
        var calendar = new HolidayBusinessDayCalendar("ZZTEST", 1, coverage: new CalendarCoverage(null, new DateOnly(2027, 6, 30)));
        var calendars = new StaticBusinessDayCalendarSource([calendar]);
        var bond = DeclaredBond("""{"businessDayConvention":"ModifiedFollowing","accrualAdjusted":false,"paymentCalendars":["ZZTEST"]}""");
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, (AsOfDate, 1.00m), (new DateOnly(2029, 1, 1), 1.00m));
        var model = Build(bond, Curves(definition, version), calendars: calendars);

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(1_100_000.00m, "50,000 + 50,000 + 1,000,000 on unit discount factors");
        result.Price.Should().Be(110m);
        result.MarketValue.Should().Be(550_000.00m, "500,000 face x 110 / 100");
        result.Inputs.Should().Equal(
            new SecurityValuationInputDto("DiscountCurveVersion", "USD-DISCOUNT-TEST", version.CurveVersionId, 1, version.ContentSha256, AsOf, version.RecordedAt),
            new SecurityValuationInputDto("SecurityTerms", "1", bond.SecurityId, 1),
            new SecurityValuationInputDto("ScheduleKernel", "contractual-schedule-kernel-v1"),
            new SecurityValuationInputDto("DeclaredScheduleMapping", "security-master-declared-schedule-v1"),
            new SecurityValuationInputDto("BusinessDayCalendar", "ZZTEST:Payment", ContentSha256: calendar.ContentHash));
        result.Diagnostics.Should().Equal(
            "CalendarCoverageGap: calendar 'ZZTEST' was asked about 2028-01-01 to 2028-01-03, outside its declared coverage; a holiday on those dates may be missing from the calendar data the schedule used.",
            DirtyValueDiagnostic);

        // The same bond declaring nothing projects on the legacy path and names no schedule evidence.
        var undeclared = DeclaredBond(block: null);
        var undeclaredResult = await Build(undeclared, Curves(definition, version), calendars: calendars)
            .ValueAsync(new SecurityValuationRequest(undeclared.SecurityId, AsOf, Quantity: 500_000m), undeclared, CancellationToken.None);

        undeclaredResult.IsBlocked.Should().BeFalse(string.Join("; ", undeclaredResult.Blockers.Select(static b => b.Reason)));
        undeclaredResult.PresentValue.Should().Be(1_100_000.00m);
        undeclaredResult.Inputs.Select(static input => input.Kind).Should().Equal("DiscountCurveVersion", "SecurityTerms");
        undeclaredResult.Diagnostics.Should().Equal(DirtyValueDiagnostic);
    }

    [Fact]
    public async Task APaidDownBond_QuotesItsPricePerOutstandingFace_AndAppliesTheFactorOnce()
    {
        // Factor 0.8 on 1,000,000 par: the walk projects two coupons of 40,000 and 800,000 of
        // principal on the 800,000 outstanding. On a unit discount curve the PV is 880,000, which is
        // 110 per 100 of OUTSTANDING face, not 88 per 100 of par: the market value of 500,000 par is
        // 500,000 x 0.8 x 110 / 100 = 440,000, the factor applied once.
        var bond = ValuationTestSecurities.PaidDownBond(0.8m);
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(bond, Curves(definition, version));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(880_000.00m, "40,000 + 40,000 + 800,000 on unit discount factors");
        result.PriceUnit.Should().Be(SecurityPriceUnit.PercentOfPar);
        result.Price.Should().Be(110m, "880,000 per 800,000 of outstanding face");
        result.MarketValue.Should().Be(440_000.00m, "500,000 par x 0.8 x 110 / 100");
    }

    [Fact]
    public async Task AFullyRepaidBond_HasNoPriceBasis_AndSaysSo()
    {
        var bond = ValuationTestSecurities.PaidDownBond(0m);
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(bond, Curves(definition, version));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(0m);
        result.Price.Should().BeNull();
        result.PriceUnit.Should().Be(SecurityPriceUnit.Unspecified);
        result.MarketValue.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("NoPriceBasis", StringComparison.Ordinal)).Which.Should().Contain("fully repaid");
    }

    [Fact]
    public async Task ADatedFactorScheduleThatDisagreesWithTheScalar_PricesOnTheFactorTheProjectionApplied()
    {
        // The schedule says 0.5 on 2025-12-01 (0.4 only from 2026-06-01); the undated scalar says
        // 0.8. The walk applies 0.5, projecting two coupons of 25,000 and 500,000 of principal, so
        // on a unit curve the PV of 550,000 is 110 per 100 of the 500,000 outstanding (not 68.75
        // per 100 of the scalar's 800,000), and 500,000 par is worth 500,000 x 0.5 x 110 / 100.
        var bond = ValuationTestSecurities.FactorScheduledBond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(bond, Curves(definition, version));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf, Quantity: 500_000m), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(550_000.00m, "25,000 + 25,000 + 500,000 on unit discount factors");
        result.PriceUnit.Should().Be(SecurityPriceUnit.PercentOfPar);
        result.Price.Should().Be(110m, "550,000 per 500,000 of outstanding face");
        result.MarketValue.Should().Be(275_000.00m, "500,000 par x 0.5 x 110 / 100");
    }

    [Theory]
    [InlineData(StructuredCashFlowStaleness.Stale, "CashFlowSourceStale")]
    [InlineData(StructuredCashFlowStaleness.Unknown, "CashFlowSourceFreshnessUnknown")]
    public async Task AStaleOrUndatedCashFlowSource_IsValued_AndSaysSo(StructuredCashFlowStaleness freshness, string diagnostic)
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints);
        var model = Build(bond, Curves(definition, version), freshness: freshness);

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().Be(1_014_000.00m, "staleness is reported, not re-decided");
        result.Diagnostics.Should().ContainSingle(d => d.StartsWith(diagnostic, StringComparison.Ordinal))
            .Which.Should().Contain("SourceLastUpdatedUtc");
    }

    [Fact]
    public async Task AFreshCashFlowSource_CarriesNoFreshnessDiagnostic()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(bond, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints)));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        result.Diagnostics.Should().NotContain(static d => d.StartsWith("CashFlowSource", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMaturedBond_HasNoProjectedFlows_AndBlocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(bond, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints)));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, new DateTimeOffset(2028, 6, 1, 0, 0, 0, TimeSpan.Zero)), bond, CancellationToken.None);

        Blocked(result, "NoProjectedFlows").Reason.Should().Contain("no net cash flows");
    }

    [Fact]
    public async Task Swap_NetsTheLegsAndDiscounts_ToTheGoldenValue()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.SwapDiscountPoints);
        var model = Build(swap, Curves(definition, version), FlatFourPercentSofr());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf, Quantity: 1_000_000m), swap, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(-162.50m, "-8,043.75 + 9,187.50 - 10,306.25 + 9,000.00");
        result.Price.Should().Be(-0.0001625m, "per unit of the 1,000,000 notional");
        result.PriceUnit.Should().Be(SecurityPriceUnit.CurrencyPerUnit);
        result.MarketValue.Should().Be(-162.50m);
        result.Observability.Should().Be(SecurityValuationObservability.ObservableInputs);
        var forwardInput = result.Inputs.Should().ContainSingle(static input => input.Kind == "ForwardCurve").Subject;
        forwardInput.Reference.Should().Be("forwardCurve:SOFR@2025-12-01 (test-curve)");
        forwardInput.AsOf.Should().Be(new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero));
        result.ObservedAsOf.Should().Be(new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero), "the forward curve dated 2025-12-01 is older than the 2026-01-01 discount curve");
        result.Diagnostics.Should().NotContain(static d => d.StartsWith("FloatingForecastFlatAtCurrentIndexRate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AForwardCurveLabelWithoutAReadableAsOf_Blocks()
    {
        // The projection's forward provenance is the only record of the forward curve's as-of; a
        // label that does not carry one leaves the oldest market input unknown, so nothing is valued.
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.SwapDiscountPoints);
        var payment = new StructuredCashFlowScheduleEntry(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), 0m, -8_125m, 1m);
        var cashFlows = Substitute.For<ISecurityMasterCashFlowService>();
        cashFlows.GetProjectionAsOfAsync(swap.SecurityId, StructuredCashFlowScenario.Base, AsOf, Arg.Any<CancellationToken>())
            .Returns(new StructuredCashFlowProjectionDto(
                swap.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base, AsOf, [payment],
                StructuredCashFlowStaleness.Fresh, DateTimeOffset.UtcNow,
                LegSchedules: [new StructuredCashFlowLegSchedule("pay-floating", CashFlowLegRateKind.Floating, CashFlowLegDirection.Pay, [payment], "forwardCurve:SOFR (undated)")]));
        var model = new DiscountedCashFlowValuationModel(cashFlows, Curves(definition, version), NullLogger<DiscountedCashFlowValuationModel>.Instance);

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf), swap, CancellationToken.None);

        Blocked(result, "UnsupportedInput").Reason.Should().Contain("pay-floating").And.Contain("as-of");
    }

    [Fact]
    public async Task Swap_UndiscountedNetsToZero_WithTheForwardCurve()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(swap, Curves(definition, version), FlatFourPercentSofr());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf), swap, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().Be(0.00m, "-8,125 + 9,375 - 10,625 + 9,375");
    }

    [Fact]
    public async Task Swap_WithoutAForwardCurve_ProjectsFlatForward_AndSaysSo()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var version = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints);
        var model = Build(swap, Curves(definition, version), new InMemorySecurityForwardCurveProvider());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf), swap, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().Be(7_500.00m, "fixed 40,000 against floating 4 x 8,125");
        result.Observability.Should().Be(SecurityValuationObservability.UnobservableInputs);
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("FloatingForecastFlatAtCurrentIndexRate", StringComparison.Ordinal))
            .Which.Should().Contain("pay-floating").And.Contain("SOFR");
        result.Inputs.Should().NotContain(static input => input.Kind == "ForwardCurve");
    }

    [Fact]
    public async Task NoDiscountCurve_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var model = Build(bond, Curves());

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "NoDiscountCurve").Reason.Should().Contain("USD");
    }

    [Fact]
    public async Task TwoDiscountCurvesInTheCurrency_Block_NamingBoth()
    {
        var bond = ValuationTestSecurities.Bond();
        var first = ValuationTestSecurities.DiscountDefinition("USD-OIS");
        var second = ValuationTestSecurities.DiscountDefinition("USD-TREASURY");
        var model = Build(bond, Curves(first, null, second, null));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "AmbiguousDiscountCurve").Reason.Should().Contain("USD-OIS").And.Contain("USD-TREASURY");
    }

    [Fact]
    public async Task APinnedCurveInAnotherCurrency_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var eur = ValuationTestSecurities.DiscountDefinition("EUR-ESTR", "EUR");
        var model = Build(bond, Curves(eur, ValuationTestSecurities.Version(eur, AsOfDate, ValuationTestSecurities.BondDiscountPoints)));

        var result = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, AsOf, Curves: [new SecurityValuationCurveInput("Discount", eur.CurveId)]), bond, CancellationToken.None);

        Blocked(result, "CurrencyMismatch").Reason.Should().Contain("EUR").And.Contain("USD");
    }

    [Fact]
    public async Task APinnedVersion_IsUsedExactly()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var pinned = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.UnitDiscountPoints) with { Version = 1 };
        var latest = ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints) with { Version = 2 };
        var curves = Curves(definition, latest);
        curves.GetVersionByIdAsync(definition.CurveId, pinned.CurveVersionId, Arg.Any<CancellationToken>()).Returns(pinned);
        var model = Build(bond, curves);

        var result = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, AsOf, Curves: [new SecurityValuationCurveInput("Discount", definition.CurveId, pinned.CurveVersionId)]), bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
        result.PresentValue.Should().Be(1_100_000.00m, "undiscounted on the pinned unit curve");
        result.Inputs.Single(static input => input.Kind == "DiscountCurveVersion").Id.Should().Be(pinned.CurveVersionId);
        await curves.Received(1).GetVersionByIdAsync(definition.CurveId, pinned.CurveVersionId, Arg.Any<CancellationToken>());
        await curves.DidNotReceive().ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        var unknownVersion = Guid.NewGuid();
        var missing = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, AsOf, Curves: [new SecurityValuationCurveInput("Discount", definition.CurveId, unknownVersion)]), bond, CancellationToken.None);
        Blocked(missing, "NoDiscountCurve").Reason.Should().Contain(unknownVersion.ToString("D"));
    }

    [Fact]
    public async Task APinnedVersionAfterTheValuationDateOrTheKnowledgeCutoff_IsUsedAsGiven_AndSaysSo()
    {
        // A pinned version bypasses the as-of and knowledge filters. One dated a month after the
        // valuation date and recorded forty days after the cutoff still values, with both facts named.
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var curveAsOf = new DateOnly(2026, 2, 1);
        var later = ValuationTestSecurities.Version(definition, curveAsOf, (curveAsOf, 1.00m), (new DateOnly(2028, 1, 1), 1.00m)) with { RecordedAt = AsOf.AddDays(40) };
        var model = Build(bond, Curves(definition, later));

        var result = await model.ValueAsync(
            new SecurityValuationRequest(bond.SecurityId, AsOf, KnownAt: AsOf.AddDays(1), Curves: [new SecurityValuationCurveInput("Discount", definition.CurveId, later.CurveVersionId)]),
            bond, CancellationToken.None);

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Blockers.Select(static b => b.Reason)));
        result.PresentValue.Should().Be(1_100_000.00m, "every factor on the pinned unit curve is 1");
        result.ObservedAsOf.Should().Be(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("DiscountCurveAsOfFollowsValuationDate", StringComparison.Ordinal))
            .Which.Should().Contain("2026-02-01").And.Contain("2026-01-01");
        result.Diagnostics.Should().ContainSingle(static d => d.StartsWith("DiscountCurveRecordedAfterKnownAt", StringComparison.Ordinal))
            .Which.Should().Contain("2026-02-10");
        result.Diagnostics.Should().NotContain(static d => d.StartsWith("DiscountCurveAsOfPrecedesValuationDate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APaymentBeyondTheCurvesLastPillar_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var shortCurve = ValuationTestSecurities.Version(definition, AsOfDate, (AsOfDate, 1.00m), (new DateOnly(2027, 1, 1), 0.96m));
        var model = Build(bond, Curves(definition, shortCurve));

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "DiscountCurveCoverage").Reason.Should().Contain("2028-01-01").And.Contain("2027-01-01");
    }

    [Fact]
    public async Task NoCashFlowSource_Blocks()
    {
        var bond = ValuationTestSecurities.Bond();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(bond, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.BondDiscountPoints)), source: null);

        var result = await model.ValueAsync(new SecurityValuationRequest(bond.SecurityId, AsOf), bond, CancellationToken.None);

        Blocked(result, "NoCashFlowSource");
    }

    [Fact]
    public async Task ABlockedProjection_PassesItsReasonThrough()
    {
        var swap = ValuationTestSecurities.FloatingSwap(currentIndexRate: null);
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(swap, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.SwapDiscountPoints)), new InMemorySecurityForwardCurveProvider());

        var result = await model.ValueAsync(new SecurityValuationRequest(swap.SecurityId, AsOf), swap, CancellationToken.None);

        Blocked(result, "ProjectionBlocked").Reason.Should().Contain("index fixing");
    }

    [Fact]
    public async Task AForwardRoleOnTheRequest_IsRefused()
    {
        var swap = ValuationTestSecurities.FloatingSwap();
        var definition = ValuationTestSecurities.DiscountDefinition();
        var model = Build(swap, Curves(definition, ValuationTestSecurities.Version(definition, AsOfDate, ValuationTestSecurities.SwapDiscountPoints)), FlatFourPercentSofr());

        var result = await model.ValueAsync(
            new SecurityValuationRequest(swap.SecurityId, AsOf, Curves: [new SecurityValuationCurveInput("Forward", Guid.NewGuid())]), swap, CancellationToken.None);

        Blocked(result, "UnsupportedInput").Reason.Should().Contain("Forward");
    }

    private const string DirtyValueDiagnostic =
        "DirtyValue: the present value includes the full next coupon; clean price and accrued interest are not separated.";

    // The golden bond (par 1,000,000, 5% annual on 30/360, 2026-01-01 to 2028-01-01) as a flat Bond row
    // stamped at its scheduleConventions version; a null block declares nothing.
    private static SecurityDetailDto DeclaredBond(string? block)
    {
        var conventions = block is null ? string.Empty : ",\"scheduleConventions\":" + block;
        var terms = JsonDocument.Parse(
            "{\"assetClassTermsVersion\":2,\"issueDate\":\"2026-01-01\",\"maturity\":\"2028-01-01\",\"couponType\":\"Fixed\",\"couponRate\":5," +
            "\"dayCount\":\"30/360\",\"isCallable\":false,\"issuerName\":\"ACME Corp\",\"subclass\":\"Corporate\",\"par\":1000000," +
            "\"paymentFrequency\":\"Annual\"" + conventions + "}").RootElement.Clone();
        return new SecurityDetailDto(
            Guid.NewGuid(),
            "Bond",
            SecurityStatusDto.Active,
            "Declared bond",
            "USD",
            JsonSerializer.SerializeToElement(new { displayName = "Declared bond", currency = "USD" }),
            terms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456783", true, AsOf.AddYears(-1))],
            [],
            1,
            AsOf.AddYears(-1),
            null);
    }

    private static SecurityValuationBlockerDto Blocked(SecurityValuationResult result, string code)
    {
        result.IsBlocked.Should().BeTrue();
        result.Price.Should().BeNull();
        result.PresentValue.Should().BeNull();
        result.MarketValue.Should().BeNull();
        var blocker = result.Blockers.Should().ContainSingle().Subject;
        blocker.Code.Should().Be(code);
        return blocker;
    }

    private static InMemorySecurityForwardCurveProvider FlatFourPercentSofr()
    {
        var provider = new InMemorySecurityForwardCurveProvider();
        provider.Publish(new SecurityForwardCurve("SOFR", AsOfDate.AddMonths(-1),
            [new(AsOfDate, 0.04m), new(AsOfDate.AddYears(1), 0.04m)], Source: "test-curve"));
        return provider;
    }

    /// <summary>A curve store with the given Discount definitions (and their latest versions, when supplied).</summary>
    private static ISecurityCurveStore Curves(params object?[] definitionsAndVersions)
    {
        var store = Substitute.For<ISecurityCurveStore>();
        var definitions = new List<SecurityCurveDefinitionDto>();
        for (var i = 0; i < definitionsAndVersions.Length; i += 2)
        {
            var definition = (SecurityCurveDefinitionDto)definitionsAndVersions[i]!;
            var version = i + 1 < definitionsAndVersions.Length ? (SecurityCurveVersionDto?)definitionsAndVersions[i + 1] : null;
            definitions.Add(definition);
            store.GetDefinitionAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(definition);
            store.GetLatestVersionAsOfAsync(definition.CurveId, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(version);
            store.ListVersionsAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(version is null ? [] : new[] { version });
            if (version is not null)
            {
                store.GetVersionByIdAsync(definition.CurveId, version.CurveVersionId, Arg.Any<CancellationToken>()).Returns(version);
            }
        }

        store.ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => definitions
                .Where(d => call.ArgAt<SecurityCurveKind?>(0) is not { } kind || d.Kind == kind)
                .Where(d => call.ArgAt<string?>(1) is not { } currency || string.Equals(d.Currency, currency, StringComparison.OrdinalIgnoreCase))
                .ToList());
        return store;
    }

    private static DiscountedCashFlowValuationModel Build(
        SecurityDetailDto security,
        ISecurityCurveStore curves,
        ISecurityForwardCurveProvider? forwardCurves = null,
        StructuredCashFlowSourceKind? source = StructuredCashFlowSourceKind.CalculatedBullet,
        StructuredCashFlowStaleness freshness = StructuredCashFlowStaleness.Fresh,
        IBusinessDayCalendarSource? calendars = null)
    {
        // The projection judges freshness on the wall clock: a source updated now is Fresh, one
        // updated a month ago Stale, and one with no update time Unknown.
        DateTimeOffset? lastUpdatedUtc = freshness switch
        {
            StructuredCashFlowStaleness.Fresh => DateTimeOffset.UtcNow,
            StructuredCashFlowStaleness.Stale => DateTimeOffset.UtcNow.AddDays(-30),
            _ => null
        };
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(source is { } kind
                ? new SecurityCashFlowSourceDto(security.SecurityId, kind, lastUpdatedUtc, false, null, null)
                : null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var cashFlows = new SecurityMasterCashFlowService(
            calendars, store, Array.Empty<IStructuredCashFlowProvider>(), query,
            NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: forwardCurves);
        return new DiscountedCashFlowValuationModel(cashFlows, curves, NullLogger<DiscountedCashFlowValuationModel>.Instance);
    }
}
