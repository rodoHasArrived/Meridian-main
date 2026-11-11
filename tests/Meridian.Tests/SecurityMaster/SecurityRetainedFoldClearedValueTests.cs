using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// A row rebuilt from an economic-only document can keep the document as its retained carry (here
/// because the document holds an ESG module no flat row expresses). The stage-3 module homes made
/// more values recoverable at flat keys, so an amendment can now clear a value the retained
/// document still holds. The fold must not put it back: the flat row could carry the value, so
/// empty there means cleared, not inexpressible.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityRetainedFoldClearedValueTests
{
    private const string Esg =
        """{"esgScore":72.5,"esgProvider":"MSCI","esgRating":"AA","esgAsOf":"2025-11-30","isGreenBond":true,"isSocialBond":false,"isSustainabilityLinked":false,"unSdgAlignments":["SDG 7"],"paiIndicatorFlags":["GHG"]}""";

    [Theory]
    [InlineData("DirectLoan", "covenants", "[]")]
    [InlineData("Bond", "principalSchedule", "[]")]
    [InlineData("StructuredCredit", "structuredCreditKind", "null")]
    public void AnAmendmentThatClearsARecoverableValue_IsNotUndoneByTheRetainedDocument(
        string assetClass, string flatKey, string cleared)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        IsSet(original, flatKey).Should().BeTrue($"precondition: the {assetClass} twin declares {flatKey}");
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));
        var document = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        document["esg"] = JsonNode.Parse(Esg);

        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(
            economic with { EconomicTerms = ToElement(document), LegacyAssetSpecificTerms = null });
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(replayed.AssetSpecificTerms, out _)
            .Should().BeTrue("precondition: a document the flat row cannot hold completely keeps its carry");
        IsSet(replayed.AssetSpecificTerms, flatKey)
            .Should().BeTrue("precondition: the value is recovered at its flat key");

        // The amendment: the complete terms again with the value cleared. The retained document
        // travels with the row, as the service carries it.
        var amendedTerms = JsonNode.Parse(original.GetRawText())!.AsObject();
        amendedTerms[flatKey] = JsonNode.Parse(cleared);
        var amended = SecurityEconomicDefinitionAdapter.CarryRetainedEconomicTerms(
            replayed,
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, ToElement(amendedTerms)));
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(amended.AssetSpecificTerms, out _).Should().BeTrue();

        var next = SecurityEconomicDefinitionAdapter.ToEconomicRecord(amended);

        next.EconomicTerms.GetProperty("esg").GetProperty("esgRating").GetString().Should().Be("AA",
            "what the flat row cannot hold still folds back from the retained document");
        var replayedAgain = SecurityEconomicDefinitionAdapter.ToProjection(next with { LegacyAssetSpecificTerms = null });
        IsSet(replayedAgain.AssetSpecificTerms, flatKey)
            .Should().BeFalse($"the cleared {flatKey} must not come back on the next replay");
    }

    [Fact]
    public void Fold_LeavesAFlatAuthoritativeFieldCleared_AndStillFillsTheRest()
    {
        using var rebuilt = JsonDocument.Parse(
            """{"schemaVersion":2,"termsFormat":"economic","loanCovenants":{"covenants":[]},"trancheContract":{"structuredCreditKind":null,"seniority":"Mezzanine"},"principalSchedule":null,"esg":null}""");
        using var retained = JsonDocument.Parse(
            """{"schemaVersion":2,"termsFormat":"economic","loanCovenants":{"covenants":[{"covenantType":"MaxLeverage","notes":null,"threshold":"4.5x"}]},"trancheContract":{"structuredCreditKind":"Clo","seniority":"Senior"},"principalSchedule":{"principalSchedule":[{"amount":400,"paymentDate":"2030-06-15"}]},"esg":{"esgRating":"AA"}}""");

        var folded = SecurityTermsEnvelope.FoldRetainedEconomicTerms(
            rebuilt.RootElement,
            retained.RootElement,
            [("loanCovenants", "covenants"), ("trancheContract", "structuredCreditKind"), ("principalSchedule", "principalSchedule")]);

        folded.GetProperty("loanCovenants").GetProperty("covenants").GetArrayLength().Should().Be(0,
            "an authoritative list the row has empty stays empty");
        folded.GetProperty("trancheContract").GetProperty("structuredCreditKind").ValueKind.Should().Be(JsonValueKind.Null,
            "an authoritative scalar the row has cleared stays cleared");
        folded.GetProperty("trancheContract").GetProperty("seniority").GetString().Should().Be("Mezzanine",
            "a rebuilt value still wins");
        folded.GetProperty("principalSchedule").ValueKind.Should().Be(JsonValueKind.Null,
            "a retained module that holds nothing but flat-authoritative content is not folded");
        folded.GetProperty("esg").GetProperty("esgRating").GetString().Should().Be("AA",
            "a module the row cannot express is still filled");

        // Without the authority list the fold is unchanged: it fills whatever is empty.
        var unscoped = SecurityTermsEnvelope.FoldRetainedEconomicTerms(rebuilt.RootElement, retained.RootElement);
        unscoped.GetProperty("loanCovenants").GetProperty("covenants").GetArrayLength().Should().Be(1);
        unscoped.GetProperty("trancheContract").GetProperty("structuredCreditKind").GetString().Should().Be("Clo");
        unscoped.GetProperty("principalSchedule").GetProperty("principalSchedule").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void FlatAuthoritativeFields_AreTheStageThreeHomes_NotTheFieldsHistoricalDocumentsHold()
    {
        var bond = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlatAuthoritativeFields("Bond");
        bond.Should().Contain([
            ("principalSchedule", "principalSchedule"), ("bondCoupon", "stepSchedule"), ("debtClassification", "subclass")]);
        bond.Should().NotContain(pair => pair.Module == "issuer" || pair.Module == "call" || pair.Module == "maturity",
            "fields the economic document carried before stage 3 keep the fold: historical documents hold values there the flat decode does not reproduce");
        bond.Should().NotContain(pair => pair.Module == "declaredSchedule",
            "declared call and put lists keep the existing fill, in step with the call summary module");

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlatAuthoritativeFields("Swap")
            .Should().Contain(("swapLegs", "legs"));
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlatAuthoritativeFields("DirectLoan")
            .Should().Contain(("loanCovenants", "covenants"));
        // A flag fallback for documents that predate the module does not make the verbatim source
        // any less the flat row's own value.
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlatAuthoritativeFields("StructuredCredit")
            .Should().Contain(("trancheContract", "structuredCreditKind"));
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlatAuthoritativeFields("NotAClass").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void EveryFlatAuthoritativeField_IsRebuiltFromTheFlatRow(string assetClass)
    {
        // The proof the authority list rests on: for a fully populated twin, each listed field the
        // economic document holds is reproduced from the flat row alone, so the retained document
        // is never needed to supply it.
        var authoritative = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlatAuthoritativeFields(assetClass);
        if (authoritative.Count == 0 || SecurityAssetClassCatalog.GetOrDefault(assetClass).RequiresProfileReference)
        {
            return;
        }

        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));
        var flattened = JsonNode.Parse(
            SecurityAssetSpecificTermsUpcasterChain.Normalize(economic.EconomicTerms, assetClass).Payload.GetRawText())!.AsObject();
        flattened.Remove(SecurityTermsEnvelope.RetainedEconomicTermsKey);

        // A complete row whose recoverable keys come from the economic document alone.
        var row = JsonNode.Parse(original.GetRawText())!.AsObject();
        foreach (var key in SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass))
        {
            row.Remove(key);
            if (flattened.TryGetPropertyValue(key, out var recovered) && recovered is not null)
            {
                row[key] = recovered.DeepClone();
            }
        }

        var rebuilt = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, ToElement(row))).EconomicTerms;

        foreach (var (module, field) in authoritative)
        {
            Raw(rebuilt, module, field).Should().Be(Raw(economic.EconomicTerms, module, field),
                $"{assetClass} must rebuild {module}.{field} from the flat row");
        }
    }

    public static TheoryData<string> DeclaredAssetClasses() => SecurityAssetTermsSchemaRoundTripTests.DeclaredAssetClasses();

    private static string Raw(JsonElement terms, string module, string field)
        => terms.TryGetProperty(module, out var held)
           && held.ValueKind == JsonValueKind.Object
           && held.TryGetProperty(field, out var value)
            ? value.GetRawText()
            : "null";

    private static bool IsSet(JsonElement terms, string key)
        => terms.TryGetProperty(key, out var value)
           && value.ValueKind switch
           {
               JsonValueKind.Null or JsonValueKind.Undefined => false,
               JsonValueKind.Array => value.GetArrayLength() > 0,
               _ => true
           };

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
