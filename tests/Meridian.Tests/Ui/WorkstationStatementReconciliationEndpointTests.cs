using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_StatementToReport_ShouldCompleteAndServeRetainedArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-statement-to-report-endpoint", Guid.NewGuid().ToString("N"));
        try
        {
            var workflow = new StatementToReportWorkflowService(
                new EndpointStatementImportService(),
                new EndpointStatementEvidenceRetainer(),
                new EndpointStatementRunWorkflowService(),
                root);
            await using var app = await CreateAppAsync(services => services.AddSingleton(workflow));
            var client = app.GetTestClient();
            using var content = BuildStatementToReportContent();

            var response = await client.PostAsync(UiApiRoutes.ReconciliationStatementToReport, content);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var result = await response.Content.ReadFromJsonAsync<StatementToReportWorkflowDto>(ServerJsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be(StatementToReportWorkflowStatusDto.Completed);
            result.TenantId.Should().Be("tenant-test");
            result.RetainedArtifacts.Should().HaveCount(2);

            var status = await client.GetFromJsonAsync<StatementToReportWorkflowDto>(
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
    public async Task MapWorkstationEndpoints_StatementRunRoutes_ShouldReturnListDetailExceptionsAndNotFound()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(new StubReconciliationApiService());
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
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IReconciliationApiService>(service);
        });
        var client = app.GetTestClient();

        var create = await client.PostAsJsonAsync(
            UiApiRoutes.ReconciliationStatementRuns,
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: "fund-1",
                ExternalAccountId: "external-1",
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
    }

    private static MultipartFormDataContent BuildStatementToReportContent()
    {
        var content = new MultipartFormDataContent();
        content.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes(
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA,AAPL,1,100,0,position,2026-06-30")),
            "file",
            "statement.csv");
        content.Add(new StringContent("csv"), "connectorId");
        content.Add(new StringContent("broker"), "sourceKind");
        content.Add(new StringContent("Broker Alpha"), "sourceInstitution");
        content.Add(new StringContent("fund-account-alpha"), "fundAccountId");
        content.Add(new StringContent("external-alpha"), "externalAccountId");
        content.Add(new StringContent("2026-06-01"), "periodStart");
        content.Add(new StringContent("2026-06-30"), "periodEnd");
        return content;
    }

    private sealed class EndpointStatementImportService : IStatementImportCommitService
    {
        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default)
            => Task.FromResult(new StatementImportCommitResultDto(
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
}
