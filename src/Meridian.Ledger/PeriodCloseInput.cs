namespace Meridian.Ledger;

/// <summary>
/// Input for projecting period-close closing entries from a point-in-time trial balance.
/// </summary>
public sealed record PeriodCloseInput
{
    public PeriodCloseInput(
        string periodId,
        DateTimeOffset closedAtUtc,
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
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

    public string PeriodId { get; }

    public DateTimeOffset ClosedAtUtc { get; }

    /// <summary>
    /// Point-in-time trial balance with normal-balance values (see
    /// <see cref="Ledger.TrialBalanceAsOf(DateTimeOffset, string?, LedgerLineDimensionSet?)"/>).
    /// </summary>
    public IReadOnlyDictionary<LedgerAccount, decimal> TrialBalance { get; }

    public string ClosedBy { get; }
}
