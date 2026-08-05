using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class OperatorReadinessConsoleViewModelTests
{
    private static readonly string[] RegisteredTags =
    [
        "HomeWorkspace", "TradingShell", "PortfolioShell", "AccountingShell", "ReportingShell",
        "StrategyShell", "DataShell", "SettingsShell", "StrategyRuns", "SecurityMaster",
        "FundReconciliation", "FundReportPack", "FundAccountingClose"
    ];

    private static bool IsRegistered(string tag)
        => RegisteredTags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task RefreshAsync_ProjectsGatesPanelsFactsAndWorkItems()
    {
        var readiness = CreateReadiness();
        var viewModel = CreateViewModel(
            new FakeReadinessProvider { Readiness = readiness },
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = CreateBreaks() },
            runWorkspaceService: null);
        using var scope = viewModel;

        await viewModel.RefreshAsync();

        viewModel.OverallStatusText.Should().Be("Review required");
        viewModel.OverallTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
        viewModel.AsOfText.Should().Be("2026-08-05 06:00 UTC");
        viewModel.GateRows.Should().HaveCount(3);
        viewModel.GateRows[0].Label.Should().Be("Overall readiness");
        viewModel.GateRows[1].StatusText.Should().Be("Ready");
        viewModel.GateRows[2].StatusText.Should().Be("Blocked");
        viewModel.SessionRows.Should().HaveCount(3, "active session, paper equity, and replay coverage rows");
        viewModel.SessionRows[0].Value.Should().Be("paper-42");
        viewModel.TrustRows.Should().HaveCount(2, "trust gate and brokerage sync rows");
        viewModel.TrustRows[0].ReadinessTone.Should().Be(WorkstationReadinessTone.EvidenceLinked, "signoff is signed and no blockers exist");
        viewModel.PromotionRows.Should().ContainSingle()
            .Which.ReadinessTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
        viewModel.BreakRows.Should().HaveCount(2, "resolved break-queue items are excluded");
        viewModel.WorkItemRows.Should().HaveCount(3, "inbox and readiness feeds merge with duplicate ids deduplicated");
        viewModel.WorkItemRows[0].WorkItemId.Should().Be("wi-critical", "highest priority score sorts first");
        viewModel.WorkItemRows[0].TargetPageTag.Should().Be("FundReconciliation");
        viewModel.Warnings.Should().Equal("One readiness warning was retained.");
        viewModel.SummaryFacts.Should().HaveCount(6);
        viewModel.SummaryFacts[1].Value.Should().Be("1/2 ready");
        viewModel.HasRunsError.Should().BeTrue("no run workspace service is present in this composition");
        viewModel.HasReadinessError.Should().BeFalse();
        viewModel.HasInboxError.Should().BeFalse();
        viewModel.HasBreaksError.Should().BeFalse();
    }

    [Theory]
    [InlineData("FundReconciliation", OperatorWorkItemKindDto.ReportPackApproval, null, "FundReconciliation")]
    [InlineData("NotARegisteredTag", OperatorWorkItemKindDto.ReportPackApproval, null, "FundReportPack")]
    [InlineData(null, OperatorWorkItemKindDto.LedgerPeriodClose, null, "FundAccountingClose")]
    [InlineData(null, (OperatorWorkItemKindDto)999, "reporting", "ReportingShell")]
    [InlineData(null, (OperatorWorkItemKindDto)999, "unknown-lane", "HomeWorkspace")]
    public void ResolveWorkItemPageTag_PrefersRegisteredTargetThenKindThenWorkspace(
        string? targetPageTag,
        OperatorWorkItemKindDto kind,
        string? workspace,
        string expectedTag)
    {
        var item = new OperatorWorkItemDto(
            WorkItemId: "wi-1",
            Kind: kind,
            Label: "Item",
            Detail: "Detail",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
            Workspace: workspace,
            TargetPageTag: targetPageTag);

        OperatorReadinessConsoleMapper.ResolveWorkItemPageTag(item, IsRegistered).Should().Be(expectedTag);
    }

    [Fact]
    public async Task RefreshAsync_MissingSources_DegradePerPanelWithoutThrowing()
    {
        using var viewModel = new OperatorReadinessConsoleViewModel(isRegisteredPageTag: IsRegistered);

        await viewModel.RefreshAsync();

        viewModel.HasReadinessError.Should().BeTrue();
        viewModel.HasInboxError.Should().BeTrue();
        viewModel.HasBreaksError.Should().BeTrue();
        viewModel.HasRunsError.Should().BeTrue();
        viewModel.OverallStatusText.Should().Be("Unavailable");
        viewModel.GateRows.Should().BeEmpty();
        viewModel.WorkItemRows.Should().BeEmpty();
        viewModel.SummaryFacts.Should().HaveCount(6, "the summary strip stays populated with unavailability facts");
        viewModel.SummaryFacts[0].Value.Should().Be("Unavailable");
    }

    [Fact]
    public async Task RefreshAsync_ReadinessProviderThrow_SurfacesReadinessErrorOnly()
    {
        var viewModel = CreateViewModel(
            new FakeReadinessProvider { Exception = new InvalidOperationException("Readiness projection failed.") },
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = CreateBreaks() },
            runWorkspaceService: null);
        using var scope = viewModel;

        await viewModel.RefreshAsync();

        viewModel.ReadinessErrorText.Should().Be("Readiness projection failed.");
        viewModel.OverallStatusText.Should().Be("Unavailable");
        viewModel.HasInboxError.Should().BeFalse("the inbox loaded independently of the readiness failure");
        viewModel.WorkItemRows.Should().NotBeEmpty("inbox work items still project when readiness fails");
        viewModel.BreakRows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_NewerRefreshSupersedesInFlightOne()
    {
        var provider = new FakeReadinessProvider { Readiness = CreateReadiness() };
        var gate = new TaskCompletionSource<TradingOperatorReadinessDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        provider.PendingReadiness = gate.Task;
        var viewModel = CreateViewModel(
            provider,
            new FakeInboxClient { Inbox = CreateInbox() },
            new FakeReconciliationClient { Breaks = CreateBreaks() },
            runWorkspaceService: null);
        using var scope = viewModel;

        var firstRefresh = viewModel.RefreshAsync();
        viewModel.IsRefreshing.Should().BeTrue("the first readiness load is still pending");

        provider.PendingReadiness = null;
        provider.Readiness = CreateReadiness() with { Warnings = ["Second-round warning."] };
        var secondRefresh = viewModel.RefreshAsync();
        await secondRefresh;
        gate.SetResult(CreateReadiness());
        await firstRefresh;

        viewModel.Warnings.Should().Equal("Second-round warning.",
            "the superseded first refresh must not overwrite the newer projection");
        viewModel.IsRefreshing.Should().BeFalse();
    }

    private static OperatorReadinessConsoleViewModel CreateViewModel(
        ITradingOperatorReadinessProvider? readinessProvider,
        IWorkstationOperatorInboxApiClient? inboxClient,
        IWorkstationReconciliationApiClient? reconciliationClient,
        StrategyRunWorkspaceService? runWorkspaceService)
        => new(readinessProvider, inboxClient, reconciliationClient, runWorkspaceService, IsRegistered);

    private static TradingOperatorReadinessDto CreateReadiness()
        => new(
            AsOf: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            ActiveSession: new TradingPaperSessionReadinessDto(
                SessionId: "paper-42",
                StrategyId: "strategy-1",
                StrategyName: "Covered Call Income",
                IsActive: true,
                InitialCash: 100_000m,
                CreatedAt: DateTimeOffset.Parse("2026-08-04T12:00:00Z"),
                ClosedAt: null,
                SymbolCount: 3,
                OrderCount: 12,
                PositionCount: 4,
                PortfolioValue: 101_250m),
            Sessions: [],
            Replay: new TradingReplayReadinessDto(
                SessionId: "paper-42",
                ReplaySource: "wal",
                IsConsistent: true,
                ComparedFillCount: 8,
                ComparedOrderCount: 12,
                ComparedLedgerEntryCount: 20,
                VerifiedAt: DateTimeOffset.Parse("2026-08-05T05:30:00Z"),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-77",
                MismatchReasons: [],
                DriftStatus: "None",
                RequiredNextAction: "None"),
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 2,
                DefaultMaxPositionSize: 100m),
            Promotion: new TradingPromotionReadinessDto(
                State: "AwaitingReview",
                Reason: "Backtest completed with review-required promotion gate.",
                RequiresReview: true,
                SourceRunId: "run-9",
                TargetRunId: null,
                SuggestedNextMode: "Paper",
                AuditReference: null,
                ApprovalStatus: "Pending",
                ManualOverrideId: null,
                ApprovedBy: null),
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "Ready",
                ReadyForOperatorReview: true,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "Signed",
                GeneratedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
                PacketPath: null,
                SourceSummary: null,
                RequiredSampleCount: 4,
                ReadySampleCount: 4,
                ValidatedEvidenceDocumentCount: 2,
                RequiredOwners: [],
                Blockers: [],
                Detail: "Trust gate evidence is complete."),
            BrokerageSync: new WorkstationBrokerageSyncStatusDto(
                FundAccountId: Guid.Parse("0e2a1c94-3f5b-4f6c-9a51-1c2d3e4f5a6b"),
                ProviderId: "alpaca",
                ExternalAccountId: "acct-1",
                Health: WorkstationBrokerageSyncHealth.Healthy,
                IsLinked: true,
                IsStale: false,
                LastAttemptedSyncAt: null,
                LastSuccessfulSyncAt: null,
                LastError: null,
                PositionCount: 4,
                OpenOrderCount: 1,
                FillCount: 8,
                CashTransactionCount: 2,
                SecurityMissingCount: 0,
                Warnings: []),
            WorkItems:
            [
                new OperatorWorkItemDto(
                    WorkItemId: "wi-readiness",
                    Kind: OperatorWorkItemKindDto.PromotionReview,
                    Label: "Review promotion",
                    Detail: "Promotion gate requires review.",
                    Tone: OperatorWorkItemToneDto.Warning,
                    CreatedAt: DateTimeOffset.Parse("2026-08-05T04:00:00Z"))
                {
                    PriorityScore = 40
                }
            ],
            Warnings: ["One readiness warning was retained."])
        {
            OverallStatus = TradingAcceptanceGateStatusDto.ReviewRequired,
            ReadyForPaperOperation = true,
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto("gate-replay", "Replay parity", TradingAcceptanceGateStatusDto.Ready, "Replay matches persisted state."),
                new TradingAcceptanceGateDto("gate-signoff", "Operator sign-off", TradingAcceptanceGateStatusDto.Blocked, "Sign-off is missing.", RequiredNextAction: "Collect the operator sign-off.")
            ]
        };

    private static OperatorInboxDto CreateInbox()
        => new(
            AsOf: DateTimeOffset.Parse("2026-08-05T06:00:00Z"),
            Items:
            [
                new OperatorWorkItemDto(
                    WorkItemId: "wi-critical",
                    Kind: OperatorWorkItemKindDto.ReconciliationBreak,
                    Label: "Resolve reconciliation break",
                    Detail: "Cash variance breached tolerance.",
                    Tone: OperatorWorkItemToneDto.Critical,
                    CreatedAt: DateTimeOffset.Parse("2026-08-05T05:45:00Z"),
                    Workspace: "Accounting",
                    TargetPageTag: "FundReconciliation")
                {
                    PriorityScore = 90
                },
                new OperatorWorkItemDto(
                    WorkItemId: "wi-report",
                    Kind: OperatorWorkItemKindDto.ReportPackApproval,
                    Label: "Approve report pack",
                    Detail: "Report pack awaits approval.",
                    Tone: OperatorWorkItemToneDto.Warning,
                    CreatedAt: DateTimeOffset.Parse("2026-08-05T05:30:00Z"),
                    Workspace: "Reporting",
                    TargetPageTag: "NotARegisteredTag")
                {
                    PriorityScore = 60
                }
            ],
            CriticalCount: 1,
            WarningCount: 1,
            ReviewCount: 0,
            Summary: "Two operator work items need attention.");

    private static IReadOnlyList<ReconciliationBreakQueueItem> CreateBreaks()
        =>
        [
            new ReconciliationBreakQueueItem(
                BreakId: "brk-1",
                RunId: "recon-run-1",
                StrategyName: "Covered Call Income",
                Category: ReconciliationBreakCategory.CashMismatch,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 125.50m,
                Reason: "Cash balance variance.",
                AssignedTo: null,
                DetectedAt: DateTimeOffset.Parse("2026-08-05T05:00:00Z"),
                LastUpdatedAt: DateTimeOffset.Parse("2026-08-05T05:10:00Z")),
            new ReconciliationBreakQueueItem(
                BreakId: "brk-2",
                RunId: "recon-run-1",
                StrategyName: "Covered Call Income",
                Category: ReconciliationBreakCategory.AmountMismatch,
                Status: ReconciliationBreakQueueStatus.InReview,
                Variance: 3m,
                Reason: "Position quantity variance.",
                AssignedTo: "ops",
                DetectedAt: DateTimeOffset.Parse("2026-08-05T04:30:00Z"),
                LastUpdatedAt: DateTimeOffset.Parse("2026-08-05T04:40:00Z")),
            new ReconciliationBreakQueueItem(
                BreakId: "brk-3",
                RunId: "recon-run-0",
                StrategyName: "Covered Call Income",
                Category: ReconciliationBreakCategory.CashMismatch,
                Status: ReconciliationBreakQueueStatus.Resolved,
                Variance: 0m,
                Reason: "Resolved variance.",
                AssignedTo: "ops",
                DetectedAt: DateTimeOffset.Parse("2026-08-04T04:30:00Z"),
                LastUpdatedAt: DateTimeOffset.Parse("2026-08-04T05:40:00Z"))
        ];

    private sealed class FakeReadinessProvider : ITradingOperatorReadinessProvider
    {
        public TradingOperatorReadinessDto Readiness { get; set; } = CreateReadiness();

        public Task<TradingOperatorReadinessDto>? PendingReadiness { get; set; }

        public Exception? Exception { get; set; }

        public Task<TradingOperatorReadinessDto> GetAsync(Guid? fundAccountId = null, CancellationToken ct = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return PendingReadiness ?? Task.FromResult(Readiness);
        }
    }

    private sealed class FakeInboxClient : IWorkstationOperatorInboxApiClient
    {
        public OperatorInboxDto? Inbox { get; set; }

        public Task<OperatorInboxDto?> GetInboxAsync(Guid? fundAccountId = null, CancellationToken ct = default)
            => Task.FromResult(Inbox);
    }

    private sealed class FakeReconciliationClient : IWorkstationReconciliationApiClient
    {
        public IReadOnlyList<ReconciliationBreakQueueItem> Breaks { get; set; } = [];

        public Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueAsync(CancellationToken ct = default)
            => Task.FromResult(Breaks);

        public Task<ReconciliationCalibrationSummaryDto?> GetCalibrationSummaryAsync(CancellationToken ct = default)
            => Task.FromResult<ReconciliationCalibrationSummaryDto?>(null);

        public Task<IReadOnlyList<StatementRunSummaryDto>> GetStatementRunsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementRunSummaryDto>>([]);

        public Task<StatementRunSummaryDto?> GetStatementRunAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<StatementRunSummaryDto?>(null);

        public Task<IReadOnlyList<StatementRunExceptionDto>> GetStatementExceptionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementRunExceptionDto>>([]);

        public Task<IReadOnlyList<StatementBreakDto>> GetOpenStatementBreaksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StatementBreakDto>>([]);

        public Task<IReadOnlyList<ReconciliationCaseSummaryDto>> GetOpenReconciliationCasesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCaseSummaryDto>>([]);

        public Task<IReadOnlyList<ReconciliationQueueAccountStatusDto>> GetReconciliationQueueStatusAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationQueueAccountStatusDto>>([]);

        public Task<ReconciliationRunDetail?> GetLatestRunDetailAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetRunDetailAsync(string reconciliationRunId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<WorkstationReconciliationActionResult> ReviewBreakAsync(
            string breakId,
            ReviewReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("The readiness console never mutates break state.");

        public Task<WorkstationReconciliationActionResult> ResolveBreakAsync(
            string breakId,
            ResolveReconciliationBreakRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("The readiness console never mutates break state.");
    }
}
