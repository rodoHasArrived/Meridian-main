using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>
/// Serialisable snapshot of a paper session's metadata.
/// Written atomically to <c>session.json</c> on every lifecycle change.
/// </summary>
public sealed record PersistedSessionRecord(
    string SessionId,
    string StrategyId,
    string? StrategyName,
    decimal InitialCash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    bool IsActive,
    List<string> Symbols);

/// <summary>
/// Lightweight serialisation DTO for a <see cref="Meridian.Ledger.LedgerAccount"/>.
/// </summary>
public sealed record PersistedLedgerAccountDto(
    string Name,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId);

/// <summary>
/// Lightweight serialisation DTO for a single debit/credit line in a journal entry.
/// </summary>
public sealed record PersistedLedgerLineDto(
    Guid EntryId,
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    PersistedLedgerAccountDto Account,
    decimal Debit,
    decimal Credit,
    string Description);

/// <summary>
/// Lightweight serialisation DTO for a complete journal entry plus its optional audit metadata.
/// Written one per line to <c>ledger.jsonl</c>.
/// </summary>
public sealed record PersistedJournalEntryDto(
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    string Description,
    IReadOnlyList<PersistedLedgerLineDto> Lines,
    string? ActivityType = null,
    string? Symbol = null,
    Guid? SecurityId = null,
    Guid? OrderId = null,
    string? LedgerView = null,
    string? StrategyId = null);

/// <summary>
/// Storage abstraction for paper trading session persistence.
/// Implementations may store session data in-memory, on the local file system,
/// or in any other durable backend.
/// </summary>
public interface IPaperSessionStore
{
    /// <summary>Creates or replaces the session metadata record.</summary>
    Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default);

    /// <summary>Appends a single fill event to the session's immutable fill log.</summary>
    Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default);

    /// <summary>Appends an order-state snapshot to the session's order history log.</summary>
    Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default);

    /// <summary>
    /// Writes the complete ledger journal for <paramref name="sessionId"/> to a JSONL sidecar
    /// file (<c>ledger.jsonl</c>).  Replaces any previously-written ledger for the same session.
    /// </summary>
    Task SaveLedgerJournalAsync(
        string sessionId,
        IReadOnlyList<PersistedJournalEntryDto> entries,
        CancellationToken ct = default);

    /// <summary>Returns metadata records for every persisted session.</summary>
    Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default);

    /// <summary>Returns all recorded fill events for <paramref name="sessionId"/>, in order.</summary>
    Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Returns the order-state history for <paramref name="sessionId"/>, in order.</summary>
    Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the persisted ledger journal entries for <paramref name="sessionId"/>.
    /// Returns an empty list when no ledger has been saved for this session.
    /// </summary>
    Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
        string sessionId,
        CancellationToken ct = default);
}
