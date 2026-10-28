namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Reads corporate actions without requiring unrelated Security Master queries.
/// </summary>
public interface ISecurityCorporateActionsQueryService
{
    /// <summary>
    /// Returns the time-ordered list of corporate action events for a security.
    /// Returns an empty list when no corporate actions are recorded.
    /// </summary>
    Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default);
}
