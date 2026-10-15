namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterQueryService
{
    Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Selects terms from the last retained stream version whose stored event timestamp is at or
    /// before <paramref name="asOfUtc"/>, attaching aliases from the current projection. Returns
    /// <c>null</c> when the stream has no eligible event; projection-only securities fall back to
    /// their current projection. Timestamps may precede lock waits and commit: this is timestamp
    /// eligibility among rows read now, not historical transaction visibility or a repeatable snapshot.
    /// </summary>
    Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// Selects retained security events and alias revisions by their stored timestamps at
    /// <paramref name="asOfUtc"/>, without a projection-only fallback. Alias validity is also
    /// evaluated at that cutoff. The timestamps are not commit receipts: a later commit can change
    /// the result for the same cutoff. Separate reads do not establish one transaction snapshot or
    /// prove what was visible to the system at that historical time.
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
    /// An explicit <paramref name="asOfUtc"/> selects terms by stored event-timestamp eligibility, but
    /// identity resolution can use later corrections and the legacy stable-identity fallback.
    /// This compatibility API does not establish what identifier the system knew at that time.
    /// </summary>
    Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null);

    /// <summary>
    /// Resolves identifiers from retained events whose stored event timestamps, and alias revisions
    /// whose stored recording timestamps, are at or before <paramref name="recordedAsOfUtc"/>.
    /// Identifier and alias validity is evaluated at <paramref name="effectiveAsOfUtc"/>, defaulting
    /// to that timestamp cutoff. Terms come from the last eligible retained stream version.
    /// Current projections and identity fallbacks are never substituted. Returns <c>null</c> when
    /// there is no retained match or multiple securities claim the identifier at these two times.
    /// Alias corrections predating migration 038 are unavailable; omitted aliases do not certify
    /// that no alias existed before that history boundary.
    /// Event, alias and migration timestamps can precede lock waits and commit. This API evaluates
    /// timestamp eligibility among rows read now; it does not reconstruct commit visibility or a
    /// single transaction snapshot. A later commit can change a repeated query with the same cutoff.
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
    Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default);
    Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default);
    Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default);
    Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default);
    Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default);
}
