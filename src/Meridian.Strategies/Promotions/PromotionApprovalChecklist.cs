using Meridian.Strategies.Models;

namespace Meridian.Strategies.Promotions;

/// <summary>
/// Canonical checklist items that make promotion approvals traceable across
/// Strategy, Trading, and operator review.
/// </summary>
public static class PromotionApprovalChecklist
{
    public const string Dk1TrustPacketReviewed = "DK1_TRUST_PACKET_REVIEWED";
    public const string RunLineageReviewed = "RUN_LINEAGE_REVIEWED";
    public const string PortfolioLedgerContinuityReviewed = "PORTFOLIO_LEDGER_CONTINUITY_REVIEWED";
    public const string RiskControlsReviewed = "RISK_CONTROLS_REVIEWED";
    public const string PaperValidationReviewed = "PAPER_VALIDATION_REVIEWED";
    public const string ReconciliationEvidenceReviewed = "RECONCILIATION_EVIDENCE_REVIEWED";
    public const string BrokerExecutionReconciliationReviewed = "BROKER_EXECUTION_RECONCILIATION_REVIEWED";
    public const string AccountingRecordsReviewed = "ACCOUNTING_RECORDS_REVIEWED";
    public const string GovernedReportingReviewed = "GOVERNED_REPORTING_REVIEWED";
    public const string GovernanceSignoffReviewed = "GOVERNANCE_SIGNOFF_REVIEWED";
    public const string ExceptionHandlingReviewed = "EXCEPTION_HANDLING_REVIEWED";
    public const string RollbackKillSwitchReviewed = "ROLLBACK_KILL_SWITCH_REVIEWED";
    public const string AuditRetentionReviewed = "AUDIT_RETENTION_REVIEWED";
    public const string LiveOverrideReviewed = "LIVE_OVERRIDE_REVIEWED";

    private static readonly string[] PaperRequiredItems =
    [
        Dk1TrustPacketReviewed,
        RunLineageReviewed,
        PortfolioLedgerContinuityReviewed,
        RiskControlsReviewed
    ];

    private static readonly string[] LiveRequiredItems =
    [
        Dk1TrustPacketReviewed,
        RunLineageReviewed,
        PortfolioLedgerContinuityReviewed,
        RiskControlsReviewed,
        PaperValidationReviewed,
        ReconciliationEvidenceReviewed,
        BrokerExecutionReconciliationReviewed,
        AccountingRecordsReviewed,
        GovernedReportingReviewed,
        GovernanceSignoffReviewed,
        ExceptionHandlingReviewed,
        RollbackKillSwitchReviewed,
        AuditRetentionReviewed,
        LiveOverrideReviewed
    ];

    public static string[] CreateRequiredFor(RunType targetRunType)
        => targetRunType == RunType.Live ? [.. LiveRequiredItems] : [.. PaperRequiredItems];

    public static string[] Normalize(IEnumerable<string>? items)
        => items is null
            ? []
            : items
                .Select(NormalizeItem)
                .Where(static item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    public static string[] GetMissingRequiredItems(RunType targetRunType, IEnumerable<string>? items)
    {
        var provided = Normalize(items).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CreateRequiredFor(targetRunType)
            .Where(required => !provided.Contains(required))
            .ToArray();
    }

    private static string NormalizeItem(string? item)
        => string.IsNullOrWhiteSpace(item)
            ? string.Empty
            : item.Trim().Replace(' ', '_').Replace('-', '_').ToUpperInvariant();
}
