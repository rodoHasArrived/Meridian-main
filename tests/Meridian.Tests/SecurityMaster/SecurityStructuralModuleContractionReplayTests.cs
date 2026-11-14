using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityStructuralModuleContractionReplayTests
{
    [Theory]
    [InlineData("{\"couponRate\":2}", "{\"couponRate\":1}")]
    [InlineData("{\"couponType\":\"Fixed\"}", "{\"couponType\":\"Floating\"}")]
    [InlineData("{\"dayCount\":\"ACT/360\"}", "{\"dayCount\":\"30/360\"}")]
    [InlineData("{\"couponRate\":2}", "{\"couponRate\":null}")]
    public void StructuralReplay_RefusesContradictoryPopulatedSummaries(string summary, string authoritative)
    {
        using var document = JsonDocument.Parse("{\"schemaVersion\":2,\"termsFormat\":\"economic\",\"coupon\":"
            + summary + ",\"bondCoupon\":" + authoritative + "}");
        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement, "Bond");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*conflicts with compatibility summary*");
    }

    [Theory]
    [InlineData("{\"couponRate\":1.00}")]
    [InlineData("{\"couponRate\":null}")]
    [InlineData("{}")]
    public void StructuralReplay_AcceptsMatchingOrAbsentSummaries_AndKeepsTheAuthority(string summary)
    {
        using var document = JsonDocument.Parse("{\"schemaVersion\":2,\"termsFormat\":\"economic\",\"coupon\":"
            + summary + ",\"bondCoupon\":{\"couponRate\":1.0000}}");
        var flat = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement, "Bond").Payload;
        flat.GetProperty("couponRate").GetRawText().Should().Be("1.0000");
    }

    [Fact]
    public void StructuralReplay_ComparesScaledSummariesInTheirOwnUnit_WithoutRoundingTheAuthority()
    {
        const decimal raw = 1.2345678901234567890123456789m;
        var economics = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            termsFormat = "economic",
            coupon = new { couponRate = raw / 10000m },
            contractualRate = new { fixedCouponBps = raw }
        });
        var flat = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(economics, "CreditDefaultSwap").Payload;
        flat.GetProperty("fixedCouponBps").GetDecimal().Should().Be(raw);

        var conflicting = System.Text.Json.Nodes.JsonNode.Parse(economics.GetRawText())!;
        conflicting["coupon"]!["couponRate"] = 0.5m;
        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(
            JsonSerializer.SerializeToElement(conflicting), "CreditDefaultSwap");
        replay.Should().Throw<InvalidOperationException>().WithMessage("*conflicts with compatibility summary*");
    }

    [Theory]
    [InlineData("Quarterly", true)]
    [InlineData("Annual", false)]
    public void StructuralReplay_RespectsHistoricalCadenceNormalization_AndPreservesRawSpelling(string summary, bool agrees)
    {
        var economics = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            termsFormat = "economic",
            coupon = new { paymentFrequency = summary },
            contractualRate = new { paymentFrequency = " Quarterly " }
        });
        if (agrees)
            SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(economics, "StructuredNote").Payload
                .GetProperty("paymentFrequency").GetString().Should().Be(" Quarterly ");
        else
        {
            Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(economics, "StructuredNote");
            replay.Should().Throw<InvalidOperationException>().WithMessage("*conflicts with compatibility summary*");
        }
    }

    [Theory]
    [InlineData("\"bondCoupon\":{\"couponRate\":1,\"couponRate\":2}")]
    [InlineData("\"bondCoupon\":{\"couponRate\":1},\"bondCoupon\":{\"couponRate\":2}")]
    [InlineData("\"loanCovenants\":{\"couponRate\":1}")]
    [InlineData("\"bondCoupon\":{\"couponRate\":1},\"bondCoupon\":null")]
    [InlineData("\"bondCoupon\":[]")]
    public void StructuralReplay_RefusesAmbiguousOrCrossModuleFields(string members)
    {
        using var document = JsonDocument.Parse("{\"schemaVersion\":2,\"termsFormat\":\"economic\"," + members + "}");
        Action replay = () => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement, "Bond");
        replay.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Bond", "bondCoupon", "couponType", "couponRate", "floatingIndex", "spreadBps", "capRate", "floorRate", "stepSchedule", "inflationIndex", "inflationBaseIndexValue", "inflationIndexRatio", "dayCount")]
    [InlineData("Bond", "principalSchedule", "principalSchedule")]
    [InlineData("DirectLoan", "principalSchedule", "principalSchedule")]
    [InlineData("Swap", "swapLegs", "legs")]
    [InlineData("DirectLoan", "loanCovenants", "covenants")]
    [InlineData("CommitmentGuarantee", "loanCovenants", "covenants")]
    public void EachStructuralGroup_PreservesFlatBytesAndEconomicReplay(string assetClass, string module, params string[] fields)
    {
        var flat = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass, flat).GetRawText()
            .Should().Be(flat.GetRawText());
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, flat));
        economic.EconomicTerms.GetProperty(module).ValueKind.Should().Be(JsonValueKind.Object);
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        foreach (var field in fields)
        {
            var expected = flat.GetProperty(field);
            var actual = replayed.AssetSpecificTerms.GetProperty(field);
            // F# record grouping cannot change any stored term value or list order.
            JsonElement.DeepEquals(actual, expected).Should().BeTrue($"{assetClass}.{field} must survive {module} replay");
        }
    }
}
