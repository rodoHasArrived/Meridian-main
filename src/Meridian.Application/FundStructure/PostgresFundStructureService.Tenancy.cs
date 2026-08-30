using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;

namespace Meridian.Application.FundStructure;

/// <summary>
/// The tenant half of <see cref="PostgresFundStructureService"/>: what a caller may see, what they
/// may claim, and what identity means across tenants (W9-GOV-008 criterion 2).
/// </summary>
/// <remarks>
/// Split from the main partial because these members answer one question that the rest of the
/// service does not — who owns a node, and who is allowed to be told it exists — and because the
/// composed file had grown past the repository's god-file threshold. The mutation and query
/// operations stay in <c>PostgresFundStructureService.cs</c>.
/// </remarks>
public sealed partial class PostgresFundStructureService
{
    /// <summary>
    /// Restricts the loaded snapshot to what the calling session's tenant may see (W9-GOV-008
    /// criterion 2).
    /// </summary>
    /// <remarks>
    /// Scoping happens here rather than in the store because every mutation resolves its parent
    /// nodes from this same snapshot. Filtering one shared view therefore closes the read on
    /// <c>/api/fund-structure/graph</c> and the link-or-mutate-by-id write in the same stroke: a node
    /// the caller cannot see is a node their <c>EnsureExistsInDict</c> check cannot find.
    /// </remarks>
    private async Task ScopeToCallerTenantAsync(MutableSnapshot snap, CancellationToken ct)
    {
        var tenants = await _store.GetNodeTenantsAsync(ct).ConfigureAwait(false);
        if (!tenants.IsPartitioned)
        {
            return;
        }

        var callerTenant = _tenantAccessor?.ResolveCallerTenant();
        var mode = _tenantScope.Mode;

        if (!FundStructureTenantScope.IsCallerAdmissible(tenants, callerTenant, mode))
        {
            // Refused, not emptied: an empty graph is indistinguishable from a genuinely empty
            // structure, so defaulting to it would hide the very rejection the criterion requires.
            throw new FundStructureTenantScopeException(
                "A tenant-scoped session is required to read the fund structure.");
        }

        bool IsVisible(Guid nodeId)
            => FundStructureTenantScope.IsVisible(tenants, callerTenant, nodeId, mode);

        RemoveHidden(snap.Organizations, IsVisible);
        RemoveHidden(snap.Businesses, IsVisible);
        RemoveHidden(snap.Clients, IsVisible);
        RemoveHidden(snap.Funds, IsVisible);
        RemoveHidden(snap.Sleeves, IsVisible);
        RemoveHidden(snap.Vehicles, IsVisible);
        RemoveHidden(snap.Entities, IsVisible);
        RemoveHidden(snap.InvestmentPortfolios, IsVisible);
        snap.LinkedAccountIds.RemoveWhere(nodeId => !IsVisible(nodeId));

        // An edge needs both endpoints: serving a link whose other end is hidden would disclose
        // that the node exists and what it hangs off, which is the ownership fact being withheld.
        RemoveHiddenBy(
            snap.OwnershipLinks,
            link => IsVisible(link.ParentNodeId) && IsVisible(link.ChildNodeId));
        RemoveHiddenBy(snap.Assignments, assignment => IsVisible(assignment.NodeId));
    }

    private static void RemoveHidden<T>(Dictionary<Guid, T> nodes, Func<Guid, bool> isVisible)
    {
        foreach (var nodeId in nodes.Keys.Where(nodeId => !isVisible(nodeId)).ToList())
        {
            nodes.Remove(nodeId);
        }
    }

    private static void RemoveHiddenBy<T>(Dictionary<Guid, T> items, Func<T, bool> isVisible)
    {
        foreach (var key in items.Where(pair => !isVisible(pair.Value)).Select(pair => pair.Key).ToList())
        {
            items.Remove(key);
        }
    }

    /// <summary>
    /// Records the calling tenant's ownership of a node this service just created.
    /// </summary>
    /// <remarks>
    /// Only newly created nodes are stamped. Claiming pre-existing unattributed nodes on an
    /// incidental write would let whichever tenant happens to write next take a shared ancestor —
    /// which is exactly the judgement <see cref="FundStructureTenantAttribution"/> refuses to make
    /// and quarantines instead.
    /// </remarks>
    private async Task StampCreatedNodesAsync(MutableSnapshot snap, CancellationToken ct)
    {
        if (snap.CreatedNodeIds.Count == 0)
        {
            return;
        }

        var callerTenant = _tenantAccessor?.ResolveCallerTenant();
        if (string.IsNullOrWhiteSpace(callerTenant))
        {
            return;
        }

        foreach (var nodeId in snap.CreatedNodeIds)
        {
            await _store.StampNodeTenantAsync(nodeId, callerTenant, ct).ConfigureAwait(false);
        }

        snap.CreatedNodeIds.Clear();
    }

    /// <summary>
    /// Reserves a node id for a create: rejects one already in use anywhere in the store, and
    /// records the node so the caller's tenant is stamped on it once the write lands.
    /// </summary>
    /// <remarks>
    /// Uniqueness is checked against <see cref="MutableSnapshot.AllNodeIds"/> — the unscoped set —
    /// not the tenant-filtered dictionaries. Scoping the uniqueness check would make an id held by
    /// another tenant look free, and the create that followed would upsert over their node, so the
    /// read gate would have become a write leak.
    /// </remarks>
    private static void ClaimNewNode(Guid nodeId, MutableSnapshot snap)
    {
        if (!snap.AllNodeIds.Add(nodeId))
            throw new InvalidOperationException($"Node {nodeId} already exists.");

        snap.CreatedNodeIds.Add(nodeId);
    }

    /// <summary>
    /// Reserves an ownership-link id for a create, rejecting one already in use anywhere in the store.
    /// </summary>
    /// <remarks>
    /// Checked against <see cref="MutableSnapshot.AllOwnershipLinkIds"/> for the same reason
    /// <see cref="ClaimNewNode"/> uses the unscoped node set: <c>UpsertOwnershipLinkAsync</c> is an
    /// unconditional <c>ON CONFLICT (ownership_link_id) DO UPDATE</c>, so an id that looked free only
    /// because scoping had filtered the row out would be written straight over another tenant's edge.
    /// </remarks>
    private static void ClaimNewOwnershipLink(Guid ownershipLinkId, MutableSnapshot snap)
    {
        if (!snap.AllOwnershipLinkIds.Add(ownershipLinkId))
            throw new InvalidOperationException($"Ownership link {ownershipLinkId} already exists.");
    }

    /// <summary>
    /// Reserves an assignment id for a create, rejecting one already in use anywhere in the store.
    /// </summary>
    /// <remarks>
    /// The <see cref="ClaimNewOwnershipLink"/> argument applies unchanged —
    /// <c>UpsertAssignmentAsync</c> is likewise an unconditional
    /// <c>ON CONFLICT (assignment_id) DO UPDATE</c>. An assignment carries the ledger grouping a node
    /// posts under, so overwriting one silently re-points another tenant's postings.
    /// </remarks>
    private static void ClaimNewAssignment(Guid assignmentId, MutableSnapshot snap)
    {
        if (!snap.AllAssignmentIds.Add(assignmentId))
            throw new InvalidOperationException($"Assignment {assignmentId} already exists.");
    }

    /// <summary>
    /// Records an account as a fund-structure node the first time an edge draws it in, stamping the
    /// caller's tenant on it only where the account store has already proved the caller owns it.
    /// </summary>
    /// <remarks>
    /// Account ids are minted by the accounts store rather than here, so unlike
    /// <see cref="ClaimNewNode"/> a repeat is not a collision — an account may legitimately be linked
    /// or assigned more than once. Only the first materialization is a candidate to claim, and only
    /// when the id is not already a node anywhere in the store: taking one another tenant has already
    /// drawn in would be exactly the incidental-write claim <see cref="StampCreatedNodesAsync"/>
    /// refuses to make.
    ///
    /// <para>Stamping matters at all because an account that stays unattributed is hidden from its own
    /// creator on the next read under the fail-closed posture — and hiding it takes the edge with it,
    /// since an edge is served only when both endpoints are visible. The caller would have written a
    /// link that vanished from their own graph.</para>
    ///
    /// <para><b>Why the claim is gated on the posture</b> (Codex review finding on PR #2871). Reaching
    /// this method means <c>ResolveNodeKindAsync</c> resolved the account through the account store,
    /// and what that proves differs by posture. Under
    /// <see cref="TenantScopeEnforcementMode.FailClosed"/> the store's predicate is
    /// <c>tenant_id = caller</c>, so resolution is positive proof the caller already owns the account
    /// and stamping the node merely restates it. Under
    /// <see cref="TenantScopeEnforcementMode.DeploymentBoundary"/> the predicate also admits
    /// <c>tenant_id is null</c>, so an <i>unattributed</i> account resolves for anyone — and claiming
    /// it would hand a shared account to whichever tenant happened to link it first, which is the
    /// judgement this codebase quarantines rather than makes. Nothing is lost by declining: under that
    /// posture an unattributed node is visible to everyone anyway, so no edge vanishes. Deployments
    /// that later tighten resolve their accumulated unattributed nodes through the attribution runner,
    /// exactly as they do for every other unattributed node.</para>
    /// </remarks>
    private async Task MaterializeLinkedAccountAsync(
        Guid accountId,
        MutableSnapshot snap,
        CancellationToken ct)
    {
        if (_tenantScope.Mode == TenantScopeEnforcementMode.FailClosed)
        {
            // Ownership is established from the account's own parents against this caller's already
            // scoped snapshot, not from the fact that the account service returned it. Which service
            // is composed decides what that return means: PostgresFundAccountStore applies the
            // tenant predicate, InMemoryFundAccountService applies nothing at all, and
            // IFundAccountService promises neither.
            //
            // Checked before anything is recorded, and for every link rather than only a first
            // materialization: an account already standing as another tenant's node fails the
            // AllNodeIds reservation below, so a check that ran only on first materialization would
            // wave exactly the foreign account through.
            var account = await _fundAccountService.GetAccountAsync(accountId, ct).ConfigureAwait(false);
            if (account is null || !IsAccountParentVisible(account, snap))
            {
                // Refused rather than merely left unstamped. Declining the stamp alone still wrote
                // the edge or assignment and the linked-account id, so the relationship survived
                // unattributed -- and whenever the account was later attributed to its rightful
                // tenant, that tenant inherited a relationship a stranger had authored.
                throw new FundStructureTenantScopeException(
                    $"Account {accountId} is not within the calling tenant's scope: no fund, entity, "
                    + "sleeve or vehicle it belongs to is visible to this caller.");
            }
        }

        snap.LinkedAccountIds.Add(accountId);

        // Only a first materialization claims, and only under the posture that established
        // ownership above. Under the deployment boundary an unattributed account is deliberately
        // visible to everyone, and taking it would hand a shared account to whichever tenant linked
        // it first -- the incidental-write claim StampCreatedNodesAsync refuses to make.
        if (snap.AllNodeIds.Add(accountId)
            && _tenantScope.Mode == TenantScopeEnforcementMode.FailClosed)
        {
            snap.CreatedNodeIds.Add(accountId);
        }
    }

    /// <summary>
    /// Whether <b>every</b> node the account hangs off is visible in <paramref name="snap"/>, which
    /// has already been narrowed to the caller's tenant, and at least one is populated.
    /// </summary>
    /// <remarks>
    /// <para>Every populated parent, not any of them. An account may carry a fund, an entity, a
    /// sleeve and a vehicle at once, and a caller-visible entity does not make a foreign fund
    /// theirs: accepting on the first visible parent would let an account that belongs to another
    /// tenant's fund through on the strength of an unrelated reference the caller happens to
    /// share.</para>
    ///
    /// <para>An account with no parent at all is not within anyone's scope either: there is nothing
    /// to derive ownership from, and inventing it is the judgement this service quarantines rather
    /// than makes.</para>
    /// </remarks>
    private static bool IsAccountParentVisible(AccountSummaryDto account, MutableSnapshot snap)
    {
        var populated = 0;

        bool ParentInScope<T>(Guid? parentId, Dictionary<Guid, T> visible)
        {
            if (parentId is not { } id)
            {
                return true;
            }

            populated++;
            return visible.ContainsKey(id);
        }

        var allInScope =
            ParentInScope(account.FundId, snap.Funds)
            & ParentInScope(account.EntityId, snap.Entities)
            & ParentInScope(account.SleeveId, snap.Sleeves)
            & ParentInScope(account.VehicleId, snap.Vehicles);

        return allInScope && populated > 0;
    }
}
