namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Stable identifiers for workstation workflow actions used across summaries and routing.
/// </summary>
public static class WorkflowActionIds
{
    public const string StrategyStartBacktest = "workflow.strategy.start-backtest";
    public const string StrategyReviewRuns = "workflow.strategy.review-runs";
    public const string StrategySendToTradingReview = "workflow.strategy.send-to-trading-review";

    public const string TradingChooseContext = "workflow.trading.choose-context";
    public const string TradingReviewPaperCandidate = "workflow.trading.review-paper-candidate";
    public const string TradingOpenCockpit = "workflow.trading.open-cockpit";
    public const string TradingReviewExecutionControls = "workflow.trading.review-execution-controls";

    public const string PortfolioOpen = "workflow.portfolio.open";
    public const string PortfolioReviewBrokerageSync = "workflow.portfolio.review-brokerage-sync";

    public const string AccountingChooseContext = "workflow.accounting.choose-context";
    public const string AccountingReviewLiveHandoff = "workflow.accounting.review-live-handoff";
    public const string AccountingReviewReconciliation = "workflow.accounting.review-reconciliation-breaks";
    public const string AccountingReviewLedgerContinuity = "workflow.accounting.review-ledger-continuity";
    public const string AccountingOpen = "workflow.accounting.open";
    public const string AccountingReviewAuditTrail = "workflow.accounting.review-audit-trail";

    public const string ReportingOpen = "workflow.reporting.open";
    public const string ReportingApproveReportPack = "workflow.reporting.approve-report-pack";

    public const string DataOpenProviderHealth = "workflow.data.open-provider-health";
    public const string DataOpenBackfillQueue = "workflow.data.open-backfill-queue";
    public const string DataOpenQueueOverview = "workflow.data.open-queue-overview";
    public const string DataReviewSecurityMaster = "workflow.data.review-security-master";

    public const string SettingsOpen = "workflow.settings.open";
}
