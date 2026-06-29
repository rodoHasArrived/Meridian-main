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

    [Fact]
    public void ResolveConflictCommand_IsDisabledUntilConflictInputsArePresent()
    {
        var viewModel = new SecurityPassportEditorViewModel(new Mock<IWorkstationSecurityMasterApiClient>().Object)
        {
            SecurityId = SecurityId,
            Version = 7
        };

        viewModel.ResolveConflictCommand.CanExecute(null).Should().BeFalse();

        viewModel.ConflictId = Guid.NewGuid();
        viewModel.ChosenWinnerSource = "ProviderB";
        viewModel.ResolveConflictCommand.CanExecute(null).Should().BeFalse("a reason is still required");

        viewModel.ConflictReason = "Vendor B confirmed the corporate action.";
        viewModel.ResolveConflictCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveConflict_PostsChosenWinnerAndAdvancesPassportVersion()
    {
        var conflictId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        ResolveSourceConflictRequest? captured = null;
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client
            .Setup(c => c.ResolveConflictAsync(SecurityId, It.IsAny<ResolveSourceConflictRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ResolveSourceConflictRequest, CancellationToken>((_, request, _) => captured = request)
            .ReturnsAsync(ApiResponse<SecurityMasterConflictResolutionDto>.Ok(
                new SecurityMasterConflictResolutionDto(
                    ConflictId: conflictId,
                    PolicyWinnerSource: "ProviderA",
                    ChosenWinnerSource: "ProviderB",
                    IsPolicyDeviation: true,
                    Reason: "Vendor B confirmed the corporate action.",
                    NewVersion: 9)));

        var viewModel = new SecurityPassportEditorViewModel(client.Object)
        {
            SecurityId = SecurityId,
            Version = 7,
            ConflictId = conflictId,
            ChosenWinnerSource = "ProviderB",
            ConflictReason = "Vendor B confirmed the corporate action.",
            AcknowledgePolicyDeviation = true
        };

        await viewModel.ResolveConflictCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.ConflictId.Should().Be(conflictId);
        captured.ExpectedVersion.Should().Be(7, "the passport version is the optimistic-concurrency token");
        captured.ChosenWinnerSource.Should().Be("ProviderB");
        captured.AcknowledgePolicyDeviation.Should().BeTrue();
        viewModel.Version.Should().Be(9, "the resolution result advances the passport version");
        viewModel.RevisionState.Should().BeNull("conflict resolution does not touch the draft lifecycle");
        viewModel.BannerIsError.Should().BeFalse();
        viewModel.StatusText.Should().Contain("policy deviation");
    }

    [Fact]
    public async Task ResolveConflict_OnVersionConflict_RaisesReloadBanner()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client
            .Setup(c => c.ResolveConflictAsync(SecurityId, It.IsAny<ResolveSourceConflictRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<SecurityMasterConflictResolutionDto>.Fail("{\"error\":\"version-conflict\",\"currentVersion\":11}", 409));

        var viewModel = new SecurityPassportEditorViewModel(client.Object)
        {
            SecurityId = SecurityId,
            Version = 7,
            ConflictId = Guid.NewGuid(),
            ChosenWinnerSource = "ProviderB",
            ConflictReason = "reason"
        };

        await viewModel.ResolveConflictCommand.ExecuteAsync(null);

        viewModel.BannerIsError.Should().BeTrue();
        viewModel.BannerText.Should().Contain("v11");
        viewModel.Version.Should().Be(7, "a rejected resolution must not advance the optimistic token");
    }

    [Fact]
    public void Writes_AreDisabledUntilAPassportIsLoaded()
    {
        // Opened from shell navigation without a passport context: SecurityId is empty, so no governed
        // write may post against the all-zero security id even once field inputs are present.
        var viewModel = new SecurityPassportEditorViewModel(new Mock<IWorkstationSecurityMasterApiClient>().Object)
        {
            FieldPath = "EconomicDefinition.Coupon",
            Justification = "Vendor confirmation #4821."
        };

        viewModel.HasLoadedPassport.Should().BeFalse();
        viewModel.SaveDraftCommand.CanExecute(null).Should().BeFalse();

        viewModel.SecurityId = SecurityId;

        viewModel.HasLoadedPassport.Should().BeTrue();
        viewModel.SaveDraftCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Parameter_HydratesIdentityAndConcurrencyTokenFromNavigationContext()
    {
        var viewModel = new SecurityPassportEditorViewModel(new Mock<IWorkstationSecurityMasterApiClient>().Object)
        {
            Parameter = new SecurityPassportEditorParameter(
                SecurityId: SecurityId,
                Version: 12,
                Symbol: "ACME",
                AssetClass: "Equity",
                FundProfileId: "fund-1")
        };

        viewModel.SecurityId.Should().Be(SecurityId);
        viewModel.Version.Should().Be(12);
        viewModel.Symbol.Should().Be("ACME");
        viewModel.AssetClass.Should().Be("Equity");
        viewModel.FundProfileId.Should().Be("fund-1");
        viewModel.HasLoadedPassport.Should().BeTrue();
        viewModel.RevisionState.Should().BeNull("a freshly-loaded passport has no working revision");
    }

    [Fact]
    public void Parameter_RehydrationClearsPriorWriteInputs()
    {
        // The editor instance is reused across contextual launches, so hydrating a new passport must
        // clear the previous security's field-edit, conflict, and approval inputs — otherwise a stale
        // draft could post against the newly assigned security id.
        var viewModel = new SecurityPassportEditorViewModel(new Mock<IWorkstationSecurityMasterApiClient>().Object)
        {
            Parameter = new SecurityPassportEditorParameter(SecurityId, Version: 7),
            FieldPath = "EconomicDefinition.Coupon",
            NewValue = "5.25",
            EffectiveFrom = new DateTime(2026, 4, 20),
            Justification = "first edit",
            ConflictId = Guid.NewGuid(),
            ChosenWinnerSource = "ProviderB",
            ConflictReason = "reason",
            AcknowledgePolicyDeviation = true,
            WorkflowId = Guid.NewGuid(),
            ExpectedWorkflowVersion = 3,
            Reviewer = "independent.reviewer",
            ReportPackId = "rp-1"
        };

        var nextSecurityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        viewModel.Parameter = new SecurityPassportEditorParameter(nextSecurityId, Version: 12, AssetClass: "Bond");

        viewModel.SecurityId.Should().Be(nextSecurityId);
        viewModel.Version.Should().Be(12);
        viewModel.AssetClass.Should().Be("Bond");
        viewModel.FieldPath.Should().BeEmpty();
        viewModel.NewValue.Should().BeNull();
        viewModel.EffectiveFrom.Should().BeNull();
        viewModel.Justification.Should().BeEmpty();
        viewModel.ConflictId.Should().BeNull();
        viewModel.ChosenWinnerSource.Should().BeEmpty();
        viewModel.ConflictReason.Should().BeEmpty();
        viewModel.AcknowledgePolicyDeviation.Should().BeFalse();
        viewModel.WorkflowId.Should().BeNull();
        viewModel.ExpectedWorkflowVersion.Should().Be(0);
        viewModel.Reviewer.Should().BeEmpty();
        viewModel.ReportPackId.Should().BeEmpty();
        viewModel.SaveDraftCommand.CanExecute(null).Should().BeFalse("the field-edit inputs were reset");
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
