namespace Meridian.Contracts.Tenancy;

/// <summary>
/// How strictly tenant scope is enforced on fund-partitioned reads.
/// </summary>
public enum TenantScopeEnforcementMode
{
    /// <summary>
    /// The historical SEC-005 posture: unattributed rows stay visible and a caller with no resolved
    /// tenant reads unfiltered, with one-company-per-deployment as the actual control.
    /// </summary>
    /// <remarks>
    /// Correct and load-bearing for a single-company deployment, and the only safe posture on a
    /// database whose tenant attribution has not been run yet — a fail-closed reader over an
    /// unstamped graph hides the retained data from everyone rather than closing a leak. It is a
    /// staging posture, not a destination: it cannot satisfy a categorical cross-tenant criterion.
    /// </remarks>
    DeploymentBoundary = 0,

    /// <summary>
    /// Cross-tenant reads fail closed: a scoped caller sees only rows their tenant owns,
    /// unattributed rows included, and a caller whose tenant cannot be resolved is refused rather
    /// than defaulted to an unfiltered read.
    /// </summary>
    FailClosed = 1,
}

/// <summary>
/// Deployment switch for <see cref="TenantScopeEnforcementMode"/>, read once at startup so changing
/// it requires a restart.
/// </summary>
/// <remarks>
/// The switch exists to stage an upgrade — a deployment attributes its graph, checks what the
/// attribution quarantined, and then tightens — not to make fail-closed optional. Anything still
/// running under <see cref="TenantScopeEnforcementMode.DeploymentBoundary"/> is relying on the
/// single-company boundary and should say so explicitly.
/// </remarks>
public sealed record TenantScopeEnforcementOptions(TenantScopeEnforcementMode Mode)
{
    /// <summary>Environment variable naming the posture, e.g. <c>deployment-boundary</c>.</summary>
    public const string EnvironmentVariable = "MERIDIAN_TENANT_SCOPE_ENFORCEMENT";

    public static readonly TenantScopeEnforcementOptions DeploymentBoundary =
        new(TenantScopeEnforcementMode.DeploymentBoundary);

    public static readonly TenantScopeEnforcementOptions FailClosed =
        new(TenantScopeEnforcementMode.FailClosed);

    public bool IsFailClosed => Mode == TenantScopeEnforcementMode.FailClosed;

    /// <summary>
    /// Parses the deployment switch. An unrecognised or absent value keeps the current default
    /// rather than guessing, because both directions of guess are harmful: guessing fail-closed on
    /// an unattributed database hides its data, and guessing open on a shared one keeps the leak.
    /// </summary>
    public static TenantScopeEnforcementOptions FromEnvironmentValue(
        string? value,
        TenantScopeEnforcementOptions fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        return value?.Trim().ToLowerInvariant() switch
        {
            "fail-closed" or "failclosed" or "closed" or "strict" => FailClosed,
            "deployment-boundary" or "deploymentboundary" or "boundary" or "open" => DeploymentBoundary,
            _ => fallback,
        };
    }
}
