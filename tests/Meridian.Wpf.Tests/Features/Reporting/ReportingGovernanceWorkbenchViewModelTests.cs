using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Reporting;

namespace Meridian.Wpf.Tests.Features.Reporting;

public sealed class ReportingGovernanceWorkbenchViewModelTests
{
    [Fact]
    public async Task AssessReadiness_RoundTripsEveryDesktopRunParameterThroughSharedContract()
    {
        var client = new FakeReportingGovernanceApiClient();
        var viewModel = CreateConfiguredViewModel(client);
        var instrumentId = Guid.Parse("d23f8d2d-86d2-488e-9b06-5d69df9d7b2e");
        var positionId = Guid.Parse("4493f350-2892-4e6e-a42e-929eeb83a573");
        viewModel.TemplateName = "board-governance-packet";
        viewModel.TemplateVersion = 3;
        viewModel.EntityScopeKind = ReportingEntityScopeKindDto.Portfolio;
        viewModel.EntityId = "entity-7";
        viewModel.PortfolioId = "portfolio-4";
        viewModel.InvestorId = "investor-2";
        viewModel.DimensionOverridesText = $"StrategyId=strategy-1;CostCenterId=cost-8;InstrumentId={instrumentId:D};PositionId={positionId:D};gl.Project=project-9";
        viewModel.PeriodId = "2026-Q2";
        viewModel.AsOfDateText = "2026-06-30";
        viewModel.LedgerBookIdText = "8b8cacd6-f013-40bb-912e-fc250b8d7691";
        viewModel.LedgerBookCode = "gaap-primary";
        viewModel.AccountingBasis = ReportingAccountingBasisDto.Management;
        viewModel.PresentationCurrency = "eur";
        viewModel.ConsolidationLevel = ReportingConsolidationLevelDto.Portfolio;
        viewModel.OutputFormat = ReportingOutputFormatDto.EvidenceVault;
        viewModel.Finality = ReportingFinalityDto.Final;
        viewModel.IncludeSupportingSchedules = false;
        viewModel.IncludeEvidenceAppendix = true;
        viewModel.TemplateParametersText = "audience=board;riskMode=full";

        await viewModel.AssessReadinessCommand.ExecuteAsync(null);

        var request = client.LastReadinessRequest.Should().NotBeNull().Subject;
        request.Template.Should().Be(new VersionedReportTemplateIdDto("board-governance-packet", 3));
        request.DatasetRows.Should().BeNull();
        request.DatasetSourceId.Should().BeNull();
        request.RequestedBy.Should().BeNull();
        request.JobId.Should().BeNull();
        request.AllowRestatement.Should().BeFalse();

        var parameters = request.Parameters.Should().NotBeNull().Subject;
        parameters.Scope.FundProfileId.Should().Be("fund-atlas");
        parameters.Scope.EntityScopeKind.Should().Be(ReportingEntityScopeKindDto.Portfolio);
        parameters.Scope.EntityId.Should().Be("entity-7");
        parameters.Scope.PortfolioId.Should().Be("portfolio-4");
        parameters.Scope.InvestorId.Should().Be("investor-2");
        parameters.Scope.Dimensions!.FundId.Should().Be("fund-atlas");
        parameters.Scope.Dimensions.StrategyId.Should().Be("strategy-1");
        parameters.Scope.Dimensions.CostCenterId.Should().Be("cost-8");
        parameters.Scope.Dimensions.InstrumentId.Should().Be(instrumentId);
        parameters.Scope.Dimensions.PositionId.Should().Be(positionId);
        parameters.Scope.Dimensions.ExternalGlDimensions.Should().Contain("Project", "project-9");
        parameters.Scope.Dimensions.BookId.Should().Be("8b8cacd6-f013-40bb-912e-fc250b8d7691");
        parameters.PeriodId.Should().Be("2026-Q2");
        parameters.AsOfDate.Should().Be(new DateOnly(2026, 6, 30));
        parameters.LedgerBook.LedgerBookId.Should().Be(Guid.Parse("8b8cacd6-f013-40bb-912e-fc250b8d7691"));
        parameters.LedgerBook.LedgerBookCode.Should().Be("gaap-primary");
        parameters.AccountingBasis.Should().Be(ReportingAccountingBasisDto.Management);
        parameters.PresentationCurrency.Should().Be("EUR");
        parameters.ConsolidationLevel.Should().Be(ReportingConsolidationLevelDto.Portfolio);
        parameters.OutputFormat.Should().Be(ReportingOutputFormatDto.EvidenceVault);
        parameters.Finality.Should().Be(ReportingFinalityDto.Final);
        parameters.IncludeSupportingSchedules.Should().BeFalse();
        parameters.IncludeEvidenceAppendix.Should().BeTrue();
        parameters.TemplateParameters.Should().Contain("audience", "board").And.Contain("riskMode", "full");
        viewModel.Readiness.Should().NotBeNull();
    }

    [Fact]
    public void RunRequest_CodeOnlyBookSelection_LeavesImmutableDimensionBookIdForServerResolution()
    {
        var viewModel = CreateConfiguredViewModel(new FakeReportingGovernanceApiClient());
        viewModel.LedgerBookIdText = string.Empty;
        viewModel.LedgerBookCode = "gaap-primary";

        var built = viewModel.TryBuildRunRequest(out var request, out var error);

        built.Should().BeTrue(error);
        request.Should().NotBeNull();
        request!.Parameters!.LedgerBook.LedgerBookId.Should().BeNull();
        request.Parameters.LedgerBook.LedgerBookCode.Should().Be("gaap-primary");
        request.Parameters.Scope.Dimensions!.BookId.Should().BeNull();
    }

    [Fact]
    public void RunRequest_ExplicitBookIdAndCode_NormalizesImmutableDimensionBookId()
    {
        var ledgerBookId = Guid.Parse("8B8CACD6-F013-40BB-912E-FC250B8D7691");
        var viewModel = CreateConfiguredViewModel(new FakeReportingGovernanceApiClient());
        viewModel.LedgerBookIdText = ledgerBookId.ToString("B").ToUpperInvariant();
        viewModel.LedgerBookCode = "gaap-primary";
        viewModel.DimensionOverridesText = $"BookId={ledgerBookId:B}";

        var built = viewModel.TryBuildRunRequest(out var request, out var error);

        built.Should().BeTrue(error);
        request.Should().NotBeNull();
        request!.Parameters!.LedgerBook.LedgerBookId.Should().Be(ledgerBookId);
        request.Parameters.Scope.Dimensions!.BookId.Should().Be(ledgerBookId.ToString("D"));
    }

    [Fact]
    public void RunRequest_InvalidExplicitBookIdOverride_IsRejected()
    {
        var viewModel = CreateConfiguredViewModel(new FakeReportingGovernanceApiClient());
        viewModel.DimensionOverridesText = "BookId=primary-book";

        var built = viewModel.TryBuildRunRequest(out var request, out var error);

        built.Should().BeFalse();
        request.Should().BeNull();
        error.Should().Be("BookId must be a valid GUID.");
    }

    [Fact]
    public void RunRequest_MismatchedExplicitBookIdOverride_IsRejected()
    {
        var viewModel = CreateConfiguredViewModel(new FakeReportingGovernanceApiClient());
        viewModel.LedgerBookIdText = "8b8cacd6-f013-40bb-912e-fc250b8d7691";
        viewModel.DimensionOverridesText = "BookId=44444444-4444-4444-4444-444444444444";

        var built = viewModel.TryBuildRunRequest(out var request, out var error);

        built.Should().BeFalse();
        request.Should().BeNull();
        error.Should().Contain("must match the explicit ledger book id");
    }

    [Fact]
    public void RunRequest_UnknownDimensionOverride_IsRejectedInsteadOfSilentlyWideningScope()
    {
        var viewModel = CreateConfiguredViewModel(new FakeReportingGovernanceApiClient());
        viewModel.DimensionOverridesText = "StrategyId=strategy-1;StratgeyId=typo-would-be-dropped";

        var built = viewModel.TryBuildRunRequest(out var request, out var error);

        built.Should().BeFalse();
        request.Should().BeNull();
        error.Should().Contain("Unsupported dimension override").And.Contain("StratgeyId");
    }

    [Theory]
    [InlineData(ReportingEntityScopeKindDto.Entity, "The scoped entity id is required.")]
    [InlineData(ReportingEntityScopeKindDto.Portfolio, "The scoped portfolio id is required.")]
    [InlineData(ReportingEntityScopeKindDto.Investor, "The scoped investor id is required.")]
    public void RunRequest_MissingSelectedScopeIdentifier_IsRejectedLocally(
        ReportingEntityScopeKindDto scopeKind,
        string expectedError)
    {
        var viewModel = CreateConfiguredViewModel(new FakeReportingGovernanceApiClient());
        viewModel.EntityScopeKind = scopeKind;
        viewModel.EntityId = string.Empty;
        viewModel.PortfolioId = string.Empty;
        viewModel.InvestorId = string.Empty;

        var built = viewModel.TryBuildRunRequest(out var request, out var error);

        built.Should().BeFalse();
        request.Should().BeNull();
        error.Should().Be(expectedError);
    }

    [Fact]
    public async Task BlockedReadiness_DisablesGenerationAndSurfacesServerReasons()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            CanGenerateDraft = false,
            CanGenerateFinal = false,
            BlockingReasons = ["Reconciliation checkpoint is incomplete.", "Evidence appendix is missing."]
        };
        var viewModel = CreateConfiguredViewModel(client);

        await viewModel.AssessReadinessCommand.ExecuteAsync(null);

        viewModel.GenerateGovernedRunCommand.CanExecute(null).Should().BeFalse();
        viewModel.ReadinessStatusText.Should().Contain("blocked");
        viewModel.ReadinessBlockerText.Should().Contain("Reconciliation checkpoint");
        viewModel.GenerateRunTooltip.Should().Contain("Evidence appendix");
        client.RunCalls.Should().Be(0);
    }

    [Fact]
    public async Task ParameterChange_InvalidatesPreviouslyReadyReceiptBeforeGeneration()
    {
        var client = new FakeReportingGovernanceApiClient();
        var viewModel = CreateConfiguredViewModel(client);
        await viewModel.AssessReadinessCommand.ExecuteAsync(null);
        viewModel.GenerateGovernedRunCommand.CanExecute(null).Should().BeTrue();

        viewModel.PeriodId = "2026-07";

        viewModel.Readiness.Should().BeNull();
        viewModel.GenerateGovernedRunCommand.CanExecute(null).Should().BeFalse();
        viewModel.GenerateRunTooltip.Should().Contain("Assess server readiness");
    }

    [Fact]
    public async Task GenerateGovernedRun_ExecutesCertifiedRunThenAttachesCanonicalDraft()
    {
        var client = new FakeReportingGovernanceApiClient();
        var viewModel = CreateConfiguredViewModel(client);
        await viewModel.AssessReadinessCommand.ExecuteAsync(null);

        await viewModel.GenerateGovernedRunCommand.ExecuteAsync(null);

        client.RunCalls.Should().Be(1);
        viewModel.CurrentRunId.Should().Be("run-42");
        viewModel.CurrentRun.Should().NotBeNull();
        viewModel.CurrentRun!.GovernanceState.Should().Be("Draft");
        viewModel.ValidateCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task LifecycleCommands_AdvanceOnlyThroughServerAdvertisedActionsAndProjectMakerCheckerEvidence()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Draft", version: 1)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);
        viewModel.ValidateCommand.CanExecute(null).Should().BeTrue();
        viewModel.SubmitCommand.CanExecute(null).Should().BeFalse();

        await viewModel.ValidateCommand.ExecuteAsync(null);
        viewModel.CurrentRun!.GovernanceState.Should().Be("Validated");
        viewModel.SubmitCommand.CanExecute(null).Should().BeTrue();

        await viewModel.SubmitCommand.ExecuteAsync(null);
        viewModel.CurrentRun!.GovernanceState.Should().Be("InReview");
        viewModel.ApproveCommand.CanExecute(null).Should().BeFalse();

        viewModel.ApprovalDecisionNote = "Independent tie-out reviewed against retained evidence.";
        viewModel.ApproveCommand.CanExecute(null).Should().BeTrue();
        await viewModel.ApproveCommand.ExecuteAsync(null);
        viewModel.CurrentRun!.GovernanceState.Should().Be("Approved");
        viewModel.MakerCheckerStatusText.Should().Contain("maker@example.test").And.Contain("checker@example.test");

        await viewModel.ReleaseCommand.ExecuteAsync(null);
        viewModel.CurrentRun!.GovernanceState.Should().Be("Released");
        viewModel.ArtifactSummaryText.Should().Contain("2 immutable artifact(s)");
        viewModel.ReleasedArtifacts.Select(static artifact => artifact.ArtifactId)
            .Should().Equal("artifact-pdf", "artifact-manifest");
        viewModel.CertifiedSnapshotText.Should().Contain("snapshot-42");
        viewModel.ScopeSnapshotText.Should().Contain("tenant-a").And.Contain("fund-atlas");
        client.TransitionCalls.Should().Equal("validate:1", "submit:2", "approve:3", "release:4");
    }

    [Fact]
    public async Task LoadedGovernedRun_ProjectsRetainedReadinessSeparatelyFromPreflightEvidence()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Draft", version: 1)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.Readiness.Should().BeNull("no preflight assessment was performed in this desktop session");
        viewModel.RetainedReadiness.Should().NotBeNull();
        viewModel.RetainedReadinessStatusText.Should().StartWith("Ready");
        viewModel.RetainedReadinessReceiptText.Should().Contain("receipt-42").And.Contain(new string('f', 64));
        viewModel.RetainedReadinessChecks.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ReportingGovernedReadinessCheckRow(
                "reconciliation",
                "Passed",
                "No failure reason retained.",
                "reconciliation-evidence-42"));
    }

    [Fact]
    public async Task LoadedGovernedRun_ProjectsExactNormalizedParametersAndTypedAccessPolicy()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Draft", version: 1)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.RetainedParameterRows.Should().Contain(row =>
            row.Group == "Period" && row.Name == "As-of date" && row.Value == "2026-06-30");
        viewModel.RetainedParameterRows.Should().Contain(row =>
            row.Group == "Dimension" && row.Name == "Strategy" && row.Value == "strategy-42");
        viewModel.RetainedParameterRows.Should().Contain(row =>
            row.Group == "Template parameter" && row.Name == "audience" && row.Value == "board");
        viewModel.RetainedParameterStatusText.Should().Contain(new string('1', 64));
        viewModel.RetainedAccessOwnerText.Should().Contain("maker@example.test").And.Contain("owner access disabled");
        viewModel.RetainedAccessPolicyText.Should().Contain("board-access/7").And.Contain(new string('a', 64));
        viewModel.RetainedAccessPrincipals.Should().BeEquivalentTo(
        [
            new ReportingGovernanceAccessPrincipalDto("User", "maker@example.test"),
            new ReportingGovernanceAccessPrincipalDto("Group", "board-reviewers"),
            new ReportingGovernanceAccessPrincipalDto("Company", "company-a")
        ], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task LifecycleCommands_FailClosedWithoutActionAvailabilityEvenWhenStateLabelLooksEligible()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Draft", version: 8, actionAvailability: [])
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.ValidateCommand.CanExecute(null).Should().BeFalse();
        viewModel.ValidateTooltip.Should().Contain("did not advertise ValidateRun");
        client.TransitionCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task LifecycleCommand_UsesMatchingServerAdvertisedExpectedVersion()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun(
                "Released",
                version: 41,
                includeRelease: true,
                actionAvailability:
                [
                    new ReportingGovernanceActionAvailabilityDto(
                        "ValidateRun",
                        IsAllowed: true,
                        BlockedReason: null,
                        ExpectedVersion: 41)
                ])
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);
        viewModel.ValidateCommand.CanExecute(null).Should().BeTrue();
        await viewModel.ValidateCommand.ExecuteAsync(null);

        client.TransitionCalls.Should().ContainSingle().Which.Should().Be("validate:41");
    }

    [Fact]
    public async Task LifecycleCommand_StaleServerActionProjectionFailsClosedUntilRefresh()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun(
                "Draft",
                version: 90,
                actionAvailability:
                [
                    new ReportingGovernanceActionAvailabilityDto(
                        "ValidateRun",
                        IsAllowed: true,
                        BlockedReason: null,
                        ExpectedVersion: 41)
                ])
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.ValidateCommand.CanExecute(null).Should().BeFalse();
        viewModel.ValidateTooltip.Should().Contain("targets retained version 41")
            .And.Contain("loaded run is version 90")
            .And.Contain("Refresh");
        client.TransitionCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RestatementApproval_ActivatesNewDraftRevisionWithoutMutatingReleasedRun()
    {
        var released = BuildRun("Released", version: 5, revision: 1, includeRelease: true);
        var client = new FakeReportingGovernanceApiClient { GovernedRun = released };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = released.RunId;
        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.RestatementReason = "Late administrator adjustment with retained journal evidence.";
        await viewModel.RequestRestatementCommand.ExecuteAsync(null);

        viewModel.CurrentRestatement.Should().NotBeNull();
        viewModel.CurrentRun.Should().BeSameAs(released);
        viewModel.ApproveRestatementCommand.CanExecute(null).Should().BeTrue();

        await viewModel.ApproveRestatementCommand.ExecuteAsync(null);

        viewModel.CurrentRun!.RunId.Should().Be("run-42-r2");
        viewModel.CurrentRun.Revision.Should().Be(2);
        viewModel.CurrentRun.GovernanceState.Should().Be("Draft");
        viewModel.CurrentRun.RestatementOfRunId.Should().Be("run-42");
        viewModel.RestatementStatusText.Should().Contain("governed revision");
        client.RestatementApprovalExpectedVersion.Should().Be(1);
    }

    [Fact]
    public async Task SecureDistribution_RequiresServerReleaseReceiptAndQueuesOnlyReleasedArtifactIds()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Approved", version: 4)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";
        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.QueueDeliveryCommand.CanExecute(null).Should().BeFalse();
        viewModel.QueueDeliveryTooltip.Should().Contain("release receipt");

        await viewModel.ReleaseCommand.ExecuteAsync(null);
        viewModel.DistributionId = "distribution-board-july";
        viewModel.TransportId = "secure-portal";
        viewModel.RecipientPrincipalId = "board-reviewers";
        viewModel.Destination = "secure-portal";
        viewModel.DeliverySubject = "July board pack";
        viewModel.DeliveryBody = "Use the secure portal to review the released board pack.";
        await viewModel.QueueDeliveryCommand.ExecuteAsync(null);

        var request = client.LastDeliveryRequest.Should().NotBeNull().Subject;
        request.RunId.Should().Be("run-42");
        request.ArtifactIds.Should().Equal("artifact-pdf", "artifact-manifest");
        request.RecipientPrincipalId.Should().Be("board-reviewers");
        request.GrantLifetimeSeconds.Should().Be(1800);
        request.GrantMaxUses.Should().Be(1);
        viewModel.DistributionStatusText.Should().Contain("Queued").And.Contain("delivery-job-1");
        viewModel.DeliveryHistory.Should().ContainSingle(delivery => delivery.JobId == "delivery-job-1");
    }

    [Fact]
    public async Task SecureDistribution_UsesOnlyServerAdvertisedTransportAndExplicitCallerCapabilities()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true),
            DistributionCapabilities = BuildDistributionCapabilities(
                canQueue: true,
                canIssue: false,
                canRevoke: true,
                transportId: "managed-recipient-relay",
                requiresDestination: true)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.TransportOptions.Select(static transport => transport.TransportId)
            .Should().Equal("managed-recipient-relay");
        viewModel.TransportId.Should().Be("managed-recipient-relay");
        viewModel.CanIssueAccessGrant.Should().BeFalse();
        viewModel.CanRevokeAccessGrant.Should().BeTrue();
        viewModel.QueueDeliveryCommand.CanExecute(null).Should().BeFalse();
        viewModel.QueueDeliveryTooltip.Should().Contain("Destination is required");

        viewModel.Destination = "recipient@example.test";
        await viewModel.QueueDeliveryCommand.ExecuteAsync(null);

        client.LastDeliveryRequest.Should().NotBeNull();
        client.LastDeliveryRequest!.TransportId.Should().Be("managed-recipient-relay");
    }

    [Fact]
    public async Task SecureDistribution_ResolverBackedExternalTransportQueuesWithBlankDestinationAssertion()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true),
            DistributionCapabilities = BuildDistributionCapabilities(
                canQueue: true,
                canIssue: true,
                canRevoke: true,
                transportId: "managed-recipient-relay",
                requiresDestination: false,
                isExternal: true)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.Destination.Should().BeEmpty();
        viewModel.DestinationFieldLabel.Should().Be("Destination assertion (optional)");
        viewModel.DestinationFieldHelp.Should().Contain("server resolves");
        viewModel.QueueDeliveryCommand.CanExecute(null).Should().BeTrue();

        await viewModel.QueueDeliveryCommand.ExecuteAsync(null);

        client.LastDeliveryRequest.Should().NotBeNull();
        client.LastDeliveryRequest!.TransportId.Should().Be("managed-recipient-relay");
        client.LastDeliveryRequest.Destination.Should().BeEmpty();
    }

    [Fact]
    public async Task SecureDistribution_FailsClosedWhenServerDeniesQueueEvenForReleasedRun()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true),
            DistributionCapabilities = BuildDistributionCapabilities(
                canQueue: false,
                canIssue: false,
                canRevoke: false,
                transportId: "secure-managed-portal",
                requiresDestination: false,
                actionDisabledReasonCode: "DELIVER_PERMISSION_REQUIRED")
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.QueueDeliveryCommand.CanExecute(null).Should().BeFalse();
        viewModel.QueueDeliveryTooltip.Should().Be("DELIVER_PERMISSION_REQUIRED");
        viewModel.IssueAccessGrantCommand.CanExecute(null).Should().BeFalse();
        viewModel.IssueAccessGrantTooltip.Should().Be("DELIVER_PERMISSION_REQUIRED");
        viewModel.RevokeAccessGrantCommand.CanExecute(null).Should().BeFalse();
        viewModel.DistributionCapabilityStatusText.Should().Contain("queue blocked")
            .And.Contain("issue grant blocked")
            .And.Contain("revoke grant blocked");
        client.LastDeliveryRequest.Should().BeNull();
    }

    [Fact]
    public async Task AccessGrantIssueAndRevoke_UseServerCapabilitiesAndRefreshNonSecretState()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true)
        };
        client.AccessGrantHistory.Add(new SecureReportingAccessGrantSummaryResponse(
            "grant-expired",
            "run-42",
            "package-run-42-v1",
            "former-reviewer",
            false,
            ["artifact-pdf"],
            "Expired",
            DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-01T08:30:00Z"),
            MaxUses: 1,
            UseCount: 0,
            LastUsedAtUtc: null,
            RevokedAtUtc: null,
            RevokedBy: null,
            RevocationReason: null));
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";
        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.AccessGrants.Should().ContainSingle(grant => grant.State == "Expired");
        viewModel.IssueAccessGrantCommand.CanExecute(null).Should().BeTrue();
        viewModel.RecipientPrincipalId = "current-reviewers";
        await viewModel.IssueAccessGrantCommand.ExecuteAsync(null);

        viewModel.LastIssuedRecipientAccessUri.Should().StartWith("/")
            .And.Contain("#token=")
            .And.NotContain("?token=");
        viewModel.AccessGrants.Should().Contain(grant =>
            grant.GrantId == "grant-issued" && grant.State == "Active");
        client.LastGrantIssueRequest.Should().NotBeNull();
        client.LastGrantIssueRequest!.ArtifactIds.Should().Equal("artifact-pdf", "artifact-manifest");

        viewModel.SelectedAccessGrantId = "grant-issued";
        viewModel.GrantRevocationReason = "Recipient engagement ended.";
        viewModel.RevokeAccessGrantCommand.CanExecute(null).Should().BeTrue();
        await viewModel.RevokeAccessGrantCommand.ExecuteAsync(null);

        viewModel.HasLastIssuedRecipientAccessUri.Should().BeFalse();
        viewModel.SelectedAccessGrant.Should().NotBeNull();
        viewModel.SelectedAccessGrant!.State.Should().Be("Revoked");
        viewModel.SelectedAccessGrant.RevokedAtUtc.Should().NotBeNull();
        viewModel.SelectedAccessGrant.RevocationReason.Should().Be("Recipient engagement ended.");
        viewModel.RevokeAccessGrantCommand.CanExecute(null).Should().BeFalse();
        client.LastRevokedGrantId.Should().Be("grant-issued");
        client.LastRevocationReason.Should().Be("Recipient engagement ended.");
    }

    [Fact]
    public async Task AccessGrantIssue_RejectsQueryTokenRecipientLinkWithoutDisplayingBearer()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true),
            GrantIssueRecipientAccessUri =
                "/portal/reporting/access-grants/grant-issued/exchange?token=query-secret#artifact=artifact-pdf"
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";
        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        await viewModel.IssueAccessGrantCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorText.Should().Contain("unsafe recipient link");
        viewModel.ErrorText.Should().NotContain("query-secret");
        viewModel.LastIssuedRecipientAccessUri.Should().BeEmpty();
        viewModel.HasLastIssuedRecipientAccessUri.Should().BeFalse();
    }

    [Theory]
    [InlineData("//attacker.example/exchange#token=opaque")]
    [InlineData("/\\attacker.example/exchange#token=opaque")]
    [InlineData("/portal/reporting/access-grants/grant-issued/exchange#token=opaque&artifact=artifact-pdf")]
    [InlineData("/portal/reporting/access-grants/grant-issued/exchange\n#token=opaque")]
    public async Task AccessGrantIssue_RejectsNonLocalOrAmbiguousFragmentWithoutDisplayingBearer(
        string unsafeRecipientAccessUri)
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true),
            GrantIssueRecipientAccessUri = unsafeRecipientAccessUri
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";
        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        await viewModel.IssueAccessGrantCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorText.Should().Contain("unsafe recipient link");
        viewModel.ErrorText.Should().NotContain("opaque");
        viewModel.LastIssuedRecipientAccessUri.Should().BeEmpty();
        viewModel.HasLastIssuedRecipientAccessUri.Should().BeFalse();
    }

    [Fact]
    public async Task DistributionCatalogFailure_LeavesQueueFailClosedAndRefreshSupportsRecovery()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true),
            DistributionCapabilitiesFailure =
                ApiResponse<SecureReportingDistributionCapabilityCatalog>.Fail("catalog unavailable", 503)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";

        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);

        viewModel.CurrentRun.Should().NotBeNull();
        viewModel.HasDistributionCapabilities.Should().BeFalse();
        viewModel.QueueDeliveryCommand.CanExecute(null).Should().BeFalse();
        viewModel.ErrorText.Should().Contain("catalog unavailable");

        client.DistributionCapabilitiesFailure = null;
        await viewModel.RefreshDistributionCapabilitiesCommand.ExecuteAsync(null);

        viewModel.HasError.Should().BeFalse();
        viewModel.HasDistributionCapabilities.Should().BeTrue();
        viewModel.TransportOptions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ChangingRunScope_ClearsRecipientAndCallerCapabilityStateBeforeReload()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            GovernedRun = BuildRun("Released", version: 5, includeRelease: true)
        };
        var viewModel = CreateConfiguredViewModel(client);
        viewModel.CurrentRunId = "run-42";
        await viewModel.LoadGovernedRunCommand.ExecuteAsync(null);
        viewModel.RecipientPrincipalId = "tenant-a-reviewers";
        viewModel.Destination = "tenant-a@example.test";

        viewModel.CurrentRunId = "run-from-another-scope";

        viewModel.CurrentRun.Should().BeNull();
        viewModel.RecipientPrincipalId.Should().BeEmpty();
        viewModel.Destination.Should().BeEmpty();
        viewModel.DistributionCapabilities.Should().BeNull();
        viewModel.TransportOptions.Should().BeEmpty();
        viewModel.QueueDeliveryCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ApplyScheduleRecords_ProjectsAccessPolicyHashAndTokenFreeReleaseHandoffState()
    {
        var viewModel = CreateConfiguredViewModel(new FakeReportingGovernanceApiClient());
        var policyHash = new string('7', 64);
        viewModel.ApplyScheduleRecords(
        [
            new ReportingScheduleRecordDto(
                "schedule-board",
                "board-governance-packet",
                "0 8 1 * *",
                new DateOnly(2026, 6, 30),
                DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
                3,
                "maker@example.test",
                ReportingScheduleStateDto.Active,
                DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-01T08:05:00Z"),
                LastRunId: "run-42",
                ReleaseDeliveryHandoffs:
                [
                    new ReportingScheduledReleaseHandoffDto(
                        HandoffId: "handoff-42",
                        TenantId: "tenant-a",
                        CompanyId: "company-a",
                        ScheduleId: "schedule-board",
                        RunId: "run-42",
                        TemplateId: "board-governance-packet",
                        DistributionId: "distribution-board",
                        TargetDistributionId: "distribution-board",
                        TransportId: "managed-relay",
                        RecipientPrincipalId: "board-reviewers",
                        Destination: "server-resolved@example.test",
                        Subject: "Board packet",
                        Body: "Use the governed portal.",
                        RequestedFormats: [GovernanceReportArtifactFormatDto.Pdf],
                        ArtifactIds: ["artifact-pdf"],
                        GrantLifetimeSeconds: 1800,
                        GrantMaxUses: 1,
                        MaxAttempts: 3,
                        CreatedAtUtc: DateTimeOffset.Parse("2026-07-01T08:06:00Z"),
                        State: ReportingScheduledReleaseHandoffStateDto.Enqueued,
                        EnqueuedDeliveryJobId: "delivery-job-42",
                        EnqueuedAtUtc: DateTimeOffset.Parse("2026-07-01T08:07:00Z"),
                        RecipientPrincipalKind: "Group")
                ],
                AccessPolicySnapshotHash: policyHash),
            new ReportingScheduleRecordDto(
                "schedule-investor",
                "investor-monthly-statement",
                "0 8 1 * *",
                new DateOnly(2026, 6, 30),
                DateTimeOffset.Parse("2026-07-01T08:00:00Z"),
                3,
                "maker@example.test",
                ReportingScheduleStateDto.Active,
                DateTimeOffset.Parse("2026-06-01T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-01T08:05:00Z"),
                AccessPolicySnapshotHash: new string('8', 64))
        ]);

        viewModel.ScheduleReleaseHandoffs.Should().HaveCount(2);
        viewModel.ScheduleReleaseHandoffs.Should().ContainEquivalentOf(new
        {
            ScheduleId = "schedule-board",
            AccessPolicySnapshotHash = policyHash,
            HandoffId = "handoff-42",
            RunId = "run-42",
            State = "Enqueued",
            Recipient = "Group:board-reviewers",
            TransportId = "managed-relay",
            DeliveryJobId = "delivery-job-42"
        });
        viewModel.ScheduleReleaseHandoffs.Should().Contain(row =>
            row.ScheduleId == "schedule-investor"
            && row.HandoffId == "No retained handoff"
            && row.AccessPolicySnapshotHash == new string('8', 64));
        viewModel.ScheduleReleaseHandoffs.SelectMany(static row => new[]
            { row.HandoffId, row.RunId, row.State, row.Recipient, row.TransportId, row.DeliveryJobId })
            .Should().NotContain(value => value.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FailedReadiness_ClearsBusyStateAndSupportsRecovery()
    {
        var client = new FakeReportingGovernanceApiClient
        {
            ReadinessFailure = ApiResponse<ReportingRunReadinessDto>.Fail("service unavailable", 503)
        };
        var viewModel = CreateConfiguredViewModel(client);

        await viewModel.AssessReadinessCommand.ExecuteAsync(null);

        viewModel.IsBusy.Should().BeFalse();
        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorText.Should().Contain("service unavailable");

        client.ReadinessFailure = null;
        await viewModel.AssessReadinessCommand.ExecuteAsync(null);
        viewModel.HasError.Should().BeFalse();
        viewModel.Readiness.Should().NotBeNull();
    }

    private static ReportingGovernanceWorkbenchViewModel CreateConfiguredViewModel(
        FakeReportingGovernanceApiClient client)
    {
        var viewModel = new ReportingGovernanceWorkbenchViewModel(client);
        viewModel.SetFundContext("fund-atlas", "USD");
        viewModel.PeriodId = "2026-06";
        viewModel.AsOfDateText = "2026-06-30";
        viewModel.LedgerBookCode = "primary";
        return viewModel;
    }

    private static GovernedReportingRunDto BuildRun(
        string state,
        long version,
        int revision = 1,
        bool includeRelease = false,
        string runId = "run-42",
        string? restatementOfRunId = null,
        IReadOnlyList<ReportingGovernanceActionAvailabilityDto>? actionAvailability = null)
    {
        var maker = Authority("maker@example.test", "CreateRun", "ExecuteRun", "ValidateRun", "SubmitRun", "RequestRestatement");
        var checker = Authority("checker@example.test", "ApproveRun", "ApproveRestatement", "ReleaseRun");
        var approval = state is "Approved" or "Released"
            ? new ReportingGovernanceApprovalDto(checker, DateTimeOffset.Parse("2026-07-01T12:00:00Z"), "Independent review complete.")
            : null;
        var release = includeRelease || state == "Released"
            ? new ReportingGovernanceReleaseDto(
                checker,
                DateTimeOffset.Parse("2026-07-01T13:00:00Z"),
                "manifest-42",
                new string('b', 64),
                [
                    new ReportingGovernanceArtifactDto("artifact-pdf", new string('c', 64), 1_024),
                    new ReportingGovernanceArtifactDto("artifact-manifest", new string('d', 64), 512)
                ],
                ["evidence-release-1"])
            : null;

        return new GovernedReportingRunDto(
            runId,
            "series-42",
            revision,
            "board-governance-packet",
            "1",
            new ReportingGovernanceOperationalScopeDto(
                "tenant-a",
                "organization-a",
                "company-a",
                "fund-atlas",
                "primary",
                "2026-06"),
            new ReportingGovernanceAccessScopeDto(
                "board-access",
                "7",
                "PrincipalScoped",
                "maker@example.test",
                AllowOwnerAccess: false,
                Principals:
                [
                    new ReportingGovernanceAccessPrincipalDto("User", "maker@example.test"),
                    new ReportingGovernanceAccessPrincipalDto("Group", "board-reviewers"),
                    new ReportingGovernanceAccessPrincipalDto("Company", "company-a")
                ],
                PolicyHash: new string('a', 64)),
            new ReportingGovernanceCertifiedSnapshotDto(
                "snapshot-42",
                new string('e', 64),
                "recon-42",
                DateTimeOffset.Parse("2026-06-30T23:59:59Z"),
                ParametersHash: new string('1', 64)),
            maker,
            DateTimeOffset.Parse("2026-07-01T10:00:00Z"),
            restatementOfRunId,
            "Succeeded",
            state,
            version,
            new ReportingGovernanceReadinessDto(
                "receipt-42",
                new string('f', 64),
                DateTimeOffset.Parse("2026-07-01T10:05:00Z"),
                IsReady: true,
                Checks:
                [
                    new ReportingGovernanceReadinessCheckDto(
                        "reconciliation",
                        Passed: true,
                        EvidenceIds: ["reconciliation-evidence-42"],
                        FailureReason: null)
                ]),
            approval,
            release,
            [],
            NormalizedParameters: new ReportingRunParametersDto(
                new ReportingRunScopeDto(
                    "fund-atlas",
                    ReportingEntityScopeKindDto.Portfolio,
                    EntityId: "entity-42",
                    PortfolioId: "portfolio-42",
                    Dimensions: new LedgerDimensionSetDto(
                        FundId: "fund-atlas",
                        EntityId: "entity-42",
                        StrategyId: "strategy-42",
                        OrganizationId: "organization-a",
                        PortfolioId: "portfolio-42",
                        BookId: "primary")),
                "2026-06",
                new DateOnly(2026, 6, 30),
                new ReportingLedgerBookSelectionDto(LedgerBookCode: "primary"),
                ReportingAccountingBasisDto.Gaap,
                "USD",
                ReportingConsolidationLevelDto.Portfolio,
                ReportingOutputFormatDto.Pdf,
                ReportingFinalityDto.Final,
                IncludeSupportingSchedules: true,
                IncludeEvidenceAppendix: true,
                TemplateParameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["audience"] = "board"
                }),
            ActionAvailability: actionAvailability ?? BuildRunActionAvailability(state, version));
    }

    private static IReadOnlyList<ReportingGovernanceActionAvailabilityDto> BuildRunActionAvailability(
        string state,
        long version) =>
    [
        Action("ValidateRun", state == "Draft", version),
        Action("SubmitRun", state == "Validated", version),
        Action("ApproveRun", state == "InReview", version),
        Action("ReleaseRun", state == "Approved", version),
        Action("RequestRestatement", state == "Released", version)
    ];

    private static ReportingGovernanceActionAvailabilityDto Action(
        string action,
        bool allowed,
        long expectedVersion) =>
        new(
            action,
            allowed,
            allowed ? null : $"Server blocked {action} for this caller.",
            expectedVersion);

    private static SecureReportingDistributionCapabilityCatalog BuildDistributionCapabilities(
        bool canQueue = true,
        bool canIssue = true,
        bool canRevoke = true,
        string transportId = "secure-portal",
        bool requiresDestination = false,
        bool? isExternal = null,
        string? actionDisabledReasonCode = null) =>
        new(
            canQueue,
            canIssue,
            canRevoke,
            actionDisabledReasonCode,
            [
                new SecureReportingTransportCapability(
                    transportId,
                    "Managed reporting transport",
                    (isExternal ?? requiresDestination) ? "ExternalNotification" : "SecurePortal",
                    IsExternal: isExternal ?? requiresDestination,
                    RequiresDestination: requiresDestination,
                    UsesGovernedRecipientScope: true,
                    IssuesAccessGrant: isExternal ?? requiresDestination,
                    SupportsProviderReceipts: isExternal ?? requiresDestination,
                    IsConfigured: true,
                    IsInfrastructureReady: true,
                    InfrastructureDisabledReasonCode: null,
                    IsReady: canQueue,
                    DisabledReasonCode: canQueue ? null : actionDisabledReasonCode)
            ]);

    private static ReportingGovernanceAuthorityDto Authority(string actor, params string[] permissions) =>
        new(
            actor,
            "tenant-a",
            "organization-a",
            "company-a",
            permissions,
            "HumanOperator",
            "correlation-42",
            [actor]);

    private sealed class FakeReportingGovernanceApiClient : IReportingGovernanceApiClient
    {
        public ReportingRunRequestDto? LastReadinessRequest { get; private set; }
        public SecureReportingDeliveryQueueCommand? LastDeliveryRequest { get; private set; }
        public GovernedReportingRunDto GovernedRun { get; set; } = BuildRun("Draft", 1);
        public bool CanGenerateDraft { get; set; } = true;
        public bool CanGenerateFinal { get; set; } = true;
        public IReadOnlyList<string> BlockingReasons { get; set; } = [];
        public ApiResponse<ReportingRunReadinessDto>? ReadinessFailure { get; set; }
        public SecureReportingDistributionCapabilityCatalog DistributionCapabilities { get; set; } =
            BuildDistributionCapabilities();
        public ApiResponse<SecureReportingDistributionCapabilityCatalog>? DistributionCapabilitiesFailure { get; set; }
        public List<SecureReportingDeliveryResponse> DeliveryHistory { get; } = [];
        public List<SecureReportingAccessGrantSummaryResponse> AccessGrantHistory { get; } = [];
        public SecureReportingGrantIssueCommand? LastGrantIssueRequest { get; private set; }
        public string GrantIssueRecipientAccessUri { get; set; } =
            "/portal/reporting/access-grants/grant-issued/exchange#token=one-time-secret";
        public string? LastRevokedGrantId { get; private set; }
        public string? LastRevocationReason { get; private set; }
        public int RunCalls { get; private set; }
        public List<string> TransitionCalls { get; } = [];
        public long? RestatementApprovalExpectedVersion { get; private set; }

        public Task<ApiResponse<ReportingRunReadinessDto>> AssessReadinessAsync(
            ReportingRunRequestDto request,
            CancellationToken ct = default)
        {
            LastReadinessRequest = request;
            if (ReadinessFailure is not null)
            {
                return Task.FromResult(ReadinessFailure);
            }

            var ready = CanGenerateDraft || CanGenerateFinal;
            var response = new ReportingRunReadinessDto(
                "readiness-42",
                DateTimeOffset.Parse("2026-07-01T09:00:00Z"),
                request.Template!,
                request.Parameters!,
                ready ? ReportingRunReadinessStatusDto.Ready : ReportingRunReadinessStatusDto.Blocked,
                CanGenerateDraft,
                CanGenerateFinal,
                [
                    new ReportingRunReadinessCheckDto(
                        "reconciliation",
                        "Reconciliation",
                        ready ? ReportingRunReadinessStatusDto.Ready : ReportingRunReadinessStatusDto.Blocked,
                        ready ? "Checkpoint retained." : "Checkpoint incomplete.",
                        ready ? 0 : 1,
                        BlocksDraft: true,
                        BlocksFinal: true,
                        EvidenceReferences: ["recon-42"])
                ],
                BlockingReasons,
                new string('f', 64));
            return Task.FromResult(ApiResponse<ReportingRunReadinessDto>.Ok(response));
        }

        public Task<ApiResponse<ReportingRunResultDto>> RunAsync(
            ReportingRunRequestDto request,
            CancellationToken ct = default)
        {
            RunCalls++;
            var payload = new WorkstationReportingRunPayload(
                "run-42",
                request.TemplateId,
                "BoardPacket",
                "Completed",
                "Manual",
                request.AsOfDate!.Value.ToString("yyyy-MM-dd"),
                1,
                3,
                3,
                ["report.pdf", "manifest.json"],
                ["Generated"],
                null,
                ResolvedTemplate: request.Template,
                ResolvedParameters: request.Parameters);
            return Task.FromResult(ApiResponse<ReportingRunResultDto>.Ok(new ReportingRunResultDto(payload), 201));
        }

        public Task<ApiResponse<GovernedReportingRunDto>> GetGovernedRunAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult(ApiResponse<GovernedReportingRunDto>.Ok(GovernedRun));

        public Task<ApiResponse<GovernedReportingRunDto>> GovernCompletedRunAsync(
            string runId,
            CancellationToken ct = default)
        {
            GovernedRun = BuildRun("Draft", 1, runId: runId);
            return Task.FromResult(ApiResponse<GovernedReportingRunDto>.Ok(GovernedRun, 201));
        }

        public Task<ApiResponse<GovernedReportingRunDto>> ValidateAsync(
            string runId,
            long expectedVersion,
            CancellationToken ct = default) =>
            Transition("validate", "Validated", expectedVersion);

        public Task<ApiResponse<GovernedReportingRunDto>> SubmitAsync(
            string runId,
            long expectedVersion,
            CancellationToken ct = default) =>
            Transition("submit", "InReview", expectedVersion);

        public Task<ApiResponse<GovernedReportingRunDto>> ApproveAsync(
            string runId,
            long expectedVersion,
            string decisionNote,
            CancellationToken ct = default) =>
            Transition("approve", "Approved", expectedVersion);

        public Task<ApiResponse<GovernedReportingRunDto>> ReleaseAsync(
            string runId,
            long expectedVersion,
            CancellationToken ct = default) =>
            Transition("release", "Released", expectedVersion, includeRelease: true);

        public Task<ApiResponse<ReportingGovernanceRestatementDto>> RequestRestatementAsync(
            string runId,
            long expectedVersion,
            string reason,
            CancellationToken ct = default)
        {
            var request = new ReportingGovernanceRestatementDto(
                "restatement-42",
                runId,
                GovernedRun.SeriesId,
                GovernedRun.Revision,
                expectedVersion,
                reason,
                [new ReportingGovernanceChangedLineDto("nav", "100", "101", ["journal-8"])],
                Authority("maker@example.test", "RequestRestatement"),
                DateTimeOffset.Parse("2026-07-02T10:00:00Z"),
                "PendingApproval",
                1,
                null,
                null,
                null,
                [],
                ActionAvailability:
                [
                    new ReportingGovernanceActionAvailabilityDto(
                        "ApproveRestatement",
                        IsAllowed: true,
                        BlockedReason: null,
                        ExpectedVersion: 1)
                ]);
            return Task.FromResult(ApiResponse<ReportingGovernanceRestatementDto>.Ok(request, 201));
        }

        public Task<ApiResponse<ReportingGovernanceRestatementApprovalDto>> ApproveRestatementAsync(
            string requestId,
            long expectedVersion,
            CancellationToken ct = default)
        {
            RestatementApprovalExpectedVersion = expectedVersion;
            var request = new ReportingGovernanceRestatementDto(
                requestId,
                GovernedRun.RunId,
                GovernedRun.SeriesId,
                GovernedRun.Revision,
                GovernedRun.Version,
                "Late administrator adjustment with retained journal evidence.",
                [],
                Authority("maker@example.test", "RequestRestatement"),
                DateTimeOffset.Parse("2026-07-02T10:00:00Z"),
                "Approved",
                expectedVersion + 1,
                Authority("checker@example.test", "ApproveRestatement"),
                DateTimeOffset.Parse("2026-07-02T11:00:00Z"),
                "run-42-r2",
                [],
                ActionAvailability:
                [
                    new ReportingGovernanceActionAvailabilityDto(
                        "ApproveRestatement",
                        IsAllowed: false,
                        BlockedReason: "Restatement request is already approved.",
                        ExpectedVersion: expectedVersion + 1)
                ]);
            var draft = BuildRun(
                "Draft",
                version: 1,
                revision: 2,
                runId: "run-42-r2",
                restatementOfRunId: "run-42");
            GovernedRun = draft;
            return Task.FromResult(ApiResponse<ReportingGovernanceRestatementApprovalDto>.Ok(
                new ReportingGovernanceRestatementApprovalDto(request, draft)));
        }

        public Task<ApiResponse<SecureReportingDistributionCapabilityCatalog>> GetDistributionCapabilitiesAsync(
            CancellationToken ct = default) =>
            Task.FromResult(
                DistributionCapabilitiesFailure
                ?? ApiResponse<SecureReportingDistributionCapabilityCatalog>.Ok(DistributionCapabilities));

        public Task<ApiResponse<SecureReportingDeliveryResponse[]>> ListDeliveriesAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult(ApiResponse<SecureReportingDeliveryResponse[]>.Ok(
                DeliveryHistory.Where(delivery => delivery.RunId == runId).ToArray()));

        public Task<ApiResponse<SecureReportingDeliveryResponse>> QueueDeliveryAsync(
            SecureReportingDeliveryQueueCommand request,
            CancellationToken ct = default)
        {
            LastDeliveryRequest = request;
            var response = new SecureReportingDeliveryResponse(
                "delivery-job-1",
                request.RunId,
                "package-run-42-v1",
                "1",
                new string('9', 64),
                request.DistributionId,
                request.TransportId,
                request.RecipientPrincipalId ?? "board-reviewers",
                request.Destination,
                request.Subject,
                "Queued",
                0,
                request.MaxAttempts,
                DateTimeOffset.Parse("2026-07-03T10:00:00Z"),
                DateTimeOffset.Parse("2026-07-03T10:00:00Z"),
                DateTimeOffset.Parse("2026-07-03T10:00:00Z"),
                null,
                null,
                null,
                null,
                []);
            DeliveryHistory.Add(response);
            return Task.FromResult(ApiResponse<SecureReportingDeliveryResponse>.Ok(response));
        }

        public Task<ApiResponse<SecureReportingGrantResponse>> IssueAccessGrantAsync(
            SecureReportingGrantIssueCommand request,
            CancellationToken ct = default)
        {
            LastGrantIssueRequest = request;
            var expiresAt = DateTimeOffset.Parse("2026-07-03T10:30:00Z");
            var response = new SecureReportingGrantResponse(
                "grant-issued",
                GrantIssueRecipientAccessUri,
                expiresAt,
                request.RecipientPrincipalId ?? "board-reviewers",
                request.RunId,
                "package-run-42-v1",
                request.ArtifactIds ?? []);
            AccessGrantHistory.Add(new SecureReportingAccessGrantSummaryResponse(
                response.GrantId,
                response.RunId,
                response.PackageId,
                response.Audience,
                false,
                response.ArtifactIds,
                "Active",
                DateTimeOffset.Parse("2026-07-03T10:00:00Z"),
                response.ExpiresAtUtc,
                request.MaxUses ?? 1,
                UseCount: 0,
                LastUsedAtUtc: null,
                RevokedAtUtc: null,
                RevokedBy: null,
                RevocationReason: null));
            return Task.FromResult(ApiResponse<SecureReportingGrantResponse>.Ok(response));
        }

        public Task<ApiResponse<SecureReportingAccessGrantSummaryResponse[]>> ListAccessGrantsAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult(ApiResponse<SecureReportingAccessGrantSummaryResponse[]>.Ok(
                AccessGrantHistory.Where(grant => grant.RunId == runId).ToArray()));

        public Task<ApiResponse<SecureReportingGrantRevocationResponse>> RevokeAccessGrantAsync(
            string grantId,
            string reason,
            CancellationToken ct = default)
        {
            LastRevokedGrantId = grantId;
            LastRevocationReason = reason;
            var index = AccessGrantHistory.FindIndex(grant => grant.GrantId == grantId);
            if (index >= 0)
            {
                var grant = AccessGrantHistory[index];
                AccessGrantHistory[index] = grant with
                {
                    State = "Revoked",
                    RevokedAtUtc = DateTimeOffset.Parse("2026-07-03T10:10:00Z"),
                    RevokedBy = "checker@example.test",
                    RevocationReason = reason
                };
            }

            return Task.FromResult(ApiResponse<SecureReportingGrantRevocationResponse>.Ok(
                new SecureReportingGrantRevocationResponse(grantId, Revoked: index >= 0)));
        }

        private Task<ApiResponse<GovernedReportingRunDto>> Transition(
            string action,
            string state,
            long expectedVersion,
            bool includeRelease = false)
        {
            TransitionCalls.Add($"{action}:{expectedVersion}");
            GovernedRun = BuildRun(
                state,
                expectedVersion + 1,
                GovernedRun.Revision,
                includeRelease,
                GovernedRun.RunId,
                GovernedRun.RestatementOfRunId);
            return Task.FromResult(ApiResponse<GovernedReportingRunDto>.Ok(GovernedRun));
        }
    }
}
