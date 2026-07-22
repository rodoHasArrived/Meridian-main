namespace Meridian.Ledger;

/// <summary>
/// One trial-balance row feeding period-close projection: an account, its normal-balance
/// value, and the optional dimensional scope (entity, sleeve, ...) it was posted under.
/// Rows with the same account but different dimensions are closed independently so
/// entity/sleeve P&amp;L and retained earnings stay dimension-scoped.
/// </summary>
public sealed record PeriodCloseAccountBalance(
    LedgerAccount Account,
    decimal Balance,
    LedgerLineDimensionSet? Dimensions = null);

/// <summary>
/// Input for projecting period-close closing entries from a point-in-time trial balance.
/// </summary>
public sealed record PeriodCloseInput
{
    public PeriodCloseInput(
        string periodId,
        DateTimeOffset closedAtUtc,
        IReadOnlyList<PeriodCloseAccountBalance> trialBalance,
        string closedBy)
    {
        ArgumentNullException.ThrowIfNull(trialBalance);
        if (string.IsNullOrWhiteSpace(periodId))
            throw new ArgumentException("Period identifier must not be null or whitespace.", nameof(periodId));
        if (string.IsNullOrWhiteSpace(closedBy))
            throw new ArgumentException("Close actor must not be null or whitespace.", nameof(closedBy));

        PeriodId = periodId.Trim();
        ClosedAtUtc = closedAtUtc.ToUniversalTime();
        TrialBalance = trialBalance;
        ClosedBy = closedBy.Trim();
    }

    /// <summary>
    /// Convenience overload for a dimension-flat trial balance (for example the in-memory
    /// <see cref="Ledger.TrialBalanceAsOf(DateTimeOffset, string?, LedgerLineDimensionSet?)"/>),
    /// which carries no per-line dimensional scope.
    /// </summary>
    public PeriodCloseInput(
        string periodId,
        DateTimeOffset closedAtUtc,
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        string closedBy)
        : this(
            periodId,
            closedAtUtc,
            (trialBalance ?? throw new ArgumentNullException(nameof(trialBalance)))
                .Select(static pair => new PeriodCloseAccountBalance(pair.Key, pair.Value))
                .ToArray(),
            closedBy)
    {
    }

    public string PeriodId { get; }

    public DateTimeOffset ClosedAtUtc { get; }

    /// <summary>
    /// Point-in-time trial balance with normal-balance values, one row per
    /// account + dimensional scope.
    /// </summary>
    public IReadOnlyList<PeriodCloseAccountBalance> TrialBalance { get; }

    public string ClosedBy { get; }
}
