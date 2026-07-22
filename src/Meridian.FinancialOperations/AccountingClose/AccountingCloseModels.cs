using System.Collections.Immutable;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public enum ClosePeriodState
{
    Open,
    Validating,
    Blocked,
    Closed
}

public sealed record LedgerDefinition(
    string LedgerId,
    string DisplayName,
    string FunctionalCurrency,
    string ReportingCurrency);

public sealed record FxRate(
    string FromCurrency,
    string ToCurrency,
    DateOnly RateDate,
    decimal Rate,
    string SourceEventId,
    string RateId = "",
    DateTimeOffset? ObservedAt = null);

public sealed record JournalLine(
    string AccountCode,
    decimal Amount,
    string Currency,
    bool IsDebit,
    string SourceEventId = "",
    string ApprovalId = "",
    string Memo = "",
    LedgerDimensionSetDto? Dimensions = null);

public sealed record JournalEntry(
    Guid JournalEntryId,
    string LedgerId,
    DateOnly TradeDate,
    string SourceEventId,
    string Description,
    ImmutableArray<JournalLine> Lines,
    DateOnly? AccountingPeriod = null,
    DateTimeOffset? CreatedAt = null,
    string CreatedBy = "system");

public sealed record TranslationAdjustment(
    Guid AdjustmentId,
    string LedgerId,
    DateOnly Period,
    string AccountCode,
    decimal FunctionalAmount,
    decimal ReportingAmount,
    decimal AdjustmentAmount,
    string SourceEventId,
    string FromCurrency = "",
    string ToCurrency = "",
    string RateId = "",
    string ApprovalId = "",
    LedgerDimensionSetDto? Dimensions = null);

public sealed record CloseEvidenceCheck(
    string CheckId,
    string Label,
    bool Required,
    bool Passed,
    string SourceEventId,
    string ApprovalId,
    string Detail);

public sealed record CloseEvidence(
    bool TrialBalanceSignedOff,
    bool ReconciliationSignedOff,
    bool ApprovalsCompleted,
    ImmutableArray<CloseEvidenceCheck> Checks = default)
{
    public ImmutableArray<CloseEvidenceCheck> NormalizedChecks => Checks.IsDefault ? ImmutableArray<CloseEvidenceCheck>.Empty : Checks;
}

public sealed record CloseApprovalHistoryEntry(
    string ApprovalId,
    string SourceEventId,
    Guid? JournalEntryId,
    string Label,
    DateOnly? AccountingPeriod,
    ImmutableArray<string> AccountCodes,
    bool Required,
    bool Completed,
    string Detail);

public sealed record CloseEvidencePackage(
    string PackageId,
    string LedgerId,
    DateOnly Period,
    ClosePeriodState State,
    bool IsLocked,
    bool TrialBalanceBalanced,
    DateTimeOffset? LockedAt,
    string? ClosedBy,
    ImmutableArray<CloseEvidenceCheck> EvidenceChecks,
    ImmutableArray<CloseApprovalHistoryEntry> ApprovalHistory,
    ImmutableArray<string> SourceEventIds,
    ImmutableArray<string> ApprovalIds,
    ImmutableArray<string> Blockers,
    string EvidenceHash);

public sealed record ClosePeriod(
    string LedgerId,
    DateOnly Period,
    ClosePeriodState State,
    CloseEvidence Evidence,
    ImmutableArray<string> Blockers,
    DateTimeOffset? LockedAt = null,
    string? ClosedBy = null)
{
    public bool IsLocked => State == ClosePeriodState.Closed || LockedAt is not null;
}

public sealed record TrialBalanceLine(
    string AccountCode,
    decimal Debit,
    decimal Credit,
    decimal Net,
    ImmutableArray<string> SourceEventIds = default,
    ImmutableArray<string> ApprovalIds = default,
    LedgerDimensionSetDto? Dimensions = null);

public sealed record RollForwardLine(
    string AccountCode,
    decimal OpeningBalance,
    decimal Activity,
    decimal TranslationAdjustment,
    decimal ClosingBalance,
    ImmutableArray<string> SourceEventIds = default,
    ImmutableArray<string> ApprovalIds = default,
    LedgerDimensionSetDto? Dimensions = null);

public sealed record SourceLinkedAuditLine(
    Guid JournalEntryId,
    string SourceEventId,
    string ApprovalId,
    DateOnly? AccountingPeriod = null,
    string Description = "",
    ImmutableArray<string> AccountCodes = default);

public sealed record JournalPostingResult(
    string LedgerId,
    ImmutableArray<JournalEntry> PostedEntries,
    ImmutableArray<SourceLinkedAuditLine> AuditLines,
    ImmutableArray<string> RejectedReasons)
{
    public bool Accepted => RejectedReasons.IsDefaultOrEmpty;
}
