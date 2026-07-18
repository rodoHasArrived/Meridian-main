namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterQueryService
{
    Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns the security detail as it was recorded at <paramref name="asOfUtc"/>
    /// (transaction time — "as the system knew it then", not "as we now know it was").
    /// Returns <c>null</c> when the security had no recorded state at that time.
    /// Securities without event history fall back to the current projection.
    /// </summary>
    Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// Returns only event-recorded security state at <paramref name="asOfUtc"/>. Unlike
    /// <see cref="GetByIdAsOfAsync"/>, this strict accounting boundary never substitutes a
    /// projection-only current record when the security has no retained event history.
    /// </summary>
    Task<SecurityDetailDto?> GetRecordedByIdAsOfAsync(
        Guid securityId,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<SecurityDetailDto?>(null);
    }

    Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null);
    Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default);
    Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default);
    Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default);
    Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default);
    Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default);
    Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default);
}
