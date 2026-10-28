using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Loads and updates the cash flow source retained for a security.
/// </summary>
public interface ISecurityMasterCashFlowStore
{
    Task<SecurityCashFlowSourceDto?> GetSourceAsync(Guid securityId, CancellationToken ct = default);
    Task UpsertSourceAsync(SecurityCashFlowSourceDto source, CancellationToken ct = default);
}
