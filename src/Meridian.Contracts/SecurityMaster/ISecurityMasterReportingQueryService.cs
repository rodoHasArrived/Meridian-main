namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Supplies a single, point-in-time Security Master reference for governed reporting.
/// Implementations must return the detail and economic definition from the same historical
/// source state so report rendering never combines yesterday's identity with today's terms.
/// </summary>
public interface ISecurityMasterReportingQueryService
{
    Task<SecurityMasterReportingReference?> GetReportingReferenceByIdentifierAsOfAsync(
        SecurityIdentifierKind identifierKind,
        string identifierValue,
        string? provider,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default);
}

/// <summary>How a reporting reference was resolved.</summary>
public enum SecurityMasterReportingResolutionMode
{
    /// <summary>The detail and economic definition were rebuilt from a historical event.</summary>
    HistoricalEvent,

    /// <summary>
    /// The security has no event history, so the current projection was used for compatibility.
    /// This mode is useful for draft output but is not certifiable.
    /// </summary>
    CurrentProjectionFallback,

    /// <summary>
    /// The query implementation does not expose a point-in-time reporting contract. Detail was
    /// resolved as-of where supported, but economic terms may be current. This is not certifiable.
    /// </summary>
    LegacyQueryFallback
}

/// <summary>
/// Frozen reference-data input used by reporting. Event coordinates are populated when the
/// reference was rebuilt from the Security Master event stream.
/// </summary>
public sealed record SecurityMasterReportingReference(
    SecurityDetailDto Detail,
    SecurityEconomicDefinitionRecord? EconomicDefinition,
    DateTimeOffset AsOfUtc,
    SecurityMasterReportingResolutionMode ResolutionMode,
    long? EventGlobalSequence = null,
    long? EventStreamVersion = null,
    DateTimeOffset? EventTimestamp = null);
