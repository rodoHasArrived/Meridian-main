using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.AssetOperations;

/// <summary>Identifies retained operands; the caller never supplies quantities, prices or carrying amounts.</summary>
public sealed record AssetPositionMarketValuationRequest(
    Guid SecurityId, Guid PositionId, Guid LedgerBookId, string AccountId, DateOnly ValuationDate,
    Guid EconomicStateId, long EconomicStateVersion, DateTimeOffset? KnownAt = null);

/// <summary>A metric-specific refusal. A market-value-only result may still refuse its carrying comparison.</summary>
public sealed record AssetPositionMarketValuationBlockerDto(string Code, string Reason, string Metric = "All");

/// <summary>
/// A calculation over a retained instrument-position projection, not a posted ledger balance or journal.
/// UnrealizedPnl is explicitly a mark-to-carrying comparison, not realized, total-return or tax-cost P/L.
/// Decimal operands and results serialize as exact strings for browser consumers.
/// </summary>
public sealed record AssetPositionMarketValuationDto(
    Guid SecurityId, Guid PositionId, Guid LedgerBookId, string AccountId, DateOnly ValuationDate,
    Guid EconomicStateId, long EconomicStateVersion, string Status, string? FundProfileId, long? PositionVersion,
    string Currency, string? AccountingBasis, string? PositionSide, string ModelKey, string ModelVersion,
    string CalculationBasis, string UnrealizedPnlBasis,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? CarryingAmount,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? Price,
    SecurityPriceUnit PriceUnit,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? MarketValue,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? UnrealizedPnl,
    DateTimeOffset? ObservedAsOf, DateTimeOffset? KnowledgeAsOf, string FreshnessStatus,
    bool? IsStaleFallback, int? DaysStale, string? SelectedSource, Guid? SelectionReceiptId,
    IReadOnlyList<SecurityValuationInputDto> Inputs,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<AssetPositionMarketValuationBlockerDto> Blockers);
