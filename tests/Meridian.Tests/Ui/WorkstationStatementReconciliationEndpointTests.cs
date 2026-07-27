using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private static readonly Guid StatementReportAccountId =
        Guid.Parse("8c5224d3-95d5-493f-a8c5-7272f8c49c14");
    private static readonly Guid StatementReportFundId =
        Guid.Parse("b581a990-d754-464c-8408-d18ae3f1621c");
    private static readonly Guid StatementReportLedgerBookId =
        Guid.Parse("287a2b1c-f05e-4ddf-a988-19ea3c812498");
    private static readonly Guid StatementReportPeriodId =
        Guid.Parse("e56a2c52-23cb-4d14-bda3-b91e9ce810a0");
    private static readonly Guid StatementReportOperationsWorkflowId =
        Guid.Parse("1dca2044-cdbf-4bc4-92c6-d1e23770fd39");

    [Fact]
    public async Task MapWorkstationEndpoints_StatementImportCommit_ShouldDelegateToCanonicalIntakeAuthority()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-import-authority-endpoint",
            Guid.NewGuid().ToString("N"));
        try
        {
            var intake = new RecordingStatementReconciliationIntakeAuthority();
            var workflow = new StatementReconciliationReportWorkflowService(
                new EndpointStatementImportService(),
                new EndpointStatementEvidenceRetainer(),
                new EndpointStatementRunWorkflowService(),
                root,
                logger: null,
                breakQueue: null,
                intakeAuthority: intake);
            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(workflow);
                services.AddSingleton<IStatementReconciliationIntakeAuthority>(intake);
                RegisterStatementReportAuthority(services);
            });
            using var content = BuildStatementReconciliationReportContent();

            var response = await app.GetTestClient().PostAsync(
                UiApiRoutes.ReconciliationStatementImportCommit,
                content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<StatementImportCommitResultDto>(
                ServerJsonOptions);
            result.Should().NotBeNull();
            result!.StatementReconciliationReportWorkflowId.Should().StartWith(
                "statement-reconciliation-report-");
            result.OperationsWorkflowId.Should().Be(StatementReportOperationsWorkflowId);
            result.AccountingScope.Should().BeEquivalentTo(
                new StatementReconciliationAccountingScopeDto(
                    StatementReportFundId.ToString("D"),
                    StatementReportLedgerBookId,
                    StatementReportPeriodId,
                    new DateOnly(2026, 6, 30)));
            intake.ResolveCount.Should().Be(1);
            intake.PublishCount.Should().Be(1);
            intake.LastTenantId.Should().Be("tenant-test");
            intake.LastCompanyId.Should().Be("tenant-test");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementReconciliationReport_ShouldCompleteAndServeRetainedArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-statement-reconciliation-report-endpoint", Guid.NewGuid().ToString("N"));
        try
        {
            var intake = new RecordingStatementReconciliationIntakeAuthority();
            var workflow = new StatementReconciliationReportWorkflowService(
                new EndpointStatementImportService(),
                new EndpointStatementEvidenceRetainer(),
                new EndpointStatementRunWorkflowService(),
                root,
                logger: null,
                breakQueue: null,
                intakeAuthority: intake);
            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(workflow);
                services.AddSingleton<IStatementReconciliationIntakeAuthority>(intake);
                RegisterStatementReportAuthority(services);
            });
            var client = app.GetTestClient();
            using var content = BuildStatementReconciliationReportContent();

            var response = await client.PostAsync(UiApiRoutes.ReconciliationStatementReconciliationReport, content);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var result = await response.Content.ReadFromJsonAsync<StatementReconciliationReportWorkflowDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
            result.TenantId.Should().Be("tenant-test");
            result.RetainedArtifacts.Should().HaveCount(2);

            var status = await client.GetFromJsonAsync<StatementReconciliationReportWorkflowDto>(
                result.StatusRoute,
                ServerJsonOptions);
            status!.WorkflowId.Should().Be(result.WorkflowId);
            var artifactResponse = await client.GetAsync(result.RetainedArtifacts[0].DownloadRoute);
            artifactResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            (await artifactResponse.Content.ReadAsStringAsync()).Should().Contain(result.WorkflowId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_PreRenameStatementReportRoutes_ShouldProjectLegacyContractDirectly()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-to-report-compat-endpoint",
            Guid.NewGuid().ToString("N"));
        try
        {
            var intake = new RecordingStatementReconciliationIntakeAuthority();
            var workflow = new StatementReconciliationReportWorkflowService(
                new EndpointStatementImportService(),
                new EndpointStatementEvidenceRetainer(),
                new EndpointStatementRunWorkflowService(),
                root,
                logger: null,
                breakQueue: null,
                intakeAuthority: intake);
            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(workflow);
                services.AddSingleton<IStatementReconciliationIntakeAuthority>(intake);
                RegisterStatementReportAuthority(services);
            });
            var client = app.GetTestClient();
            using var content = BuildStatementReconciliationReportContent();

            var startedResponse = await client.PostAsync(
                UiApiRoutes.ReconciliationStatementToReport,
                content);

            startedResponse.StatusCode.Should().Be(HttpStatusCode.Created);
#pragma warning disable CS0618 // Verifies the retained pre-rename HTTP contract.
            var started = await startedResponse.Content.ReadFromJsonAsync<StatementToReportWorkflowDto>(
                ServerJsonOptions);
            started.Should().NotBeNull();
            started!.Status.Should().Be(StatementToReportWorkflowStatusDto.Completed);
            started.StatusRoute.Should().StartWith(
                "/api/workstation/reconciliation/statement-to-report/");
            started.ResumeRoute.Should().StartWith(
                "/api/workstation/reconciliation/statement-to-report/");

            var statusResponse = await client.GetAsync(started.StatusRoute);
            statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var status = await statusResponse.Content.ReadFromJsonAsync<StatementToReportWorkflowDto>(
                ServerJsonOptions);
            status!.Status.Should().Be(StatementToReportWorkflowStatusDto.Completed);

            var resumeResponse = await client.PostAsync(started.ResumeRoute, content: null);
            resumeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var resumed = await resumeResponse.Content.ReadFromJsonAsync<StatementToReportWorkflowDto>(
                ServerJsonOptions);
            resumed!.Status.Should().Be(StatementToReportWorkflowStatusDto.Completed);

            var artifactResponse = await client.GetAsync(started.RetainedArtifacts[0].DownloadRoute);
            artifactResponse.StatusCode.Should().Be(HttpStatusCode.OK);
#pragma warning restore CS0618
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatementReconciliationReport_LegacyProjection_ShouldRetainOldWireStatusAndLinks()
    {
        var retainedAt = DateTimeOffset.Parse("2026-07-26T12:00:00Z");
        var canonical = new StatementReconciliationReportWorkflowDto(
            "statement-reconciliation-report-retained",
            StatementReconciliationReportWorkflowStatusDto.RenderingReconciliationReport,
            3,
            "tenant-test",
            "company-test",
            "Broker",
            "fund-1",
            "external-1",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "run-1",
            null,
            [
                new StatementReconciliationReportArtifactDto(
                    "artifact-1",
                    "reconciliation-report-json",
                    "report.json",
                    "application/json",
                    10,
                    new string('a', 64),
                    "/canonical-artifact",
                    retainedAt)
            ],
            ["evidence-1"],
            1,
            1,
            retainedAt,
            retainedAt,
            null,
            null,
            null,
            "/canonical-status",
            "/canonical-resume");

#pragma warning disable CS0618 // Verifies the retained pre-rename wire contract.
        var legacy = Meridian.Ui.Shared.Endpoints.WorkstationEndpoints
            .ToLegacyStatementToReportWorkflow(canonical);

        legacy.Status.Should().Be(StatementToReportWorkflowStatusDto.RenderingReport);
        legacy.StatusRoute.Should().StartWith(
            UiApiRoutes.ReconciliationStatementToReportById.Split('{')[0]);
        legacy.ResumeRoute.Should().StartWith(
            UiApiRoutes.ReconciliationStatementToReportResume.Split('{')[0]);
        legacy.RetainedArtifacts[0].DownloadRoute.Should().Contain(
            UiApiRoutes.ReconciliationStatementToReportArtifact.Split('{')[0]);
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData("statement.bai")]
    [InlineData("statement.bai2")]
    [InlineData("statement.camt")]
    [InlineData("statement.053")]
    public async Task MapWorkstationEndpoints_StatementReconciliationReport_ShouldAcceptRegisteredConnectorExtensions(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-statement-connector-extension", Guid.NewGuid().ToString("N"));
        try
        {
            var intake = new RecordingStatementReconciliationIntakeAuthority();
            var workflow = new StatementReconciliationReportWorkflowService(
                new EndpointStatementImportService(),
                new EndpointStatementEvidenceRetainer(),
                new EndpointStatementRunWorkflowService(),
                root,
                logger: null,
                breakQueue: null,
                intakeAuthority: intake);
            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(workflow);
                services.AddSingleton<IStatementReconciliationIntakeAuthority>(intake);
                RegisterStatementReportAuthority(services);
            });
            using var content = BuildStatementReconciliationReportContent(fileName);

            var response = await app.GetTestClient().PostAsync(UiApiRoutes.ReconciliationStatementReconciliationReport, content);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementReconciliationReport_ForeignTenantAccountFailsBeforeInputRetention()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-report-foreign-account",
            Guid.NewGuid().ToString("N"));
        try
        {
            var imports = new EndpointStatementImportService();
            var intake = new RecordingStatementReconciliationIntakeAuthority();
            var workflow = new StatementReconciliationReportWorkflowService(
                imports,
                new EndpointStatementEvidenceRetainer(),
                new EndpointStatementRunWorkflowService(),
                root,
                logger: null,
                breakQueue: null,
                intakeAuthority: intake);
            await using var app = await CreateAppAsync(services =>
            {
                services.AddSingleton(workflow);
                services.AddSingleton<IStatementReconciliationIntakeAuthority>(intake);
                RegisterStatementReportAuthority(
                    services,
                    new FundProfileOwnership(
                        StatementReportFundId.ToString("D"),
                        "tenant-other",
                        "company-other"));
            });
            using var content = BuildStatementReconciliationReportContent();

            var response = await app.GetTestClient().PostAsync(
                UiApiRoutes.ReconciliationStatementReconciliationReport,
                content);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            imports.CommitCount.Should().Be(0);
            Directory.Exists(root).Should().BeFalse(
                "foreign account scope must be rejected before the workflow retains uploaded input");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementFetchPreview_ForeignTenantAccountFailsBeforeProviderFetch()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-fetch-preview-foreign-account",
            Guid.NewGuid().ToString("N"));
        try
        {
            var connector = new RecordingFetchingStatementConnector();
            var importService = new StatementImportService(
                new StatementConnectorRegistry([connector]),
                new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(root)),
                new EndpointStatementRunWorkflowService(),
                root);
            await using var app = await CreateAppAsync(
                services =>
                {
                    services.AddSingleton(importService);
                    RegisterStatementReportAuthority(
                        services,
                        new FundProfileOwnership(
                            StatementReportFundId.ToString("D"),
                            "tenant-other",
                            "company-other"));
                },
                currentUserPermissions: UserPermission.AdminMaintenance);

            var response = await app.GetTestClient().PostAsJsonAsync(
                UiApiRoutes.ReconciliationStatementFetchPreview,
                new
                {
                    connectorId = RecordingFetchingStatementConnector.ConnectorId,
                    externalAccountId = "external-alpha",
                    fundAccountId = StatementReportAccountId.ToString("D"),
                    sourceInstitution = "Broker Alpha",
                    sourceKind = "broker",
                    datasets = "activity"
                },
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            connector.FetchCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementFetchPreview_MissingCompanyScopeFailsBeforeProviderFetch()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-fetch-preview-missing-company",
            Guid.NewGuid().ToString("N"));
        try
        {
            var connector = new RecordingFetchingStatementConnector();
            var importService = new StatementImportService(
                new StatementConnectorRegistry([connector]),
                new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(root)),
                new EndpointStatementRunWorkflowService(),
                root);
            await using var app = await CreateAppAsync(
                services =>
                {
                    services.AddSingleton(importService);
                    RegisterStatementReportAuthority(services);
                },
                currentUserPermissions: UserPermission.AdminMaintenance,
                currentUserCompanyId: null,
                currentUserTenantId: "tenant-test");

            var response = await app.GetTestClient().PostAsJsonAsync(
                UiApiRoutes.ReconciliationStatementFetchPreview,
                new
                {
                    connectorId = RecordingFetchingStatementConnector.ConnectorId,
                    externalAccountId = "external-alpha",
                    fundAccountId = StatementReportAccountId.ToString("D"),
                    sourceInstitution = "Broker Alpha",
                    sourceKind = "broker",
                    datasets = "activity"
                },
                ServerJsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            connector.FetchCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementImportPreview_ForeignTenantAccountFailsBeforeParsing()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-statement-import-preview-foreign-account",
            Guid.NewGuid().ToString("N"));
        try
        {
            var connector = new RecordingFetchingStatementConnector();
            var importService = new StatementImportService(
                new StatementConnectorRegistry([connector]),
                new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(root)),
                new EndpointStatementRunWorkflowService(),
                root);
            await using var app = await CreateAppAsync(
                services =>
                {
                    services.AddSingleton(importService);
                    RegisterStatementReportAuthority(
                        services,
                        new FundProfileOwnership(
                            StatementReportFundId.ToString("D"),
                            "tenant-other",
                            "company-other"));
                },
                currentUserPermissions: UserPermission.AdminMaintenance);
            using var content = BuildStatementReconciliationReportContent();

            var response = await app.GetTestClient().PostAsync(
                UiApiRoutes.ReconciliationStatementImportPreview,
                content);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            connector.ParseCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunRoutes_ShouldReturnListDetailExceptionsAndNotFound()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        });
        var client = app.GetTestClient();

        var list = await client.GetFromJsonAsync<List<StatementRunSummaryDto>>(
            UiApiRoutes.ReconciliationStatementRuns,
            ServerJsonOptions);
        var detail = await client.GetFromJsonAsync<StatementRunSummaryDto>(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunById, "runId", "statement-run-1"),
            ServerJsonOptions);
        var exceptions = await client.GetFromJsonAsync<List<StatementRunExceptionDto>>(
            UiApiRoutes.ReconciliationStatementExceptions,
            ServerJsonOptions);
        var missing = await client.GetAsync(UiApiRoutes.WithParam(
            UiApiRoutes.ReconciliationStatementRunById,
            "runId",
            "missing-run"));

        list.Should().ContainSingle(run => run.RunId == "statement-run-1" && run.OpenExceptionCount == 1);
        detail.Should().NotBeNull();
        detail!.RunId.Should().Be("statement-run-1");
        exceptions.Should().ContainSingle(item => item.RunId == "statement-run-1" && item.ToleranceBreached);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        service.ObservedScopes.Should().OnlyContain(scope =>
            scope.TenantId == "tenant-test" &&
            scope.CompanyId == "tenant-test");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunRoutes_ShouldSurfaceRegistrationBlocker()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.ReconciliationStatementRuns);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunMutationRoutes_ShouldTrustAuthenticatedActor()
    {
        var service = new StubReconciliationApiService();
        var accountId = Guid.NewGuid();
        var accounts = CreateStatementAccount(accountId);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
            services.AddSingleton<IAccountQueryService>(accounts);
        }, currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        var create = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: accountId.ToString("D"),
                ExternalAccountId: "external-allowed",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: @"C:\imports\statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "browser-spoof"),
            ServerJsonOptions);
        var reconcile = await client.PostAsJsonAsync(
            UiApiRoutes.WithParam(UiApiRoutes.ReconciliationStatementRunReconcile, "runId", "statement-run-1"),
            new StatementRunReconcileRequestDto(Actor: "browser-spoof", Reason: "reconcile"),
            ServerJsonOptions);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        reconcile.StatusCode.Should().Be(HttpStatusCode.OK);
        service.CreatedRequests.Should().ContainSingle();
        service.CreatedRequests[0].ImportedBy.Should().Be("ops-user");
        service.ReconciledRequests.Should().ContainSingle();
        service.ReconciledRequests[0].RunId.Should().Be("statement-run-1");
        service.ReconciledRequests[0].Request.Actor.Should().Be("ops-user");
        service.ObservedScopes.Should().OnlyContain(scope =>
            scope.TenantId == "tenant-test" &&
            scope.CompanyId == "tenant-test");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunRoutes_WithoutCompanyScope_ShouldFailClosed()
    {
        var service = new StubReconciliationApiService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IReconciliationApiService>(service),
            currentUserCompanyId: null,
            currentUserTenantId: "tenant-only");

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.ReconciliationStatementRuns);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.ObservedScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunCreate_ShouldRejectUnboundExternalAccount()
    {
        var service = new StubReconciliationApiService();
        var accounts = CreateStatementAccount(Guid.NewGuid());
        var account = (await accounts.ListAccountsAsync(null, null, null)).Single();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
            services.AddSingleton<IAccountQueryService>(accounts);
        }, currentUserPermissions: Meridian.Identity.Auth.UserPermission.AdminMaintenance);

        var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: account.AccountId.ToString("D"),
                ExternalAccountId: "external-other",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: @"C:\imports\statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "browser-spoof"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.CreatedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StatementRunCreate_ShouldRejectUnscopedAccount()
    {
        var service = new StubReconciliationApiService();
        var accounts = CreateStatementAccount(Guid.NewGuid());
        var account = (await accounts.ListAccountsAsync(null, null, null)).Single();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
            services.AddSingleton<IAccountQueryService>(accounts);
        }, currentUserPermissions: Meridian.Identity.Auth.UserPermission.ManageDirectLending);

        var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: account.AccountId.ToString("D"),
                ExternalAccountId: "external-allowed",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: @"C:\imports\statement.csv",
                OriginalFileName: "statement.csv",
                MappingProfileId: "mapping-v1",
                ToleranceProfileId: "tolerance-v1",
                ImportedBy: "browser-spoof"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.CreatedRequests.Should().BeEmpty();
    }

    private static InMemoryFundAccountService CreateStatementAccount(
        Guid accountId,
        Guid? fundId = null,
        string institution = "Sample Custodian",
        string externalAccountId = "external-allowed")
    {
        var accounts = new InMemoryFundAccountService();
        accounts.CreateAccountAsync(new Meridian.Contracts.FundStructure.CreateAccountRequest(
            accountId,
            Meridian.Contracts.FundStructure.AccountTypeDto.Custody,
            "ACCOUNT-ALLOWED",
            "Allowed statement account",
            "USD",
            DateTimeOffset.UtcNow,
            "test-operator",
            FundId: fundId,
            Institution: institution,
            CustodianDetails: new Meridian.Contracts.FundStructure.CustodianAccountDetailsDto(
                externalAccountId, null, null, null, null, null, null, null))).GetAwaiter().GetResult();
        return accounts;
    }

    private static void RegisterStatementReportAuthority(
        IServiceCollection services,
        FundProfileOwnership? ownership = null)
    {
        services.AddSingleton<IAccountQueryService>(
            CreateStatementAccount(
                StatementReportAccountId,
                StatementReportFundId,
                "Broker Alpha",
                "external-alpha"));
        services.AddSingleton<IFundProfileTenancyRegistry>(
            new FixedFundProfileTenancyRegistry(
                ownership ?? new FundProfileOwnership(
                    StatementReportFundId.ToString("D"),
                    "tenant-test",
                    "tenant-test")));
        services.AddSingleton<IScopedAuthorizationService>(
            new StatementReportScopedAuthorizationService(StatementReportAccountId));
    }

    private static MultipartFormDataContent BuildStatementReconciliationReportContent(string fileName = "statement.csv")
    {
        var content = new MultipartFormDataContent();
        content.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes(
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA,AAPL,1,100,0,position,2026-06-30")),
            "file",
            fileName);
        content.Add(new StringContent("csv"), "connectorId");
        content.Add(new StringContent("broker"), "sourceKind");
        content.Add(new StringContent("Broker Alpha"), "sourceInstitution");
        content.Add(new StringContent(StatementReportAccountId.ToString("D")), "fundAccountId");
        content.Add(new StringContent("external-alpha"), "externalAccountId");
        content.Add(new StringContent("2026-06-01"), "periodStart");
        content.Add(new StringContent("2026-06-30"), "periodEnd");
        return content;
    }

    private sealed class EndpointStatementImportService : IStatementImportCommitService
    {
        public int CommitCount { get; private set; }

        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default)
        {
            CommitCount++;
            return Task.FromResult(new StatementImportCommitResultDto(
                "statement-run-endpoint",
                Duplicate: false,
                RecordCount: 1,
                KindSummaries: [new StatementKindSummaryDto("Position", 1, [])],
                BreakCount: 0,
                CaseCount: 0,
                RetainedSourcePath: "reconciliation/source.csv",
                RetainedCanonicalPath: "reconciliation/canonical.csv",
                Status: "Imported",
                NextAction: "Build report."));
        }

        public Task<StatementImportValidationResult> ValidateAsync(
            StatementSourceDocument document,
            string? connectorId,
            CancellationToken ct = default)
            => Task.FromResult(new StatementImportValidationResult(true, 1, []));
    }

    private sealed class FixedFundProfileTenancyRegistry(FundProfileOwnership ownership)
        : IFundProfileTenancyRegistry
    {
        public Task<FundProfileOwnership> BindAsync(
            string fundProfileId,
            string tenantId,
            string? companyId = null,
            CancellationToken ct = default)
            => Task.FromResult(ownership);

        public Task<FundProfileOwnership?> ResolveAsync(
            string fundProfileId,
            CancellationToken ct = default)
            => Task.FromResult<FundProfileOwnership?>(
                string.Equals(
                    fundProfileId,
                    ownership.FundProfileId,
                    StringComparison.OrdinalIgnoreCase)
                    ? ownership
                    : null);

        public async Task<bool> IsAccessibleAsync(
            string fundProfileId,
            string tenantId,
            string? companyId = null,
            CancellationToken ct = default)
            => (await ResolveAsync(fundProfileId, ct))?.IsHeldBy(tenantId) == true;
    }

    private sealed class StatementReportScopedAuthorizationService(Guid accountId)
        : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
            => Task.FromResult(
                new ScopedAuthorizationDecisionDto(
                    scopeKind == AccessScopeKindDto.Account && scopeId == accountId,
                    actor,
                    requiredPermission,
                    scopeKind,
                    scopeId,
                    "statement-report-test"));
    }

    private sealed class EndpointStatementEvidenceRetainer : IStatementImportEvidenceRetainer
    {
        public Task<StatementImportCommitResultDto> RetainAsync(
            StatementImportCommitResultDto result,
            StatementImportEvidenceBridgeRequest request,
            CancellationToken ct = default)
            => Task.FromResult(result with
            {
                EvidenceVaultIdentity = new EvidenceVaultIdentityDto(
                    "vault-endpoint",
                    "statement-run",
                    result.RunId,
                    "evidence/manifest.json",
                    "/api/workstation/evidence/vault-endpoint",
                    DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
                    new string('A', 64),
                    1,
                    "File")
            });
    }

    private sealed class RecordingStatementReconciliationIntakeAuthority
        : IStatementReconciliationIntakeAuthority
    {
        public int ResolveCount { get; private set; }
        public int PublishCount { get; private set; }
        public string? LastTenantId { get; private set; }
        public string? LastCompanyId { get; private set; }

        public Task<StatementAccountingScope> ResolveAccountingScopeAsync(
            StatementReconciliationIntakeScopeRequest request,
            CancellationToken ct = default)
        {
            ResolveCount++;
            LastTenantId = request.TenantId;
            LastCompanyId = request.CompanyId;
            return Task.FromResult(new StatementAccountingScope(
                StatementReportFundId.ToString("D"),
                StatementReportLedgerBookId,
                StatementReportPeriodId,
                new DateOnly(2026, 6, 30)));
        }

        public Task<StatementReconciliationIntakeReceipt> PublishAsync(
            string statementWorkflowId,
            StatementImportCommitResultDto import,
            StatementAccountingScope accountingScope,
            string tenantId,
            string companyId,
            string actor,
            string sourceInstitution,
            IReadOnlyList<string> evidenceReferences,
            CancellationToken ct = default)
        {
            PublishCount++;
            return Task.FromResult(new StatementReconciliationIntakeReceipt(
                accountingScope,
                StatementReportOperationsWorkflowId,
                PublishedCaseCount: 0,
                evidenceReferences));
        }
    }

    private sealed class EndpointStatementRunWorkflowService : IStatementRunWorkflowService
    {
        public Task<StatementRunWorkflowResult?> GetAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult<StatementRunWorkflowResult?>(new StatementRunWorkflowResult(null!, [], []));

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);

        public Task<StatementRunWorkflowResult> CreateAsync(StatementRunRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
    }

    private sealed class RecordingFetchingStatementConnector : IFetchingStatementConnector
    {
        public const string ConnectorId = "recording-statement-fetch";

        public int FetchCount { get; private set; }
        public int ParseCount { get; private set; }

        public StatementConnectorDescriptor Descriptor { get; } = new(
            ConnectorId,
            "Recording statement fetch",
            [".json"],
            SupportsFileImport: true,
            SupportsRemoteFetch: true,
            RequiresMappingProfile: false,
            DefaultProfileId: null);

        public bool CanHandle(StatementSourceDocument document) => true;

        public Task<StatementSourceDocument> FetchAsync(
            StatementFetchRequest request,
            CancellationToken ct = default)
        {
            FetchCount++;
            return Task.FromResult(new StatementSourceDocument(
                "recording-statement.json",
                "{}"u8.ToArray(),
                request.MappingProfileId,
                request.ExternalAccountId));
        }

        public Task<StatementParseResult> ParseAsync(
            StatementSourceDocument document,
            CancellationToken ct = default)
        {
            ParseCount++;
            throw new NotSupportedException("The authorization tests must fail before parsing.");
        }
    }
}
