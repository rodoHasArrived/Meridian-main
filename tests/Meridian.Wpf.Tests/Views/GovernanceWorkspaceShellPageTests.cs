using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class GovernanceWorkspaceShellPageTests
{
    [Theory]
    [InlineData(GovernanceSubarea.Operations, "FundLedger")]
    [InlineData(GovernanceSubarea.Accounting, "FundTrialBalance")]
    [InlineData(GovernanceSubarea.Reconciliation, "FundReconciliation")]
    [InlineData(GovernanceSubarea.Reporting, "FundCashFinancing")]
    [InlineData(GovernanceSubarea.Audit, "FundAuditTrail")]
    public void ResolveLanePrimaryActionId_ReturnsExpectedActionId(
        GovernanceSubarea subarea,
        string expectedActionId)
    {
        GovernanceWorkspacePresentationService.ResolveLanePrimaryActionId(subarea)
            .Should()
            .Be(expectedActionId);
    }

    [Fact]
    public void BuildLaneHeroState_WithoutFundContext_UsesSwitchContextForSelectedLane()
    {
        var workflow = new WorkspaceWorkflowSummary(
            WorkspaceId: "accounting",
            WorkspaceTitle: "Accounting",
            StatusLabel: "Context required",
            StatusDetail: "Accounting review cannot start until a fund-linked operating context is selected.",
            StatusTone: "Warning",
            NextAction: new WorkflowNextAction(
                Label: "Choose Context",
                Detail: "Select the active context before opening accounting lanes.",
                TargetPageTag: "AccountingShell",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "choose-context",
                Label: "No operating context selected",
                Detail: "Accounting and reconciliation review scope to the active operating context.",
                Tone: "Warning",
                IsBlocking: true),
            Evidence: []);

        var hero = GovernanceWorkspacePresentationService.BuildLaneHeroState(
            GovernanceSubarea.Accounting,
            operatingContext: null,
            profile: null,
            workspace: null,
            workflow,
            notifications: Array.Empty<NotificationHistoryItem>(),
            unreadAlerts: 0);

        hero.LaneLabel.Should().Be("Accounting");
        hero.Summary.Should().Contain("Accounting review is waiting for a fund-linked context.");
        hero.HandoffTitle.Should().Be("Context required");
        hero.PrimaryActionId.Should().Be("SwitchContext");
        hero.PrimaryActionLabel.Should().Be("Switch Context");
        hero.SecondaryActionId.Should().Be("Diagnostics");
        hero.TargetLabel.Should().Be("Target page: Context selector");
    }

    [Fact]
    public void BuildLaneHeroState_ReconciliationLane_WithOpenBreaks_PrioritizesBreakReview()
    {
        var asOf = new DateTimeOffset(2026, 4, 25, 15, 30, 0, TimeSpan.Zero);
        var profile = new FundProfileDetail(
            FundProfileId: "fund-001",
            DisplayName: "Atlas Opportunities",
            LegalEntityName: "Atlas Opportunities LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "accounting",
            DefaultLandingPageTag: "AccountingShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated);
        var ledger = new FundLedgerSummary(
            FundProfileId: profile.FundProfileId,
            FundDisplayName: profile.DisplayName,
            ScopeKind: FundLedgerScope.Consolidated,
            ScopeId: null,
            AsOf: asOf,
            JournalEntryCount: 18,
            LedgerEntryCount: 44,
            AssetBalance: 1_500_000m,
            LiabilityBalance: 100_000m,
            EquityBalance: 1_400_000m,
            RevenueBalance: 22_000m,
            ExpenseBalance: 8_000m,
            TrialBalance:
            [
                new FundTrialBalanceLine("Cash", "Asset", "USD", "fa-1", 120_000m, 3)
            ],
            Journal:
            [
                new FundJournalLine(Guid.NewGuid(), asOf, "Daily close", 100m, 100m, 2)
            ],
            EntityCount: 2,
            SleeveCount: 1,
            VehicleCount: 1);
        var workspace = new FundOperationsWorkspaceDto(
            FundProfileId: profile.FundProfileId,
            DisplayName: profile.DisplayName,
            BaseCurrency: profile.BaseCurrency,
            AsOf: asOf,
            RecordedRunCount: 4,
            RelatedRunIds: ["paper-run-1"],
            Workspace: new FundWorkspaceSummary(
                FundProfileId: profile.FundProfileId,
                FundDisplayName: profile.DisplayName,
                BaseCurrency: profile.BaseCurrency,
                AsOf: asOf,
                TotalAccounts: 5,
                BankAccountCount: 2,
                BrokerageAccountCount: 2,
                CustodyAccountCount: 1,
                TotalCash: 500_000m,
                GrossExposure: 1_250_000m,
                NetExposure: 850_000m,
                TotalEquity: 1_400_000m,
                FinancingCost: 15_000m,
                PendingSettlement: 12_000m,
                OpenReconciliationBreaks: 3,
                ReconciliationRuns: 2,
                JournalEntryCount: ledger.JournalEntryCount,
                TrialBalanceLineCount: ledger.TrialBalance.Count,
                SecurityResolvedCount: 10,
                SecurityMissingCount: 1,
                SecurityCoverageIssues: 1),
            Ledger: ledger,
            LedgerReconciliationSnapshot: new FundLedgerReconciliationSnapshot(
                FundProfileId: profile.FundProfileId,
                AsOf: asOf,
                Consolidated: new FundLedgerDimensionSnapshot(asOf, 18, 44, []),
                Entities: new Dictionary<string, FundLedgerDimensionSnapshot>(),
                Sleeves: new Dictionary<string, FundLedgerDimensionSnapshot>(),
                Vehicles: new Dictionary<string, FundLedgerDimensionSnapshot>()),
            Accounts: [],
            BankSnapshots: [],
            CashFinancing: new CashFinancingSummary(
                Currency: "USD",
                TotalCash: 500_000m,
                PendingSettlement: 12_000m,
                FinancingCost: 15_000m,
                MarginBalance: 0m,
                RealizedPnl: 22_000m,
                UnrealizedPnl: 6_000m,
                LongMarketValue: 900_000m,
                ShortMarketValue: 50_000m,
                GrossExposure: 950_000m,
                NetExposure: 850_000m,
                TotalEquity: 1_400_000m,
                Highlights: ["Cash stable"]),
            Reconciliation: new ReconciliationSummary(
                RunCount: 2,
                OpenBreakCount: 3,
                BreakAmountTotal: 125_000m,
                RecentRuns: [],
                SecurityCoverageIssueCount: 1),
            Nav: new FundNavAttributionSummaryDto(
                Currency: "USD",
                TotalNav: 1_400_000m,
                ComponentCount: 4,
                EntityCount: 2,
                SleeveCount: 1,
                VehicleCount: 1,
                AssetClassExposure: []),
            Reporting: new FundReportingSummaryDto(
                ProfileCount: 2,
                RecommendedProfiles: ["Board"],
                ReportPackTargets: ["Board Pack", "Operations Pack"],
                Profiles: [],
                Summary: "Two governance profiles ready."));
        var workflow = new WorkspaceWorkflowSummary(
            WorkspaceId: "accounting",
            WorkspaceTitle: "Accounting",
            StatusLabel: "3 break(s) open",
            StatusDetail: "Three reconciliation breaks are blocking close sign-off.",
            StatusTone: "Warning",
            NextAction: new WorkflowNextAction(
                Label: "Review Breaks",
                Detail: "Open reconciliation review.",
                TargetPageTag: "FundReconciliation",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "open-breaks",
                Label: "Approval hold",
                Detail: "3 break(s) block close sign-off until the queue is reviewed.",
                Tone: "Warning",
                IsBlocking: true),
            Evidence:
            [
                new WorkflowEvidenceBadge("Audit", "2 recent approvals", "Info")
            ]);
        var notifications = new[]
        {
            new NotificationHistoryItem
            {
                Title = "Break threshold exceeded",
                Message = "Review reconciliation exceptions before approval release.",
                Type = NotificationType.Warning,
                Timestamp = new DateTime(2026, 4, 25, 8, 15, 0),
                Tag = "warning"
            }
        };

        var hero = GovernanceWorkspacePresentationService.BuildLaneHeroState(
            GovernanceSubarea.Reconciliation,
            operatingContext: null,
            profile,
            workspace,
            workflow,
            notifications,
            unreadAlerts: 2);

        hero.LaneLabel.Should().Be("Reconciliation");
        hero.Summary.Should().Be("3 break(s) open");
        hero.Detail.Should().Contain("block close sign-off");
        hero.HandoffTitle.Should().Be("Review breaks before approval release");
        hero.HandoffDetail.Should().Contain("security coverage");
        hero.PrimaryActionId.Should().Be("FundReconciliation");
        hero.PrimaryActionLabel.Should().Be("Review Breaks");
        hero.SecondaryActionId.Should().Be("FundAuditTrail");
        hero.SecondaryActionLabel.Should().Be("Audit Trail");
        hero.TargetLabel.Should().Be("Target page: FundReconciliation");
    }

    [Fact]
    public void BuildLaneSummaries_WithGovernanceProjection_UsesSharedLifecyclePosture()
    {
        var asOf = new DateTimeOffset(2026, 4, 25, 15, 30, 0, TimeSpan.Zero);
        var profile = new FundProfileDetail(
            FundProfileId: "fund-001",
            DisplayName: "Atlas Opportunities",
            LegalEntityName: "Atlas Opportunities LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "accounting",
            DefaultLandingPageTag: "AccountingShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated);
        var workspace = new FundOperationsWorkspaceDto(
            FundProfileId: profile.FundProfileId,
            DisplayName: profile.DisplayName,
            BaseCurrency: profile.BaseCurrency,
            AsOf: asOf,
            RecordedRunCount: 1,
            RelatedRunIds: ["paper-run-1"],
            Workspace: new FundWorkspaceSummary(
                FundProfileId: profile.FundProfileId,
                FundDisplayName: profile.DisplayName,
                BaseCurrency: profile.BaseCurrency,
                AsOf: asOf,
                TotalAccounts: 1,
                BankAccountCount: 1,
                BrokerageAccountCount: 0,
                CustodyAccountCount: 0,
                TotalCash: 100m,
                GrossExposure: 100m,
                NetExposure: 100m,
                TotalEquity: 100m,
                FinancingCost: 0m,
                PendingSettlement: 0m,
                OpenReconciliationBreaks: 0,
                ReconciliationRuns: 1,
                JournalEntryCount: 1,
                TrialBalanceLineCount: 1),
            Ledger: new FundLedgerSummary(
                FundProfileId: profile.FundProfileId,
                FundDisplayName: profile.DisplayName,
                ScopeKind: FundLedgerScope.Consolidated,
                ScopeId: null,
                AsOf: asOf,
                JournalEntryCount: 1,
                LedgerEntryCount: 1,
                AssetBalance: 100m,
                LiabilityBalance: 0m,
                EquityBalance: 100m,
                RevenueBalance: 0m,
                ExpenseBalance: 0m,
                TrialBalance: [new FundTrialBalanceLine("Cash", "Asset", "USD", "fa-1", 100m, 1)],
                Journal: [new FundJournalLine(Guid.NewGuid(), asOf, "Daily close", 100m, 100m, 2)],
                EntityCount: 1,
                SleeveCount: 0,
                VehicleCount: 0),
            LedgerReconciliationSnapshot: new FundLedgerReconciliationSnapshot(
                FundProfileId: profile.FundProfileId,
                AsOf: asOf,
                Consolidated: new FundLedgerDimensionSnapshot(asOf, 1, 1, []),
                Entities: new Dictionary<string, FundLedgerDimensionSnapshot>(),
                Sleeves: new Dictionary<string, FundLedgerDimensionSnapshot>(),
                Vehicles: new Dictionary<string, FundLedgerDimensionSnapshot>()),
            Accounts: [],
            BankSnapshots: [],
            CashFinancing: new CashFinancingSummary("USD", 100m, 0m, 0m, 0m, 0m, 0m, 100m, 0m, 100m, 100m, 100m, []),
            Reconciliation: new ReconciliationSummary(1, 0, 0m, []),
            Nav: new FundNavAttributionSummaryDto("USD", 100m, 1, 1, 0, 0, []),
            Reporting: new FundReportingSummaryDto(1, ["excel"], ["board"], [], "ready"),
            Governance: new GovernanceLifecycleProjectionDto(
                DecisionPosture: "Reconciliation decisions are cleared in shared lifecycle state.",
                SignoffPosture: "Sign-off is submitted and awaiting reviewer decision in shared lifecycle.",
                CloseReadiness: "Close readiness remains blocked until shared lifecycle gates, reconciliation decisions, and report-pack evidence align.",
                AuditTraceability: "Traceability is backed by 3 lifecycle event(s) and 5 evidence reference(s).",
                TimelineEventCount: 3,
                EvidenceReferenceCount: 5));

        var summaries = GovernanceWorkspacePresentationService.BuildLaneSummaries(
            profile,
            workspace,
            workflow: null,
            notifications: [],
            unreadAlerts: 0);

        summaries.Reconciliation.Summary.Should().Contain("shared lifecycle");
        summaries.Reconciliation.Detail.Should().Contain("Sign-off is submitted");
        summaries.Reporting.Detail.Should().Contain("Close readiness remains blocked");
        summaries.Audit.Summary.Should().Contain("Traceability is backed");
    }
}
