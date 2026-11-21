using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Each residual reference group independently proves its exact typed replay home.</summary>
public sealed class SecurityReferenceModuleContractionReplayTests
{
    [Fact]
    public void CommodityContract_PreservesQuantityUnitAndExactContractSize()
        => AssertReplay("Commodity", "commodityContract", "commodityType", "denomination", "contractSize");

    [Fact]
    public void DigitalAsset_PreservesNetworkIndependentlyOfTheCurrencyPair()
        => AssertReplay("CryptoCurrency", "digitalAsset", "network");

    [Fact]
    public void UnderlyingExposure_PreservesExactLeverageInsteadOfInvertingRoundedMargin()
        => AssertReplay("Cfd", "underlyingExposure", "underlyingAssetClass", "underlyingDescription", "leverage");

    [Fact]
    public void UnderlyingExposure_NonTerminatingMarginReciprocalCannotChangeLeverage()
    {
        const decimal leverage = 3.000000000000000000000000001m;
        var payload = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Cfd"])!;
        payload["leverage"] = leverage;
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Cfd", payload);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Cfd", original));
        var replay = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });

        economic.EconomicTerms.GetProperty("underlyingExposure").GetProperty("leverage").GetDecimal().Should().Be(leverage);
        replay.AssetSpecificTerms.GetProperty("leverage").GetDecimal().Should().Be(leverage);
    }

    [Fact]
    public void Settlement_PreservesTheDeclaredMethod()
        => AssertReplay("FxForward", "settlement", "settlementType");

    [Fact]
    public void ClassificationDetail_PreservesCategoryAndSubtypeSeparately()
        => AssertReplay("OtherSecurity", "classificationDetail", "category", "subType");

    [Fact]
    public void FundShare_PreservesNavCurrencyAndDistributionBehavior()
        => AssertReplay("InvestmentFund", "fundShare", "fundType", "navCurrency", "distributionPolicy", "isStableNav");

    [Theory]
    [InlineData("DirectLoan", "pricingSource")]
    [InlineData("InvestmentFund", "pricingSource")]
    [InlineData("NonDeliverableForward", "fixingSource")]
    public void ReferenceDataSource_PreservesOperationalSourceLabels(string assetClass, string field)
        => AssertReplay(assetClass, "referenceDataSource", field);

    [Theory]
    [InlineData("Commodity", "commodityType", "VendorMetal/Series-7")]
    [InlineData("Commodity", "denomination", "troy ounce / contract")]
    [InlineData("CryptoCurrency", "network", "Private chain: synthetic-v9")]
    [InlineData("Cfd", "underlyingAssetClass", "VendorBasket/42")]
    [InlineData("OtherSecurity", "category", "Vendor category: A")]
    [InlineData("OtherSecurity", "subType", "Vendor subtype: B")]
    [InlineData("InvestmentFund", "fundType", "VendorFund/SidePocket")]
    [InlineData("InvestmentFund", "distributionPolicy", "VendorPolicy/Periodic")]
    [InlineData("DirectLoan", "pricingSource", "Source: loan desk / v2")]
    [InlineData("NonDeliverableForward", "fixingSource", "Source: published fixing / v2")]
    public void VerbatimLabels_AreNotInferredOrCanonicalizedIntoDifferentFacts(string assetClass, string field, string label)
    {
        var payload = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!;
        payload[field] = label;
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass, payload);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));
        var replay = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });

        replay.AssetSpecificTerms.GetProperty(field).GetString().Should().Be(label);
    }

    [Theory]
    [InlineData(typeof(CommodityTerms), "CommodityType", "Denomination", "ContractSize")]
    [InlineData(typeof(CryptoTerms), "Network")]
    [InlineData(typeof(CfdTerms), "UnderlyingAssetClass", "UnderlyingDescription", "Leverage")]
    [InlineData(typeof(FxForwardTerms), "SettlementType")]
    [InlineData(typeof(NonDeliverableForwardTerms), "FixingSource")]
    [InlineData(typeof(OtherSecurityTerms), "Category", "SubType")]
    [InlineData(typeof(InvestmentFundTerms), "FundType", "NavCurrency", "DistributionPolicy", "IsStableNav", "PricingSource")]
    [InlineData(typeof(DirectLoanTerms), "PricingSource")]
    public void ContractedWrappers_StoreModulesInsteadOfDuplicateScalarFields(Type recordType, params string[] removedFields)
        => FSharpType.GetRecordFields(recordType, null).Select(static property => property.Name)
            .Should().NotIntersectWith(removedFields);

    private static void AssertReplay(string assetClass, string module, params string[] fields)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass, original)
            .GetRawText().Should().Be(original.GetRawText(), "the permanent legacy codec preserves its bytes");
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));
        economic.EconomicTerms.GetProperty(module).ValueKind.Should().Be(JsonValueKind.Object);
        var imageReplay = SecurityEconomicDefinitionAdapter.ToProjection(economic);
        imageReplay.AssetSpecificTerms.GetRawText().Should().Be(original.GetRawText());
        var economicOnly = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        foreach (var field in fields)
        {
            SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass).Should().Contain(field);
            JsonElement.DeepEquals(economicOnly.AssetSpecificTerms.GetProperty(field), original.GetProperty(field))
                .Should().BeTrue($"{assetClass}.{field} survives economic-only replay without reinterpretation");
        }
    }
}
