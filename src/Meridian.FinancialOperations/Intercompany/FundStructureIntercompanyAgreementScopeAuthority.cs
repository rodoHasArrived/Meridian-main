using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>Resolves organization membership, tenant ownership and real book/chart identities from retained services.</summary>
public sealed class FundStructureIntercompanyAgreementScopeAuthority(
    IFundStructureService structures, IFundStructureStore? structureStore, ILedgerJournalStore journals,
    IAccountingConfigurationStore configuration, IEntityLifecycleStore lifecycleStore,
    IFundProfileTenancyRegistry ownership, bool allowUnpartitionedSyntheticStore = false, TimeProvider? timeProvider = null)
    : IIntercompanyAgreementScopeAuthority
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public async Task<IAsyncDisposable> AcquireLeaseAsync(string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default)
        => await AcquireLeasesAsync(tenantId, [agreement], ct).ConfigureAwait(false);

    public async Task<IAsyncDisposable> AcquireLeasesAsync(string tenantId, IReadOnlyList<IntercompanyAgreementDto> agreements, CancellationToken ct = default)
    {
        IAsyncDisposable? graphLease = null;
        try
        {
            if (structureStore is PostgresFundStructureStore postgres)
                graphLease = await postgres.AcquireConsolidationAuthorityLeaseAsync(ct).ConfigureAwait(false);
            var lifecycleLease = await lifecycleStore.AcquireReadLeaseAsync(tenantId,
                agreements.SelectMany(x => new[] { x.PartyOneEntityId, x.PartyTwoEntityId }).Distinct().Order().ToArray(), ct).ConfigureAwait(false);
            return new ScopeLease(lifecycleLease, graphLease);
        }
        catch
        {
            if (graphLease is not null) await graphLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ScopeLease(IAsyncDisposable lifecycle, IAsyncDisposable? graph) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await lifecycle.DisposeAsync().ConfigureAwait(false); }
            finally { if (graph is not null) await graph.DisposeAsync().ConfigureAwait(false); }
        }
    }

    public async Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(
        string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default)
    {
        var result = new List<IntercompanyEntityLifecycleReferenceDto>();
        foreach (var id in new[] { agreement.PartyOneEntityId, agreement.PartyTwoEntityId })
        {
            var revision = await lifecycleStore.GetLatestAsync(tenantId, id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Agreement approval requires a retained entity lifecycle baseline.");
            var eligibility = EntityLifecyclePolicy.Evaluate(revision, agreement.EffectiveFrom, EntityLifecycleOperationDto.NewAgreement, false);
            if (!eligibility.Allowed) throw new InvalidOperationException(eligibility.Message);
            var currentEligibility = EntityLifecyclePolicy.Evaluate(revision, _clock.GetUtcNow(), EntityLifecycleOperationDto.NewAgreement, false);
            if (!currentEligibility.Allowed) throw new InvalidOperationException(currentEligibility.Message);
            result.Add(new(id, revision.Revision, revision.ContentHash));
        }
        return result;
    }

    public async Task ValidateAsync(string tenantId, IntercompanyAgreementDto agreement, bool requireActive, CancellationToken ct = default)
    {
        if (structureStore is null && !allowUnpartitionedSyntheticStore)
            throw new InvalidOperationException("Agreement authority requires an authoritative tenant-partitioned entity store.");
        var tenants = structureStore is null ? FundStructureTenantMap.Unpartitioned
            : await structureStore.GetNodeTenantsAsync(ct).ConfigureAwait(false);
        if (!tenants.IsPartitioned && !allowUnpartitionedSyntheticStore)
            throw new InvalidOperationException("Agreement authority requires tenant-partitioned entity storage.");
        if (tenants.IsPartitioned)
            foreach (var id in new[] { agreement.OrganizationId, agreement.PartyOneEntityId, agreement.PartyTwoEntityId })
                if (!tenants.NodeTenants.TryGetValue(id, out var owner) || !string.Equals(owner.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("An agreement party or organization is outside the current tenant.");

        var graph = await structures.GetOrganizationStructureAsync(new(OrganizationId: agreement.OrganizationId,
            ActiveOnly: false, AsOf: agreement.EffectiveFrom), ct).ConfigureAwait(false);
        if (!graph.Organizations.Any(x => x.OrganizationId == agreement.OrganizationId))
            throw new UnauthorizedAccessException("The agreement organization is unavailable.");
        foreach (var id in new[] { agreement.PartyOneEntityId, agreement.PartyTwoEntityId })
        {
            var entity = graph.Entities.SingleOrDefault(x => x.EntityId == id)
                ?? throw new UnauthorizedAccessException("An agreement party is outside its organization.");
            if (entity.EffectiveFrom > agreement.EffectiveFrom || entity.EffectiveTo is { } end &&
                (agreement.EffectiveTo is null || agreement.EffectiveTo > end))
                throw new InvalidOperationException("The agreement window must fit each party's legal existence.");
        }
        if (requireActive) await GetLifecycleEvidenceAsync(tenantId, agreement, ct).ConfigureAwait(false);
        foreach (var map in agreement.PartyMappings)
        {
            var book = await journals.GetLedgerBookAsync(map.LedgerBookId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("An agreement book does not exist.");
            if (book.FundStructureNodeKind != FundStructureNodeKindDto.Entity || book.FundStructureNodeId != map.EntityId)
                throw new InvalidOperationException("An agreement book must belong to its mapped legal entity.");
            var owner = await ownership.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(tenantId))
                throw new UnauthorizedAccessException("The agreement book has no matching retained tenant ownership.");
            var workspace = await configuration.GetAsync(book.FundProfileId, map.LedgerBookId, ct, tenantId, owner.CompanyId).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The agreement book has no retained chart configuration.");
            if (tenants.IsPartitioned && !string.Equals(workspace.TenantId?.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The agreement chart is outside the current tenant.");
            if (!string.Equals(workspace.CompanyId?.Trim(), owner.CompanyId?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The agreement chart is outside the book owner's company.");
            foreach (var nodeId in new[] { map.ReceivableAccountNodeId, map.PayableAccountNodeId,
                map.SettlementAccountNodeId, map.RevenueAccountNodeId, map.ExpenseAccountNodeId }.Where(x => x is not null))
                if (!workspace.ChartOfAccounts.Any(x => x.NodeId == nodeId && !x.IsArchived))
                    throw new InvalidOperationException("An agreement account mapping is missing or archived.");
            if (requireActive)
                foreach (var (nodeId, role) in new (string? NodeId, IntercompanyAccountRoleDto Role)[]
                {
                    (map.ReceivableAccountNodeId, IntercompanyAccountRoleDto.Receivable),
                    (map.PayableAccountNodeId, IntercompanyAccountRoleDto.Payable),
                    (map.RevenueAccountNodeId, IntercompanyAccountRoleDto.Revenue),
                    (map.ExpenseAccountNodeId, IntercompanyAccountRoleDto.Expense)
                })
                    if (nodeId is not null && !workspace.ChartOfAccounts.Any(x => x.NodeId == nodeId && x.IntercompanyRole == role && !x.IsArchived))
                        throw new InvalidOperationException("Agreement approval requires the mapped chart nodes to carry their intercompany roles.");
        }
    }
}
