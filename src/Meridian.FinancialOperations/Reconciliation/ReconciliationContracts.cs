using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed record LegacyReconciliationPollingSchedule(TimeSpan PollInterval, TimeSpan Timeout);

public sealed record LegacyReconciliationSourcePayload(
    string SourceId,
    ReconciliationSourceType SourceType,
    IReadOnlyList<LegacyRawPositionRecord> RawPositions,
    IReadOnlyList<LegacyRawCashRecord> RawCashEntries,
    DateTimeOffset CapturedAt,
    string Version);

public sealed record LegacyRawPositionRecord(
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

public sealed record LegacyRawCashRecord(
    string CashEntryId,
    string AccountId,
    string? CounterpartyReference,
    string? SettlementId,
    string? Comments,
    decimal Amount,
    string Currency,
    DateTimeOffset BookingTimestamp);

public interface ILegacyReconciliationSourceAdapter
{
    string AdapterId { get; }
    ReconciliationSourceType SourceType { get; }
    Task<LegacyReconciliationSourcePayload> PollAsync(CancellationToken cancellationToken);
}

public interface ILegacyPrimeBrokerSourceAdapter : ILegacyReconciliationSourceAdapter;
public interface ILegacyCustodianSourceAdapter : ILegacyReconciliationSourceAdapter;
public interface ILegacyAdministratorSourceAdapter : ILegacyReconciliationSourceAdapter;
public interface ILegacyInternalLedgerSourceAdapter : ILegacyReconciliationSourceAdapter;

public interface ILegacyReconciliationSourceIngestionScheduler
{
    Task<IReadOnlyList<LegacyReconciliationSourcePayload>> IngestScheduledAsync(
        IReadOnlyList<ILegacyReconciliationSourceAdapter> adapters,
        LegacyReconciliationPollingSchedule schedule,
        CancellationToken cancellationToken);
}

public interface ILegacyInstrumentMappingService
{
    string ResolveInstrumentKey(string? cusip, string? isin, string? ticker, string? internalSecurityId);
}

public interface ILegacyFxConversionService
{
    decimal GetFxRate(string fromCurrency, string toCurrency, DateTimeOffset timestamp);
}

public interface ILegacyAccountingPeriodService
{
    DateOnly ResolvePeriod(DateTimeOffset bookingTimestamp, string sourceId);
    DateTimeOffset AlignTimestamp(DateTimeOffset timestamp, string sourceId);
}
