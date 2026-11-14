using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The composed asset-specific-terms migrate-on-read chain, dispatched on the declared tag alone:
/// unstamped flat payloads stamp to v1, declared economic-terms documents flatten to v1, accepted
/// versions pass through, unknown versions (flat 2 included) pass through with their version
/// preserved for the acceptance guard to diagnose, and untagged payloads are refused. Plus the read-tolerance surface for enums written by newer nodes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetSpecificTermsUpcasterChainTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // ── Chain ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_UnstampedPayload_StampsLegacyVersion()
    {
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(Json("""{"termsFormat":"flat","shareClass":"Common"}"""));

        result.SchemaVersion.Should().Be(AssetSpecificTermsSchema.Legacy);
        result.Payload.GetProperty("schemaVersion").GetInt32().Should().Be(AssetSpecificTermsSchema.Legacy);
        result.Payload.GetProperty("shareClass").GetString().Should().Be("Common");
    }

    [Fact]
    public void Normalize_EconomicTermsV2_FlattensToLegacyAssetSpecificShape()
    {
        // The exact nested shape BuildEconomicTermsJson writes with schemaVersion 2.
        var economic = Json("""
        {
            "schemaVersion": 2,
            "termsFormat": "economic",
            "maturity": { "effectiveDate": "2020-01-15", "issueDate": "2020-01-15", "maturityDate": "2030-01-15" },
            "coupon": { "couponType": "Fixed", "couponRate": 4.25, "paymentFrequency": "SemiAnnual", "dayCount": "Thirty360" },
            "discount": { "discountRate": null, "yieldRate": 4.1 },
            "accrual": { "accrualMethod": "Standard", "accrualStartDate": "2020-01-15", "dayCount": "Actual365" },
            "payment": { "paymentFrequency": "SemiAnnual", "paymentLagDays": 2 }
        }
        """);

        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(economic);

        result.SchemaVersion.Should().Be(AssetSpecificTermsSchema.Legacy);
        result.Payload.GetProperty("schemaVersion").GetInt32().Should().Be(AssetSpecificTermsSchema.Legacy);
        result.Payload.GetProperty("maturityDate").GetString().Should().Be("2030-01-15");
        result.Payload.GetProperty("issueDate").GetString().Should().Be("2020-01-15");
        result.Payload.GetProperty("couponType").GetString().Should().Be("Fixed");
        result.Payload.GetProperty("couponRate").GetDecimal().Should().Be(4.25m);
        result.Payload.GetProperty("paymentFrequency").GetString().Should().Be("SemiAnnual");
        // The coupon block's day count wins over the accrual block's.
        result.Payload.GetProperty("dayCount").GetString().Should().Be("Thirty360");
        result.Payload.GetProperty("accrualStartDate").GetString().Should().Be("2020-01-15");
        result.Payload.GetProperty("yieldRate").GetDecimal().Should().Be(4.1m);
        // Null source fields are omitted, not written as nulls.
        result.Payload.TryGetProperty("discountRate", out _).Should().BeFalse();

        // The flattened payload is accepted by the asset-specific-terms guard — the trap where a
        // v2 document in this slot threw "Unsupported schemaVersion '2'" is closed.
        AssetSpecificTermsSchema.IsAccepted(result.SchemaVersion, isProfileBacked: false).Should().BeTrue();
    }

    [Fact]
    public void Normalize_EconomicTermsV2_FallsBackToAccrualDayCountWhenCouponHasNone()
    {
        var economic = Json("""
        {
            "schemaVersion": 2,
            "termsFormat": "economic",
            "coupon": { "couponRate": 3.0 },
            "accrual": { "dayCount": "Actual360" }
        }
        """);

        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(economic);

        result.Payload.GetProperty("dayCount").GetString().Should().Be("Actual360");
    }

    [Fact]
    public void Normalize_AcceptedProfileVersion_PassesThroughUnchanged()
    {
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(
            Json($$"""{"schemaVersion":{{AssetSpecificTermsSchema.CustomAssetProfile}},"termsFormat":"customProfile","customProfileId":"p-1"}"""));

        result.SchemaVersion.Should().Be(AssetSpecificTermsSchema.CustomAssetProfile);
        result.Payload.GetProperty("customProfileId").GetString().Should().Be("p-1");
    }

    [Fact]
    public void Normalize_UnknownFutureVersion_PassesThroughWithVersionPreserved()
    {
        // A v7 payload from a future node is not silently rewritten: the chain preserves it and
        // the acceptance guard stays the single place that decides (and precisely diagnoses) it.
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(
            Json("""{"schemaVersion":7,"termsFormat":"flat","futureField":"x"}"""));

        result.SchemaVersion.Should().Be(7);
        result.Payload.GetProperty("futureField").GetString().Should().Be("x");
        AssetSpecificTermsSchema.IsAccepted(result.SchemaVersion, isProfileBacked: false).Should().BeFalse();
    }

    [Fact]
    public void EconomicUpcaster_DeclaresTheCrossFamilyTransition()
    {
        var upcaster = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Instance;

        upcaster.FromSchemaVersion.Should().Be(EconomicTermsSchema.Current);
        upcaster.ToSchemaVersion.Should().Be(AssetSpecificTermsSchema.Legacy);
    }

    // ── The shared integer key (version 2 is retired from live dispatch) ───────────────────────

    [Fact]
    public void ReservedVersionTwo_SurvivesOnlyInTheLegacyEventClassifier()
    {
        // ADR-024 amendment 2026-11-13 step 6: live dispatch reads the declared tag alone, so the
        // flat family no longer reserves the economic family's number. The reservation is kept only
        // by the frozen classifier of untagged event history, pinned to the number the historical
        // economic serializer wrote.
        SecurityTermsLegacyEventClassifier.ReservedForEconomicTerms.Should().Be(EconomicTermsSchema.Current);
        typeof(AssetSpecificTermsSchema).GetField("ReservedForEconomicTerms").Should().BeNull(
            "live acceptance no longer declares a reserved flat version");

        // No flat version is accepted at 2 today; acceptance is the plain guard set, not a reservation.
        AssetSpecificTermsSchema.Accepted(isProfileBacked: false).Should().Equal(AssetSpecificTermsSchema.Legacy);
        AssetSpecificTermsSchema.Accepted(isProfileBacked: true)
            .Should().Equal(AssetSpecificTermsSchema.Legacy, AssetSpecificTermsSchema.CustomAssetProfile);
        AssetSpecificTermsSchema.IsAccepted(2, isProfileBacked: false).Should().BeFalse();
    }

    [Fact]
    public void Normalize_DeclaredFlatVersionTwo_ReadsAsFlat_EvenWithEconomicModuleKeys()
    {
        // A declared flat document stamped 2 is read as flat — never routed into the economic
        // bridge, whatever keys it carries — and passes through with its version preserved, so the
        // acceptance guard refuses it like any other flat version it does not accept.
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(
            Json("""{"schemaVersion":2,"termsFormat":"flat","shareClass":"Common","maturity":{"maturityDate":"2030-01-15"}}"""));

        result.SchemaVersion.Should().Be(2);
        result.Payload.GetProperty("shareClass").GetString().Should().Be("Common");
        result.Payload.GetProperty("maturity").GetProperty("maturityDate").GetString().Should().Be("2030-01-15");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(result.Payload).Should().BeFalse();
        AssetSpecificTermsSchema.IsAccepted(result.SchemaVersion, isProfileBacked: false).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"shareClass":"Common","votingRightsCat":"FullVoting"}""")]
    [InlineData("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-15"}}""")]
    [InlineData("""{"schemaVersion":1,"shareClass":"Common"}""")]
    [InlineData("""{"shareClass":"Common"}""")]
    public void Normalize_UntaggedPayload_IsRefusedPrecisely_InsteadOfClassifiedByShape(string json)
    {
        var normalize = () => SecurityAssetSpecificTermsUpcasterChain.Normalize(Json(json));

        normalize.Should().Throw<SecurityTermsFormatUndeclaredException>()
            .WithMessage("*declare no termsFormat*no longer infers the payload family by shape*");
        var classAware = () => SecurityAssetSpecificTermsUpcasterChain.Normalize(Json(json), "Equity");
        classAware.Should().Throw<SecurityTermsFormatUndeclaredException>();
    }

    [Fact]
    public void IsEconomicTermsDocument_ReadsTheDeclaredTagOnly()
    {
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":2,"termsFormat":"economic","maturity":{"maturityDate":"2030-01-15"}}""")).Should().BeTrue();
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":2,"termsFormat":"flat","maturity":{"maturityDate":"2030-01-15"}}""")).Should().BeFalse(
            "a declared flat document is flat whatever keys it carries");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":1,"termsFormat":"economic"}""")).Should().BeTrue("the tag, not the integer, decides");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(Json("[]")).Should().BeFalse();

        var untagged = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-15"}}"""));
        untagged.Should().Throw<SecurityTermsFormatUndeclaredException>();
    }

    [Fact]
    public void LegacyEventClassifier_DiscriminatesUntaggedHistoryOnModuleKeysNotOnTheIntegerAlone()
    {
        // The frozen rule for untagged retained terms in event history (and only there).
        SecurityTermsLegacyEventClassifier.Classify(
            Json("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-15"}}""")).Should().Be(SecurityTermsFormats.Economic);
        // The serializer writes every module key even when the module is null; a null module still
        // identifies the family.
        SecurityTermsLegacyEventClassifier.Classify(
            Json("""{"schemaVersion":2,"maturity":null,"coupon":null}""")).Should().Be(SecurityTermsFormats.Economic);
        SecurityTermsLegacyEventClassifier.Classify(
            Json("""{"schemaVersion":2,"shareClass":"Common"}""")).Should().Be(SecurityTermsFormats.Flat, "a flat document has no module keys");
        SecurityTermsLegacyEventClassifier.Classify(
            Json("""{"schemaVersion":1,"maturity":{"maturityDate":"2030-01-15"}}""")).Should().Be(SecurityTermsFormats.Flat, "only the reserved version is economic");
        SecurityTermsLegacyEventClassifier.Classify(
            Json("""{"maturity":{"maturityDate":"2030-01-15"}}""")).Should().Be(SecurityTermsFormats.Flat, "an unstamped payload is legacy flat");
        SecurityTermsLegacyEventClassifier.Classify(
            Json("""{"schemaVersion":3,"customProfileId":"p","profileFields":{}}""")).Should().Be(SecurityTermsFormats.CustomProfile);
        SecurityTermsLegacyEventClassifier.Classify(
            Json("""{"schemaVersion":2,"termsFormat":"flat","maturity":{}}""")).Should().Be(SecurityTermsFormats.Flat, "a declared tag wins");
    }

    [Fact]
    public void Normalize_EconomicTermsV2_MarksTheFlattenedPayload()
    {
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(
            Json("""{"schemaVersion":2,"termsFormat":"economic","maturity":{"maturityDate":"2030-01-15"}}"""));

        result.Payload.GetProperty(SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty)
            .GetInt32().Should().Be(EconomicTermsSchema.Current);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(result.Payload).Should().BeTrue();
    }

    // ── Adapter seam (the latent v2 trap) ───────────────────────────────────────────────────────

    [Fact]
    public void ToProjection_WithoutLegacyTerms_LandsEconomicTermsAsAcceptedV1Payload()
    {
        // LegacyAssetSpecificTerms null is the "one null field away" case: the raw v2 economic
        // document used to flow into the v1-validated slot and fail every read of the row.
        var economic = new SecurityEconomicDefinitionRecord(
            SecurityId: Guid.Parse("abababab-0000-0000-0000-000000000001"),
            AssetClass: "FixedIncome",
            AssetFamily: "CorporateDebt",
            SubType: "CorporateBond",
            TypeName: "Bond",
            IssuerType: "Corporate",
            RiskCountry: "US",
            Status: SecurityStatusDto.Active,
            DisplayName: "Trap bond",
            Currency: "USD",
            Classification: Json("""{"assetClass":"FixedIncome"}"""),
            CommonTerms: Json("""{"displayName":"Trap bond","currency":"USD"}"""),
            EconomicTerms: Json("""
            {
                "schemaVersion": 2,
                "maturity": { "maturityDate": "2031-03-01" },
                "coupon": { "couponRate": 5.0, "dayCount": "Thirty360" }
            }
            """),
            Provenance: Json("""{"sourceSystem":"tests","updatedBy":"tests","asOf":"2026-01-01T00:00:00Z"}"""),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow,
            EffectiveTo: null,
            Identifiers: [],
            LegacyAssetClass: "Bond",
            LegacyAssetSpecificTerms: null);

        var projection = SecurityEconomicDefinitionAdapter.ToProjection(economic);

        var version = SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(projection.AssetSpecificTerms);
        version.Should().Be(AssetSpecificTermsSchema.Legacy);
        AssetSpecificTermsSchema.IsAccepted(version, isProfileBacked: false).Should().BeTrue();
        projection.AssetSpecificTerms.GetProperty("maturity").GetString().Should().Be("2031-03-01");
        projection.AssetSpecificTerms.GetProperty("couponRate").GetDecimal().Should().Be(5.0m);
        // The route is lossy, and the rebuilt payload says so.
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(projection.AssetSpecificTerms).Should().BeTrue();
    }

    // ── Enum read tolerance ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ticker", SecurityIdentifierKind.Ticker)]
    [InlineData("cusip", SecurityIdentifierKind.Cusip)]
    [InlineData("QuantumId2049", SecurityIdentifierKind.Unknown)]
    [InlineData("", SecurityIdentifierKind.Unknown)]
    [InlineData(null, SecurityIdentifierKind.Unknown)]
    [InlineData("9999", SecurityIdentifierKind.Unknown)]
    public void ParseOrFallback_DegradesUnrecognizedValuesInsteadOfThrowing(string? raw, SecurityIdentifierKind expected)
        => SecurityMasterEnumReads.ParseOrFallback(raw, SecurityIdentifierKind.Unknown).Should().Be(expected);

    [Fact]
    public void ParseOrFallback_NumericStringNamingADefinedMember_StillParses()
        => SecurityMasterEnumReads.ParseOrFallback("0", SecurityStatusDto.Unknown).Should().Be(SecurityStatusDto.Active);

    [Fact]
    public void ReadToleranceEnums_AllCarryAnUnknownMember()
    {
        // The members the row-read paths degrade into; removing one reintroduces a read outage.
        Enum.IsDefined(SecurityIdentifierKind.Unknown).Should().BeTrue();
        Enum.IsDefined(SecurityStatusDto.Unknown).Should().BeTrue();
        Enum.IsDefined(SecurityAliasScope.Unknown).Should().BeTrue();
        Enum.IsDefined(SecurityMasterRevisionStateDto.Unknown).Should().BeTrue();
        Enum.IsDefined(StructuredCashFlowSourceKind.Unknown).Should().BeTrue();
    }
}
