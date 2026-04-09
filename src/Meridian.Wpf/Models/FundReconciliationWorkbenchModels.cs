using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

public enum FundReconciliationQueueView : byte
{
    BreakQueue = 0,
    Runs = 1
}

public enum FundReconciliationBreakQueueFilter : byte
{
    Open = 0,
    InReview = 1,
    All = 2
}

public enum FundReconciliationScopeFilter : byte
{
    All = 0,
    Strategy = 1,
    Account = 2
}

public enum FundReconciliationSourceType : byte
{
    StrategyRun = 0,
    AccountRun = 1
}

public sealed record FundReconciliationWorkbenchSnapshot(
    ReconciliationSummary Summary,
    IReadOnlyList<FundReconciliationBreakQueueRow> BreakQueueItems,
    IReadOnlyList<FundReconciliationRunRow> RunRows,
    DateTimeOffset RefreshedAt,
    int InReviewBreakCount);

public sealed record FundReconciliationBreakQueueRow(
    string BreakId,
    string RunId,
    string StrategyName,
    string DisplayLabel,
    ReconciliationBreakQueueStatus Status,
    string StatusLabel,
    string StatusIcon,
    string CategoryLabel,
    decimal Variance,
    string VarianceText,
    string Reason,
    string AssignedToLabel,
    DateTimeOffset DetectedAt,
    string DetectedAtText,
    DateTimeOffset LastUpdatedAt,
    string LastUpdatedAtText,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAt = null,
    string? ResolvedBy = null,
    DateTimeOffset? ResolvedAt = null,
    string? ResolutionNote = null);

public sealed record FundReconciliationRunRow(
    string RowKey,
    Guid ReconciliationRunId,
    Guid AccountId,
    FundReconciliationSourceType SourceType,
    string ScopeLabel,
    string ScopeIcon,
    string PrimaryLabel,
    string SecondaryLabel,
    string Status,
    string StatusLabel,
    string StatusIcon,
    DateOnly AsOfDate,
    string AsOfDateText,
    int TotalChecks,
    int TotalMatched,
    int TotalBreaks,
    decimal BreakAmountTotal,
    string BreakAmountText,
    DateTimeOffset RequestedAt,
    string RequestedAtText,
    DateTimeOffset? CompletedAt,
    string CompletedAtText,
    int SecurityIssueCount,
    bool HasSecurityCoverageIssues,
    string CoverageLabel,
    string? StrategyName = null,
    string? RunId = null)
{
    public bool HasOpenExceptions =>
        TotalBreaks > 0 ||
        HasSecurityCoverageIssues ||
        !string.Equals(Status, "Matched", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "Resolved", StringComparison.OrdinalIgnoreCase);
}

public sealed record FundReconciliationDetailModel(
    FundReconciliationSourceType SourceType,
    string DetailKey,
    string Title,
    string Subtitle,
    string StatusLabel,
    string CoverageSummary,
    string LastUpdatedText,
    Guid? AccountId,
    Guid ReconciliationRunId,
    string? RunId,
    string? FocusBreakId,
    bool SupportsBreakActions,
    int TotalChecks,
    int TotalMatched,
    int TotalBreaks,
    decimal BreakAmountTotal,
    int SecurityIssueCount,
    string EmptyExceptionsText,
    string EmptySecurityCoverageText,
    IReadOnlyList<FundReconciliationCheckDetailRow> ExceptionRows,
    IReadOnlyList<FundReconciliationCheckDetailRow> AllCheckRows,
    IReadOnlyList<FundReconciliationSecurityCoverageRow> SecurityCoverageRows,
    IReadOnlyList<FundReconciliationAuditTrailRow> AuditRows);

public sealed record FundReconciliationCheckDetailRow(
    string RowKey,
    string CheckLabel,
    string CategoryLabel,
    string StatusLabel,
    string StatusIcon,
    string SourceLabel,
    string ExpectedAmountText,
    string ActualAmountText,
    string VarianceText,
    string Reason,
    string ExpectedAsOfText,
    string ActualAsOfText,
    bool IsHighlighted = false);

public sealed record FundReconciliationSecurityCoverageRow(
    string Source,
    string Symbol,
    string AccountName,
    string Reason);

public sealed record FundReconciliationAuditTrailRow(
    DateTimeOffset Timestamp,
    string TimestampText,
    string Title,
    string Description,
    string ActorLabel);
