using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task StatementReconciliationReport_MissingProductionAuthority_ReturnsServiceUnavailable()
    {
        await using var app = await CreateAppAsync(_ => { });
        using var content = BuildStatementReconciliationReportContent();

        var response = await app.GetTestClient().PostAsync(
            UiApiRoutes.ReconciliationStatementReconciliationReport,
            content);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatementReconciliationReport_SameCompanyUnauthorizedAccount_IsForbiddenAcrossProtectedRoutes(
        bool useLegacyRoutes)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-report-account-authority",
            Guid.NewGuid().ToString("N"));
        try
        {
            var intake = new RecordingStatementReconciliationIntakeAuthority();
            var workflow = CreateStatementReconciliationReportEndpointWorkflow(root, intake);
            await using var authorizedApp = await CreateAppAsync(
                services => RegisterStatementReconciliationReportEndpointAuthority(
                    services,
                    workflow,
                    intake,
                    StatementReportAccountId),
                currentUserPermissions: UserPermission.ManageDirectLending);
            var authorizedClient = authorizedApp.GetTestClient();
            using var content = BuildStatementReconciliationReportContent();

            using var startResponse = await authorizedClient.PostAsync(
                UiApiRoutes.ReconciliationStatementReconciliationReport,
                content);
            startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var started = await startResponse.Content.ReadFromJsonAsync<StatementReconciliationReportWorkflowDto>(
                ServerJsonOptions);
            started.Should().NotBeNull();
            started!.RetainedArtifacts.Should().NotBeEmpty();
            var routes = BuildStatementReconciliationReportProtectedRoutes(started, useLegacyRoutes);

            using var authorizedGet = await authorizedClient.GetAsync(routes.Status);
            using var authorizedResume = await authorizedClient.PostAsync(routes.Resume, content: null);
            using var authorizedDownload = await authorizedClient.GetAsync(routes.Artifact);
            authorizedGet.StatusCode.Should().Be(HttpStatusCode.OK);
            authorizedResume.StatusCode.Should().Be(HttpStatusCode.OK);
            authorizedDownload.StatusCode.Should().Be(HttpStatusCode.OK);

            await using var unauthorizedApp = await CreateAppAsync(
                services => RegisterStatementReconciliationReportEndpointAuthority(
                    services,
                    workflow,
                    intake,
                    Guid.NewGuid()),
                currentUserPermissions: UserPermission.ManageDirectLending);
            var unauthorizedClient = unauthorizedApp.GetTestClient();

            using var deniedGet = await unauthorizedClient.GetAsync(routes.Status);
            using var deniedResume = await unauthorizedClient.PostAsync(routes.Resume, content: null);
            using var deniedDownload = await unauthorizedClient.GetAsync(routes.Artifact);
            deniedGet.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            deniedResume.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            deniedDownload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatementReconciliationReport_MissingCompany_IsForbiddenAcrossProtectedRoutes(
        bool useLegacyRoutes)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-report-company-authority",
            Guid.NewGuid().ToString("N"));
        try
        {
            var intake = new RecordingStatementReconciliationIntakeAuthority();
            var workflow = CreateStatementReconciliationReportEndpointWorkflow(root, intake);
            await using var authorizedApp = await CreateAppAsync(
                services => RegisterStatementReconciliationReportEndpointAuthority(
                    services,
                    workflow,
                    intake,
                    StatementReportAccountId),
                currentUserPermissions: UserPermission.ManageDirectLending);
            var authorizedClient = authorizedApp.GetTestClient();
            using var content = BuildStatementReconciliationReportContent();
            using var startResponse = await authorizedClient.PostAsync(
                UiApiRoutes.ReconciliationStatementReconciliationReport,
                content);
            var started = await startResponse.Content.ReadFromJsonAsync<StatementReconciliationReportWorkflowDto>(
                ServerJsonOptions);
            startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            started.Should().NotBeNull();
            var routes = BuildStatementReconciliationReportProtectedRoutes(started!, useLegacyRoutes);

            await using var missingCompanyApp = await CreateAppAsync(
                services => RegisterStatementReconciliationReportEndpointAuthority(
                    services,
                    workflow,
                    intake,
                    StatementReportAccountId),
                currentUserPermissions: UserPermission.ManageDirectLending,
                currentUserCompanyId: null,
                currentUserTenantId: "tenant-test");
            var missingCompanyClient = missingCompanyApp.GetTestClient();

            using var deniedGet = await missingCompanyClient.GetAsync(routes.Status);
            using var deniedResume = await missingCompanyClient.PostAsync(routes.Resume, content: null);
            using var deniedDownload = await missingCompanyClient.GetAsync(routes.Artifact);
            deniedGet.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            deniedResume.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            deniedDownload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static StatementReconciliationReportWorkflowService CreateStatementReconciliationReportEndpointWorkflow(
        string root,
        IStatementReconciliationIntakeAuthority intake)
        => new(
            new EndpointStatementImportService(),
            new EndpointStatementEvidenceRetainer(),
            new EndpointStatementRunWorkflowService(),
            root,
            logger: null,
            breakQueue: null,
            intakeAuthority: intake);

    private static void RegisterStatementReconciliationReportEndpointAuthority(
        IServiceCollection services,
        StatementReconciliationReportWorkflowService workflow,
        IStatementReconciliationIntakeAuthority intake,
        Guid scopedAccountId)
    {
        services.AddSingleton(workflow);
        services.AddSingleton(intake);
        services.AddSingleton<IAccountQueryService>(
            CreateStatementAccount(
                StatementReportAccountId,
                StatementReportFundId,
                "Broker Alpha",
                "external-alpha"));
        services.AddSingleton<IFundProfileTenancyRegistry>(
            new FixedFundProfileTenancyRegistry(
                new FundProfileOwnership(
                    StatementReportFundId.ToString("D"),
                    "tenant-test",
                    "tenant-test")));
        services.AddSingleton<IScopedAuthorizationService>(
            new StatementReportScopedAuthorizationService(scopedAccountId));
    }

    private static (
        string Status,
        string Resume,
        string Artifact) BuildStatementReconciliationReportProtectedRoutes(
        StatementReconciliationReportWorkflowDto workflow,
        bool useLegacyRoutes)
    {
        var statusTemplate = useLegacyRoutes
            ? UiApiRoutes.ReconciliationStatementToReportById
            : UiApiRoutes.ReconciliationStatementReconciliationReportById;
        var resumeTemplate = useLegacyRoutes
            ? UiApiRoutes.ReconciliationStatementToReportResume
            : UiApiRoutes.ReconciliationStatementReconciliationReportResume;
        var artifactTemplate = useLegacyRoutes
            ? UiApiRoutes.ReconciliationStatementToReportArtifact
            : UiApiRoutes.ReconciliationStatementReconciliationReportArtifact;
        var artifactId = workflow.RetainedArtifacts[0].ArtifactId;
        return (
            UiApiRoutes.WithParam(statusTemplate, "workflowId", workflow.WorkflowId),
            UiApiRoutes.WithParam(resumeTemplate, "workflowId", workflow.WorkflowId),
            UiApiRoutes.WithParam(
                UiApiRoutes.WithParam(artifactTemplate, "workflowId", workflow.WorkflowId),
                "artifactId",
                artifactId));
    }
}
