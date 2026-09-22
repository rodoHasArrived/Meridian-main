using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Categories of price per the Clearwater pricing taxonomy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityPriceKind>))]
public enum SecurityPriceKind
{
    Raw,
    TradePrice,
    ClientOverride,
    MarketGoldenCopy,
    Comparison,
    CalculatedPar,
    CalculatedStraightLine
}

/// <summary>Quote basis; unspecified legacy observations cannot support valuation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityPriceUnit>))]
public enum SecurityPriceUnit { Unspecified, CurrencyPerUnit, PercentOfPar }

/// <summary>A dated source observation retained without overwriting earlier prices.</summary>
public sealed record SecurityRawPriceDto(string SourceId, decimal Price, DateTimeOffset PriceAsOf, SecurityPriceUnit Unit);

/// <summary>
/// One entry in a per-security pricing source priority chain.
/// </summary>
public sealed record PricingHierarchyEntryDto(
    int Priority,
    string SourceId,
    string SourceDisplayName,
    int MaxDaysStale);

/// <summary>
/// Full pricing source hierarchy for a security, optionally scoped to an account.
/// </summary>
public sealed record SecurityPricingHierarchyDto(
    Guid SecurityId,
    string? AccountId,
    IReadOnlyList<PricingHierarchyEntryDto> Entries,
    DateTimeOffset AsOf,
    string UpdatedBy);

/// <summary>
/// A price from one comparison source alongside its deviation from the golden copy.
/// </summary>
public sealed record SecurityComparisonPriceDto(
    string SourceId,
    decimal Price,
    DateTimeOffset PriceAsOf,
    decimal? PctDiffFromGoldenCopy,
    SecurityPriceUnit Unit = SecurityPriceUnit.Unspecified);

/// <summary>
/// The selected golden copy price with explicit units, exact hierarchy and comparison evidence.
/// SelectionReceiptId replays the immutable evaluation. Economic/knowledge timestamps filter
/// eligibility but do not identify a PostgreSQL visibility snapshot or certify close approval.
/// </summary>
public sealed record SecurityPriceGoldenCopyDto(
    Guid SecurityId,
    decimal GoldenCopyPrice,
    SecurityPriceKind PriceKind,
    string SelectedSource,
    DateTimeOffset PriceAsOf,
    bool IsStaleFallback,
    int? DaysStale,
    IReadOnlyList<SecurityComparisonPriceDto> ComparisonPrices,
    SecurityPriceUnit Unit = SecurityPriceUnit.Unspecified,
    DateTimeOffset? EvaluatedAsOf = null,
    DateTimeOffset? HierarchyAsOf = null,
    DateTimeOffset? KnowledgeAsOf = null,
    Guid? SelectionReceiptId = null,
    SecurityPricingHierarchyDto? HierarchySnapshot = null);

/// <summary>
/// Request to record a raw price from a single source for golden copy evaluation.
/// </summary>
public sealed record RecordRawPriceRequest(
    Guid SecurityId,
    string SourceId,
    decimal Price,
    DateTimeOffset PriceAsOf,
    string RecordedBy,
    SecurityPriceUnit Unit = SecurityPriceUnit.Unspecified);
