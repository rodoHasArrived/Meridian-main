using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Domain.Reconciliation;

namespace Meridian.Application.Reconciliation.Legacy;

public sealed record ReconciliationPollingSchedule(TimeSpan PollInterval, TimeSpan Timeout);

public sealed record ReconciliationSourcePayload(
    string SourceId,
    ReconciliationSourceType SourceType,
    IReadOnlyList<RawPositionRecord> RawPositions,
    IReadOnlyList<RawCashRecord> RawCashEntries,
    DateTimeOffset CapturedAt,
    string Version);

public sealed record RawPositionRecord(
    string PositionId,
    string? Cusip,
    string? Isin,
    string? Ticker,
    string? InternalSecurityId,
    decimal Quantity,
    decimal Price,
    decimal MarketValue,
    string Currency,
    DateTimeOffset AsOfTimestamp);

public sealed record RawCashRecord(
    string CashEntryId,
    string AccountId,
    string? CounterpartyReference,
    string? SettlementId,
    string? Comments,
    decimal Amount,
    string Currency,
    DateTimeOffset BookingTimestamp);

public interface IReconciliationSourceAdapter
{
    string AdapterId { get; }
    ReconciliationSourceType SourceType { get; }
    Task<ReconciliationSourcePayload> PollAsync(CancellationToken cancellationToken);
}

public interface IPrimeBrokerSourceAdapter : IReconciliationSourceAdapter;
public interface ICustodianSourceAdapter : IReconciliationSourceAdapter;
public interface IAdministratorSourceAdapter : IReconciliationSourceAdapter;
public interface IInternalLedgerSourceAdapter : IReconciliationSourceAdapter;

public interface IReconciliationSourceIngestionScheduler
{
    Task<IReadOnlyList<ReconciliationSourcePayload>> IngestScheduledAsync(
        IReadOnlyList<IReconciliationSourceAdapter> adapters,
        ReconciliationPollingSchedule schedule,
        CancellationToken cancellationToken);
}

public interface IInstrumentMappingService
{
    string ResolveInstrumentKey(string? cusip, string? isin, string? ticker, string? internalSecurityId);
}

public interface IFxConversionService
{
    decimal GetFxRate(string fromCurrency, string toCurrency, DateTimeOffset timestamp);
}

public interface IAccountingPeriodService
{
    DateOnly ResolvePeriod(DateTimeOffset bookingTimestamp, string sourceId);
    DateTimeOffset AlignTimestamp(DateTimeOffset timestamp, string sourceId);
}
