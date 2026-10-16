namespace Meridian.Contracts.Tenancy;

/// <summary>
/// SEC-005 slice 4c-iii decision for a fund-scoped <b>write</b> request: whether a session with no
/// server-resolved tenant may create or evaluate fund-scoped accounting artifacts.
/// </summary>
/// <remarks>
/// Supported hosts enable enforcement by default. Explicit deployment-boundary compatibility may
/// retain detection-only writes while a legacy installation completes reviewed attribution. The
/// decision is isolated here so the three-way branch is unit-testable without spinning up the pipeline.
/// </remarks>
public enum FundScopedWriteTenantDecision
{
    /// <summary>The caller has a resolved tenant; proceed normally.</summary>
    Allow,

    /// <summary>Tenantless caller, enforcement off: record a detection warning, then proceed.</summary>
    WarnAndAllow,

    /// <summary>Tenantless caller, enforcement on: refuse the write (fail closed).</summary>
    Deny,
}

/// <summary>Pure decision for the SEC-005 4c-iii fund-scoped write tenant gate.</summary>
public static class FundScopedWriteTenantGate
{
    /// <summary>
    /// A caller with a resolved tenant is always allowed. A tenantless caller is denied only when
    /// enforcement is enabled; otherwise it is allowed but flagged for detection.
    /// </summary>
    public static FundScopedWriteTenantDecision Decide(bool hasTenantScope, bool enforce)
        => hasTenantScope
            ? FundScopedWriteTenantDecision.Allow
            : enforce
                ? FundScopedWriteTenantDecision.Deny
                : FundScopedWriteTenantDecision.WarnAndAllow;
}
