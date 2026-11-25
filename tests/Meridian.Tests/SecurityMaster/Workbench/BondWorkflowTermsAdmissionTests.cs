using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

internal static class BondWorkflowAdmissionFixtures
{
    public const string Overnight = """{"floatingMethod":"CompoundedOvernight","fixingCalendarId":"USNY","lookbackBusinessDays":2,"observationMethod":"ObservationShift","observationWeighting":"InterestPeriod","lockoutBusinessDays":0}""";
    public const string Inflation = """{"lagMonths":3,"interpolateMonthly":true,"couponFloorRatio":0,"redemptionFloorRatio":1}""";

    public static JsonElement Terms(string kind = "Fixed", int stamp = 2, string? workflow = null)
    {
        var terms = JsonNode.Parse(AheadOfWriteFixtures.BondAtWriteVersion)!.AsObject();
        terms["assetClassTermsVersion"] = stamp;
        terms["couponType"] = kind;
        terms["scheduleConventions"] = JsonSerializer.SerializeToNode(new
        { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "DayCount" });
        if (kind == "Floating")
        {
            // A floating canonical row has no fixed coupon-rate companion. Leaving the fixed
            // fixture's 4.25 here correctly trips the existing orphaned-coupon write guard.
            terms["couponRate"] = null;
            terms["floatingIndex"] = "SOFR";
            terms["spreadBps"] = 15;
            terms["subclass"] = "FloatingRate";
        }
        if (kind == "InflationLinked")
        {
            terms["inflationIndex"] = "CPI-U";
            terms["inflationBaseIndexValue"] = 100m;
            terms["inflationIndexRatio"] = 1m;
            terms["subclass"] = "InflationLinked";
        }
        if (workflow is not null)
            terms["bondWorkflow"] = JsonNode.Parse(workflow);
        return JsonSerializer.SerializeToElement(terms);
    }
}

public sealed class BondWorkflowTermsAdmissionTests
{
    [Theory]
    [InlineData("Floating", BondWorkflowAdmissionFixtures.Overnight)]
    [InlineData("InflationLinked", BondWorkflowAdmissionFixtures.Inflation)]
    public void BondWorkflowVersionFiveSurvivesTypedRecordAndSnapshotCodec(string kind, string workflow)
    {
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond",
            BondWorkflowAdmissionFixtures.Terms(kind, 5, workflow));
        var typed = SecurityMasterMapping.ToRecord(projection);
        var serialized = new SecurityMasterSnapshotWrapper(typed).AssetSpecificTermsJson;
        using var document = JsonDocument.Parse(serialized);

        document.RootElement.GetProperty("assetClassTermsVersion").GetInt32().Should().Be(5);
        JsonNode.DeepEquals(JsonNode.Parse(document.RootElement.GetProperty("bondWorkflow").GetRawText()),
            JsonNode.Parse(workflow)).Should().BeTrue();
        var second = SecurityMasterMapping.ToRecord(projection with { AssetSpecificTerms = document.RootElement.Clone() });
        new SecurityMasterSnapshotWrapper(second).AssetSpecificTermsJson.Should().Be(serialized);
    }

    [Fact]
    public void BondWorkflowOlderStampResidueCannotActivateOrChangeHistoricalCodecBytes()
    {
        var old = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", BondWorkflowAdmissionFixtures.Terms(stamp: 4));
        var stray = old with
        {
            AssetSpecificTerms = BondWorkflowAdmissionFixtures.Terms(stamp: 4,
            workflow: BondWorkflowAdmissionFixtures.Overnight)
        };
        var baseline = new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(old));
        var inert = new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(stray));

        inert.AssetSpecificTermsJson.Should().Be(baseline.AssetSpecificTermsJson,
            "a v4 row cannot acquire v5 overnight economics through a stray JSON key");
        using var document = JsonDocument.Parse(inert.AssetSpecificTermsJson);
        document.RootElement.TryGetProperty("bondWorkflow", out _).Should().BeFalse();
        document.RootElement.GetProperty("assetClassTermsVersion").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData("{\"floatingMethod\":\"MysteryAverage\"}")]
    [InlineData("{\"observationMethod\":\"MagicShift\"}")]
    [InlineData("{\"lookbackBusinessDays\":1.5}")]
    [InlineData("{\"lookbackBusinessDays\":-1}")]
    [InlineData("{\"lockoutBusinessDays\":31}")]
    [InlineData("{\"lagMonths\":-1}")]
    [InlineData("{\"lagMonths\":25}")]
    [InlineData("{\"redemptionFloorRatio\":-1}")]
    [InlineData("{\"floatingMethod\":\"CompoundedOvernight\"}")]
    [InlineData("{\"inventedConvention\":true}")]
    public void BondWorkflowInvalidConventionsCannotPassGovernedFieldAdmission(string workflow)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.bondWorkflow", workflow,
            out _, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }
}

public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    [Theory]
    [InlineData("Floating", BondWorkflowAdmissionFixtures.Overnight)]
    [InlineData("InflationLinked", BondWorkflowAdmissionFixtures.Inflation)]
    public async Task BondWorkflowExplicitGovernedEditStagesPublishesAndPersistsVersionFive(string kind, string workflow)
    {
        var revisions = new InMemorySecurityMasterRevisionStore();
        var store = new Mock<ISecurityMasterStore>(MockBehavior.Loose);
        var canonical = new CanonicalAmendHarness(store, revisionStore: revisions);
        var amender = new RecordingAmender(canonical.Service);
        var merge = new ApprovedFieldEditCanonicalMergeHandler(revisions,
            NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, store.Object, amender);
        var harness = new Harness(currentVersion: 3, handlers: [merge], revisions: revisions);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", BondWorkflowAdmissionFixtures.Terms(kind));
        var existing = await harness.ProjectionStore.Object.GetProjectionAsync(SecurityId);
        store.Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        const string path = "assetSpecificTerms.bondWorkflow";

        var staged = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit(path, workflow));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        var draft = (await revisions.GetAsync(staged.RevisionId))!;
        draft.FieldValue.Should().Be(workflow);
        await revisions.TransitionAsync(staged.RevisionId, SecurityMasterRevisionStateDto.Draft,
            SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await revisions.TransitionAsync(staged.RevisionId, SecurityMasterRevisionStateDto.Submitted,
            SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(SecurityId,
            staged.RevisionId, "ops.analyst", "ops.reviewer"));

        amender.Failures.Should().BeEmpty();
        (await revisions.GetAsync(staged.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
        var persisted = canonical.Upserted.Should().ContainSingle().Which;
        persisted.AssetSpecificTerms.GetProperty("assetClassTermsVersion").GetInt32().Should().Be(5);
        JsonNode.DeepEquals(JsonNode.Parse(persisted.AssetSpecificTerms.GetProperty("bondWorkflow").GetRawText()),
            JsonNode.Parse(workflow)).Should().BeTrue();
        canonical.Appended.Should().ContainSingle("the convention change is canonical economic evidence, not only an overlay");

        var encodedAgain = new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(persisted));
        using var replayed = JsonDocument.Parse(encodedAgain.AssetSpecificTermsJson);
        replayed.RootElement.GetProperty("assetClassTermsVersion").GetInt32().Should().Be(5);
        JsonNode.DeepEquals(JsonNode.Parse(replayed.RootElement.GetProperty("bondWorkflow").GetRawText()),
            JsonNode.Parse(workflow)).Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"floatingMethod\":\"NotAConvention\"}")]
    [InlineData("{\"lookbackBusinessDays\":-1}")]
    public async Task BondWorkflowRejectedConventionLeavesNoDraft(string workflow)
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", BondWorkflowAdmissionFixtures.Terms());

        await harness.Service.Invoking(service => service.UpdateSecurityFieldAsync(
            ProfileFieldEdit("assetSpecificTerms.bondWorkflow", workflow))).Should().ThrowAsync<ArgumentException>();

        harness.Overrides.Verify(o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()), Times.Never);
    }
}
