using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// W9-GOV-008 criterion 2, fund-structure half. <c>PostgresFundStructureService</c> contained no
/// tenant column and no predicate anywhere: <c>LoadSnapshotAsync</c> took no scope and loaded every
/// organization, business, fund and relationship, <c>/api/fund-structure/graph</c> served that
/// snapshot, and the mutations resolved their parent nodes from the same global view. So a tenant-A
/// administrator could read tenant-B structure and link or mutate tenant-B nodes by id — while
/// <c>RequireFundScopedWriteTenant</c> passed them, because it proves only that a caller has
/// <i>some</i> tenant.
/// </summary>
/// <remarks>
/// Backed by <see cref="FakeFundStructureStore"/> rather than PostgreSQL, for the reason
/// <see cref="FundStructureScopeContractTests"/> gives: the scoping under test is service-layer, and
/// a suite that skips wherever no database is available would leave the leak unguarded in CI.
/// </remarks>
public sealed class FundStructureTenantScopeTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    private const string TenantAlpha = "tenant-alpha";
    private const string TenantBeta = "tenant-beta";

    [Fact]
    public async Task Graph_DoesNotServeAnotherTenantsStructure()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");

        var alphaService = CreateService(store, TenantAlpha);
        var nodeIds = await VisibleNodeIdsAsync(alphaService);

        Assert.Contains(alpha.OrganizationId, nodeIds);
        Assert.DoesNotContain(beta.OrganizationId, nodeIds);
        Assert.DoesNotContain(beta.BusinessId, nodeIds);
        Assert.DoesNotContain(beta.FundId, nodeIds);

        // And on the route the plan names by path: /api/fund-structure/graph.
        var fundGraphNodeIds = await FundGraphNodeIdsAsync(alphaService);
        Assert.Contains(alpha.FundId, fundGraphNodeIds);
        Assert.DoesNotContain(beta.FundId, fundGraphNodeIds);
    }

    [Fact]
    public async Task Mutation_CannotResolveAnotherTenantsNodeById()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var alphaService = CreateService(store, TenantAlpha);

        // The leak in its sharpest form: the caller knows the foreign id and supplies it directly.
        var createUnderForeignParent = () => alphaService.CreateBusinessAsync(new CreateBusinessRequest(
            Guid.NewGuid(), beta.OrganizationId, BusinessKindDto.FundManager,
            "BUS-X", "Business X", "USD", EffectiveFrom, "tenant-scope-test"));

        await Assert.ThrowsAsync<InvalidOperationException>(createUnderForeignParent);
    }

    [Fact]
    public async Task Create_CannotOverwriteAnotherTenantsNodeByReusingItsId()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var alphaService = CreateService(store, TenantAlpha);

        // Uniqueness has to span tenants. If it were checked against the caller's scoped view, the
        // foreign id would look free and this create would upsert straight over tenant-beta's node —
        // the read gate turned into a write leak.
        var reuseForeignId = () => alphaService.CreateOrganizationAsync(new CreateOrganizationRequest(
            beta.OrganizationId, "ORG-CLASH", "Organization Clash", "USD", EffectiveFrom, "tenant-scope-test"));

        await Assert.ThrowsAsync<InvalidOperationException>(reuseForeignId);

        var betaStructure = await CreateService(store, TenantBeta)
            .GetOrganizationStructureAsync(new OrganizationStructureQuery());
        var retained = Assert.Single(betaStructure.Organizations);
        Assert.Equal("ORG-BETA", retained.Code);
    }

    [Fact]
    public async Task Create_StampsTheCallersTenantOnEveryNewNode()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);

        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");

        // The organization is written outside PersistChangedAsync, so it is the node most likely to
        // be left unattributed by a stamping path that only covers the bulk persist.
        Assert.Equal(TenantAlpha, store.TenantOf(alpha.OrganizationId));
        Assert.Equal(TenantAlpha, store.TenantOf(alpha.BusinessId));
        Assert.Equal(TenantAlpha, store.TenantOf(alpha.FundId));
    }

    [Fact]
    public async Task Create_DoesNotClaimAPreExistingUnattributedNode()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);

        // A node the attribution quarantined - a shared ancestor, say - left deliberately unstamped.
        var sharedOrganization = Guid.NewGuid();
        await store.UpsertOrganizationAsync(new OrganizationSummaryDto(
            sharedOrganization, "ORG-SHARED", "Shared", "USD", true, EffectiveFrom, null, [], null));

        var alphaService = CreateService(store, TenantAlpha);
        await alphaService.CreateBusinessAsync(new CreateBusinessRequest(
            Guid.NewGuid(), sharedOrganization, BusinessKindDto.FundManager,
            "BUS-A", "Business A", "USD", EffectiveFrom, "tenant-scope-test"));

        // Writing beneath it must not hand the ancestor to whoever wrote next; that is precisely the
        // judgement FundStructureTenantAttribution declines to make and quarantines instead.
        Assert.Null(store.TenantOf(sharedOrganization));
    }

    [Fact]
    public async Task DeploymentBoundary_KeepsUnattributedNodesVisible()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var legacyOrganization = Guid.NewGuid();
        await store.UpsertOrganizationAsync(new OrganizationSummaryDto(
            legacyOrganization, "ORG-LEGACY", "Legacy", "USD", true, EffectiveFrom, null, [], null));

        var nodeIds = await VisibleNodeIdsAsync(CreateService(store, TenantAlpha));

        // The ordering constraint, stated as a test: rows the attribution has not reached stay
        // visible under the staging posture, so the backfill can land before the tightening without
        // a scoped reader losing data in between.
        Assert.Contains(legacyOrganization, nodeIds);
    }

    [Fact]
    public async Task FailClosed_HidesUnattributedNodesFromAScopedCaller()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var legacyOrganization = Guid.NewGuid();
        await store.UpsertOrganizationAsync(new OrganizationSummaryDto(
            legacyOrganization, "ORG-LEGACY", "Legacy", "USD", true, EffectiveFrom, null, [], null));

        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");

        var nodeIds = await VisibleNodeIdsAsync(CreateService(
            store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed));

        Assert.Contains(alpha.OrganizationId, nodeIds);
        Assert.DoesNotContain(legacyOrganization, nodeIds);
    }

    [Fact]
    public async Task FailClosed_RejectsACallerWithNoResolvableTenantRatherThanDefaultingTheRead()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");

        var tenantless = CreateService(store, callerTenantId: null, TenantScopeEnforcementOptions.FailClosed);

        // Not an empty graph: the caller could not tell that apart from a genuinely empty structure,
        // and neither could an operator reading the support ticket it produced.
        await Assert.ThrowsAsync<FundStructureTenantScopeException>(
            () => tenantless.GetOrganizationStructureAsync(new OrganizationStructureQuery()));
    }

    [Fact]
    public async Task DeploymentBoundary_StillServesATenantlessCaller()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");

        var nodeIds = await VisibleNodeIdsAsync(CreateService(store, callerTenantId: null));

        // Single-company deployments and the legacy tenantless admin profile keep working until the
        // deployment opts in to the tightened posture.
        Assert.NotEmpty(nodeIds);
    }

    [Fact]
    public async Task UnpartitionedStore_IsUnaffectedByTenantScoping()
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: false);
        await SeedOrganizationAsync(store, TenantBeta, "BETA");

        var nodeIds = await VisibleNodeIdsAsync(CreateService(
            store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed));

        // A store that does not model ownership has none to enforce. The in-memory and JSON-backed
        // stores are barred from production compositions by ADR-019, not by this check.
        Assert.NotEmpty(nodeIds);
    }

    /// <summary>
    /// Every node id the organization-structure read would serve this caller. That surface is the
    /// one that exposes organizations and businesses; the fund-structure graph is fund-centric and
    /// never carries the upper hierarchy, so asserting the ancestor leak needs this one.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> VisibleNodeIdsAsync(PostgresFundStructureService service)
    {
        var structure = await service.GetOrganizationStructureAsync(new OrganizationStructureQuery());
        return
        [
            .. structure.Organizations.Select(item => item.OrganizationId),
            .. structure.Businesses.Select(item => item.BusinessId),
            .. structure.Clients.Select(item => item.ClientId),
            .. structure.Funds.Select(item => item.FundId),
        ];
    }

    /// <summary>The node ids <c>/api/fund-structure/graph</c> itself would serve this caller.</summary>
    private static async Task<IReadOnlyList<Guid>> FundGraphNodeIdsAsync(PostgresFundStructureService service)
    {
        var graph = await service.GetFundStructureGraphAsync(new FundStructureQuery());
        return [.. graph.Nodes.Select(node => node.NodeId)];
    }

    private sealed record SeededOrganization(Guid OrganizationId, Guid BusinessId, Guid FundId);

    private static async Task<SeededOrganization> SeedOrganizationAsync(
        FakeFundStructureStore store,
        string tenantId,
        string tag)
    {
        var service = CreateService(store, tenantId);
        var seeded = new SeededOrganization(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await service.CreateOrganizationAsync(new CreateOrganizationRequest(
            seeded.OrganizationId, $"ORG-{tag}", $"Organization {tag}", "USD", EffectiveFrom, "tenant-scope-test"));

        await service.CreateBusinessAsync(new CreateBusinessRequest(
            seeded.BusinessId, seeded.OrganizationId, BusinessKindDto.FundManager,
            $"BUS-{tag}", $"Business {tag}", "USD", EffectiveFrom, "tenant-scope-test"));

        await service.CreateFundAsync(new CreateFundRequest(
            seeded.FundId, $"FND-{tag}", $"Fund {tag}", "USD", EffectiveFrom, "tenant-scope-test",
            BusinessId: seeded.BusinessId));

        return seeded;
    }

    [Fact]
    public async Task LegalEntityUpdate_CannotReachAnotherTenantsEntity()
    {
        // Codex review finding on PR #2866. UpdateLegalEntityProfileAsync read and wrote the entity
        // straight through the store, never through LoadSnapshotAsync -- which is where the tenant
        // filtering lives. So the one mutation that skipped the snapshot also skipped the gate, and
        // a tenant-A caller holding tenant B's entity id could overwrite it in fail-closed mode.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var betaEntityId = Guid.NewGuid();

        var betaService = CreateService(store, TenantBeta);
        await betaService.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            betaEntityId, LegalEntityTypeDto.ManagementCompany, "ENT-BETA", "Beta Entity",
            "US", "USD", EffectiveFrom, "tenant-scope-test"));

        var alphaService = CreateService(store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed);
        var hijack = async () => await alphaService.UpdateLegalEntityProfileAsync(
            new UpdateLegalEntityProfileRequest(betaEntityId, "attacker", Name: "Renamed By Alpha"));

        await Assert.ThrowsAsync<InvalidOperationException>(hijack);

        var retained = await store.GetLegalEntityAsync(betaEntityId);
        Assert.Equal("Beta Entity", retained!.Name);
    }

    [Fact]
    public async Task ANewlyCreatedLegalEntity_IsStampedWithItsCreatorsTenant()
    {
        // Codex review finding on PR #2866. This create path writes the entity directly rather than
        // through PersistChangedAsync, so it never reached the stamping the other creates get: the
        // entity stayed unattributed, and its own creator would lose sight of it the moment the
        // deployment went fail-closed.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var entityId = Guid.NewGuid();

        var service = CreateService(store, TenantAlpha);
        await service.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            entityId, LegalEntityTypeDto.ManagementCompany, "ENT-ALPHA", "Alpha Entity",
            "US", "USD", EffectiveFrom, "tenant-scope-test"));

        Assert.Equal(TenantAlpha, store.TenantOf(entityId));

        // And its creator can still reach it once the deployment tightens.
        var failClosed = CreateService(store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed);
        var updated = await failClosed.UpdateLegalEntityProfileAsync(
            new UpdateLegalEntityProfileRequest(entityId, "owner", Name: "Alpha Entity Renamed"));
        Assert.Equal("Alpha Entity Renamed", updated.Name);
    }

    [Fact]
    public async Task LinkCreate_CannotOverwriteAnotherTenantsOwnershipLinkByReusingItsId()
    {
        // Nodes were given cross-tenant uniqueness on PR #2866; edges were not. LinkNodesAsync
        // checked the id against the SCOPED snapshot, and UpsertOwnershipLinkAsync is an
        // unconditional ON CONFLICT (ownership_link_id) DO UPDATE — so a foreign link id looked free
        // precisely because scoping had filtered the row out, and the create rewrote another
        // tenant's edge to point at the caller's own nodes.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var betaLinkId = Guid.NewGuid();
        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var betaEntityId = await SeedOwnedEntityAsync(store, TenantBeta, beta.FundId, betaLinkId, "BETA");

        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");
        var alphaService = CreateService(store, TenantAlpha);
        var alphaEntity = await alphaService.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            Guid.NewGuid(), LegalEntityTypeDto.LimitedPartner, "LP-ALPHA", "Alpha Limited Partner",
            "US", "USD", EffectiveFrom, "tenant-scope-test"));

        // A link that is valid in every respect except its identifier, so the refusal below can only
        // be the collision and not an ownership-policy rejection standing in for one.
        var reuseForeignLinkId = () => alphaService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            betaLinkId, alpha.FundId, alphaEntity.EntityId,
            OwnershipRelationshipTypeDto.Owns, EffectiveFrom, "tenant-scope-test",
            OwnershipPercent: 60m));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(reuseForeignLinkId);
        Assert.Contains("already exists", refusal.Message, StringComparison.Ordinal);

        var retained = Assert.Single(
            await store.GetAllOwnershipLinksAsync(),
            link => link.OwnershipLinkId == betaLinkId);
        Assert.Equal(beta.FundId, retained.ParentNodeId);
        Assert.Equal(betaEntityId, retained.ChildNodeId);
    }

    [Fact]
    public async Task OwnershipReplacement_CannotOverwriteAnotherTenantsLinkByReusingItsIdAsTheReplacement()
    {
        // Same leak through the other door: the replacement identifier is a create, and it was
        // checked against the scoped snapshot too. The existing-link lookup beside it stays scoped —
        // not finding another tenant's link is the correct answer for that one.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var betaLinkId = Guid.NewGuid();
        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var betaEntityId = await SeedOwnedEntityAsync(store, TenantBeta, beta.FundId, betaLinkId, "BETA");

        var alphaLinkId = Guid.NewGuid();
        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");
        var alphaEntityId = await SeedOwnedEntityAsync(store, TenantAlpha, alpha.FundId, alphaLinkId, "ALPHA");

        var alphaService = CreateService(store, TenantAlpha);
        var reuseForeignLinkId = () => alphaService.ReplaceOwnershipLinkAsync(new ReplaceOwnershipLinkRequest(
            alphaLinkId, betaLinkId, alpha.FundId, alphaEntityId,
            OwnershipRelationshipTypeDto.Owns, EffectiveFrom.AddDays(1), "tenant-scope-test",
            OwnershipPercent: 60m));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(reuseForeignLinkId);
        Assert.Contains("already exists", refusal.Message, StringComparison.Ordinal);

        var retained = Assert.Single(
            await store.GetAllOwnershipLinksAsync(),
            link => link.OwnershipLinkId == betaLinkId);
        Assert.Equal(betaEntityId, retained.ChildNodeId);
    }

    [Fact]
    public async Task AssignmentCreate_CannotOverwriteAnotherTenantsAssignmentByReusingItsId()
    {
        // An assignment carries the ledger grouping a node posts under, and UpsertAssignmentAsync is
        // likewise an unconditional ON CONFLICT DO UPDATE — so the scoped uniqueness check let one
        // tenant silently re-point another tenant's postings at their own node.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var assignmentId = Guid.NewGuid();

        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        await CreateService(store, TenantBeta).AssignNodeAsync(new AssignFundStructureNodeRequest(
            assignmentId, beta.FundId, LedgerGroupingRules.LedgerGroupAssignmentType,
            "BETA.OPS:PRIMARY", EffectiveFrom, "tenant-scope-test"));

        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");
        var alphaService = CreateService(store, TenantAlpha);
        var reuseForeignAssignmentId = () => alphaService.AssignNodeAsync(new AssignFundStructureNodeRequest(
            assignmentId, alpha.FundId, LedgerGroupingRules.LedgerGroupAssignmentType,
            "ALPHA.OPS:PRIMARY", EffectiveFrom, "tenant-scope-test"));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(reuseForeignAssignmentId);
        Assert.Contains("already exists", refusal.Message, StringComparison.Ordinal);

        var retained = Assert.Single(await store.GetAllAssignmentsAsync());
        Assert.Equal(beta.FundId, retained.NodeId);
        Assert.Equal("BETA.OPS:PRIMARY", retained.AssignmentReference);
    }

    [Fact]
    public async Task LinkingAnAccount_StampsItSoTheEdgeSurvivesTheFailClosedPosture()
    {
        // An account only becomes a fund-structure node when an edge draws it in, and that
        // materialization was never stamped. Under fail-closed the unattributed account is hidden
        // from its own creator on the next read — and an edge is served only when both endpoints are
        // visible, so the link the caller had just written vanished from their own graph.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var accountService = new InMemoryFundAccountService();
        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");

        var account = await accountService.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(), AccountTypeDto.Custody, "ACC-ALPHA", "Alpha Custody Account",
            "USD", EffectiveFrom, "tenant-scope-test", FundId: alpha.FundId));

        var linkId = Guid.NewGuid();

        // Fail-closed, because that is the posture whose account-store predicate is
        // "tenant_id = caller" -- so resolving the account is positive proof the caller owns it, and
        // stamping the node merely restates what the account store already says.
        var alphaService = CreateService(
            store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed, accountService);
        await alphaService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            linkId, alpha.FundId, account.AccountId,
            OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "tenant-scope-test"));

        Assert.Equal(TenantAlpha, store.TenantOf(account.AccountId));

        var failClosed = CreateService(
            store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed, accountService);
        var graph = await failClosed.GetFundStructureGraphAsync(new FundStructureQuery());
        Assert.Contains(graph.OwnershipLinks, link => link.OwnershipLinkId == linkId);
        Assert.Contains(graph.Nodes, node => node.NodeId == account.AccountId);
    }

    [Fact]
    public async Task UnderTheBoundaryPosture_LinkingAnUnattributedAccount_DoesNotClaimIt()
    {
        // Codex review finding on PR #2871. Reaching MaterializeLinkedAccount means the account store
        // resolved the account, and what that proves depends on the posture. Under the boundary
        // posture the store's predicate also admits tenant_id is null, so an unattributed account
        // resolves for anyone -- and claiming it would hand a shared account to whichever tenant
        // linked it first, the judgement this codebase quarantines rather than makes. Nothing is lost
        // by declining: unattributed nodes are visible to everyone under that posture.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var accountService = new InMemoryFundAccountService();
        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");

        var account = await accountService.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(), AccountTypeDto.Custody, "ACC-UNATTRIBUTED", "Unattributed Account",
            "USD", EffectiveFrom, "tenant-scope-test", FundId: alpha.FundId));

        var boundaryService = CreateService(
            store, TenantAlpha, TenantScopeEnforcementOptions.DeploymentBoundary, accountService);
        await boundaryService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(), alpha.FundId, account.AccountId,
            OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "tenant-scope-test"));

        Assert.Null(store.TenantOf(account.AccountId));
    }

    [Fact]
    public async Task FailClosed_DoesNotClaimAnAccountWhoseParentTheCallerCannotSee()
    {
        // Second Codex review finding on PR #2871. Gating the claim on the fail-closed posture
        // assumed resolution had already proved ownership -- true of PostgresFundAccountStore, whose
        // read carries the tenant predicate, and false of InMemoryFundAccountService, which filters
        // nothing. IFundAccountService promises neither, so ownership is established from the
        // account's own parents against the caller's scoped snapshot instead.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var accountService = new InMemoryFundAccountService();

        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var betaAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(), AccountTypeDto.Custody, "ACC-BETA", "Beta Custody Account",
            "USD", EffectiveFrom, "tenant-scope-test", FundId: beta.FundId));

        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");
        var alphaService = CreateService(
            store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed, accountService);

        // Third Codex review round: declining the stamp was not enough. The link and the
        // linked-account id were still written, so the relationship survived unattributed -- and
        // whenever the account was later attributed to tenant-beta, beta inherited a relationship
        // tenant-alpha had authored. The mutation is refused outright.
        var hijackLinkId = Guid.NewGuid();
        var hijack = () => alphaService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            hijackLinkId, alpha.FundId, betaAccount.AccountId,
            OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "tenant-scope-test"));

        await Assert.ThrowsAsync<FundStructureTenantScopeException>(hijack);

        Assert.Null(store.TenantOf(betaAccount.AccountId));
        Assert.DoesNotContain(
            await store.GetAllOwnershipLinksAsync(), link => link.OwnershipLinkId == hijackLinkId);
        Assert.DoesNotContain(betaAccount.AccountId, await store.GetAllLinkedAccountIdsAsync());
    }

    [Fact]
    public async Task FailClosed_RefusesAnAccountAlreadyStandingAsAnotherTenantsNode()
    {
        // The same check has to run for every link, not only a first materialization: an account
        // already materialized as tenant-beta's node fails the AllNodeIds reservation, so a check
        // gated on that reservation would wave exactly the foreign account through.
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var accountService = new InMemoryFundAccountService();

        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var betaAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(), AccountTypeDto.Custody, "ACC-BETA2", "Beta Custody Account",
            "USD", EffectiveFrom, "tenant-scope-test", FundId: beta.FundId));

        await CreateService(store, TenantBeta, TenantScopeEnforcementOptions.FailClosed, accountService)
            .LinkNodesAsync(new LinkFundStructureNodesRequest(
                Guid.NewGuid(), beta.FundId, betaAccount.AccountId,
                OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "tenant-scope-test"));

        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");
        var alphaService = CreateService(
            store, TenantAlpha, TenantScopeEnforcementOptions.FailClosed, accountService);

        var hijackLinkId = Guid.NewGuid();
        var hijack = () => alphaService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            hijackLinkId, alpha.FundId, betaAccount.AccountId,
            OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "tenant-scope-test"));

        await Assert.ThrowsAsync<FundStructureTenantScopeException>(hijack);

        Assert.Equal(TenantBeta, store.TenantOf(betaAccount.AccountId));
        Assert.DoesNotContain(
            await store.GetAllOwnershipLinksAsync(), link => link.OwnershipLinkId == hijackLinkId);
    }

    [Fact]
    public async Task UnderTheBoundaryPosture_AnAccountAnotherTenantHolds_IsSharedButNotReclaimed()
    {
        // The mirror of Create_DoesNotClaimAPreExistingUnattributedNode, for accounts. Under the
        // deployment boundary the shared link is still permitted — that posture's whole premise is
        // that the deployment is the control — but ownership must not move to whoever links next,
        // which is the incidental-write claim the stamping refuses to make. (Under fail-closed the
        // same attempt is refused outright; see
        // FailClosed_RefusesAnAccountAlreadyStandingAsAnotherTenantsNode.)
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var accountService = new InMemoryFundAccountService();

        var beta = await SeedOrganizationAsync(store, TenantBeta, "BETA");
        var account = await accountService.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(), AccountTypeDto.Custody, "ACC-SHARED", "Shared Custody Account",
            "USD", EffectiveFrom, "tenant-scope-test", FundId: beta.FundId));

        await CreateService(store, TenantBeta, TenantScopeEnforcementOptions.FailClosed, accountService)
            .LinkNodesAsync(new LinkFundStructureNodesRequest(
                Guid.NewGuid(), beta.FundId, account.AccountId,
                OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "tenant-scope-test"));

        var alpha = await SeedOrganizationAsync(store, TenantAlpha, "ALPHA");
        await CreateService(
                store, TenantAlpha, TenantScopeEnforcementOptions.DeploymentBoundary, accountService)
            .LinkNodesAsync(new LinkFundStructureNodesRequest(
                Guid.NewGuid(), alpha.FundId, account.AccountId,
                OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "tenant-scope-test"));

        Assert.Equal(TenantBeta, store.TenantOf(account.AccountId));
    }

    /// <summary>
    /// Creates a legal entity owned by <paramref name="fundId"/> under a caller in
    /// <paramref name="tenantId"/>, using <paramref name="ownershipLinkId"/> for the edge.
    /// </summary>
    private static async Task<Guid> SeedOwnedEntityAsync(
        FakeFundStructureStore store,
        string tenantId,
        Guid fundId,
        Guid ownershipLinkId,
        string tag)
    {
        var service = CreateService(store, tenantId);
        var entity = await service.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            Guid.NewGuid(), LegalEntityTypeDto.LimitedPartner, $"LP-{tag}", $"{tag} Limited Partner",
            "US", "USD", EffectiveFrom, "tenant-scope-test"));

        await service.LinkNodesAsync(new LinkFundStructureNodesRequest(
            ownershipLinkId, fundId, entity.EntityId,
            OwnershipRelationshipTypeDto.Owns, EffectiveFrom, "tenant-scope-test",
            OwnershipPercent: 60m));

        return entity.EntityId;
    }

    private static PostgresFundStructureService CreateService(
        FakeFundStructureStore store,
        string? callerTenantId,
        TenantScopeEnforcementOptions? tenantScope = null,
        InMemoryFundAccountService? accountService = null)
        => new(
            store,
            accountService ?? new InMemoryFundAccountService(),
            new FundStructurePolicyService(),
            tenantAccessor: new StubTenantAccessor(callerTenantId),
            tenantScope: tenantScope);

    private sealed class StubTenantAccessor(string? tenantId) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenantId;
    }
}
