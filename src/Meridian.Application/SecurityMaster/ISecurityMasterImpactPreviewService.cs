using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityMasterImpactScopeAccessor
{
    SecurityMasterImpactScope GetRequired();
}

public interface ISecurityMasterReleasedReportImpactQuery
{
    Task<SecurityReleasedReportImpact> EvaluateAsync(Guid securityId, string? fundProfileId,
        DateTimeOffset effectiveFrom, SecurityMasterImpactScope scope, CancellationToken ct = default);
    Task<bool> CanReadRetainedRunIdsAsync(IReadOnlyList<string> runIds, SecurityMasterImpactScope scope,
        CancellationToken ct = default) => Task.FromResult(false);
}

public interface ISecurityMasterImpactPreviewStore
{
    Task RetainAsync(SecurityMasterImpactPreviewReceipt receipt, CancellationToken ct = default);
    Task<SecurityMasterImpactPreviewReceipt?> GetAsync(SecurityMasterImpactScope scope, string receiptId, CancellationToken ct = default);
}

public interface ISecurityMasterImpactPreviewService
{
    Task<SecurityMasterImpactPreviewReceipt> CreateAsync(Guid securityId, Guid revisionId,
        SecurityMasterImpactScope scope, DateTimeOffset asOf, CancellationToken ct = default);
    Task<SecurityMasterImpactPreviewReceipt?> GetAsync(SecurityMasterImpactScope scope, string receiptId, CancellationToken ct = default);
    Task<SecurityMasterImpactPreviewReceipt> ValidateAsync(SecurityMasterRevisionRecord revision,
        SecurityMasterImpactScope scope, string receiptId, bool canonicalMergeApplied = false, CancellationToken ct = default);
    Task<IAsyncDisposable> AcquireFinalizationLeaseAsync(SecurityMasterRevisionRecord revision,
        SecurityMasterImpactScope scope, CancellationToken ct = default);
}

public sealed class SecurityMasterImpactPreviewException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
