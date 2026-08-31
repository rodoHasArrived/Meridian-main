using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public interface IReconciliationSourceAdapter
{
    ReconciliationSourceType SourceType { get; }

    Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct);
}

public interface IReconciliationIngestionScheduler
{
    Task<IReadOnlyList<DataSourceSnapshot>> CaptureAsync(
        IReadOnlyList<IReconciliationSourceAdapter> adapters,
        ReconciliationIngestionRequest request,
        CancellationToken ct);
}

public sealed record ReconciliationIngestionRequest(
    DateOnly BusinessDate,
    DateTimeOffset RunTimestampUtc,
    string BaseCurrency,
    int SnapshotVersion);

public interface IInstrumentMappingService
{
    string ResolveCanonicalId(string? cusip, string? isin, string? ticker, string? internalId);
}

/// <summary>
/// Business-calendar seam for the reconciliation floor. Beyond mapping a posting timestamp to its
/// accounting period, it exposes the business-day arithmetic the matcher needs so settlement
/// proximity is measured in business days (a Friday wire against its Monday ledger posting is one
/// business day apart, not three calendar days). The production implementation is
/// <see cref="BusinessDayAccountingCalendar"/>; deployments load holiday sets through
/// <see cref="FileAccountingCalendar"/>.
/// </summary>
public interface IAccountingCalendar
{
    /// <summary>
    /// Resolves the accounting period (business date) a posting belongs to. Postings stamped on a
    /// non-business day belong to the next business day's period.
    /// </summary>
    DateOnly ResolvePeriod(DateTimeOffset postedAtUtc);

    bool IsBusinessDay(DateOnly date);

    /// <summary>Returns <paramref name="date"/> when it is a business day, otherwise the next one.</summary>
    DateOnly RollForwardToBusinessDay(DateOnly date);

    /// <summary>Returns <paramref name="date"/> when it is a business day, otherwise the previous one.</summary>
    DateOnly RollBackToBusinessDay(DateOnly date);

    /// <summary>
    /// Signed business-day distance: the number of business days in <c>(from, to]</c> when
    /// <paramref name="to"/> is later, negated when earlier, and zero for the same date.
    /// </summary>
    int CountBusinessDaysBetween(DateOnly from, DateOnly to);

    DateOnly AddBusinessDays(DateOnly date, int businessDays);
}
