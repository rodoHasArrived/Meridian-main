using Meridian.Contracts.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

/// <summary>Closes the ownership/policy change window between workflow review and durable append.</summary>
public sealed class PostgresConsolidationPostingAuthority(ConsolidationService consolidation,
    PostgresFundStructureStore structure, IAccountingPolicyService policies,
    IIntercompanyAccountBindingStore? bindings = null, IConsolidationFxAuthority? fxAuthority = null,
    ILegacyIntercompanyAttributionStore? legacyAttributions = null,
    IConsolidationTransitionStore? transitions = null,
    IIntercompanyAccountingArrangementStore? arrangements = null,
    IIntercompanyAccountingArrangementLegalAuthority? legalAuthority = null) : IConsolidationPostingAuthority
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
        IAsyncDisposable? legalLease = null;
        IAsyncDisposable? arrangementLease = null;
        try
        {
            ownershipLease = await structure.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false);
            var sources = evidence.Sources ?? [];
            var legalSources = sources.Where(source => source.IntercompanyAgreementId.HasValue
                || source.IntercompanyAgreementVersion.HasValue || source.IntercompanyAgreementPayloadHash is not null).ToArray();
            if (legalSources.Length > 0)
            {
                if (legalSources.Any(source => !source.IntercompanyAgreementId.HasValue || source.IntercompanyAgreementId.Value == Guid.Empty
                    || source.IntercompanyAgreementVersion is not > 0 || string.IsNullOrWhiteSpace(source.IntercompanyAgreementPayloadHash)))
                    throw new InvalidOperationException("Consolidation sources require complete exact canonical agreement pins.");
                var tenants = await structure.GetNodeTenantsAsync(ct).ConfigureAwait(false);
                if (!tenants.IsPartitioned || !tenants.NodeTenants.TryGetValue(evidence.Request.OwnershipRootId, out var tenant)
                    || string.IsNullOrWhiteSpace(tenant) || !tenants.NodeTenants.TryGetValue(evidence.Request.OrganizationId, out var organizationTenant)
                    || !string.Equals(tenant.Trim(), organizationTenant.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Canonical consolidation authority requires an attributed ownership root and organization.");
                var pins = legalSources.Select(source => new IntercompanyCanonicalAgreementReferenceDto(source.IntercompanyAgreementId!.Value,
                    source.IntercompanyAgreementVersion!.Value, source.IntercompanyAgreementPayloadHash!)).Distinct().ToArray();
                var retained = await (legalAuthority ?? throw new InvalidOperationException("Canonical agreement authority is unavailable."))
                    .AcquireLeaseAsync(tenant.Trim(), pins,
                        ((ILegalEntityReadAuthorityLease)ownershipLease).StructureAuthorityProof, ct).ConfigureAwait(false);
                legalLease = retained;
                foreach (var source in legalSources)
                    _ = retained.ResolveExact(new(source.IntercompanyAgreementId!.Value,
                        source.IntercompanyAgreementVersion!.Value, source.IntercompanyAgreementPayloadHash!));
            }
            if (evidence.RuleVersion != "w10-v1")
                bindingLease = await (bindings ?? throw new InvalidOperationException("Account mapping authority is unavailable."))
                    .AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (sources.Any(source => source.AccountingArrangementId is not null))
                arrangementLease = await (arrangements ?? throw new InvalidOperationException("Accounting arrangement authority is unavailable."))
                    .AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (evidence.RuleVersion is "w10-v3" or "w10-v4")
                fxLease = await (fxAuthority ?? throw new InvalidOperationException("FX authority is unavailable."))
                    .AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (evidence.RuleVersion != "w10-v1" && legacyAttributions is not null)
                attributionLease = await legacyAttributions.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            if (transitions is not null)
                transitionLease = await transitions.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
            await consolidation.ValidateEvidenceCurrentAsync(evidence, ct).ConfigureAwait(false);
            return new AuthorityLease(ownershipLease, policyLease, legalLease, bindingLease, arrangementLease, fxLease, attributionLease, transitionLease);
        }
        catch
        {
            await DisposeAllAsync([transitionLease, attributionLease, fxLease, arrangementLease, bindingLease, legalLease, ownershipLease, policyLease], 0).ConfigureAwait(false);
            throw;
        }
    }

    private sealed class AuthorityLease(IAsyncDisposable ownership, IAsyncDisposable policy,
        IAsyncDisposable? legal, IAsyncDisposable? bindings, IAsyncDisposable? arrangements,
        IAsyncDisposable? fx, IAsyncDisposable? attributions, IAsyncDisposable? transitions) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            await DisposeAllAsync([transitions, attributions, fx, arrangements, bindings, legal, ownership, policy], 0).ConfigureAwait(false);
        }
    }

    private static async ValueTask DisposeAllAsync(IAsyncDisposable?[] leases, int index)
    {
        if (index == leases.Length)
            return;
        try
        {
            if (leases[index] is { } lease)
                await lease.DisposeAsync().ConfigureAwait(false);
        }
        finally { await DisposeAllAsync(leases, index + 1).ConfigureAwait(false); }
    }
}
