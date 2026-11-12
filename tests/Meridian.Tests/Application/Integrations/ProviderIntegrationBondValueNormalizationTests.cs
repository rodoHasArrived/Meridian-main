using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationBondValueNormalizationTests
{
    [Theory]
    [InlineData("Bond")]
    [InlineData("FixedIncome")]
    [InlineData("CorporateBond")]
    [InlineData("MunicipalBond")]
    public void BondClassifications_RequireExplicitBondConsiderationInputs(string assetClass)
    {
        var record = new JsonObject { ["assetClass"] = assetClass };
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationBondValueNormalization.AddValidationIssues(record, issues).Should().BeTrue();

        issues.Should().Contain(issue => issue.Code == "bond-value.conventions.incomplete"
            && issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        record["bondValueReconciliation"]!["normalizedValue"].Should().BeNull();
    }

    [Theory]
    [InlineData("Equity")]
    [InlineData("StructuredCredit")]
    [InlineData("StructuredNote")]
    [InlineData("CustomAsset")]
    [InlineData("Unrecognized")]
    public void OtherClassifications_DoNotInheritBondNormalization(string assetClass)
    {
        var record = new JsonObject
        {
            ["security"] = new JsonObject { ["assetClass"] = assetClass },
            ["bondValueReconciliation"] = new JsonObject { ["status"] = "Matched" }
        };
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationBondValueNormalization.AddValidationIssues(record, issues).Should().BeFalse();

        issues.Should().BeEmpty();
        record.ContainsKey("bondValueReconciliation").Should().BeFalse();
    }

    [Fact]
    public void ExplicitBondPayload_PreservesExactConsiderationForAnUnregisteredClassification()
    {
        var record = JsonNode.Parse("""
            {"assetClass":"ProviderDebtLabel", "bond":{
              "originalFace":"9007199254740993", "currentFactor":"1",
              "quotationConvention":"Per100", "priceBasis":"Clean", "marketValueBasis":"Clean",
              "accruedInterest":{"amount":"0", "currency":"USD"}},
             "price":{"amount":"100", "currency":"USD"},
             "marketValue":{"amount":"9007199254740993", "currency":"USD"}}
            """)!.AsObject();
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationBondValueNormalization.AddValidationIssues(record, issues).Should().BeTrue();

        issues.Should().BeEmpty();
        var evidence = record["bondValueReconciliation"]!;
        evidence["providerValue"]!.GetValue<string>().Should().Be("9007199254740993");
        evidence["normalizedValue"]!.GetValue<string>().Should().Be("9007199254740993");
        evidence["variance"]!.GetValue<string>().Should().Be("0");
    }
}
