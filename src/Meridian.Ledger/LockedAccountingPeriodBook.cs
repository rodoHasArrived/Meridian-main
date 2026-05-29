namespace Meridian.Ledger;

/// <summary>
/// Book-scoped accounting period lock registry with guarded posting helpers.
/// </summary>
public sealed class LockedAccountingPeriodBook
{
    private readonly List<LockedAccountingPeriod> _lockedPeriods = [];

    public IReadOnlyList<LockedAccountingPeriod> LockedPeriods
        => _lockedPeriods
            .OrderBy(period => period.LedgerKey.ProjectId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(period => period.LedgerKey.LedgerBook, StringComparer.OrdinalIgnoreCase)
            .ThenBy(period => period.LedgerKey.LedgerView)
            .ThenBy(period => period.LedgerKey.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(period => period.StartsAtInclusive)
            .ToList();

    public LockedAccountingPeriod LockPeriod(
        LedgerBookKey ledgerKey,
        string periodId,
        DateTimeOffset startsAtInclusive,
        DateTimeOffset endsAtInclusive,
        DateTimeOffset lockedAtUtc,
        string lockedBy,
        string reason)
    {
        var candidate = new LockedAccountingPeriod(
            ledgerKey,
            periodId,
            startsAtInclusive,
            endsAtInclusive,
            lockedAtUtc,
            lockedBy,
            reason);

        if (_lockedPeriods.Any(period => SameLedgerKey(period.LedgerKey, candidate.LedgerKey)
                                         && string.Equals(period.PeriodId, candidate.PeriodId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Accounting period '{candidate.PeriodId}' is already locked for ledger book '{candidate.LedgerKey.LedgerBook}'.");
        }

        if (_lockedPeriods.Any(period => SameLedgerKey(period.LedgerKey, candidate.LedgerKey) && Overlaps(period, candidate)))
        {
            throw new InvalidOperationException(
                $"Accounting period '{candidate.PeriodId}' overlaps an existing locked period for ledger book '{candidate.LedgerKey.LedgerBook}'.");
        }

        _lockedPeriods.Add(candidate);
        return candidate;
    }

    public bool TryFindLock(LedgerBookKey ledgerKey, DateTimeOffset timestamp, out LockedAccountingPeriod? lockedPeriod)
    {
        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        lockedPeriod = _lockedPeriods.FirstOrDefault(period => SameLedgerKey(period.LedgerKey, normalizedKey) && period.Contains(timestamp));
        return lockedPeriod is not null;
    }

    public void EnsureCanPost(LedgerBookKey ledgerKey, JournalEntry journalEntry)
    {
        ArgumentNullException.ThrowIfNull(journalEntry);
        var normalizedKey = NormalizeLedgerKey(ledgerKey);

        if (!TryFindLock(normalizedKey, journalEntry.Timestamp, out var lockedPeriod))
            return;

        throw new LedgerValidationException(
            $"Accounting period '{lockedPeriod!.PeriodId}' for ledger book '{normalizedKey.LedgerBook}' is locked; " +
            $"journal '{journalEntry.JournalEntryId}' dated '{journalEntry.Timestamp:O}' cannot be posted. " +
            $"Locked by '{lockedPeriod.LockedBy}' at '{lockedPeriod.LockedAtUtc:O}'.");
    }

    public void Post(ProjectLedgerBook projectLedgerBook, LedgerBookKey ledgerKey, JournalEntry journalEntry)
    {
        ArgumentNullException.ThrowIfNull(projectLedgerBook);
        ArgumentNullException.ThrowIfNull(journalEntry);

        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        EnsureCanPost(normalizedKey, journalEntry);
        projectLedgerBook.GetOrCreate(normalizedKey).Post(journalEntry);
    }

    public void PostLines(
        ProjectLedgerBook projectLedgerBook,
        LedgerBookKey ledgerKey,
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines,
        JournalEntryMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(projectLedgerBook);

        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        if (TryFindLock(normalizedKey, timestamp, out var lockedPeriod))
        {
            throw new LedgerValidationException(
                $"Accounting period '{lockedPeriod!.PeriodId}' for ledger book '{normalizedKey.LedgerBook}' is locked; " +
                $"journal dated '{timestamp:O}' cannot be posted. Locked by '{lockedPeriod.LockedBy}' at '{lockedPeriod.LockedAtUtc:O}'.");
        }

        projectLedgerBook.GetOrCreate(normalizedKey).PostLines(timestamp, description, lines, metadata);
    }

    private static LedgerBookKey NormalizeLedgerKey(LedgerBookKey ledgerKey)
    {
        ArgumentNullException.ThrowIfNull(ledgerKey);
        return ledgerKey.Normalize();
    }

    private static bool Overlaps(LockedAccountingPeriod left, LockedAccountingPeriod right)
        => left.StartsAtInclusive <= right.EndsAtInclusive && right.StartsAtInclusive <= left.EndsAtInclusive;

    private static bool SameLedgerKey(LedgerBookKey left, LedgerBookKey right)
        => string.Equals(left.ProjectId, right.ProjectId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.LedgerBook, right.LedgerBook, StringComparison.OrdinalIgnoreCase)
           && left.LedgerView == right.LedgerView
           && string.Equals(left.ScenarioId, right.ScenarioId, StringComparison.OrdinalIgnoreCase);
}
