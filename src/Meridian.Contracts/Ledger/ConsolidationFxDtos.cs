using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<ConsolidationFxRateKindDto>))]
public enum ConsolidationFxRateKindDto { Closing, TransactionDate, Historical, PeriodAverage }

/// <summary>A positive quote: one unit of FromCurrency buys Rate units of ToCurrency.</summary>
public sealed record ConsolidationFxRateDto(
    Guid RateId, string FromCurrency, string ToCurrency, DateOnly RateDate, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Rate,
    ConsolidationFxRateKindDto Kind, string SourceEvidence, bool AllowInverse = false,
    DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null);

/// <summary>Approved immutable evidence; a correction is a new contiguous revision.</summary>
public sealed record ConsolidationFxRateSetDto(
    Guid RateSetId, int Revision, string TenantId, string CompanyId,
    IReadOnlyList<ConsolidationFxRateDto> Rates, string SourceEvidence,
    string PreparedBy, string ApprovedBy, DateTimeOffset ApprovedAt,
    string? PreviousHash = null, string? ContentHash = null);

public sealed record ConsolidationFxAccountRuleDto(
    string AccountPath, ConsolidationFxRateKindDto Method,
    DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null,
    bool RequiresOpeningTranslatedBalance = false);

/// <summary>Functional-to-reporting translation, not functional-currency remeasurement.</summary>
public sealed record ConsolidationFxPolicyDto(
    string PolicyId, string Revision, string ReportingCurrency, int DecimalPlaces,
    IReadOnlyList<ConsolidationFxAccountRuleDto>? AccountRules = null,
    string TranslationReserveAccount = "Equity:Cumulative Translation Adjustment")
{
    /// <summary>Explicit approved holiday/weekend selections. Absence means the exact as-of date.</summary>
    public IReadOnlyList<ConsolidationFxClosingDateDto>? ClosingRateDates { get; init; }
}

public sealed record ConsolidationFxClosingDateDto(DateOnly AsOf, DateOnly RateDate, string EvidenceReference);

public sealed record ConsolidationFxBookConfigurationDto(
    Guid LedgerBookId, int Revision, string TenantId, string CompanyId,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid RateSetId, int RateSetRevision,
    ConsolidationFxPolicyDto Policy, string ApprovedBy, DateTimeOffset ApprovedAt,
    string? PreviousHash = null, string? ContentHash = null)
{
    public IReadOnlyList<ConsolidationFxHistoricalFactDto>? HistoricalFacts { get; init; }
}

public sealed record ConsolidationFxHistoricalFactDto(Guid LedgerBookId, Guid JournalEntryId,
    Guid LineId, DateOnly? HistoricalDate, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal? OpeningTranslatedSignedAmount,
    string EvidenceReference);

public sealed record ConsolidationFxContextDto(
    ConsolidationFxBookConfigurationDto Configuration, ConsolidationFxRateSetDto RateSet);

public sealed record ConsolidationFxAmountDto([property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Amount, string Currency, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] decimal Rate,
    Guid? RateId, bool Inverted, Guid RateSetId, int RateSetRevision,
    string PolicyId, string PolicyRevision, string ConfigurationHash);

public sealed record ConsolidationFxSourceLineDto(
    Guid LedgerBookId, Guid JournalEntryId, Guid LineId, string EntityId,
    string AccountPath, string AccountType, string FunctionalCurrency,
    decimal Debit, decimal Credit, DateOnly EffectiveDate,
    DateOnly? HistoricalDate = null, decimal? OpeningTranslatedSignedAmount = null);

public sealed record ConsolidationFxTranslatedLineDto(
    ConsolidationFxSourceLineDto Source, decimal Debit, decimal Credit,
    decimal Rate, Guid? RateId, bool Inverted, ConsolidationFxRateKindDto Method)
{
    public decimal UnroundedSignedAmount { get; init; }
}

public sealed record ConsolidationFxReserveDto(Guid LedgerBookId, string EntityId, decimal SignedAmount,
    decimal TranslationDifferenceSignedAmount = 0m, decimal RoundingDifferenceSignedAmount = 0m);

public sealed record ConsolidationFxTranslationResultDto(
    string Currency, IReadOnlyList<ConsolidationFxTranslatedLineDto> Lines,
    decimal TranslationReserveSignedAmount, IReadOnlyList<string> Blockers,
    string Fingerprint, IReadOnlyList<ConsolidationFxReserveDto>? Reserves = null)
{
    public bool IsComplete => Blockers.Count == 0;
    public decimal TranslationDifferenceSignedAmount { get; init; }
    public decimal RoundingDifferenceSignedAmount { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConsolidationFxRateSetDto))]
[JsonSerializable(typeof(ConsolidationFxBookConfigurationDto))]
[JsonSerializable(typeof(ConsolidationFxContextDto))]
[JsonSerializable(typeof(ConsolidationFxTranslationResultDto))]
[JsonSerializable(typeof(IReadOnlyList<ConsolidationFxRateSetDto>))]
public sealed partial class ConsolidationFxJsonContext : JsonSerializerContext;
