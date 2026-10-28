using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class ScheduleTermsWriteRulesTests
{
    [Fact]
    public void CurrentWriteVersion_DoesNotActivateReadableOnlyScheduleRules()
    {
        var payload = JsonSerializer.SerializeToElement(new { callSchedule = new[] { new { callDate = "bad" } } });
        var action = () => ScheduleTermsWriteRules.Validate("Bond", payload);
        action.Should().NotThrow("ADR-024's existing write refusal owns unreadable-to-write fields");
    }

    [Theory]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\"},{\"callDate\":\"2027-01-01\"}],\"maturity\":\"2030-01-01\"}")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"callEndDate\":\"2026-01-01\"}]}")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"minimumNoticeDays\":30,\"maximumNoticeDays\":10,\"noticeDayType\":\"Calendar\"}],\"maturity\":\"2030-01-01\"}")]
    [InlineData("{\"putSchedule\":[{\"putDate\":\"2027-01-01\",\"minimumNoticeDays\":5}],\"maturity\":\"2030-01-01\"}")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"Bermudan\"}],\"maturity\":\"2030-01-01\"}")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\"}],\"maturity\":\"2030-01-01\",\"isCallable\":false}")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\"}],\"maturity\":\"2030-01-01\",\"callDate\":\"2028-01-01\"}")]
    [InlineData("{\"scheduleConventions\":{\"couponRateUnit\":\"Percent\",\"couponCashBasis\":\"DayCount\",\"paymentLagBusinessDays\":-1}}")]
    public void ReadableDeclarations_RefuseSemanticContradictions(string json)
    {
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", JsonSerializer.Deserialize<JsonElement>(json), 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("Invalid declared schedule terms:*");
    }

    [Fact]
    public void AllNullEntries_DoNotInventWindows()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"callSchedule\":[{}, {\"callDate\":null}]} ");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().NotThrow();
    }

    [Fact]
    public void CertificateScalarDate_MustMatchItsSchedule()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"callableDate\":\"2027-01-01\",\"maturity\":\"2030-01-01\",\"callSchedule\":[{\"callDate\":\"2028-01-01\"}]}");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("CertificateOfDeposit", payload, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*callableDate*");
    }

    [Fact]
    public void WindowsAndNotices_KeepExplicitMissingPricesAndStylesWithoutInventingDefaults()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""
            {"maturity":"2030-01-01","isCallable":true,"callDate":"2027-01-01",
             "callSchedule":[{"callDate":"2027-01-01","minimumNoticeDays":5,"maximumNoticeDays":20,"noticeDayType":"Calendar"},
                             {"callDate":"2028-01-01","exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly"}]}
            """);
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().NotThrow();
    }
}
