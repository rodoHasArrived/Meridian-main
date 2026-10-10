using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the asset-class-aware half of the cross-family v2 → v1 bridge
/// (<see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonElement, string?)"/>):
/// every flat key it writes is declared for the class, and a fully populated v1 record taken through
/// v1 → economic definition → (legacy terms discarded, as on event replay) → v1 comes back with the
/// same value at every key the economic document carries.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicTermsClassAwareBridgeTests
{
    public static TheoryData<string> DeclaredAssetClasses() => SecurityAssetTermsSchemaRoundTripTests.DeclaredAssetClasses();

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void RecoverableKeys_AreDeclaredForTheirClass(string assetClass)
    {
        var declared = SecurityAssetTermsSchema.Fields(assetClass).Select(static field => field.Key).ToHashSet(StringComparer.Ordinal);

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass)
            .Should().OnlyContain(key => declared.Contains(key), $"the class-aware bridge may only write keys {assetClass} declares")
            .And.OnlyHaveUniqueItems();

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys(assetClass)
            .Concat(SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass))
            .Should().BeEquivalentTo(declared, "recoverable and unrecoverable keys partition the declared schema");
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void ReplayWithoutLegacyTerms_RecoversEveryCarriedValueAtItsDeclaredKey(string assetClass)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(rebuilt).Should().BeTrue();
        var declared = SecurityAssetTermsSchema.Fields(assetClass).Select(static field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var property in rebuilt.EnumerateObject())
        {
            if (property.Name is "schemaVersion" or SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty)
            {
                continue;
            }

            declared.Should().Contain(property.Name, $"the rebuilt {assetClass} payload must read like an original v1 write");
            original.TryGetProperty(property.Name, out var originalValue).Should().BeTrue();
            if (originalValue.ValueKind == JsonValueKind.Number)
            {
                property.Value.GetDecimal().Should().Be(originalValue.GetDecimal(), $"{assetClass}.{property.Name} must survive replay");
            }
            else
            {
                property.Value.GetRawText().Should().Be(originalValue.GetRawText(), $"{assetClass}.{property.Name} must survive replay");
            }
        }
    }

    [Theory]
    [InlineData("CertificateOfDeposit", "callableDate")]
    [InlineData("CertificateOfDeposit", "issuerName")]
    [InlineData("Deposit", "institutionName")]
    [InlineData("Deposit", "isCallable")]
    [InlineData("StructuredCredit", "currentFactor")]
    [InlineData("StructuredCredit", "poolId")]
    [InlineData("StructuredCredit", "tranche")]
    [InlineData("StructuredCredit", "originalFace")]
    [InlineData("Repo", "counterparty")]
    [InlineData("Repo", "haircut")]
    [InlineData("CashSweep", "programName")]
    [InlineData("MoneyMarketFund", "liquidityFeeEligible")]
    [InlineData("TreasuryBill", "auctionDate")]
    [InlineData("CommercialPaper", "isAssetBacked")]
    [InlineData("Bond", "maturity")]
    [InlineData("Bond", "couponType")]
    [InlineData("Swap", "maturityDate")]
    [InlineData("Option", "expiry")]
    public void ReplayWithoutLegacyTerms_RecoversPreviouslyDroppedTerms(string assetClass, string flatKey)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        rebuilt.TryGetProperty(flatKey, out var value).Should().BeTrue($"{assetClass}.{flatKey} is carried by the economic document");
        value.GetRawText().Should().Be(original.GetProperty(flatKey).GetRawText());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplayWithoutLegacyTerms_CommercialPaperKeepsItsRequiredAssetBackedFlag(bool isAssetBacked)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "CommercialPaper",
            new
            {
                issuerName = "Meridian Funding LLC",
                maturity = "2026-11-15",
                discountRate = 5.05m,
                dayCount = "ACT/360",
                isAssetBacked
            });
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("CommercialPaper", original));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        rebuilt.GetProperty("isAssetBacked").GetBoolean().Should().Be(isAssetBacked);
    }

    [Fact]
    public void Convert_UnknownOrMissingClass_FallsBackToTheClassIndependentBridge()
    {
        using var document = JsonDocument.Parse("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-15"},"call":{"isCallable":true}}""");

        var unknown = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement, "NotAClass").Payload;
        var missing = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement, null).Payload;
        var generic = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement).Payload;

        unknown.GetRawText().Should().Be(generic.GetRawText());
        missing.GetRawText().Should().Be(generic.GetRawText());
        generic.GetProperty("maturityDate").GetString().Should().Be("2030-01-15");
    }

    [Fact]
    public void UnrecoverableKeys_NameWhatTheEconomicDocumentNeverCarried()
    {
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys("Option")
            .Should().Contain(["strike", "putCall", "underlyingId"]);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys("Swap").Should().Contain("legs");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys("Bond").Should().Contain("subclass");
    }
}
