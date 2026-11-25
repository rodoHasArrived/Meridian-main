using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed class EntityTimelineService(IFundStructureService structures, IEntityLifecycleStore lifecycle,
    IntercompanyAccountingArrangementService arrangements, ILedgerJournalStore journals, IIntercompanyAgreementService agreements, IEntityTimelineAccessAuthority access)
{
    public async Task<EntityTimelineDto> GetAsync(string tenant, Guid organizationId, Guid entityId,
        DateTimeOffset asOf, CancellationToken ct = default, string actor = "entity-timeline")
    {
        var caller = new IntercompanyAgreementCallContext(tenant, actor);
        if (!await access.CanAccessEntitiesAsync(caller, [entityId], ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The selected entity is outside the operator's scoped grants.");
        var graph = await structures.GetOrganizationStructureAsync(new(OrganizationId: organizationId,
            ActiveOnly: false, AsOf: asOf), ct).ConfigureAwait(false);
        var entity = graph.Entities.SingleOrDefault(x => x.EntityId == entityId)
            ?? throw new UnauthorizedAccessException("The entity is unavailable in this organization and tenant.");
        if (!graph.Organizations.Any(x => x.OrganizationId == organizationId))
            throw new UnauthorizedAccessException("The organization is unavailable.");
        var revisions = await lifecycle.GetHistoryAsync(tenant, entityId, ct).ConfigureAwait(false);
        var profiles = new List<ResolvedIntercompanyAccountingArrangementDto>();
        foreach (var profile in await arrangements.ListAsync(tenant, organizationId, ct).ConfigureAwait(false))
            if ((profile.PartyOneEntityId == entityId || profile.PartyTwoEntityId == entityId)
                && await access.CanAccessEntitiesAsync(caller, [profile.PartyOneEntityId, profile.PartyTwoEntityId], ct).ConfigureAwait(false))
                profiles.Add(profile);
        var contracts = new List<IntercompanyAgreementDetailDto>();
        string? cursor = null;
        do
        {
            var page = await agreements.ListAsync(new(tenant, actor), new(EntityId: entityId,
                AsOf: DateOnly.FromDateTime(asOf.UtcDateTime), Cursor: cursor, PageSize: 100), ct).ConfigureAwait(false);
            contracts.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        var books = await journals.ListLedgerBooksAsync(fundStructureNodeId: entityId,
            fundStructureNodeKind: FundStructureNodeKindDto.Entity, ct: ct).ConfigureAwait(false);
        var warnings = new List<string>();
        if (revisions.Count == 0)
            warnings.Add("Lifecycle history has not been established; earlier eligibility is unknown.");
        else if (revisions[0].LegacySnapshot)
            warnings.Add("The first revision is a legacy snapshot; it does not establish earlier lifecycle history.");
        return new(organizationId, entity, asOf, revisions,
            graph.OwnershipLinks.Where(x => x.ChildNodeId == entityId || x.ParentNodeId == entityId).ToArray(), contracts, profiles,
            books.Select(x => new LedgerBookDto(x.LedgerBookId, x.FundProfileId, x.FundStructureNodeId,
                x.FundStructureNodeKind, x.DisplayName, x.BaseCurrency, x.CreatedAt, x.UpdatedAt,
                x.Description, x.AccountingBasis, x.AccountingPolicyId, x.AccountingPolicyVersion)).ToArray(), warnings);
    }
}
