namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Stable identifiers for workstation workflow actions used across summaries and routing.
/// </summary>
public static class WorkflowActionIds
{
    public const string PrimaryOperatorImport = "workflow.primary-operator.import";
    public const string PrimaryOperatorValidate = "workflow.primary-operator.validate";
    public const string PrimaryOperatorReconcile = "workflow.primary-operator.reconcile";
    public const string PrimaryOperatorInvestigate = "workflow.primary-operator.investigate";
    public const string PrimaryOperatorApprove = "workflow.primary-operator.approve";
    public const string PrimaryOperatorReport = "workflow.primary-operator.report";

    public const string AccountingRecordsReviewSourceRecords = "workflow.accounting-records.review-source-records";
    public const string AccountingRecordsReviewNormalizedActivity = "workflow.accounting-records.review-normalized-activity";
    public const string AccountingRecordsReviewReconciliationCases = "workflow.accounting-records.review-reconciliation-cases";
    public const string AccountingRecordsReviewLedgerEvidence = "workflow.accounting-records.review-ledger-evidence";
    public const string AccountingRecordsReviewApprovals = "workflow.accounting-records.review-approvals";
    public const string AccountingRecordsReviewReportLineage = "workflow.accounting-records.review-report-lineage";

    public const string StrategyStartBacktest = "workflow.strategy.start-backtest";
    public const string StrategyReviewRuns = "workflow.strategy.review-runs";
    public const string StrategySendToTradingReview = "workflow.strategy.send-to-trading-review";

    public const string TradingChooseContext = "workflow.trading.choose-context";
    public const string TradingReviewPaperCandidate = "workflow.trading.review-paper-candidate";
    public const string TradingOpenCockpit = "workflow.trading.open-cockpit";
    public const string TradingReviewExecutionControls = "workflow.trading.review-execution-controls";

    public const string PortfolioOpen = "workflow.portfolio.open";
    public const string PortfolioReviewAggregate = "workflow.portfolio.review-aggregate";
    public const string PortfolioReviewRunPortfolio = "workflow.portfolio.review-run-portfolio";
    public const string PortfolioReviewBrokerageSync = "workflow.portfolio.review-brokerage-sync";
    public const string PortfolioImportSnapshots = "workflow.portfolio.import-snapshots";

    public const string AccountingChooseContext = "workflow.accounting.choose-context";
    public const string AccountingConfigure = "workflow.accounting.configure";
    public const string AccountingReviewLiveHandoff = "workflow.accounting.review-live-handoff";
    public const string AccountingReviewReconciliation = "workflow.accounting.review-reconciliation-breaks";
    public const string AccountingReviewLedgerContinuity = "workflow.accounting.review-ledger-continuity";
    public const string AccountingReviewOperationsContinuity = "workflow.accounting.review-operations-continuity";
    public const string AccountingReviewCloseReadiness = "workflow.accounting.review-close-readiness";
    public const string AccountingOpen = "workflow.accounting.open";
    public const string AccountingReviewAuditTrail = "workflow.accounting.review-audit-trail";

    public const string ReportingOpen = "workflow.reporting.open";
    public const string ReportingApproveReportPack = "workflow.reporting.approve-report-pack";

    public const string EvidenceOpenPacket = "workflow.evidence.open-packet";
    public const string EvidenceValidate = "workflow.evidence.validate";
    public const string EvidenceExportManifest = "workflow.evidence.export-manifest";

    public const string DataOpenProviderHealth = "workflow.data.open-provider-health";
    public const string DataOpenBackfillQueue = "workflow.data.open-backfill-queue";
    public const string DataOpenQueueOverview = "workflow.data.open-queue-overview";
    public const string DataReviewSecurityMaster = "workflow.data.review-security-master";

    public const string SettingsOpen = "workflow.settings.open";
}
