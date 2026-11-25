using System.Text.Json.Serialization;

namespace Meridian.Contracts.FixedIncome;

public sealed record BondIdentifierDiscoveryRequest(string Identifier, string IdentifierType, string? MarketSector = null);
public sealed record BondIdentifierCandidateDto(string Figi, string? CompositeFigi, string? SecurityType,
    string? MarketSector, string? Ticker, string? Name, string? ExchangeCode);
public sealed record BondIdentifierDiscoveryResult(string Identifier, string IdentifierType,
    IReadOnlyList<BondIdentifierCandidateDto> Candidates, bool IsAmbiguous, string? ResolvedFigi,
    BondProviderEvidenceStatus EvidenceStatus);

/// <summary>Request contains source identifiers and dates, never an unreviewed series/unit mapping.</summary>
public sealed record BondFredIntakeRequest(string EvidenceUri, string DocumentId,
    DateOnly From, DateOnly Through, DateOnly VintageDate);
public sealed record BondFredIntakeResult(BondMarketImportResult RetainedObservations,
    IReadOnlyList<DateOnly> MissingDates, string ResponseSha256, string BindingEvidenceSha256,
    BondProviderEvidenceStatus EvidenceStatus)
{
    public string? ResponseEvidenceReference { get; init; }
}

/// <summary>The provider's exact returned JSON text, retained as UTF-8 and addressed by its SHA-256.</summary>
public sealed record BondProviderResponseDto(string ProviderId, string ContentSha256,
    string EvidenceReference, string RawResponseJson);

public interface IBondProviderResponseStore
{
    Task<BondProviderResponseDto> RetainAsync(string tenantId, string companyId, string providerId,
        string rawResponseJson, CancellationToken ct = default);
    Task<BondProviderResponseDto?> GetAsync(string tenantId, string companyId, string contentSha256,
        CancellationToken ct = default);
}

/// <summary>Exact independently accepted provider-trust/fred document. BindingKey names an immutable mapping.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BondFredSeriesBindingDocument(
    int SchemaVersion, string Kind, string BindingKey, string SeriesId, string IndexName,
    [property: JsonRequired] BondMarketObservationKind ObservationKind,
    [property: JsonRequired] BondMarketValueUnit Unit,
    string SourceName, string? Currency = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondFredSeriesBindingDocument))]
public partial class BondProviderIntakeJsonContext : JsonSerializerContext;
