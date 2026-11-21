using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityTrancheContractContractionTests
{
    [Theory]
    [InlineData("Clo")]
    [InlineData("InterestOnly")]
    [InlineData("Vendor retained tranche family")]
    public void ExactTrancheAndCouponHomes_PreserveFullFlatReplayBytes(string kind)
    {
        var payload = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads["StructuredCredit"])!.AsObject();
        payload["structuredCreditKind"] = kind;
        payload["couponOrIndex"] = "  SOFR + 125 bps  ";
        var before = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("StructuredCredit", payload);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", before));

        economic.EconomicTerms.GetProperty("trancheContract").GetProperty("structuredCreditKind").GetString().Should().Be(kind);
        economic.EconomicTerms.GetProperty("coupon").GetProperty("couponType").GetString().Should().Be("  SOFR + 125 bps  ");
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("StructuredCredit", replayed.AssetSpecificTerms)
            .GetRawText().Should().Be(before.GetRawText());
    }

    [Fact]
    public void UndeclaredTrancheContractMembers_RemainUndeclaredOnReplay()
    {
        var payload = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads["StructuredCredit"])!.AsObject();
        foreach (var field in new[] { "structuredCreditKind", "seniority", "attachmentPoint", "detachmentPoint", "reinvestmentPeriodEnd", "nonCallPeriodEnd" })
            payload[field] = null;
        var before = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("StructuredCredit", payload);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", before));
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });

        SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("StructuredCredit", replayed.AssetSpecificTerms)
            .GetRawText().Should().Be(before.GetRawText());
    }

    [Theory]
    [InlineData("Clo")]
    [InlineData("InterestOnly")]
    [InlineData("PrincipalOnly")]
    [InlineData(null)]
    public void ExactKind_SupersedesStripFlagsAndWritesExactlyOneFlatMember(string? kind)
    {
        var flat = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("StructuredCredit",
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads["StructuredCredit"]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", flat));
        var document = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        document["trancheContract"]!["structuredCreditKind"] = kind;
        document["structuredProduct"]!["isInterestOnly"] = true;
        document["structuredProduct"]!["isPrincipalOnly"] = true;
        var recovered = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(
            JsonSerializer.SerializeToElement(document), "StructuredCredit").Payload;

        recovered.EnumerateObject().Count(static property => property.NameEquals("structuredCreditKind")).Should().Be(1);
        var actual = recovered.GetProperty("structuredCreditKind");
        if (kind is null)
            actual.ValueKind.Should().Be(JsonValueKind.Null);
        else
            actual.GetString().Should().Be(kind);
    }

    [Fact]
    public void StructuredCreditCodec_StoresOnlySharedTypedHomes()
    {
        var fields = FSharpType.GetRecordFields(typeof(StructuredCreditTerms), null);
        fields.Select(static field => field.Name).Should().NotContain(new[]
        {
            "CouponOrIndex", "Kind", "Seniority", "AttachmentPoint", "DetachmentPoint", "ReinvestmentPeriodEnd", "NonCallPeriodEnd"
        });
    }
}
