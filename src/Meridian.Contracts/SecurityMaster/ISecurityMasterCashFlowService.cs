namespace Meridian.Contracts.SecurityMaster;

public interface ISecurityMasterCashFlowService
{
    Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(Guid securityId, CancellationToken ct = default);
    Task UpsertCashFlowSourceAsync(UpsertCashFlowSourceRequest request, CancellationToken ct = default);
    Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default);
}
