#if WINDOWS
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SecurityPassportEditorViewModelTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ClassifyWorkbenchError_MapsStatusAndCodeToOperatorMessage()
    {
        SecurityPassportEditorViewModel
            .ClassifyWorkbenchError(409, "{\"error\":\"version-conflict\",\"currentVersion\":9}")
            .Should().Contain("v9");
        SecurityPassportEditorViewModel
            .ClassifyWorkbenchError(409, "{\"error\":\"revision-state-conflict\",\"message\":\"not approved\"}")
            .Should().Be("not approved");
        SecurityPassportEditorViewModel
            .ClassifyWorkbenchError(422, "{\"error\":\"workflow-required\"}")
            .Should().Contain("approval workflow");
        SecurityPassportEditorViewModel.ClassifyWorkbenchError(403, null).Should().Contain("permission");
        SecurityPassportEditorViewModel.ClassifyWorkbenchError(401, null).Should().Contain("authenticated");
    }

    [Fact]
    public void PublishCommand_IsDisabledUntilApproved()
    {
        var viewModel = new SecurityPassportEditorViewModel(new Mock<IWorkstationSecurityMasterApiClient>().Object)
        {
            SecurityId = SecurityId,
            Version = 7
        };

        viewModel.PublishCommand.CanExecute(null).Should().BeFalse();

        viewModel.RevisionState = SecurityMasterRevisionStateDto.Approved;
        viewModel.PublishCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SaveDraft_PostsLoadedVersionAndAdvancesLifecycle()
    {
        UpdateSecurityFieldRequest? captured = null;
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client
            .Setup(c => c.UpdateFieldAsync(SecurityId, It.IsAny<UpdateSecurityFieldRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateSecurityFieldRequest, CancellationToken>((_, request, _) => captured = request)
            .ReturnsAsync(ApiResponse<SecurityMasterEditResultDto>.Ok(EditResult(SecurityMasterRevisionStateDto.Draft, 8)));

        var viewModel = new SecurityPassportEditorViewModel(client.Object)
        {
            SecurityId = SecurityId,
            Version = 7,
            FieldPath = "EconomicDefinition.Coupon",
            Justification = "Vendor confirmation #4821."
        };

        viewModel.SaveDraftCommand.CanExecute(null).Should().BeTrue();
        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.ExpectedVersion.Should().Be(7);
        captured.FieldPath.Should().Be("EconomicDefinition.Coupon");
        viewModel.RevisionState.Should().Be(SecurityMasterRevisionStateDto.Draft);
        viewModel.Version.Should().Be(8);
        viewModel.SubmitCommand.CanExecute(null).Should().BeTrue();
        viewModel.BannerIsError.Should().BeFalse();
    }

    [Fact]
    public async Task SaveDraft_OnVersionConflict_RaisesReloadBanner()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client
            .Setup(c => c.UpdateFieldAsync(SecurityId, It.IsAny<UpdateSecurityFieldRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterEditResultDto>.Fail("{\"error\":\"version-conflict\",\"currentVersion\":9}", 409));

        var viewModel = new SecurityPassportEditorViewModel(client.Object)
        {
            SecurityId = SecurityId,
            Version = 7,
            FieldPath = "EconomicDefinition.Coupon",
            Justification = "reason"
        };

        await viewModel.SaveDraftCommand.ExecuteAsync(null);

        viewModel.BannerIsError.Should().BeTrue();
        viewModel.HasBanner.Should().BeTrue();
        viewModel.BannerText.Should().Contain("v9");
        viewModel.RevisionState.Should().BeNull();
    }

    [Fact]
    public async Task Submit_StoresWorkflowVersionInExpectedWorkflowVersion_NotPassportVersion()
    {
        // A workflow-backed submit returns the operations approval-workflow version, which must feed the
        // next ExpectedWorkflowVersion (so Approve matches) and must NOT overwrite the passport token.
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client
            .Setup(c => c.SubmitRevisionAsync(SecurityId, It.IsAny<SubmitSecurityMasterRevisionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterEditResultDto>.Ok(EditResult(SecurityMasterRevisionStateDto.Submitted, version: 42)));

        var viewModel = new SecurityPassportEditorViewModel(client.Object)
        {
            SecurityId = SecurityId,
            Version = 7,
            RevisionId = Guid.NewGuid(),
            RevisionState = SecurityMasterRevisionStateDto.Draft,
            WorkflowId = Guid.NewGuid(),
            ExpectedWorkflowVersion = 1,
            Reviewer = "independent.reviewer"
        };

        await viewModel.SubmitCommand.ExecuteAsync(null);

        viewModel.RevisionState.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        viewModel.ExpectedWorkflowVersion.Should().Be(42, "the returned workflow version drives the next gate command");
        viewModel.Version.Should().Be(7, "the passport optimistic token must not be clobbered by the workflow version");
        viewModel.ApproveCommand.CanExecute(null).Should().BeTrue();
    }

    private static SecurityMasterEditResultDto EditResult(SecurityMasterRevisionStateDto state, long version)
        => new(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            NewVersion: version,
            State: state,
            ChangeEntry: new SecurityMasterChangeHistoryItemDto(
                ChangeId: Guid.NewGuid().ToString("N"),
                StreamVersion: version,
                EventType: "OperatorFieldAnnotation",
                ChangedAtUtc: DateTimeOffset.UtcNow,
                EffectiveAtUtc: DateTimeOffset.UtcNow,
                Actor: "session.actor",
                Origin: "Operator",
                SourceSystem: "operator",
                SourceRecordId: null,
                Reason: "reason",
                Summary: "summary",
                ChangedFields: new[] { "EconomicDefinition.Coupon" },
                ChangedFieldsSummary: "EconomicDefinition.Coupon"));
}
#endif
