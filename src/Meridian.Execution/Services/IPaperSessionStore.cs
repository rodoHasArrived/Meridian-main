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

    /// <summary>Returns metadata records for every persisted session.</summary>
    Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default);

    /// <summary>Returns all recorded fill events for <paramref name="sessionId"/>, in order.</summary>
    Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Returns the order-state history for <paramref name="sessionId"/>, in order.</summary>
    Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default);
}
