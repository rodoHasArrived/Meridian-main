using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationBondValueNormalizationTests
{
    [Theory]
    [InlineData("Bond", true)]
    [InlineData("bOnD", true)]
    [InlineData(" Bond ", true)]
    [InlineData("FixedIncome", true)]
    [InlineData("fixedincome", true)]
    [InlineData("CorporateBond", true)]
    [InlineData("MunicipalBond", true)]
    [InlineData("AssetBackedSecurity", false)]
    [InlineData("StructuredNote", false)]
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
                issues.Should().ContainSingle(issue => issue.Code == "bond-value.conventions.incomplete");
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

        issues.Should().ContainSingle(issue => issue.Code == "bond-value.conventions.incomplete");
        record["bondValueReconciliation"]!["status"]!.GetValue<string>().Should().Be("Blocked");
        record["bondValueReconciliation"]!["normalizedValue"].Should().BeNull();
    }
}
