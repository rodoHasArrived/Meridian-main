using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityMasterQueryService
{
    Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default);
    Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default);
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
