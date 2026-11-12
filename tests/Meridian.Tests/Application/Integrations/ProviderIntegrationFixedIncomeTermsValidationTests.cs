using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Calendars;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationFixedIncomeTermsValidationTests
{
    [Theory]
    [InlineData(ProviderCapabilityKindDto.SecurityReferenceData)]
    [InlineData(ProviderCapabilityKindDto.Positions)]
    public void ProviderSuppliedPreview_IsRemovedEvenOutsideFixedIncomeProfile(ProviderCapabilityKindDto capability)
    {
        var mapped = JsonNode.Parse("""
            {"security":{"name":"Unreviewed source"}, "fixedIncomePaymentPreview":{"sampleCouponPayment":"999999"}}
            """)!.AsObject();
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFixedIncomeTermsValidation.AddValidationIssues(capability, mapped, issues);

        mapped.ContainsKey("fixedIncomePaymentPreview").Should().BeFalse();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void NormalizedTermsInspector_RetainsExactJsonTextAcrossSerialization()
    {
        var mapped = CreateRecord();
        mapped["security"]!["coupon"] = "4.123456789012345678";
        Validate(mapped).Should().BeEmpty();

        var browserPayload = JsonNode.Parse(mapped.ToJsonString())!;

        browserPayload["fixedIncomePaymentPreview"]!["normalizedTermsJson"]!.GetValue<string>()
            .Should().Contain("4.123456789012345678");
    }

    [Theory]
    [InlineData("5", "Percent")]
    [InlineData("0.05", "Fraction")]
    public void Preview_UsesExplicitUnitsAndCanonicalCouponKernel(string coupon, string unit)
    {
        var mapped = CreateRecord();
        mapped["security"]!["coupon"] = coupon;
        mapped["security"]!["couponRateUnit"] = unit;
        var issues = Validate(mapped);

        issues.Should().BeEmpty();
        decimal.Parse(mapped["fixedIncomePaymentPreview"]!["sampleCouponPayment"]!.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture).Should().Be(2500m);
        mapped["fixedIncomePaymentPreview"]!["paymentDate"]!.GetValue<string>().Should().Be("2027-01-15");
        mapped["security"]!["callSchedule"].Should().BeOfType<JsonArray>();
    }

    [Theory]
    [InlineData("couponRateUnit", "")]
    [InlineData("couponCashBasis", "")]
    [InlineData("currencyReview", "Unreviewed")]
    [InlineData("dayCount", "Guess")]
    [InlineData("settlementDays", "1.5")]
    public void MissingOrAmbiguousConvention_BlocksSampleAndStaging(string field, string value)
    {
        var mapped = CreateRecord();
        mapped["security"]![field] = value;
        var issues = Validate(mapped);

        issues.Should().Contain(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical &&
            issue.TargetField == $"security.{field}");
        mapped.ContainsKey("fixedIncomePaymentPreview").Should().BeFalse();
    }

    [Fact]
    public void UnknownCalendar_IsNotReplacedWithWeekends()
    {
        var mapped = CreateRecord();
        mapped["security"]!["paymentCalendars"] = "[\"MISSING-HOLIDAY-SET\"]";
        var issues = Validate(mapped);

        issues.Should().Contain(issue => issue.TargetField == "security.paymentCalendars");
        mapped.ContainsKey("fixedIncomePaymentPreview").Should().BeFalse();
    }

    [Fact]
    public void ConfiguredHolidayCalendar_DrivesTheSamplePaymentDate()
    {
        var mapped = CreateRecord();
        mapped["security"]!["paymentCalendars"] = "[\"TEST-HOLIDAYS\"]";
        var calendars = new StaticBusinessDayCalendarSource([
            BusinessDayCalendars.WeekendsOnly,
            new HolidayBusinessDayCalendar("TEST-HOLIDAYS", 7, [new DateOnly(2027, 1, 15)])
        ]);
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFixedIncomeTermsValidation.AddValidationIssues(
            ProviderCapabilityKindDto.SecurityReferenceData, mapped, issues, calendars);

        issues.Should().BeEmpty();
        mapped["fixedIncomePaymentPreview"]!["paymentDate"]!.GetValue<string>().Should().Be("2027-01-18");
    }

    [Fact]
    public void DatedFactorIsAppliedToSampleCash()
    {
        var mapped = CreateRecord();
        mapped["security"]!["factorHistory"] = "[{\"asOfDate\":\"2026-07-15\",\"factor\":0.8}]";

        Validate(mapped).Should().BeEmpty();

        decimal.Parse(mapped["fixedIncomePaymentPreview"]!["sampleCouponPayment"]!.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture).Should().Be(2000m);
    }

    [Fact]
    public void InvalidCallWindow_UsesCanonicalScheduleAdmissionRules()
    {
        var mapped = CreateRecord();
        mapped["security"]!["callSchedule"] = "[{\"callDate\":\"2028-01-01\",\"callPrice\":100}]";

        Validate(mapped).Should().Contain(issue => issue.Code == "fixed-income.conventions.invalid");
        mapped.ContainsKey("fixedIncomePaymentPreview").Should().BeFalse();
    }

    [Fact]
    public void InvalidFactorHistory_CannotBecomeAnEmptySchedule()
    {
        var mapped = CreateRecord();
        mapped["security"]!["factorHistory"] = "[{\"asOfDate\":\"2026-07-15\",\"factor\":-1}]";

        Validate(mapped).Should().Contain(issue => issue.TargetField == "security.factorHistory");
        mapped.ContainsKey("fixedIncomePaymentPreview").Should().BeFalse();
    }

    private static List<ValidationIssueDto> Validate(JsonObject mapped)
    {
        var issues = new List<ValidationIssueDto>();
        ProviderIntegrationFixedIncomeTermsValidation.AddValidationIssues(ProviderCapabilityKindDto.SecurityReferenceData, mapped, issues);
        return issues;
    }

    private static JsonObject CreateRecord() => JsonNode.Parse("""
        {"security":{
          "importTermsProfile":"fixed-income-v2", "name":"Synthetic bond", "coupon":"5",
          "couponRateUnit":"Percent", "couponCashBasis":"Level", "paymentFrequency":"SemiAnnual",
          "dayCount":"30/360", "issueDate":"2026-07-15", "maturityDate":"2028-07-15",
          "currency":"USD", "currencyReview":"Reviewed", "paymentCalendars":"[\"WEEKENDS\"]",
          "businessDayConvention":"Following", "accrualAdjusted":"false", "settlementDays":"1",
          "settlementConvention":"Following", "settlementCalendars":"[\"WEEKENDS\"]",
          "callSchedule":"[]", "factorHistory":"[]"
        }}
        """)!.AsObject();
}
