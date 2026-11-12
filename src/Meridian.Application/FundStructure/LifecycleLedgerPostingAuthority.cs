using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.Application.FundStructure;

/// <summary>Resolves entity identity and lifecycle from authoritative structure and retained revisions.</summary>
public sealed class LifecycleLedgerPostingAuthority(IFundStructureService structure, IEntityLifecycleStore history,
    Func<IControlledCloseAuthorizationStore?> closeAuthorizations,
    Func<CancellationToken, Task<IAsyncDisposable>>? structureAuthority = null) : ILedgerPostingAuthority, ILedgerBookControlPreflight
{
    public string ControlName => "entity-lifecycle";
    public async Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default)
    {
        var ordinary = new List<LedgerPostingAuthorityContext>();
        foreach (var context in contexts)
        {
            if (ConsolidationPostingEvidenceGuard.Validate(context.Write) is not null)
                context.Decisions.Add(new("entity-lifecycle", "delegated-to-consolidation", true,
                    "Validated group eliminations use the dedicated ownership/perimeter authority retained through standalone append."));
            else ordinary.Add(context);
        }
        contexts = ordinary;
        if (contexts.Count == 0) return EmptyLedgerAuthorityLease.Instance;
        var leases = new List<IAsyncDisposable>();
        try
        {
            if (structureAuthority is not null) leases.Add(await structureAuthority(ct).ConfigureAwait(false));
            var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false), ct).ConfigureAwait(false);
            foreach (var group in contexts.GroupBy(item => item.TenantId ?? string.Empty).OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var entityIds = group.SelectMany(item => ResolveEntities(item.Book, graph)).Distinct().Order().ToArray();
                if (entityIds.Length > 0 && string.IsNullOrWhiteSpace(group.Key))
                {
                    foreach (var context in group) context.Decisions.Add(new("entity-lifecycle", "tenant-required", false,
                        "Entity lifecycle controls require an attributed tenant."));
                    continue;
                }
                leases.Add(await history.AcquireReadLeaseAsync(group.Key, entityIds, ct).ConfigureAwait(false));
                foreach (var context in group)
                {
                    var owners = ResolveEntities(context.Book, graph);
                    if (owners.Count == 0)
                    {
                        var node = graph.Nodes.SingleOrDefault(item => item.NodeId == context.Book.FundStructureNodeId);
                        var valid = context.Book.FundStructureNodeKind != FundStructureNodeKindDto.Entity && node is not null
                            && node.Kind == context.Book.FundStructureNodeKind && node.IsActive;
                        context.Decisions.Add(new("entity-lifecycle", "structure-owner", valid,
                            valid ? "The authoritative structural book owner is active; entity-specific obligations require entity books."
                                : "The ledger book has no unambiguous authorized entity owner."));
                        continue;
                    }
                    var authorization = context.Write.PostingCommand?.ControlledCloseAuthorizationId is { } id
                        && closeAuthorizations() is { } closeStore
                        ? await closeStore.GetCloseAuthorizationAsync(id, ct).ConfigureAwait(false) : null;
                    var authorizedClose = authorization is not null && authorization.JournalEntryId == context.Write.Entry.JournalEntryId
                        && authorization.LedgerBookId == context.Book.LedgerBookId
                        && authorization.ApprovalId == context.Write.PostingCommand?.ApprovalId
                        && authorization.PostingFingerprint == ControlledCloseFingerprint.Compute(context.Write)
                        && (authorization.Operation != EntityLifecycleOperationDto.Correction
                            || (authorization.SourceJournalEntryId is not null
                                && authorization.SourceJournalEntryId == (context.Write.SourceJournalEntryId ?? context.Write.PostingCommand?.SourceJournalEntryId)
                                && context.Write.PostingCommand?.Intent is AccountingPostingIntentDto.Reversal or AccountingPostingIntentDto.Rebook
                                    or AccountingPostingIntentDto.Restatement or AccountingPostingIntentDto.Adjustment));
                    if (context.Write.PostingCommand?.ControlledCloseAuthorizationId is not null && !authorizedClose)
                        throw new InvalidOperationException("Controlled-close authorization does not match the reviewed posting economics.");
                    var operation = authorizedClose ? authorization!.Operation : EntityLifecycleOperationDto.GeneralPosting;
                    var evidence = authorizedClose && !string.IsNullOrWhiteSpace(authorization!.SourceObligationEvidence);
                    foreach (var entityId in owners)
                    {
                        var revision = await history.GetLatestAsync(group.Key, entityId, ct).ConfigureAwait(false);
                        if (authorizedClose && (revision is null || authorization!.EntityLifecycleRevisions is null
                            || !authorization.EntityLifecycleRevisions.TryGetValue(entityId.ToString("D"), out var reviewedRevision)
                            || !string.Equals(reviewedRevision, revision.ContentHash, StringComparison.Ordinal)))
                            throw new InvalidOperationException("Entity lifecycle changed after controlled-close authorization; renew review against the current history.");
                        var date = context.Write.PostingCommand?.EffectiveDate ?? context.Write.Entry.Metadata.EffectiveDate
                            ?? DateOnly.FromDateTime(context.Write.Entry.Timestamp.UtcDateTime);
                        var effective = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
                        var entity = graph.Entities.Single(item => item.EntityId == entityId);
                        var correction = authorizedClose && operation == EntityLifecycleOperationDto.Correction;
                        var effectiveWindow = entity.EffectiveFrom <= effective && (entity.EffectiveTo is null || effective < entity.EffectiveTo.Value);
                        context.Decisions.Add(new("entity-activity", "entity-effective-window",
                            effectiveWindow && (entity.IsActive || correction),
                            "Entity activity and effective dates must permit the journal, or a retained historical correction must govern it."));
                        var eligibility = EntityLifecyclePolicy.Evaluate(revision, effective, operation, evidence,
                            correction);
                        context.Decisions.Add(new("entity-lifecycle", eligibility.ReasonCode, eligibility.Allowed,
                            eligibility.Message, revision?.ContentHash));
                        // Backdating must not bypass today's restriction on creating new obligations.
                        var current = EntityLifecyclePolicy.Evaluate(revision, DateTimeOffset.UtcNow, operation, evidence,
                            authorizedClose && operation == EntityLifecycleOperationDto.Correction);
                        context.Decisions.Add(new("entity-lifecycle-current", current.ReasonCode, current.Allowed,
                            current.Message, revision?.ContentHash));
                    }
                }
            }
            return new LeaseSet(leases);
        }
        catch { await new LeaseSet(leases).DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<LedgerBookControlPreflightLease> CheckAsync(LedgerBookRecord book, string? tenantId, CancellationToken ct = default)
    {
        var graphLease = structureAuthority is not null ? await structureAuthority(ct).ConfigureAwait(false) : EmptyLedgerAuthorityLease.Instance;
        try
        {
        var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false), ct).ConfigureAwait(false);
        var owners = ResolveEntities(book, graph);
        if (owners.Count == 0)
        {
            var known = book.FundStructureNodeKind != FundStructureNodeKindDto.Entity
                && graph.Nodes.Any(item => item.NodeId == book.FundStructureNodeId && item.Kind == book.FundStructureNodeKind);
            return new([new("entity-lifecycle", "owner-mapping", known,
                known ? "Structural book owner is authorized." : "Entity mapping must be resolved before activation.")], graphLease);
        }
        if (string.IsNullOrWhiteSpace(tenantId))
            return new([new("entity-lifecycle", "tenant-required", false, "A tenant is required to activate entity controls.")], graphLease);
        var lease = await history.AcquireReadLeaseAsync(tenantId, owners, ct).ConfigureAwait(false);
        try
        {
            var decisions = new List<LedgerControlDecisionDto>();
            foreach (var entity in owners)
            {
                var revision = await history.GetLatestAsync(tenantId, entity, ct).ConfigureAwait(false);
                decisions.Add(new("entity-lifecycle", $"baseline:{entity:D}", revision is not null,
                    revision is not null ? "Retained lifecycle baseline is available." : "An evidenced lifecycle baseline is required.", revision?.ContentHash));
            }
            return new(decisions, new LeaseSet([graphLease, lease]));
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
        }
        catch { await graphLease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static IReadOnlyList<Guid> ResolveEntities(LedgerBookRecord book, OrganizationStructureGraphDto graph)
    {
        if (book.FundStructureNodeKind == FundStructureNodeKindDto.Entity)
            return graph.Entities.Any(item => item.EntityId == book.FundStructureNodeId) ? [book.FundStructureNodeId] : [];
        if (book.FundStructureNodeKind == FundStructureNodeKindDto.Vehicle)
            return graph.Vehicles.Where(item => item.VehicleId == book.FundStructureNodeId)
                .Select(item => item.LegalEntityId).Where(id => graph.Entities.Any(entity => entity.EntityId == id)).Distinct().ToArray();
        return [];
    }

    private sealed class LeaseSet(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--) await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}
