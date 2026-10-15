using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityMasterQueryService
{
    Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns terms from the last security event recorded at <paramref name="asOfUtc"/> or earlier
    /// (transaction time), attaching aliases from the current projection without historical filtering.
    /// Returns <c>null</c> when an event stream exists but has no recorded state by that timestamp.
    /// Securities without event history fall back to the current projection. This compatibility API
    /// does not establish historical alias knowledge; use <see cref="GetRecordedByIdAsOfAsync"/> for that boundary.
    /// </summary>
    Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// Returns only event-recorded security state at <paramref name="asOfUtc"/>. This strict
    /// accounting boundary does not fall back to a projection-only current definition.
    /// </summary>
    Task<SecurityDetailDto?> GetRecordedByIdAsOfAsync(
        Guid securityId,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<SecurityDetailDto?>(null);
    }

    /// <summary>
    /// Resolves identifiers and aliases using current knowledge of their valid-time windows.
    /// An explicit <paramref name="asOfUtc"/> selects event-recorded terms at that timestamp, but
    /// identity resolution can use later corrections and the legacy stable-identity fallback.
    /// This compatibility API does not establish what identifier the system knew at that time.
    /// </summary>
    Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null);

    /// <summary>
    /// Resolves only identifiers retained in security events and alias revisions recorded at or
    /// before <paramref name="recordedAsOfUtc"/>. Identifier and alias validity is evaluated at
    /// <paramref name="effectiveAsOfUtc"/>, defaulting to the recording cutoff. Returns the terms
    /// from the last event recorded by the cutoff, with the aliases recorded and effective then.
    /// Current projections and identity fallbacks are never substituted. Returns <c>null</c> when
    /// there is no retained match or multiple securities claim the identifier at these two times.
    /// Alias corrections predating migration 038 are unavailable; omitted aliases do not certify
    /// that no alias existed before that history boundary.
    /// </summary>
    Task<SecurityDetailDto?> GetRecordedByIdentifierAsOfAsync(
        SecurityIdentifierKind identifierKind,
        string identifierValue,
        string? provider,
        DateTimeOffset recordedAsOfUtc,
        DateTimeOffset? effectiveAsOfUtc = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<SecurityDetailDto?>(null);
    }
    Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns the full economic definition record for a security, rebuilt from its event stream.
    /// This is a heavier operation than <see cref="GetByIdAsync"/> and should only be called
    /// by governance or drill-in surfaces that need classification and sub-type detail.
    /// </summary>
    Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns the trading parameters (lot size, tick size, etc.) for a security as of the
    /// specified point in time. Returns <c>null</c> when the security is not found.
    /// </summary>
    Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>
    /// Returns the time-ordered list of corporate action events for a security.
    /// Returns an empty list when no corporate actions are recorded.
    /// </summary>
    Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns the preferred-equity-specific terms for a security.
    /// Returns <c>null</c> when the security has no preferred-equity terms recorded.
    /// </summary>
    Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns the convertible-equity-specific terms for a security.
    /// Returns <c>null</c> when the security has no convertible-equity terms recorded.
    /// </summary>
    Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default);
}
