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
