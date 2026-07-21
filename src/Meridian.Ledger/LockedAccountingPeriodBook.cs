namespace Meridian.Ledger;

/// <summary>
/// Book-scoped accounting period lock registry with guarded posting helpers.
/// </summary>
/// <remarks>
/// Access to the internal lock and reopen collections is synchronized so the book is safe to share
/// across threads — consistent with <see cref="JournalTemplateBook"/> and
/// <see cref="PortfolioPricingRuleBook"/>. Composite operations hold the lock for their whole body so
/// they stay atomic: locking and reopening are atomic check-then-act, and <see cref="Post"/> /
/// <see cref="PostLines"/> perform their lock check and the ledger post under a single lock
/// acquisition, so a period cannot be locked between the check and the post and admit a journal that
/// should have been blocked. Internal <c>*Locked</c> helpers assume the caller already holds the lock,
/// which keeps the composite paths free of the non-reentrant re-lock that would otherwise deadlock.
/// </remarks>
public sealed class LockedAccountingPeriodBook
{
    private readonly object _gate = new();
    private readonly List<LockedAccountingPeriod> _lockedPeriods = [];
    private readonly List<ReopenedAccountingPeriod> _reopenHistory = [];

    public IReadOnlyList<LockedAccountingPeriod> LockedPeriods
    {
        get
        {
            lock (_gate)
            {
                return _lockedPeriods
                    .OrderBy(period => period.LedgerKey.ProjectId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(period => period.LedgerKey.LedgerBook, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(period => period.LedgerKey.LedgerView)
                    .ThenBy(period => period.LedgerKey.ScenarioId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(period => period.StartsAtInclusive)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Chronological audit trail of every period reopen, newest actions appended last. A reopen never
    /// erases history; the original lock and its reopen evidence both remain queryable here.
    /// </summary>
    public IReadOnlyList<ReopenedAccountingPeriod> ReopenHistory
    {
        get
        {
            lock (_gate)
            {
                return _reopenHistory.ToList();
            }
        }
    }

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

        lock (_gate)
        {
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
        }

        return candidate;
    }

    public bool TryFindLock(LedgerBookKey ledgerKey, DateTimeOffset timestamp, out LockedAccountingPeriod? lockedPeriod)
    {
        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        lock (_gate)
        {
            lockedPeriod = FindLockLocked(normalizedKey, timestamp);
        }

        return lockedPeriod is not null;
    }

    /// <summary>Returns <see langword="true"/> when a period with the given id is currently locked for the book.</summary>
    public bool IsLocked(LedgerBookKey ledgerKey, string periodId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodId);
        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        var normalizedPeriodId = periodId.Trim();
        lock (_gate)
        {
            return _lockedPeriods.Any(period => SameLedgerKey(period.LedgerKey, normalizedKey)
                                                && string.Equals(period.PeriodId, normalizedPeriodId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Reopens a currently-locked accounting period, removing its posting guard so corrections can be
    /// posted again, and records the reopen — with its supporting <paramref name="evidence"/> — in
    /// <see cref="ReopenHistory"/>. The period may be re-locked afterwards via <see cref="LockPeriod"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the period is not currently locked, or when the reopen predates the approval that
    /// authorizes it (<see cref="PeriodReopenEvidence.ApprovedAtUtc"/> later than
    /// <paramref name="reopenedAtUtc"/>).
    /// </exception>
    public ReopenedAccountingPeriod ReopenPeriod(
        LedgerBookKey ledgerKey,
        string periodId,
        DateTimeOffset reopenedAtUtc,
        string reopenedBy,
        PeriodReopenEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reopenedBy);
        ArgumentNullException.ThrowIfNull(evidence);

        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        var normalizedPeriodId = periodId.Trim();
        var reopenedAt = reopenedAtUtc.ToUniversalTime();

        // A reopen must not predate the approval that authorizes it; otherwise corrections could be
        // posted into the period before the approving evidence exists.
        if (evidence.ApprovedAtUtc > reopenedAt)
        {
            throw new InvalidOperationException(
                $"Accounting period '{normalizedPeriodId}' cannot be reopened at '{reopenedAt:O}' because its " +
                $"approval '{evidence.ReopenId}' is dated '{evidence.ApprovedAtUtc:O}'; the authorizing approval " +
                "must be no later than the reopen.");
        }

        lock (_gate)
        {
            var lockedPeriod = _lockedPeriods.FirstOrDefault(period =>
                SameLedgerKey(period.LedgerKey, normalizedKey)
                && string.Equals(period.PeriodId, normalizedPeriodId, StringComparison.OrdinalIgnoreCase));

            if (lockedPeriod is null)
            {
                throw new InvalidOperationException(
                    $"Accounting period '{normalizedPeriodId}' is not locked for ledger book '{normalizedKey.LedgerBook}'; nothing to reopen.");
            }

            // A reopen must not predate the lock it unwinds, or the history would record the period as
            // reopened before it was ever locked. This complements the approval-time gate above (the
            // approval can itself be earlier than the lock).
            if (reopenedAt < lockedPeriod.LockedAtUtc)
            {
                throw new InvalidOperationException(
                    $"Accounting period '{normalizedPeriodId}' cannot be reopened at '{reopenedAt:O}' because that " +
                    $"precedes when it was locked ('{lockedPeriod.LockedAtUtc:O}'); a period cannot be reopened before it was locked.");
            }

            _lockedPeriods.Remove(lockedPeriod);

            var reopened = new ReopenedAccountingPeriod(
                lockedPeriod,
                reopenedAt,
                reopenedBy.Trim(),
                evidence);
            _reopenHistory.Add(reopened);
            return reopened;
        }
    }

    public void EnsureCanPost(LedgerBookKey ledgerKey, JournalEntry journalEntry)
    {
        ArgumentNullException.ThrowIfNull(journalEntry);
        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        lock (_gate)
        {
            EnsureCanPostLocked(normalizedKey, journalEntry);
        }
    }

    public void Post(ProjectLedgerBook projectLedgerBook, LedgerBookKey ledgerKey, JournalEntry journalEntry)
    {
        ArgumentNullException.ThrowIfNull(projectLedgerBook);
        ArgumentNullException.ThrowIfNull(journalEntry);

        var normalizedKey = NormalizeLedgerKey(ledgerKey);
        // Hold the lock across the check and the post so a concurrent LockPeriod cannot slip in between
        // and admit a journal into a period that became locked after the check.
        lock (_gate)
        {
            EnsureCanPostLocked(normalizedKey, journalEntry);
            projectLedgerBook.GetOrCreate(normalizedKey).Post(journalEntry);
        }
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
        lock (_gate)
        {
            var lockedPeriod = FindLockLocked(normalizedKey, timestamp);
            if (lockedPeriod is not null)
            {
                throw new LedgerValidationException(
                    $"Accounting period '{lockedPeriod.PeriodId}' for ledger book '{normalizedKey.LedgerBook}' is locked; " +
                    $"journal dated '{timestamp:O}' cannot be posted. Locked by '{lockedPeriod.LockedBy}' at '{lockedPeriod.LockedAtUtc:O}'.");
            }

            projectLedgerBook.GetOrCreate(normalizedKey).PostLines(timestamp, description, lines, metadata);
        }
    }

    // ── Lock-held helpers (caller must hold _gate) ─────────────────────────────

    private LockedAccountingPeriod? FindLockLocked(LedgerBookKey normalizedKey, DateTimeOffset timestamp)
        => _lockedPeriods.FirstOrDefault(period => SameLedgerKey(period.LedgerKey, normalizedKey) && period.Contains(timestamp));

    private void EnsureCanPostLocked(LedgerBookKey normalizedKey, JournalEntry journalEntry)
    {
        var lockedPeriod = FindLockLocked(normalizedKey, journalEntry.Timestamp);
        if (lockedPeriod is null)
            return;

        throw new LedgerValidationException(
            $"Accounting period '{lockedPeriod.PeriodId}' for ledger book '{normalizedKey.LedgerBook}' is locked; " +
            $"journal '{journalEntry.JournalEntryId}' dated '{journalEntry.Timestamp:O}' cannot be posted. " +
            $"Locked by '{lockedPeriod.LockedBy}' at '{lockedPeriod.LockedAtUtc:O}'.");
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
