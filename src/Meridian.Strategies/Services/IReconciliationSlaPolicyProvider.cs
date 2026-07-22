using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Supplies a domain-specific SLA policy for a reconciliation case so callers can
/// override the generic severity/priority defaults computed by
/// <see cref="ReconciliationSlaCalculator.DefaultPolicyFor"/>.
/// Returning <c>null</c> defers to the default policy.
/// </summary>
public interface IReconciliationSlaPolicyProvider
{
    /// <summary>
    /// Resolves the SLA policy for <paramref name="item"/>, or <c>null</c> to use the default.
    /// </summary>
    ReconciliationSlaPolicy? ResolvePolicy(ReconciliationBreakQueueItem item);
}
