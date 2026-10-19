using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.CertificatesOfDeposit;
using Meridian.Contracts.Deposits;
using Meridian.Contracts.Derivatives;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Futures;
using Meridian.Contracts.Options;
using Meridian.Contracts.SecurityMaster;

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
    [InlineData("Bond")]
    [InlineData("CreditDefaultSwap")]
    public void CouponPaymentFrequency_IsACouponFrequencyConvention(string assetClass)
        => SecurityAssetTermsSchema.Field(assetClass, "paymentFrequency")!.Convention
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
}
