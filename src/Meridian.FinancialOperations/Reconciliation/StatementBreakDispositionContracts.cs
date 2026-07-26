using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Authenticated operator command that dispositions a statement break and its paired case.
/// <paramref name="Actor"/> must be supplied from trusted authentication context rather than
/// request payload data.
/// </summary>
public sealed record StatementBreakDispositionCommand(
    string BreakId,
    long ExpectedVersion,
    string CommandId,
    ReconciliationBreakDispositionDto Disposition,
    string Actor,
    string Rationale,
    IReadOnlyList<string> EvidenceLinks,
    string? SupersedingBreakId = null);

public enum StatementBreakDispositionOutcome : byte
{
    Applied = 0,
    Resumed = 1,
    IdempotentReplay = 2,
    RecoveryPending = 3,
    NotFound = 4,
    VersionConflict = 5,
    CommandConflict = 6,
    Rejected = 7
}

public enum StatementBreakDispositionTransactionState : byte
{
    Prepared = 0,
    BreakApplied = 1,
    CaseApplied = 2,
    Completed = 3
}

/// <summary>
/// Immutable decision audit entry. Entry hashes form one sequence across the disposition
/// authority snapshot.
/// </summary>
public sealed record StatementBreakDispositionAuditEntry(
    string AuditId,
    long Sequence,
    string TransactionId,
    string CommandId,
    string BreakId,
    string CaseId,
    long Version,
    ReconciliationBreakDispositionDto Disposition,
    string Actor,
    string Rationale,
    IReadOnlyList<string> EvidenceLinks,
    DateTimeOffset OccurredAtUtc,
    string? PreviousHash,
    string EntryHash);

/// <summary>
/// Retained command binding used to distinguish an exact retry from reuse of a command id with
/// materially different input.
/// </summary>
public sealed record StatementBreakDispositionCommandReceipt(
    string CommandId,
    string BreakId,
    string TransactionId,
    string InputHashSha256,
    long ExpectedVersion,
    DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Durable transaction authority. The two after-images are committed before either source
/// projection is materialized.
/// </summary>
public sealed record StatementBreakDispositionTransaction(
    string TransactionId,
    string CommandId,
    string InputHashSha256,
    string BreakId,
    string CaseId,
    long ExpectedVersion,
    long Version,
    ReconciliationBreakDispositionDto Disposition,
    string Actor,
    string Rationale,
    IReadOnlyList<string> EvidenceLinks,
    string EvidenceHashSha256,
    string? SupersedingBreakId,
    ReconciliationBreakRecord BreakAfter,
    ReconciliationCase CaseAfter,
    StatementBreakDispositionTransactionState State,
    DateTimeOffset PreparedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int ProjectionAttemptCount,
    DateTimeOffset? LastProjectionAttemptAtUtc,
    string? LastError);

/// <summary>
/// Complete durable state held in the single atomic disposition authority file.
/// </summary>
public sealed record StatementBreakDispositionTransactionSnapshot(
    int SchemaVersion,
    IReadOnlyList<StatementBreakDispositionTransaction> Transactions,
    IReadOnlyList<StatementBreakDispositionCommandReceipt> CommandReceipts,
    IReadOnlyList<StatementBreakDispositionAuditEntry> AuditHistory,
    string? ContentHashSha256)
{
    public static StatementBreakDispositionTransactionSnapshot Empty { get; } = new(
        SchemaVersion: 1,
        Transactions: [],
        CommandReceipts: [],
        AuditHistory: [],
        ContentHashSha256: null);
}

public sealed record StatementBreakDispositionResult(
    StatementBreakDispositionOutcome Outcome,
    string BreakId,
    string? CaseId,
    string? TransactionId,
    string CommandId,
    long Version,
    ReconciliationBreakDispositionDto? Disposition,
    string? Actor,
    string? Rationale,
    IReadOnlyList<string>? EvidenceLinks,
    DateTimeOffset? DisposedAtUtc,
    ReconciliationBreakRecord? Break,
    ReconciliationCase? Case,
    IReadOnlyList<StatementBreakDispositionAuditEntry>? AuditHistory,
    string? Error = null);

public interface IStatementBreakDispositionService
{
    Task<StatementBreakDispositionResult> DispositionAsync(
        StatementBreakDispositionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes source-store projection for every transaction committed before an interrupted
    /// process stopped. Returns the number advanced to <see cref="StatementBreakDispositionTransactionState.Completed"/>.
    /// </summary>
    Task<int> ResumePendingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StatementBreakDispositionAuditEntry>> GetAuditHistoryAsync(
        string breakId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exclusive session held under both an in-process gate and an operating-system file lease.
/// Each save atomically replaces the complete authority snapshot.
/// </summary>
public interface IStatementBreakDispositionTransactionSession
{
    StatementBreakDispositionTransactionSnapshot Snapshot { get; }

    Task SaveAsync(
        StatementBreakDispositionTransactionSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public interface IStatementBreakDispositionTransactionStore
{
    Task<TResult> ExecuteExclusiveAsync<TResult>(
        Func<IStatementBreakDispositionTransactionSession, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task<StatementBreakDispositionTransactionSnapshot> ReadAsync(
        CancellationToken cancellationToken = default);
}
