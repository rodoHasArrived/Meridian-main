using FluentAssertions;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class OperationsContinuityReconciliationBridgeTests
{
    private static readonly Guid FundAccountId =
        Guid.Parse("d6f39f86-652c-4a09-a0fc-f56f026a4c3e");
    private static readonly string FundProfileId =
        Guid.Parse("51086ec9-304f-4643-bc17-e2533e4e5c48").ToString("D");
    private static readonly Guid LedgerBookId =
        Guid.Parse("97f69c70-2f6e-4567-b54f-94e7fed2528a");
    private static readonly Guid AccountingPeriodId =
        Guid.Parse("9218037e-4c89-4641-a7aa-c39fc420c55b");
    private static readonly DateOnly AccountingAsOf = new(2026, 5, 31);
    private static readonly ReconciliationBreakQueueScope AlphaScope =
        new("tenant-alpha", "company-alpha");
    private static readonly ReconciliationBreakQueueScope BetaScope =
        new("tenant-beta", "company-beta");

    [Fact]
    public async Task RunReconciliationAsync_ShouldProjectSecurityMasterIssuesAsWorkflowBreakCases()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var workflowService = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);

        var reconciliationDetail = CreateReconciliationDetail();
        var breakQueueItem = CreateBreakQueueItem(reconciliationDetail, AlphaScope, "fund-controller");
        var foreignBreakQueueItem = CreateBreakQueueItem(reconciliationDetail, BetaScope, "foreign-controller");
        var reconciliationService = new StaticReconciliationRunService(reconciliationDetail);
        var breakQueueRepository = new StaticReconciliationBreakQueueRepository(
            [foreignBreakQueueItem, breakQueueItem]);
        await RetainStatementBindingAsync(
            auditStore,
            workflow,
            reconciliationDetail.Summary.RunId);
        var statementAuthority = StatementAuthority(
            workflow,
            reconciliationDetail.Summary.RunId,
            AlphaScope);
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            reconciliationService,
            breakQueueRepository,
            statementAuthority.Object);

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                SourceRunId: reconciliationDetail.Summary.RunId),
            AlphaScope);

        result.Success.Should().BeTrue();
        reconciliationService.RunCallCount.Should().Be(0);
        reconciliationService.LatestReadCallCount.Should().Be(1);
        breakQueueRepository.ScopedReadCount.Should().Be(1);
        breakQueueRepository.UnscopedReadCount.Should().Be(0);
        breakQueueRepository.MutationCount.Should().Be(0);
        result.Workflow.Should().NotBeNull();
        result.Workflow!.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "SM_RECON_SECURITY_UNRESOLVED" &&
            breakCase.Category == "SecurityMasterCoverage" &&
            breakCase.Symbol == "MBS1" &&
            breakCase.EvidenceLinks.Any(link => link.Route == "/workstation/data/security-master") &&
            breakCase.RootCauseCode == "SecurityMasterCoverage" &&
            breakCase.ApprovalState == "ApprovalRequired" &&
            breakCase.BlockedOutputs != null &&
            breakCase.BlockedOutputs.Contains("Period close"));
        result.Workflow.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "FACTOR_PAYDOWN_AMOUNT_MISMATCH" &&
            breakCase.Category == "SecurityMasterAccounting" &&
            breakCase.ExpectedAmount == 3_000m &&
            breakCase.ActualAmount == 2_900m &&
            breakCase.Variance == -100m &&
            breakCase.Owner == "fund-controller" &&
            breakCase.SlaState == ReconciliationCaseSlaState.OnTrack.ToString() &&
            breakCase.SlaDueAtUtc == breakQueueItem.SlaDueAt &&
            breakCase.Materiality == 100m &&
            breakCase.RootCauseCode == "SecurityMasterAccounting" &&
            breakCase.ApprovalState == "awaiting-approval" &&
            breakCase.BlockedOutputs != null &&
            breakCase.BlockedOutputs.Contains("Investor statements"));
        result.Workflow.ReconciliationLanes.Should().HaveCount(7);
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "mbs-factor-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.ReviewRequired &&
            lane.BreakCount == 1 &&
            lane.EvidenceLinks.Any(link => link.Route == "/workstation/accounting/reconciliation"));
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "position-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.ReviewRequired &&
            lane.BreakCount == 1);
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "bank-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.Ready &&
            lane.Summary.Contains("normalized bank transaction", StringComparison.OrdinalIgnoreCase));
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "income-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.Ready &&
            lane.Summary.Contains("expected accounting event", StringComparison.OrdinalIgnoreCase));
        result.Workflow.ReconciliationLanes.Should().Contain(lane =>
            lane.LaneId == "gl-reconciliation" &&
            lane.Status == OperationsReconciliationLaneStatusDto.Ready &&
            lane.Summary.Contains("expected journal preview", StringComparison.OrdinalIgnoreCase));
        result.Workflow.Gates.Single(gate => gate.GateKey == OperationsGateKeyDto.SecurityMaster)
            .Status.Should().Be(OperationsGateStatusDto.Blocked);
    }

    [Fact]
    public async Task RunReconciliationAsync_WithForeignScope_ShouldRejectBeforeReadingForeignDetail()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var workflowService = new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var detail = CreateReconciliationDetail();
        var breakQueueRepository = new StaticReconciliationBreakQueueRepository(
            [CreateBreakQueueItem(detail, AlphaScope, "alpha-controller")]);
        var reconciliationService = new StaticReconciliationRunService(detail);
        var statementAuthority = StatementAuthority(workflow, detail.Summary.RunId, AlphaScope);
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            reconciliationService,
            breakQueueRepository,
            statementAuthority.Object);

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                SourceRunId: detail.Summary.RunId),
            BetaScope);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RECONCILIATION_WORKFLOW_NOT_AUTHORIZED");
        result.Workflow.Should().BeNull();
        reconciliationService.TotalReadCount.Should().Be(0);
        breakQueueRepository.ScopedReadCount.Should().Be(0);
        breakQueueRepository.UnscopedReadCount.Should().Be(0);
        breakQueueRepository.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task RunReconciliationAsync_WithSourceRunAndNoScope_ShouldFailClosedWithoutReadsOrMutation()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var workflowService = new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var detail = CreateReconciliationDetail();
        var reconciliationService = new StaticReconciliationRunService(detail);
        var breakQueueRepository = new StaticReconciliationBreakQueueRepository(
            [CreateBreakQueueItem(detail, AlphaScope, "alpha-controller")]);
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            reconciliationService,
            breakQueueRepository);

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                SourceRunId: detail.Summary.RunId));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RECONCILIATION_SCOPE_REQUIRED");
        result.ErrorMessage.Should().Contain("tenant- and company-scoped");
        reconciliationService.TotalReadCount.Should().Be(0);
        reconciliationService.RunCallCount.Should().Be(0);
        breakQueueRepository.ScopedReadCount.Should().Be(0);
        breakQueueRepository.UnscopedReadCount.Should().Be(0);
        breakQueueRepository.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task RunReconciliationAsync_WithOnlyReconciliationRunId_ShouldFailClosedWithoutDetailRead()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var workflowService = new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var detail = CreateReconciliationDetail();
        var reconciliationService = new StaticReconciliationRunService(detail);
        var breakQueueRepository = new StaticReconciliationBreakQueueRepository([]);
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            reconciliationService,
            breakQueueRepository,
            StatementAuthority(workflow, detail.Summary.RunId, AlphaScope).Object);

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                ReconciliationRunId: detail.Summary.ReconciliationRunId),
            AlphaScope);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RECONCILIATION_SOURCE_NOT_AUTHORIZED");
        reconciliationService.TotalReadCount.Should().Be(0);
        breakQueueRepository.ScopedReadCount.Should().Be(0);
    }

    [Fact]
    public async Task RunReconciliationAsync_DirectRequestWithoutExactFundAuthority_ShouldRejectBeforeMutation()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var workflowService = new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var statementAuthority = new Mock<IReconciliationApiService>(MockBehavior.Strict);
        statementAuthority
            .Setup(candidate => candidate.GetAuthorizedFundAccountAsync(
                workflow.FundAccountId,
                AlphaScope,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReconciliationFundAccountAuthorization?)null);
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            statementReconciliation: statementAuthority.Object);

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                BreakCases: []),
            AlphaScope);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RECONCILIATION_WORKFLOW_NOT_AUTHORIZED");
        var retained = await repository.GetAsync(workflow.WorkflowId);
        retained.Should().NotBeNull();
        retained!.ReconciliationState.Should().Be(OperationsReconciliationStateDto.Pending);
        retained.Version.Should().Be(workflow.Version);
    }

    [Fact]
    public async Task RunReconciliationAsync_DirectRequestWithoutCallerScope_ShouldFailClosedBeforeWorkflowRead()
    {
        var workflowService = new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict);
        var bridge = new OperationsContinuityReconciliationBridge(workflowService.Object);

        var result = await bridge.RunReconciliationAsync(
            Guid.NewGuid(),
            new OperationsReconciliationRunRequestDto(
                ExpectedVersion: 7,
                Actor: "ops-user",
                BreakCases: []));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RECONCILIATION_SCOPE_REQUIRED");
        workflowService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunReconciliationAsync_SourceOwnedButNotBoundToTargetWorkflow_ShouldRejectBeforeDetailRead()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var workflowService = new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var detail = CreateReconciliationDetail();
        var reconciliationService = new StaticReconciliationRunService(detail);
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            reconciliationService,
            statementReconciliation: StatementAuthority(
                workflow,
                detail.Summary.RunId,
                AlphaScope).Object);

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                SourceRunId: detail.Summary.RunId),
            AlphaScope);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RECONCILIATION_SOURCE_NOT_AUTHORIZED");
        result.ErrorMessage.Should().Contain("not retained as intake evidence");
        reconciliationService.TotalReadCount.Should().Be(0);
        var retained = await repository.GetAsync(workflow.WorkflowId);
        retained!.ReconciliationState.Should().Be(OperationsReconciliationStateDto.Pending);
    }

    [Fact]
    public async Task RunReconciliationAsync_SourceWithDifferentLedgerScope_ShouldRejectBeforeTimelineOrDetailRead()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var auditStore = new InMemoryOperationsWorkflowAuditStore();
        var workflowService = new OperationsContinuityWorkflowService(repository, auditStore, derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var detail = CreateReconciliationDetail();
        await RetainStatementBindingAsync(auditStore, workflow, detail.Summary.RunId);
        var reconciliationService = new StaticReconciliationRunService(detail);
        var statementAuthority = StatementAuthority(
            workflow,
            detail.Summary.RunId,
            AlphaScope,
            ledgerBookId: Guid.NewGuid());
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            reconciliationService,
            statementReconciliation: statementAuthority.Object);

        var result = await bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                SourceRunId: detail.Summary.RunId),
            AlphaScope);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RECONCILIATION_SOURCE_NOT_AUTHORIZED");
        reconciliationService.TotalReadCount.Should().Be(0);
    }

    [Fact]
    public async Task RunReconciliationAsync_AuthorityCancellation_ShouldPropagateWithoutMutation()
    {
        using var cts = new CancellationTokenSource();
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var workflowService = new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var statementAuthority = new Mock<IReconciliationApiService>(MockBehavior.Strict);
        statementAuthority
            .Setup(candidate => candidate.GetAuthorizedFundAccountAsync(
                workflow.FundAccountId,
                AlphaScope,
                cts.Token))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromCanceled<ReconciliationFundAccountAuthorization?>(cts.Token);
            });
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            statementReconciliation: statementAuthority.Object);

        var act = () => bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                BreakCases: []),
            AlphaScope,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var retained = await repository.GetAsync(workflow.WorkflowId);
        retained!.ReconciliationState.Should().Be(OperationsReconciliationStateDto.Pending);
    }

    [Fact]
    public async Task RunReconciliationAsync_SourceAuthorityFailure_ShouldPropagateWithoutDetailReadOrMutation()
    {
        var derivation = new OperationsStatusDerivationService();
        var repository = new InMemoryOperationsContinuityRepository(derivation);
        var workflowService = new OperationsContinuityWorkflowService(
            repository,
            new InMemoryOperationsWorkflowAuditStore(),
            derivation);
        var workflow = CreateLedgerPostedWorkflow();
        await repository.SaveAsync(workflow);
        var detail = CreateReconciliationDetail();
        var reconciliationService = new StaticReconciliationRunService(detail);
        var statementAuthority = new Mock<IReconciliationApiService>(MockBehavior.Strict);
        statementAuthority
            .Setup(candidate => candidate.GetAuthorizedFundAccountAsync(
                workflow.FundAccountId,
                AlphaScope,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReconciliationFundAccountAuthorization(
                workflow.FundAccountId,
                FundProfileId));
        statementAuthority
            .Setup(candidate => candidate.GetStatementRunAuthorizationAsync(
                detail.Summary.RunId,
                AlphaScope,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Statement authority read failed."));
        var bridge = new OperationsContinuityReconciliationBridge(
            workflowService,
            reconciliationService,
            statementReconciliation: statementAuthority.Object);

        var act = () => bridge.RunReconciliationAsync(
            workflow.WorkflowId,
            new OperationsReconciliationRunRequestDto(
                workflow.Version,
                "ops-user",
                SourceRunId: detail.Summary.RunId),
            AlphaScope);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Statement authority read failed.");
        reconciliationService.TotalReadCount.Should().Be(0);
        var retained = await repository.GetAsync(workflow.WorkflowId);
        retained!.ReconciliationState.Should().Be(OperationsReconciliationStateDto.Pending);
    }

    private static OperationsContinuityWorkflow CreateLedgerPostedWorkflow()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = OperationsContinuityWorkflow.Start(
            Guid.NewGuid(),
            FundAccountId,
            AccountingPeriodId.ToString("D"),
            securityMasterSnapshotId: null,
            brokerSource: "custodian",
            now,
            LedgerBookId);

        workflow.BrokerIntakeState = OperationsBrokerIntakeStateDto.Complete;
        workflow.SecurityMasterState = OperationsSecurityMasterStateDto.Complete;
        workflow.LedgerPostingState = OperationsLedgerPostingStateDto.Complete;
        workflow.ReconciliationState = OperationsReconciliationStateDto.Pending;
        workflow.BrokerIngestGate = workflow.BrokerIngestGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.SecurityMasterGate = workflow.SecurityMasterGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.LedgerPostingGate = workflow.LedgerPostingGate.WithStatus(OperationsGateStatusDto.Passed);
        workflow.Version = 7;
        return workflow;
    }

    private static ReconciliationRunDetail CreateReconciliationDetail()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var summary = new ReconciliationRunSummary(
            ReconciliationRunId: "recon-ops-security-1",
            RunId: "run-ops-security-1",
            CreatedAt: createdAt,
            PortfolioAsOf: createdAt,
            LedgerAsOf: createdAt,
            MatchCount: 0,
            BreakCount: 0,
            OpenBreakCount: 0,
            HasTimingDrift: false,
            AmountTolerance: 0.01m,
            MaxAsOfDriftMinutes: 5,
            SecurityIssueCount: 1,
            HasSecurityCoverageIssues: true,
            BankTransactionCount: 2,
            ExpectedAccountingEventCount: 1,
            ExpectedJournalPreviewCount: 1,
            SecurityMasterAccountingIssueCount: 1,
            HasSecurityMasterAccountingIssues: true);

        return new ReconciliationRunDetail(
            summary,
            Matches: [],
            Breaks: [],
            SecurityCoverageIssues:
            [
                new ReconciliationSecurityCoverageIssueDto(
                    Source: "portfolio",
                    Symbol: "MBS1",
                    AccountName: "Mortgage pool",
                    Reason: "Portfolio position 'MBS1' is missing a Security Master match.",
                    Code: "SM_RECON_SECURITY_UNRESOLVED",
                    Severity: ReconciliationBreakSeverity.High,
                    EvidenceLink: "/workstation/data/security-master")
            ],
            SecurityMasterAccountingIssues:
            [
                new SecurityMasterAccountingIssueDto(
                    Code: "FACTOR_PAYDOWN_AMOUNT_MISMATCH",
                    Source: "actual-activity",
                    Symbol: "MBS1",
                    AccountId: "acct-1",
                    Reason: "Actual principal paydown differs from the Security Master expected amount.",
                    Severity: ReconciliationBreakSeverity.High,
                    EvidenceLink: "/workstation/accounting/reconciliation",
                    ExpectedAmount: 3_000m,
                    ActualAmount: 2_900m)
            ]);
    }

    private static Mock<IReconciliationApiService> StatementAuthority(
        OperationsContinuityWorkflow workflow,
        string runId,
        ReconciliationBreakQueueScope ownerScope,
        Guid? ledgerBookId = null)
    {
        var service = new Mock<IReconciliationApiService>(MockBehavior.Strict);
        service
            .Setup(candidate => candidate.GetAuthorizedFundAccountAsync(
                workflow.FundAccountId,
                It.Is<ReconciliationBreakQueueScope>(scope =>
                    scope.TenantId == ownerScope.TenantId
                    && scope.CompanyId == ownerScope.CompanyId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReconciliationFundAccountAuthorization(
                workflow.FundAccountId,
                FundProfileId));
        service
            .Setup(candidate => candidate.GetAuthorizedFundAccountAsync(
                workflow.FundAccountId,
                It.Is<ReconciliationBreakQueueScope>(scope =>
                    scope.TenantId != ownerScope.TenantId
                    || scope.CompanyId != ownerScope.CompanyId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReconciliationFundAccountAuthorization?)null);
        service
            .Setup(candidate => candidate.GetStatementRunAuthorizationAsync(
                runId,
                It.Is<ReconciliationBreakQueueScope>(scope =>
                    scope.TenantId == ownerScope.TenantId
                    && scope.CompanyId == ownerScope.CompanyId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatementReconciliationRunAuthorization(
                runId,
                workflow.FundAccountId,
                FundProfileId,
                ledgerBookId ?? LedgerBookId,
                AccountingPeriodId,
                AccountingAsOf,
                new DateOnly(2026, 5, 1),
                AccountingAsOf));
        return service;
    }

    private static Task RetainStatementBindingAsync(
        IOperationsWorkflowAuditStore auditStore,
        OperationsContinuityWorkflow workflow,
        string runId) =>
        auditStore.AppendAsync(new OperationsWorkflowAuditDraft(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            "StatementIntakeRetained",
            OperationsWorkflowStatusDto.InProgress,
            OperationsWorkflowStatusDto.InProgress,
            OperationsGateKeyDto.BrokerIngest,
            OperationsGateStatusDto.Passed,
            OperationsGateStatusDto.Passed,
            "statement-intake",
            "Retained the authoritative statement intake.",
            runId,
            [
                new OperationsEvidenceLinkDto(
                    $"statement-intake:{runId}",
                    "Retained statement",
                    $"/api/workstation/reconciliation/statement-reconciliation-report/{runId}",
                    "statement-reconciliation-report",
                    DateTimeOffset.UtcNow)
            ]));

    private static ReconciliationBreakQueueItem CreateBreakQueueItem(
        ReconciliationRunDetail detail,
        ReconciliationBreakQueueScope scope,
        string owner)
    {
        var createdAt = detail.Summary.CreatedAt;
        return new ReconciliationBreakQueueItem(
            BreakId: "recon-ops-security-1:security-accounting:actual-activity:mbs1:factor-paydown-amount-mismatch",
            RunId: detail.Summary.RunId,
            StrategyName: "Operations close",
            Category: ReconciliationBreakCategory.AmountMismatch,
            Status: ReconciliationBreakQueueStatus.InReview,
            Variance: -100m,
            Reason: "Actual principal paydown differs from the Security Master expected amount.",
            AssignedTo: owner,
            DetectedAt: createdAt.AddHours(-2),
            LastUpdatedAt: createdAt,
            Severity: ReconciliationBreakSeverity.High,
            RequiredSignoffRole: "Controller",
            SignoffStatus: "awaiting-approval",
            RoutingTarget: "Investor statements",
            RoutingDetail: "Principal paydown evidence requires controller approval.",
            RecommendedAction: "Approve factor paydown support.",
            Priority: ReconciliationCasePriority.High,
            SlaPolicyId: "default-high-high",
            SlaDueAt: createdAt.AddHours(8),
            SlaWarningAt: createdAt.AddHours(4),
            SlaState: ReconciliationCaseSlaState.OnTrack,
            RootCauseCode: "SecurityMasterAccounting",
            Score: new ReconciliationBreakScore(
                SeverityScore: 55,
                PriorityScore: 65,
                MaterialityComponent: 100m,
                AgeHours: 2,
                CounterpartyCriticalityComponent: 0,
                RecurringPatternComponent: 0,
                IsHighPriority: false,
                SlaDueAt: createdAt.AddHours(8)))
        {
            TenantId = scope.TenantId,
            CompanyId = scope.CompanyId
        };
    }

    private sealed class StaticReconciliationRunService : IReconciliationRunService
    {
        private readonly ReconciliationRunDetail _detail;

        public StaticReconciliationRunService(ReconciliationRunDetail detail)
        {
            _detail = detail;
        }

        public int RunCallCount { get; private set; }

        public int LatestReadCallCount { get; private set; }

        public int ByIdReadCallCount { get; private set; }

        public int TotalReadCount => LatestReadCallCount + ByIdReadCallCount;

        public Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
        {
            RunCallCount++;
            throw new InvalidOperationException("A read path must not execute reconciliation.");
        }

        public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default)
        {
            ByIdReadCallCount++;
            return Task.FromResult<ReconciliationRunDetail?>(_detail);
        }

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
        {
            LatestReadCallCount++;
            return Task.FromResult<ReconciliationRunDetail?>(_detail);
        }

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([_detail.Summary]);
    }

    private sealed class StaticReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
    {
        private readonly IReadOnlyList<ReconciliationBreakQueueItem> _items;

        public StaticReconciliationBreakQueueRepository(IReadOnlyList<ReconciliationBreakQueueItem> items)
        {
            _items = items;
        }

        public int ScopedReadCount { get; private set; }

        public int UnscopedReadCount { get; private set; }

        public int MutationCount { get; private set; }

        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(ReconciliationBreakQueueStatus? status = null, CancellationToken ct = default)
        {
            UnscopedReadCount++;
            var items = status.HasValue
                ? _items.Where(item => item.Status == status.Value).ToArray()
                : _items;
            return Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(items);
        }

        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(
            ReconciliationBreakQueueScope scope,
            ReconciliationBreakQueueStatus? status = null,
            CancellationToken ct = default)
        {
            ScopedReadCount++;
            var items = _items
                .Where(scope.Owns)
                .Where(item => !status.HasValue || item.Status == status.Value)
                .ToArray();
            return Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(items);
        }

        public Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default) =>
            Task.FromResult(_items.FirstOrDefault(item => string.Equals(item.BreakId, breakId, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
        {
            MutationCount++;
            throw new NotSupportedException();
        }

        public Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
        {
            MutationCount++;
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
        {
            MutationCount++;
            throw new NotSupportedException();
        }

        public Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(ReviewReconciliationBreakRequest request, CancellationToken ct = default)
        {
            MutationCount++;
            throw new NotSupportedException();
        }

        public Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default)
        {
            MutationCount++;
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationBreakQueueAuditEvent>>([]);
    }
}
