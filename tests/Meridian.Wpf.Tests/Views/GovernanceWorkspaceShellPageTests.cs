using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class GovernanceWorkspaceShellPageTests
{
    [Fact]
    public void BuildLaneHeroState_WithoutFundContext_UsesSwitchContextForSelectedLane()
    {
        var workflow = new WorkspaceWorkflowSummary(
            WorkspaceId: "governance",
            WorkspaceTitle: "Governance",
            StatusLabel: "Context required",
            StatusDetail: "Governance review cannot start until a fund-linked operating context is selected.",
            StatusTone: "Warning",
            NextAction: new WorkflowNextAction(
                Label: "Choose Context",
                Detail: "Select the active context before opening governance lanes.",
                TargetPageTag: "GovernanceShell",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "choose-context",
                Label: "No operating context selected",
                Detail: "Accounting and reconciliation review scope to the active operating context.",
                Tone: "Warning",
                IsBlocking: true),
            Evidence: []);

        var hero = GovernanceWorkspaceShellPage.BuildLaneHeroState(
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
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
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
            WorkspaceId: "governance",
            WorkspaceTitle: "Governance",
            StatusLabel: "3 break(s) open",
            StatusDetail: "Three reconciliation breaks are blocking governance sign-off.",
            StatusTone: "Warning",
            NextAction: new WorkflowNextAction(
                Label: "Review Breaks",
                Detail: "Open reconciliation review.",
                TargetPageTag: "FundReconciliation",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "open-breaks",
                Label: "Approval hold",
                Detail: "3 break(s) block governance sign-off until the queue is reviewed.",
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

        var hero = GovernanceWorkspaceShellPage.BuildLaneHeroState(
            GovernanceSubarea.Reconciliation,
            operatingContext: null,
            profile,
            workspace,
            workflow,
            notifications,
            unreadAlerts: 2);

        hero.LaneLabel.Should().Be("Reconciliation");
        hero.Summary.Should().Be("3 break(s) open");
        hero.Detail.Should().Contain("block governance sign-off");
        hero.HandoffTitle.Should().Be("Review breaks before approval release");
        hero.HandoffDetail.Should().Contain("security coverage");
        hero.PrimaryActionId.Should().Be("FundReconciliation");
        hero.PrimaryActionLabel.Should().Be("Review Breaks");
        hero.SecondaryActionId.Should().Be("FundAuditTrail");
        hero.SecondaryActionLabel.Should().Be("Audit Trail");
        hero.TargetLabel.Should().Be("Target page: FundReconciliation");
    }
}
