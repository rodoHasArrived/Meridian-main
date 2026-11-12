using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingServicingPolicyValidatorTests
{
    private static readonly DateOnly Origination = new(2026, 1, 15);
    private static readonly DateOnly Maturity = new(2029, 1, 15);

    [Fact]
    public void Validate_AcceptsTermsWithoutPolicies()
    {
        DirectLendingServicingPolicyValidator.Validate(FloatingTerms()).Should().BeNull();
    }

    [Fact]
    public void Validate_AcceptsAWellFormedScheduleAndPolicy()
    {
        var terms = FloatingTerms() with
        {
            RateResetSchedule = Schedule(),
            PikCapitalization = new PikCapitalizationPolicy(PikElection.WhenToggled, DeclaredRate.Percent(50m), PikCapitalizationFrequency.Quarterly, new DateOnly(2026, 4, 15))
        };

        DirectLendingServicingPolicyValidator.Validate(terms).Should().BeNull();
    }

    [Fact]
    public void Validate_RefusesAScheduleOnFixedRateTerms()
    {
        var terms = FloatingTerms() with { RateTypeKind = RateTypeKind.Fixed, FixedAnnualRate = 0.08m, RateResetSchedule = Schedule() };

        DirectLendingServicingPolicyValidator.Validate(terms).Should().Contain("floating-rate");
    }

    [Fact]
    public void Validate_RefusesAScheduleForAnotherIndex()
    {
        var terms = FloatingTerms() with { RateResetSchedule = Schedule() with { IndexName = "EURIBOR" } };

        DirectLendingServicingPolicyValidator.Validate(terms).Should().Contain("must match");
    }

    [Fact]
    public void Validate_RefusesACapBelowTheFloor()
    {
        var terms = FloatingTerms() with
        {
            RateResetSchedule = Schedule() with { Floor = DeclaredRate.Percent(3m), Cap = DeclaredRate.BasisPoints(200m) }
        };

        DirectLendingServicingPolicyValidator.Validate(terms).Should().Contain("cap");
    }

    [Fact]
    public void Validate_RefusesResetDatesOutsideTheLoanLife()
    {
        var terms = FloatingTerms() with
        {
            RateResetSchedule = Schedule() with { ResetDates = [Maturity.AddDays(1)] }
        };

        DirectLendingServicingPolicyValidator.Validate(terms).Should().NotBeNull();
    }

    [Fact]
    public void Validate_RefusesAPikShareAboveOneHundredPercent()
    {
        var terms = FloatingTerms() with
        {
            PikCapitalization = new PikCapitalizationPolicy(PikElection.Always, DeclaredRate.Percent(150m), PikCapitalizationFrequency.Monthly, Origination)
        };

        DirectLendingServicingPolicyValidator.Validate(terms).Should().Contain("PIK share");
    }

    [Fact]
    public void Validate_RefusesANegativeLookback()
    {
        var terms = FloatingTerms() with { RateResetSchedule = Schedule() with { LookbackDays = -1 } };

        DirectLendingServicingPolicyValidator.Validate(terms).Should().Contain("lookback");
    }

    private static FloatingRateResetSchedule Schedule() =>
        new("SOFR", RateResetFrequency.Quarterly, Origination, 2, LookbackDayKind.Business, DeclaredRate.BasisPoints(250m), DeclaredRate.Percent(1m));

    private static DirectLendingTermsDto FloatingTerms() =>
        new(
            OriginationDate: Origination,
            MaturityDate: Maturity,
            CommitmentAmount: 1_000_000m,
            BaseCurrency: CurrencyCode.USD,
            RateTypeKind: RateTypeKind.Floating,
            FixedAnnualRate: null,
            InterestIndexName: "SOFR",
            SpreadBps: 250m,
            FloorRate: null,
            CapRate: null,
            DayCountBasis: DayCountBasis.Act360,
            PaymentFrequency: PaymentFrequency.Quarterly,
            AmortizationType: AmortizationType.Bullet,
            CommitmentFeeRate: null,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: true,
            CovenantsJson: null);
}
