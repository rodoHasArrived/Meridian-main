using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Application.SecurityMaster;
using Meridian.FinancialOperations.OperationsContinuity;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    [Theory]
    [InlineData("scheduleConventions")]
    [InlineData("callSchedule")]
    [InlineData("putSchedule")]
    public async Task ExplicitJsonNullContainerReplacement_StagesTheUnsetValue(string key)
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", AheadOfWriteFixtures.BondTerms(1, withBlock: false));
        var request = ProfileFieldEdit($"assetSpecificTerms.{key}", "null");

        var staged = await harness.Service.UpdateSecurityFieldAsync(request);

        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        (await harness.Revisions.GetAsync(staged.RevisionId))!.FieldValue.Should().Be("null",
            "an explicit JSON null is an authored unset value, while a null FieldValue withdraws the overlay");
    }

    [Fact]
    public async Task Submit_RevalidatesDeclaredTermsBeforeMutatingApprovalWorkflow()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        var revision = await harness.Revisions.CreateDraftAsync(SecurityId, "analyst", "assetSpecificTerms.couponRate",
            DateTimeOffset.UtcNow, "Rate correction", fieldValue: new SecurityMasterRevisionFieldValue("6"));
        // A retained refresh since staging now carries a declaration that this build must refuse.
        harness.SetProjectionAssetTerms("Bond", new
        {
            schemaVersion = 1,
            termsFormat = "flat",
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            isCallable = false,
            subclass = "Corporate",
            couponType = "Fixed",
            couponRate = 4m,
            paymentFrequency = "Annual",
            par = 100m,
            dayCount = "30/360",
            scheduleConventions = new { businessDayConvention = "NotAConvention", accrualAdjusted = false }
        });
        var request = new SubmitSecurityMasterRevisionRequest(SecurityId, revision.RevisionId, "analyst", "Review",
            WorkflowId: Guid.NewGuid(), Reviewer: "independent-reviewer");

        await harness.Service.Invoking(service => service.SubmitForApprovalAsync(request)).Should().ThrowAsync<ArgumentException>();
        harness.Workflow.Verify(workflow => workflow.SubmitForApprovalAsync(It.IsAny<Guid>(),
            It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        (await harness.Revisions.GetAsync(revision.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }
}
