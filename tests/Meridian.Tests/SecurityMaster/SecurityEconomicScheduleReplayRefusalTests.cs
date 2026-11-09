using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Pins where economic-only replay still refuses after ADR-027 stage 1 increment 1. Accrual
/// conventions remain unrecoverable everywhere; the Call module's schedule lists refuse on the
/// class-independent route and for classes without the declared flat arrays, while a declaring
/// class (Bond, CertificateOfDeposit, StructuredCredit) recovers them —
/// <c>SecurityEconomicCallScheduleFlatteningTests</c> pins that recovery. Malformed schedule
/// members (non-array lists, populated entries without a date) refuse on every route.
/// </summary>
public sealed class SecurityEconomicScheduleReplayRefusalTests
{
    public static IEnumerable<object[]> AccrualSchedules()
    {
        yield return ["{\"accrual\":{\"businessDayConvention\":\"Following\"}}"];
        yield return ["{\"accrual\":{\"businessDayConvention\":\"Unadjusted\"}}"];
        yield return ["{\"accrual\":{\"businessDayConvention\":\"\"}}"];
        yield return ["{\"accrual\":{\"holidayCalendar\":\"USNY\"}}"];
        yield return ["{\"accrual\":{\"holidayCalendar\":{}}}"];
        yield return ["{\"accrual\":{\"holidayCalendar\":null},\"Accrual\":{\"HOLIDAYCALENDAR\":\"USNY\"}}"];
    }

    [Theory]
    [MemberData(nameof(AccrualSchedules))]
    public void AccrualConventions_RefuseOnEveryRoute(string json)
    {
        var terms = Json(json);
        Action generic = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms);
        Action bond = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms, "Bond");
        Action unknown = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms, "UnknownClass");

        foreach (var convert in new[] { generic, bond, unknown })
            convert.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");
    }

    public static IEnumerable<object[]> CallSchedules()
    {
        yield return ["{\"call\":{\"callSchedule\":[{\"callDt\":\"2027-01-01\"}]}}"];
        yield return ["{\"call\":{\"putSchedule\":[{\"putDt\":\"2027-01-01\"}]}}"];
        yield return ["{\"call\":{\"callSchedule\":[null]}}"];
        yield return ["{\"call\":{\"putSchedule\":\"not-an-array\"}}"];
        yield return ["{\"call\":{\"callSchedule\":[],\"CALLSCHEDULE\":[{}]}}"];
    }

    [Theory]
    [MemberData(nameof(CallSchedules))]
    public void CallSchedules_StillRefuse_WithoutDeclaredFlatArrays(string json)
    {
        var terms = Json(json);
        Action generic = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms);
        // Deposit has a class flattening but declares no flat callSchedule/putSchedule arrays.
        Action deposit = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms, "Deposit");
        Action unknown = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(terms, "UnknownClass");

        foreach (var convert in new[] { generic, deposit, unknown })
            convert.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");
    }

    [Theory]
    [InlineData("{\"call\":{\"putSchedule\":\"not-an-array\"}}")]
    [InlineData("{\"call\":{\"callSchedule\":[\"not-an-entry\"]}}")]
    [InlineData("{\"call\":{\"callSchedule\":[{\"callPx\":102.5}]}}")] // populated but dateless
    public void MalformedSchedules_RefuseEvenForDeclaringClasses(string json)
    {
        Action bond = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(Json(json), "Bond");
        bond.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");
    }

    [Theory]
    [InlineData("{\"call\":{\"callSchedule\":[null]}}")]
    [InlineData("{\"call\":{\"callSchedule\":[],\"CALLSCHEDULE\":[{}]}}")]
    [InlineData("{\"call\":{\"callSchedule\":[{\"callDt\":null,\"callPx\":null}]}}")] // all-null entry is unset
    public void UnsetEntries_ConvertWithoutDeclaringSchedules_OnDeclaringClasses(string json)
    {
        var flat = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(Json(json), "Bond").Payload;

        flat.TryGetProperty("callSchedule", out _).Should().BeFalse("an unset entry is not a term, so no schedule is declared");
        flat.TryGetProperty("putSchedule", out _).Should().BeFalse();
        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(SecurityAssetClassCatalog.GetStampFloor("Bond"), "a document holding no versioned terms stamps at the floor");
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
    public void EconomicOnlyAdapterReplay_RecoversDeclaredCalls_AndRetainedFlatTermsRemainByteIdentical()
    {
        var terms = Json("""
            {"termsFormat":"economic","schemaVersion":2,
             "call":{"isCallable":false,"callSchedule":[{"callDt":"2027-01-01"}],"putSchedule":[]}}
            """);

        // Since ADR-027 stage 1 increment 1 the economic-only route recovers the declared calls
        // into the flat array instead of refusing; the source document stays retained beside them.
        var recovered = SecurityEconomicDefinitionAdapter.ToProjection(EconomicRecord(terms, null));
        recovered.AssetSpecificTerms.GetProperty("callSchedule").GetArrayLength().Should().Be(1);
        recovered.AssetSpecificTerms.GetProperty("callSchedule")[0].GetProperty("callDate").GetString().Should().Be("2027-01-01");
        recovered.AssetSpecificTerms.TryGetProperty(SecurityTermsEnvelope.RetainedEconomicTermsKey, out _).Should().BeTrue();

        // The retained-flat replay path is untouched: stored flat terms replay byte-identically.
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
