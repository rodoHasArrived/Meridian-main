using System.Collections.Immutable;

namespace Meridian.Application.AccountingClose;

public enum ClosePeriodState
{
    Open,
    Validating,
    Blocked,
    Closed
}

public sealed record FxRate(string FromCurrency, string ToCurrency, DateOnly RateDate, decimal Rate, string SourceEventId);

public sealed record JournalLine(string AccountCode, decimal Amount, string Currency, bool IsDebit);

public sealed record JournalEntry(
    Guid JournalEntryId,
    string LedgerId,
    DateOnly TradeDate,
    string SourceEventId,
    string Description,
    ImmutableArray<JournalLine> Lines);

public sealed record TranslationAdjustment(
    Guid AdjustmentId,
    string LedgerId,
    DateOnly Period,
    string AccountCode,
    decimal FunctionalAmount,
    decimal ReportingAmount,
    decimal AdjustmentAmount,
    string SourceEventId);

public sealed record CloseEvidence(bool TrialBalanceSignedOff, bool ReconciliationSignedOff, bool ApprovalsCompleted);

public sealed record ClosePeriod(
    string LedgerId,
    DateOnly Period,
    ClosePeriodState State,
    CloseEvidence Evidence,
    ImmutableArray<string> Blockers);

public sealed record TrialBalanceLine(string AccountCode, decimal Debit, decimal Credit, decimal Net);
public sealed record RollForwardLine(string AccountCode, decimal OpeningBalance, decimal Activity, decimal TranslationAdjustment, decimal ClosingBalance);
public sealed record SourceLinkedAuditLine(Guid JournalEntryId, string SourceEventId, string ApprovalId);
