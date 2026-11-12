using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Categories of price in the Security Master pricing taxonomy.
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
public sealed record SecurityRawPriceDto(string SourceId, decimal Price, DateTimeOffset PriceAsOf, SecurityPriceUnit Unit)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityPriceQuotationEvidence? Quotation { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityPriceBasis>))]
public enum SecurityPriceBasis { Unspecified, Clean, Dirty }

[JsonConverter(typeof(JsonStringEnumConverter<SecurityQuotationConvention>))]
public enum SecurityQuotationConvention { Unspecified, Per100Face, CurrencyPerUnit }

[JsonConverter(typeof(JsonStringEnumConverter<SecurityQuoteStatus>))]
public enum SecurityQuoteStatus { Unspecified, Bid, Ask, Mid, Evaluated, Trade }

/// <summary>Provider-declared quotation and settlement facts; absent legacy facts are never inferred.</summary>
public sealed record SecurityPriceQuotationEvidence(
    SecurityPriceBasis Basis,
    SecurityQuotationConvention Convention,
    SecurityQuoteStatus Status,
    DateOnly SettlementDate,
    string SettlementBasis)
{
    public static bool EconomicallyComparable(SecurityPriceQuotationEvidence? left, SecurityPriceQuotationEvidence? right)
        => left is null ? right is null : right is not null && left.Basis == right.Basis
            && left.Convention == right.Convention && left.SettlementDate == right.SettlementDate
            && string.Equals(left.SettlementBasis, right.SettlementBasis, StringComparison.Ordinal);
}

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
    SecurityPriceUnit Unit = SecurityPriceUnit.Unspecified)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityPriceQuotationEvidence? Quotation { get; init; }
}

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
    SecurityPricingHierarchyDto? HierarchySnapshot = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityPriceQuotationEvidence? Quotation { get; init; }
}

/// <summary>
/// Request to record a raw price from a single source for golden copy evaluation.
/// </summary>
public sealed record RecordRawPriceRequest(
    Guid SecurityId,
    string SourceId,
    decimal Price,
    DateTimeOffset PriceAsOf,
    string RecordedBy,
    SecurityPriceUnit Unit = SecurityPriceUnit.Unspecified)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityPriceQuotationEvidence? Quotation { get; init; }
}
