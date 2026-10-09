using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

/// <summary>Closes the ownership/policy change window between workflow review and durable append.</summary>
public sealed class PostgresConsolidationPostingAuthority(ConsolidationService consolidation,
    PostgresFundStructureStore structure, IAccountingPolicyService policies) : IConsolidationPostingAuthority
{
    public async Task<IAsyncDisposable> AcquireValidatedLeaseAsync(ConsolidationEvidenceDto evidence, CancellationToken ct = default)
    {
        var policyLease = await policies.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Accounting policy authority did not retain its validation lease.");
        IAsyncDisposable? ownershipLease = null;
        try
        {
            ownershipLease = await structure.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false);
            await consolidation.ValidateEvidenceCurrentAsync(evidence, ct).ConfigureAwait(false);
            return new AuthorityLease(ownershipLease, policyLease);
        }
        catch
        {
            try
            {
                if (ownershipLease is not null)
                    await ownershipLease.DisposeAsync().ConfigureAwait(false);
            }
            finally { await policyLease.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private sealed class AuthorityLease(IAsyncDisposable ownership, IAsyncDisposable policy) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            { await ownership.DisposeAsync().ConfigureAwait(false); }
            finally { await policy.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
