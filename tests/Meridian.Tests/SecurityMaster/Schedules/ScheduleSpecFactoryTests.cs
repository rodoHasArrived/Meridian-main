using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Pins <see cref="ScheduleSpecFactory"/>: which records declare a schedule (only where the mapping
/// decodes the block), the anchors read by schema fact, the spellings, conflicts and defaults, the
/// calendar rules with no fallback, the refusal texts and their precedence, the calendar-free shape
/// check, totality and determinism.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScheduleSpecFactoryTests
{
    private static readonly Guid SecurityId = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000000f8");

    // The members of a flat Bond row, without braces: a one-year quarterly bond issued on a month end.
    private const string BondBody =
        "\"maturity\":\"2027-01-31\",\"issueDate\":\"2026-01-31\",\"couponType\":\"Fixed\",\"couponRate\":4.5,\"dayCount\":\"ACT/360\"," +
        "\"isCallable\":false,\"issuerName\":\"ACME Corp\",\"subclass\":\"Corporate\",\"par\":1000,\"paymentFrequency\":\"Quarterly\"";

    // The same bond without its schedule anchors (maturity, issueDate, paymentFrequency), which BondWith places.
    private const string BondEconomics =
        "\"couponType\":\"Fixed\",\"couponRate\":4.5,\"dayCount\":\"ACT/360\"," +
        "\"isCallable\":false,\"issuerName\":\"ACME Corp\",\"subclass\":\"Corporate\",\"par\":1000";

    private const string SwapBody = "\"effectiveDate\":\"2026-01-31\",\"maturityDate\":\"2031-01-31\"";

    private const string PlainLeg =
        """{"legId":"fixed","legType":"Fixed","currency":"USD","direction":"Pay","fixedRate":4.25,"notional":10000000,"paymentFrequency":"SemiAnnual","dayCount":"30/360","exchangesPrincipal":false}""";

    private const string DeclaringLeg =
        """{"legId":"float","legType":"Floating","currency":"USD","direction":"Receive","index":"SOFR","notional":10000000,"paymentFrequency":"Quarterly","dayCount":"ACT/360","exchangesPrincipal":false,"scheduleConventions":{"accrualAdjusted":false}}""";

    private const string Cadence = "Declared schedule conventions need a paymentFrequency that is monthly or longer, or AtMaturity";

    private const string LegsNotFollowed =
        "Declared schedule conventions are not yet followed on leg-based projections; the projection is blocked until per-leg declared schedules are supported.";

    private const string NoCalendarAssumed = "neither the common terms' holiday calendar nor weekends only is assumed.";

    // Test calendars carry no holidays: the tests pin which calendar is named, not holiday data.
    private static readonly IBusinessDayCalendar Usny = new HolidayBusinessDayCalendar("USNY", 1);
    private static readonly IBusinessDayCalendar Gblo = new HolidayBusinessDayCalendar("GBLO", 1);
    private static readonly IBusinessDayCalendar Weekends = BusinessDayCalendars.WeekendsOnly;
    private static readonly IBusinessDayCalendarSource Calendars = new StaticBusinessDayCalendarSource([Weekends, Usny, Gblo]);

    // ── (a) Undeclared ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> UndeclaredRows => new()
    {
        { "Bond", Flat(1, BondBody + ",\"scheduleConventions\":{\"businessDayConvention\":\"MF\",\"accrualAdjusted\":false,\"paymentCalendars\":[\"WEEKENDS\"]}") },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":null") },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":{}") },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":" + AllNullBlock) },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":{\"paymentCalendars\":[\" \"]}") },
        { "Bond", Flat(2, BondBody) },
        { "Equity", Flat(2, "\"shareClass\":\"A\",\"scheduleConventions\":{\"accrualAdjusted\":false}") },
        { "Repo", Flat(2, "\"counterparty\":\"Dealer\",\"startDate\":\"2026-01-02\",\"endDate\":\"2026-02-02\",\"repoRate\":4.1,\"scheduleConventions\":{\"accrualAdjusted\":false}") },
        { "Swap", Flat(2, SwapBody + ",\"legs\":[" + PlainLeg.Replace("}", ",\"scheduleConventions\":null}", StringComparison.Ordinal) + "," + PlainLeg.Replace("}", ",\"scheduleConventions\":{}}", StringComparison.Ordinal) + "]") },
        { "Swap", Flat(2, SwapBody + ",\"legs\":{\"scheduleConventions\":{\"accrualAdjusted\":false}}") },
        { "Swap", Flat(1, SwapBody + ",\"legs\":[" + DeclaringLeg + "]") },
    };

    [Theory]
    [MemberData(nameof(UndeclaredRows))]
    public void RecordsThatDeclareNothingTheMappingDecodes_AreUndeclared(string assetClass, string terms)
    {
        var security = Security(assetClass, Json(terms));

        foreach (var resolution in new[] { Resolve(security), Resolve(security, calendars: null), Shape(security) })
        {
            ShouldBeUndeclared(resolution);
        }
    }

    // ── (b) The minimal declared Bond ───────────────────────────────────────────────────────────

    [Fact]
    public void TheMinimalDeclaredBond_MapsToTheIssueAnchoredForwardGrid()
    {
        var resolution = Resolve(Bond("""{"accrualAdjusted":false}"""));

        var spec = DeclaredSpec(resolution);
        spec.Should().Be(new ScheduleSpec(
            D(2026, 1, 31),
            D(2027, 1, 31),
            ScheduleTenor.Months(3),
            ScheduleDirection.Forward,
            ScheduleAnchorMode.Anchored,
            ScheduleRoll.AnchorDay,
            ScheduleStubPolicy.Short,
            FirstRegularDate: null,
            LastRegularDate: null,
            AccrualAdjustment: null,
            PaymentAdjustment: new BusinessDayAdjustment(BusinessDayConvention.Unadjusted, null),
            EffectiveDateAdjustment: null,
            TerminationDateAdjustment: null,
            PaymentLagBusinessDays: 0,
            MaxPeriods: 1200));
        resolution.IsDeclared.Should().BeTrue();
        resolution.Conventions.Should().BeEquivalentTo(ScheduleConventionTerms.Empty with { AccrualAdjusted = false });

        var declared = ScheduleGenerator.Generate(spec);
        var legacy = ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)));
        declared.Succeeded.Should().BeTrue();
        Unadjusted(declared).Should().Equal(Unadjusted(legacy));
        Unadjusted(declared).Should().Equal(
            (D(2026, 1, 31), D(2026, 4, 30)),
            (D(2026, 4, 30), D(2026, 7, 31)),
            (D(2026, 7, 31), D(2026, 10, 31)),
            (D(2026, 10, 31), D(2027, 1, 31)));
        declared.Periods.Select(static period => period.PaymentDate).Should().Equal(declared.Periods.Select(static period => period.UnadjustedEnd));
    }

    [Fact]
    public void DeclaredMappingVersion_IsPinned_AndIsTheVersionTheCapabilityLineLogs()
    {
        ScheduleSpecFactory.DeclaredMappingVersion.Should().Be("security-master-declared-schedule-v1");
        ScheduleSpecFactory.DeclaredMappingVersion.Should().Be(SecurityVersionedTerms.DeclaredScheduleMappingVersion);
    }

    // ── (c) Cadence ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AtMaturity_IsOneTermPeriod_AndIgnoresTheRoll()
    {
        var spec = DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false,"rollConvention":"15"}""", body: BondWith(frequency: "AtMaturity"))));

        spec.Tenor.Should().Be(ScheduleTenor.Term);
        spec.Roll.Should().Be(ScheduleRoll.AnchorDay);
        var generated = ScheduleGenerator.Generate(spec);
        generated.Periods.Should().ContainSingle().Which.Kind.Should().Be(SchedulePeriodKind.Term);
    }

    [Theory]
    [InlineData(null, Cadence + "; the record carries none.")]
    [InlineData("Irregular", Cadence + "; 'Irregular' is not one.")]
    [InlineData("Weekly", Cadence + "; 'Weekly' is not one.")]
    [InlineData("Fortnightly", Cadence + "; 'Fortnightly' is not one.")]
    [InlineData("Daily", Cadence + "; 'Daily' is not one.")]
    [InlineData("every so often", Cadence + "; 'every so often' is not one.")]
    [InlineData(" ", Cadence + "; ' ' is not one.")]
    public void AFrequencyWithNoTenor_IsCadenceUnavailable(string? frequency, string message)
    {
        var security = Bond("""{"accrualAdjusted":false}""", body: BondWith(frequency: frequency));

        ShouldRefuse(Resolve(security), DeclaredScheduleRefusalCode.CadenceUnavailable, message);
        ShouldRefuse(Shape(security), DeclaredScheduleRefusalCode.CadenceUnavailable, message);
    }

    [Fact]
    public void AClassWhoseSchemaDeclaresNoIssueDateOrPaymentFrequency_IsRefusedWithTheSchemaFact()
    {
        var certificate = Security("CertificateOfDeposit", Json(Flat(2,
            "\"issuerName\":\"First Bank\",\"maturity\":\"2027-01-31\",\"couponRate\":4.0,\"dayCount\":\"ACT/365\",\"scheduleConventions\":{\"accrualAdjusted\":false}")));
        var structuredCredit = Security("StructuredCredit", Json(Flat(3,
            "\"tranche\":\"A-1\",\"collateralType\":\"CLO\",\"originalFace\":1000000,\"couponOrIndex\":\"5.25\",\"maturity\":\"2033-01-15\",\"issueDate\":\"2026-01-15\",\"paymentFrequency\":\"Quarterly\",\"scheduleConventions\":{\"accrualAdjusted\":false}")));

        ShouldRefuse(
            Resolve(certificate),
            DeclaredScheduleRefusalCode.EffectiveDateMissing,
            "Declared schedule conventions need the contractual issue date, and 'CertificateOfDeposit' terms declare no issueDate; the record's effective-from date is not used as a schedule anchor.");
        ShouldRefuse(
            Resolve(structuredCredit),
            DeclaredScheduleRefusalCode.EffectiveDateMissing,
            "Declared schedule conventions need the contractual issue date, and 'StructuredCredit' terms declare no issueDate; the record's effective-from date is not used as a schedule anchor.");
    }

    // ── (d) Roll ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-01-31", "2027-01-31", "Forward", "EndOfMonth")]
    [InlineData("2026-01-30", "2027-01-30", "Forward", "FixedDay(30)")]
    [InlineData("2026-01-15", "2027-02-28", "Backward", "EndOfMonth")]
    [InlineData("2026-01-31", "2027-02-27", "Backward", "FixedDay(27)")]
    public void EndOfMonthRule_RollsFromTheGenerationAnchor(string issue, string maturity, string direction, string roll)
    {
        var spec = DeclaredSpec(Resolve(Bond(
            $$"""{"accrualAdjusted":false,"endOfMonthRule":true,"direction":"{{direction}}"}""",
            body: BondWith(issue: issue, maturity: maturity))));

        spec.Roll.ToString().Should().Be(roll);
    }

    [Fact]
    public void EndOfMonthRule_AnchorsOnTheFirstRegularDate_GoingForward()
    {
        var spec = DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false,"endOfMonthRule":true,"firstRegularDate":"2026-04-30"}""",
            body: BondWith(issue: "2026-02-15"))));

        spec.Roll.Should().Be(ScheduleRoll.EndOfMonth);
        spec.FirstRegularDate.Should().Be(D(2026, 4, 30));
    }

    [Theory]
    [InlineData("2027-02-15", "2026-11-30", "EndOfMonth")]
    [InlineData("2027-02-28", "2026-11-15", "FixedDay(15)")]
    public void EndOfMonthRule_AnchorsOnTheLastRegularDate_GoingBackward(string maturity, string lastRegularDate, string roll)
    {
        var spec = DeclaredSpec(Resolve(Bond(
            $$"""{"accrualAdjusted":false,"endOfMonthRule":true,"direction":"Backward","lastRegularDate":"{{lastRegularDate}}"}""",
            body: BondWith(issue: "2026-01-15", maturity: maturity))));

        spec.Roll.ToString().Should().Be(roll);
        spec.LastRegularDate.Should().Be(DateOnly.Parse(lastRegularDate, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("EOM", "EndOfMonth")]
    [InlineData("15", "FixedDay(15)")]
    [InlineData("IMM", "Imm")]
    [InlineData("AnchorDay", "AnchorDay")]
    public void ADeclaredRoll_Wins(string rollConvention, string roll)
    {
        DeclaredSpec(Resolve(Bond($$"""{"accrualAdjusted":false,"rollConvention":"{{rollConvention}}"}"""))).Roll.ToString().Should().Be(roll);
    }

    [Fact]
    public void ADeclaredRoll_AgreeingWithEndOfMonthRule_IsAccepted()
    {
        DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false,"rollConvention":"EOM","endOfMonthRule":true}"""))).Roll.Should().Be(ScheduleRoll.EndOfMonth);
        DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false,"rollConvention":"15","endOfMonthRule":false}"""))).Roll.Should().Be(ScheduleRoll.FixedDay(15));
    }

    [Theory]
    [InlineData("""{"accrualAdjusted":false,"rollConvention":"NEAREST"}""", DeclaredScheduleRefusalCode.ConventionUnrecognized, "Declared rollConvention 'NEAREST' is not a recognized value.")]
    [InlineData("""{"accrualAdjusted":false,"businessDayConvention":"NEAREST","paymentCalendars":["WEEKENDS"]}""", DeclaredScheduleRefusalCode.ConventionUnrecognized, "Declared businessDayConvention 'NEAREST' is not a recognized value.")]
    [InlineData("""{"accrualAdjusted":false,"rollConvention":"15","endOfMonthRule":true}""", DeclaredScheduleRefusalCode.ConventionConflict, "Declared rollConvention '15' conflicts with endOfMonthRule true.")]
    [InlineData("""{"accrualAdjusted":false,"rollConvention":"IMM","endOfMonthRule":true}""", DeclaredScheduleRefusalCode.ConventionConflict, "Declared rollConvention 'IMM' conflicts with endOfMonthRule true.")]
    [InlineData("""{"accrualAdjusted":false,"rollConvention":"EOM","endOfMonthRule":false}""", DeclaredScheduleRefusalCode.ConventionConflict, "Declared rollConvention 'EOM' conflicts with endOfMonthRule false.")]
    public void RollSpellingsAndConflicts_AreRefused(string block, DeclaredScheduleRefusalCode code, string message)
    {
        ShouldRefuse(Resolve(Bond(block)), code, message);
        ShouldRefuse(Shape(Bond(block)), code, message);
    }

    // ── (e) Spellings and anchors ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("stubPolicy", "ShortInitial")]
    [InlineData("stubPolicy", "LongFinal")]
    [InlineData("direction", "backward")]
    [InlineData("direction", "Backwards")]
    [InlineData("couponRateUnit", "percent")]
    [InlineData("couponCashBasis", "level")]
    [InlineData("businessDayConvention", "")]
    [InlineData("rollConvention", "")]
    [InlineData("direction", "")]
    [InlineData("stubPolicy", "")]
    [InlineData("couponRateUnit", "")]
    [InlineData("couponCashBasis", "")]
    [InlineData("rollConvention", "32")]
    public void AnUnrecognizedSpelling_IsRefusedWithTheExactText(string member, string raw)
    {
        var block = $$"""{"accrualAdjusted":false,"paymentCalendars":["WEEKENDS"],"{{member}}":"{{raw}}"}""";

        ShouldRefuse(Resolve(Bond(block)), DeclaredScheduleRefusalCode.ConventionUnrecognized, $"Declared {member} '{raw}' is not a recognized value.");
    }

    [Fact]
    public void RecognizedSpellings_AreAccepted_AndOnlyTheScheduleMembersShapeTheSpec()
    {
        var spec = DeclaredSpec(Resolve(Bond(
            """{"accrualAdjusted":false,"direction":"Backward","stubPolicy":"Long","couponRateUnit":"Percent","couponCashBasis":"Level"}""")));

        spec.Direction.Should().Be(ScheduleDirection.Backward);
        spec.StubPolicy.Should().Be(ScheduleStubPolicy.Long);
        DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false,"couponRateUnit":"Fraction","couponCashBasis":"DayCount"}""")))
            .Should().Be(DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false}"""))));
    }

    [Fact]
    public void AnAbsentIssueDate_IsRefused_AndTheEffectiveFromDateIsNeverUsed()
    {
        var security = Bond("""{"accrualAdjusted":false}""", body: BondWith(issue: null));
        security.EffectiveFrom.Should().NotBe(default);

        ShouldRefuse(
            Resolve(security),
            DeclaredScheduleRefusalCode.EffectiveDateMissing,
            "Declared schedule conventions need the contractual issue date; the record carries no issueDate, and its effective-from date is not used as a schedule anchor.");
    }

    [Theory]
    [InlineData("2027-01-31", "2027-01-31")]
    [InlineData("2027-06-30", "2027-01-31")]
    public void AnIssueDateNotBeforeTheMaturity_IsRefused(string issue, string maturity)
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false}""", body: BondWith(issue: issue, maturity: maturity))),
            DeclaredScheduleRefusalCode.EffectiveDateNotBeforeTermination,
            $"Declared schedule conventions need an issueDate before the maturity; the record's issueDate {issue} is not before its maturity {maturity}.");
    }

    [Fact]
    public void ARecordWithNoMaturityAnywhere_IsRefused()
    {
        // The engine refuses a missing maturity before it asks the factory; the shape check must not throw.
        var security = Bond("""{"accrualAdjusted":false}""", body: BondWith(maturity: null));

        ShouldRefuse(
            Shape(security),
            DeclaredScheduleRefusalCode.EffectiveDateNotBeforeTermination,
            "Declared schedule conventions need an issueDate before the maturity; the record carries no maturity.");
    }

    // ── (f) Profile envelope ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AStructuredCreditProfileEnvelope_IsUndeclared_WhereverItsBlocksSit()
    {
        const string body =
            "\"tranche\":\"A-1\",\"collateralType\":\"CLO\",\"originalFace\":1000000,\"couponOrIndex\":\"5.25\",\"maturity\":\"2033-01-15\"," +
            "\"scheduleConventions\":{\"accrualAdjusted\":false}";
        var envelope = Security("StructuredCredit", Json(
            "{\"assetClassTermsVersion\":4,\"customProfileId\":\"structured-credit-v1\",\"profileFields\":{" + body + "}," + body + "}"));

        ShouldBeUndeclared(Resolve(envelope));
        ShouldBeUndeclared(Shape(envelope));
    }

    // ── (g) Adjustments and conflicts ───────────────────────────────────────────────────────────

    [Fact]
    public void AStatedUnadjustedAccrual_AdjustsPaymentDatesOnly()
    {
        var spec = DeclaredSpec(Resolve(Bond("""{"businessDayConvention":"MF","paymentCalendars":["WEEKENDS"],"accrualAdjusted":false}""")));

        spec.PaymentAdjustment.Should().Be(BusinessDayAdjustment.Of(BusinessDayConvention.ModifiedFollowing, Weekends));
        spec.AccrualAdjustment.Should().BeNull();
        spec.EffectiveDateAdjustment.Should().BeNull();
        spec.TerminationDateAdjustment.Should().BeNull();
        spec.PaymentLagBusinessDays.Should().Be(0);
    }

    [Fact]
    public void AStatedAdjustedAccrual_AdjustsAccrualAndPaymentDates()
    {
        var spec = DeclaredSpec(Resolve(Bond("""{"businessDayConvention":"ModifiedFollowing","paymentCalendars":["WEEKENDS"],"accrualAdjusted":true,"paymentLagBusinessDays":2}""")));

        var modifiedFollowing = BusinessDayAdjustment.Of(BusinessDayConvention.ModifiedFollowing, Weekends);
        spec.AccrualAdjustment.Should().Be(modifiedFollowing);
        spec.PaymentAdjustment.Should().Be(modifiedFollowing);
        spec.EffectiveDateAdjustment.Should().BeNull("effective and termination dates take the accrual adjustment");
        spec.TerminationDateAdjustment.Should().BeNull();
        spec.PaymentLagBusinessDays.Should().Be(2);
    }

    [Fact]
    public void ALagWithCalendarsAndNoConvention_LagsUnadjustedPayments()
    {
        var spec = DeclaredSpec(Resolve(Bond("""{"paymentLagBusinessDays":2,"paymentCalendars":["WEEKENDS"]}""")));

        spec.PaymentAdjustment.Should().Be(new BusinessDayAdjustment(BusinessDayConvention.Unadjusted, Weekends));
        spec.AccrualAdjustment.Should().BeNull();
        spec.PaymentLagBusinessDays.Should().Be(2);
    }

    [Theory]
    [InlineData("""{"businessDayConvention":"MF","paymentCalendars":["WEEKENDS"]}""",
        "Declared businessDayConvention 'MF' needs accrualAdjusted stated: true adjusts accrual and payment dates, false adjusts payment dates only.")]
    [InlineData("""{"businessDayConvention":"Following"}""",
        "Declared businessDayConvention 'Following' needs accrualAdjusted stated: true adjusts accrual and payment dates, false adjusts payment dates only.")]
    [InlineData("""{"accrualAdjusted":true,"paymentCalendars":["WEEKENDS"]}""",
        "Declared accrualAdjusted true needs a businessDayConvention other than Unadjusted.")]
    [InlineData("""{"businessDayConvention":"Unadjusted","accrualAdjusted":true}""",
        "Declared accrualAdjusted true needs a businessDayConvention other than Unadjusted.")]
    [InlineData("""{"businessDayConvention":"MF","accrualAdjusted":false,"paymentLagBusinessDays":2,"paymentCalendars":["WEEKENDS"]}""",
        "Declared businessDayConvention 'MF' has no effect: with paymentLagBusinessDays 2 and accrualAdjusted false, the payment date counts business days from an unadjusted accrual end.")]
    [InlineData("""{"accrualAdjusted":false,"paymentLagBusinessDays":-1}""",
        "Declared paymentLagBusinessDays -1 must be zero or more.")]
    public void ConflictingOrIneffectiveMembers_AreRefused(string block, string message)
    {
        ShouldRefuse(Resolve(Bond(block)), DeclaredScheduleRefusalCode.ConventionConflict, message);
        ShouldRefuse(Shape(Bond(block)), DeclaredScheduleRefusalCode.ConventionConflict, message);
    }

    [Fact]
    public void AnUnadjustedConvention_NeedsNoAccrualStatementAndNoCalendar()
    {
        var spec = DeclaredSpec(Resolve(Bond("""{"businessDayConvention":"Unadjusted"}"""), calendars: null));

        spec.PaymentAdjustment.Should().Be(BusinessDayAdjustment.Unadjusted);
        spec.AccrualAdjustment.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"businessDayConvention":"Following","accrualAdjusted":false}""",
        "Declared businessDayConvention 'Following' needs paymentCalendars; " + NoCalendarAssumed)]
    [InlineData("""{"businessDayConvention":"MF","accrualAdjusted":true,"paymentLagBusinessDays":2}""",
        "Declared businessDayConvention 'MF' needs paymentCalendars; " + NoCalendarAssumed)]
    [InlineData("""{"accrualAdjusted":false,"paymentLagBusinessDays":2}""",
        "Declared paymentLagBusinessDays 2 needs paymentCalendars; " + NoCalendarAssumed)]
    public void AConventionOrLagWithoutCalendars_IsRefused_WithNoHolidayCalendarFallback(string block, string message)
    {
        var withHolidayCalendar = Security("Bond", Json(Flat(2, BondBody + ",\"scheduleConventions\":" + block)),
            Json("""{"displayName":"Declared schedule","currency":"USD","holidayCalendarId":"WEEKENDS"}"""));

        ShouldRefuse(Resolve(Bond(block)), DeclaredScheduleRefusalCode.CalendarRequired, message);
        ShouldRefuse(Resolve(withHolidayCalendar), DeclaredScheduleRefusalCode.CalendarRequired, message);
        ShouldRefuse(Shape(withHolidayCalendar), DeclaredScheduleRefusalCode.CalendarRequired, message);
    }

    // ── (h) Calendars ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SeveralPaymentCalendars_ResolveToTheirJoint()
    {
        var spec = DeclaredSpec(Resolve(Bond("""{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["USNY","GBLO"]}""")));

        spec.PaymentAdjustment!.Convention.Should().Be(BusinessDayConvention.ModifiedFollowing);
        spec.PaymentAdjustment.Calendar!.CalendarId.Should().Be("GBLO+USNY");
    }

    [Fact]
    public void AnUnresolvableCalendar_IsNamedExactly_TrimmedAndOnce()
    {
        ShouldRefuse(
            Resolve(Bond("""{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["USNY","ZZNONE"]}""")),
            DeclaredScheduleRefusalCode.CalendarUnresolvable,
            "Declared paymentCalendars name calendars this node has not loaded: 'ZZNONE'; a missing calendar is never replaced by weekends only.");
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false,"paymentCalendars":[" zzone ","USNY","ZZONE","ZZTWO"]}""")),
            DeclaredScheduleRefusalCode.CalendarUnresolvable,
            "Declared paymentCalendars name calendars this node has not loaded: 'zzone', 'ZZTWO'; a missing calendar is never replaced by weekends only.");
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false,"paymentCalendars":["WEEKENDS"]}"""), new StaticBusinessDayCalendarSource([Usny])),
            DeclaredScheduleRefusalCode.CalendarUnresolvable,
            "Declared paymentCalendars name calendars this node has not loaded: 'WEEKENDS'; a missing calendar is never replaced by weekends only.");
    }

    [Fact]
    public void DeclaredCalendars_WithNoSource_AreRefused()
    {
        ShouldRefuse(
            Resolve(Bond("""{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["USNY"]}"""), calendars: null),
            DeclaredScheduleRefusalCode.CalendarSourceUnavailable,
            "Declared paymentCalendars cannot be resolved: no business-day calendar source is registered on this node.");
        DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false}"""), calendars: null));
    }

    // ── (i) Refusal precedence ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnreadableStamp_ComesBeforeLegs()
    {
        var terms = Json(Flat(4, BondBody + ",\"cashFlowLegs\":[" + PlainLeg + "],\"scheduleConventions\":{\"accrualAdjusted\":false}"));
        SecurityTermsEnvelope.TryGetReadRefusal("Bond", terms, out var reason).Should().BeTrue();

        const string message = "Declared schedule conventions cannot be read: Unsupported assetClassTermsVersion '4' for asset class 'Bond'; this node reads versions 1 through 3.";
        message.Should().Be($"Declared schedule conventions cannot be read: {reason}");
        ShouldRefuse(Resolve(Security("Bond", terms)), DeclaredScheduleRefusalCode.TermsVersionUnreadable, message);
        Resolve(Security("Bond", terms)).Conventions.Should().BeSameAs(ScheduleConventionTerms.Empty);
    }

    [Fact]
    public void Legs_ComeBeforeAnchors()
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false}""", body: BondWith(issue: null, frequency: "Weekly") + ",\"cashFlowLegs\":[" + PlainLeg + "]")),
            DeclaredScheduleRefusalCode.LegConventionsNotFollowed,
            LegsNotFollowed);
    }

    [Fact]
    public void AMissingIssueDate_ComesBeforeCadence()
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false,"stubPolicy":"ShortInitial"}""", body: BondWith(issue: null, frequency: "Weekly"))),
            DeclaredScheduleRefusalCode.EffectiveDateMissing,
            "Declared schedule conventions need the contractual issue date; the record carries no issueDate, and its effective-from date is not used as a schedule anchor.");
    }

    [Fact]
    public void Cadence_ComesBeforeSpellings()
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false,"stubPolicy":"ShortInitial"}""", body: BondWith(frequency: "Weekly"))),
            DeclaredScheduleRefusalCode.CadenceUnavailable,
            Cadence + "; 'Weekly' is not one.");
    }

    [Fact]
    public void Spellings_ComeBeforeConflicts_AndConflictsBeforeCalendars()
    {
        ShouldRefuse(
            Resolve(Bond("""{"businessDayConvention":"NEAREST","accrualAdjusted":true,"paymentLagBusinessDays":-1}""")),
            DeclaredScheduleRefusalCode.ConventionUnrecognized,
            "Declared businessDayConvention 'NEAREST' is not a recognized value.");
        ShouldRefuse(
            Resolve(Bond("""{"businessDayConvention":"MF","paymentCalendars":["ZZNONE"]}""")),
            DeclaredScheduleRefusalCode.ConventionConflict,
            "Declared businessDayConvention 'MF' needs accrualAdjusted stated: true adjusts accrual and payment dates, false adjusts payment dates only.");
        ShouldRefuse(
            Resolve(Bond("""{"businessDayConvention":"MF","accrualAdjusted":false,"paymentCalendars":["ZZNONE"]}"""), calendars: null),
            DeclaredScheduleRefusalCode.CalendarSourceUnavailable,
            "Declared paymentCalendars cannot be resolved: no business-day calendar source is registered on this node.");
    }

    // ── (j) Swap and legs ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("legs")]
    [InlineData("swapLegs")]
    [InlineData("cashFlowLegs")]
    [InlineData("SwapLegs")]
    public void ASwapDeclaringOnALeg_IsNotFollowed_UnderEveryAlias(string alias)
    {
        var swap = Security("Swap", Json(Flat(2, SwapBody + $",\"{alias}\":[" + PlainLeg + "," + DeclaringLeg + "]")));

        var resolution = Resolve(swap);
        ShouldRefuse(resolution, DeclaredScheduleRefusalCode.LegConventionsNotFollowed, LegsNotFollowed);
        resolution.Conventions.Should().BeSameAs(ScheduleConventionTerms.Empty);
        ShouldRefuse(Shape(swap), DeclaredScheduleRefusalCode.LegConventionsNotFollowed, LegsNotFollowed);
    }

    [Fact]
    public void TheLegContainer_IsTheFirstNonEmptyAlias()
    {
        var emptyFirst = Security("Swap", Json(Flat(2, SwapBody + ",\"swapLegs\":[],\"cashFlowLegs\":[" + DeclaringLeg + "]")));
        var plainFirst = Security("Swap", Json(Flat(2, SwapBody + ",\"legs\":[" + PlainLeg + "],\"cashFlowLegs\":[" + DeclaringLeg + "]")));

        ShouldRefuse(Resolve(emptyFirst), DeclaredScheduleRefusalCode.LegConventionsNotFollowed, LegsNotFollowed);
        ShouldBeUndeclared(Resolve(plainFirst));
    }

    [Fact]
    public void ASwapStampedAboveWhatTheNodeReads_IsUnreadable()
    {
        ShouldRefuse(
            Resolve(Security("Swap", Json(Flat(3, SwapBody + ",\"legs\":[" + PlainLeg + "]")))),
            DeclaredScheduleRefusalCode.TermsVersionUnreadable,
            "Declared schedule conventions cannot be read: Unsupported assetClassTermsVersion '3' for asset class 'Swap'; this node reads versions 1 through 2.");
    }

    [Fact]
    public void ADeclaringBondWithUngovernedLegs_IsNotFollowed()
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false}""", body: BondBody + ",\"cashFlowLegs\":[" + PlainLeg + "]")),
            DeclaredScheduleRefusalCode.LegConventionsNotFollowed,
            LegsNotFollowed);
    }

    // ── (k) Ambiguity ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AProfileFieldsIssueDate_DisagreeingWithTheRoot_IsAmbiguous()
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false}""", body: BondBody + ",\"profileFields\":{\"issueDate\":\"2026-02-01\"}")),
            DeclaredScheduleRefusalCode.TermsAmbiguous,
            "Declared schedule conventions need one unambiguous issue date; the record's 'issueDate' is '2026-01-31' but the cash-flow terms resolve '2026-02-01' from another key or source.");
    }

    [Fact]
    public void AStrayMaturityDate_IsAmbiguous()
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false}""", body: BondBody + ",\"maturityDate\":\"2027-02-28\"")),
            DeclaredScheduleRefusalCode.TermsAmbiguous,
            "Declared schedule conventions need one unambiguous maturity; the record's 'maturity' is '2027-01-31' but the cash-flow terms resolve '2027-02-28' from another key or source.");
    }

    [Fact]
    public void ACommonTermsCouponFrequency_IsAmbiguous_BeforeCadence()
    {
        var security = Security(
            "Bond",
            Json(Flat(2, BondWith(frequency: null) + ",\"scheduleConventions\":{\"accrualAdjusted\":false}")),
            Json("""{"displayName":"Declared schedule","currency":"USD","couponFrequency":"Quarterly"}"""));

        ShouldRefuse(
            Resolve(security),
            DeclaredScheduleRefusalCode.TermsAmbiguous,
            "Declared schedule conventions need one unambiguous payment frequency; the record carries no 'paymentFrequency' but the cash-flow terms resolve 'Quarterly' from another key or source.");
    }

    [Fact]
    public void AProfileFieldsPaymentFrequency_DisagreeingWithTheRoot_IsAmbiguous()
    {
        ShouldRefuse(
            Resolve(Bond("""{"accrualAdjusted":false}""", body: BondBody + ",\"profileFields\":{\"paymentFrequency\":\"Monthly\"}")),
            DeclaredScheduleRefusalCode.TermsAmbiguous,
            "Declared schedule conventions need one unambiguous payment frequency; the record's 'paymentFrequency' is 'Quarterly' but the cash-flow terms resolve 'Monthly' from another key or source.");
    }

    // A root anchor of a kind the exact getters do not read, which the cash-flow terms read from the
    // same key: the refusal names the record's value, not another key or source.
    public static TheoryData<string, string> UnreadableRootAnchorCases => new()
    {
        {
            BondWith(frequency: null) + ",\"paymentFrequency\":4",
            "Declared schedule conventions need one unambiguous payment frequency; the record's 'paymentFrequency' is 4, which is not a string the schedule anchors read, but the cash-flow terms resolve '4'."
        },
        {
            BondWith(frequency: null) + ",\"paymentFrequency\":true",
            "Declared schedule conventions need one unambiguous payment frequency; the record's 'paymentFrequency' is true, which is not a string the schedule anchors read, but the cash-flow terms resolve 'True'."
        },
        {
            BondWith(maturity: null) + ",\"maturity\":\"2027-01-31T00:00:00Z\"",
            "Declared schedule conventions need one unambiguous maturity; the record's 'maturity' is '2027-01-31T00:00:00Z', which is not a date the schedule anchors read, but the cash-flow terms resolve '2027-01-31'."
        },
        {
            BondWith(issue: null) + ",\"issueDate\":\"2026-01-31T00:00:00Z\"",
            "Declared schedule conventions need one unambiguous issue date; the record's 'issueDate' is '2026-01-31T00:00:00Z', which is not a date the schedule anchors read, but the cash-flow terms resolve '2026-01-31'."
        }
    };

    [Theory]
    [MemberData(nameof(UnreadableRootAnchorCases))]
    public void ARootAnchorTheGettersCannotRead_IsAmbiguous_WithoutBlamingAnotherKey(string body, string message)
    {
        var security = Bond("""{"accrualAdjusted":false}""", body: body);

        ShouldRefuse(Resolve(security), DeclaredScheduleRefusalCode.TermsAmbiguous, message);
        ShouldRefuse(Shape(security), DeclaredScheduleRefusalCode.TermsAmbiguous, message);
    }

    [Fact]
    public void ARootIssueDateNothingReads_IsRefusedNamingTheRecordsValue_NotAsAbsent()
    {
        var security = Bond("""{"accrualAdjusted":false}""", body: BondWith(issue: null) + ",\"issueDate\":20260131");
        StructuredCashFlowTermsResolver.Resolve(security).IssueDate.Should().BeNull();
        const string message =
            "Declared schedule conventions need the contractual issue date; the record's 'issueDate' is 20260131, which is not a date the schedule anchors read, and its effective-from date is not used as a schedule anchor.";

        ShouldRefuse(Resolve(security), DeclaredScheduleRefusalCode.EffectiveDateMissing, message);
        ShouldRefuse(Shape(security), DeclaredScheduleRefusalCode.EffectiveDateMissing, message);
    }

    [Fact]
    public void AnAgreeingDuplicate_IsNotAmbiguous()
    {
        DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false}""", body: BondBody + ",\"profileFields\":{\"issueDate\":\"2026-01-31\",\"paymentFrequency\":\"Quarterly\"}")));
    }

    // ── (l) Format ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnUntaggedBondCarryingAProfileShape_IsDeclared()
    {
        // ResolveFormat reads this row as a custom profile, but a Bond is not profile-governed: the
        // mapping decodes its root block by its own stamp, and so does the factory.
        var security = Bond("""{"accrualAdjusted":false}""", body: BondBody + ",\"customProfileId\":\"bond-profile\",\"profileFields\":{\"issuerName\":\"ACME Corp\"}");
        SecurityTermsEnvelope.ResolveFormat(security.AssetSpecificTerms).Should().Be(SecurityTermsFormats.CustomProfile);

        DeclaredSpec(Resolve(security)).Should().Be(DeclaredSpec(Resolve(Bond("""{"accrualAdjusted":false}"""))));
    }

    // ── (m) Totality ────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string?, DeclaredScheduleRefusalCode?> TotalityRows => new()
    {
        { "Bond", "[]", null },
        { "Bond", "\"x\"", null },
        { "Bond", null, null },
        { "Swap", "[1,2]", null },
        { "Swap", null, null },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":5"), null },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":\"Following\""), null },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":[{\"accrualAdjusted\":false}]"), null },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":{\"paymentCalendars\":{\"USNY\":true}}"), null },
        { "Bond", Flat(2, BondBody + ",\"scheduleConventions\":" + GarbageBlock), null },
        { "Bond", Flat(2, "\"scheduleConventions\":{\"accrualAdjusted\":5,\"paymentLagBusinessDays\":1e10}"), null },
        { "Bond", "{\"assetClassTermsVersion\":\"3\"," + BondBody + ",\"scheduleConventions\":{\"accrualAdjusted\":false}}", null },
        { "Swap", Flat(2, SwapBody + ",\"legs\":{\"scheduleConventions\":{\"accrualAdjusted\":false}}"), null },
        { "Swap", Flat(2, SwapBody + ",\"legs\":7,\"cashFlowLegs\":[" + DeclaringLeg + "]"), null },
        { "Swap", Flat(2, SwapBody + ",\"legs\":[1,\"x\",null,[" + DeclaringLeg + "],{\"scheduleConventions\":[]}]"), null },
        { "Bond", Flat(99, BondBody + ",\"scheduleConventions\":{\"accrualAdjusted\":false}"), DeclaredScheduleRefusalCode.TermsVersionUnreadable },
        { "Bond", Flat(0, BondBody), DeclaredScheduleRefusalCode.TermsVersionUnreadable },
        { "Bond", Flat(-3, BondBody), DeclaredScheduleRefusalCode.TermsVersionUnreadable },
        { "Swap", Flat(3, SwapBody + ",\"legs\":7"), DeclaredScheduleRefusalCode.TermsVersionUnreadable },
        { "StructuredCredit", Flat(5, "\"tranche\":\"A-1\""), DeclaredScheduleRefusalCode.TermsVersionUnreadable },
        { "Bond", "{\"termsFormat\":\"Economic\",\"assetClassTermsVersion\":9,\"scheduleConventions\":{\"accrualAdjusted\":false}}", null },
        { "NotAClass", Flat(2, BondBody + ",\"scheduleConventions\":{\"accrualAdjusted\":false}"), null },
    };

    [Theory]
    [MemberData(nameof(TotalityRows))]
    public void BothEntryPoints_AreTotal(string assetClass, string? terms, DeclaredScheduleRefusalCode? refusal)
    {
        var security = Security(assetClass, terms is null ? default : Json(terms));
        var garbageCommon = security with { CommonTerms = default };

        foreach (var candidate in new[] { security, garbageCommon })
        {
            foreach (var resolved in new[] { StructuredCashFlowTerms.Empty, StructuredCashFlowTermsResolver.Resolve(candidate) })
            {
                foreach (var resolution in new[]
                         {
                             ScheduleSpecFactory.Resolve(candidate, resolved, Calendars),
                             ScheduleSpecFactory.Resolve(candidate, resolved, calendars: null),
                             ScheduleSpecFactory.ResolveShape(candidate, resolved),
                         })
                {
                    if (refusal is { } code)
                    {
                        resolution.Status.Should().Be(DeclaredScheduleStatus.Refused);
                        resolution.Refusal!.Code.Should().Be(code);
                    }
                    else
                    {
                        ShouldBeUndeclared(resolution);
                    }
                }
            }
        }
    }

    [Fact]
    public void NullArguments_AreTheOnlyThrows()
    {
        var security = Bond("""{"accrualAdjusted":false}""");

        FluentActions.Invoking(() => ScheduleSpecFactory.Resolve(null!, StructuredCashFlowTerms.Empty, Calendars)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ScheduleSpecFactory.Resolve(security, null!, Calendars)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ScheduleSpecFactory.ResolveShape(null!, StructuredCashFlowTerms.Empty)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ScheduleSpecFactory.ResolveShape(security, null!)).Should().Throw<ArgumentNullException>();
    }

    // ── (n) ResolveShape ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Shape_RefusesARollTheAnchorIsNotOn()
    {
        var security = Bond("""{"accrualAdjusted":false,"rollConvention":"EOM"}""", body: BondWith(issue: "2041-05-30", maturity: "2046-05-30"));

        ShouldRefuse(
            Shape(security),
            DeclaredScheduleRefusalCode.KernelRefused,
            "Declared schedule could not be generated: The generation anchor 2041-05-30 is not on the EndOfMonth roll.");
        DeclaredSpec(Resolve(security)).Roll.Should().Be(ScheduleRoll.EndOfMonth, "Resolve maps the block; the engine runs the kernel");
    }

    [Fact]
    public void Shape_RefusesALastRegularDateAloneGoingForward()
    {
        ShouldRefuse(
            Shape(Bond("""{"accrualAdjusted":false,"lastRegularDate":"2026-12-15"}""")),
            DeclaredScheduleRefusalCode.KernelRefused,
            "Declared schedule could not be generated: A first regular date requires forward generation; a last regular date alone requires backward generation.");
    }

    [Fact]
    public void Shape_RefusesAScheduleBeyondThePeriodLimit()
    {
        ShouldRefuse(
            Shape(Bond("""{"accrualAdjusted":false}""", body: BondWith(maturity: "9999-12-31"))),
            DeclaredScheduleRefusalCode.KernelRefused,
            "Declared schedule could not be generated: The schedule would exceed 1200 periods.");
    }

    [Fact]
    public void Shape_DoesNotResolveCalendars()
    {
        var security = Bond("""{"businessDayConvention":"MF","accrualAdjusted":true,"paymentLagBusinessDays":2,"paymentCalendars":["ZZNONE"]}""");

        var shape = Shape(security);
        var spec = DeclaredSpec(shape);
        spec.AccrualAdjustment.Should().BeNull();
        spec.PaymentAdjustment.Should().BeNull();
        spec.PaymentLagBusinessDays.Should().Be(0);
        shape.Conventions.PaymentCalendars.Should().Equal("ZZNONE");
        Resolve(security).Refusal!.Code.Should().Be(DeclaredScheduleRefusalCode.CalendarUnresolvable);
    }

    // ── (o) Determinism ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoResolutions_GenerateEqualSchedules()
    {
        var security = Bond("""{"businessDayConvention":"MF","accrualAdjusted":true,"paymentLagBusinessDays":2,"paymentCalendars":["USNY","GBLO"],"rollConvention":"EOM"}""");

        var first = ScheduleGenerator.Generate(DeclaredSpec(Resolve(security)));
        var second = ScheduleGenerator.Generate(DeclaredSpec(Resolve(security)));

        first.Succeeded.Should().BeTrue(first.Failure?.Message);
        first.Periods.Should().HaveCount(4);
        second.Periods.Should().BeEquivalentTo(first.Periods, options => options.WithStrictOrdering());
        second.CalendarsUsed.Should().Equal(first.CalendarsUsed);
        Shape(security).Should().BeEquivalentTo(Shape(security));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private const string AllNullBlock =
        """{"businessDayConvention":null,"accrualAdjusted":null,"paymentCalendars":null,"paymentLagBusinessDays":null,"rollConvention":null,"endOfMonthRule":null,"firstRegularDate":null,"lastRegularDate":null,"direction":null,"stubPolicy":null,"couponRateUnit":null,"couponCashBasis":null}""";

    // Every member with a JSON kind the reader does not take, so the block reads as empty.
    private const string GarbageBlock =
        """{"businessDayConvention":{"x":[1]},"accrualAdjusted":"yes","paymentCalendars":[1,{"a":null},[" "],null,"  "],"paymentLagBusinessDays":2.5,"rollConvention":15,"endOfMonthRule":"true","firstRegularDate":20260131,"lastRegularDate":{"d":1},"direction":["Forward"],"stubPolicy":null,"couponRateUnit":true,"couponCashBasis":0,"BusinessDayConvention":"MF"}""";

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string Flat(int? stamp, string body)
        => "{" + (stamp is { } version ? $"\"assetClassTermsVersion\":{version}," : string.Empty) + body + "}";

    // The Bond body with its anchors replaced; a null leaves the member out.
    private static string BondWith(string? issue = "2026-01-31", string? maturity = "2027-01-31", string? frequency = "Quarterly")
    {
        var members = new List<string> { BondEconomics };
        if (maturity is not null)
        {
            members.Add($"\"maturity\":\"{maturity}\"");
        }

        if (issue is not null)
        {
            members.Add($"\"issueDate\":\"{issue}\"");
        }

        if (frequency is not null)
        {
            members.Add($"\"paymentFrequency\":{JsonSerializer.Serialize(frequency)}");
        }

        return string.Join(",", members);
    }

    private static SecurityDetailDto Security(string assetClass, JsonElement assetSpecificTerms, JsonElement? commonTerms = null)
        => new(
            SecurityId,
            assetClass,
            SecurityStatusDto.Active,
            $"Declared schedule {assetClass}",
            "USD",
            commonTerms ?? Json("""{"displayName":"Declared schedule","currency":"USD"}"""),
            assetSpecificTerms,
            [],
            [],
            1,
            new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
            null);

    private static SecurityDetailDto Bond(string block, int? stamp = 2, string body = BondBody)
        => Security("Bond", Json(Flat(stamp, body + ",\"scheduleConventions\":" + block)));

    private static DeclaredScheduleResolution Resolve(SecurityDetailDto security)
        => Resolve(security, Calendars);

    private static DeclaredScheduleResolution Resolve(SecurityDetailDto security, IBusinessDayCalendarSource? calendars)
        => ScheduleSpecFactory.Resolve(security, StructuredCashFlowTermsResolver.Resolve(security), calendars);

    private static DeclaredScheduleResolution Shape(SecurityDetailDto security)
        => ScheduleSpecFactory.ResolveShape(security, StructuredCashFlowTermsResolver.Resolve(security));

    private static ScheduleSpec DeclaredSpec(DeclaredScheduleResolution resolution)
    {
        resolution.Status.Should().Be(DeclaredScheduleStatus.Declared, resolution.Refusal?.Message);
        resolution.Refusal.Should().BeNull();
        resolution.IsDeclared.Should().BeTrue();
        resolution.Conventions.IsEmpty.Should().BeFalse("a declared record carries the block it declared");
        return resolution.Spec!;
    }

    private static void ShouldRefuse(DeclaredScheduleResolution resolution, DeclaredScheduleRefusalCode code, string message)
    {
        resolution.Status.Should().Be(DeclaredScheduleStatus.Refused, resolution.Refusal?.Message ?? "the resolution was not refused");
        resolution.IsDeclared.Should().BeTrue("a refused record leaves the legacy path");
        resolution.Spec.Should().BeNull();
        resolution.Refusal.Should().Be(new DeclaredScheduleRefusal(code, message));
    }

    private static void ShouldBeUndeclared(DeclaredScheduleResolution resolution)
    {
        resolution.Status.Should().Be(DeclaredScheduleStatus.Undeclared, resolution.Refusal?.Message);
        resolution.IsDeclared.Should().BeFalse();
        resolution.Spec.Should().BeNull();
        resolution.Refusal.Should().BeNull();
        resolution.Conventions.Should().BeSameAs(ScheduleConventionTerms.Empty);
    }

    private static IEnumerable<(DateOnly Start, DateOnly End)> Unadjusted(ScheduleGenerationResult result)
        => result.Periods.Select(static period => (period.UnadjustedStart, period.UnadjustedEnd));
}
