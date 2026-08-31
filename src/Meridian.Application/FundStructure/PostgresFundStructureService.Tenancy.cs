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
        // An account already standing in the caller's scoped snapshot needs no further proof: it is
        // a fund-structure node, and ScopeToCallerTenantAsync retained it in LinkedAccountIds only
        // because its own tenant stamp is visible to this caller. Re-deriving ownership from the
        // account DTO's parents would be strictly weaker than the stamp, and wrong for the shapes
        // that carry no GUID parent at all -- a migrated account, or one whose only reference is a
        // free-text PortfolioId, both of which CreateAccountRequest permits. Those would score no
        // populated parent and be refused, so a caller could not link, replace or assign against an
        // account the same snapshot is already showing them (Codex review finding on PR #2871).
        if (snap.LinkedAccountIds.Contains(accountId))
        {
            return;
        }

        // Ownership is established from the account's own parents against this caller's already
        // scoped snapshot, not from the fact that the account service returned it. Which service is
        // composed decides what that return means: PostgresFundAccountStore applies the tenant
        // predicate, InMemoryFundAccountService applies nothing at all, and IFundAccountService
        // promises neither.
        //
        // Checked before anything is recorded, and for every link rather than only a first
        // materialization: an account already standing as another tenant's node fails the AllNodeIds
        // reservation below, so a check that ran only on first materialization would wave exactly
        // the foreign account through.
        //
        // Run under BOTH postures. Gating the whole check on fail-closed conflated "unattributed"
        // with "foreign": the deployment boundary shares unattributed nodes, but it hides nodes
        // attributed to another tenant just as fail-closed does, so an account hanging off another
        // tenant's fund was never boundary-visible either. Skipping the check there let a caller
        // persist an edge or a ledger assignment against it, which the rightful tenant then
        // inherited once attribution reached the account (Codex review finding on PR #2871). Only
        // the ownership-evidence requirement stays posture-specific.
        var requireOwnershipEvidence = _tenantScope.Mode == TenantScopeEnforcementMode.FailClosed;
        var account = await _fundAccountService.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (account is null || !IsAccountParentVisible(account, snap, requireOwnershipEvidence))
        {
            // Refused rather than merely left unstamped. Declining the stamp alone still wrote the
            // edge or assignment and the linked-account id, so the relationship survived
            // unattributed -- and whenever the account was later attributed to its rightful tenant,
            // that tenant inherited a relationship a stranger had authored.
            throw new FundStructureTenantScopeException(
                $"Account {accountId} is not within the calling tenant's scope: a fund, entity, "
                + "sleeve, vehicle or portfolio it belongs to is not visible to this caller.");
        }

        // The global reservation, with its answer acted on rather than discarded. Control only
        // reaches here when the account was absent from the caller's scoped LinkedAccountIds, so an
        // id already present in the unscoped set is held by a node or linked account this caller
        // cannot see. Adding and ignoring the result -- which is what this did -- meant a
        // caller-chosen CreateAccountRequest.AccountId matching another tenant's node id was linked
        // and assigned against anyway, and RemoveHiddenBy keys assignment visibility on NodeId
        // alone, so the owning tenant then inherited a stranger's assignment on their own node
        // (Codex review finding on PR #2871). ClaimNewNode refuses the identical collision on
        // creates, and the comment above already claimed this reservation was what caught it.
        if (!snap.AllNodeIds.Add(accountId))
        {
            throw new FundStructureTenantScopeException(
                $"Account {accountId} is not within the calling tenant's scope: its id is already "
                + "held by a fund-structure node or linked account this caller cannot see.");
        }

        snap.LinkedAccountIds.Add(accountId);

        // Only a first materialization claims, and only under the posture that established
        // ownership above. Under the deployment boundary an unattributed account is deliberately
        // visible to everyone, and taking it would hand a shared account to whichever tenant linked
        // it first -- the incidental-write claim StampCreatedNodesAsync refuses to make.
        if (_tenantScope.Mode == TenantScopeEnforcementMode.FailClosed)
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
    /// sleeve, a vehicle and an investment portfolio at once, and a caller-visible entity does not
    /// make a foreign fund theirs: accepting on the first visible parent would let an account that
    /// belongs to another tenant's fund through on the strength of an unrelated reference the
    /// caller happens to share.</para>
    ///
    /// <para><b>The portfolio counts as a parent, but only when it names one.</b> It is the one
    /// reference that is not a <see cref="Guid"/> on the DTO, and leaving it out made this refuse
    /// accounts whose only structural reference is a portfolio the caller can see -- a shape
    /// <c>CreateAccountRequest</c> supports and <c>FilterAccountsByScope</c> already resolves
    /// alongside the other four. A blank or non-GUID <c>PortfolioId</c> is a free-text reference
    /// rather than a node, so it neither counts as populated nor has to resolve.</para>
    ///
    /// <para>Parsing as a GUID is not enough on its own, though. The same field doubles as an
    /// external brokerage identifier -- <c>BrokeragePortfolioSyncService.ResolveLinkAsync</c> falls
    /// back to it for <c>ExternalAccountId</c> -- and a provider that issues UUID-shaped ids would
    /// otherwise have every one of its accounts treated as hanging off an investment portfolio no
    /// fund structure contains, hiding them from every read view and refusing every link (Codex
    /// review finding on PR #2871). <see cref="MutableSnapshot.AllNodeIds"/> settles it: it is the
    /// unscoped node set, so a portfolio another tenant holds is still recognised as a node and
    /// still refused, while an id belonging to no node at all is read as the external reference it
    /// is.</para>
    ///
    /// <para>An account with no parent at all is not within anyone's scope either: there is nothing
    /// to derive ownership from, and inventing it is the judgement this service quarantines rather
    /// than makes.</para>
    /// </remarks>
    private static bool IsAccountParentVisible(
        AccountSummaryDto account, MutableSnapshot snap, bool requireOwnershipEvidence)
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
            & ParentInScope(account.VehicleId, snap.Vehicles)
            & ParentInScope(StructuralPortfolioId(account.PortfolioId, snap), snap.InvestmentPortfolios);

        // A parent the snapshot does not hold is refused in either posture, because
        // FundStructureTenantScope.IsVisible already hides a node attributed to another tenant under
        // both -- only the unattributed row differs. Requiring at least one populated parent is the
        // part that is fail-closed only: under the deployment boundary an account with no structural
        // reference is not evidence of foreignness, it is the shared, unattributed shape that
        // posture exists to serve, and refusing it would break the default deployment.
        return allInScope && (!requireOwnershipEvidence || populated > 0);
    }

    /// <summary>
    /// The investment-portfolio node <paramref name="portfolioId"/> names, or <c>null</c> when it
    /// names no fund-structure node at all.
    /// </summary>
    /// <remarks>
    /// <para>Checked against the <b>unscoped</b> portfolio set on purpose. Scoping it would answer
    /// "not a node" for a portfolio another tenant holds, which is exactly the case that has to keep
    /// refusing; the question here is only whether the value identifies a portfolio at all, and the
    /// visibility of the one it identifies is <see cref="IsAccountParentVisible"/>'s to decide.</para>
    ///
    /// <para>Portfolios specifically, not <see cref="MutableSnapshot.AllNodeIds"/>. That set holds
    /// every node kind and every linked account, so an external id colliding with an unrelated
    /// fund, entity or account would be classified structural and then fail the investment-portfolio
    /// lookup below — hiding an account over a collision that says nothing about ownership (Codex
    /// review finding on PR #2871).</para>
    /// </remarks>
    private static Guid? StructuralPortfolioId(string? portfolioId, MutableSnapshot snap)
        => TryParseGuid(portfolioId, out var id) && snap.AllInvestmentPortfolioIds.Contains(id)
            ? id
            : null;
}
