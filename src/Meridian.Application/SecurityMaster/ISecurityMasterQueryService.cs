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
}
