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
    /// Parses the deployment switch. An <b>absent</b> value keeps the current default; a value that
    /// is present but unrecognised is refused.
    /// </summary>
    /// <remarks>
    /// The two cases are deliberately not the same, though it is tempting to fold them together.
    /// Saying nothing is a deployment that has not chosen, and inheriting the default is right.
    /// Saying <c>fail_closed</c> or <c>failclosd</c> is a deployment that <i>has</i> chosen and been
    /// misheard — and because the default is the open posture, silently falling back would start a
    /// shared deployment with unattributed rows and tenantless reads exposed, by an operator who
    /// believed they had closed it. A refusal at startup is loud, immediate, and trivially fixed; a
    /// silent downgrade of a security posture is none of those.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is present but not a recognised posture.</exception>
    public static TenantScopeEnforcementOptions FromEnvironmentValue(
        string? value,
        TenantScopeEnforcementOptions fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "fail-closed" or "failclosed" or "closed" or "strict" => FailClosed,
            "deployment-boundary" or "deploymentboundary" or "boundary" or "open" => DeploymentBoundary,
            _ => throw new ArgumentException(
                $"{EnvironmentVariable} is set to '{value.Trim()}', which is not a recognised tenant "
                + "scope posture. Use 'fail-closed' or 'deployment-boundary'.",
                nameof(value)),
        };
    }
}

/// <summary>
/// Raised when a fund-scoped read is refused because the caller's tenant could not be resolved.
/// </summary>
/// <remarks>
/// Distinct from returning no rows. Under
/// <see cref="TenantScopeEnforcementMode.FailClosed"/> an unresolvable scope is <i>rejected rather
/// than defaulted</i>, and an empty result set is a default: the caller cannot tell it apart from a
/// genuinely empty ledger, and an operator reading the resulting support ticket cannot either. Every
/// fund-scoped store throws this one type so the web layer has a single thing to map to 403.
///
/// <para>An out-of-request reader that legitimately holds retained authority avoids this by
/// declaring it through <see cref="FundScopeTenantAuthority"/>, not by being exempted.</para>
/// </remarks>
public sealed class TenantScopeRejectedException : Exception
{
    public TenantScopeRejectedException(string readDescription)
        : base($"A tenant-scoped caller is required to read {readDescription}.")
    {
        ReadDescription = readDescription;
    }

    /// <summary>What the caller was trying to read, for the operator-facing message.</summary>
    public string ReadDescription { get; }
}
