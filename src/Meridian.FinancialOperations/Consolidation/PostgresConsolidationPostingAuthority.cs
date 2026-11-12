using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

/// <summary>Closes the ownership/policy change window between workflow review and durable append.</summary>
public sealed class PostgresConsolidationPostingAuthority(ConsolidationService consolidation,
    PostgresFundStructureStore structure, IAccountingPolicyService policies,
    IIntercompanyAccountBindingStore? bindings = null, IConsolidationFxAuthority? fxAuthority = null,
    ILegacyIntercompanyAttributionStore? legacyAttributions = null,
    IConsolidationTransitionStore? transitions = null) : IConsolidationPostingAuthority
{
    public async Task<IAsyncDisposable> AcquireValidatedLeaseAsync(ConsolidationEvidenceDto evidence, CancellationToken ct = default)
    {
        var policyLease = await policies.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Accounting policy authority did not retain its validation lease.");
        IAsyncDisposable? ownershipLease = null;
        IAsyncDisposable? bindingLease = null;
        IAsyncDisposable? fxLease = null;
        IAsyncDisposable? attributionLease = null;
        IAsyncDisposable? transitionLease = null;
        try
        {
            ownershipLease = await structure.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (evidence.RuleVersion != "w10-v1")
                bindingLease = await (bindings ?? throw new InvalidOperationException("Account mapping authority is unavailable."))
                    .AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (evidence.RuleVersion is "w10-v3" or "w10-v4")
                fxLease = await (fxAuthority ?? throw new InvalidOperationException("FX authority is unavailable."))
                    .AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (evidence.RuleVersion != "w10-v1" && legacyAttributions is not null)
                attributionLease = await legacyAttributions.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (transitions is not null)
                transitionLease = await transitions.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            await consolidation.ValidateEvidenceCurrentAsync(evidence, ct).ConfigureAwait(false);
            return new AuthorityLease(ownershipLease, policyLease, bindingLease, fxLease, attributionLease, transitionLease);
        }
        catch
        {
            await DisposeAllAsync([transitionLease, attributionLease, fxLease, bindingLease, ownershipLease, policyLease], 0).ConfigureAwait(false);
            throw;
        }
    }

    private sealed class AuthorityLease(IAsyncDisposable ownership, IAsyncDisposable policy,
        IAsyncDisposable? bindings, IAsyncDisposable? fx, IAsyncDisposable? attributions, IAsyncDisposable? transitions) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            await DisposeAllAsync([transitions, attributions, fx, bindings, ownership, policy], 0).ConfigureAwait(false);
        }
    }

    private static async ValueTask DisposeAllAsync(IAsyncDisposable?[] leases, int index)
    {
        if (index == leases.Length) return;
        try
        {
            if (leases[index] is { } lease) await lease.DisposeAsync().ConfigureAwait(false);
        }
        finally { await DisposeAllAsync(leases, index + 1).ConfigureAwait(false); }
    }
}
