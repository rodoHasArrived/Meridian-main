using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the month-end statement-intake failure mode where ambiguous or closed accounting scope
/// could otherwise retain evidence outside the exact account, tenant, company, book, and period
/// authority, or leave governed reconciliation casework unpublished until an operator opens a UI.
/// </summary>
[Trait("Category", "Scenario")]
public sealed class StatementReconciliationIntakeAuthorityTests : IDisposable
{
    private const string TenantId = "tenant-alpha";
    private const string CompanyId = "company-alpha";
    private const string ExternalAccountId = "CUSTODY-7842";
    private const string SourceInstitution = "Northstar Custody";
    private const string StatementRunId = "statement-run-june-2026";
    private const string SourceBreakId = "statement-break-cash-june-2026";
    private static readonly Guid FundAccountId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FundProfileId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LedgerBookId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AccountingPeriodId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly PeriodStart = new(2026, 6, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 6, 30);
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 1, 8, 15, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-intake-authority-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Scenario_MonthEndStatementIntake_ExactAuthorityPublishesCaseworkAndOperationsEvidenceBeforeReview()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var harness = CreateHarness(
            [Book(LedgerBookId)],
            [Period(AccountingPeriodId, LedgerBookId, LedgerPeriodStatusDto.Open)]);

        var execution = await harness.Workflow.StartAsync(Command(), ct);

        var exactScope = new StatementAccountingScope(
            FundProfileId.ToString("D"),
            LedgerBookId,
            AccountingPeriodId,
            PeriodEnd);
        execution.Workflow.WorkflowId.Should().StartWith("statement-reconciliation-report-");
        execution.Workflow.Status.Should().Be(
            StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        execution.Workflow.AccountingScope.Should().BeEquivalentTo(exactScope);
        execution.Workflow.RetainedArtifacts.Should().BeEmpty(
            "the existing statement workflow must begin governed casework before rendering");
        harness.Imports.CommitCount.Should().Be(1);
        harness.Imports.LastRequest.Should().NotBeNull();
        harness.Imports.LastRequest!.AccountingScope.Should().Be(exactScope);

        var queueItem = (await harness.Queue.GetAllAsync(ct: ct))
            .Should().ContainSingle().Which;
        queueItem.SourceType.Should().Be("statement");
        queueItem.SourceImportId.Should().Be(StatementRunId);
        queueItem.SourceBreakId.Should().Be(SourceBreakId);
        queueItem.FundAccountId.Should().Be(FundAccountId.ToString("D"));
        queueItem.FundProfileId.Should().Be(FundProfileId.ToString("D"));
        queueItem.LedgerBookId.Should().Be(LedgerBookId);
        queueItem.AccountingPeriodId.Should().Be(AccountingPeriodId.ToString("D"));
        queueItem.AsOfDate.Should().Be(PeriodEnd);
        queueItem.BlockedOutputs.Should().BeEquivalentTo(
            ["FinalReport", "PeriodClose", "ClientDelivery"]);

        var operationsSummary = (await harness.Operations.ListAsync(
                FundAccountId,
                AccountingPeriodId.ToString("D"),
                status: null,
                ct: ct,
                ledgerBookId: LedgerBookId))
            .Should().ContainSingle().Which;
        var operations = await harness.Operations.GetAsync(operationsSummary.WorkflowId, ct);
        operations.Should().NotBeNull();
        operations!.BrokerIntakeState.Should().Be(
            OperationsBrokerIntakeStateDto.Imported,
            "new statement intake must advance the existing Operations workflow through retained broker import");
        operations.LedgerBookId.Should().Be(LedgerBookId);
        execution.Workflow.OperationsWorkflowId.Should().Be(operations.WorkflowId);

        var timelineEvidence = (await harness.Operations
                .GetTimelineAsync(operations.WorkflowId, ct))
            .SelectMany(static entry => entry.References)
            .ToArray();
        timelineEvidence.Should().Contain(reference =>
            reference.EvidenceId == $"statement-intake:{StatementRunId}"
            && reference.Source == "statement-reconciliation-report"
            && reference.Route ==
            $"/api/workstation/reconciliation/statement-reconciliation-report/{execution.Workflow.WorkflowId}");
        timelineEvidence.Should().Contain(reference =>
            reference.Source == "statement-evidence-vault"
            && reference.Route == "evidence-vault:statement-vault-june-2026");
        execution.Workflow.EvidenceReferences.Should().Contain(
            $"operations-workflow:{operations.WorkflowId:D}");

        harness.Accounts.Verify(
            service => service.GetAccountAsync(FundAccountId, It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "account ownership is resolved before retention and revalidated before publication");
        harness.Tenancy.Verify(
            registry => registry.ResolveAsync(
                FundProfileId.ToString("D"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "tenant/company ownership is resolved before retention and revalidated before publication");
        harness.LedgerBooks.Verify(
            service => service.ListBooksAsync(
                It.Is<LedgerBookQuery>(query =>
                    query.FundProfileId == FundProfileId.ToString("D")
                    && query.AccountingBasis == AccountingBasisKindDto.Primary),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "the primary ledger book is resolved before retention and revalidated before publication");
        harness.LedgerBooks.Verify(
            service => service.ListPeriodsAsync(
                It.Is<LedgerPeriodQuery>(query => query.LedgerBookId == LedgerBookId),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "the exact accounting period is resolved before retention and revalidated before publication");
    }

    [Fact]
    public async Task PublishAsync_ReplayBeforeCallerCheckpoint_DoesNotRecreateUnscopedPredecessor()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var harness = CreateHarness(
            [Book(LedgerBookId)],
            [Period(AccountingPeriodId, LedgerBookId, LedgerPeriodStatusDto.Open)]);
        var scope = new StatementAccountingScope(
            FundProfileId.ToString("D"),
            LedgerBookId,
            AccountingPeriodId,
            PeriodEnd);

        var first = await harness.Authority.PublishAsync(
            "statement-reconciliation-report-replay-window",
            ImportResult(),
            scope,
            TenantId,
            CompanyId,
            "statement-operations",
            SourceInstitution,
            ["evidence-vault:statement-vault-june-2026"],
            ct);

        // Simulate a process loss after PublishAsync committed but before the caller persisted
        // OperationsWorkflowId into its own workflow snapshot.
        var replay = await harness.Authority.PublishAsync(
            "statement-reconciliation-report-replay-window",
            ImportResult(),
            scope,
            TenantId,
            CompanyId,
            "statement-operations",
            SourceInstitution,
            ["evidence-vault:statement-vault-june-2026"],
            ct);

        replay.OperationsWorkflowId.Should().Be(first.OperationsWorkflowId);
        var retained = (await harness.Queue.GetAllAsync(ct: ct))
            .Should().ContainSingle(
                "a replay must keep one destination-scoped case and must not recreate its removed unscoped predecessor")
            .Which;
        retained.FundProfileId.Should().Be(scope.FundProfileId);
        retained.LedgerBookId.Should().Be(scope.LedgerBookId);
        retained.AccountingPeriodId.Should().Be(scope.AccountingPeriodId.ToString("D"));
        retained.AsOfDate.Should().Be(scope.AsOfDate);
        retained.BlockedOutputs.Should().NotContain("reconciliation-scope-resolution");
        (await harness.Queue.GetAuditHistoryAsync(retained.BreakId, ct))
            .Should().ContainSingle(entry => entry.EventType == "CreateReplayAccepted");
    }

    [Fact]
    public async Task Scenario_AmbiguousPrimaryLedgerAuthority_FailsClosedBeforeStatementRetention()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var secondBookId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var harness = CreateHarness(
            [Book(LedgerBookId), Book(secondBookId)],
            [Period(AccountingPeriodId, LedgerBookId, LedgerPeriodStatusDto.Open)]);

        var act = () => harness.Workflow.StartAsync(Command(), timeout.Token);

        var failure = await act.Should()
            .ThrowAsync<StatementReconciliationIntakeAuthorityException>();
        failure.Which.Code.Should().Be("STATEMENT_LEDGER_BOOK_AMBIGUOUS");
        harness.Imports.CommitCount.Should().Be(0);
        Directory.Exists(Path.Combine(
                harness.DataRoot,
                "reporting",
                "statement-reconciliation-report"))
            .Should().BeFalse("authority resolution must precede input retention");
        (await harness.Queue.GetAllAsync(ct: timeout.Token)).Should().BeEmpty();
        (await harness.Operations.ListAsync(ct: timeout.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_AmbiguousAccountingPeriodAuthority_FailsClosedBeforeStatementRetention()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var secondPeriodId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var harness = CreateHarness(
            [Book(LedgerBookId)],
            [
                Period(AccountingPeriodId, LedgerBookId, LedgerPeriodStatusDto.Open),
                Period(secondPeriodId, LedgerBookId, LedgerPeriodStatusDto.Open)
            ]);

        var act = () => harness.Workflow.StartAsync(Command(), timeout.Token);

        var failure = await act.Should()
            .ThrowAsync<StatementReconciliationIntakeAuthorityException>();
        failure.Which.Code.Should().Be("STATEMENT_ACCOUNTING_PERIOD_AMBIGUOUS");
        harness.Imports.CommitCount.Should().Be(0);
        Directory.Exists(Path.Combine(
                harness.DataRoot,
                "reporting",
                "statement-reconciliation-report"))
            .Should().BeFalse("ambiguous periods cannot create retained statement evidence");
        (await harness.Queue.GetAllAsync(ct: timeout.Token)).Should().BeEmpty();
        (await harness.Operations.ListAsync(ct: timeout.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_ClosedAccountingPeriodAuthority_FailsClosedBeforeStatementRetention()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var harness = CreateHarness(
            [Book(LedgerBookId)],
            [Period(AccountingPeriodId, LedgerBookId, LedgerPeriodStatusDto.SoftClosed)]);

        var act = () => harness.Workflow.StartAsync(Command(), timeout.Token);

        var failure = await act.Should()
            .ThrowAsync<StatementReconciliationIntakeAuthorityException>();
        failure.Which.Code.Should().Be("STATEMENT_ACCOUNTING_PERIOD_CLOSED");
        harness.Imports.CommitCount.Should().Be(0);
        Directory.Exists(Path.Combine(
                harness.DataRoot,
                "reporting",
                "statement-reconciliation-report"))
            .Should().BeFalse("a non-Open period cannot admit a new authoritative statement");
        (await harness.Queue.GetAllAsync(ct: timeout.Token)).Should().BeEmpty();
        (await harness.Operations.ListAsync(ct: timeout.Token)).Should().BeEmpty();
    }

    private IntakeHarness CreateHarness(
        IReadOnlyList<LedgerBookDto> books,
        IReadOnlyList<LedgerPeriodDto> periods)
    {
        var dataRoot = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var accounts = new Mock<IAccountQueryService>(MockBehavior.Strict);
        accounts
            .Setup(service => service.GetAccountAsync(
                FundAccountId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Account());
        var tenancy = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        tenancy
            .Setup(registry => registry.ResolveAsync(
                FundProfileId.ToString("D"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(
                FundProfileId.ToString("D"),
                TenantId,
                CompanyId));
        var ledgerBooks = new Mock<ILedgerBookService>(MockBehavior.Strict);
        ledgerBooks
            .Setup(service => service.ListBooksAsync(
                It.IsAny<LedgerBookQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);
        ledgerBooks
            .Setup(service => service.ListPeriodsAsync(
                It.IsAny<LedgerPeriodQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(periods);

        var importResult = ImportResult();
        var statementRun = StatementRun();
        var statementRuns = new Mock<IStatementRunWorkflowService>(MockBehavior.Strict);
        statementRuns
            .Setup(service => service.GetAsync(
                StatementRunId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(statementRun);
        var reconciliation = new Mock<IReconciliationApiService>(MockBehavior.Strict);
        reconciliation
            .Setup(service => service.ListOpenStatementBreaksAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([StatementBreak()]);

        var derivation = new OperationsStatusDerivationService();
        var operations = new OperationsContinuityWorkflowService(
            new InMemoryOperationsContinuityRepository(derivation),
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var queue = new FileReconciliationBreakQueueRepository(
            Path.Combine(dataRoot, "reconciliation-casework"),
            NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var authority = new StatementReconciliationIntakeAuthority(
            accounts.Object,
            tenancy.Object,
            ledgerBooks.Object,
            operations,
            statementRuns.Object,
            reconciliation.Object,
            queue);
        var imports = new RecordingStatementImportService(importResult);
        var workflow = new StatementReconciliationReportWorkflowService(
            imports,
            new RetainingStatementEvidenceService(),
            statementRuns.Object,
            dataRoot,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            queue,
            authority);
        return new IntakeHarness(
            dataRoot,
            workflow,
            imports,
            queue,
            operations,
            accounts,
            tenancy,
            ledgerBooks)
        {
            Authority = authority
        };
    }

    private static StatementReconciliationReportStartCommand Command()
        => new(
            new StatementImportCommitRequest(
                new StatementSourceDocument(
                    "northstar-june-2026.csv",
                    "account,currency,amount,bookAmount\nCUSTODY-7842,USD,125000,124500"u8
                        .ToArray()),
                ConnectorId: "csv",
                SourceKind: "custodian",
                SourceInstitution,
                FundAccountId.ToString("D"),
                ExternalAccountId,
                PeriodStart,
                PeriodEnd,
                ToleranceProfileId: "month-end-cash",
                ImportedBy: "statement-operations"),
            TenantId,
            CompanyId);

    private static AccountSummaryDto Account()
        => new(
            FundAccountId,
            AccountTypeDto.Custody,
            EntityId: null,
            FundId: FundProfileId,
            SleeveId: null,
            VehicleId: null,
            AccountCode: ExternalAccountId,
            DisplayName: "Northstar operating custody",
            BaseCurrency: "USD",
            Institution: SourceInstitution,
            IsActive: true,
            EffectiveFrom: ObservedAt.AddYears(-1),
            EffectiveTo: null,
            PortfolioId: null,
            LedgerReference: null,
            StrategyId: null,
            RunId: null);

    private static LedgerBookDto Book(Guid ledgerBookId)
        => new(
            ledgerBookId,
            FundProfileId.ToString("D"),
            FundProfileId,
            FundStructureNodeKindDto.Fund,
            "Primary reporting book",
            "USD",
            ObservedAt.AddYears(-1),
            ObservedAt.AddDays(-1),
            AccountingBasis: AccountingBasisKindDto.Primary);

    private static LedgerPeriodDto Period(
        Guid periodId,
        Guid ledgerBookId,
        LedgerPeriodStatusDto status)
        => new(
            periodId,
            ledgerBookId,
            FiscalYear: 2026,
            PeriodNo: 6,
            Label: "2026-06",
            StartDate: PeriodStart,
            EndDate: PeriodEnd,
            Status: status,
            OpenedAt: ObservedAt.AddMonths(-1),
            ClosedAt: status == LedgerPeriodStatusDto.Open ? null : ObservedAt,
            Version: 1);

    private static StatementImportCommitResultDto ImportResult()
        => new(
            StatementRunId,
            Duplicate: false,
            RecordCount: 1,
            KindSummaries: [new StatementKindSummaryDto("Cash", 1, [])],
            BreakCount: 1,
            CaseCount: 1,
            RetainedSourcePath: "reconciliation/statements/northstar-june-2026.csv",
            RetainedCanonicalPath: "reconciliation/statements/northstar-june-2026.canonical.json",
            Status: "Imported",
            NextAction: "Review the retained cash break.")
        {
            BreakIds = [SourceBreakId],
            CaseIds = ["statement-case-cash-june-2026"],
            ReconciliationCaseLinks =
            [
                new StatementImportReconciliationCaseLinkDto(
                    "statement-case-cash-june-2026",
                    SourceBreakId,
                    "/accounting/reconciliation/statements/cases/statement-case-cash-june-2026",
                    "June cash variance",
                    "Open",
                    "High",
                    "Custodian cash exceeds the book.",
                    "Review retained statement evidence.")
            ]
        };

    private static StatementRunWorkflowResult StatementRun()
    {
        var import = new CanonicalStatementImport(
            StatementRunId,
            SourceInstitution,
            PeriodEnd,
            ObservedAt,
            "reconciliation/statements/northstar-june-2026.csv",
            new string('a', 64),
            RawRowCount: 1,
            NormalizedRowCount: 1)
        {
            SourceInstitution = SourceInstitution,
            FundAccountId = FundAccountId.ToString("D"),
            ExternalAccountId = ExternalAccountId,
            StatementPeriodStart = PeriodStart,
            StatementPeriodEnd = PeriodEnd,
            OriginalFileName = "northstar-june-2026.csv",
            ImportedBy = "statement-operations",
            AccountingScope = new StatementAccountingScope(
                FundProfileId.ToString("D"),
                LedgerBookId,
                AccountingPeriodId,
                PeriodEnd)
        };
        var statementBreak = new ReconciliationBreakRecord(
            SourceBreakId,
            StatementRunId,
            StatementRunId,
            $"{StatementRunId}:row-1",
            "CASH_BALANCE_MISMATCH",
            "cash",
            Delta: 500m,
            Tolerance: 100m,
            ToleranceBreached: true,
            CreatedAtUtc: ObservedAt,
            Status: "Open");
        var statementCase = new ReconciliationCase(
            "statement-case-cash-june-2026",
            StatementRunId,
            "Open",
            "Custodian cash exceeds the retained book balance.",
            Confidence: 0.5m,
            Rationale: "Review the retained custodian statement.",
            CreatedAtUtc: ObservedAt,
            History: []);
        return new StatementRunWorkflowResult(import, [statementBreak], [statementCase]);
    }

    private static StatementBreakDto StatementBreak()
        => new(
            SourceBreakId,
            StatementBreakType.CashBalanceMismatch,
            StatementValidationSeverity.Error,
            MatchTier: null,
            StatementReference: $"{StatementRunId}:row-1",
            Description: "Custodian cash exceeds the retained book balance.",
            StatementAmount: 125_000m,
            BookAmount: 124_500m,
            Delta: 500m,
            Tolerance: 100m,
            Currency: "USD",
            CreatedAtUtc: ObservedAt,
            Status: "Open",
            InternalReference: StatementRunId,
            Owner: "fund-operations",
            EvidenceLink:
            $"/api/workstation/reconciliation/statement-reconciliation-report/{StatementRunId}#cash-row-1");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record IntakeHarness(
        string DataRoot,
        StatementReconciliationReportWorkflowService Workflow,
        RecordingStatementImportService Imports,
        FileReconciliationBreakQueueRepository Queue,
        OperationsContinuityWorkflowService Operations,
        Mock<IAccountQueryService> Accounts,
        Mock<IFundProfileTenancyRegistry> Tenancy,
        Mock<ILedgerBookService> LedgerBooks)
    {
        public IStatementReconciliationIntakeAuthority Authority { get; init; } = null!;
    }

    private sealed class RecordingStatementImportService(StatementImportCommitResultDto result)
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
            => Task.FromResult(new StatementImportValidationResult(true, 1, []));
    }

    private sealed class RetainingStatementEvidenceService : IStatementImportEvidenceRetainer
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
                    "statement-vault-june-2026",
                    "statement-run",
                    result.RunId,
                    "evidence/statement-vault-june-2026/manifest.json",
                    "/api/workstation/evidence/statement-vault-june-2026",
                    ObservedAt,
                    new string('b', 64),
                    SchemaVersion: 1,
                    StorageKind: "File")
            });
        }
    }
}
