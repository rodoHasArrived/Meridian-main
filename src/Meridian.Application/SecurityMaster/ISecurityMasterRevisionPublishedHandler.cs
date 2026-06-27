using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Side-effect handler invoked after a Security Master passport revision is durably published.
/// Implementations MUST be idempotent — a publish may be retried after a partial fan-out.
/// Phase 3 supplies the concrete handlers (per-security projection rebuild, coverage invalidation,
/// and the period-aware restatement-proposal handler).
/// </summary>
public interface ISecurityMasterRevisionPublishedHandler
{
    /// <summary>Lower runs first; the period-aware restatement handler runs last.</summary>
    int Order { get; }

    Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default);
}
