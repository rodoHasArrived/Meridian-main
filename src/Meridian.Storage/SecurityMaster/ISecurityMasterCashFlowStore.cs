using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public interface ISecurityMasterCashFlowStore
{
    Task<SecurityCashFlowSourceDto?> GetSourceAsync(Guid securityId, CancellationToken ct = default);
    Task UpsertSourceAsync(SecurityCashFlowSourceDto source, CancellationToken ct = default);
}
