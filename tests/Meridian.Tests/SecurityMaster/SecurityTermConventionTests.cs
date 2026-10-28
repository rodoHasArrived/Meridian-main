using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.CertificatesOfDeposit;
using Meridian.Contracts.Deposits;
using Meridian.Contracts.Derivatives;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Futures;
using Meridian.Contracts.Options;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityTermConventionTests
{
    [Fact]
    public void DayCountFields_AreTypedConventions_OnEveryClassThatCarriesOne()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            if (SecurityAssetTermsSchema.Field(assetClass, "dayCount") is { } field)
            {
                field.Convention.Should().Be(SecurityTermConventionKind.DayCount, assetClass);
            }
        }
    }

    [Theory]
    [InlineData("Option")]
    [InlineData("Future")]
    public void DerivativeSettlementType_IsATypedConvention(string assetClass)
        => SecurityAssetTermsSchema.Field(assetClass, "settlementType")!.Convention
            .Should().Be(SecurityTermConventionKind.SettlementMethod);

    [Theory]
    [InlineData("Bond", "paymentFrequency")]
    [InlineData("CreditDefaultSwap", "paymentFrequency")]
    [InlineData("StructuredNote", "paymentFrequency")]
    [InlineData("Swaption", "fixedLegPaymentFrequency")]
    public void CouponPaymentFrequency_IsACouponFrequencyConvention(string assetClass, string key)
        => SecurityAssetTermsSchema.Field(assetClass, key)!.Convention
            .Should().Be(SecurityTermConventionKind.CouponFrequency);

    [Theory]
    [InlineData("CashSweep", "sweepFrequency")]
    [InlineData("DirectLoan", "resetFrequency")]
    public void CadenceFields_AreTypedPaymentFrequencyConventions(string assetClass, string key)
        => SecurityAssetTermsSchema.Field(assetClass, key)!.Convention
            .Should().Be(SecurityTermConventionKind.PaymentFrequency);

    [Fact]
    public void SwapLegPaymentFrequency_IsAScheduleDrivingCouponFrequency()
        => SecurityAssetTermsSchema.ElementFields("Swap", "legs")
            .Single(static field => field.Key == "paymentFrequency").Convention
            .Should().Be(SecurityTermConventionKind.CouponFrequency);

    [Theory]
    [InlineData("Monthly", true)]
    [InlineData("BiMonthly", true)]
    [InlineData("3M", true)]
    [InlineData("Semi-Annual", true)]
    [InlineData("Annual", true)]
    [InlineData("Bullet", true)]
    [InlineData("Daily", false)]
    [InlineData("Weekly", false)]
    [InlineData("fortnightly", false)]
    [InlineData("52", false)]
    [InlineData("Irregular", false)]
    public void CouponFrequency_AdmitsOnlyCadencesTheScheduleEnginesCanStep(string raw, bool recognized)
        => SecurityTermConventions.IsRecognized(SecurityTermConventionKind.CouponFrequency, raw).Should().Be(recognized);

    [Fact]
    public void CouponFrequency_CanonicalValues_AreTheSchedulableCadences()
        => SecurityTermConventions.CanonicalValues(SecurityTermConventionKind.CouponFrequency)
            .Should().Equal("Monthly", "BiMonthly", "Quarterly", "SemiAnnual", "Annual", "AtMaturity");

    [Theory]
    [InlineData("Monthly", 12)]
    [InlineData("2M", 6)]
    [InlineData("Quarterly", 4)]
    [InlineData("6M", 2)]
    [InlineData("annually", 1)]
    [InlineData("12", 12)]
    [InlineData("Weekly", null)]
    [InlineData("Bullet", null)]
    [InlineData("every other tuesday", null)]
    public void CouponPaymentsPerYear_ReadsMonthlyOrLongerCadences(string raw, int? expected)
        => SecurityTermConventions.CouponPaymentsPerYear(raw).Should().Be(expected);

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("Monthly", 12)]
    [InlineData("BiMonthly", 6)]
    [InlineData("Quarterly", 4)]
    [InlineData("3M", 4)]
    [InlineData("SemiAnnual", 2)]
    [InlineData("semi-annually", 2)]
    [InlineData("HALF_YEARLY", 2)]
    [InlineData("Annual", 1)]
    [InlineData("12M", 1)]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    [InlineData("13", 12)]
    [InlineData("52", 12)]
    [InlineData("0", 1)]
    [InlineData("-3", 3)]
    [InlineData("1.5", 1)]
    [InlineData("1.2", 12)]
    [InlineData("99999999999", 1)]
    [InlineData("Daily", 1)]
    [InlineData("Weekly", 1)]
    [InlineData("2W", 1)]
    [InlineData("4M", 1)]
    [InlineData("AtMaturity", 1)]
    [InlineData("Bullet", 1)]
    [InlineData("Irregular", 1)]
    [InlineData("every other tuesday", 1)]
    public void LegacyCouponPaymentsPerYear_ReadsStoredCadencesAsTheLegacySchedulersDid(string? raw, int expected)
        => SecurityTermConventions.LegacyCouponPaymentsPerYear(raw).Should().Be(expected);

    [Theory]
    [InlineData("Monthly")]
    [InlineData("1M")]
    [InlineData("12")]
    [InlineData("2M")]
    [InlineData("6")]
    [InlineData("Quarterly")]
    [InlineData("4")]
    [InlineData("half-yearly")]
    [InlineData("6M")]
    [InlineData("2")]
    [InlineData("annually")]
    [InlineData("1Y")]
    [InlineData("1")]
    public void LegacyCouponPaymentsPerYear_TakesTheConventionReading_WhereverTheConventionRecognizesTheSpelling(string raw)
        => SecurityTermConventions.LegacyCouponPaymentsPerYear(raw).Should().Be(SecurityTermConventions.CouponPaymentsPerYear(raw)!.Value);

    [Theory]
    [InlineData("Daily", SecurityPaymentFrequency.Daily)]
    [InlineData("Weekly", SecurityPaymentFrequency.Weekly)]
    [InlineData("fortnightly", SecurityPaymentFrequency.BiWeekly)]
    [InlineData("Monthly", SecurityPaymentFrequency.Monthly)]
    [InlineData("1M", SecurityPaymentFrequency.Monthly)]
    [InlineData("12", SecurityPaymentFrequency.Monthly)]
    [InlineData("Quarterly", SecurityPaymentFrequency.Quarterly)]
    [InlineData("3M", SecurityPaymentFrequency.Quarterly)]
    [InlineData("4", SecurityPaymentFrequency.Quarterly)]
    [InlineData("SemiAnnual", SecurityPaymentFrequency.SemiAnnual)]
    [InlineData("Semi-Annual", SecurityPaymentFrequency.SemiAnnual)]
    [InlineData("semiannually", SecurityPaymentFrequency.SemiAnnual)]
    [InlineData("half-yearly", SecurityPaymentFrequency.SemiAnnual)]
    [InlineData("6M", SecurityPaymentFrequency.SemiAnnual)]
    [InlineData("2", SecurityPaymentFrequency.SemiAnnual)]
    [InlineData("Annual", SecurityPaymentFrequency.Annual)]
    [InlineData("annually", SecurityPaymentFrequency.Annual)]
    [InlineData("Yearly", SecurityPaymentFrequency.Annual)]
    [InlineData("1Y", SecurityPaymentFrequency.Annual)]
    [InlineData("1", SecurityPaymentFrequency.Annual)]
    [InlineData("Bullet", SecurityPaymentFrequency.AtMaturity)]
    [InlineData("at maturity", SecurityPaymentFrequency.AtMaturity)]
    [InlineData("zero coupon", SecurityPaymentFrequency.AtMaturity)]
    [InlineData("Irregular", SecurityPaymentFrequency.Irregular)]
    public void PaymentFrequency_ParsesCommonSpellings(string raw, SecurityPaymentFrequency expected)
    {
        SecurityTermConventions.TryParsePaymentFrequency(raw, out var frequency).Should().BeTrue();
        frequency.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("every other tuesday")]
    [InlineData("13")]
    public void PaymentFrequency_RejectsUnrecognizedSpellings(string? raw)
        => SecurityTermConventions.TryParsePaymentFrequency(raw, out _).Should().BeFalse();

    [Fact]
    public void PaymentFrequency_CanonicalNames_RoundTripThroughTheParser()
    {
        foreach (var frequency in Enum.GetValues<SecurityPaymentFrequency>())
        {
            SecurityTermConventions.TryParsePaymentFrequency(frequency.ToString(), out var parsed).Should().BeTrue(frequency.ToString());
            parsed.Should().Be(frequency);
        }

        SecurityTermConventions.CanonicalValues(SecurityTermConventionKind.PaymentFrequency)
            .Should().Equal(Enum.GetNames<SecurityPaymentFrequency>());
    }

    [Theory]
    [InlineData(SecurityPaymentFrequency.Monthly, 12, 1)]
    [InlineData(SecurityPaymentFrequency.Quarterly, 4, 3)]
    [InlineData(SecurityPaymentFrequency.SemiAnnual, 2, 6)]
    [InlineData(SecurityPaymentFrequency.Annual, 1, 12)]
    [InlineData(SecurityPaymentFrequency.Weekly, 52, null)]
    [InlineData(SecurityPaymentFrequency.AtMaturity, null, null)]
    [InlineData(SecurityPaymentFrequency.Irregular, null, null)]
    public void PaymentFrequency_PeriodArithmetic(SecurityPaymentFrequency frequency, int? periodsPerYear, int? monthsPerPeriod)
    {
        frequency.PeriodsPerYear().Should().Be(periodsPerYear);
        frequency.MonthsPerPeriod().Should().Be(monthsPerPeriod);
    }

    [Fact]
    public void DirectLendingPaymentFrequency_MapsOntoTheSecurityMasterVocabulary()
    {
        foreach (var loanFrequency in Enum.GetValues<Meridian.Contracts.DirectLending.PaymentFrequency>())
        {
            var mapped = loanFrequency.ToSecurityPaymentFrequency();

            // The loan enum's own spelling is a recognized Security Master spelling of the same cadence.
            SecurityTermConventions.TryParsePaymentFrequency(loanFrequency.ToString(), out var parsed).Should().BeTrue(loanFrequency.ToString());
            parsed.Should().Be(mapped);
        }

        Meridian.Contracts.DirectLending.PaymentFrequency.Bullet.ToSecurityPaymentFrequency()
            .Should().Be(SecurityPaymentFrequency.AtMaturity);
    }

    [Fact]
    public void FieldEdit_RejectsAnUnrecognizedPaymentFrequency_AndAcceptsARecognizedSpelling()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.paymentFrequency", "every other tuesday", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("coupon frequency");

        // A recognized cadence the coupon scheduler cannot step is refused on a schedule-driving field.
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.paymentFrequency", "Weekly", out _, out _)
            .Should().BeFalse();
        SecurityAssetTermsFieldEditValidator.TryValidate("CashSweep", "assetSpecificTerms.sweepFrequency", "Weekly", out _, out _)
            .Should().BeTrue();

        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.paymentFrequency", "Semi-Annual", out _, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void PaymentFrequencyHints_OfferTheCanonicalSpellings()
    {
        var hint = SecurityAssetClassRegistry.Get("Bond").UiHints.Single(static hint => hint.Key == "paymentFrequency");

        hint.Editor.Should().Be(SecurityAssetTermEditorKind.Convention);
        hint.Choices.Should().Contain(SecurityPaymentFrequency.SemiAnnual.ToString());
    }

    [Fact]
    public void ReferenceDtos_ExposeTypedCompanions_AndKeepTheRawText()
    {
        var future = new FutureReferenceDto(Guid.NewGuid(), "ESZ6", "USD", "ES", "2026-12", new DateOnly(2026, 12, 18), 50m,
            "cash settled", false, null, null, null, null, null, FutureLifecycleStat.Active, "ESZ6", 1);
        future.SettlementMethod.Should().Be(SecuritySettlementMethod.Cash);
        future.SettlementType.Should().Be("cash settled");

        var deposit = new DepositReferenceDto(Guid.NewGuid(), "Term deposit", "USD", "Term", "Bank", null, 0.04m, "ACT/360", false, "DEP-1", 1);
        deposit.DayCountConvention.Should().Be(DayCountConvention.Actual360);

        var cd = new CertificateOfDepositReferenceDto(Guid.NewGuid(), "CD", "USD", "Bank", new DateOnly(2027, 1, 1), 0.045m, null, "30/360", "CD-1", 1);
        cd.DayCountConvention.Should().Be(DayCountConvention.Thirty360);

        var option = new OptionContractReferenceDto("SPY260117P00450000", null, "SPY-2026-01-17", "SPY", null, "P", 450m,
            new DateOnly(2026, 1, 17), 100m, false, null, 1);
        option.Right.Should().Be(SecurityOptionRight.Put);

        var lifecycle = new BondLifecycleDto(Guid.NewGuid(), BondLifecycleStat.Active, null, null, new DateOnly(2030, 1, 1), false, 1,
            PaymentFrequency: "Semi-Annual");
        lifecycle.PaymentCadence.Should().Be(SecurityPaymentFrequency.SemiAnnual);
        lifecycle.PaymentFrequency.Should().Be("Semi-Annual");

        var accrual = new BondAccrualConventionDto(Guid.NewGuid(), "ACT/ACT", null, null, "Fixed", 0.05m, null, null, 1);
        accrual.DayCount.Should().Be(DayCountConvention.ActualActualIsda);

        var leg = new SwapLegDto("Fixed", "USD", null, 0.035m, "3M", "30/360");
        leg.PaymentCadence.Should().Be(SecurityPaymentFrequency.Quarterly);
        leg.DayCountConvention.Should().Be(DayCountConvention.Thirty360);

        // Unrecognized text is kept, and the typed companion reads null rather than guessing.
        var odd = new SwapLegDto("Fixed", "USD", null, null, "every other tuesday", "lunar");
        odd.PaymentFrequency.Should().Be("every other tuesday");
        odd.PaymentCadence.Should().BeNull();
        odd.DayCountConvention.Should().BeNull();
    }

    [Theory]
    [InlineData("Cash", SecuritySettlementMethod.Cash)]
    [InlineData("cash settled", SecuritySettlementMethod.Cash)]
    [InlineData("Physical Delivery", SecuritySettlementMethod.Physical)]
    [InlineData("Delivery", SecuritySettlementMethod.Physical)]
    [InlineData("Elective", SecuritySettlementMethod.Elective)]
    public void SettlementMethod_ParsesCommonSpellings(string raw, SecuritySettlementMethod expected)
    {
        SecurityTermConventions.TryParseSettlementMethod(raw, out var method).Should().BeTrue();
        method.Should().Be(expected);
    }

    [Fact]
    public void Readers_ReturnTypedValues()
    {
        var terms = JsonSerializer.SerializeToElement(new { dayCount = "ACT/360", settlementType = "Physical", putCall = "Put", paymentFrequency = "Quarterly" });

        SecurityTermConventions.ReadDayCount(terms).Should().Be(DayCountConvention.Actual360);
        SecurityTermConventions.ReadPaymentFrequency(terms).Should().Be(SecurityPaymentFrequency.Quarterly);
        SecurityTermConventions.ReadSettlementMethod(terms).Should().Be(SecuritySettlementMethod.Physical);
        SecurityTermConventions.ReadOptionRight(terms).Should().Be(SecurityOptionRight.Put);
    }

    [Fact]
    public void FieldEdit_RejectsAnUnrecognizedDayCount_AndAcceptsARecognizedSpelling()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.dayCount", "every other tuesday", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("day-count convention");

        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.dayCount", "30/360", out _, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void ConventionHints_OfferTheCanonicalSpellings()
    {
        var hint = SecurityAssetClassRegistry.Get("Bond").UiHints.Single(static hint => hint.Key == "dayCount");

        hint.Editor.Should().Be(SecurityAssetTermEditorKind.Convention);
        hint.Choices.Should().Contain(DayCountConvention.Actual360.ToString()).And.NotContain(DayCountConvention.Unknown.ToString());
    }

    // ── Schedule conventions (the scheduleConventions block, ADR-024 and ADR-025) ───────────────

    [Theory]
    [InlineData("Unadjusted", BusinessDayConvention.Unadjusted)]
    [InlineData("NONE", BusinessDayConvention.Unadjusted)]
    [InlineData("F", BusinessDayConvention.Following)]
    [InlineData("following", BusinessDayConvention.Following)]
    [InlineData("MF", BusinessDayConvention.ModifiedFollowing)]
    [InlineData("MODFOLLOWING", BusinessDayConvention.ModifiedFollowing)]
    [InlineData("Modified Following", BusinessDayConvention.ModifiedFollowing)]
    [InlineData("P", BusinessDayConvention.Preceding)]
    [InlineData("MP", BusinessDayConvention.ModifiedPreceding)]
    [InlineData("mod-preceding", BusinessDayConvention.ModifiedPreceding)]
    public void BusinessDayConvention_ParsesTheCanonicalNamesAndCommonAbbreviations(string raw, BusinessDayConvention expected)
    {
        SecurityTermConventions.TryParseBusinessDayConvention(raw, out var convention).Should().BeTrue();
        convention.Should().Be(expected);
        SecurityTermConventions.IsRecognized(SecurityTermConventionKind.BusinessDayConvention, raw).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NEAREST")]
    [InlineData("FRN")]
    [InlineData("Modified")]
    public void BusinessDayConvention_RefusesConventionsTheKernelCannotAdjust(string? raw)
    {
        SecurityTermConventions.TryParseBusinessDayConvention(raw, out _).Should().BeFalse();
        SecurityTermConventions.IsRecognized(SecurityTermConventionKind.BusinessDayConvention, raw).Should().BeFalse();
    }

    [Theory]
    [InlineData("AnchorDay", ScheduleRollKind.AnchorDay, 0)]
    [InlineData("Anchor", ScheduleRollKind.AnchorDay, 0)]
    [InlineData("EndOfMonth", ScheduleRollKind.EndOfMonth, 0)]
    [InlineData("EOM", ScheduleRollKind.EndOfMonth, 0)]
    [InlineData("end of month", ScheduleRollKind.EndOfMonth, 0)]
    [InlineData("Imm", ScheduleRollKind.Imm, 0)]
    [InlineData("IMM", ScheduleRollKind.Imm, 0)]
    [InlineData("1", ScheduleRollKind.FixedDay, 1)]
    [InlineData("15", ScheduleRollKind.FixedDay, 15)]
    [InlineData(" 15 ", ScheduleRollKind.FixedDay, 15)]
    [InlineData("31", ScheduleRollKind.FixedDay, 31)]
    [InlineData("FixedDay(15)", ScheduleRollKind.FixedDay, 15)]
    [InlineData("fixedday( 31 )", ScheduleRollKind.FixedDay, 31)]
    public void RollConvention_ParsesNamedRollsAndFixedDaysOfTheMonth(string raw, ScheduleRollKind kind, int dayOfMonth)
    {
        SecurityTermConventions.TryParseRollConvention(raw, out var roll).Should().BeTrue();
        roll.Kind.Should().Be(kind);
        roll.DayOfMonth.Should().Be(dayOfMonth);
        SecurityTermConventions.IsRecognized(SecurityTermConventionKind.RollConvention, raw).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("1.5")]
    [InlineData("-3")]
    [InlineData("+3")]
    [InlineData("0")]
    [InlineData("32")]
    [InlineData("99999999999")]
    [InlineData("FixedDay(0)")]
    [InlineData("FixedDay(32)")]
    [InlineData("FixedDay(1.5)")]
    [InlineData("FixedDay()")]
    [InlineData("FixedDay15")]
    [InlineData("NEAREST")]
    public void RollConvention_RefusesAnythingButANamedRollOrAWholeDayFromOneToThirtyOne(string? raw)
    {
        // A day of the month is read from the trimmed text itself, so 1.5 and -3 are not read as 15 and 3.
        SecurityTermConventions.TryParseRollConvention(raw, out _).Should().BeFalse();
        SecurityTermConventions.IsRecognized(SecurityTermConventionKind.RollConvention, raw).Should().BeFalse();
    }

    [Theory]
    [InlineData("Short", ScheduleStubPolicy.Short)]
    [InlineData("ShortStub", ScheduleStubPolicy.Short)]
    [InlineData("short stub", ScheduleStubPolicy.Short)]
    [InlineData("Long", ScheduleStubPolicy.Long)]
    [InlineData("LONGSTUB", ScheduleStubPolicy.Long)]
    [InlineData("RequireRegular", ScheduleStubPolicy.RequireRegular)]
    [InlineData("Regular", ScheduleStubPolicy.RequireRegular)]
    [InlineData("NoStub", ScheduleStubPolicy.RequireRegular)]
    [InlineData("no-stub", ScheduleStubPolicy.RequireRegular)]
    public void StubPolicy_ParsesThePolicyWithoutAPosition(string raw, ScheduleStubPolicy expected)
    {
        SecurityTermConventions.TryParseStubPolicy(raw, out var policy).Should().BeTrue();
        policy.Should().Be(expected);
        SecurityTermConventions.IsRecognized(SecurityTermConventionKind.StubPolicy, raw).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ShortInitial")]
    [InlineData("ShortFinal")]
    [InlineData("LongInitial")]
    [InlineData("LongFinal")]
    [InlineData("None")]
    public void StubPolicy_RefusesSpellingsThatAlsoNameThePosition(string? raw)
    {
        // The stub's position follows from the generation direction, so FpML's positional spellings are refused.
        SecurityTermConventions.TryParseStubPolicy(raw, out _).Should().BeFalse();
        SecurityTermConventions.IsRecognized(SecurityTermConventionKind.StubPolicy, raw).Should().BeFalse();
    }

    [Fact]
    public void ScheduleConventionKinds_CanonicalValues_AreTheKernelSpellings()
    {
        string[] rolls = ["AnchorDay", "EndOfMonth", "Imm", .. Enumerable.Range(1, 31).Select(static day => day.ToString(CultureInfo.InvariantCulture))];

        SecurityTermConventions.CanonicalValues(SecurityTermConventionKind.BusinessDayConvention)
            .Should().Equal("Unadjusted", "Following", "ModifiedFollowing", "Preceding", "ModifiedPreceding");
        SecurityTermConventions.CanonicalValues(SecurityTermConventionKind.RollConvention).Should().Equal(rolls);
        SecurityTermConventions.CanonicalValues(SecurityTermConventionKind.StubPolicy)
            .Should().Equal("Short", "Long", "RequireRegular");
    }

    [Fact]
    public void CanonicalValues_RoundTripThroughIsRecognized_ForEveryKind()
    {
        foreach (var kind in Enum.GetValues<SecurityTermConventionKind>())
        {
            foreach (var value in SecurityTermConventions.CanonicalValues(kind))
            {
                SecurityTermConventions.IsRecognized(kind, value).Should().BeTrue($"'{value}' is a canonical {kind} spelling");
            }
        }

        // The schedule kinds' canonical spellings also parse to the value they name.
        foreach (var convention in Enum.GetValues<BusinessDayConvention>())
        {
            SecurityTermConventions.TryParseBusinessDayConvention(convention.ToString(), out var parsed).Should().BeTrue();
            parsed.Should().Be(convention);
        }

        foreach (var policy in Enum.GetValues<ScheduleStubPolicy>())
        {
            SecurityTermConventions.TryParseStubPolicy(policy.ToString(), out var parsed).Should().BeTrue();
            parsed.Should().Be(policy);
        }

        foreach (var value in SecurityTermConventions.CanonicalValues(SecurityTermConventionKind.RollConvention))
        {
            var expected = value switch
            {
                "AnchorDay" => ScheduleRoll.AnchorDay,
                "EndOfMonth" => ScheduleRoll.EndOfMonth,
                "Imm" => ScheduleRoll.Imm,
                _ => ScheduleRoll.FixedDay(int.Parse(value, CultureInfo.InvariantCulture))
            };
            SecurityTermConventions.TryParseRollConvention(value, out var roll).Should().BeTrue(value);
            roll.Should().Be(expected, value);
        }
    }

    [Fact]
    public void ScheduleConventionMembers_CheckTheirConventions_AndLeaveBlanksToTheOptionalRule()
    {
        var fields = SecurityAssetTermsSchema.ElementFields("Bond", "scheduleConventions");
        SecurityAssetTermField Member(string key) => fields.Single(field => field.Key == key);

        Member("businessDayConvention").SatisfiesConvention("MF").Should().BeTrue();
        Member("businessDayConvention").SatisfiesConvention("NEAREST").Should().BeFalse();
        Member("rollConvention").SatisfiesConvention("FixedDay(15)").Should().BeTrue();
        Member("rollConvention").SatisfiesConvention("1.5").Should().BeFalse();
        Member("rollConvention").SatisfiesConvention(" ").Should().BeTrue();
        Member("stubPolicy").SatisfiesConvention("NoStub").Should().BeTrue();
        Member("stubPolicy").SatisfiesConvention("ShortInitial").Should().BeFalse();
    }

    [Fact]
    public void ScheduleConventionKinds_DescribeThemselvesInTheRejectionReason()
    {
        SecurityTermConventions.DescribeUnrecognized(SecurityTermConventionKind.BusinessDayConvention, "Bond", "businessDayConvention", "NEAREST")
            .Should().StartWith("Value 'NEAREST' is not a recognized business-day convention for 'businessDayConvention' on asset class 'Bond'. ")
            .And.EndWith("Recognized values include: Unadjusted, Following, ModifiedFollowing, Preceding, ModifiedPreceding.");
        SecurityTermConventions.DescribeUnrecognized(SecurityTermConventionKind.RollConvention, "Bond", "rollConvention", "1.5")
            .Should().StartWith(
                "Value '1.5' is not a recognized roll convention (AnchorDay, EndOfMonth, Imm or a day of the month from 1 to 31) " +
                "for 'rollConvention' on asset class 'Bond'. Recognized values include: AnchorDay, EndOfMonth, Imm, 1, 2, ");
        SecurityTermConventions.DescribeUnrecognized(SecurityTermConventionKind.StubPolicy, "Bond", "stubPolicy", "ShortInitial")
            .Should().Be("Value 'ShortInitial' is not a recognized stub policy for 'stubPolicy' on asset class 'Bond'. Recognized values include: Short, Long, RequireRegular.");
    }
}
