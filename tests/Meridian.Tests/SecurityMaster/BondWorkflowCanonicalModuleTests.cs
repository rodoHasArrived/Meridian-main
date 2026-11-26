using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.SecurityMaster.Workbench;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

public sealed class BondWorkflowCanonicalModuleTests
{
    [Theory]
    [InlineData("Floating", BondWorkflowAdmissionFixtures.Overnight)]
    [InlineData("InflationLinked", BondWorkflowAdmissionFixtures.Inflation)]
    public void WorkflowHasRegisteredTypedHomeAndEconomicOnlyReplayCarriesItsCompleteConventions(string kind, string workflow)
    {
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond",
            BondWorkflowAdmissionFixtures.Terms(kind, 5, workflow));
        var record = SecurityMasterMapping.ToRecord(projection);
        var terms = ((SecurityKind.Bond)record.Kind).Item;
        terms.Workflow.Should().NotBeNull();
        FSharpType.IsRecord(terms.Workflow.Value.GetType(), null).Should().BeTrue();
        FSharpType.GetRecordFields(typeof(SecurityTermModules), null).Should().ContainSingle(field =>
            field.Name == "BondWorkflow" && field.PropertyType == typeof(FSharpOption<BondWorkflowTerms>));
        using var canonical = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        using var expected = JsonDocument.Parse(workflow);
        JsonElement.DeepEquals(canonical.RootElement.GetProperty("bondWorkflow"), expected.RootElement).Should().BeTrue();
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(record, projection);
        JsonElement.DeepEquals(economic.EconomicTerms.GetProperty("bondWorkflow"), expected.RootElement).Should().BeTrue();
        var reconstructed = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        reconstructed.AssetSpecificTerms.GetProperty("assetClassTermsVersion").GetInt32().Should().Be(5);
        JsonElement.DeepEquals(reconstructed.AssetSpecificTerms.GetProperty("bondWorkflow"), expected.RootElement).Should().BeTrue();
        var decoded = ((SecurityKind.Bond)SecurityMasterMapping.ToRecord(reconstructed).Kind).Item.Workflow.Value;
        decoded.Should().Be(terms.Workflow.Value);
    }

    [Fact]
    public void ZeroCountsFalseAndZeroFloorsRemainExplicitWhileLegacyAbsenceKeepsItsBytes()
    {
        const string workflow = """{"lagMonths":0,"interpolateMonthly":false,"couponFloorRatio":0,"redemptionFloorRatio":1,"exCouponBusinessDays":0}""";
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond",
            BondWorkflowAdmissionFixtures.Terms("InflationLinked", 5, workflow));
        var typed = ((SecurityKind.Bond)SecurityMasterMapping.ToRecord(projection).Kind).Item.Workflow.Value;
        typed.LagMonths.Value.Should().Be(0);
        typed.InterpolateMonthly.Value.Should().BeFalse();
        typed.CouponFloorRatio.Value.Should().Be(0m);
        typed.ExCouponBusinessDays.Value.Should().Be(0);
        using var expected = JsonDocument.Parse(workflow);
        JsonElement.DeepEquals(BondWorkflowTermsJson.write(typed), expected.RootElement).Should().BeTrue();

        var legacy = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", BondWorkflowAdmissionFixtures.Terms());
        var residue = legacy with { AssetSpecificTerms = BondWorkflowAdmissionFixtures.Terms(stamp: 4, workflow: workflow) };
        var baseline = new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(legacy)).AssetSpecificTermsJson;
        var ignored = new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(residue)).AssetSpecificTermsJson;
        ignored.Should().Be(baseline);
        ((SecurityKind.Bond)SecurityMasterMapping.ToRecord(legacy).Kind).Item.Workflow.Should().BeNull();
        SecurityEconomicDefinitionAdapter.ToEconomicRecord(legacy).EconomicTerms.TryGetProperty("bondWorkflow", out _).Should().BeFalse();
    }

    [Fact]
    public void DirectTypedWriterCannotBypassDeclaredVocabularyAndCountAdmission()
    {
        var invalid = new BondWorkflowTerms(FSharpOption<string>.Some("Simple"), null,
            FSharpOption<int>.Some(-1), null, null, null, null, null, null, null, null);
        Action write = () => BondWorkflowTermsJson.write(invalid);
        write.Should().Throw<InvalidOperationException>().WithMessage("*lookbackBusinessDays*");
        var wrongVocabulary = new BondWorkflowTerms(FSharpOption<string>.Some("MagicAverage"), null,
            null, null, null, null, null, null, null, null, null);
        Action writeUnknown = () => BondWorkflowTermsJson.write(wrongVocabulary);
        writeUnknown.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ClearingFlatWorkflowCannotResurrectConventionsFromRetainedEconomicHistory()
    {
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond",
            BondWorkflowAdmissionFixtures.Terms("InflationLinked", 5, BondWorkflowAdmissionFixtures.Inflation));
        var retained = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var clearedTerms = JsonNode.Parse(BondWorkflowAdmissionFixtures.Terms("InflationLinked", 5).GetRawText())!.AsObject();
        clearedTerms[SecurityTermsEnvelope.RetainedEconomicTermsKey] = JsonNode.Parse(retained.EconomicTerms.GetRawText());
        var cleared = projection with { AssetSpecificTerms = JsonSerializer.SerializeToElement(clearedTerms) };
        SecurityEconomicDefinitionAdapter.ToEconomicRecord(cleared).EconomicTerms.TryGetProperty("bondWorkflow", out _)
            .Should().BeFalse();
    }
}
