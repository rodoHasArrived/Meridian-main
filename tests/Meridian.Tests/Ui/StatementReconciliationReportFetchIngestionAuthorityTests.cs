using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Scheduled custodian statement scenario: guards the canonical fetch-to-report adapter against
/// scope-validation bypass and success without retained Operations Continuity authority.
/// </summary>
public sealed class StatementReconciliationReportFetchIngestionAuthorityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-fetch-ingestion-authority-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IngestAsync_ScheduledStatementWithExactPersistedScope_ReturnsRetainedWorkflowAuthority()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var exactScope = BuildExactScope();
        var operationsWorkflowId = Guid.Parse("384b63f4-7a6e-468c-817b-ae21745cd82e");
        var imports = new RecordingImportService(BuildImportResult());
        var intake = new RecordingIntakeAuthority(exactScope, operationsWorkflowId);
        var workflow = BuildWorkflow(imports, intake);
        var adapter = new StatementReconciliationReportFetchIngestionAuthority(workflow, intake);

        var result = await adapter.IngestAsync(BuildCommand(exactScope), timeout.Token);

        intake.ScopeRequests.Should().HaveCount(2,
            "the adapter and retained workflow must each revalidate the exact persisted scope");
        intake.ScopeRequests.Should().OnlyContain(request => request.RequestedScope == exactScope);
        imports.LastRequest.Should().NotBeNull();
        imports.LastRequest!.AccountingScope.Should().Be(exactScope);
        intake.PublishCount.Should().Be(1);
        intake.PublishedScope.Should().Be(exactScope);

        result.StatementReconciliationReportWorkflowId.Should().NotBeNullOrWhiteSpace();
        result.StatementReconciliationReportStatusRoute.Should().EndWith(
            result.StatementReconciliationReportWorkflowId);
        result.OperationsWorkflowId.Should().Be(operationsWorkflowId);
        result.AccountingScope.Should().BeEquivalentTo(ToDto(exactScope));

        var retained = await workflow.GetAsync(
            result.StatementReconciliationReportWorkflowId!,
            "tenant-alpha",
            "company-alpha",
            timeout.Token);
        retained.Should().NotBeNull();
        retained!.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.Completed);
        retained.OperationsWorkflowId.Should().Be(operationsWorkflowId);
        retained.AccountingScope.Should().BeEquivalentTo(ToDto(exactScope));
    }

    [Fact]
    public async Task IngestAsync_ScheduledStatementWithoutOperationsAuthority_Throws()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var exactScope = BuildExactScope();
        var imports = new RecordingImportService(BuildImportResult());
        var intake = new RecordingIntakeAuthority(
            exactScope,
            Guid.Parse("8cb409f1-01bd-4ad4-bbbd-7917fe0670de"));
        var workflowWithoutOperationsAuthority = BuildWorkflow(imports, intakeAuthority: null);
        var adapter = new StatementReconciliationReportFetchIngestionAuthority(
            workflowWithoutOperationsAuthority,
            intake);

        Func<Task> ingest = () => adapter.IngestAsync(BuildCommand(exactScope), timeout.Token);

        await ingest.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*authoritative statement intake service is not configured*");
        intake.ScopeRequests.Should().ContainSingle();
        intake.ScopeRequests.Single().RequestedScope.Should().Be(exactScope);
        intake.PublishCount.Should().Be(0);
        imports.CommitCount.Should().Be(0,
            "missing Operations authority must fail before statement bytes or derived records are retained");
        imports.LastRequest.Should().BeNull();
    }

    private StatementReconciliationReportWorkflowService BuildWorkflow(
        RecordingImportService imports,
        IStatementReconciliationIntakeAuthority? intakeAuthority)
        => new(
            imports,
            new RetainingEvidenceService(),
            new ReconciledStatementRunService(),
            _root,
            logger: null,
            breakQueue: null,
            intakeAuthority: intakeAuthority);

    private static StatementFetchIngestionCommand BuildCommand(StatementAccountingScope accountingScope)
        => new(
            new StatementSourceDocument(
                "scheduled-custodian-statement.csv",
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nexternal-alpha,AAPL,1,100,0,position,2026-06-30"u8.ToArray()),
            ConnectorId: "csv",
            SourceKind: "custodian",
            SourceInstitution: "Custodian Alpha",
            FundAccountId: "fund-account-alpha",
            ExternalAccountId: "external-alpha",
            PeriodStart: new DateOnly(2026, 6, 1),
            PeriodEnd: new DateOnly(2026, 6, 30),
            ToleranceProfileId: "statement-default",
            ImportedBy: "statement-fetch-scheduler",
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha",
            AccountingScope: accountingScope);

    private static StatementAccountingScope BuildExactScope()
        => new(
            "fund-profile-alpha",
            Guid.Parse("832a080d-58ae-49f4-bf74-1f1de813066f"),
            Guid.Parse("9de9a40a-c63e-4760-a070-4cf437558130"),
            new DateOnly(2026, 6, 30));

    private static StatementReconciliationAccountingScopeDto ToDto(StatementAccountingScope scope)
        => new(
            scope.FundProfileId,
            scope.LedgerBookId,
            scope.AccountingPeriodId,
            scope.AsOfDate);

    private static StatementImportCommitResultDto BuildImportResult()
        => new(
            RunId: "statement-run-scheduled-alpha",
            Duplicate: false,
            RecordCount: 1,
            KindSummaries: [new StatementKindSummaryDto("Position", 1, [])],
            BreakCount: 0,
            CaseCount: 0,
            RetainedSourcePath: "reconciliation/statement-connector-imports/source.csv",
            RetainedCanonicalPath: "reconciliation/statement-connector-imports/canonical.csv",
            Status: "Imported",
            NextAction: "Review reconciliation.");

    private sealed class RecordingImportService(StatementImportCommitResultDto result)
        : IStatementImportCommitService
    {
        public int CommitCount { get; private set; }
        public StatementImportCommitRequest? LastRequest { get; private set; }

        public Task<StatementImportCommitResultDto> CommitAsync(
            StatementImportCommitRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CommitCount++;
            LastRequest = request;
            return Task.FromResult(result);
        }

        public Task<StatementImportValidationResult> ValidateAsync(
            StatementSourceDocument document,
            string? connectorId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new StatementImportValidationResult(true, result.RecordCount, []));
        }
    }

    private sealed class RetainingEvidenceService : IStatementImportEvidenceRetainer
    {
        public Task<StatementImportCommitResultDto> RetainAsync(
            StatementImportCommitResultDto result,
            StatementImportEvidenceBridgeRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(result with
            {
                EvidenceVaultIdentity = new EvidenceVaultIdentityDto(
                    "vault-scheduled-alpha",
                    "statement-run",
                    result.RunId,
                    "evidence/manifest.json",
                    "/api/workstation/evidence/vault-scheduled-alpha",
                    DateTimeOffset.Parse("2026-07-01T06:00:00Z"),
                    new string('A', 64),
                    1,
                    "File")
            });
        }
    }

    private sealed class ReconciledStatementRunService : IStatementRunWorkflowService
    {
        public Task<StatementRunWorkflowResult?> GetAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<StatementRunWorkflowResult?>(
                new StatementRunWorkflowResult(null!, [], []));
        }

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);

        public Task<StatementRunWorkflowResult> CreateAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
    }

    private sealed class RecordingIntakeAuthority(
        StatementAccountingScope exactScope,
        Guid operationsWorkflowId) : IStatementReconciliationIntakeAuthority
    {
        public List<StatementReconciliationIntakeScopeRequest> ScopeRequests { get; } = [];
        public int PublishCount { get; private set; }
        public StatementAccountingScope? PublishedScope { get; private set; }

        public Task<StatementAccountingScope> ResolveAccountingScopeAsync(
            StatementReconciliationIntakeScopeRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ScopeRequests.Add(request);
            return Task.FromResult(exactScope);
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
            ct.ThrowIfCancellationRequested();
            PublishCount++;
            PublishedScope = accountingScope;
            return Task.FromResult(new StatementReconciliationIntakeReceipt(
                accountingScope,
                operationsWorkflowId,
                PublishedCaseCount: 0,
                evidenceReferences));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
