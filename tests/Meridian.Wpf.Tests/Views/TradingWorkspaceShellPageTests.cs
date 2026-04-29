using System.IO;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class TradingWorkspaceShellPageTests
{
    [Fact]
    public void ResolvePortfolioNavigationTarget_WithActiveRun_UsesRunPortfolioInsideTradingShell()
    {
        var target = TradingWorkspaceShellPage.ResolvePortfolioNavigationTarget(new ActiveRunContext
        {
            RunId = "paper-run-42",
            StrategyName = "Alpha Mean Reversion"
        });

        target.PageTag.Should().Be("RunPortfolio");
        target.Action.Should().Be(PaneDropAction.SplitLeft);
        target.RunId.Should().Be("paper-run-42");
    }

    [Fact]
    public void ResolvePortfolioNavigationTarget_WithoutActiveRun_FallsBackToAccountPortfolio()
    {
        var target = TradingWorkspaceShellPage.ResolvePortfolioNavigationTarget(null);

        target.PageTag.Should().Be("AccountPortfolio");
        target.Action.Should().Be(PaneDropAction.Replace);
        target.RunId.Should().BeNull();
    }

    [Fact]
    public void ResolveFundAccountId_WithAccountContext_ReturnsAccountId()
    {
        var accountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
        var resolved = TradingWorkspaceShellPage.ResolveFundAccountId(new WorkstationOperatingContext
        {
            ScopeKind = OperatingContextScopeKind.Account,
            ScopeId = "account-scope-id",
            AccountId = accountId.ToString("D"),
            DisplayName = "Northwind Brokerage Account"
        });

        resolved.Should().Be(accountId);
    }

    [Fact]
    public void ResolveFundAccountId_WithAccountScopeFallback_ReturnsScopeId()
    {
        var accountId = Guid.Parse("61e8f9ac-6d5e-47f0-9328-c8ee72bf74f4");
        var resolved = TradingWorkspaceShellPage.ResolveFundAccountId(new WorkstationOperatingContext
        {
            ScopeKind = OperatingContextScopeKind.Account,
            ScopeId = accountId.ToString("D"),
            DisplayName = "Northwind Brokerage Account"
        });

        resolved.Should().Be(accountId);
    }

    [Fact]
    public void ResolveFundAccountId_WithFundContext_ReturnsNull()
    {
        var resolved = TradingWorkspaceShellPage.ResolveFundAccountId(new WorkstationOperatingContext
        {
            ScopeKind = OperatingContextScopeKind.Fund,
            ScopeId = "alpha-credit",
            DisplayName = "Alpha Credit"
        });

        resolved.Should().BeNull();
    }

    [Fact]
    public void BuildReplayStatusItem_WithStaleReplayEvidence_ShowsActiveAndVerifiedCounts()
    {
        var readiness = new TradingOperatorReadinessDto(
            AsOf: new DateTimeOffset(2026, 4, 27, 15, 0, 0, TimeSpan.Zero),
            ActiveSession: new TradingPaperSessionReadinessDto(
                SessionId: "paper-desk-stale",
                StrategyId: "strategy-stale",
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
            },
            Sessions: [],
            Replay: new TradingReplayReadinessDto(
                SessionId: "paper-desk-stale",
                ReplaySource: "local",
                IsConsistent: true,
                ComparedFillCount: 14,
                ComparedOrderCount: 18,
                ComparedLedgerEntryCount: 9,
                VerifiedAt: new DateTimeOffset(2026, 4, 27, 14, 45, 0, TimeSpan.Zero),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-stale",
                MismatchReasons: []),
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 1,
                DefaultMaxPositionSize: 50_000m),
            Promotion: null,
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "ready",
                ReadyForOperatorReview: true,
                OperatorSignoffRequired: false,
                OperatorSignoffStatus: "signed",
                GeneratedAt: null,
                PacketPath: null,
                SourceSummary: null,
                RequiredSampleCount: 0,
                ReadySampleCount: 0,
                ValidatedEvidenceDocumentCount: 0,
                RequiredOwners: [],
                Blockers: [],
                Detail: "Trust gate evidence is ready."),
            BrokerageSync: null,
            WorkItems: [],
            Warnings: [])
        {
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto(
                    GateId: "replay",
                    Label: "Replay verified",
                    Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                    Detail: "Replay verification for paper session paper-desk-stale is stale (fills active=15, verified=14; orders active=19, verified=18; ledger active=11, verified=9).",
                    SessionId: "paper-desk-stale",
                    AuditReference: "audit-stale")
            ],
            OverallStatus = TradingAcceptanceGateStatusDto.ReviewRequired,
            ReadyForPaperOperation = false
        };

        var item = TradingWorkspaceShellPage.BuildReplayStatusItem(readiness);

        item.Label.Should().Be("Replay stale");
        item.Tone.Should().Be(TradingWorkspaceStatusTone.Warning);
        item.Detail.Should().Contain("orders active=19, verified=18");
        item.Detail.Should().Contain("Active session has 19 order(s), 15 fill(s), and 11 ledger entry(s); replay verified 18 order(s), 14 fill(s), and 9 ledger entry(s).");
    }

    [Fact]
    public void BuildDeskHeroState_WithoutOperatingContext_UsesSwitchContextAction()
    {
        var workflow = new WorkspaceWorkflowSummary(
            WorkspaceId: "trading",
            WorkspaceTitle: "Trading",
            StatusLabel: "Context required",
            StatusDetail: "Trading posture cannot be trusted until an operating context is selected.",
            StatusTone: "Warning",
            NextAction: new WorkflowNextAction(
                Label: "Choose Context",
                Detail: "Select the active operating context before opening the cockpit review.",
                TargetPageTag: "TradingShell",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "choose-context",
                Label: "No operating context selected",
                Detail: "Paper review, live posture, and governance-linked trading actions scope to the active operating context.",
                Tone: "Warning",
                IsBlocking: true),
            Evidence: []);

        var hero = TradingWorkspaceShellPage.BuildDeskHeroState(
            activeRun: null,
            workflow,
            readiness: null,
            hasOperatingContext: false,
            operatingContextDisplayName: null);

        hero.FocusLabel.Should().Be("Context handoff");
        hero.Summary.Should().Contain("waiting for an operating context");
        hero.BadgeText.Should().Be("Context required");
        hero.PrimaryActionId.Should().Be("SwitchContext");
        hero.PrimaryActionLabel.Should().Be("Switch Context");
        hero.SecondaryActionId.Should().Be("StrategyRuns");
        hero.SecondaryActionLabel.Should().Be("Run Browser");
        hero.TargetLabel.Should().Be("Target page: Context selector");
    }

    [Fact]
    public void BuildDeskHeroState_WithReplayMismatch_PrioritizesAuditTrail()
    {
        var readiness = new TradingOperatorReadinessDto(
            AsOf: new DateTimeOffset(2026, 4, 25, 16, 0, 0, TimeSpan.Zero),
            ActiveSession: new TradingPaperSessionReadinessDto(
                SessionId: "paper-desk-17",
                StrategyId: "strategy-17",
                StrategyName: "Atlas Intraday",
                IsActive: true,
                InitialCash: 250_000m,
                CreatedAt: new DateTimeOffset(2026, 4, 25, 13, 0, 0, TimeSpan.Zero),
                ClosedAt: null,
                SymbolCount: 6,
                OrderCount: 18,
                PositionCount: 3,
                PortfolioValue: 255_000m),
            Sessions: [],
            Replay: new TradingReplayReadinessDto(
                SessionId: "paper-desk-17",
                ReplaySource: "local",
                IsConsistent: false,
                ComparedFillCount: 14,
                ComparedOrderCount: 11,
                ComparedLedgerEntryCount: 9,
                VerifiedAt: new DateTimeOffset(2026, 4, 25, 15, 45, 0, TimeSpan.Zero),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-17",
                MismatchReasons: ["Fill sequence mismatch detected for paper replay."]),
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 1,
                DefaultMaxPositionSize: 50_000m),
            Promotion: null,
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "pending",
                ReadyForOperatorReview: false,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "waiting",
                GeneratedAt: null,
                PacketPath: null,
                SourceSummary: null,
                RequiredSampleCount: 3,
                ReadySampleCount: 1,
                ValidatedEvidenceDocumentCount: 1,
                RequiredOwners: [],
                Blockers: ["Replay mismatch"],
                Detail: "Replay verification must be resolved before operator review."),
            BrokerageSync: null,
            WorkItems: [],
            Warnings: []);

        var hero = TradingWorkspaceShellPage.BuildDeskHeroState(
            activeRun: new ActiveRunContext
            {
                RunId = "paper-run-17",
                StrategyName = "Atlas Intraday",
                ModeLabel = "Paper",
                ValidationStatus = new TradingWorkspaceStatusItem
                {
                    Label = "Replay mismatch",
                    Detail = "Replay evidence is inconsistent.",
                    Tone = TradingWorkspaceStatusTone.Warning
                }
            },
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Atlas Paper Desk");

        hero.FocusLabel.Should().Be("Replay");
        hero.Summary.Should().Be("Fill sequence mismatch detected for paper replay.");
        hero.HandoffTitle.Should().Contain("Verify replay evidence");
        hero.PrimaryActionId.Should().Be("FundAuditTrail");
        hero.PrimaryActionLabel.Should().Be("Audit Trail");
        hero.SecondaryActionId.Should().Be("NotificationCenter");
        hero.TargetLabel.Should().Be("Target page: FundAuditTrail");
    }

    [Fact]
    public void BuildDeskHeroState_WithUnexplainedControlEvidence_PrioritizesAuditTrail()
    {
        var readiness = new TradingOperatorReadinessDto(
            AsOf: new DateTimeOffset(2026, 4, 26, 18, 0, 0, TimeSpan.Zero),
            ActiveSession: null,
            Sessions: [],
            Replay: null,
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 0,
                DefaultMaxPositionSize: null)
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
            },
            Promotion: null,
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "pending",
                ReadyForOperatorReview: false,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "waiting",
                GeneratedAt: null,
                PacketPath: null,
                SourceSummary: null,
                RequiredSampleCount: 0,
                ReadySampleCount: 0,
                ValidatedEvidenceDocumentCount: 0,
                RequiredOwners: [],
                Blockers: [],
                Detail: "Trust gate evidence is pending."),
            BrokerageSync: null,
            WorkItems: [],
            Warnings: []);

        var hero = TradingWorkspaceShellPage.BuildDeskHeroState(
            activeRun: null,
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Wave 2 Paper Desk");

        hero.FocusLabel.Should().Be("Controls");
        hero.Summary.Should().Contain("missing actor, scope, reason");
        hero.HandoffTitle.Should().Be("Complete risk evidence");
        hero.PrimaryActionId.Should().Be("FundAuditTrail");
        hero.SecondaryActionId.Should().Be("RunRisk");
        hero.TargetLabel.Should().Be("Target page: FundAuditTrail");
    }

    [Fact]
    public void BuildDeskHeroState_WithTrustGateAwaitingSignoff_DoesNotShowReady()
    {
        var readiness = new TradingOperatorReadinessDto(
            AsOf: new DateTimeOffset(2026, 4, 26, 19, 0, 0, TimeSpan.Zero),
            ActiveSession: new TradingPaperSessionReadinessDto(
                SessionId: "paper-desk-41",
                StrategyId: "strategy-41",
                StrategyName: "Delta Carry",
                IsActive: true,
                InitialCash: 500_000m,
                CreatedAt: new DateTimeOffset(2026, 4, 26, 15, 0, 0, TimeSpan.Zero),
                ClosedAt: null,
                SymbolCount: 5,
                OrderCount: 9,
                PositionCount: 2,
                PortfolioValue: 502_000m),
            Sessions: [],
            Replay: new TradingReplayReadinessDto(
                SessionId: "paper-desk-41",
                ReplaySource: "local",
                IsConsistent: true,
                ComparedFillCount: 8,
                ComparedOrderCount: 9,
                ComparedLedgerEntryCount: 6,
                VerifiedAt: new DateTimeOffset(2026, 4, 26, 18, 45, 0, TimeSpan.Zero),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-41",
                MismatchReasons: []),
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 2,
                DefaultMaxPositionSize: 125_000m),
            Promotion: new TradingPromotionReadinessDto(
                State: "Approved",
                Reason: "Backtest to paper review passed.",
                RequiresReview: false,
                SourceRunId: "backtest-41",
                TargetRunId: "paper-41",
                SuggestedNextMode: "Paper",
                AuditReference: "audit-promotion-41",
                ApprovalStatus: "approved",
                ManualOverrideId: null,
                ApprovedBy: "operator"),
            TrustGate: new TradingTrustGateReadinessDto(
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
            BrokerageSync: null,
            WorkItems: [],
            Warnings: [])
        {
            AcceptanceGates =
            [
                new TradingAcceptanceGateDto(
                    GateId: "dk1-trust",
                    Label: "DK1 trust gate",
                    Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                    Detail: "DK1 packet is ready for review, but operator sign-off is still pending.",
                    AuditReference: "artifacts/provider-validation/_automation/2026-04-26/dk1.json")
            ],
            OverallStatus = TradingAcceptanceGateStatusDto.ReviewRequired,
            ReadyForPaperOperation = false
        };

        var hero = TradingWorkspaceShellPage.BuildDeskHeroState(
            activeRun: new ActiveRunContext
            {
                RunId = "paper-41",
                StrategyName = "Delta Carry",
                ModeLabel = "Paper",
                StatusLabel = "Running",
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
                    Label = "Approved",
                    Detail = "Promotion handoff completed.",
                    Tone = TradingWorkspaceStatusTone.Success
                }
            },
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Delta Paper Desk");

        hero.FocusLabel.Should().Be("Operator review");
        hero.BadgeText.Should().Be("Attention");
        hero.BadgeTone.Should().Be(TradingWorkspaceStatusTone.Warning);
        hero.Summary.Should().Contain("operator sign-off is still pending");
        hero.HandoffTitle.Should().Be("Complete dk1 trust gate");
        hero.PrimaryActionId.Should().Be("FundAuditTrail");
        hero.TargetLabel.Should().Be("Target page: FundAuditTrail");
    }

    [Fact]
    public void BuildDeskHeroState_WithReadyLiveRun_UsesBlotterAndRiskRail()
    {
        var readiness = new TradingOperatorReadinessDto(
            AsOf: new DateTimeOffset(2026, 4, 25, 17, 0, 0, TimeSpan.Zero),
            ActiveSession: new TradingPaperSessionReadinessDto(
                SessionId: "live-review-31",
                StrategyId: "strategy-31",
                StrategyName: "Gamma Rotation",
                IsActive: true,
                InitialCash: 1_000_000m,
                CreatedAt: new DateTimeOffset(2026, 4, 25, 14, 0, 0, TimeSpan.Zero),
                ClosedAt: null,
                SymbolCount: 8,
                OrderCount: 22,
                PositionCount: 4,
                PortfolioValue: 1_045_000m),
            Sessions: [],
            Replay: new TradingReplayReadinessDto(
                SessionId: "live-review-31",
                ReplaySource: "local",
                IsConsistent: true,
                ComparedFillCount: 18,
                ComparedOrderCount: 12,
                ComparedLedgerEntryCount: 10,
                VerifiedAt: new DateTimeOffset(2026, 4, 25, 16, 45, 0, TimeSpan.Zero),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-31",
                MismatchReasons: []),
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 2,
                DefaultMaxPositionSize: 150_000m),
            Promotion: new TradingPromotionReadinessDto(
                State: "Live managed",
                Reason: "Desk is actively managed.",
                RequiresReview: false,
                SourceRunId: "paper-31",
                TargetRunId: "live-31",
                SuggestedNextMode: "Live",
                AuditReference: "audit-31",
                ApprovalStatus: "approved",
                ManualOverrideId: null,
                ApprovedBy: "operator"),
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "ready",
                ReadyForOperatorReview: true,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "signed",
                GeneratedAt: new DateTimeOffset(2026, 4, 25, 16, 50, 0, TimeSpan.Zero),
                PacketPath: "artifacts/provider-validation/_automation/2026-04-25/dk1.json",
                SourceSummary: "Trust gate ready",
                RequiredSampleCount: 3,
                ReadySampleCount: 3,
                ValidatedEvidenceDocumentCount: 4,
                RequiredOwners: ["ops"],
                Blockers: [],
                Detail: "Trust gate evidence is ready for operator review."),
            BrokerageSync: new WorkstationBrokerageSyncStatusDto(
                FundAccountId: Guid.NewGuid(),
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
                Warnings: []),
            WorkItems: [],
            Warnings: [])
        {
            AcceptanceGates =
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
                    SessionId: "live-review-31",
                    AuditReference: "audit-31"),
                new TradingAcceptanceGateDto(
                    GateId: "audit-controls",
                    Label: "Risk state explainable",
                    Status: TradingAcceptanceGateStatusDto.Ready,
                    Detail: "Control evidence is explainable."),
                new TradingAcceptanceGateDto(
                    GateId: "promotion",
                    Label: "Promotion trace complete",
                    Status: TradingAcceptanceGateStatusDto.Ready,
                    Detail: "Promotion trace is complete.",
                    RunId: "paper-31",
                    AuditReference: "audit-31"),
                new TradingAcceptanceGateDto(
                    GateId: "dk1-trust",
                    Label: "DK1 trust gate",
                    Status: TradingAcceptanceGateStatusDto.Ready,
                    Detail: "Trust gate is signed.")
            ],
            OverallStatus = TradingAcceptanceGateStatusDto.Ready,
            ReadyForPaperOperation = true
        };

        var hero = TradingWorkspaceShellPage.BuildDeskHeroState(
            activeRun: new ActiveRunContext
            {
                RunId = "live-31",
                StrategyName = "Gamma Rotation",
                ModeLabel = "Live",
                StatusLabel = "Running",
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
                    Label = "Live managed",
                    Detail = "Promotion handoff completed.",
                    Tone = TradingWorkspaceStatusTone.Success
                }
            },
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Gamma Live Desk");

        hero.FocusLabel.Should().Be("Live oversight");
        hero.Summary.Should().Contain("active live handoff");
        hero.Detail.Should().Contain("Brokerage sync healthy");
        hero.BadgeText.Should().Be("Ready");
        hero.PrimaryActionId.Should().Be("PositionBlotter");
        hero.PrimaryActionLabel.Should().Be("Open Blotter");
        hero.SecondaryActionId.Should().Be("RunRisk");
        hero.SecondaryActionLabel.Should().Be("Open Risk Rail");
        hero.TargetLabel.Should().Be("Target page: PositionBlotter");
    }

    [Fact]
    public void BuildDeskHeroState_WithCriticalWorkItem_DoesNotShowReadyRun()
    {
        var readiness = new TradingOperatorReadinessDto(
            AsOf: new DateTimeOffset(2026, 4, 27, 17, 0, 0, TimeSpan.Zero),
            ActiveSession: new TradingPaperSessionReadinessDto(
                SessionId: "paper-desk-52",
                StrategyId: "strategy-52",
                StrategyName: "Brokerage Sync Desk",
                IsActive: true,
                InitialCash: 1_000_000m,
                CreatedAt: new DateTimeOffset(2026, 4, 27, 14, 0, 0, TimeSpan.Zero),
                ClosedAt: null,
                SymbolCount: 7,
                OrderCount: 10,
                PositionCount: 3,
                PortfolioValue: 1_008_000m),
            Sessions: [],
            Replay: new TradingReplayReadinessDto(
                SessionId: "paper-desk-52",
                ReplaySource: "local",
                IsConsistent: true,
                ComparedFillCount: 8,
                ComparedOrderCount: 10,
                ComparedLedgerEntryCount: 6,
                VerifiedAt: new DateTimeOffset(2026, 4, 27, 16, 45, 0, TimeSpan.Zero),
                LastPersistedFillAt: null,
                LastPersistedOrderUpdateAt: null,
                VerificationAuditId: "audit-52",
                MismatchReasons: []),
            Controls: new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 2,
                DefaultMaxPositionSize: 150_000m),
            Promotion: new TradingPromotionReadinessDto(
                State: "Approved",
                Reason: "Paper promotion evidence is complete.",
                RequiresReview: false,
                SourceRunId: "backtest-52",
                TargetRunId: "paper-52",
                SuggestedNextMode: "Paper",
                AuditReference: "audit-promotion-52",
                ApprovalStatus: "approved",
                ManualOverrideId: null,
                ApprovedBy: "operator"),
            TrustGate: new TradingTrustGateReadinessDto(
                GateId: "dk1",
                Status: "ready",
                ReadyForOperatorReview: true,
                OperatorSignoffRequired: true,
                OperatorSignoffStatus: "signed",
                GeneratedAt: new DateTimeOffset(2026, 4, 27, 16, 50, 0, TimeSpan.Zero),
                PacketPath: "artifacts/provider-validation/_automation/2026-04-27/dk1.json",
                SourceSummary: "Trust gate ready",
                RequiredSampleCount: 4,
                ReadySampleCount: 4,
                ValidatedEvidenceDocumentCount: 5,
                RequiredOwners: ["ops"],
                Blockers: [],
                Detail: "Trust gate evidence is signed."),
            BrokerageSync: null,
            WorkItems:
            [
                new OperatorWorkItemDto(
                    WorkItemId: "brokerage-sync-attention-52",
                    Kind: OperatorWorkItemKindDto.BrokerageSync,
                    Label: "Brokerage sync attention",
                    Detail: "Brokerage sync failed for the active account.",
                    Tone: OperatorWorkItemToneDto.Critical,
                    CreatedAt: new DateTimeOffset(2026, 4, 27, 16, 55, 0, TimeSpan.Zero),
                    TargetPageTag: "TradingShell")
            ],
            Warnings: [])
        {
            AcceptanceGates =
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
                    Detail: "Replay is consistent."),
                new TradingAcceptanceGateDto(
                    GateId: "audit-controls",
                    Label: "Risk state explainable",
                    Status: TradingAcceptanceGateStatusDto.Ready,
                    Detail: "Controls are explainable."),
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
            ],
            OverallStatus = TradingAcceptanceGateStatusDto.Ready,
            ReadyForPaperOperation = true
        };

        var hero = TradingWorkspaceShellPage.BuildDeskHeroState(
            activeRun: new ActiveRunContext
            {
                RunId = "paper-52",
                StrategyName = "Brokerage Sync Desk",
                ModeLabel = "Paper",
                StatusLabel = "Running",
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
                    Label = "Approved",
                    Detail = "Promotion handoff completed.",
                    Tone = TradingWorkspaceStatusTone.Success
                }
            },
            workflow: null,
            readiness,
            hasOperatingContext: true,
            operatingContextDisplayName: "Brokerage Paper Desk");

        hero.FocusLabel.Should().Be("Readiness blocked");
        hero.Summary.Should().Be("Brokerage sync failed for the active account.");
        hero.BadgeText.Should().Be("Attention");
        hero.BadgeTone.Should().Be(TradingWorkspaceStatusTone.Warning);
        hero.HandoffTitle.Should().Be("Brokerage sync attention");
        hero.PrimaryActionId.Should().Be("AccountPortfolio");
        hero.PrimaryActionLabel.Should().Be("Open Portfolio");
        hero.SecondaryActionId.Should().Be("FundAuditTrail");
        hero.TargetLabel.Should().Be("Target page: AccountPortfolio");
    }

    [Fact]
    public void ResolveOperatorWorkItemActionId_WithRouteBackedGovernanceShell_OpensConcreteWorkbench()
    {
        var workItem = new OperatorWorkItemDto(
            WorkItemId: "reconciliation-break-route-52",
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: "Reconciliation break requires review",
            Detail: "Cash mismatch is open and assigned to governance review.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: new DateTimeOffset(2026, 4, 27, 17, 30, 0, TimeSpan.Zero),
            TargetRoute: $"{UiApiRoutes.ReconciliationBreakQueue}/break-123/review",
            TargetPageTag: "GovernanceShell");

        var actionId = TradingWorkspaceShellPage.ResolveOperatorWorkItemActionId(workItem);

        actionId.Should().Be("FundReconciliation");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldProjectConsistentDegradedStatusCard()
    {
        var viewModelCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\ViewModels\TradingWorkspaceShellViewModel.cs"));
        var serviceCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Services\TradingWorkspaceShellPresentationService.cs"));

        viewModelCode.Should().Contain("ApplyState(_presentationService.BuildDegradedState());");
        serviceCode.Should().Contain("internal static TradingStatusCardPresentation BuildStatusCardPresentation(TradingWorkspaceSummary summary)");
        serviceCode.Should().Contain("internal static TradingStatusCardPresentation BuildDegradedStatusCardPresentation()");
        serviceCode.Should().Contain("Label = \"Promotion refresh degraded\"");
        serviceCode.Should().Contain("Label = \"Audit refresh degraded\"");
        serviceCode.Should().Contain("Label = \"Validation refresh degraded\"");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldNotExposePrematureDeepReviewActions()
    {
        var code = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml.cs"));
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml"));

        code.Should().NotContain("Id = \"RunDetail\"");
        code.Should().NotContain("Id = \"EventReplay\"");
        code.Should().NotContain("Id = \"CollectionSessions\"");
        code.Should().NotContain("case \"RunDetail\":");
        code.Should().NotContain("case \"EventReplay\":");
        code.Should().NotContain("case \"CollectionSessions\":");
        xaml.Should().NotContain("Deeper Review");
        xaml.Should().NotContain("OpenRunReview_Click");
        xaml.Should().NotContain("OpenReplayReview_Click");
        xaml.Should().NotContain("OpenCollectionSessions_Click");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldReplaceGenericAwaitingRunsCopyWithWorkflowGuidance()
    {
        var serviceCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Services\TradingWorkspaceShellPresentationService.cs"));
        var viewModelCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\ViewModels\TradingWorkspaceShellViewModel.cs"));
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml"));

        xaml.Should().Contain("Workflow Status");
        xaml.Should().Contain("Handoff");
        xaml.Should().Contain("Primary Blocker");
        xaml.Should().Contain("Next Action");
        xaml.Should().Contain("TradingWorkflowPrimaryButton");
        xaml.Should().NotContain("Awaiting runs");

        serviceCode.Should().Contain("GetTradingWorkflowSummaryAsync");
        serviceCode.Should().Contain("BuildWorkflowStatusCardPresentation");
        serviceCode.Should().Contain("CreateWorkflowActionRequest");
        serviceCode.Should().Contain("Target page:");
        viewModelCode.Should().Contain("ExecuteWorkflowNextAction");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldConsumeSharedOperatorReadiness()
    {
        var serviceCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Services\TradingWorkspaceShellPresentationService.cs"));

        serviceCode.Should().Contain("TradingOperatorReadinessService? operatorReadinessService = null");
        serviceCode.Should().Contain("GetTradingOperatorReadinessAsync");
        serviceCode.Should().Contain("BuildOperatorReadinessStatusCardPresentation");
        serviceCode.Should().Contain("DK1 trust gate");
        serviceCode.Should().Contain("Paper session, controls, brokerage sync, and Security Master coverage");
        serviceCode.Should().Contain("Brokerage sync evidence is unavailable.");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldExposeDeskBriefingHero()
    {
        var pageCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml.cs"));
        var serviceCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Services\TradingWorkspaceShellPresentationService.cs"));
        var viewModelCode = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\ViewModels\TradingWorkspaceShellViewModel.cs"));
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml"));

        xaml.Should().Contain("Desk Briefing");
        xaml.Should().Contain("TradingHeroPrimaryActionButton");
        xaml.Should().Contain("TradingHeroSecondaryActionButton");
        xaml.Should().Contain("TradingHeroTargetText");
        xaml.IndexOf("Desk Briefing", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Active Positions", StringComparison.Ordinal));

        serviceCode.Should().Contain("internal static TradingDeskHeroState BuildDeskHeroState(");
        serviceCode.Should().Contain("internal static TradingDeskHeroState BuildDegradedDeskHeroState()");
        viewModelCode.Should().Contain("ApplyDeskHero(");
        pageCode.Should().Contain("OnTradingHeroPrimaryActionClick");
        viewModelCode.Should().Contain("ExecuteHeroPrimaryAction");
    }

    [Fact]
    public void TradingWorkspaceShellPageSource_ShouldPlaceDeskActionsAheadOfNarrativeSupportPanels()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\TradingWorkspaceShellPage.xaml"));

        xaml.Should().Contain("Desk Lanes &amp; Supporting Tools");
        xaml.IndexOf("Active Positions", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Paper Runs", StringComparison.Ordinal));
        xaml.IndexOf("Workflow Status", StringComparison.Ordinal).Should().BeLessThan(xaml.IndexOf("Desk Lanes &amp; Supporting Tools", StringComparison.Ordinal));
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

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
