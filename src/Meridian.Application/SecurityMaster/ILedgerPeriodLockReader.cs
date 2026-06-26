using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Authoritative read of accounting-period lock status for a date, backing the Security Master
/// Passport Workbench period-aware propagation (Phase 3). The authority is the ledger accounting
/// period status (<see cref="LedgerAccountingPeriod"/>.<c>Status</c>), the same source
/// <c>LedgerPeriodPostingGuard</c> enforces at post-time — not close-readiness.
///
/// <para><b>Default-deny:</b> when the covering period is missing, indeterminate, or its status is
/// unrecognized, the reader returns <see cref="LedgerPeriodStatusDto.HardClosed"/> so an upstream
/// reference-data edit can never silently mutate a book whose lock state could not be confirmed.</para>
/// </summary>
public interface ILedgerPeriodLockReader
{
    /// <summary>
    /// Resolves the accounting period covering <paramref name="date"/> for <paramref name="ledgerBookId"/>
    /// and returns its lock status; <see cref="LedgerPeriodStatusDto.HardClosed"/> when indeterminate.
    /// </summary>
    Task<LedgerPeriodStatusDto> GetPeriodStatusAsync(
        Guid ledgerBookId, DateOnly date, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="ILedgerPeriodLockReader"/> over <see cref="ILedgerJournalStore"/>, applying the
/// default-deny posture on any indeterminate state.
/// </summary>
public sealed class LedgerPeriodLockReader : ILedgerPeriodLockReader
{
    private readonly ILedgerJournalStore _journalStore;
    private readonly ILogger<LedgerPeriodLockReader> _logger;

    public LedgerPeriodLockReader(
        ILedgerJournalStore journalStore,
        ILogger<LedgerPeriodLockReader> logger)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LedgerPeriodStatusDto> GetPeriodStatusAsync(
        Guid ledgerBookId, DateOnly date, CancellationToken ct = default)
    {
        try
        {
            var periods = await _journalStore
                .ListPeriodsAsync(ledgerBookId: ledgerBookId, ct: ct)
                .ConfigureAwait(false);

            var covering = periods.FirstOrDefault(p => date >= p.StartDate && date <= p.EndDate);
            if (covering is null)
            {
                _logger.LogWarning(
                    "No accounting period covers {Date} for ledger book {LedgerBookId}; defaulting to HardClosed.",
                    date, ledgerBookId);
                return LedgerPeriodStatusDto.HardClosed;
            }

            return ParseStatus(covering.Status, ledgerBookId, date);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to resolve accounting period status for ledger book {LedgerBookId} at {Date}; defaulting to HardClosed.",
                ledgerBookId, date);
            return LedgerPeriodStatusDto.HardClosed;
        }
    }

    private LedgerPeriodStatusDto ParseStatus(string status, Guid ledgerBookId, DateOnly date)
    {
        if (string.Equals(status, "Open", StringComparison.Ordinal))
        {
            return LedgerPeriodStatusDto.Open;
        }

        if (string.Equals(status, "SoftClosed", StringComparison.Ordinal))
        {
            return LedgerPeriodStatusDto.SoftClosed;
        }

        if (string.Equals(status, "HardClosed", StringComparison.Ordinal))
        {
            return LedgerPeriodStatusDto.HardClosed;
        }

        _logger.LogWarning(
            "Unrecognized accounting period status '{Status}' for ledger book {LedgerBookId} at {Date}; defaulting to HardClosed.",
            status, ledgerBookId, date);
        return LedgerPeriodStatusDto.HardClosed;
    }
}
