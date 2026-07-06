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

public interface IFxRateProvider
{
    decimal Convert(decimal amount, string fromCurrency, string toCurrency, DateTimeOffset atUtc);
}

public interface IAccountingCalendar
{
    DateOnly ResolvePeriod(DateTimeOffset postedAtUtc);
}
