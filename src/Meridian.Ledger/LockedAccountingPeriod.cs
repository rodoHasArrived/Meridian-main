namespace Meridian.Ledger;

/// <summary>
/// Immutable audit record for a book-scoped accounting period that rejects later postings.
/// </summary>
public sealed record LockedAccountingPeriod
{
    public LockedAccountingPeriod(
        LedgerBookKey ledgerKey,
        string periodId,
        DateTimeOffset startsAtInclusive,
        DateTimeOffset endsAtInclusive,
        DateTimeOffset lockedAtUtc,
        string lockedBy,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(ledgerKey);
        if (string.IsNullOrWhiteSpace(periodId))
            throw new ArgumentException("Period identifier must not be null or whitespace.", nameof(periodId));
        if (endsAtInclusive < startsAtInclusive)
            throw new ArgumentException("Period end must be greater than or equal to period start.", nameof(endsAtInclusive));
        if (string.IsNullOrWhiteSpace(lockedBy))
            throw new ArgumentException("Lock owner must not be null or whitespace.", nameof(lockedBy));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Lock reason must not be null or whitespace.", nameof(reason));

        LedgerKey = ledgerKey.Normalize();
        PeriodId = periodId.Trim();
        StartsAtInclusive = startsAtInclusive;
        EndsAtInclusive = endsAtInclusive;
        LockedAtUtc = lockedAtUtc.ToUniversalTime();
        LockedBy = lockedBy.Trim();
        Reason = reason.Trim();
    }

    public LedgerBookKey LedgerKey { get; }

    public string PeriodId { get; }

    public DateTimeOffset StartsAtInclusive { get; }

    public DateTimeOffset EndsAtInclusive { get; }

    public DateTimeOffset LockedAtUtc { get; }

    public string LockedBy { get; }

    public string Reason { get; }

    public bool Contains(DateTimeOffset timestamp)
        => timestamp >= StartsAtInclusive && timestamp <= EndsAtInclusive;
}
