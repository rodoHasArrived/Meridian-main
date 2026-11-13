using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationBondValueNormalizationTests
{
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

    [Theory]
    [InlineData("Bond", true)]
    [InlineData("bOnD", true)]
    [InlineData(" Bond ", true)]
    [InlineData("FixedIncome", true)]
    [InlineData("fixedincome", true)]
    [InlineData("CorporateBond", true)]
    [InlineData("MunicipalBond", true)]
    [InlineData("AssetBackedSecurity", false)]
    [InlineData("StructuredCredit", false)]
    [InlineData("StructuredNote", false)]
    [InlineData("CustomAsset", false)]
    [InlineData("Unrecognized", false)]
    [InlineData("Equity", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void ClassOnlyRecord_PreservesItsDeclaredNormalizationBoundary(string? assetClass, bool applies)
    {
        foreach (var nested in new[] { false, true })
        {
            var record = nested
                ? new JsonObject { ["security"] = new JsonObject { ["assetClass"] = assetClass } }
                : new JsonObject { ["assetClass"] = assetClass };
            record["bondValueReconciliation"] = new JsonObject { ["status"] = "Matched", ["normalizedValue"] = "100000" };
            var issues = new List<ValidationIssueDto>();

            ProviderIntegrationBondValueNormalization.AddValidationIssues(record, issues).Should().Be(applies);

            if (applies)
            {
                issues.Should().ContainSingle(issue => issue.Code == "bond-value.conventions.incomplete"
                    && issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
                record["bondValueReconciliation"]!["status"]!.GetValue<string>().Should().Be("Blocked");
                record["bondValueReconciliation"]!["normalizedValue"].Should().BeNull();
            }
            else
            {
                issues.Should().BeEmpty();
                record.ContainsKey("bondValueReconciliation").Should().BeFalse();
            }
        }
    }

    [Theory]
    [InlineData("Bond")]
    [InlineData("CorporateBond")]
    [InlineData("Equity")]
    [InlineData(null)]
    public void ExplicitBondObject_RequiresReviewedConventionsRegardlessOfClass(string? assetClass)
    {
        var record = new JsonObject
        {
            ["assetClass"] = assetClass,
            ["bond"] = new JsonObject(),
            ["bondValueReconciliation"] = new JsonObject { ["status"] = "Matched", ["normalizedValue"] = "100000" }
        };
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationBondValueNormalization.AddValidationIssues(record, issues).Should().BeTrue();

        issues.Should().ContainSingle(issue => issue.Code == "bond-value.conventions.incomplete"
            && issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        record["bondValueReconciliation"]!["status"]!.GetValue<string>().Should().Be("Blocked");
        record["bondValueReconciliation"]!["normalizedValue"].Should().BeNull();
    }
}
