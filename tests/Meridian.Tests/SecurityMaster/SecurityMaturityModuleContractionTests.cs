using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMaturityModuleContractionTests
{
    public static TheoryData<string, string?, string?, string?> DateMappings => new()
    {
        { "Option", null, null, "expiry" },
        { "Future", null, null, "expiry" },
        { "Bond", null, "issueDate", "maturity" },
        { "Deposit", null, null, "maturity" },
        { "CertificateOfDeposit", null, null, "maturity" },
        { "CommercialPaper", null, null, "maturity" },
        { "TreasuryBill", null, null, "maturity" },
        { "OtherSecurity", null, null, "maturity" },
        { "Swap", "effectiveDate", "effectiveDate", "maturityDate" },
        { "DirectLoan", null, null, "maturity" },
        { "StructuredCredit", null, null, "maturity" },
        { "CommitmentGuarantee", "effectiveDate", "effectiveDate", "expiryDate" },
        { "Warrant", null, null, "expiry" },
        { "FxForward", "tradeDate", null, "valueDate" },
        { "NonDeliverableForward", "fixingDate", null, "valueDate" },
        { "CreditDefaultSwap", "effectiveDate", "effectiveDate", "maturityDate" },
        { "Swaption", "underlyingEffectiveDate", null, "expiry" },
        { "CapFloor", "effectiveDate", null, "maturityDate" },
        { "StructuredNote", null, "issueDate", "maturity" }
    };

    [Theory]
    [MemberData(nameof(DateMappings))]
    public void DateGroup_RetainsFlatReplayBytesAndItsExistingEconomicMeaning(
        string assetClass, string? effectiveField, string? issueField, string? maturityField)
    {
        var before = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, before);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var expected = JsonSerializer.SerializeToElement(new
        {
            effectiveDate = ReadDate(before, effectiveField),
            issueDate = ReadDate(before, issueField),
            maturityDate = ReadDate(before, maturityField)
        });

        JsonElement.DeepEquals(economic.EconomicTerms.GetProperty("maturity"), expected).Should().BeTrue(
            $"{assetClass}'s date group must keep its established economic date meanings");
        var replayed = SecurityEconomicDefinitionAdapter.ToProjection(economic);
        replayed.AssetSpecificTerms.GetRawText().Should().Be(before.GetRawText());
        SecurityEconomicDefinitionAdapter.ToEconomicRecord(replayed).EconomicTerms.GetProperty("maturity")
            .GetRawText().Should().Be(economic.EconomicTerms.GetProperty("maturity").GetRawText());
    }

    [Theory]
    [InlineData("Deposit", false)]
    [InlineData("OtherSecurity", false)]
    [InlineData("StructuredCredit", true)]
    public void OptionalMaturity_KeepsTheOriginalModuleAbsencePolicy(string assetClass, bool absentModule)
    {
        var payload = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!.AsObject();
        payload["maturity"] = null;
        var flat = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(assetClass, payload);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, flat));

        var module = economic.EconomicTerms.GetProperty("maturity");
        (module.ValueKind == JsonValueKind.Null).Should().Be(absentModule);
        if (!absentModule)
            module.GetProperty("maturityDate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(typeof(BondTerms), "Maturity", "IssueDate")]
    [InlineData(typeof(OptionTerms), "Expiry")]
    [InlineData(typeof(SwapTerms), "EffectiveDate", "MaturityDate")]
    [InlineData(typeof(NonDeliverableForwardTerms), "FixingDate", "ValueDate")]
    [InlineData(typeof(SwaptionTerms), "Expiry", "UnderlyingEffectiveDate")]
    [InlineData(typeof(StructuredCreditTerms), "Maturity")]
    public void Dates_AreStoredOnlyInTheSharedModule(Type recordType, params string[] removedFields)
        => FSharpType.GetRecordFields(recordType, null).Select(static field => field.Name)
            .Should().NotIntersectWith(removedFields);

    private static string? ReadDate(JsonElement terms, string? field)
        => field is not null && terms.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
