using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Tests.Ui;

public sealed class StatementReconciliationReportWorkflowServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-reconciliation-report-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_CleanStatement_RetainsReportsAndIsIdempotentAcrossRestart()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService { ReturnReconciled = true };
        var service = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);
        var command = BuildCommand();

        var first = await service.StartAsync(command);

        first.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        first.Workflow.RetainedArtifacts.Should().HaveCount(2);
        first.Workflow.EvidenceVaultIdentity.Should().NotBeNull();
        imports.CommitCount.Should().Be(1);
        evidence.RetainCount.Should().Be(1);

        var jsonArtifact = first.Workflow.RetainedArtifacts.Single(item =>
            item.ArtifactId == "reconciliation-report-json");
        var download = await service.DownloadArtifactAsync(
            first.Workflow.WorkflowId,
            jsonArtifact.ArtifactId,
            "tenant-alpha",
            "company-alpha");
        download.Should().NotBeNull();
        Convert.ToHexString(SHA256.HashData(download!.Content))
            .Should().Be(jsonArtifact.ContentHashSha256);
        Encoding.UTF8.GetString(download.Content).Should().Contain(first.Workflow.WorkflowId);

        var restarted = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);
        var repeated = await restarted.StartAsync(command);

        repeated.Workflow.WorkflowId.Should().Be(first.Workflow.WorkflowId);
        repeated.Workflow.Version.Should().Be(first.Workflow.Version);
        imports.CommitCount.Should().Be(1, "completed content-addressed workflows must not re-import after restart");
        evidence.RetainCount.Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_AfterReconciliationClears_CompletesFromPersistedImport()
    {
        var imports = new FakeImportService(BuildImportResult(breakCount: 1, caseCount: 1));
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService();
        var service = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);

        var started = await service.StartAsync(BuildCommand());

        started.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        started.Workflow.RetainedArtifacts.Should().BeEmpty();
        started.Workflow.RecoveryAction.Should().Contain("Resolve or disposition");

        runs.ReturnReconciled = true;
        var restarted = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);
        var resumed = await restarted.ResumeAsync(
            started.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        resumed.Should().NotBeNull();
        resumed!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        resumed.Workflow.RetainedArtifacts.Should().HaveCount(2);
        imports.CommitCount.Should().Be(1, "resume must use the persisted import checkpoint");
        var report = await restarted.DownloadArtifactAsync(
            resumed.Workflow.WorkflowId,
            "reconciliation-report-json",
            "tenant-alpha",
            "company-alpha");
        Encoding.UTF8.GetString(report!.Content).Should().Contain("case-alpha",
            "the final report must retain lineage to the reconciliation case that was resolved");
    }

    [Fact]
    public async Task ResumeAsync_AfterEvidenceFailure_DoesNotRepeatCommittedImport()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer { FailNext = true };
        var runs = new FakeStatementRunWorkflowService { ReturnReconciled = true };
        var service = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);

        var failed = await service.StartAsync(BuildCommand());

        failed.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Failed);
        failed.Workflow.RecoveryAction.Should().Contain("retained import");
        imports.CommitCount.Should().Be(1);

        var restarted = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);
        var resumed = await restarted.ResumeAsync(
            failed.Workflow.WorkflowId,
            "tenant-alpha",
            "company-alpha");

        resumed!.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        imports.CommitCount.Should().Be(1);
        evidence.RetainCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_DifferentTenant_FailsClosed()
    {
        var service = new StatementReconciliationReportWorkflowService(
            new FakeImportService(BuildImportResult()),
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true },
            _root);
        var completed = await service.StartAsync(BuildCommand());

        var act = () => service.GetAsync(
            completed.Workflow.WorkflowId,
            "tenant-beta",
            "company-alpha");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task StartAsync_SameContentWithDifferentMappingPolicy_CreatesDistinctWorkflow()
    {
        var imports = new FakeImportService(BuildImportResult());
        var service = new StatementReconciliationReportWorkflowService(
            imports,
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true },
            _root);
        var firstCommand = BuildCommand();
        var secondCommand = firstCommand with
        {
            Import = firstCommand.Import with
            {
                Document = firstCommand.Import.Document with { MappingProfileId = "mapping-profile-v2" }
            }
        };

        var first = await service.StartAsync(firstCommand);
        var second = await service.StartAsync(secondCommand);

        second.Workflow.WorkflowId.Should().NotBe(first.Workflow.WorkflowId,
            "mapping policy is part of the authoritative import identity");
        imports.CommitCount.Should().Be(2);
    }

    [Fact]
    public async Task StartAsync_LegacyPersistedWorkflow_ResumesWithoutAdvertisingLegacyRoutes()
    {
        var imports = new FakeImportService(BuildImportResult());
        var evidence = new FakeEvidenceRetainer();
        var runs = new FakeStatementRunWorkflowService { ReturnReconciled = true };
        var service = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);
        var command = BuildCommand();
        var completed = await service.StartAsync(command);
        var legacyWorkflowId = completed.Workflow.WorkflowId.Replace(
            "statement-reconciliation-report-",
            "statement-report-",
            StringComparison.Ordinal);
        var currentDirectory = Path.Combine(
            _root,
            "reporting",
            "statement-reconciliation-report",
            completed.Workflow.WorkflowId);
        var legacyDirectory = Path.Combine(
            _root,
            "reporting",
            "statement-to-report",
            legacyWorkflowId);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyDirectory)!);
        Directory.Move(currentDirectory, legacyDirectory);
        var snapshotPath = Path.Combine(legacyDirectory, "workflow.json");
        var snapshot = await File.ReadAllTextAsync(snapshotPath);
        snapshot = snapshot
            .Replace(completed.Workflow.WorkflowId, legacyWorkflowId, StringComparison.Ordinal)
            .Replace("RenderingReconciliationReport", "RenderingReport", StringComparison.Ordinal)
            .Replace(
                "/api/workstation/reconciliation/statement-reconciliation-report/",
                "/api/workstation/reconciliation/statement-to-report/",
                StringComparison.Ordinal);
        await File.WriteAllTextAsync(snapshotPath, snapshot);

        var restarted = new StatementReconciliationReportWorkflowService(imports, evidence, runs, _root);
        var resumed = await restarted.StartAsync(command);

        resumed.Workflow.WorkflowId.Should().Be(legacyWorkflowId);
        resumed.Workflow.StatusRoute.Should().StartWith(
            "/api/workstation/reconciliation/statement-reconciliation-report/");
        resumed.Workflow.ResumeRoute.Should().StartWith(
            "/api/workstation/reconciliation/statement-reconciliation-report/");
        resumed.Workflow.RetainedArtifacts.Should().OnlyContain(artifact =>
            artifact.DownloadRoute.StartsWith(
                "/api/workstation/reconciliation/statement-reconciliation-report/",
                StringComparison.Ordinal));
        imports.CommitCount.Should().Be(1);
        evidence.RetainCount.Should().Be(1);
    }

    [Fact]
    public async Task PreRenameServiceAdapter_ShouldDelegateToCanonicalWorkflowAndProjectLegacyLinks()
    {
        var canonicalCommand = BuildCommand();
#pragma warning disable CS0618 // Verifies source compatibility for pre-rename callers.
        var service = new StatementToReportWorkflowService(
            new FakeImportService(BuildImportResult()),
            new FakeEvidenceRetainer(),
            new FakeStatementRunWorkflowService { ReturnReconciled = true },
            _root);

        var completed = await service.StartAsync(
            new StatementToReportStartCommand(
                canonicalCommand.Import,
                canonicalCommand.TenantId,
                canonicalCommand.CompanyId));

        completed.Workflow.Status.Should().Be(StatementToReportWorkflowStatusDto.Completed);
        completed.Workflow.StatusRoute.Should().StartWith(
            "/api/workstation/reconciliation/statement-to-report/");
        completed.Workflow.ResumeRoute.Should().StartWith(
            "/api/workstation/reconciliation/statement-to-report/");
        completed.Workflow.RetainedArtifacts.Should().OnlyContain(artifact =>
            artifact.DownloadRoute.StartsWith(
                "/api/workstation/reconciliation/statement-to-report/",
                StringComparison.Ordinal));
#pragma warning restore CS0618
    }

    private static StatementReconciliationReportStartCommand BuildCommand()
        => new(
            new StatementImportCommitRequest(
                new StatementSourceDocument(
                    "broker-statement.csv",
                    "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA,AAPL,1,100,0,position,2026-06-30"u8.ToArray()),
                ConnectorId: "csv",
                SourceKind: "broker",
                SourceInstitution: "Broker Alpha",
                FundAccountId: "fund-account-alpha",
                ExternalAccountId: "external-alpha",
                PeriodStart: new DateOnly(2026, 6, 1),
                PeriodEnd: new DateOnly(2026, 6, 30),
                ToleranceProfileId: null,
                ImportedBy: "operator-alpha"),
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha");

    private static StatementImportCommitResultDto BuildImportResult(int breakCount = 0, int caseCount = 0)
        => new(
            RunId: "statement-run-alpha",
            Duplicate: false,
            RecordCount: 2,
            KindSummaries:
            [
                new StatementKindSummaryDto("Position", 1, []),
                new StatementKindSummaryDto("Transaction", 1, [])
            ],
            BreakCount: breakCount,
            CaseCount: caseCount,
            RetainedSourcePath: "reconciliation/statement-connector-imports/source.csv",
            RetainedCanonicalPath: "reconciliation/statement-connector-imports/canonical.csv",
            Status: "Imported",
            NextAction: "Review reconciliation.")
        {
            BreakIds = breakCount == 0 ? [] : ["break-alpha"],
            CaseIds = caseCount == 0 ? [] : ["case-alpha"]
        };

    private sealed class FakeImportService(StatementImportCommitResultDto result) : IStatementImportCommitService
    {
        public int CommitCount { get; private set; }

        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default)
        {
            CommitCount++;
            return Task.FromResult(result);
        }

        public Task<StatementImportValidationResult> ValidateAsync(
            StatementSourceDocument document,
            string? connectorId,
            CancellationToken ct = default)
            => Task.FromResult(new StatementImportValidationResult(true, result.RecordCount, []));
    }

    private sealed class FakeEvidenceRetainer : IStatementImportEvidenceRetainer
    {
        public int RetainCount { get; private set; }
        public bool FailNext { get; set; }

        public Task<StatementImportCommitResultDto> RetainAsync(
            StatementImportCommitResultDto result,
            StatementImportEvidenceBridgeRequest request,
            CancellationToken ct = default)
        {
            RetainCount++;
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("Evidence store temporarily unavailable.");
            }

            return Task.FromResult(result with
            {
                EvidenceVaultIdentity = new EvidenceVaultIdentityDto(
                    "vault-alpha",
                    "statement-run",
                    result.RunId,
                    "evidence/manifest.json",
                    "/api/workstation/evidence/vault-alpha",
                    DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
                    new string('A', 64),
                    1,
                    "File")
            });
        }
    }

    private sealed class FakeStatementRunWorkflowService : IStatementRunWorkflowService
    {
        public bool ReturnReconciled { get; set; }

        public Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<StatementRunWorkflowResult?>(
                ReturnReconciled ? new StatementRunWorkflowResult(null!, [], []) : null);

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
