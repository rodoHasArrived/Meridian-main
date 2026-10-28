using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class ScheduleTermsWriteRulesTests
{
    [Fact]
    public void VersionBeforeContainerIntroduction_DoesNotActivateReadableOnlyRules()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"maturity\":false,\"callSchedule\":[{\"callDate\":\"bad\"}]}");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 1);
        action.Should().NotThrow("the versioned containers are not declarations at the floor version");
    }

    [Theory]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"European\"},{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"European\"}],\"maturity\":\"2030-01-01\"}", "*Duplicate*")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"callEndDate\":\"2026-01-01\",\"exerciseStyle\":\"European\"}]}", "*inverted*")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"European\",\"minimumNoticeDays\":30,\"maximumNoticeDays\":10,\"noticeDayType\":\"Calendar\"}],\"maturity\":\"2030-01-01\"}", "*Notice bounds*")]
    [InlineData("{\"putSchedule\":[{\"putDate\":\"2027-01-01\",\"exerciseStyle\":\"European\",\"minimumNoticeDays\":5}],\"maturity\":\"2030-01-01\"}", "*noticeDayType*")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"Bermudan\"}],\"maturity\":\"2030-01-01\"}", "*regular exercise*")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"European\"}],\"maturity\":\"2030-01-01\",\"isCallable\":false}", "*isCallable=true*")]
    [InlineData("{\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"European\"}],\"maturity\":\"2030-01-01\",\"isCallable\":true,\"callDate\":\"2028-01-01\"}", "*callDate*")]
    [InlineData("{\"putSchedule\":[{\"putDate\":\"2027-01-01\"}],\"maturity\":\"2030-01-01\"}", "*exerciseStyle*")]
    [InlineData("{\"putSchedule\":[{\"exerciseStyle\":\"European\"}]}", "*requires an ISO putDate*")]
    [InlineData("{\"putSchedule\":[{\"putDate\":\"2027-01-01\",\"exerciseStyle\":\"european\"}],\"maturity\":\"2030-01-01\"}", "*exerciseStyle*")]
    [InlineData("{\"putSchedule\":[{\"putDate\":\"2027-01-01\",\"exerciseStyle\":\"European\",\"PutPrice\":100}],\"maturity\":\"2030-01-01\"}", "*case-variant*")]
    [InlineData("{\"PutSchedule\":[]}", "*Case-variant container*")]
    public void WrittenEntries_RefuseSpecificInvalidSemantics(string json, string reason)
    {
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", JsonSerializer.Deserialize<JsonElement>(json), 3);
        action.Should().Throw<InvalidOperationException>().WithMessage(reason);
    }

    [Theory]
    [InlineData("American", null)]
    [InlineData("European", "Business")]
    public void AmericanWindowsAndBusinessNotices_RequireTheBlocksCalendars(string style, string? noticeType)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            maturity = "2030-01-01",
            putSchedule = new[] { new { putDate = "2027-01-01", exerciseStyle = style, noticeDayType = noticeType } }
        });
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*block paymentCalendars*");
    }

    [Fact]
    public void ScalarDateAndCallableFlag_AreExplicitOnDeclaredCalls()
    {
        var cd = JsonSerializer.Deserialize<JsonElement>("{\"callableDate\":\"2027-01-01\",\"maturity\":\"2030-01-01\",\"callSchedule\":[{\"callDate\":\"2028-01-01\",\"exerciseStyle\":\"European\"}]}");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("CertificateOfDeposit", cd, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*callableDate*");
        var bond = JsonSerializer.Deserialize<JsonElement>("{\"callDate\":\"2027-01-01\",\"maturity\":\"2030-01-01\",\"callSchedule\":[{\"callDate\":\"2027-01-01\",\"exerciseStyle\":\"European\"}]}");
        action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", bond, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*isCallable=true*");
    }

    [Fact]
    public void WindowsAndNotices_AllowMissingPricesWithoutInventingPar()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""
            {"maturity":"2030-01-01","isCallable":true,"callDate":"2027-01-01",
             "callSchedule":[{"callDate":"2027-01-01","exerciseStyle":"European","minimumNoticeDays":5,"maximumNoticeDays":20,"noticeDayType":"Calendar"},
                             {"callDate":"2028-01-01","exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly"}]}
            """);
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9.99)]
    public void SuppliedPrices_RespectOwnerApprovedDecision14PlausibilityFloor(decimal price)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            maturity = "2030-01-01",
            putSchedule = new[] { new { putDate = "2027-01-01", exerciseStyle = "European", putPrice = price } }
        });
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*approved plausibility floor of 10*");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"putDate\":null,\"exerciseStyle\":null,\"exerciseFrequency\":null}]")]
    public void ProtocolUnsetPutContainers_DoNotBecomeEntries(string unset)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"putSchedule\":" + unset + "}");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"paymentCalendars\":[]}")]
    [InlineData("{\"accrualAdjusted\":null,\"couponCashBasis\":null}")]
    public void ProtocolUnsetBlocks_DoNotRequireAnchors(string unset)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"scheduleConventions\":" + unset + "}");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().NotThrow();
    }

    [Fact]
    public void DeclaredBlock_UsesCanonicalShapeAndKeepsOptionalCouponDefaults()
    {
        var payload = Bond(new { businessDayConvention = "Unadjusted" });
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);
        action.Should().NotThrow("absent coupon unit retains the legacy reading and absent cash basis is DayCount");
    }

    [Theory]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("StructuredCredit", 4)]
    public void ClassesWithoutSchemaAnchors_CannotWriteDeclaredBlocks(string assetClass, int version)
    {
        var payload = Bond(new { couponCashBasis = "DayCount" });
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations(assetClass, payload, version);
        action.Should().Throw<InvalidOperationException>().WithMessage("*terms declare no issueDate*");
    }

    [Theory]
    [InlineData("{\"paymentLagBusinessDays\":-1}", "*paymentLagBusinessDays*")]
    [InlineData("{\"businessDayConvention\":\"Following\",\"paymentCalendars\":[\"USNY\"]}", "*accrualAdjusted*")]
    [InlineData("{\"rollConvention\":\"AnchorDay\",\"endOfMonthRule\":true}", "*rollConvention*")]
    [InlineData("{\"couponRateUnit\":\"percent\"}", "*couponRateUnit*")]
    [InlineData("{\"PaymentLagBusinessDays\":0}", "*case-variant*")]
    [InlineData("{\"unsupportedRule\":true}", "*Unknown*")]
    public void DeclaredBlock_RefusesShapeAndFrozenVocabularyConflicts(string block, string reason)
    {
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", Bond(JsonSerializer.Deserialize<JsonElement>(block)), 3);
        action.Should().Throw<InvalidOperationException>().WithMessage(reason);
    }

    [Fact]
    public void WriteSideDayCountRules_AreNarrowerThanProjectionReadiness()
    {
        var business = Bond(new { businessDayConvention = "Unadjusted" }, "BUS/252");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", business, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*Business/252*");
        var icma = Bond(new { businessDayConvention = "Following", accrualAdjusted = true, paymentCalendars = new[] { "USNY" } }, "ACT/ACT ICMA");
        action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", icma, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*ICMA*");
        var term = Bond(new { couponCashBasis = "Level" }, frequency: "AtMaturity");
        action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", term, 3);
        action.Should().Throw<InvalidOperationException>().WithMessage("*AtMaturity*");
        var unresolvedDayCount = Bond(new { couponCashBasis = "DayCount" }, "Unknown day count");
        action = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", unresolvedDayCount, 3);
        action.Should().NotThrow("missing coupon or unrecognized day count remains a projection-time refusal");
    }

    [Fact]
    public void ProfileContainers_AreOutsideTheClassScheduleWriteRules()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"profileFields\":{},\"scheduleConventions\":{\"unsupportedRule\":true}}");
        var action = () => ScheduleTermsWriteRules.ValidateDeclarations("StructuredCredit", payload, 4);
        action.Should().NotThrow();
    }

    private static JsonElement Bond(object block, string? dayCount = null, string frequency = "SemiAnnual")
        => JsonSerializer.SerializeToElement(new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            paymentFrequency = frequency,
            dayCount,
            scheduleConventions = block
        });
}
