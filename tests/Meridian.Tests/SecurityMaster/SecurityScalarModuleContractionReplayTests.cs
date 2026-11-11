using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Each S3-E scalar group has its own replay check across the flat codec boundary.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityScalarModuleContractionReplayTests
{
    [Fact]
    public void CreditProtection_ReplayPreservesReferenceAndProtectionTerms()
        => AssertReplay("CreditDefaultSwap", "creditProtection",
            "referenceEntity", "referenceObligation", "protectionSide", "seniority", "restructuringClause",
            "upfrontPoints", "recoveryRateAssumption", "indexName");

    [Fact]
    public void StructuredPayoff_ReplayPreservesUnderlyingAndContingentPayoff()
        => AssertReplay("StructuredNote", "structuredPayoff", "underlyingDescription", "principalProtectionPct",
            "participationRate", "returnCap", "barrierLevel");

    [Theory]
    [InlineData("PrivateFundInterest", new[] { "vintage", "navDate", "lockup" })]
    [InlineData("PrivateCompanyEquity", new[] { "ownershipPercent", "costBasis", "latestValuation", "transferRestrictions" })]
    [InlineData("RealEstateHolding", new[] { "ownershipPercent", "addressOrMarket", "appraisalValue", "valuationDate", "debtStack" })]
    public void PrivateAssetValuation_ReplayPreservesFactsAndRestrictions(string assetClass, string[] fields)
        => AssertReplay(assetClass, "privateAssetValuation", fields);

    [Theory]
    [InlineData("Option", new[] { "optChainId", "settlementType", "isAdjusted", "lastTradingDt" })]
    [InlineData("Future", new[] { "rootSymbol", "contractMonth", "lastTradingDt", "firstNoticeDt", "deliveryMonthDt", "settlementType", "deliveryLocationCode", "isRollTarget", "rollWindowDays" })]
    public void ListingDetail_ReplayPreservesSeriesSettlementAndDelivery(string assetClass, string[] fields)
        => AssertReplay(assetClass, "listingDetail", fields);

    [Theory]
    [InlineData("Deposit", "depositType")]
    [InlineData("Deposit", "institutionName")]
    [InlineData("CertificateOfDeposit", "issuerName")]
    [InlineData("CommercialPaper", "issuerName")]
    [InlineData("DirectLoan", "borrower")]
    [InlineData("PrivateFundInterest", "gpSponsor")]
    [InlineData("PrivateFundInterest", "strategy")]
    [InlineData("PrivateCompanyEquity", "issuer")]
    [InlineData("PrivateCompanyEquity", "round")]
    [InlineData("RealEstateHolding", "propertyType")]
    [InlineData("CommitmentGuarantee", "counterparty")]
    [InlineData("StructuredNote", "issuerName")]
    [InlineData("Repo", "counterparty")]
    [InlineData("CashSweep", "programName")]
    public void RequiredIssuerAndProgramFields_LegacyReadKeepsWhitespaceForExplicitRepair(string assetClass, string field)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var historical = System.Text.Json.Nodes.JsonNode.Parse(original.GetRawText())!;
        historical[field] = "  ";
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass,
            JsonSerializer.SerializeToElement(historical));
        var record = SecurityMasterMapping.ToRecord(projection);
        using var rewritten = JsonDocument.Parse(new Meridian.FSharp.SecurityMasterInterop.SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        rewritten.RootElement.GetProperty(field).GetString().Should().Be("  ",
            "a read-only codec must preserve malformed historical text for governed repair");
    }

    private static void AssertReplay(string assetClass, string module, params string[] fields)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        // Legacy SecurityProjection replay remains the permanent read-only upcaster: grouped
        // record fields must reproduce exactly the bytes it previously exposed.
        SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass, original)
            .GetRawText().Should().Be(original.GetRawText());
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));
        economic.EconomicTerms.GetProperty(module).ValueKind.Should().Be(JsonValueKind.Object);
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        foreach (var field in fields)
        {
            SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass).Should().Contain(field);
            var expected = original.GetProperty(field);
            var actual = replayed.AssetSpecificTerms.GetProperty(field);
            if (expected.ValueKind == JsonValueKind.Number)
                actual.GetDecimal().Should().Be(expected.GetDecimal(), $"{assetClass}.{field} survives economic-only replay");
            else
                actual.GetRawText().Should().Be(expected.GetRawText(), $"{assetClass}.{field} survives economic-only replay");
        }
    }
}
