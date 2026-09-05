namespace Meridian.Contracts.Workstation;

/// <summary>Server-owned shared close evidence check at each publication or ledger-lock boundary.</summary>
public interface IClosePublicationReadinessGuard
{
    Task<IReadOnlyList<OperationsWorkflowBlockerDto>> ValidateAsync(
        Guid workflowId,
        long expectedVersion,
        CloseReadinessScopeDto? scope,
        string? tenantId = null,
        string? companyId = null,
        CancellationToken ct = default);
}
