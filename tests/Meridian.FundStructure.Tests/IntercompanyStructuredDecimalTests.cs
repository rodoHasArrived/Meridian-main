using System.Globalization;
using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class IntercompanyStructuredDecimalTests
{
    private static readonly Guid First = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static TheoryData<string, decimal> DecimalValues()
    {
        var data = new TheoryData<string, decimal>();
        foreach (var field in DecimalFields)
            foreach (var value in new[]
            {
                decimal.MinValue, decimal.MaxValue, -1m, 0m, 1m,
                -0.0000000000000000000000000001m, 0.0000000000000000000000000001m,
                -0.00001m, 0.00001m, 0.0000100m, 0.1234567890123456789012345678m
            })
                data.Add(field, value);
        return data;
    }

    [Theory]
    [MemberData(nameof(DecimalValues))]
    public void NormalizationAndContractRoundTripPreserveEveryStructuredDecimal(string field, decimal value)
    {
        // Draft normalization retains the entire decimal domain; completeness validation owns the permitted range.
        var normalized = IntercompanyAgreementRules.Normalize(Draft(Terms(field, value)));
        Assert.Equal(value, Value(field, normalized.Terms!.StructuredTerms!));

        var json = JsonSerializer.Serialize(normalized, FundStructureContractsJsonContext.Default.IntercompanyAgreementDraftDto);
        var restored = JsonSerializer.Deserialize(json, FundStructureContractsJsonContext.Default.IntercompanyAgreementDraftDto)!;
        var renormalized = IntercompanyAgreementRules.Normalize(restored);

        Assert.Equal(value, Value(field, restored.Terms!.StructuredTerms!));
        Assert.Equal(decimal.GetBits(Value(field, normalized.Terms.StructuredTerms!)),
            decimal.GetBits(Value(field, renormalized.Terms!.StructuredTerms!)));
        Assert.Equal(ReviewHash(normalized), ReviewHash(renormalized));
    }

    [Theory]
    [InlineData("FixedAnnualRate")]
    [InlineData("SpreadBps")]
    [InlineData("FixedFeeAmount")]
    [InlineData("AnnualFeeRate")]
    [InlineData("AllocationPercent")]
    [InlineData("GuaranteeLimit")]
    public void EquivalentScalesKeepOneReviewHashRegardlessOfCurrentCulture(string field)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach (var pair in new[] { (0.00001m, 0.0000100m), (1.23m, 1.2300m), (0m, 0.0000m) })
            {
                var first = IntercompanyAgreementRules.Normalize(Draft(Terms(field, pair.Item1)));
                var second = IntercompanyAgreementRules.Normalize(Draft(Terms(field, pair.Item2)));
                Assert.Equal(decimal.GetBits(Value(field, first.Terms!.StructuredTerms!)),
                    decimal.GetBits(Value(field, second.Terms!.StructuredTerms!)));
                Assert.Equal(ReviewHash(first), ReviewHash(second));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void SmallDeclarationsReachCompletenessValidationWithoutChangingFinancialUnits()
    {
        const decimal smallest = 0.0000000000000000000000000001m;
        foreach (var rate in new[] { -smallest, smallest, -1m, 1m })
            Validate(Draft(LoanTerms() with { FixedAnnualRate = rate }));
        foreach (var spread in new[] { decimal.MinValue, -smallest, smallest, decimal.MaxValue })
            Validate(Draft(LoanTerms() with
            { RateBasis = IntercompanyRateBasisDto.Floating, FixedAnnualRate = null, FloatingIndexReference = "SOFR", SpreadBps = spread }));
        foreach (var amount in new[] { smallest, decimal.MaxValue })
        {
            Validate(Draft(new(Schedule(), FixedFeeAmount: amount), IntercompanyAgreementTypeDto.ManagementFee));
            Validate(Draft(new(Guarantee: new("Synthetic beneficiary", amount)), IntercompanyAgreementTypeDto.Guarantee));
        }
        Validate(Draft(new(Schedule(), AnnualFeeRate: smallest, CalculationBaseReference: "Reviewed NAV"),
            IntercompanyAgreementTypeDto.ManagementFee));
        Validate(Draft(new(Schedule(), Allocations: [new(First, 0.00001m), new(Second, 99.99999m)]),
            IntercompanyAgreementTypeDto.CostShare));

        Assert.Throws<IntercompanyAgreementValidationException>(() => Validate(Draft(LoanTerms() with { FixedAnnualRate = 5m })));
        Assert.Throws<IntercompanyAgreementValidationException>(() => Validate(Draft(
            new(Schedule(), AnnualFeeRate: 1.5m, CalculationBaseReference: "Reviewed NAV"), IntercompanyAgreementTypeDto.ManagementFee)));
        Assert.Throws<IntercompanyAgreementValidationException>(() => Validate(Draft(
            new(Schedule(), FixedFeeAmount: -smallest), IntercompanyAgreementTypeDto.ManagementFee)));
        Assert.Throws<IntercompanyAgreementValidationException>(() => Validate(Draft(
            new(Guarantee: new("Synthetic beneficiary", -smallest)), IntercompanyAgreementTypeDto.Guarantee)));
    }

    private static readonly string[] DecimalFields =
        ["FixedAnnualRate", "SpreadBps", "FixedFeeAmount", "AnnualFeeRate", "AllocationPercent", "GuaranteeLimit"];

    private static IntercompanyStructuredTermsDto Terms(string field, decimal value) => field switch
    {
        "FixedAnnualRate" => new(FixedAnnualRate: value),
        "SpreadBps" => new(SpreadBps: value),
        "FixedFeeAmount" => new(FixedFeeAmount: value),
        "AnnualFeeRate" => new(AnnualFeeRate: value),
        "AllocationPercent" => new(Allocations: [new(First, value)]),
        "GuaranteeLimit" => new(Guarantee: new("Synthetic beneficiary", value)),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static decimal Value(string field, IntercompanyStructuredTermsDto terms) => field switch
    {
        "FixedAnnualRate" => terms.FixedAnnualRate!.Value,
        "SpreadBps" => terms.SpreadBps!.Value,
        "FixedFeeAmount" => terms.FixedFeeAmount!.Value,
        "AnnualFeeRate" => terms.AnnualFeeRate!.Value,
        "AllocationPercent" => Assert.Single(terms.Allocations!).Percent,
        "GuaranteeLimit" => terms.Guarantee!.LimitAmount!.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static IntercompanyAgreementDraftDto Draft(IntercompanyStructuredTermsDto terms,
        IntercompanyAgreementTypeDto type = IntercompanyAgreementTypeDto.Loan)
    {
        var roles = type switch
        {
            IntercompanyAgreementTypeDto.ManagementFee => ("Service provider", "Recipient"),
            IntercompanyAgreementTypeDto.CostShare => ("Participant", "Participant"),
            IntercompanyAgreementTypeDto.Guarantee => ("Guarantor", "Supported entity"),
            _ => ("Lender", "Borrower")
        };
        return new("IC-DECIMAL", "Synthetic decimal terms", First, roles.Item1, Second, roles.Item2,
            type, "USD", new(2026, 1, 1), new(2027, 1, 1), new() { StructuredTerms = terms });
    }

    private static IntercompanyStructuredTermsDto LoanTerms() => new(Schedule(), IntercompanyRateBasisDto.Fixed, 0.05m,
        DayCount: IntercompanyDayCountDto.Actual360, Compounding: IntercompanyCompoundingDto.Simple);
    private static IntercompanyScheduleDto Schedule() => new(new(2026, 1, 1), new(2027, 1, 1),
        IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following);
    private static string ReviewHash(IntercompanyAgreementDraftDto draft) =>
        IntercompanyAgreementRules.ReviewedHash(new(draft, null, null, "Reviewed exact decimal declarations"));
    private static void Validate(IntercompanyAgreementDraftDto draft) =>
        IntercompanyAgreementRules.ValidateComplete(IntercompanyAgreementRules.Normalize(draft));
}
