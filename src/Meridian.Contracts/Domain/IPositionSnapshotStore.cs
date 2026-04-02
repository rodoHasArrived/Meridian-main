namespace Meridian.Contracts.Domain;

/// <summary>
/// Serialisation-friendly snapshot of a single position for persistence.
/// Used by <see cref="IPositionSnapshotStore"/> — deliberately flat so that the
/// storage layer has no dependency on <c>Meridian.Execution</c> types.
/// </summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="Quantity">Signed quantity (positive = long, negative = short).</param>
/// <param name="CostBasis">Average cost per share.</param>
/// <param name="UnrealisedPnl">Mark-to-market unrealised P&amp;L.</param>
/// <param name="RealisedPnl">Cumulative realised P&amp;L.</param>
public sealed record PositionRecord(
    string Symbol,
    decimal Quantity,
    decimal CostBasis,
    decimal UnrealisedPnl,
    decimal RealisedPnl);

/// <summary>
/// Serialisation-friendly per-account portfolio snapshot persisted by
/// <see cref="IPositionSnapshotStore"/>.
/// </summary>
/// <param name="RunId">Strategy run identifier.</param>
/// <param name="AccountId">Account identifier within the run.</param>
/// <param name="AccountDisplayName">Human-readable account name.</param>
/// <param name="AccountKind">Brokerage or Bank (string value from <c>AccountKind</c> enum).</param>
/// <param name="Cash">Cash balance at snapshot time.</param>
/// <param name="MarginBalance">Margin used (0 for cash accounts).</param>
/// <param name="UnrealisedPnl">Aggregate unrealised P&amp;L.</param>
/// <param name="RealisedPnl">Cumulative realised P&amp;L.</param>
/// <param name="Positions">All open positions at snapshot time.</param>
/// <param name="AsOf">UTC timestamp of the snapshot.</param>
public sealed record AccountSnapshotRecord(
    string RunId,
    string AccountId,
    string AccountDisplayName,
    string AccountKind,
    decimal Cash,
    decimal MarginBalance,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    IReadOnlyList<PositionRecord> Positions,
    DateTimeOffset AsOf);

/// <summary>
/// Provides crash-safe persistence for per-account portfolio snapshots.
/// Implementations must follow the WAL pattern (ADR-007) and respect the
/// <c>LifecyclePolicyEngine</c> tiered-storage rules (ADR-002).
/// </summary>
public interface IPositionSnapshotStore
{
    /// <summary>
    /// Persists an account snapshot.  Idempotent — latest write wins.
    /// </summary>
    Task SaveSnapshotAsync(AccountSnapshotRecord snapshot, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent snapshot for the given run / account pair.
    /// Returns <see langword="null"/> when none exists.
    /// </summary>
    Task<AccountSnapshotRecord?> GetLatestSnapshotAsync(string runId, string accountId, CancellationToken ct = default);

    /// <summary>
    /// Streams all snapshots for the given run / account pair between
    /// <paramref name="from"/> and <paramref name="to"/> (both inclusive, UTC).
    /// </summary>
    IAsyncEnumerable<AccountSnapshotRecord> GetSnapshotHistoryAsync(
        string runId,
        string accountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}
