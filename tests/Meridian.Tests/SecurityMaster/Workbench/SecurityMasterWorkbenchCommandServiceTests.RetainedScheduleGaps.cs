using FluentAssertions;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.SecurityMaster.Workbench;

// Staging judges the effective overlay against the canonical row it overlays: a stored declaration that
// predates a schedule-terms gap rule does not block an unrelated edit, while replacing it is judged in full.
public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    private static object StoredBondWithRetainedEuropeanEnd() => new
    {
        schemaVersion = 1,
        termsFormat = "flat",
        assetClassTermsVersion = 3,
        issueDate = "2026-01-01",
        maturity = "2030-01-01",
        isCallable = true,
        callDate = "2028-01-01",
        subclass = "Corporate",
        couponType = "Fixed",
        couponRate = 4m,
        paymentFrequency = "Annual",
        par = 100m,
        dayCount = "30/360",
        callSchedule = new[] { new { callDate = "2028-01-01", callEndDate = "2028-06-01", exerciseStyle = "European" } }
    };

    [Fact]
    public async Task UnrelatedEdit_OverARetainedDeclarationThatPredatesAGapRule_Stages()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", StoredBondWithRetainedEuropeanEnd());

        var staged = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit("assetSpecificTerms.couponRate", "5"));

        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }

    [Fact]
    public async Task ReplacingTheContainer_IsJudgedUnderTheGapRules()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", StoredBondWithRetainedEuropeanEnd());
        var replacement = """[{"callDate":"2028-01-01","callEndDate":"2028-09-01","exerciseStyle":"European"}]""";

        await harness.Service.Invoking(service => service.UpdateSecurityFieldAsync(ProfileFieldEdit("assetSpecificTerms.callSchedule", replacement)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Declared callSchedule?0? is European, which exercises on its start date only*");
    }
}

// A publish handler refused by the terms capability floor surfaces as that refusal (the API's 503),
// not as a generic publish failure; the revision stays Approved for a retry once the floor is restored.
public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    [Fact]
    public async Task Publish_HandlerRefusedByTheTermsCapabilityFloor_SurfacesTheRefusal_AndLeavesRevisionApproved()
    {
        var refusing = new RecordingHandler(order: 10, onHandle: () =>
            throw Meridian.Tests.Ui.SecurityMasterTermsCapabilityRefusalEndpointsTests.MissingFloorRefusal());
        var harness = new Harness(currentVersion: 6, handlers: [refusing]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        var request = new PublishSecurityMasterRevisionRequest(SecurityId, revisionId, "ops.analyst", "ops.reviewer");

        await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<Meridian.Application.SecurityMaster.SecurityMasterTermsCapabilityRefusedException>();

        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }
}
