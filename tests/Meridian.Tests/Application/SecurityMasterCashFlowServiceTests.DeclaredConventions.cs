using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

/// <summary>
/// The declared mode of the calculated projection (ADR-025, increment 5a): a record whose declared
/// <c>scheduleConventions</c> map to a kernel spec projects the kernel's periods with period-aware day
/// counts and carries schedule evidence; a refusal blocks the projection with its reason; and every
/// record that declares nothing projects exactly as before.
/// </summary>
/// <remarks>
/// The SCH-DEF twins mirror the characterization pins on <c>T0</c>, the first day of next month (UTC),
/// and reach each defect's TARGET only because the record declares conventions; the pins themselves
/// stay on the legacy path. Cases whose dates are business-day adjusted or lagged anchor on fixed 2041
/// dates, so their weekdays never move.
/// </remarks>
public sealed partial class SecurityMasterCashFlowServiceTests
{
    private const string MinimalBlock = """{"accrualAdjusted":false}""";

    private const string LegsNotFollowedReason =
        "Declared schedule conventions are not yet followed on leg-based projections; the projection is blocked until per-leg declared schedules are supported.";

    private const string DeclaredDayCountMissingReason =
        "Declared schedule conventions need a recognized dayCount to compute interest; the record carries none.";

    // A block that would be refused (USNY is not loaded) if it were ever decoded, so a twin that read it
    // could not project as its clean twin does.
    private const string PopulatedBlock =
        """{"businessDayConvention":"ModifiedFollowing","accrualAdjusted":true,"paymentCalendars":["USNY"],"couponRateUnit":"Percent","endOfMonthRule":true}""";

    private static readonly IBusinessDayCalendar WeekendsCalendar = BusinessDayCalendars.WeekendsOnly;

    private static readonly IBusinessDayCalendarSource WeekendsSource = new StaticBusinessDayCalendarSource([WeekendsCalendar]);

    // ── 1. SCH-DEF-001 twin ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_AtMaturity_ProjectsOneTermPeriod_SCH_DEF_001_Target()
    {
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, t0, t0.AddMonths(36), "AtMaturity", par: 1000m),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Should().Equal(Row(t0.AddMonths(36), principal: 1000m, interest: 150.0000m, factor: 0m));
        projection.ScheduleEvidence.Should().NotBeNull();
        projection.ScheduleEvidence!.KernelVersion.Should().Be("contractual-schedule-kernel-v1");
        projection.ScheduleEvidence.DeclaredMappingVersion.Should().Be("security-master-declared-schedule-v1");
        projection.ScheduleEvidence.Calendars.Should().BeEmpty("an unadjusted 30/360 schedule uses no calendar");
        projection.ScheduleEvidence.CoverageGaps.Should().BeEmpty();
    }

    // ── 2. SCH-DEF-003 twin ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"accrualAdjusted":false,"couponRateUnit":"Percent"}""", 0.5, 0.1250)]
    [InlineData("""{"accrualAdjusted":false,"couponRateUnit":"Fraction"}""", 0.05, 1.2500)]
    [InlineData(MinimalBlock, 0.5, 12.5000)]
    public async Task DeclaredConventions_CouponRateUnit_ReplacesTheLegacyHeuristic_SCH_DEF_003_Target(
        string block, double coupon, double expectedInterest)
    {
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(block, t0, t0.AddMonths(12), "Quarterly", coupon: (decimal)coupon),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(Enumerable.Repeat((decimal)expectedInterest, 4));
    }

    [Fact]
    public async Task DeclaredConventions_ACaseVariantCouponRateUnit_IsBlockedWithTheSpellingText()
    {
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectDeclaredAsync(
            DeclaredBond("""{"accrualAdjusted":false,"couponRateUnit":"percent"}""", t0, t0.AddMonths(12), "Quarterly", coupon: 0.5m),
            WeekendsSource);

        ShouldBeBlocked(projection, "Declared couponRateUnit 'percent' is not a recognized value.");
    }

    // ── 3. SCH-DEF-004 twins ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_ActActIcma_DividesByTheCouponPeriod_SCH_DEF_004_Target()
    {
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, t0, t0.AddMonths(12), "SemiAnnual", dayCount: "ACT/ACT ICMA"),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule[0].InterestAmount.Should().Be(2.5000m);
    }

    [Fact]
    public async Task DeclaredConventions_Business252_CountsBusinessDaysOnTheDeclaredCalendar_SCH_DEF_004_Target()
    {
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectDeclaredAsync(
            DeclaredBond("""{"paymentCalendars":["WEEKENDS"]}""", t0, t0.AddMonths(12), "SemiAnnual", dayCount: "BUS/252"),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule[0].InterestAmount.Should().Be(RoundCashLikeService(
            100m * 0.05m * DayCountConventions.Business252Days(t0, t0.AddMonths(6), WeekendsCalendar.IsBusinessDay) / 252m));
        projection.ScheduleEvidence!.Calendars.Should().Equal(
            new StructuredCashFlowCalendarEvidence(BusinessDayCalendars.WeekendsOnlyId, WeekendsCalendar.ContentHash, "DayCount"));
        projection.ScheduleEvidence.CoverageGaps.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclaredConventions_Business252_ReportsTheDatesOutsideTheCalendarsCoverage()
    {
        var t0 = ScheduleCharacterizationT0();
        var partial = new HolidayBusinessDayCalendar("BRBD", 1, coverage: new CalendarCoverage(null, t0.AddMonths(3)));
        var projection = await ProjectDeclaredAsync(
            DeclaredBond("""{"paymentCalendars":["BRBD"]}""", t0, t0.AddMonths(12), "SemiAnnual", dayCount: "BUS/252"),
            new StaticBusinessDayCalendarSource([partial]));

        projection.BlockedReason.Should().BeNull("a coverage gap is informational");
        projection.Schedule[0].InterestAmount.Should().Be(RoundCashLikeService(
            100m * 0.05m * DayCountConventions.Business252Days(t0, t0.AddMonths(6), partial.IsBusinessDay) / 252m));
        projection.ScheduleEvidence!.Calendars.Should().Equal(
            new StructuredCashFlowCalendarEvidence("BRBD", partial.ContentHash, "DayCount"));
        projection.ScheduleEvidence.CoverageGaps.Should().Equal(
            new StructuredCashFlowCoverageGap("BRBD", t0.AddMonths(3).AddDays(1), t0.AddMonths(12).AddDays(-1)));
    }

    [Fact]
    public async Task DeclaredConventions_Business252WithoutCalendars_IsBlockedWithTheDayCountReason()
    {
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, t0, t0.AddMonths(12), "SemiAnnual", dayCount: "BUS/252"),
            WeekendsSource);

        ShouldBeBlocked(projection, "Declared schedule interest could not be computed: Business/252 requires a business-day calendar.");
    }

    // ── 4. SCH-DEF-010 twin ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_FarDatedMaturity_IsBlockedByThePeriodBound_SCH_DEF_010_Target()
    {
        var t0 = ScheduleCharacterizationT0();
        var terms = DeclaredBond(MinimalBlock, t0, new DateOnly(9999, 12, 31), "Quarterly");
        var expected = KernelFailure(BuildSecurity(Guid.NewGuid(), "Bond", terms));
        expected.Code.Should().Be(ScheduleFailureCode.PeriodLimitExceeded);

        var projection = await ProjectDeclaredAsync(terms, WeekendsSource);

        ShouldBeBlocked(projection, $"Declared schedule could not be generated: {expected.Message}");
    }

    // ── 5. Adjusted dates ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_ModifiedFollowingPaymentDates_AccrueOnUnadjustedIcmaPeriods()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(
                """{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["WEEKENDS"],"endOfMonthRule":true}""",
                D(2041, 5, 31), D(2042, 5, 31), "Quarterly", dayCount: "ACT/ACT ICMA", coupon: 4m),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => ScheduleRowDate(row)).Should().Equal(
            D(2041, 8, 30), D(2041, 11, 29), D(2042, 2, 28), D(2042, 5, 30));
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(1.0000m, 1.0000m, 1.0000m, 1.0000m);
        projection.Schedule.Select(static row => row.PrincipalAmount).Should().Equal(0m, 0m, 0m, 100m);
        projection.ScheduleEvidence!.Calendars.Should().Equal(
            new StructuredCashFlowCalendarEvidence(BusinessDayCalendars.WeekendsOnlyId, WeekendsCalendar.ContentHash, nameof(ScheduleCalendarPurpose.Payment)));
    }

    [Fact]
    public async Task DeclaredConventions_AdjustedAccrual_RefusesActActIcma()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(
                """{"businessDayConvention":"MF","accrualAdjusted":true,"paymentCalendars":["WEEKENDS"],"endOfMonthRule":true}""",
                D(2041, 5, 31), D(2042, 5, 31), "Quarterly", dayCount: "ACT/ACT ICMA", coupon: 4m),
            WeekendsSource);

        ShouldBeBlocked(
            projection,
            "Declared schedule interest could not be computed: Actual/Actual (ICMA) is defined on unadjusted coupon periods; this schedule adjusts its accrual dates.");
    }

    // ── 6. Payment lag ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_PaymentLag_CountsBusinessDaysFromTheAccrualEnd()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(
                """{"paymentCalendars":["WEEKENDS"],"paymentLagBusinessDays":2,"endOfMonthRule":true}""",
                D(2041, 5, 31), D(2042, 5, 31), "Quarterly", dayCount: "ACT/ACT ICMA", coupon: 4m),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => ScheduleRowDate(row)).Should().Equal(
            D(2041, 9, 3), D(2041, 12, 3), D(2042, 3, 4), D(2042, 6, 3));
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(1.0000m, 1.0000m, 1.0000m, 1.0000m);
    }

    // ── 7. Blocked ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_AnUnloadedCalendar_BlocksTheProjection()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond("""{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["USNY"]}""", D(2041, 1, 31), D(2042, 1, 31), "Quarterly"),
            WeekendsSource);

        ShouldBeBlocked(
            projection,
            "Declared paymentCalendars name calendars this node has not loaded: 'USNY'; a missing calendar is never replaced by weekends only.");
        projection.TermsUsed.Should().NotBeNull();
        projection.LegSchedules.Should().BeNull();
    }

    [Fact]
    public async Task DeclaredConventions_NoCalendarSource_BlocksTheProjection()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond("""{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["WEEKENDS"]}""", D(2041, 1, 31), D(2042, 1, 31), "Quarterly"),
            calendars: null);

        ShouldBeBlocked(
            projection,
            "Declared paymentCalendars cannot be resolved: no business-day calendar source is registered on this node.");
    }

    [Fact]
    public async Task DeclaredConventions_AnAnchorOffTheDeclaredRoll_IsBlockedByTheKernel()
    {
        var terms = DeclaredBond("""{"rollConvention":"EOM","accrualAdjusted":false}""", D(2041, 5, 30), D(2042, 5, 31), "Quarterly");
        var expected = KernelFailure(BuildSecurity(Guid.NewGuid(), "Bond", terms));
        expected.Code.Should().Be(ScheduleFailureCode.AnchorNotOnRollDay);

        var projection = await ProjectDeclaredAsync(terms, WeekendsSource);

        ShouldBeBlocked(projection, $"Declared schedule could not be generated: {expected.Message}");
    }

    // ── 8. Undeclared paths are untouched ───────────────────────────────────────────────────────

    public static TheoryData<string, string, string> UndeclaredTwins()
    {
        var bond = DeclaredBondObject(block: null, D(2041, 1, 31), D(2043, 1, 31), "SemiAnnual", stamp: null);
        var structuredCredit = StructuredCreditEnvelope(withBlocks: false);
        var swap = StampOneSwap(withLegBlocks: false);
        return new TheoryData<string, string, string>
        {
            { "Bond", Text(WithMembers(bond, ("assetClassTermsVersion", JsonValue.Create(1)), ("scheduleConventions", JsonNode.Parse(PopulatedBlock)))), Text(WithMembers(bond, ("assetClassTermsVersion", JsonValue.Create(1)))) },
            { "Bond", Text(WithMembers(bond, ("scheduleConventions", JsonNode.Parse(PopulatedBlock)))), Text(bond) },
            { "StructuredCredit", Text(StructuredCreditEnvelope(withBlocks: true)), Text(structuredCredit) },
            { "Swap", Text(StampOneSwap(withLegBlocks: true)), Text(swap) },
        };
    }

    [Theory]
    [MemberData(nameof(UndeclaredTwins))]
    public async Task DeclaredConventions_ARecordThatDeclaresNothing_ProjectsExactlyAsItsNoBlockTwin(
        string assetClass, string withBlock, string withoutBlock)
    {
        var twin = await ProjectDeclaredAsync(Json(withBlock), WeekendsSource, assetClass: assetClass);
        var clean = await ProjectDeclaredAsync(Json(withoutBlock), WeekendsSource, assetClass: assetClass);

        clean.Schedule.Should().NotBeEmpty("the clean twin projects rows to compare");
        twin.Schedule.Should().Equal(clean.Schedule);
        if (clean.LegSchedules is null)
        {
            twin.LegSchedules.Should().BeNull();
        }
        else
        {
            twin.LegSchedules.Should().BeEquivalentTo(clean.LegSchedules, static options => options.WithStrictOrdering());
        }

        twin.BlockedReason.Should().Be(clean.BlockedReason);
        twin.ScheduleEvidence.Should().BeNull();
        clean.ScheduleEvidence.Should().BeNull();
    }

    // ── 9. Other classes and shapes ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("CertificateOfDeposit",
        """{"assetClassTermsVersion":2,"issuerName":"First Bank","maturity":"2042-01-31","couponRate":4.0,"dayCount":"ACT/365","scheduleConventions":{"accrualAdjusted":false}}""")]
    [InlineData("StructuredCredit",
        """{"termsFormat":"flat","assetClassTermsVersion":3,"tranche":"A-1","collateralType":"CLO","originalFace":1000000,"couponOrIndex":"5.25","maturity":"2043-01-15","issueDate":"2041-01-15","paymentFrequency":"Quarterly","scheduleConventions":{"accrualAdjusted":false}}""")]
    public async Task DeclaredConventions_AClassWhoseSchemaDeclaresNoIssueDate_IsBlockedWithTheSchemaFact(string assetClass, string terms)
    {
        var projection = await ProjectDeclaredAsync(Json(terms), WeekendsSource, assetClass: assetClass);

        ShouldBeBlocked(
            projection,
            $"Declared schedule conventions need the contractual issue date, and '{assetClass}' terms declare no issueDate; the record's effective-from date is not used as a schedule anchor.");
    }

    [Fact]
    public async Task DeclaredConventions_OnOrBesideLegs_AreBlockedWithTheLegReason()
    {
        var swap = StampOneSwap(withLegBlocks: true);
        swap["assetClassTermsVersion"] = 2;
        var bondWithLegs = WithMembers(
            DeclaredBondObject(MinimalBlock, D(2041, 1, 31), D(2043, 1, 31), "SemiAnnual"),
            ("cashFlowLegs", JsonNode.Parse("""[{"legId":"fixed","legType":"Fixed","fixedRate":4.25,"notional":1000000,"paymentFrequency":"SemiAnnual","dayCount":"30/360"}]""")));

        var declaringSwap = await ProjectDeclaredAsync(JsonSerializer.SerializeToElement(swap), WeekendsSource, assetClass: "Swap");
        var declaringBond = await ProjectDeclaredAsync(JsonSerializer.SerializeToElement(bondWithLegs), WeekendsSource);

        ShouldBeBlocked(declaringSwap, LegsNotFollowedReason);
        declaringSwap.LegSchedules.Should().BeNull();
        ShouldBeBlocked(declaringBond, LegsNotFollowedReason);
    }

    [Fact]
    public async Task DeclaredConventions_ABondStampedAboveWhatThisNodeReads_FailsClosedWithTheReadGuardReason()
    {
        var terms = DeclaredBond(MinimalBlock, D(2041, 1, 31), D(2043, 1, 31), "SemiAnnual", stamp: 4);
        SecurityTermsEnvelope.TryGetReadRefusal("Bond", terms, out var guardReason).Should().BeTrue();

        var projection = await ProjectDeclaredAsync(terms, WeekendsSource);

        ShouldBeBlocked(projection, $"Declared schedule conventions cannot be read: {guardReason}");
    }

    // ── 10. Principal ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_AContractualPrincipalSchedule_IsBlocked()
    {
        var terms = WithMembers(
            DeclaredBondObject(MinimalBlock, D(2041, 1, 31), D(2042, 1, 31), "Quarterly", par: 1000m),
            ("principalSchedule", JsonNode.Parse("""[{"paymentDate":"2041-07-31","amount":400}]""")));

        var projection = await ProjectDeclaredAsync(JsonSerializer.SerializeToElement(terms), WeekendsSource, StructuredCashFlowSourceKind.CalculatedSinker);

        ShouldBeBlocked(
            projection,
            "Declared schedule conventions are not yet followed for a contractual principal schedule; the projection is blocked.");
    }

    [Fact]
    public async Task DeclaredConventions_AnAssertedEmptyPrincipalSchedule_IsADeclaredBullet()
    {
        var terms = WithMembers(
            DeclaredBondObject(MinimalBlock, D(2041, 1, 31), D(2042, 1, 31), "Quarterly", par: 1000m),
            ("principalSchedule", new JsonArray()));

        var projection = await ProjectDeclaredAsync(JsonSerializer.SerializeToElement(terms), WeekendsSource, StructuredCashFlowSourceKind.CalculatedSinker);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => row.PrincipalAmount).Should().Equal(0m, 0m, 0m, 1000m);
        projection.Schedule.Select(static row => row.Factor).Should().Equal(1m, 1m, 1m, 0m);
        projection.ScheduleEvidence.Should().NotBeNull();
    }

    [Fact]
    public async Task DeclaredConventions_CalculatedSinker_PaysEqualPrincipalPerPeriod()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, D(2041, 1, 31), D(2042, 1, 31), "Quarterly", par: 1000m),
            WeekendsSource,
            StructuredCashFlowSourceKind.CalculatedSinker);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => row.PrincipalAmount).Should().Equal(250m, 250m, 250m, 250m);
        projection.Schedule.Select(static row => row.Factor).Should().Equal(0.75m, 0.5m, 0.25m, 0m);
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(12.5000m, 9.3750m, 6.2500m, 3.1250m);
    }

    // ── 11. Level and zero coupon ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_LevelCouponCashBasis_PaysRateOverPeriodsPerYear()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond("""{"accrualAdjusted":false,"couponCashBasis":"Level"}""", D(2041, 1, 15), D(2043, 1, 15), "SemiAnnual", dayCount: "ACT/360"),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(2.5000m, 2.5000m, 2.5000m, 2.5000m);
    }

    [Fact]
    public async Task DeclaredConventions_LevelAtMaturity_IsBlocked()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond("""{"accrualAdjusted":false,"couponCashBasis":"Level"}""", D(2041, 1, 15), D(2043, 1, 15), "AtMaturity"),
            WeekendsSource);

        ShouldBeBlocked(projection, "Declared couponCashBasis 'Level' needs a periodic paymentFrequency.");
    }

    [Fact]
    public async Task DeclaredConventions_AZeroCouponWithoutADayCount_PaysNoInterest()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, D(2041, 1, 31), D(2042, 1, 31), "Quarterly", dayCount: null, coupon: 0m),
            WeekendsSource);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Should().HaveCount(4).And.OnlyContain(static row => row.InterestAmount == 0m);
        projection.Schedule[^1].PrincipalAmount.Should().Be(100m);
    }

    [Theory]
    [InlineData(null, DeclaredDayCountMissingReason)]
    [InlineData("Lunar/13", "Declared schedule conventions need a recognized dayCount to compute interest; 'Lunar/13' is not one.")]
    public async Task DeclaredConventions_ACouponWithoutARecognizedDayCount_IsBlocked(string? dayCount, string reason)
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, D(2041, 1, 31), D(2042, 1, 31), "Quarterly", dayCount: dayCount),
            WeekendsSource);

        ShouldBeBlocked(projection, reason);
    }

    // ── 12. Ordering and wrapping ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeclaredConventions_WithoutAMaturity_GetTheMaturityText()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, D(2041, 1, 31), maturity: null, "Quarterly"),
            WeekendsSource);

        ShouldBeBlocked(projection, "Maturity is missing; no calculated cash flows may post.");
    }

    [Fact]
    public async Task DeclaredConventions_OnAStepCouponBond_GetTheStepText()
    {
        var terms = DeclaredBondObject(MinimalBlock, D(2041, 1, 31), D(2043, 1, 31), "SemiAnnual");
        terms["couponType"] = "Step";
        terms["stepSchedule"] = JsonNode.Parse("""[{"effectiveDate":"2041-01-31","rate":4.0},{"effectiveDate":"2042-01-31","rate":5.0}]""");
        var element = JsonSerializer.SerializeToElement(terms);
        var security = BuildSecurity(Guid.NewGuid(), "Bond", element);
        ScheduleSpecFactory.Resolve(security, StructuredCashFlowTermsResolver.Resolve(security), WeekendsSource)
            .Status.Should().Be(DeclaredScheduleStatus.Declared, "the step-coupon bond's block is factory-clean");

        var projection = await ProjectDeclaredAsync(element, WeekendsSource);

        ShouldBeBlocked(
            projection,
            "Step-coupon or inflation-linked terms require a supported provider schedule; the fixed-rate calculation cannot represent them.");
    }

    [Fact]
    public async Task DeclaredConventions_TheNormalizedBasisWrapper_KeepsTheDeclaredReason()
    {
        var projection = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, D(2041, 1, 31), D(2042, 1, 31), "Quarterly", dayCount: null, par: null),
            WeekendsSource);

        projection.IsNormalizedPer100.Should().BeTrue();
        ShouldBeBlocked(projection, DeclaredDayCountMissingReason);
    }

    [Fact]
    public async Task DeclaredConventions_WithoutAnIssueDate_AreRefusedBeforeTheEffectiveFromFallback()
    {
        // EffectiveFrom lies after the maturity, so the legacy fallback would return an empty schedule
        // with no reason; a declaring record is refused with its reason first.
        var effectiveFrom = new DateTimeOffset(2043, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var declaring = await ProjectDeclaredAsync(
            DeclaredBond(MinimalBlock, issue: null, D(2042, 1, 31), "Quarterly"),
            WeekendsSource,
            effectiveFrom: effectiveFrom);
        var undeclared = await ProjectDeclaredAsync(
            DeclaredBond(block: null, issue: null, D(2042, 1, 31), "Quarterly"),
            WeekendsSource,
            effectiveFrom: effectiveFrom);

        ShouldBeBlocked(
            declaring,
            "Declared schedule conventions need the contractual issue date; the record carries no issueDate, and its effective-from date is not used as a schedule anchor.");
        undeclared.Schedule.Should().BeEmpty();
        undeclared.BlockedReason.Should().BeNull("the undeclared fallback is unchanged");
    }

    [Fact]
    public async Task DeclaredConventions_ACoverageGap_IsReportedAndLoggedAtDebugWithoutBlocking()
    {
        var partial = new HolidayBusinessDayCalendar("BRBD", 1, coverage: new CalendarCoverage(null, D(2041, 6, 30)));
        var securityId = Guid.NewGuid();
        var logger = new RecordingLogger<SecurityMasterCashFlowService>();
        var service = new SecurityMasterCashFlowService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            Array.Empty<IStructuredCashFlowProvider>(),
            QueryWith(securityId, DeclaredBond(
                """{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["BRBD"]}""",
                D(2041, 1, 31), D(2042, 1, 31), "Quarterly")),
            logger,
            calendars: new StaticBusinessDayCalendarSource([partial]));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection!.BlockedReason.Should().BeNull("a coverage gap is informational");
        projection.Schedule.Select(static row => ScheduleRowDate(row)).Should().Equal(
            D(2041, 4, 30), D(2041, 7, 31), D(2041, 10, 31), D(2042, 1, 31));
        projection.ScheduleEvidence!.Calendars.Should().Equal(
            new StructuredCashFlowCalendarEvidence("BRBD", partial.ContentHash, nameof(ScheduleCalendarPurpose.Payment)));
        projection.ScheduleEvidence.CoverageGaps.Should().Equal(
            new StructuredCashFlowCoverageGap("BRBD", D(2041, 7, 31), D(2042, 1, 31)));
        logger.Entries.Should().ContainSingle(static entry => entry.Message.Contains("used calendar dates outside coverage"))
            .Which.Should().Match<RecordedLogEntry>(static entry =>
                entry.LogLevel == LogLevel.Debug
                && entry.Message.Contains("1 calendar(s)")
                && entry.Message.Contains("first BRBD from"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static SecurityMasterCashFlowService BuildService(
        ISecurityMasterCashFlowStore store,
        SecurityMasterQueryContract query,
        IBusinessDayCalendarSource? calendars)
        => new(
            store,
            Array.Empty<IStructuredCashFlowProvider>(),
            query,
            NullLogger<SecurityMasterCashFlowService>.Instance,
            calendars: calendars);

    private static SecurityDetailDto BuildSecurity(Guid securityId, string assetClass, JsonElement assetSpecificTerms)
        => BuildSecurity(securityId, assetSpecificTerms) with { AssetClass = assetClass };

    private static async Task<StructuredCashFlowProjectionDto> ProjectDeclaredAsync(
        JsonElement terms,
        IBusinessDayCalendarSource? calendars,
        StructuredCashFlowSourceKind sourceKind = StructuredCashFlowSourceKind.CalculatedBullet,
        string assetClass = "Bond",
        DateTimeOffset? effectiveFrom = null)
    {
        var securityId = Guid.NewGuid();
        var security = BuildSecurity(securityId, assetClass, terms);
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(effectiveFrom is { } from ? security with { EffectiveFrom = from } : security);
        var service = BuildService(StoreWith(securityId, sourceKind), query, calendars);

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        return projection!;
    }

    private static void ShouldBeBlocked(StructuredCashFlowProjectionDto projection, string reason)
    {
        projection.BlockedReason.Should().Be(reason);
        projection.Schedule.Should().BeEmpty();
        projection.ScheduleEvidence.Should().BeNull("only a generated declared schedule carries evidence");
    }

    // The kernel's refusal of the spec the factory maps the security's declared block to.
    private static ScheduleFailure KernelFailure(SecurityDetailDto security)
    {
        var resolution = ScheduleSpecFactory.Resolve(security, StructuredCashFlowTermsResolver.Resolve(security), WeekendsSource);
        resolution.Status.Should().Be(DeclaredScheduleStatus.Declared, resolution.Refusal?.Message);
        var generated = ScheduleGenerator.Generate(resolution.Spec!);
        generated.Succeeded.Should().BeFalse();
        return generated.Failure!;
    }

    // A flat Bond row stamped at its scheduleConventions version unless told otherwise; a null member
    // is left out, and a null block declares nothing.
    private static JsonElement DeclaredBond(
        string? block,
        DateOnly? issue,
        DateOnly? maturity,
        string? frequency,
        string? dayCount = "30/360",
        decimal? coupon = 5m,
        decimal? par = 100m,
        int? stamp = 2)
        => JsonSerializer.SerializeToElement(DeclaredBondObject(block, issue, maturity, frequency, dayCount, coupon, par, stamp));

    private static JsonObject DeclaredBondObject(
        string? block,
        DateOnly? issue,
        DateOnly? maturity,
        string? frequency,
        string? dayCount = "30/360",
        decimal? coupon = 5m,
        decimal? par = 100m,
        int? stamp = 2)
    {
        var terms = new JsonObject();
        if (stamp is { } version)
        {
            terms["assetClassTermsVersion"] = version;
        }

        if (maturity is { } maturityDate)
        {
            terms["maturity"] = Iso(maturityDate);
        }

        if (issue is { } issueDate)
        {
            terms["issueDate"] = Iso(issueDate);
        }

        terms["couponType"] = "Fixed";
        if (coupon is { } rate)
        {
            terms["couponRate"] = rate;
        }

        if (dayCount is not null)
        {
            terms["dayCount"] = dayCount;
        }

        terms["isCallable"] = false;
        terms["issuerName"] = "ACME Corp";
        terms["subclass"] = "Corporate";
        if (par is { } face)
        {
            terms["par"] = face;
        }

        if (frequency is not null)
        {
            terms["paymentFrequency"] = frequency;
        }

        if (block is not null)
        {
            terms["scheduleConventions"] = JsonNode.Parse(block);
        }

        return terms;
    }

    // A StructuredCredit profile envelope at stamp 3: never decoded for versioned containers, wherever
    // its blocks sit.
    private static JsonObject StructuredCreditEnvelope(bool withBlocks)
    {
        var fields = new JsonObject
        {
            ["tranche"] = "A-1",
            ["collateralType"] = "CLO",
            ["originalFace"] = 1_000_000m,
            ["couponOrIndex"] = "5.25",
            ["maturity"] = "2043-01-15",
            ["issueDate"] = "2041-01-15",
            ["paymentFrequency"] = "Quarterly",
            ["dayCount"] = "30/360",
        };
        if (withBlocks)
        {
            fields["scheduleConventions"] = JsonNode.Parse(PopulatedBlock);
        }

        var envelope = new JsonObject
        {
            ["assetClassTermsVersion"] = 3,
            ["customProfileId"] = "structured-credit-v1",
            ["profileVersion"] = 1,
            ["profileFields"] = fields.DeepClone(),
        };
        foreach (var (key, value) in fields)
        {
            envelope[key] = value?.DeepClone();
        }

        return envelope;
    }

    // A two-leg Swap at stamp 1, whose leg blocks sit below the version that introduces them.
    private static JsonObject StampOneSwap(bool withLegBlocks)
    {
        var swap = new JsonObject
        {
            ["assetClassTermsVersion"] = 1,
            ["effectiveDate"] = "2041-01-31",
            ["maturityDate"] = "2043-01-31",
            ["legs"] = JsonNode.Parse(
                """
                [
                  {"legId":"fixed","legType":"Fixed","currency":"USD","direction":"Pay","fixedRate":4.25,"notional":1000000,"paymentFrequency":"SemiAnnual","dayCount":"30/360","exchangesPrincipal":false},
                  {"legId":"float","legType":"Floating","currency":"USD","direction":"Receive","index":"SOFR","spreadBps":15,"currentIndexRate":4.0,"notional":1000000,"paymentFrequency":"Quarterly","dayCount":"ACT/360","exchangesPrincipal":false}
                ]
                """),
        };
        if (withLegBlocks)
        {
            foreach (var leg in swap["legs"]!.AsArray())
            {
                leg!["scheduleConventions"] = JsonNode.Parse(PopulatedBlock);
            }
        }

        return swap;
    }

    private static JsonObject WithMembers(JsonObject terms, params (string Key, JsonNode? Value)[] members)
    {
        var copy = terms.DeepClone().AsObject();
        foreach (var (key, value) in members)
        {
            copy[key] = value;
        }

        return copy;
    }

    private static StructuredCashFlowScheduleEntry Row(DateOnly date, decimal principal, decimal interest, decimal factor)
        => new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), principal, interest, factor);

    private static string Text(JsonObject terms) => terms.ToJsonString();

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly D(int year, int month, int day) => new(year, month, day);
}
