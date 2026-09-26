using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The composed asset-specific-terms migrate-on-read chain: unstamped payloads stamp to v1,
/// cross-family v2 economic-terms documents flatten to v1, accepted versions pass through, and
/// unknown future versions pass through with their version preserved for the acceptance guard to
/// diagnose. Plus the read-tolerance surface for enums written by newer nodes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetSpecificTermsUpcasterChainTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // ── Chain ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_UnstampedPayload_StampsLegacyVersion()
    {
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(Json("""{"shareClass":"Common"}"""));

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
            Json($$"""{"schemaVersion":{{AssetSpecificTermsSchema.CustomAssetProfile}},"customProfileId":"p-1"}"""));

        result.SchemaVersion.Should().Be(AssetSpecificTermsSchema.CustomAssetProfile);
        result.Payload.GetProperty("customProfileId").GetString().Should().Be("p-1");
    }

    [Fact]
    public void Normalize_UnknownFutureVersion_PassesThroughWithVersionPreserved()
    {
        // A v7 payload from a future node is not silently rewritten: the chain preserves it and
        // the acceptance guard stays the single place that decides (and precisely diagnoses) it.
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(
            Json("""{"schemaVersion":7,"futureField":"x"}"""));

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

    // ── The shared integer key (version 2 is reserved) ──────────────────────────────────────────

    [Fact]
    public void AssetSpecificTermsSchema_ReservesTheEconomicTermsVersion_AndNeverAcceptsIt()
    {
        // The two payload families share one schemaVersion key and the chain dispatches on the
        // bare integer, so the flat family can never use the economic family's number. The
        // reservation is declared, not merely a gap between 1 and 3, and no accepted flat version
        // may ever equal it — filling the gap would route every such payload into the flattener.
        AssetSpecificTermsSchema.ReservedForEconomicTerms.Should().Be(EconomicTermsSchema.Current);
        AssetSpecificTermsSchema.Legacy.Should().NotBe(EconomicTermsSchema.Current);
        AssetSpecificTermsSchema.CustomAssetProfile.Should().NotBe(EconomicTermsSchema.Current);
        AssetSpecificTermsSchema.Default.Should().NotBe(EconomicTermsSchema.Current);

        AssetSpecificTermsSchema.Accepted(isProfileBacked: false).Should().NotContain(EconomicTermsSchema.Current);
        AssetSpecificTermsSchema.Accepted(isProfileBacked: true).Should().NotContain(EconomicTermsSchema.Current);
        AssetSpecificTermsSchema.IsAccepted(EconomicTermsSchema.Current, isProfileBacked: false).Should().BeFalse();
        AssetSpecificTermsSchema.IsAccepted(EconomicTermsSchema.Current, isProfileBacked: true).Should().BeFalse();
    }

    [Fact]
    public void Normalize_ReservedVersionWithoutEconomicShape_PassesThroughForTheGuardInsteadOfEmptying()
    {
        // A flat document stamped with the reserved number is not an economic-terms document: it
        // has no module objects. Before the shape check, the chain flattened it to an empty
        // {"schemaVersion":1} — a total, silent loss of the record's economics stamped as valid
        // legacy. Now it passes through with its version preserved so the acceptance guard refuses
        // it with a precise "Unsupported schemaVersion '2'" diagnostic.
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(
            Json($$"""{"schemaVersion":{{AssetSpecificTermsSchema.ReservedForEconomicTerms}},"shareClass":"Common","votingRightsCat":"FullVoting"}"""));

        result.SchemaVersion.Should().Be(AssetSpecificTermsSchema.ReservedForEconomicTerms);
        result.Payload.GetProperty("shareClass").GetString().Should().Be("Common");
        result.Payload.GetProperty("votingRightsCat").GetString().Should().Be("FullVoting");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(result.Payload).Should().BeFalse();
        AssetSpecificTermsSchema.IsAccepted(result.SchemaVersion, isProfileBacked: false).Should().BeFalse();
    }

    [Fact]
    public void IsEconomicTermsDocument_DiscriminatesOnModuleKeysNotOnTheIntegerAlone()
    {
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-15"}}""")).Should().BeTrue();
        // The serializer writes every module key even when the module is null; a null module still
        // identifies the family.
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":2,"maturity":null,"coupon":null}""")).Should().BeTrue();
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":2,"shareClass":"Common"}""")).Should().BeFalse("a flat document has no module keys");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"schemaVersion":1,"maturity":{"maturityDate":"2030-01-15"}}""")).Should().BeFalse("only the economic version is bridged");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(
            Json("""{"maturity":{"maturityDate":"2030-01-15"}}""")).Should().BeFalse("an unstamped payload is legacy flat");
    }

    [Fact]
    public void Normalize_EconomicTermsV2_MarksTheFlattenedPayload()
    {
        var result = SecurityAssetSpecificTermsUpcasterChain.Normalize(
            Json("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-15"}}"""));

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
        projection.AssetSpecificTerms.GetProperty("maturityDate").GetString().Should().Be("2031-03-01");
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
