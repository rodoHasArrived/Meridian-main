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
public sealed class SecurityEquityCapitalContractionTests
{
    private const string HistoricalRefusal = "Equity economic replay requires retained legacy asset-specific terms; the economic payload cannot reconstruct the complete classification and preferred/convertible terms.";

    [Theory]
    [InlineData(null)]
    [InlineData("Common")]
    [InlineData("Preferred")]
    [InlineData("Convertible")]
    [InlineData("ConvertiblePreferred")]
    [InlineData("Other")]
    public void CompleteCapitalModule_ReplaysEveryDiscriminantWithoutChangingFlatBytes(string? classification)
    {
        var payload = FullEquity(classification);
        // Unknown voting-right and Other-classification labels remain verbatim typed union cases.
        payload["votingRightsCat"] = "InvestorVeto";
        var before = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity", payload);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity", before));

        var module = economic.EconomicTerms.GetProperty("equityCapital");
        module.ValueKind.Should().Be(JsonValueKind.Object);
        module.GetProperty("classification").GetRawText().Should().Be(before.GetProperty("classification").GetRawText());
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        var after = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity", replayed.AssetSpecificTerms);

        after.GetRawText().Should().Be(before.GetRawText());
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(replayed.AssetSpecificTerms, out _).Should().BeFalse(
            "a complete canonical equity module reconstructs its own flat fields and summaries exactly");
    }

    [Theory]
    [InlineData("Pari", null, "Fixed")]
    [InlineData("Senior", "2.25", "Floating")]
    [InlineData("Subordinated", null, "Cumulative")]
    public void PreferredCapital_KeepsParticipationAndLiquidationValues(string kind, string? multiple, string dividendType)
    {
        var payload = FullEquity("ConvertiblePreferred");
        var preferred = payload["preferredTerms"]!.AsObject();
        preferred["dividendType"] = dividendType;
        preferred["liquidationPreference"] = new JsonObject
        {
            ["kind"] = kind,
            ["multiple"] = multiple is null ? null : JsonValue.Create(decimal.Parse(multiple, System.Globalization.CultureInfo.InvariantCulture))
        };
        preferred["participationTerms"] = new JsonObject
        {
            ["participatesInCommonDividends"] = false,
            ["additionalDividendThreshold"] = 0.125m
        };
        var before = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity", payload);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity", before));
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });

        SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity", replayed.AssetSpecificTerms)
            .GetRawText().Should().Be(before.GetRawText());
    }

    [Theory]
    [InlineData("module")]
    [InlineData("classification")]
    [InlineData("inconsistent-other-label")]
    [InlineData("preferredTerms.participationTerms")]
    [InlineData("preferredTerms.liquidationPreference.multiple")]
    [InlineData("convertibleTerms.underlyingSecurityId")]
    public void HistoricalOrIncompleteCapital_KeepsTheExistingReplayRefusal(string missing)
    {
        var flat = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity", FullEquity("ConvertiblePreferred"));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity", flat));
        var document = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        if (missing == "module")
            document.Remove("equityCapital");
        else if (missing == "inconsistent-other-label")
            document["equityCapital"]!["otherClassification"] = "An unselected label";
        else
        {
            var path = missing.Split('.');
            var parent = document["equityCapital"]!;
            foreach (var segment in path[..^1])
                parent = parent[segment]!;
            parent.AsObject().Remove(path[^1]);
        }
        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic with
        {
            EconomicTerms = JsonSerializer.SerializeToElement(document),
            LegacyAssetSpecificTerms = null
        });

        replay.Should().Throw<InvalidOperationException>().WithMessage(HistoricalRefusal);
    }

    [Fact]
    public void CompleteCapital_WithAdditionalImportedEconomics_KeepsTheirRetainedEvidence()
    {
        var flat = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity", FullEquity("Preferred"));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity", flat));
        var document = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        document["fund"] = new JsonObject { ["fundFamily"] = "Imported external family" };
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic with
        {
            EconomicTerms = JsonSerializer.SerializeToElement(document),
            LegacyAssetSpecificTerms = null
        });

        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(replayed.AssetSpecificTerms, out var retained).Should().BeTrue();
        retained.GetProperty("fund").GetProperty("fundFamily").GetString().Should().Be("Imported external family");
        SecurityEconomicDefinitionAdapter.ToEconomicRecord(replayed).EconomicTerms.GetProperty("fund")
            .GetProperty("fundFamily").GetString().Should().Be("Imported external family");
    }

    [Fact]
    public void Capital_ReplacesThePerClassStorageFields()
    {
        FSharpType.GetRecordFields(typeof(EquityTerms), null).Select(static field => field.Name)
            .Should().Equal("Capital");
        FSharpType.GetRecordFields(typeof(PrivateCompanyEquityTerms), null).Select(static field => field.Name)
            .Should().NotContain("ShareClass");
    }

    private static JsonObject FullEquity(string? classification)
    {
        var payload = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Equity"])!.AsObject();
        payload["classification"] = classification;
        payload["otherClassification"] = classification == "Other" ? "Governed special class" : null;
        if (classification is not ("Preferred" or "ConvertiblePreferred"))
            payload["preferredTerms"] = null;
        if (classification is not ("Convertible" or "ConvertiblePreferred"))
            payload["convertibleTerms"] = null;
        return payload;
    }
}
