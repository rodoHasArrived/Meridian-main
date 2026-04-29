using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class TradingWorkspaceShellViewModelTests
{
    [Fact]
    public void BuildDeskHeroState_WithoutOperatingContext_UsesContextRequiredAction()
    {
        var hero = TradingWorkspaceShellPresentationService.BuildDeskHeroState(
            activeRun: null,
            workflow: null,
            readiness: null,
            hasOperatingContext: false,
            operatingContextDisplayName: null);

        hero.FocusLabel.Should().Be("Context handoff");
        hero.Summary.Should().Contain("waiting for an operating context");
        hero.PrimaryActionId.Should().Be("SwitchContext");
        hero.PrimaryActionLabel.Should().Be("Switch Context");
        hero.TargetLabel.Should().Be("Target page: Context selector");
    }

    [Fact]
    public void BuildDeskHeroState_WithReplayMismatch_RoutesToAuditTrail()
    {
        var readiness = CreateReadiness(
            replay: new TradingReplayReadinessDto(
                SessionId: "paper-mismatch",
                ReplaySource: "local",
                IsConsistent: false,
                ComparedFillCount: 14,
                ComparedOrderCount: 11,
                ComparedLedgerEntryCount: 9,
                VerifiedAt: new DateTimeOffset(2026, 4, 25, 15, 45, 0, TimeSpan.Zero),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-mismatch",
                MismatchReasons: ["Fill sequence mismatch detected for paper replay."]),
            overallStatus: TradingAcceptanceGateStatusDto.Blocked,
            readyForPaperOperation: false);

        var hero = TradingWorkspaceShellPresentationService.BuildDeskHeroState(
            activeRun: CreateActiveRun("paper-run", "Atlas Intraday", "Paper"),
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Atlas Paper Desk");

        hero.FocusLabel.Should().Be("Replay");
        hero.PrimaryActionId.Should().Be("FundAuditTrail");
        hero.PrimaryActionLabel.Should().Be("Audit Trail");
        hero.SecondaryActionId.Should().Be("NotificationCenter");
    }

    [Fact]
    public void BuildDeskHeroState_WithUnexplainedControlEvidence_RoutesToAuditTrail()
    {
        var controls = CreateControls() with
        {
            UnexplainedEvidenceCount = 1,
            ExplainabilityWarnings =
            [
                "OrderRejected audit audit-risk-missing-context is missing actor, scope, reason."
            ],
            RecentEvidence =
            [
                new TradingControlEvidenceDto(
                    AuditId: "audit-risk-missing-context",
                    Category: "Order",
                    Action: "OrderRejected",
                    Outcome: "Rejected",
                    OccurredAt: new DateTimeOffset(2026, 4, 26, 18, 0, 0, TimeSpan.Zero),
                    Actor: null,
                    Scope: "unscoped",
                    Reason: "No rationale was recorded.",
                    IsExplained: false,
                    MissingFields: ["actor", "scope", "reason"])
            ]
        };
        var readiness = CreateReadiness(
            controls: controls,
            overallStatus: TradingAcceptanceGateStatusDto.ReviewRequired,
            readyForPaperOperation: false);

        var hero = TradingWorkspaceShellPresentationService.BuildDeskHeroState(
            activeRun: null,
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Wave 2 Paper Desk");

        hero.FocusLabel.Should().Be("Controls");
        hero.Summary.Should().Contain("missing actor, scope, reason");
        hero.PrimaryActionId.Should().Be("FundAuditTrail");
        hero.SecondaryActionId.Should().Be("RunRisk");
    }

    [Fact]
    public void BuildDeskHeroState_WithDk1SignoffPending_DoesNotShowReadyState()
    {
        var readiness = CreateReadiness(
            trustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "ready-for-operator-review",
                ReadyForOperatorReview: true,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "ready-for-review",
                GeneratedAt: new DateTimeOffset(2026, 4, 26, 18, 50, 0, TimeSpan.Zero),
                PacketPath: "artifacts/provider-validation/_automation/2026-04-26/dk1.json",
                SourceSummary: "Trust gate packet generated",
                RequiredSampleCount: 4,
                ReadySampleCount: 4,
                ValidatedEvidenceDocumentCount: 5,
                RequiredOwners: ["data-ops", "trading"],
                Blockers: [],
                Detail: "DK1 packet is ready for review, but operator sign-off is still pending."),
            acceptanceGates:
            [
                new TradingAcceptanceGateDto(
                    GateId: "dk1-trust",
                    Label: "DK1 trust gate",
                    Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                    Detail: "DK1 packet is ready for review, but operator sign-off is still pending.",
                    AuditReference: "artifacts/provider-validation/_automation/2026-04-26/dk1.json")
            ],
            overallStatus: TradingAcceptanceGateStatusDto.ReviewRequired,
            readyForPaperOperation: false);

        var hero = TradingWorkspaceShellPresentationService.BuildDeskHeroState(
            activeRun: CreateActiveRun("paper-41", "Delta Carry", "Paper"),
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Delta Paper Desk");

        hero.FocusLabel.Should().Be("Operator review");
        hero.BadgeText.Should().Be("Attention");
        hero.PrimaryActionId.Should().Be("FundAuditTrail");
        hero.Summary.Should().Contain("operator sign-off is still pending");
    }

    [Fact]
    public void BuildDeskHeroState_WithReadyLiveRun_UsesBlotterAndRiskRail()
    {
        var readiness = CreateReadiness(
            brokerageSync: new WorkstationBrokerageSyncStatusDto(
                FundAccountId: Guid.Parse("9c37d51f-2eba-40f5-9c86-6c9eb3863b8b"),
                ProviderId: "alpaca",
                ExternalAccountId: "acct-31",
                Health: WorkstationBrokerageSyncHealth.Healthy,
                IsLinked: true,
                IsStale: false,
                LastAttemptedSyncAt: new DateTimeOffset(2026, 4, 25, 16, 55, 0, TimeSpan.Zero),
                LastSuccessfulSyncAt: new DateTimeOffset(2026, 4, 25, 16, 54, 0, TimeSpan.Zero),
                LastError: null,
                PositionCount: 4,
                OpenOrderCount: 1,
                FillCount: 18,
                CashTransactionCount: 2,
                SecurityMissingCount: 0,
                Warnings: []));

        var hero = TradingWorkspaceShellPresentationService.BuildDeskHeroState(
            activeRun: CreateActiveRun("live-31", "Gamma Rotation", "Live"),
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Gamma Live Desk");

        hero.FocusLabel.Should().Be("Live oversight");
        hero.BadgeText.Should().Be("Ready");
        hero.PrimaryActionId.Should().Be("PositionBlotter");
        hero.SecondaryActionId.Should().Be("RunRisk");
        hero.Detail.Should().Contain("Brokerage sync healthy");
    }

    [Fact]
    public void BuildDeskHeroState_WithCriticalWorkItem_RoutesBeforeReadyRun()
    {
        var readiness = CreateReadiness(
            workItems:
            [
                new OperatorWorkItemDto(
                    WorkItemId: "brokerage-sync-attention-52",
                    Kind: OperatorWorkItemKindDto.BrokerageSync,
                    Label: "Brokerage sync attention",
                    Detail: "Brokerage sync failed for the active account.",
                    Tone: OperatorWorkItemToneDto.Critical,
                    CreatedAt: new DateTimeOffset(2026, 4, 27, 16, 55, 0, TimeSpan.Zero),
                    TargetPageTag: "TradingShell")
            ]);

        var hero = TradingWorkspaceShellPresentationService.BuildDeskHeroState(
            activeRun: CreateActiveRun("paper-52", "Brokerage Sync Desk", "Paper"),
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Brokerage Paper Desk");

        hero.FocusLabel.Should().Be("Readiness blocked");
        hero.PrimaryActionId.Should().Be("AccountPortfolio");
        hero.TargetLabel.Should().Be("Target page: AccountPortfolio");
    }

    [Fact]
    public void ResolveOperatorWorkItemActionId_WithAccountScopedBrokerageSyncRoute_OpensAccountPortfolio()
    {
        var workItem = new OperatorWorkItemDto(
            WorkItemId: "brokerage-sync-route-1",
            Kind: OperatorWorkItemKindDto.BrokerageSync,
            Label: "Brokerage sync attention",
            Detail: "Brokerage sync failed for account scope.",
            Tone: OperatorWorkItemToneDto.Critical,
            CreatedAt: new DateTimeOffset(2026, 4, 27, 17, 0, 0, TimeSpan.Zero),
            TargetRoute: $"{UiApiRoutes.FundAccountBrokerageSyncAccounts}?fundAccountId=9c37d51f-2eba-40f5-9c86-6c9eb3863b8b",
            TargetPageTag: "TradingShell");

        var actionId = TradingWorkspaceShellPresentationService.ResolveOperatorWorkItemActionId(workItem);

        actionId.Should().Be("AccountPortfolio");
    }

    [Fact]
    public void ExecuteCommandAction_WithNoActiveRun_RaisesAccountPortfolioRequest()
    {
        var viewModel = new TradingWorkspaceShellViewModel();
        TradingWorkspaceShellActionRequest? captured = null;
        viewModel.ActionRequested += (_, request) => captured = request;

        viewModel.ExecuteCommandAction("RunPortfolio");

        captured.Should().NotBeNull();
        captured!.Value.PageTag.Should().Be("AccountPortfolio");
        captured.Value.Action.Should().Be(PaneDropAction.Replace);
    }

    private static TradingOperatorReadinessDto CreateReadiness(
        TradingReplayReadinessDto? replay = null,
        TradingControlReadinessDto? controls = null,
        TradingTrustGateReadinessDto? trustGate = null,
        WorkstationBrokerageSyncStatusDto? brokerageSync = null,
        IReadOnlyList<OperatorWorkItemDto>? workItems = null,
        IReadOnlyList<TradingAcceptanceGateDto>? acceptanceGates = null,
        TradingAcceptanceGateStatusDto overallStatus = TradingAcceptanceGateStatusDto.Ready,
        bool readyForPaperOperation = true)
        => new(
            AsOf: new DateTimeOffset(2026, 4, 27, 15, 0, 0, TimeSpan.Zero),
            ActiveSession: CreateSession(),
            Sessions: [],
            Replay: replay ?? CreateReplay(),
            Controls: controls ?? CreateControls(),
            Promotion: new TradingPromotionReadinessDto(
                State: "Approved",
                Reason: "Promotion handoff completed.",
                RequiresReview: false,
                SourceRunId: "backtest-1",
                TargetRunId: "paper-1",
                SuggestedNextMode: "Paper",
                AuditReference: "audit-promotion-1",
                ApprovalStatus: "approved",
                ManualOverrideId: null,
                ApprovedBy: "operator"),
            TrustGate: trustGate ?? CreateSignedTrustGate(),
            BrokerageSync: brokerageSync,
            WorkItems: workItems ?? [],
            Warnings: [])
        {
            AcceptanceGates = acceptanceGates ?? CreateReadyGates(),
            OverallStatus = overallStatus,
            ReadyForPaperOperation = readyForPaperOperation
        };

    private static ActiveRunContext CreateActiveRun(string runId, string strategyName, string modeLabel)
        => new()
        {
            RunId = runId,
            StrategyName = strategyName,
            ModeLabel = modeLabel,
            StatusLabel = "Running",
            FundScopeLabel = "Test Desk",
            ValidationStatus = new TradingWorkspaceStatusItem
            {
                Label = "Replay verified",
                Detail = "Desk posture is healthy.",
                Tone = TradingWorkspaceStatusTone.Success
            },
            AuditStatus = new TradingWorkspaceStatusItem
            {
                Label = "Audit ready",
                Detail = "Audit trail is healthy.",
                Tone = TradingWorkspaceStatusTone.Success
            },
            PromotionStatus = new TradingWorkspaceStatusItem
            {
                Label = modeLabel == "Live" ? "Live managed" : "Approved",
                Detail = "Promotion handoff completed.",
                Tone = TradingWorkspaceStatusTone.Success
            }
        };

    private static TradingPaperSessionReadinessDto CreateSession()
        => new(
            SessionId: "paper-session-1",
            StrategyId: "strategy-1",
            StrategyName: "Atlas Intraday",
            IsActive: true,
            InitialCash: 250_000m,
            CreatedAt: new DateTimeOffset(2026, 4, 27, 13, 0, 0, TimeSpan.Zero),
            ClosedAt: null,
            SymbolCount: 5,
            OrderCount: 19,
            PositionCount: 3,
            PortfolioValue: 254_000m)
        {
            FillCount = 15,
            LedgerEntryCount = 11
        };

    private static TradingReplayReadinessDto CreateReplay()
        => new(
            SessionId: "paper-session-1",
            ReplaySource: "local",
            IsConsistent: true,
            ComparedFillCount: 15,
            ComparedOrderCount: 19,
            ComparedLedgerEntryCount: 11,
            VerifiedAt: new DateTimeOffset(2026, 4, 27, 14, 45, 0, TimeSpan.Zero),
            LastPersistedFillAt: null,
            LastPersistedOrderUpdateAt: null,
            VerificationAuditId: "audit-ready",
            MismatchReasons: []);

    private static TradingControlReadinessDto CreateControls()
        => new(
            CircuitBreakerOpen: false,
            CircuitBreakerReason: null,
            CircuitBreakerChangedBy: null,
            CircuitBreakerChangedAt: null,
            ManualOverrideCount: 0,
            SymbolLimitCount: 1,
            DefaultMaxPositionSize: 50_000m);

    private static TradingTrustGateReadinessDto CreateSignedTrustGate()
        => new(
            GateId: "dk1",
            Status: "ready",
            ReadyForOperatorReview: true,
            OperatorSignoffRequired: true,
            OperatorSignoffStatus: "signed",
            GeneratedAt: new DateTimeOffset(2026, 4, 27, 14, 50, 0, TimeSpan.Zero),
            PacketPath: "artifacts/provider-validation/_automation/2026-04-27/dk1.json",
            SourceSummary: "Trust gate ready",
            RequiredSampleCount: 3,
            ReadySampleCount: 3,
            ValidatedEvidenceDocumentCount: 4,
            RequiredOwners: ["ops"],
            Blockers: [],
            Detail: "Trust gate evidence is ready for operator review.");

    private static IReadOnlyList<TradingAcceptanceGateDto> CreateReadyGates()
        =>
        [
            new TradingAcceptanceGateDto(
                GateId: "session",
                Label: "Session active",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: "Session is active."),
            new TradingAcceptanceGateDto(
                GateId: "replay",
                Label: "Replay verified",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: "Replay is consistent.",
                SessionId: "paper-session-1",
                AuditReference: "audit-ready"),
            new TradingAcceptanceGateDto(
                GateId: "audit-controls",
                Label: "Risk state explainable",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: "Control evidence is explainable."),
            new TradingAcceptanceGateDto(
                GateId: "promotion",
                Label: "Promotion trace complete",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: "Promotion trace is complete."),
            new TradingAcceptanceGateDto(
                GateId: "dk1-trust",
                Label: "DK1 trust gate",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: "Trust gate is signed.")
        ];
}
