namespace Meridian.Contracts.Tenancy;

/// <summary>
/// The tenant an out-of-request reader is acting for, established explicitly for the duration of a
/// unit of background work (W9-GOV-008 criterion 2).
/// </summary>
/// <remarks>
/// <para><b>The problem this solves.</b> Fund-scoped stores resolve the caller's tenant from an
/// ambient <see cref="IFundScopeTenantAccessor"/>, which is HTTP-backed and returns null outside a
/// request. <c>ILedgerJournalStore</c> alone serves roughly fifty internal and worker call sites. The
/// moment an unresolved tenant fails closed, every one of those loses access — even a job that holds
/// perfectly good retained authority, like the scheduled statement fetch, which retains and
/// reauthorizes tenant and company data but never establishes an ambient tenant before it reads.
/// Without this, tightening the read posture would not close a leak so much as stop the workers.</para>
///
/// <para><b>Why an explicit scope rather than an exemption.</b> The obvious shortcut — let a
/// tenantless background caller read unfiltered — reintroduces the fail-open path the criterion
/// exists to remove, and does so on the code path least likely to be looked at again. Requiring the
/// job to name the tenant it is acting for keeps the read scoped, makes the authority visible at the
/// call site, and means a job that has <i>not</i> been given authority still fails closed rather than
/// quietly reading everything.</para>
///
/// <para><b>Scope semantics.</b> The value flows with the async context, so work started inside the
/// scope inherits it and work outside does not. Scopes nest; disposing restores the enclosing value.
/// Dispose on the same async path that entered — the usual <c>using</c> — since restoring from a
/// different context would leak the authority into it.</para>
/// </remarks>
public static class FundScopeTenantAuthority
{
    private static readonly AsyncLocal<RetainedAuthority?> Current = new();

    /// <summary>The tenant the current background scope holds authority for, or null when none is held.</summary>
    public static string? CurrentTenantId => Current.Value?.TenantId;

    /// <summary>
    /// Why the current scope holds authority — the job or workflow name — for diagnostics when a
    /// background read is scoped differently from an operator's.
    /// </summary>
    public static string? CurrentReason => Current.Value?.Reason;

    /// <summary>
    /// Acts as <paramref name="tenantId"/> until the returned scope is disposed.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant whose retained authority this work carries. Must be a real tenant: a blank value
    /// would establish an authority that resolves to nothing, which is indistinguishable from having
    /// entered no scope at all and would fail closed later, far from the mistake.
    /// </param>
    /// <param name="reason">The job or workflow acting, recorded for diagnostics.</param>
    public static IDisposable Enter(string tenantId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var previous = Current.Value;
        Current.Value = new RetainedAuthority(tenantId.Trim(), reason);
        return new Scope(previous);
    }

    private sealed record RetainedAuthority(string TenantId, string Reason);

    private sealed class Scope(RetainedAuthority? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = previous;
        }
    }
}
