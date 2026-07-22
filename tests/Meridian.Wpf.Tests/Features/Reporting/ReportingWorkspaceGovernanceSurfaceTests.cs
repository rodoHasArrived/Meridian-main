namespace Meridian.Wpf.Tests.Features.Reporting;

public sealed class ReportingWorkspaceGovernanceSurfaceTests
{
    [Fact]
    public void ShellXaml_ExposesStableAutomationIdsForCertifiedGovernanceWorkflow()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(
            @"src\Meridian.Wpf\Features\Reporting\Shell\ReportingWorkspaceShellPage.xaml"));

        xaml.Should().Contain("ReportingGovernanceWorkbench");
        xaml.Should().Contain("ReportingGovernanceRunParameters");
        xaml.Should().Contain("ReportingGovernanceAssessReadinessButton");
        xaml.Should().Contain("ReportingGovernanceReadinessBlockers");
        xaml.Should().Contain("ReportingGovernanceGenerateRunButton");
        xaml.Should().Contain("ReportingGovernanceLifecycleState");
        xaml.Should().Contain("ReportingGovernanceValidateButton");
        xaml.Should().Contain("ReportingGovernanceSubmitButton");
        xaml.Should().Contain("ReportingGovernanceApproveButton");
        xaml.Should().Contain("ReportingGovernanceReleaseButton");
        xaml.Should().Contain("ReportingGovernanceMakerCheckerStatus");
        xaml.Should().Contain("ReportingGovernanceCertifiedSnapshot");
        xaml.Should().Contain("ReportingGovernanceRetainedEvidencePanel");
        xaml.Should().Contain("ReportingGovernanceRetainedReadinessReceipt");
        xaml.Should().Contain("ReportingGovernanceRetainedReadinessChecks");
        xaml.Should().Contain("ReportingGovernanceRetainedParameterStatus");
        xaml.Should().Contain("ReportingGovernanceRetainedParameters");
        xaml.Should().Contain("ReportingGovernanceRetainedAccessPolicy");
        xaml.Should().Contain("ReportingGovernanceRetainedAccessOwner");
        xaml.Should().Contain("ReportingGovernanceRetainedAccessPrincipals");
        xaml.Should().Contain("ReportingGovernanceScheduleHandoffStatus");
        xaml.Should().Contain("ReportingGovernanceScheduleHandoffs");
        xaml.Should().Contain("Binding=\"{Binding AccessPolicySnapshotHash}\"");
        xaml.Should().Contain("EnableRowVirtualization=\"True\"");
        xaml.Should().Contain("EnableColumnVirtualization=\"True\"");
        xaml.Should().Contain("ReportingGovernanceArtifactSummary");
        xaml.Should().Contain("ReportingGovernanceReleasedArtifacts");
        xaml.Should().Contain("ReportingGovernanceRequestRestatementButton");
        xaml.Should().Contain("ReportingGovernanceApproveRestatementButton");
        xaml.Should().Contain("ReportingGovernanceDistributionCapabilities");
        xaml.Should().Contain("ReportingGovernanceRefreshDistributionCapabilitiesButton");
        xaml.Should().Contain("DisplayMemberPath=\"DisplayName\"");
        xaml.Should().Contain("SelectedValuePath=\"TransportId\"");
        xaml.Should().Contain("ReportingGovernanceQueueDeliveryButton");
        xaml.Should().Contain("ReportingGovernanceDistributionStatus");
        xaml.Should().Contain("ReportingGovernanceIssueAccessGrantButton");
        xaml.Should().Contain("ReportingGovernanceIssuedRecipientLink");
        xaml.Should().Contain("ReportingGovernanceAccessGrantSelector");
        xaml.Should().Contain("ReportingGovernanceRevokeAccessGrantButton");
        xaml.Should().Contain("ReportingGovernanceDeliveryHistory");
        xaml.Should().Contain("ReportingGovernanceAccessGrantHistory");
    }

    [Fact]
    public void ApiClient_UsesSharedContractsAndCanonicalRouteCatalog()
    {
        var client = File.ReadAllText(GetRepositoryFilePath(
            @"src\Meridian.Wpf\Features\Reporting\ReportingGovernanceApiClient.cs"));

        client.Should().Contain("UiApiRoutes.ReportingRunReadiness");
        client.Should().Contain("UiApiRoutes.ReportingRuns");
        client.Should().Contain("UiApiRoutes.ReportingGovernedRunValidate");
        client.Should().Contain("UiApiRoutes.ReportingGovernedRunSubmit");
        client.Should().Contain("UiApiRoutes.ReportingGovernedRunApprove");
        client.Should().Contain("UiApiRoutes.ReportingGovernedRunRelease");
        client.Should().Contain("UiApiRoutes.ReportingGovernedRunRestatementRequests");
        client.Should().Contain("UiApiRoutes.ReportingDistributionTransports");
        client.Should().Contain("UiApiRoutes.ReportingDistributionQueueDelivery");
        client.Should().Contain("UiApiRoutes.ReportingDistributionPackageDeliveries");
        client.Should().Contain("UiApiRoutes.ReportingDistributionIssueAccessGrant");
        client.Should().Contain("UiApiRoutes.ReportingDistributionPackageAccessGrants");
        client.Should().Contain("UiApiRoutes.ReportingDistributionRevokeAccessGrant");
        client.Should().Contain("ReportingGovernanceVersionRequestDto");
        client.Should().NotContain("SecureReportingDistributionRoutes");
        client.Should().NotContain("ActorId =");
        client.Should().NotContain("TenantId =");
    }

    [Fact]
    public void Workbench_UsesServerActionAvailabilityAndContainsNoHardCodedTransportOrQueryTokenLink()
    {
        var viewModel = File.ReadAllText(GetRepositoryFilePath(
            @"src\Meridian.Wpf\Features\Reporting\ReportingGovernanceWorkbenchViewModel.cs"));

        viewModel.Should().Contain("ActionAvailability");
        viewModel.Should().Contain("ExpectedVersion");
        viewModel.Should().Contain("candidate.ExpectedVersion == run.Version");
        viewModel.Should().Contain("candidate.ExpectedVersion == restatement.Version");
        viewModel.Should().Contain("RetainedReadiness");
        viewModel.Should().Contain("RetainedParameterRows");
        viewModel.Should().Contain("RetainedAccessPrincipals");
        viewModel.Should().Contain("ScheduleReleaseHandoffs");
        viewModel.Should().Contain("CanQueueDelivery");
        viewModel.Should().Contain("CanIssueAccessGrant");
        viewModel.Should().Contain("CanRevokeAccessGrant");
        viewModel.Should().NotContain("TransportOptionsValue");
        viewModel.Should().NotContain("[\"secure-portal\", \"http-relay\"]");
        viewModel.Should().NotContain("?token=");
    }

    [Fact]
    public void ReportingShell_HandsSharedScheduleRecordsToGovernanceProjection()
    {
        var shellViewModel = File.ReadAllText(GetRepositoryFilePath(
            @"src\Meridian.Wpf\ViewModels\WorkspaceShellViewModelBase.cs"));

        shellViewModel.Should().Contain("Governance?.ApplyScheduleRecords(reporting?.Schedules)");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
