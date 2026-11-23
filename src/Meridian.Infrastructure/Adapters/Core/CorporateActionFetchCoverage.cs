namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Coverage of one vendor family within a bounded query, never accounting authority.</summary>
public enum CorporateActionTypeCoverageStatus
{
    Complete,
    Incomplete,
    Unavailable,
    NotRequested,
}

public sealed record CorporateActionTypeCoverage(
    string ProviderEventType,
    CorporateActionTypeCoverageStatus Status,
    int Observations,
    int RelatedSymbolObservationsSkipped = 0,
    string? Reason = null);

/// <summary>
/// Explicit query boundaries. A complete fetch does not assert that the vendor has published
/// every announcement, that unmapped families were searched, or that all mutations were seen.
/// </summary>
public sealed record CorporateActionFetchCoverage(
    string Endpoint,
    string DateBasis,
    DateOnly Start,
    DateOnly End,
    string Region,
    string DataQuality,
    IReadOnlyList<CorporateActionTypeCoverage> Types,
    bool IncludesCancellationHistory = false,
    bool IncludesCompleteRevisionHistory = false);
