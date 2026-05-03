namespace Meridian.Domain.Reconciliation;

public sealed class StatementReconciliationAggregate
{
    public required StatementBatchMetadata Batch { get; init; }
    public List<NormalizedStatementRow> Rows { get; } = [];
    public List<ReconciliationMatchLink> MatchLinks { get; } = [];
    public List<ReconciliationCase> Cases { get; } = [];
}

public sealed record StatementBatchMetadata(
    string ImportId,
    string Broker,
    string Account,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset ImportedAtUtc,
    string SourceHash,
    string SourceKind,
    string SourcePath);

public enum StatementRowKind { Position, CashBalance, Transaction, Fee, Dividend }

public sealed record NormalizedStatementRow(
    string RowId,
    StatementRowKind Kind,
    string Symbol,
    decimal Quantity,
    decimal Amount,
    DateTimeOffset EffectiveAtUtc,
    string Currency,
    string Fingerprint,
    IReadOnlyDictionary<string, string> RawSnapshot);

public sealed record ReconciliationMatchLink(
    string RowId,
    string? PositionId,
    string? LedgerEntryId,
    string? OrderId,
    string? FillId,
    string? SessionEvidenceId,
    string? RunEvidenceId,
    string Confidence,
    string Rationale);

public enum ReconciliationCaseState { Open, InReview, Resolved, Waived }

public sealed record ReconciliationCaseEvent(
    DateTimeOffset OccurredAtUtc,
    string OperatorId,
    ReconciliationCaseState NewState,
    string Note);
