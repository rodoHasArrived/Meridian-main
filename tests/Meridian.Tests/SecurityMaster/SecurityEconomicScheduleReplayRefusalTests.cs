using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityEconomicScheduleReplayRefusalTests
{
    public static IEnumerable<object[]> DeclaredSchedules()
    {
        yield return ["{\"accrual\":{\"businessDayConvention\":\"Following\"}}"];
        yield return ["{\"accrual\":{\"businessDayConvention\":\"Unadjusted\"}}"];
        yield return ["{\"accrual\":{\"businessDayConvention\":\"\"}}"];
        yield return ["{\"accrual\":{\"holidayCalendar\":\"USNY\"}}"];
        yield return ["{\"accrual\":{\"holidayCalendar\":{}}}"];
        yield return ["{\"call\":{\"callSchedule\":[{\"callDt\":\"2027-01-01\"}]}}"];
        yield return ["{\"call\":{\"putSchedule\":[{\"putDt\":\"2027-01-01\"}]}}"];
        yield return ["{\"call\":{\"callSchedule\":[null]}}"];
        yield return ["{\"call\":{\"putSchedule\":\"not-an-array\"}}"];
        yield return ["{\"accrual\":{\"holidayCalendar\":null},\"Accrual\":{\"HOLIDAYCALENDAR\":\"USNY\"}}"];
        yield return ["{\"call\":{\"callSchedule\":[],\"CALLSCHEDULE\":[{}]}}"];
    }

    [Theory]
    [MemberData(nameof(DeclaredSchedules))]
    public void EconomicOnlyConversion_RefusesUnrecoverableScheduleModules(string json)
    {
        var terms = Json(json);
        Action generic = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms);
        Action bond = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms, "Bond");
        Action unknown = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms, "UnknownClass");

        foreach (var convert in new[] { generic, bond, unknown })
            convert.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");
    }

    [Fact]
    public void OrdinaryAccrualAndScalarCallTerms_RemainRecoverable()
    {
        var terms = Json("""
            {"termsFormat":"economic","schemaVersion":2,
             "accrual":{"businessDayConvention":null,"holidayCalendar":null,"dayCount":"Thirty360","accrualStartDate":"2026-01-01"},
             "call":{"isCallable":true,"firstCallDate":"2027-01-01","callSchedule":[],"putSchedule":null},
             "maturity":{"maturityDate":"2030-01-01"}}
            """);

        var flat = SecurityAssetSpecificTermsUpcasterChain.Normalize(terms, "Bond").Payload;

        flat.GetProperty("dayCount").GetString().Should().Be("Thirty360");
        flat.GetProperty("callDate").GetString().Should().Be("2027-01-01");
        flat.GetProperty("isCallable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void EconomicOnlyAdapterReplay_Refuses_ButRetainedFlatTermsRemainByteIdentical()
    {
        var terms = Json("""
            {"termsFormat":"economic","schemaVersion":2,
             "call":{"isCallable":false,"callSchedule":[{"callDt":"2027-01-01"}],"putSchedule":[]}}
            """);
        var economic = EconomicRecord(terms, null);
        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic);
        replay.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");

        var flat = Json("""
            {"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,
             "maturity":"2030-01-01","isCallable":false,
             "retainedEconomicTerms":{"schemaVersion":2,"call":{"callSchedule":[{"callDt":"2027-01-01"}]}}}
            """);
        var retained = SecurityEconomicDefinitionAdapter.ToProjection(EconomicRecord(terms, flat));
        retained.AssetSpecificTerms.GetRawText().Should().Be(flat.GetRawText());
        SecurityAssetSpecificTermsUpcasterChain.Normalize(flat, "Bond").Payload.GetRawText().Should().Be(flat.GetRawText());
    }

    [Fact]
    public void EquityRefusal_KeepsItsExistingOrdering()
    {
        Action convert = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(
            Json("""{"call":{"callSchedule":[{}]}}"""), "Equity");
        convert.Should().Throw<InvalidOperationException>().WithMessage("Equity economic replay requires retained legacy asset-specific terms;*");
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static SecurityEconomicDefinitionRecord EconomicRecord(JsonElement terms, JsonElement? retainedFlat)
        => new(
            SecurityId: Guid.NewGuid(), AssetClass: "FixedIncome", AssetFamily: "CorporateDebt",
            SubType: "CorporateBond", TypeName: "Bond", IssuerType: "Corporate", RiskCountry: "US",
            Status: SecurityStatusDto.Active, DisplayName: "Replay bond", Currency: "USD",
            Classification: Json("{}"), CommonTerms: Json("{}"), EconomicTerms: terms, Provenance: Json("{}"),
            Version: 1, EffectiveFrom: DateTimeOffset.UnixEpoch, EffectiveTo: null, Identifiers: [],
            LegacyAssetClass: "Bond", LegacyAssetSpecificTerms: retainedFlat);
}
