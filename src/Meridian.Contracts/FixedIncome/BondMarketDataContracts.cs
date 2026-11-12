using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.FixedIncome;

[JsonConverter(typeof(JsonStringEnumConverter<BondMarketObservationKind>))]
public enum BondMarketObservationKind { Fixing, InflationIndex, PrincipalFactor, CreditRating, Quote }
[JsonConverter(typeof(JsonStringEnumConverter<BondMarketValueUnit>))]
public enum BondMarketValueUnit { AnnualFraction, Percent, IndexLevel, PrincipalFactor, PercentOfPar, CurrencyPerUnit, Rating }
[JsonConverter(typeof(JsonStringEnumConverter<BondQuoteCategory>))]
public enum BondQuoteCategory { EvaluatedValuation, ReportedTrade, IndicativeQuote, ExecutableQuote }
[JsonConverter(typeof(JsonStringEnumConverter<BondQuotePriceBasis>))]
public enum BondQuotePriceBasis { Clean, Dirty }
[JsonConverter(typeof(JsonStringEnumConverter<BondProviderEvidenceStatus>))]
public enum BondProviderEvidenceStatus { SyntheticVerified, LiveVerified, LiveUntested, Unsupported }

/// <summary>A quote's economic meaning. A price selection receipt remains the pricing authority.</summary>
public sealed record BondMarketQuoteDetails(
    [property: JsonRequired] BondQuoteCategory Category,
    [property: JsonRequired] BondQuotePriceBasis PriceBasis,
    DateOnly SettlementDate,
    decimal? AvailableFace = null,
    decimal? ProviderYield = null,
    string? ProviderYieldConvention = null,
    DateTimeOffset? ExpiresAtUtc = null)
{
    public string? PricingSourceId { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
}

/// <summary>
/// One explicitly typed source revision. ObservationKey is the stable provider record key across
/// corrections. Rate values preserve their declared units; series names never imply a unit.
/// </summary>
public sealed record BondMarketObservationInput(
    string ObservationKey,
    long Revision,
    [property: JsonRequired] BondMarketObservationKind Kind,
    string ProviderId,
    string SeriesKey,
    DateOnly EconomicDate,
    [property: JsonRequired] BondMarketValueUnit Unit,
    decimal? Value,
    string SourceName,
    string SourceEvidenceSha256,
    string EvidenceReference,
    DateTimeOffset? PublishedAtUtc = null,
    Guid? SecurityId = null,
    string? Currency = null,
    string? Rating = null,
    string? RatingAgency = null,
    BondMarketQuoteDetails? Quote = null,
    DateOnly? SourceVintageDate = null);

/// <summary>Append-only knowledge-time envelope; its content identity excludes no economic facts.</summary>
public sealed record BondMarketObservationDto(
    Guid ObservationId,
    BondMarketObservationInput Input,
    DateTimeOffset RecordedAtUtc,
    string RecordedBy,
    string ContentSha256);

public sealed record BondMarketImportRequest(IReadOnlyList<BondMarketObservationInput> Observations);
public sealed record BondMarketImportResult(IReadOnlyList<BondMarketObservationDto> Observations, int AppendedCount);

/// <summary>
/// Query one typed input population. EconomicDate selects the exact date; no previous fixing or
/// future observation is substituted. IncludeSuperseded exposes eligible source revision history.
/// </summary>
public sealed record BondMarketDataQuery(
    BondMarketObservationKind? Kind = null,
    string? SeriesKey = null,
    Guid? SecurityId = null,
    DateOnly? EconomicDate = null,
    DateOnly? FromDate = null,
    DateOnly? ThroughDate = null,
    DateTimeOffset? KnownAtUtc = null,
    string? ProviderId = null,
    bool IncludeSuperseded = false);

public interface IBondMarketDataStore
{
    Task<BondMarketImportResult> AppendAsync(string tenantId, IReadOnlyList<BondMarketObservationDto> observations, CancellationToken ct = default);
    Task<IReadOnlyList<BondMarketObservationDto>> ReadAsync(string tenantId, CancellationToken ct = default);
}

public interface IBondMarketDataService
{
    Task<BondMarketImportResult> ImportAsync(string tenantId, string actor, BondMarketImportRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BondMarketObservationDto>> QueryAsync(string tenantId, BondMarketDataQuery query, CancellationToken ct = default);
    Task<BondMarketObservationDto?> GetAsync(string tenantId, Guid observationId, CancellationToken ct = default);
}

/// <summary>Configured connectivity, contractual entitlement and proof are independent facts.</summary>
public sealed record BondConnectionCapabilityDto(
    string ConnectionId,
    string ProviderId,
    string Capability,
    bool Implemented,
    bool Configured,
    bool? Entitled,
    BondProviderEvidenceStatus EvidenceStatus,
    DateTimeOffset? VerifiedAtUtc,
    string? VerificationEvidence,
    IReadOnlyList<string> Blockers)
{
    public bool Usable => Implemented && Configured && Entitled == true
        && EvidenceStatus != BondProviderEvidenceStatus.Unsupported && Blockers.Count == 0;
}

/// <summary>Shared validation and canonical identities for adapters, durable stores and imports.</summary>
public static class BondMarketDataRules
{
    public static BondMarketObservationInput Normalize(BondMarketObservationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required.") : value.Trim();
        var result = input with
        {
            ObservationKey = Required(input.ObservationKey, nameof(input.ObservationKey)),
            ProviderId = Required(input.ProviderId, nameof(input.ProviderId)).ToLowerInvariant(),
            SeriesKey = Required(input.SeriesKey, nameof(input.SeriesKey)),
            SourceName = Required(input.SourceName, nameof(input.SourceName)),
            SourceEvidenceSha256 = Required(input.SourceEvidenceSha256, nameof(input.SourceEvidenceSha256)).ToLowerInvariant(),
            EvidenceReference = Required(input.EvidenceReference, nameof(input.EvidenceReference)),
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant(),
            Rating = string.IsNullOrWhiteSpace(input.Rating) ? null : input.Rating.Trim(),
            RatingAgency = string.IsNullOrWhiteSpace(input.RatingAgency) ? null : input.RatingAgency.Trim()
        };
        if (result.Revision < 1 || result.EconomicDate == default || !Enum.IsDefined(result.Kind) || !Enum.IsDefined(result.Unit)
            || result.SecurityId == Guid.Empty || result.SourceEvidenceSha256.Length != 64 || !result.SourceEvidenceSha256.All(Uri.IsHexDigit)
            || result.PublishedAtUtc is { } published && (published == default || published.Offset != TimeSpan.Zero)
            || result.Currency is { } currency && (currency.Length != 3 || !currency.All(char.IsAsciiLetter)))
            throw new ArgumentException("Observation identity, revision, economic date, units, UTC publication time and source evidence must be explicit and valid.");
        if (result.Kind == BondMarketObservationKind.CreditRating)
        {
            if (result.Unit != BondMarketValueUnit.Rating || result.Value is not null || result.Rating is null || result.RatingAgency is null || result.SecurityId is null)
                throw new ArgumentException("A credit rating requires its security, agency and rating label, without a fabricated numeric value.");
        }
        else if (result.Value is null || result.Rating is not null || result.RatingAgency is not null)
            throw new ArgumentException("Numeric observations require a value; ratings belong to the credit-rating input category.");
        switch (result.Kind)
        {
            case BondMarketObservationKind.Fixing when result.Unit is not (BondMarketValueUnit.AnnualFraction or BondMarketValueUnit.Percent):
                throw new ArgumentException("A fixing requires explicit annual-fraction or percent units.");
            case BondMarketObservationKind.InflationIndex when result.Unit != BondMarketValueUnit.IndexLevel || result.Value <= 0m:
                throw new ArgumentException("An inflation observation requires a positive index level.");
            case BondMarketObservationKind.PrincipalFactor when result.Unit != BondMarketValueUnit.PrincipalFactor || result.Value < 0m || result.Value > 1m || result.SecurityId is null:
                throw new ArgumentException("A principal factor requires its security and a factor in [0, 1].");
            case BondMarketObservationKind.Quote:
                if (result.SecurityId is null || result.Currency is null || result.Unit is not (BondMarketValueUnit.PercentOfPar or BondMarketValueUnit.CurrencyPerUnit)
                    || result.Value < 0m || result.Quote is not { } quote || !Enum.IsDefined(quote.Category) || !Enum.IsDefined(quote.PriceBasis)
                    || quote.SettlementDate == default || quote.AvailableFace <= 0m
                    || quote.ProviderYield.HasValue != !string.IsNullOrWhiteSpace(quote.ProviderYieldConvention)
                    || quote.PricingSourceId is { } pricingSource && string.IsNullOrWhiteSpace(pricingSource)
                    || quote.ObservedAtUtc is { } observed && (observed == default || observed.Offset != TimeSpan.Zero)
                    || quote.ExpiresAtUtc is { } expiry && (expiry == default || expiry.Offset != TimeSpan.Zero))
                    throw new ArgumentException("A quote requires security, currency, category, clean/dirty basis, settlement date and quotation units; provider yield must name its convention.");
                break;
        }
        if (result.Kind != BondMarketObservationKind.Quote && result.Quote is not null)
            throw new ArgumentException("Quote metadata belongs only to a quote observation.");
        return result;
    }

    public static string SourceRevisionKey(BondMarketObservationInput input)
        => JsonSerializer.Serialize(new[] { input.ProviderId, input.ObservationKey, input.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) });

    public static string SourceKey(BondMarketObservationInput input)
        => JsonSerializer.Serialize(new[] { input.ProviderId, input.ObservationKey });

    public static string ContentHash(BondMarketObservationInput input)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(input, BondMarketDataJsonContext.Default.BondMarketObservationInput));

    public static void ValidateRetained(BondMarketObservationDto observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var normalized = Normalize(observation.Input);
        if (observation.ObservationId == Guid.Empty || observation.RecordedAtUtc == default || observation.RecordedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(observation.RecordedBy) || normalized.PublishedAtUtc > observation.RecordedAtUtc
            || !Sha256Digest.FixedEquals(ContentHash(normalized), observation.ContentSha256)
            || JsonSerializer.Serialize(normalized, BondMarketDataJsonContext.Default.BondMarketObservationInput)
                != JsonSerializer.Serialize(observation.Input, BondMarketDataJsonContext.Default.BondMarketObservationInput))
            throw new InvalidDataException("Retained market observation identity, normalization, knowledge time or content hash is invalid.");
    }

    public static decimal AnnualFraction(BondMarketObservationDto observation)
        => observation.Input.Kind == BondMarketObservationKind.Fixing && observation.Input.Value is { } rate
            ? observation.Input.Unit switch
            {
                BondMarketValueUnit.AnnualFraction => rate,
                BondMarketValueUnit.Percent => rate / 100m,
                _ => throw new ArgumentException("The observation does not carry rate units.")
            }
            : throw new ArgumentException("The observation is not a fixing.");
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BondMarketObservationInput))]
[JsonSerializable(typeof(BondMarketObservationDto))]
[JsonSerializable(typeof(BondMarketObservationDto[]))]
[JsonSerializable(typeof(BondMarketImportRequest))]
[JsonSerializable(typeof(BondMarketImportResult))]
[JsonSerializable(typeof(BondMarketDataQuery))]
[JsonSerializable(typeof(BondConnectionCapabilityDto[]))]
public partial class BondMarketDataJsonContext : JsonSerializerContext;
