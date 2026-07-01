namespace Meridian.Contracts.Tenancy;

/// <summary>
/// SEC-005 slice 4c decision for a fund-account <b>child</b> write (balance snapshot, custodian/bank
/// statement, reconciliation run, sync-history, or margin snapshot). Given the writing caller's
/// resolved tenant and the owning <c>account_definition</c> row's stamped tenant, it decides whether
/// the write is permitted and, if so, the tenant value to stamp on the child rows.
/// </summary>
/// <remarks>
/// The fund-accounts store is a separate database with no in-DB registry linkage, so — like the root
/// <c>account_definition</c> stamp — child rows inherit their partition tenant trust-on-first-use from
/// the owning account. This decision is isolated here (rather than inlined in the store) so its
/// fail-open branch is unit-testable in CI: the Postgres store suite is skipped without a database, so
/// an over-strict regression that refused legitimate single-company writes, or a normalization
/// mismatch, would otherwise ship uncaught.
///
/// Semantics (mirrors <see cref="TenantReadPredicate"/> and <see cref="FundProfileOwnership.IsHeldBy"/>
/// — trimmed, case-insensitive):
/// <list type="bullet">
/// <item>A <b>tenantless caller</b> (single-company deployment, or the legacy tenantless admin) always
/// proceeds and inherits the account's stamp (which may itself be null) — behavior is identical under
/// one-company-per-deployment, including the pre-existing allowance of a write to an account row that
/// does not (yet) exist.</item>
/// <item>A <b>tenant-scoped caller</b> may write only to an account that is unstamped (legacy/unbound —
/// fail-open) or owned by the same tenant; the child rows are then stamped with the caller's tenant so
/// the aggregate is consistently partitioned going forward. A write to a missing account, or one owned
/// by a different tenant, is refused (<see cref="Decision.Refused"/>) so a caller cannot inject child
/// rows keyed on another tenant's account UUID — or learn that it exists.</item>
/// </list>
/// </remarks>
public static class FundAccountChildWriteTenantGuard
{
    /// <summary>Outcome of a fund-account child-write tenant decision.</summary>
    public readonly record struct Decision(bool Allowed, string? TenantStamp)
    {
        /// <summary>The write is permitted; stamp child rows with <see cref="TenantStamp"/> (may be null).</summary>
        public static Decision Allow(string? tenantStamp) => new(true, tenantStamp);

        /// <summary>The write is refused (missing or cross-tenant account for a tenant-scoped caller).</summary>
        public static Decision Refused { get; } = new(false, null);
    }

    /// <summary>
    /// Decide whether a fund-account child write is permitted and which tenant to stamp on the child
    /// rows. See the type remarks for the full fail-open contract.
    /// </summary>
    /// <param name="accountExists">Whether the owning <c>account_definition</c> row exists.</param>
    /// <param name="accountTenant">The owning account's stamped <c>tenant_id</c> (null when unbound/legacy).</param>
    /// <param name="callerTenant">The writing caller's resolved tenant (null/blank when tenantless).</param>
    public static Decision Decide(bool accountExists, string? accountTenant, string? callerTenant)
    {
        var caller = Normalize(callerTenant);
        var owner = Normalize(accountTenant);

        // Tenantless caller: preserve the single-company / legacy-admin fail-open posture. Inherit the
        // account's stamp (may be null) and never refuse — including the pre-existing allowance of a
        // write to an account that does not exist.
        if (caller is null)
        {
            return Decision.Allow(owner);
        }

        // Tenant-scoped caller: the account must be visible to this caller — it must exist and be either
        // unstamped (legacy/unbound, fail-open) or owned by the same tenant.
        if (!accountExists)
        {
            return Decision.Refused;
        }

        if (owner is not null && !string.Equals(owner, caller, StringComparison.OrdinalIgnoreCase))
        {
            return Decision.Refused;
        }

        // Own tenant, or an unstamped/legacy account being written by a scoped caller: stamp the child
        // rows with the caller's tenant so the aggregate is consistently partitioned going forward.
        return Decision.Allow(caller);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
