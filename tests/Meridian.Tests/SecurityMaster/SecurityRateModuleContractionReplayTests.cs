using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Rate homes retain the stored decimal and its separately governed unit verbatim.</summary>
public sealed class SecurityRateModuleContractionReplayTests
{
    [Theory]
    [InlineData("Deposit", "coupon", new[] { "interestRate", "dayCount" })]
    [InlineData("CertificateOfDeposit", "coupon", new[] { "couponRate", "dayCount" })]
    [InlineData("CommercialPaper", "discount", new[] { "discountRate" })]
    [InlineData("CommercialPaper", "coupon", new[] { "dayCount" })]
    [InlineData("TreasuryBill", "discount", new[] { "discountRate" })]
    [InlineData("Repo", "discount", new[] { "repoRate" })]
    [InlineData("CashSweep", "discount", new[] { "yieldRate" })]
    [InlineData("DirectLoan", "coupon", new[] { "currentCouponRate" })]
    [InlineData("DirectLoan", "floatingRate", new[] { "referenceIndex", "spreadBps", "resetFrequency" })]
    public void EachRateGroup_PreservesFlatBytesAndEconomicOnlyReplay(string assetClass, string module, string[] fields)
    {
        var before = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        AssertReplay(assetClass, module, fields, before);
    }

    [Theory]
    [InlineData("Deposit", "interestRate", "coupon")]
    [InlineData("CertificateOfDeposit", "couponRate", "coupon")]
    [InlineData("CommercialPaper", "discountRate", "discount")]
    [InlineData("TreasuryBill", "discountRate", "discount")]
    [InlineData("Repo", "repoRate", "discount")]
    [InlineData("CashSweep", "yieldRate", "discount")]
    [InlineData("DirectLoan", "currentCouponRate", "coupon")]
    [InlineData("DirectLoan", "spreadBps", "floatingRate")]
    public void RateHomes_PreserveSubOneZeroAndSignedStoredValues(string assetClass, string field, string module)
    {
        foreach (var rate in new[] { 0.125m, 0m, -0.125m })
        {
            var payload = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!.AsObject();
            payload[field] = rate;
            // Replay must preserve historical stored values even when current create validation
            // forbids that sign for the class. This is the read codec seam, not a new write.
            var before = SerializeStoredThroughDomain(assetClass,
                JsonSerializer.SerializeToElement(payload));
            before.GetProperty(field).GetDecimal().Should().Be(rate);
            AssertReplay(assetClass, module, [field], before);
        }
    }

    [Theory]
    [InlineData(typeof(DepositTerms), new[] { "InterestRate", "DayCount" })]
    [InlineData(typeof(CertificateOfDepositTerms), new[] { "CouponRate", "DayCount" })]
    [InlineData(typeof(CommercialPaperTerms), new[] { "DiscountRate", "DayCount" })]
    [InlineData(typeof(TreasuryBillTerms), new[] { "DiscountRate" })]
    [InlineData(typeof(RepoTerms), new[] { "RepoRate" })]
    [InlineData(typeof(CashSweepTerms), new[] { "YieldRate" })]
    [InlineData(typeof(DirectLoanTerms), new[] { "ReferenceIndex", "SpreadBps", "CurrentCouponRate", "ResetFrequency" })]
    public void PerClassRecords_HaveNoDuplicateRateStorage(Type recordType, string[] removedFields)
        => FSharpType.GetRecordFields(recordType, null).Select(static field => field.Name)
            .Should().NotIntersectWith(removedFields);

    [Fact]
    public void HistoricalDirectLoanWithoutRateModules_RetainsItsFlatImageWithoutRewritingEvent()
    {
        var flat = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("DirectLoan",
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads["DirectLoan"]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("DirectLoan", flat));
        var historical = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        historical["coupon"] = null;
        historical["floatingRate"] = null;
        var oldDocument = JsonSerializer.SerializeToElement(historical);
        var oldEvent = economic with { EconomicTerms = oldDocument };

        SecurityEconomicDefinitionAdapter.ToProjection(oldEvent).AssetSpecificTerms.GetRawText()
            .Should().Be(flat.GetRawText());
        oldEvent.EconomicTerms.GetRawText().Should().Be(oldDocument.GetRawText());
    }

    private static void AssertReplay(string assetClass, string module, string[] fields, JsonElement before)
    {
        SerializeStoredThroughDomain(assetClass, before).GetRawText()
            .Should().Be(before.GetRawText(), "contracting record fields must preserve the flat codec bytes");
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, before));
        economic.EconomicTerms.GetProperty(module).ValueKind.Should().Be(JsonValueKind.Object);
        SecurityEconomicDefinitionAdapter.ToProjection(economic).AssetSpecificTerms.GetRawText()
            .Should().Be(before.GetRawText(), "an economic event's retained image remains authoritative");
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        var after = SerializeStoredThroughDomain(assetClass, replayed.AssetSpecificTerms);
        foreach (var field in fields)
            JsonElement.DeepEquals(after.GetProperty(field), before.GetProperty(field)).Should().BeTrue(
                $"{assetClass}.{field} must survive {module} replay with its exact stored value");
    }

    private static JsonElement SerializeStoredThroughDomain(string assetClass, JsonElement payload)
    {
        var record = SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, payload));
        using var document = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        return document.RootElement.Clone();
    }
}
