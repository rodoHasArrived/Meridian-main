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
/// <param name="TenantId">Immutable tenant owner of the snapshot.</param>
/// <param name="CompanyId">Immutable company owner of the snapshot.</param>
/// <param name="FundProfileId">Immutable fund-profile owner of the snapshot.</param>
/// <param name="LedgerBookId">Immutable ledger-book owner of the snapshot.</param>
/// <param name="EntityId">Immutable legal/reporting entity owner of the snapshot.</param>
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
    DateTimeOffset AsOf,
    string? TenantId = null,
    string? CompanyId = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    string? EntityId = null);

/// <summary>
/// Immutable accounting-owner scope used to retrieve a position snapshot without crossing a
/// tenant, company, fund, ledger-book, or entity boundary.
/// </summary>
public sealed record PositionSnapshotOwnerScope(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string EntityId);

/// <summary>
/// Result of an atomic position-snapshot conditional append.
/// </summary>
public enum PositionSnapshotSaveOutcome
{
    /// <summary>The proposed snapshot was durably appended.</summary>
    Appended = 0,

    /// <summary>An equivalent snapshot already occupied the logical timestamp.</summary>
    EquivalentAlreadyExists = 1
}

/// <summary>
/// Raised when a position-snapshot partition already contains a different payload at the same
/// logical timestamp. Callers must fail closed rather than selecting a winner by append order.
/// </summary>
public sealed class PositionSnapshotConflictException : InvalidOperationException
{
    public PositionSnapshotConflictException(string runId, string accountId, DateTimeOffset asOf)
        : base(
            $"Position snapshot scope '{runId.Trim()}|{accountId.Trim()}' contains a different " +
            $"payload at the same source timestamp {asOf.ToUniversalTime():O}.")
    {
        RunId = runId;
        AccountId = accountId;
        AsOf = asOf.ToUniversalTime();
    }

    public string RunId { get; }

    public string AccountId { get; }

    public DateTimeOffset AsOf { get; }
}

/// <summary>
/// Defines the canonical equivalence used by snapshot stores and their callers. Position order and
/// symbol casing do not change the payload; owner, balance, P&amp;L, and position-value differences do.
/// </summary>
public static class PositionSnapshotEquivalence
{
    public static bool AreEquivalent(AccountSnapshotRecord left, AccountSnapshotRecord right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!TrimmedEquals(left.RunId, right.RunId, StringComparison.OrdinalIgnoreCase) ||
            !TrimmedEquals(left.AccountId, right.AccountId, StringComparison.OrdinalIgnoreCase) ||
            !TrimmedEquals(left.AccountDisplayName, right.AccountDisplayName, StringComparison.Ordinal) ||
            !TrimmedEquals(left.AccountKind, right.AccountKind, StringComparison.OrdinalIgnoreCase) ||
            left.Cash != right.Cash ||
            left.MarginBalance != right.MarginBalance ||
            left.UnrealisedPnl != right.UnrealisedPnl ||
            left.RealisedPnl != right.RealisedPnl ||
            left.AsOf.ToUniversalTime() != right.AsOf.ToUniversalTime() ||
            !TrimmedEquals(left.TenantId, right.TenantId, StringComparison.OrdinalIgnoreCase) ||
            !TrimmedEquals(left.CompanyId, right.CompanyId, StringComparison.OrdinalIgnoreCase) ||
            !TrimmedEquals(left.FundProfileId, right.FundProfileId, StringComparison.OrdinalIgnoreCase) ||
            left.LedgerBookId != right.LedgerBookId ||
            !TrimmedEquals(left.EntityId, right.EntityId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CanonicalPositions(left.Positions).SequenceEqual(CanonicalPositions(right.Positions));
    }

    private static IEnumerable<PositionRecord> CanonicalPositions(IReadOnlyList<PositionRecord>? positions)
        => (positions ?? [])
            .Select(static position => position with
            {
                Symbol = position.Symbol?.Trim().ToUpperInvariant() ?? string.Empty
            })
            .OrderBy(static position => position.Symbol, StringComparer.Ordinal)
            .ThenBy(static position => position.Quantity)
            .ThenBy(static position => position.CostBasis)
            .ThenBy(static position => position.UnrealisedPnl)
            .ThenBy(static position => position.RealisedPnl);

    private static bool TrimmedEquals(string? left, string? right, StringComparison comparison)
        => string.Equals(left?.Trim(), right?.Trim(), comparison);
}

/// <summary>
/// Provides crash-safe persistence for per-account portfolio snapshots.
/// Implementations must follow the WAL pattern (ADR-007) and respect the
/// <c>LifecyclePolicyEngine</c> tiered-storage rules (ADR-002).
/// </summary>
public interface IPositionSnapshotStore
{
    /// <summary>
    /// Persists an account snapshot with the same atomic equivalence/conflict semantics as
    /// <see cref="SaveSnapshotConditionallyAsync"/>. This retained compatibility method does not
    /// expose whether an equivalent retry was skipped.
    /// </summary>
    Task SaveSnapshotAsync(
        AccountSnapshotRecord snapshot,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically persists an account snapshot using the logical identity of run, account, exact
    /// owner scope, and UTC <see cref="AccountSnapshotRecord.AsOf"/>. An equivalent retry must be
    /// a no-op; a different payload at the same identity must throw
    /// <see cref="PositionSnapshotConflictException"/>. The check and append must be serialized
    /// for concurrent writers to the same durable partition. Implementations must resolve
    /// "latest" by the greatest <see cref="AccountSnapshotRecord.AsOf"/> value rather than physical
    /// append order.
    /// </summary>
    /// <remarks>
    /// The default implementation fails closed so an older store implementation cannot silently
    /// provide non-atomic behavior to a caller that requires a conditional append.
    /// </remarks>
    Task<PositionSnapshotSaveOutcome> SaveSnapshotConditionallyAsync(
        AccountSnapshotRecord snapshot,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This position snapshot store does not implement atomic conditional append semantics.");

    /// <summary>
    /// Retrieves the snapshot with the greatest <see cref="AccountSnapshotRecord.AsOf"/> value
    /// for the given run / account pair.
    /// Returns <see langword="null"/> when none exists.
    /// Equal-time conflicting legacy records must throw <see cref="PositionSnapshotConflictException"/>
    /// rather than return a file-order-dependent winner.
    /// </summary>
    Task<AccountSnapshotRecord?> GetLatestSnapshotAsync(string runId, string accountId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the snapshot with the greatest <see cref="AccountSnapshotRecord.AsOf"/> value
    /// owned by the exact accounting scope. Implementations must
    /// not fall back to an unowned or differently owned run/account snapshot.
    /// Equal-time conflicting legacy records must throw <see cref="PositionSnapshotConflictException"/>.
    /// </summary>
    Task<AccountSnapshotRecord?> GetLatestSnapshotAsync(
        string runId,
        string accountId,
        PositionSnapshotOwnerScope ownerScope,
        CancellationToken ct = default);

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

    /// <summary>
    /// Streams snapshots for the exact accounting owner between the inclusive UTC bounds.
    /// Implementations must not fall back to legacy unowned or differently owned history.
    /// </summary>
    IAsyncEnumerable<AccountSnapshotRecord> GetSnapshotHistoryAsync(
        string runId,
        string accountId,
        PositionSnapshotOwnerScope ownerScope,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}
