using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityRecurringAccrualPolicyTests
{
    [Fact]
    public void ReviewedFixedBulletTerms_AreAdmittedByTheRegisteredProfile()
    {
        SecurityRecurringAccrualPolicies.EvaluateTerms(Security(), SecurityRecurringAccrualPolicies.FixedBulletActual365)
            .Should().BeNull();
    }

    [Fact]
    public void SupplementalCommonEconomics_CannotHideFeesOrOriginalIssueDiscount()
    {
        var security = Security() with { CommonTerms = JsonSerializer.SerializeToElement(new { fees = 1000, originalIssueDiscount = .01m }) };
        var blocker = SecurityRecurringAccrualPolicies.EvaluateTerms(security, SecurityRecurringAccrualPolicies.FixedBulletActual365);
        blocker.Should().NotBeNull();
        blocker!.Code.Should().Be("AccrualEconomicsUnsupported");
    }

    [Theory]
    [InlineData("assetClassTermsVersion", "5", "AccrualAnchorMissing")]
    [InlineData("principal", "0", "AccrualAnchorInvalid")]
    [InlineData("issueDate", "null", "AccrualAnchorMissing")]
    [InlineData("rateUnit", "null", "RateUnitUndeclared")]
    [InlineData("couponRate", "-0.5", "AccrualCouponUnsupported")]
    [InlineData("dayCount", "\"ACT/360\"", "AccrualDayCountUnsupported")]
    [InlineData("paymentFrequency", "\"Unknown\"", "AccrualFrequencyUnsupported")]
    [InlineData("paymentFrequency", "\"Monthly\"", "AccrualFrequencyUnsupported")]
    [InlineData("callableDate", "\"2026-06-30\"", "AccrualEconomicsUnsupported")]
    [InlineData("scheduleConventions", "{\"couponRateUnit\":\"Percent\"}", "AccrualEconomicsUnsupported")]
    [InlineData("fees", "5", "AccrualEconomicsUnsupported")]
    public void UnsupportedOrUndeclaredEconomics_AreExplicitlyRefused(string key, string value, string code)
    {
        var security = Security(key, value);
        var blocker = SecurityRecurringAccrualPolicies.EvaluateTerms(security, SecurityRecurringAccrualPolicies.FixedBulletActual365);
        blocker.Should().NotBeNull();
        blocker!.Code.Should().Be(code);
    }

    [Theory]
    [InlineData(100, BondAmortizationMethod.NoAmortization, true)]
    [InlineData(99, BondAmortizationMethod.NoAmortization, false)]
    [InlineData(101, BondAmortizationMethod.NoAmortization, false)]
    [InlineData(100, BondAmortizationMethod.StraightLine, false)]
    public void LotAdmission_RequiresParAndExplicitNoAmortization(int price, BondAmortizationMethod method, bool admitted)
    {
        var security = Security();
        var holdings = new[] { new FaceValueAccrualHolding(new(2026, 1, 1),
            new FaceValueLot("cd-lot", security.SecurityId, new(2025, 12, 31), 10_000m, price),
            1m, 1m, AmortizationMethod: method) };
        (SecurityRecurringAccrualPolicies.EvaluateHoldings(SecurityRecurringAccrualPolicies.FixedBulletActual365, holdings) is null)
            .Should().Be(admitted);
    }

    internal static SecurityDetailDto Security(string? key = null, string? value = null)
    {
        var terms = JsonNode.Parse("""
            {"termsFormat":"flat","assetClassTermsVersion":6,"issuerName":"Synthetic bank","issueDate":"2025-12-31",
             "maturity":"2026-12-31","principal":100000,"couponRate":3.65,"rateUnit":"Percent",
             "paymentFrequency":"AtMaturity","dayCount":"ACT/365"}
            """)!.AsObject();
        if (key is not null)
            terms[key] = JsonNode.Parse(value!);
        return new(Guid.Parse("7c3f9f2e-91d4-4a3b-8f65-2f0c5a9d7e10"), "CertificateOfDeposit", SecurityStatusDto.Active,
            "Synthetic CD", "USD", JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms),
            [], [], 1, new(2025, 12, 15, 0, 0, 0, TimeSpan.Zero), null);
    }
}
