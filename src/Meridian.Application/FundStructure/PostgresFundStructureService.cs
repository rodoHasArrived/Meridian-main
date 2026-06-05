using Meridian.Application.Composition;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IFundStructureService"/>.
/// On each mutation the full snapshot is loaded from the store, updated in-memory,
/// and the changed rows are written back.
/// </summary>
public sealed class PostgresFundStructureService : IFundStructureService
{
    private readonly IFundStructureStore _store;
    private readonly IFundAccountService _fundAccountService;
    private readonly IGovernanceSharedDataAccessService? _sharedDataAccessService;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IFundStructurePolicyService _policy;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PostgresFundStructureService(
        IFundStructureStore store,
        IFundAccountService fundAccountService,
        IFundStructurePolicyService policyService,
        IGovernanceSharedDataAccessService? sharedDataAccessService = null,
        ISecurityMasterQueryService? securityMasterQueryService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _policy = policyService ?? throw new ArgumentNullException(nameof(policyService));
        _sharedDataAccessService = sharedDataAccessService;
        _securityMasterQueryService = securityMasterQueryService;
    }

    // ── Create operations ─────────────────────────────────────────────────────

    public async Task<OrganizationSummaryDto> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.OrganizationId, snap);

            var summary = new OrganizationSummaryDto(
                request.OrganizationId,
                request.Code,
                request.Name,
                request.BaseCurrency,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                BusinessIds: [],
                request.Description);

            await _store.UpsertOrganizationAsync(summary, ct).ConfigureAwait(false);
            return summary;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<BusinessSummaryDto> CreateBusinessAsync(
        CreateBusinessRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.BusinessId, snap);
            EnsureExistsInDict(snap.Organizations, request.OrganizationId, "Organization");

            var summary = new BusinessSummaryDto(
                request.BusinessId,
                request.OrganizationId,
                request.BusinessKind,
                request.Code,
                request.Name,
                request.BaseCurrency,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                ClientIds: [],
                FundIds: [],
                InvestmentPortfolioIds: [],
                request.Description);

            snap.Businesses[request.BusinessId] = summary;
            var link = MakeAutoLink(request.OrganizationId, request.BusinessId,
                OwnershipRelationshipTypeDto.Owns, request.EffectiveFrom, "Organization root");
            snap.OwnershipLinks[link.OwnershipLinkId] = link;
            ApplyOwnershipLink(link, snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return snap.Businesses[request.BusinessId];
        }
        finally { _writeLock.Release(); }
    }

    public async Task<ClientSummaryDto> CreateClientAsync(
        CreateClientRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.ClientId, snap);
            EnsureExistsInDict(snap.Businesses, request.BusinessId, "Business");

            var summary = new ClientSummaryDto(
                request.ClientId,
                request.BusinessId,
                request.Code,
                request.Name,
                request.BaseCurrency,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                InvestmentPortfolioIds: [],
                request.Description,
                request.ClientSegmentKind);

            snap.Clients[request.ClientId] = summary;
            var link = MakeAutoLink(request.BusinessId, request.ClientId,
                OwnershipRelationshipTypeDto.Advises, request.EffectiveFrom, "Advisory client");
            snap.OwnershipLinks[link.OwnershipLinkId] = link;
            ApplyOwnershipLink(link, snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return snap.Clients[request.ClientId];
        }
        finally { _writeLock.Release(); }
    }

    public async Task<FundSummaryDto> CreateFundAsync(
        CreateFundRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.FundId, snap);
            if (request.BusinessId.HasValue)
                EnsureExistsInDict(snap.Businesses, request.BusinessId.Value, "Business");

            var summary = new FundSummaryDto(
                request.FundId,
                request.BusinessId,
                request.Code,
                request.Name,
                request.BaseCurrency,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                SleeveIds: [],
                VehicleIds: [],
                EntityIds: [],
                InvestmentPortfolioIds: [],
                AccountIds: [],
                request.Description);

            snap.Funds[request.FundId] = summary;
            if (request.BusinessId.HasValue)
            {
                var link = MakeAutoLink(request.BusinessId.Value, request.FundId,
                    OwnershipRelationshipTypeDto.Operates, request.EffectiveFrom, "Fund operating business");
                snap.OwnershipLinks[link.OwnershipLinkId] = link;
                ApplyOwnershipLink(link, snap);
            }
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return snap.Funds[request.FundId];
        }
        finally { _writeLock.Release(); }
    }

    public async Task<SleeveSummaryDto> CreateSleeveAsync(
        CreateSleeveRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.SleeveId, snap);
            EnsureExistsInDict(snap.Funds, request.FundId, "Fund");

            var summary = new SleeveSummaryDto(
                request.SleeveId,
                request.FundId,
                request.Code,
                request.Name,
                request.Mandate,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                request.StrategyIds?.ToList() ?? [],
                InvestmentPortfolioIds: [],
                AccountIds: []);

            snap.Sleeves[request.SleeveId] = summary;
            var link = MakeAutoLink(request.FundId, request.SleeveId,
                OwnershipRelationshipTypeDto.AllocatesTo, request.EffectiveFrom, "Fund sleeve");
            snap.OwnershipLinks[link.OwnershipLinkId] = link;
            ApplyOwnershipLink(link, snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return snap.Sleeves[request.SleeveId];
        }
        finally { _writeLock.Release(); }
    }

    public async Task<VehicleSummaryDto> CreateVehicleAsync(
        CreateVehicleRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.VehicleId, snap);
            EnsureExistsInDict(snap.Funds, request.FundId, "Fund");
            EnsureExistsInDict(snap.Entities, request.LegalEntityId, "LegalEntity");

            var summary = new VehicleSummaryDto(
                request.VehicleId,
                request.FundId,
                request.LegalEntityId,
                request.Code,
                request.Name,
                request.BaseCurrency,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                InvestmentPortfolioIds: [],
                AccountIds: [],
                request.Description);

            snap.Vehicles[request.VehicleId] = summary;
            var link1 = MakeAutoLink(request.FundId, request.VehicleId,
                OwnershipRelationshipTypeDto.Owns, request.EffectiveFrom, "Fund vehicle");
            snap.OwnershipLinks[link1.OwnershipLinkId] = link1;
            ApplyOwnershipLink(link1, snap);
            var link2 = MakeAutoLink(request.LegalEntityId, request.VehicleId,
                OwnershipRelationshipTypeDto.Owns, request.EffectiveFrom, "Legal entity vehicle");
            snap.OwnershipLinks[link2.OwnershipLinkId] = link2;
            ApplyOwnershipLink(link2, snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return snap.Vehicles[request.VehicleId];
        }
        finally { _writeLock.Release(); }
    }

    public async Task<LegalEntitySummaryDto> CreateLegalEntityAsync(
        CreateLegalEntityRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.EntityId, snap);

            var summary = new LegalEntitySummaryDto(
                request.EntityId,
                request.EntityType,
                request.Code,
                request.Name,
                request.Jurisdiction,
                request.BaseCurrency,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                request.Description);

            await _store.UpsertLegalEntityAsync(summary, ct).ConfigureAwait(false);
            return summary;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<InvestmentPortfolioSummaryDto> CreateInvestmentPortfolioAsync(
        CreateInvestmentPortfolioRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        _policy.EnsureSingleOperatingParent(request);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            EnsureUniqueNode(request.InvestmentPortfolioId, snap);
            EnsureExistsInDict(snap.Businesses, request.BusinessId, "Business");
            if (request.ClientId.HasValue) EnsureExistsInDict(snap.Clients, request.ClientId.Value, "Client");
            if (request.FundId.HasValue) EnsureExistsInDict(snap.Funds, request.FundId.Value, "Fund");
            if (request.SleeveId.HasValue) EnsureExistsInDict(snap.Sleeves, request.SleeveId.Value, "Sleeve");
            if (request.VehicleId.HasValue) EnsureExistsInDict(snap.Vehicles, request.VehicleId.Value, "Vehicle");
            if (request.EntityId.HasValue) EnsureExistsInDict(snap.Entities, request.EntityId.Value, "LegalEntity");

            var summary = new InvestmentPortfolioSummaryDto(
                request.InvestmentPortfolioId,
                request.BusinessId,
                request.Code,
                request.Name,
                request.BaseCurrency,
                IsActive: true,
                request.EffectiveFrom,
                EffectiveTo: null,
                request.ClientId,
                request.FundId,
                request.SleeveId,
                request.VehicleId,
                request.EntityId,
                AccountIds: [],
                request.Description);

            snap.InvestmentPortfolios[request.InvestmentPortfolioId] = summary;

            void AutoLink(Guid parentId, OwnershipRelationshipTypeDto rel, string notes)
            {
                var lnk = MakeAutoLink(parentId, request.InvestmentPortfolioId, rel, request.EffectiveFrom, notes);
                snap.OwnershipLinks[lnk.OwnershipLinkId] = lnk;
                ApplyOwnershipLink(lnk, snap);
            }

            AutoLink(request.BusinessId, OwnershipRelationshipTypeDto.Operates, "Business operating portfolio");
            if (request.ClientId.HasValue) AutoLink(request.ClientId.Value, OwnershipRelationshipTypeDto.Owns, "Client investment portfolio");
            if (request.FundId.HasValue) AutoLink(request.FundId.Value, OwnershipRelationshipTypeDto.AllocatesTo, "Fund investment portfolio");
            if (request.SleeveId.HasValue) AutoLink(request.SleeveId.Value, OwnershipRelationshipTypeDto.AllocatesTo, "Sleeve investment portfolio");
            if (request.VehicleId.HasValue) AutoLink(request.VehicleId.Value, OwnershipRelationshipTypeDto.Owns, "Vehicle investment portfolio");
            if (request.EntityId.HasValue) AutoLink(request.EntityId.Value, OwnershipRelationshipTypeDto.Owns, "Entity overlay");

            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return snap.InvestmentPortfolios[request.InvestmentPortfolioId];
        }
        finally { _writeLock.Release(); }
    }

    public async Task<OwnershipLinkDto> LinkNodesAsync(
        LinkFundStructureNodesRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var parentKind = await ResolveNodeKindAsync(request.ParentNodeId, ct).ConfigureAwait(false);
        var childKind = await ResolveNodeKindAsync(request.ChildNodeId, ct).ConfigureAwait(false);
        if (parentKind is null) throw new InvalidOperationException($"Parent node {request.ParentNodeId} was not found.");
        if (childKind is null) throw new InvalidOperationException($"Child node {request.ChildNodeId} was not found.");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            if (snap.OwnershipLinks.ContainsKey(request.OwnershipLinkId))
                throw new InvalidOperationException($"Ownership link {request.OwnershipLinkId} already exists.");

            var link = new OwnershipLinkDto(
                request.OwnershipLinkId,
                request.ParentNodeId,
                request.ChildNodeId,
                request.RelationshipType,
                request.OwnershipPercent,
                request.IsPrimary,
                request.EffectiveFrom,
                EffectiveTo: null,
                request.Notes);

            var nodeKinds = CaptureNodeKinds(snap);
            nodeKinds[request.ParentNodeId] = parentKind.Value;
            nodeKinds[request.ChildNodeId] = childKind.Value;
            _policy.ValidateOwnershipLink(
                link,
                parentKind.Value,
                childKind.Value,
                snap.OwnershipLinks.Values.ToList(),
                nodeKinds);

            if (parentKind == FundStructureNodeKindDto.Account) snap.LinkedAccountIds.Add(request.ParentNodeId);
            if (childKind == FundStructureNodeKindDto.Account) snap.LinkedAccountIds.Add(request.ChildNodeId);

            snap.OwnershipLinks[link.OwnershipLinkId] = link;
            ApplyOwnershipLink(link, snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return link;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<OwnershipLinkDto> UpdateOwnershipLinkAsync(
        UpdateOwnershipLinkRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            if (!snap.OwnershipLinks.TryGetValue(request.OwnershipLinkId, out var existing))
                throw new InvalidOperationException($"Ownership link {request.OwnershipLinkId} was not found.");

            var updated = existing with
            {
                RelationshipType = request.RelationshipType,
                OwnershipPercent = request.OwnershipPercent,
                IsPrimary = request.IsPrimary,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo,
                Notes = request.Notes
            };
            var nodeKinds = CaptureNodeKinds(snap);
            var parentKind = ResolveKnownNodeKind(existing.ParentNodeId, nodeKinds);
            var childKind = ResolveKnownNodeKind(existing.ChildNodeId, nodeKinds);
            _policy.ValidateOwnershipUpdate(
                existing,
                updated,
                parentKind,
                childKind,
                snap.OwnershipLinks.Values.ToList(),
                nodeKinds);

            snap.OwnershipLinks[updated.OwnershipLinkId] = updated;
            RebuildOwnershipProjections(snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return updated;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<OwnershipLinkDto> ExpireOwnershipLinkAsync(
        ExpireOwnershipLinkRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            if (!snap.OwnershipLinks.TryGetValue(request.OwnershipLinkId, out var existing))
                throw new InvalidOperationException($"Ownership link {request.OwnershipLinkId} was not found.");
            _policy.ValidateOwnershipExpiration(existing, request.EffectiveTo);

            var notes = string.IsNullOrWhiteSpace(request.Notes) ? existing.Notes : request.Notes;
            var expired = existing with { EffectiveTo = request.EffectiveTo, Notes = notes };
            snap.OwnershipLinks[expired.OwnershipLinkId] = expired;
            RebuildOwnershipProjections(snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return expired;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<OwnershipLinkDto> ReplaceOwnershipLinkAsync(
        ReplaceOwnershipLinkRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var parentKind = await ResolveNodeKindAsync(request.ParentNodeId, ct).ConfigureAwait(false);
        var childKind = await ResolveNodeKindAsync(request.ChildNodeId, ct).ConfigureAwait(false);
        if (parentKind is null) throw new InvalidOperationException($"Parent node {request.ParentNodeId} was not found.");
        if (childKind is null) throw new InvalidOperationException($"Child node {request.ChildNodeId} was not found.");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            if (!snap.OwnershipLinks.TryGetValue(request.OwnershipLinkId, out var existing))
                throw new InvalidOperationException($"Ownership link {request.OwnershipLinkId} was not found.");
            if (snap.OwnershipLinks.ContainsKey(request.ReplacementOwnershipLinkId))
                throw new InvalidOperationException($"Ownership link {request.ReplacementOwnershipLinkId} already exists.");

            var replacedEffectiveTo = request.ReplacedEffectiveTo ?? request.EffectiveFrom;
            var expiredExisting = existing with { EffectiveTo = replacedEffectiveTo };
            var replacement = new OwnershipLinkDto(
                request.ReplacementOwnershipLinkId,
                request.ParentNodeId,
                request.ChildNodeId,
                request.RelationshipType,
                request.OwnershipPercent,
                request.IsPrimary,
                request.EffectiveFrom,
                EffectiveTo: null,
                request.Notes);
            var nodeKinds = CaptureNodeKinds(snap);
            nodeKinds[request.ParentNodeId] = parentKind.Value;
            nodeKinds[request.ChildNodeId] = childKind.Value;
            _policy.ValidateOwnershipReplacement(
                existing,
                expiredExisting,
                replacement,
                parentKind.Value,
                childKind.Value,
                snap.OwnershipLinks.Values.ToList(),
                nodeKinds);

            snap.OwnershipLinks[existing.OwnershipLinkId] = expiredExisting;
            if (parentKind == FundStructureNodeKindDto.Account) snap.LinkedAccountIds.Add(request.ParentNodeId);
            if (childKind == FundStructureNodeKindDto.Account) snap.LinkedAccountIds.Add(request.ChildNodeId);

            snap.OwnershipLinks[replacement.OwnershipLinkId] = replacement;
            RebuildOwnershipProjections(snap);
            await PersistChangedAsync(snap, ct).ConfigureAwait(false);
            return replacement;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<OwnershipGraphValidationResultDto> ValidateOwnershipGraphAsync(
        ValidateOwnershipGraphRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var issues = ValidateOwnershipGraph(snap, request.ActiveOnly, asOf);
        return new OwnershipGraphValidationResultDto(issues.Count == 0, asOf, issues);
    }

    public async Task<FundStructureAssignmentDto> AssignNodeAsync(
        AssignFundStructureNodeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var kind = await ResolveNodeKindAsync(request.NodeId, ct).ConfigureAwait(false);
        if (kind is null) throw new InvalidOperationException($"Node {request.NodeId} was not found.");

        var normalizedRef = LedgerGroupingRules.NormalizeAssignmentReference(
            request.AssignmentType, request.AssignmentReference);
        var assignment = new FundStructureAssignmentDto(
            request.AssignmentId,
            request.NodeId,
            request.AssignmentType,
            normalizedRef,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.IsPrimary);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
            if (snap.Assignments.ContainsKey(request.AssignmentId))
                throw new InvalidOperationException($"Assignment {request.AssignmentId} already exists.");
            if (kind == FundStructureNodeKindDto.Account) snap.LinkedAccountIds.Add(request.NodeId);
            await _store.UpsertAssignmentAsync(assignment, ct).ConfigureAwait(false);
            return assignment;
        }
        finally { _writeLock.Release(); }
    }

    // ── Query operations ──────────────────────────────────────────────────────

    public async Task<OrganizationStructureGraphDto> GetOrganizationStructureAsync(
        OrganizationStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snap = (await LoadSnapshotAsync(ct).ConfigureAwait(false)).ToStructureSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var visibleAccounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false), sharedDataAccess);
        var filtered = FilterForOrganizationScope(snap, visibleAccounts, query.OrganizationId, query.BusinessId, query.ActiveOnly, asOf);
        var enrichedPortfolios = AttachSharedDataAccess(filtered.InvestmentPortfolios, sharedDataAccess);

        var nodes = BuildNodes(filtered)
            .Where(n => query.NodeId is null || n.NodeId == query.NodeId.Value)
            .Where(n => query.NodeKind is null || n.Kind == query.NodeKind.Value)
            .ToList();
        var nodeIds = nodes.Select(static n => n.NodeId).ToHashSet();

        return new OrganizationStructureGraphDto(
            filtered.Organizations, filtered.Businesses, filtered.Clients, filtered.Funds,
            filtered.Sleeves, filtered.Vehicles, filtered.Entities, enrichedPortfolios,
            filtered.Accounts, nodes,
            filtered.OwnershipLinks.Where(l => nodeIds.Contains(l.ParentNodeId) && nodeIds.Contains(l.ChildNodeId)).ToList(),
            filtered.Assignments.Where(a => nodeIds.Contains(a.NodeId)).ToList(),
            sharedDataAccess);
    }

    public async Task<FundStructureGraphDto> GetFundStructureGraphAsync(
        FundStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snap = (await LoadSnapshotAsync(ct).ConfigureAwait(false)).ToStructureSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var visibleAccounts = await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false);
        var activeLinks = FilterVisible(snap.OwnershipLinks, query.ActiveOnly, asOf, static l => (l.EffectiveFrom, l.EffectiveTo));
        var activeAssignments = FilterVisible(snap.Assignments, query.ActiveOnly, asOf, static a => (a.EffectiveFrom, a.EffectiveTo));
        var funds = FilterVisible(snap.Funds, query.ActiveOnly, asOf, static f => (f.EffectiveFrom, f.EffectiveTo));
        if (query.FundId.HasValue) funds = funds.Where(f => f.FundId == query.FundId.Value).ToList();

        var fundIds = funds.Select(static f => f.FundId).ToHashSet();
        var sleeves = FilterVisible(snap.Sleeves, query.ActiveOnly, asOf, static s => (s.EffectiveFrom, s.EffectiveTo))
            .Where(s => fundIds.Contains(s.FundId)).ToList();
        var sleeveIds = sleeves.Select(static s => s.SleeveId).ToHashSet();
        var vehicles = FilterVisible(snap.Vehicles, query.ActiveOnly, asOf, static v => (v.EffectiveFrom, v.EffectiveTo))
            .Where(v => fundIds.Contains(v.FundId)).ToList();
        var vehicleIds = vehicles.Select(static v => v.VehicleId).ToHashSet();
        var portfolioIds = FilterVisible(snap.InvestmentPortfolios, query.ActiveOnly, asOf, static p => (p.EffectiveFrom, p.EffectiveTo))
            .Where(p => (p.FundId.HasValue && fundIds.Contains(p.FundId.Value))
                     || (p.SleeveId.HasValue && sleeveIds.Contains(p.SleeveId.Value))
                     || (p.VehicleId.HasValue && vehicleIds.Contains(p.VehicleId.Value)))
            .Select(static p => p.InvestmentPortfolioId).ToHashSet();
        var entityIds = vehicles.Select(static v => v.LegalEntityId)
            .Concat(funds.SelectMany(static f => f.EntityIds)).ToHashSet();
        var accounts = visibleAccounts.Where(a =>
            (a.FundId.HasValue && fundIds.Contains(a.FundId.Value))
            || (a.SleeveId.HasValue && sleeveIds.Contains(a.SleeveId.Value))
            || (a.VehicleId.HasValue && vehicleIds.Contains(a.VehicleId.Value))
            || (a.EntityId.HasValue && entityIds.Contains(a.EntityId.Value))
            || IsAccountLinkedToAny(a.AccountId, fundIds, activeLinks)
            || IsAccountLinkedToAny(a.AccountId, sleeveIds, activeLinks)
            || IsAccountLinkedToAny(a.AccountId, vehicleIds, activeLinks)
            || IsAccountLinkedToAny(a.AccountId, portfolioIds, activeLinks)).ToList();
        var entities = FilterVisible(snap.Entities, query.ActiveOnly, asOf, static e => (e.EffectiveFrom, e.EffectiveTo))
            .Where(e => entityIds.Contains(e.EntityId) || IsEntityLinkedToScope(e.EntityId, fundIds, activeLinks)).ToList();

        var scope = new StructureScope([], [], [], funds, sleeves, vehicles, entities, [], accounts, activeLinks, activeAssignments);
        var nodes = BuildNodes(scope)
            .Where(n => n.Kind is FundStructureNodeKindDto.Fund or FundStructureNodeKindDto.Sleeve
                or FundStructureNodeKindDto.Vehicle or FundStructureNodeKindDto.Entity or FundStructureNodeKindDto.Account)
            .Where(n => query.NodeId is null || n.NodeId == query.NodeId.Value)
            .Where(n => query.NodeKind is null || n.Kind == query.NodeKind.Value)
            .ToList();
        var nodeIds = nodes.Select(static n => n.NodeId).ToHashSet();

        return new FundStructureGraphDto(
            nodes,
            activeLinks.Where(l => nodeIds.Contains(l.ParentNodeId) && nodeIds.Contains(l.ChildNodeId)).ToList(),
            activeAssignments.Where(a => nodeIds.Contains(a.NodeId)).ToList());
    }

    public async Task<AdvisoryStructureViewDto?> GetAdvisoryViewAsync(
        AdvisoryStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snap = (await LoadSnapshotAsync(ct).ConfigureAwait(false)).ToStructureSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var accounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false), sharedDataAccess);
        var activeLinks = FilterVisible(snap.OwnershipLinks, query.ActiveOnly, asOf, static l => (l.EffectiveFrom, l.EffectiveTo));

        var business = FilterVisible(snap.Businesses, query.ActiveOnly, asOf, static b => (b.EffectiveFrom, b.EffectiveTo))
            .FirstOrDefault(b => b.BusinessId == query.BusinessId);
        if (business is null) return null;
        if (query.OrganizationId.HasValue && business.OrganizationId != query.OrganizationId.Value) return null;

        var organization = FilterVisible(snap.Organizations, query.ActiveOnly, asOf, static o => (o.EffectiveFrom, o.EffectiveTo))
            .FirstOrDefault(o => o.OrganizationId == business.OrganizationId);
        if (organization is null) return null;

        var clients = FilterVisible(snap.Clients, query.ActiveOnly, asOf, static c => (c.EffectiveFrom, c.EffectiveTo))
            .Where(c => c.BusinessId == business.BusinessId)
            .Where(c => query.ClientId is null || c.ClientId == query.ClientId.Value).ToList();
        var portfolios = FilterVisible(snap.InvestmentPortfolios, query.ActiveOnly, asOf, static p => (p.EffectiveFrom, p.EffectiveTo))
            .Where(p => p.BusinessId == business.BusinessId)
            .Where(IsAdvisoryPortfolio)
            .Where(p => query.InvestmentPortfolioId is null || p.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        var enrichedPortfolios = AttachSharedDataAccess(portfolios, sharedDataAccess);

        var clientViews = clients.Select(client =>
        {
            var clientPortfolios = enrichedPortfolios.Where(p => p.ClientId == client.ClientId).ToList();
            var portfolioIds = clientPortfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet();
            var clientAccounts = GetScopeAccounts(accounts, activeLinks, [], [], [], [], portfolioIds).ToList();
            return new AdvisoryClientViewDto(client, clientPortfolios, clientAccounts);
        }).ToList();

        var unassignedPortfolios = enrichedPortfolios.Where(p => p.ClientId is null).ToList();
        var assignedAccountIds = clientViews.SelectMany(static v => v.Accounts).Select(static a => a.AccountId).ToHashSet();
        var unassignedAccounts = GetScopeAccounts(accounts, activeLinks, [], [], [], [],
                unassignedPortfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet())
            .Where(a => !assignedAccountIds.Contains(a.AccountId)).ToList();

        return new AdvisoryStructureViewDto(organization, business, clientViews, unassignedPortfolios, unassignedAccounts, sharedDataAccess);
    }

    public async Task<FundOperatingViewDto?> GetFundOperatingViewAsync(
        FundOperatingStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snap = (await LoadSnapshotAsync(ct).ConfigureAwait(false)).ToStructureSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var accounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false), sharedDataAccess);
        var activeLinks = FilterVisible(snap.OwnershipLinks, query.ActiveOnly, asOf, static l => (l.EffectiveFrom, l.EffectiveTo));

        var business = FilterVisible(snap.Businesses, query.ActiveOnly, asOf, static b => (b.EffectiveFrom, b.EffectiveTo))
            .FirstOrDefault(b => b.BusinessId == query.BusinessId);
        if (business is null) return null;
        if (query.OrganizationId.HasValue && business.OrganizationId != query.OrganizationId.Value) return null;

        var organization = FilterVisible(snap.Organizations, query.ActiveOnly, asOf, static o => (o.EffectiveFrom, o.EffectiveTo))
            .FirstOrDefault(o => o.OrganizationId == business.OrganizationId);
        if (organization is null) return null;

        var funds = FilterVisible(snap.Funds, query.ActiveOnly, asOf, static f => (f.EffectiveFrom, f.EffectiveTo))
            .Where(f => f.BusinessId == business.BusinessId)
            .Where(f => query.FundId is null || f.FundId == query.FundId.Value).ToList();
        var fundIds = funds.Select(static f => f.FundId).ToHashSet();
        var allSleeves = FilterVisible(snap.Sleeves, query.ActiveOnly, asOf, static s => (s.EffectiveFrom, s.EffectiveTo))
            .Where(s => fundIds.Contains(s.FundId))
            .Where(s => query.SleeveId is null || s.SleeveId == query.SleeveId.Value).ToList();
        var allVehicles = FilterVisible(snap.Vehicles, query.ActiveOnly, asOf, static v => (v.EffectiveFrom, v.EffectiveTo))
            .Where(v => fundIds.Contains(v.FundId))
            .Where(v => query.VehicleId is null || v.VehicleId == query.VehicleId.Value).ToList();
        var allPortfolios = FilterVisible(snap.InvestmentPortfolios, query.ActiveOnly, asOf, static p => (p.EffectiveFrom, p.EffectiveTo))
            .Where(p => p.BusinessId == business.BusinessId)
            .Where(IsFundPortfolio)
            .Where(p => query.InvestmentPortfolioId is null || p.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        var enrichedPortfolios = AttachSharedDataAccess(allPortfolios, sharedDataAccess);
        var entities = FilterVisible(snap.Entities, query.ActiveOnly, asOf, static e => (e.EffectiveFrom, e.EffectiveTo))
            .ToDictionary(static e => e.EntityId);

        var fundSlices = new List<FundOperatingSliceDto>();
        foreach (var fund in funds)
        {
            var sleeves = allSleeves.Where(s => s.FundId == fund.FundId).ToList();
            var vehicles = allVehicles.Where(v => v.FundId == fund.FundId).ToList();
            var directPortfolios = enrichedPortfolios
                .Where(p => p.FundId == fund.FundId && p.SleeveId is null && p.VehicleId is null).ToList();
            var directAccounts = GetScopeAccounts(accounts, activeLinks,
                    fund.EntityIds.ToHashSet(), [fund.FundId], [], [],
                    directPortfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet())
                .Where(a => a.SleeveId is null && a.VehicleId is null).ToList();

            var sleeveSlices = sleeves.Select(sleeve =>
            {
                var portfolios = enrichedPortfolios.Where(p => p.SleeveId == sleeve.SleeveId).ToList();
                var sleeveAccounts = GetScopeAccounts(accounts, activeLinks, [], [], [sleeve.SleeveId], [],
                    portfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet()).ToList();
                return new FundSleeveOperatingViewDto(sleeve, portfolios, sleeveAccounts);
            }).ToList();

            var vehicleSlices = vehicles.Select(vehicle =>
            {
                var portfolios = enrichedPortfolios.Where(p => p.VehicleId == vehicle.VehicleId).ToList();
                var vehicleAccounts = GetScopeAccounts(accounts, activeLinks,
                    [vehicle.LegalEntityId], [], [], [vehicle.VehicleId],
                    portfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet()).ToList();
                entities.TryGetValue(vehicle.LegalEntityId, out var entity);
                return new VehicleOperatingViewDto(vehicle, entity, portfolios, vehicleAccounts);
            }).ToList();

            fundSlices.Add(new FundOperatingSliceDto(fund, directPortfolios, directAccounts, sleeveSlices, vehicleSlices));
        }

        var assignedPortfolioIds = fundSlices.SelectMany(static s => s.InvestmentPortfolios)
            .Concat(fundSlices.SelectMany(static s => s.Sleeves).SelectMany(static s => s.InvestmentPortfolios))
            .Concat(fundSlices.SelectMany(static s => s.Vehicles).SelectMany(static v => v.InvestmentPortfolios))
            .Select(static p => p.InvestmentPortfolioId).ToHashSet();
        var unassignedPortfolios = enrichedPortfolios.Where(p => !assignedPortfolioIds.Contains(p.InvestmentPortfolioId)).ToList();
        var assignedAccountIds = fundSlices.SelectMany(static s => s.Accounts)
            .Concat(fundSlices.SelectMany(static s => s.Sleeves).SelectMany(static s => s.Accounts))
            .Concat(fundSlices.SelectMany(static s => s.Vehicles).SelectMany(static v => v.Accounts))
            .Select(static a => a.AccountId).ToHashSet();
        var unassignedAccounts = GetScopeAccounts(accounts, activeLinks, [], [], [], [],
                unassignedPortfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet())
            .Where(a => !assignedAccountIds.Contains(a.AccountId)).ToList();

        return new FundOperatingViewDto(organization, business, fundSlices, unassignedPortfolios, unassignedAccounts, sharedDataAccess);
    }

    public async Task<AccountingStructureViewDto> GetAccountingViewAsync(
        AccountingStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snap = (await LoadSnapshotAsync(ct).ConfigureAwait(false)).ToStructureSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var visibleAccounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false), sharedDataAccess);
        var scoped = FilterForOrganizationScope(snap, visibleAccounts, query.OrganizationId, query.BusinessId, query.ActiveOnly, asOf);

        var portfolios = scoped.InvestmentPortfolios
            .Where(p => query.ClientId is null || p.ClientId == query.ClientId.Value)
            .Where(p => query.FundId is null || p.FundId == query.FundId.Value)
            .Where(p => query.SleeveId is null || p.SleeveId == query.SleeveId.Value)
            .Where(p => query.VehicleId is null || p.VehicleId == query.VehicleId.Value)
            .Where(p => query.InvestmentPortfolioId is null || p.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        portfolios = AttachSharedDataAccess(portfolios, sharedDataAccess).ToList();
        var accountScope = BuildAccountingAccountScope(scoped, portfolios);
        var accounts = scoped.Accounts
            .Where(a => query.LedgerReference is null || string.Equals(a.LedgerReference, query.LedgerReference, StringComparison.OrdinalIgnoreCase))
            .Where(a => IsAccountInAccountingScope(a, query, accountScope, scoped.OwnershipLinks))
            .ToList();

        var organization = query.OrganizationId.HasValue
            ? scoped.Organizations.FirstOrDefault(o => o.OrganizationId == query.OrganizationId.Value)
            : scoped.Organizations.FirstOrDefault();
        var business = query.BusinessId.HasValue
            ? scoped.Businesses.FirstOrDefault(b => b.BusinessId == query.BusinessId.Value)
            : scoped.Businesses.FirstOrDefault();

        var portfolioById = portfolios.ToDictionary(static p => p.InvestmentPortfolioId);
        var ledgerAssignments = LedgerGroupingRules.BuildLedgerAssignments(scoped.Assignments);
        var ledgerGroups = accounts
            .GroupBy(a => LedgerGroupingRules.ResolveLedgerGroupId(a, ledgerAssignments))
            .Select(group =>
            {
                var accountIds = group.Select(static a => a.AccountId).ToList();
                var relatedPortfolioIds = group
                    .Select(a => TryParseGuid(a.PortfolioId, out var pid) ? pid : Guid.Empty)
                    .Where(static pid => pid != Guid.Empty)
                    .Concat(scoped.OwnershipLinks
                        .Where(link => accountIds.Contains(link.ChildNodeId) && portfolioById.ContainsKey(link.ParentNodeId))
                        .Select(static link => link.ParentNodeId))
                    .Distinct().ToList();
                var relatedPortfolios = relatedPortfolioIds
                    .Where(portfolioById.ContainsKey)
                    .Select(pid => portfolioById[pid]).ToList();
                return new LedgerGroupSummaryDto(
                    group.Key, group.Key.Value, accountIds, relatedPortfolioIds,
                    relatedPortfolios.Where(static p => p.ClientId.HasValue).Select(static p => p.ClientId!.Value).Distinct().ToList(),
                    group.Where(static a => a.FundId.HasValue).Select(static a => a.FundId!.Value)
                        .Concat(relatedPortfolios.Where(static p => p.FundId.HasValue).Select(static p => p.FundId!.Value)).Distinct().ToList(),
                    group.Where(static a => a.SleeveId.HasValue).Select(static a => a.SleeveId!.Value)
                        .Concat(relatedPortfolios.Where(static p => p.SleeveId.HasValue).Select(static p => p.SleeveId!.Value)).Distinct().ToList(),
                    group.Where(static a => a.VehicleId.HasValue).Select(static a => a.VehicleId!.Value)
                        .Concat(relatedPortfolios.Where(static p => p.VehicleId.HasValue).Select(static p => p.VehicleId!.Value)).Distinct().ToList(),
                    SharedDataAccess: sharedDataAccess);
            })
            .OrderBy(static g => g.DisplayName).ToList();

        return new AccountingStructureViewDto(
            organization,
            business,
            portfolios,
            accounts,
            ledgerGroups,
            sharedDataAccess,
            scoped.Assignments);
    }

    public async Task<GovernanceCashFlowViewDto?> GetCashFlowViewAsync(
        GovernanceCashFlowQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        _policy.ValidateCashFlowQuery(query);
        if (!await CashFlowScopeTargetExistsAsync(query, ct).ConfigureAwait(false))
        {
            return null;
        }

        var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
        var projector = new InMemoryFundStructureService(
            _fundAccountService,
            _sharedDataAccessService,
            _securityMasterQueryService,
            _policy,
            snap.Organizations.Values.ToList(),
            snap.Businesses.Values.ToList(),
            snap.Clients.Values.ToList(),
            snap.Funds.Values.ToList(),
            snap.Sleeves.Values.ToList(),
            snap.Vehicles.Values.ToList(),
            snap.Entities.Values.ToList(),
            snap.InvestmentPortfolios.Values.ToList(),
            snap.OwnershipLinks.Values.ToList(),
            snap.Assignments.Values.ToList(),
            snap.LinkedAccountIds.ToList());

        return await projector.GetCashFlowViewAsync(query, ct).ConfigureAwait(false);
    }

    private async Task<bool> CashFlowScopeTargetExistsAsync(GovernanceCashFlowQuery query, CancellationToken ct)
    {
        return query.ScopeKind switch
        {
            GovernanceCashFlowScopeKindDto.Organization when query.OrganizationId.HasValue
                => await _store.GetOrganizationAsync(query.OrganizationId.Value, ct).ConfigureAwait(false) is not null,
            GovernanceCashFlowScopeKindDto.Business when query.BusinessId.HasValue
                => await _store.GetBusinessAsync(query.BusinessId.Value, ct).ConfigureAwait(false) is not null,
            GovernanceCashFlowScopeKindDto.Client when query.ClientId.HasValue
                => await _store.GetClientAsync(query.ClientId.Value, ct).ConfigureAwait(false) is not null,
            GovernanceCashFlowScopeKindDto.Fund when query.FundId.HasValue
                => await _store.GetFundAsync(query.FundId.Value, ct).ConfigureAwait(false) is not null,
            GovernanceCashFlowScopeKindDto.Sleeve when query.SleeveId.HasValue
                => await _store.GetSleeveAsync(query.SleeveId.Value, ct).ConfigureAwait(false) is not null,
            GovernanceCashFlowScopeKindDto.Vehicle when query.VehicleId.HasValue
                => await _store.GetVehicleAsync(query.VehicleId.Value, ct).ConfigureAwait(false) is not null,
            GovernanceCashFlowScopeKindDto.InvestmentPortfolio when query.InvestmentPortfolioId.HasValue
                => await _store.GetInvestmentPortfolioAsync(query.InvestmentPortfolioId.Value, ct).ConfigureAwait(false) is not null,
            _ => true
        };
    }

    // ── Snapshot helpers ──────────────────────────────────────────────────────

    private sealed class MutableSnapshot
    {
        public Dictionary<Guid, OrganizationSummaryDto> Organizations = new();
        public Dictionary<Guid, BusinessSummaryDto> Businesses = new();
        public Dictionary<Guid, ClientSummaryDto> Clients = new();
        public Dictionary<Guid, FundSummaryDto> Funds = new();
        public Dictionary<Guid, SleeveSummaryDto> Sleeves = new();
        public Dictionary<Guid, VehicleSummaryDto> Vehicles = new();
        public Dictionary<Guid, LegalEntitySummaryDto> Entities = new();
        public Dictionary<Guid, InvestmentPortfolioSummaryDto> InvestmentPortfolios = new();
        public Dictionary<Guid, OwnershipLinkDto> OwnershipLinks = new();
        public Dictionary<Guid, FundStructureAssignmentDto> Assignments = new();
        public HashSet<Guid> LinkedAccountIds = [];

        public StructureSnapshot ToStructureSnapshot() => new(
            Organizations.Values.ToList(),
            Businesses.Values.ToList(),
            Clients.Values.ToList(),
            Funds.Values.ToList(),
            Sleeves.Values.ToList(),
            Vehicles.Values.ToList(),
            Entities.Values.ToList(),
            InvestmentPortfolios.Values.ToList(),
            OwnershipLinks.Values.ToList(),
            Assignments.Values.ToList());
    }

    private async Task<MutableSnapshot> LoadSnapshotAsync(CancellationToken ct)
    {
        var snap = new MutableSnapshot();
        snap.Organizations = (await _store.GetAllOrganizationsAsync(ct).ConfigureAwait(false)).ToDictionary(static o => o.OrganizationId);
        snap.Businesses = (await _store.GetAllBusinessesAsync(ct).ConfigureAwait(false)).ToDictionary(static b => b.BusinessId);
        snap.Clients = (await _store.GetAllClientsAsync(ct).ConfigureAwait(false)).ToDictionary(static c => c.ClientId);
        snap.Funds = (await _store.GetAllFundsAsync(ct).ConfigureAwait(false)).ToDictionary(static f => f.FundId);
        snap.Sleeves = (await _store.GetAllSleevesAsync(ct).ConfigureAwait(false)).ToDictionary(static s => s.SleeveId);
        snap.Vehicles = (await _store.GetAllVehiclesAsync(ct).ConfigureAwait(false)).ToDictionary(static v => v.VehicleId);
        snap.Entities = (await _store.GetAllLegalEntitiesAsync(ct).ConfigureAwait(false)).ToDictionary(static e => e.EntityId);
        snap.InvestmentPortfolios = (await _store.GetAllInvestmentPortfoliosAsync(ct).ConfigureAwait(false)).ToDictionary(static p => p.InvestmentPortfolioId);
        snap.OwnershipLinks = (await _store.GetAllOwnershipLinksAsync(ct).ConfigureAwait(false)).ToDictionary(static l => l.OwnershipLinkId);
        snap.Assignments = (await _store.GetAllAssignmentsAsync(ct).ConfigureAwait(false)).ToDictionary(static a => a.AssignmentId);

        var structureNodeIds = snap.Organizations.Keys
            .Concat<Guid>(snap.Businesses.Keys).Concat(snap.Clients.Keys)
            .Concat(snap.Funds.Keys).Concat(snap.Sleeves.Keys).Concat(snap.Vehicles.Keys)
            .Concat(snap.Entities.Keys).Concat(snap.InvestmentPortfolios.Keys).ToHashSet();
        foreach (var link in snap.OwnershipLinks.Values)
        {
            if (!structureNodeIds.Contains(link.ParentNodeId)) snap.LinkedAccountIds.Add(link.ParentNodeId);
            if (!structureNodeIds.Contains(link.ChildNodeId)) snap.LinkedAccountIds.Add(link.ChildNodeId);
        }
        foreach (var assignment in snap.Assignments.Values)
        {
            if (!structureNodeIds.Contains(assignment.NodeId)) snap.LinkedAccountIds.Add(assignment.NodeId);
        }
        return snap;
    }

    private async Task PersistChangedAsync(MutableSnapshot snap, CancellationToken ct)
    {
        foreach (var o in snap.Organizations.Values) await _store.UpsertOrganizationAsync(o, ct).ConfigureAwait(false);
        foreach (var b in snap.Businesses.Values) await _store.UpsertBusinessAsync(b, ct).ConfigureAwait(false);
        foreach (var c in snap.Clients.Values) await _store.UpsertClientAsync(c, ct).ConfigureAwait(false);
        foreach (var f in snap.Funds.Values) await _store.UpsertFundAsync(f, ct).ConfigureAwait(false);
        foreach (var s in snap.Sleeves.Values) await _store.UpsertSleeveAsync(s, ct).ConfigureAwait(false);
        foreach (var v in snap.Vehicles.Values) await _store.UpsertVehicleAsync(v, ct).ConfigureAwait(false);
        foreach (var e in snap.Entities.Values) await _store.UpsertLegalEntityAsync(e, ct).ConfigureAwait(false);
        foreach (var p in snap.InvestmentPortfolios.Values) await _store.UpsertInvestmentPortfolioAsync(p, ct).ConfigureAwait(false);
        foreach (var l in snap.OwnershipLinks.Values) await _store.UpsertOwnershipLinkAsync(l, ct).ConfigureAwait(false);
        foreach (var a in snap.Assignments.Values) await _store.UpsertAssignmentAsync(a, ct).ConfigureAwait(false);
    }

    private async Task<FundStructureNodeKindDto?> ResolveNodeKindAsync(Guid nodeId, CancellationToken ct)
    {
        var snap = await LoadSnapshotAsync(ct).ConfigureAwait(false);
        if (snap.Organizations.ContainsKey(nodeId)) return FundStructureNodeKindDto.Organization;
        if (snap.Businesses.ContainsKey(nodeId)) return FundStructureNodeKindDto.Business;
        if (snap.Clients.ContainsKey(nodeId)) return FundStructureNodeKindDto.Client;
        if (snap.Funds.ContainsKey(nodeId)) return FundStructureNodeKindDto.Fund;
        if (snap.Sleeves.ContainsKey(nodeId)) return FundStructureNodeKindDto.Sleeve;
        if (snap.Vehicles.ContainsKey(nodeId)) return FundStructureNodeKindDto.Vehicle;
        if (snap.InvestmentPortfolios.ContainsKey(nodeId)) return FundStructureNodeKindDto.InvestmentPortfolio;
        if (snap.Entities.ContainsKey(nodeId)) return FundStructureNodeKindDto.Entity;
        if (snap.LinkedAccountIds.Contains(nodeId)) return FundStructureNodeKindDto.Account;
        var account = await _fundAccountService.GetAccountAsync(nodeId, ct).ConfigureAwait(false);
        return account is not null ? FundStructureNodeKindDto.Account : null;
    }

    // ── Back-reference update ─────────────────────────────────────────────────

    private static void RebuildOwnershipProjections(MutableSnapshot snap)
    {
        foreach (var (id, organization) in snap.Organizations.ToList())
            snap.Organizations[id] = organization with { BusinessIds = [] };
        foreach (var (id, business) in snap.Businesses.ToList())
        {
            snap.Businesses[id] = business with
            {
                ClientIds = [],
                FundIds = [],
                InvestmentPortfolioIds = []
            };
        }
        foreach (var (id, client) in snap.Clients.ToList())
            snap.Clients[id] = client with { InvestmentPortfolioIds = [] };
        foreach (var (id, fund) in snap.Funds.ToList())
        {
            snap.Funds[id] = fund with
            {
                BusinessId = null,
                SleeveIds = [],
                VehicleIds = [],
                EntityIds = [],
                InvestmentPortfolioIds = [],
                AccountIds = []
            };
        }
        foreach (var (id, sleeve) in snap.Sleeves.ToList())
            snap.Sleeves[id] = sleeve with { InvestmentPortfolioIds = [], AccountIds = [] };
        foreach (var (id, vehicle) in snap.Vehicles.ToList())
            snap.Vehicles[id] = vehicle with { InvestmentPortfolioIds = [], AccountIds = [] };
        foreach (var (id, portfolio) in snap.InvestmentPortfolios.ToList())
        {
            snap.InvestmentPortfolios[id] = portfolio with
            {
                ClientId = null,
                FundId = null,
                SleeveId = null,
                VehicleId = null,
                EntityId = null,
                AccountIds = []
            };
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var link in snap.OwnershipLinks.Values
            .Where(link => IsOwnershipLinkVisible(link, activeOnly: true, now))
            .OrderBy(static link => link.EffectiveFrom)
            .ThenBy(static link => link.OwnershipLinkId))
        {
            ApplyOwnershipLink(link, snap);
        }
    }

    private static List<OwnershipGraphValidationIssueDto> ValidateOwnershipGraph(
        MutableSnapshot snap,
        bool activeOnly,
        DateTimeOffset asOf)
    {
        var issues = new List<OwnershipGraphValidationIssueDto>();
        var links = snap.OwnershipLinks.Values
            .Where(link => IsOwnershipLinkVisible(link, activeOnly, asOf))
            .ToList();
        foreach (var link in links)
        {
            if (link.ParentNodeId == link.ChildNodeId)
            {
                issues.Add(new OwnershipGraphValidationIssueDto(
                    "ownership.self-link",
                    "Ownership links cannot point a node to itself.",
                    link.OwnershipLinkId,
                    link.ParentNodeId));
            }
            if (!TryGetNodeKind(snap, link.ParentNodeId, out _))
            {
                issues.Add(new OwnershipGraphValidationIssueDto(
                    "ownership.parent-not-found",
                    $"Parent node {link.ParentNodeId} was not found.",
                    link.OwnershipLinkId,
                    link.ParentNodeId));
            }
            if (!TryGetNodeKind(snap, link.ChildNodeId, out _))
            {
                issues.Add(new OwnershipGraphValidationIssueDto(
                    "ownership.child-not-found",
                    $"Child node {link.ChildNodeId} was not found.",
                    link.OwnershipLinkId,
                    link.ChildNodeId));
            }
        }

        var adjacency = links
            .GroupBy(static link => link.ParentNodeId)
            .ToDictionary(static group => group.Key, static group => group.Select(link => (link.ChildNodeId, link.OwnershipLinkId)).ToList());
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        foreach (var nodeId in adjacency.Keys.ToList())
        {
            DetectOwnershipCycles(nodeId, adjacency, visiting, visited, issues);
        }
        return issues;
    }

    private static bool TryGetNodeKind(MutableSnapshot snap, Guid nodeId, out FundStructureNodeKindDto kind)
    {
        if (snap.Organizations.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.Organization; return true; }
        if (snap.Businesses.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.Business; return true; }
        if (snap.Clients.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.Client; return true; }
        if (snap.Funds.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.Fund; return true; }
        if (snap.Sleeves.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.Sleeve; return true; }
        if (snap.Vehicles.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.Vehicle; return true; }
        if (snap.InvestmentPortfolios.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.InvestmentPortfolio; return true; }
        if (snap.Entities.ContainsKey(nodeId)) { kind = FundStructureNodeKindDto.Entity; return true; }
        if (snap.LinkedAccountIds.Contains(nodeId)) { kind = FundStructureNodeKindDto.Account; return true; }
        kind = default;
        return false;
    }

    private static bool DetectOwnershipCycles(
        Guid nodeId,
        IReadOnlyDictionary<Guid, List<(Guid ChildNodeId, Guid OwnershipLinkId)>> adjacency,
        HashSet<Guid> visiting,
        HashSet<Guid> visited,
        List<OwnershipGraphValidationIssueDto> issues)
    {
        if (visited.Contains(nodeId)) return false;
        if (!visiting.Add(nodeId))
        {
            issues.Add(new OwnershipGraphValidationIssueDto(
                "ownership.cycle",
                $"Ownership graph contains a cycle at node {nodeId}.",
                NodeId: nodeId));
            return true;
        }
        if (adjacency.TryGetValue(nodeId, out var children))
        {
            foreach (var (childNodeId, ownershipLinkId) in children)
            {
                if (DetectOwnershipCycles(childNodeId, adjacency, visiting, visited, issues))
                {
                    issues.Add(new OwnershipGraphValidationIssueDto(
                        "ownership.cycle-link",
                        $"Ownership link {ownershipLinkId} participates in a cycle.",
                        ownershipLinkId,
                        childNodeId));
                    return true;
                }
            }
        }
        visiting.Remove(nodeId);
        visited.Add(nodeId);
        return false;
    }

    private static bool IsOwnershipLinkVisible(OwnershipLinkDto link, bool activeOnly, DateTimeOffset asOf) =>
        !activeOnly || (link.EffectiveFrom <= asOf && (link.EffectiveTo is null || link.EffectiveTo > asOf));

    private static void ApplyOwnershipLink(OwnershipLinkDto link, MutableSnapshot snap)
    {
        if (snap.Organizations.TryGetValue(link.ParentNodeId, out var org)
            && snap.Businesses.TryGetValue(link.ChildNodeId, out var biz))
        {
            snap.Organizations[org.OrganizationId] = org with { BusinessIds = AppendUnique(org.BusinessIds, biz.BusinessId) };
            snap.Businesses[biz.BusinessId] = biz with { OrganizationId = org.OrganizationId };
            return;
        }
        if (snap.Businesses.TryGetValue(link.ParentNodeId, out biz))
        {
            if (snap.Clients.TryGetValue(link.ChildNodeId, out var client))
            {
                snap.Businesses[biz.BusinessId] = biz with { ClientIds = AppendUnique(biz.ClientIds, client.ClientId) };
                snap.Clients[client.ClientId] = client with { BusinessId = biz.BusinessId };
                return;
            }
            if (snap.Funds.TryGetValue(link.ChildNodeId, out var fund))
            {
                snap.Businesses[biz.BusinessId] = biz with { FundIds = AppendUnique(biz.FundIds, fund.FundId) };
                snap.Funds[fund.FundId] = fund with { BusinessId = biz.BusinessId };
                return;
            }
            if (snap.InvestmentPortfolios.TryGetValue(link.ChildNodeId, out var bizPortfolio))
            {
                snap.Businesses[biz.BusinessId] = biz with { InvestmentPortfolioIds = AppendUnique(biz.InvestmentPortfolioIds, bizPortfolio.InvestmentPortfolioId) };
                snap.InvestmentPortfolios[bizPortfolio.InvestmentPortfolioId] = bizPortfolio with { BusinessId = biz.BusinessId };
                return;
            }
        }
        if (snap.Clients.TryGetValue(link.ParentNodeId, out var parentClient)
            && snap.InvestmentPortfolios.TryGetValue(link.ChildNodeId, out var clientPortfolio))
        {
            snap.Clients[parentClient.ClientId] = parentClient with { InvestmentPortfolioIds = AppendUnique(parentClient.InvestmentPortfolioIds, clientPortfolio.InvestmentPortfolioId) };
            snap.InvestmentPortfolios[clientPortfolio.InvestmentPortfolioId] = ApplyOperatingParent(clientPortfolio, clientId: parentClient.ClientId);
            return;
        }
        if (snap.Funds.TryGetValue(link.ParentNodeId, out var parentFund))
        {
            if (snap.Sleeves.TryGetValue(link.ChildNodeId, out var sleeve))
            {
                snap.Funds[parentFund.FundId] = parentFund with { SleeveIds = AppendUnique(parentFund.SleeveIds, sleeve.SleeveId) };
                snap.Sleeves[sleeve.SleeveId] = sleeve with { FundId = parentFund.FundId };
                return;
            }
            if (snap.Vehicles.TryGetValue(link.ChildNodeId, out var vehicle))
            {
                snap.Funds[parentFund.FundId] = parentFund with { VehicleIds = AppendUnique(parentFund.VehicleIds, vehicle.VehicleId) };
                snap.Vehicles[vehicle.VehicleId] = vehicle with { FundId = parentFund.FundId };
                return;
            }
            if (snap.Entities.TryGetValue(link.ChildNodeId, out var entityNode))
            {
                snap.Funds[parentFund.FundId] = parentFund with { EntityIds = AppendUnique(parentFund.EntityIds, entityNode.EntityId) };
                return;
            }
            if (snap.InvestmentPortfolios.TryGetValue(link.ChildNodeId, out var fundPortfolio))
            {
                snap.Funds[parentFund.FundId] = parentFund with { InvestmentPortfolioIds = AppendUnique(parentFund.InvestmentPortfolioIds, fundPortfolio.InvestmentPortfolioId) };
                snap.InvestmentPortfolios[fundPortfolio.InvestmentPortfolioId] = ApplyOperatingParent(fundPortfolio, fundId: parentFund.FundId);
                return;
            }
            if (snap.LinkedAccountIds.Contains(link.ChildNodeId))
            {
                snap.Funds[parentFund.FundId] = parentFund with { AccountIds = AppendUnique(parentFund.AccountIds, link.ChildNodeId) };
                return;
            }
        }
        if (snap.Sleeves.TryGetValue(link.ParentNodeId, out var parentSleeve))
        {
            if (snap.InvestmentPortfolios.TryGetValue(link.ChildNodeId, out var sleevePortfolio))
            {
                snap.Sleeves[parentSleeve.SleeveId] = parentSleeve with { InvestmentPortfolioIds = AppendUnique(parentSleeve.InvestmentPortfolioIds, sleevePortfolio.InvestmentPortfolioId) };
                snap.InvestmentPortfolios[sleevePortfolio.InvestmentPortfolioId] = ApplyOperatingParent(sleevePortfolio, sleeveId: parentSleeve.SleeveId);
                return;
            }
            if (snap.LinkedAccountIds.Contains(link.ChildNodeId))
            {
                snap.Sleeves[parentSleeve.SleeveId] = parentSleeve with { AccountIds = AppendUnique(parentSleeve.AccountIds, link.ChildNodeId) };
                return;
            }
        }
        if (snap.Vehicles.TryGetValue(link.ParentNodeId, out var parentVehicle))
        {
            if (snap.InvestmentPortfolios.TryGetValue(link.ChildNodeId, out var vehiclePortfolio))
            {
                snap.Vehicles[parentVehicle.VehicleId] = parentVehicle with { InvestmentPortfolioIds = AppendUnique(parentVehicle.InvestmentPortfolioIds, vehiclePortfolio.InvestmentPortfolioId) };
                snap.InvestmentPortfolios[vehiclePortfolio.InvestmentPortfolioId] = ApplyOperatingParent(vehiclePortfolio, vehicleId: parentVehicle.VehicleId);
                return;
            }
            if (snap.LinkedAccountIds.Contains(link.ChildNodeId))
            {
                snap.Vehicles[parentVehicle.VehicleId] = parentVehicle with { AccountIds = AppendUnique(parentVehicle.AccountIds, link.ChildNodeId) };
                return;
            }
        }
        if (snap.Entities.TryGetValue(link.ParentNodeId, out var parentEntity))
        {
            if (snap.Vehicles.TryGetValue(link.ChildNodeId, out var linkedVehicle))
            {
                snap.Vehicles[linkedVehicle.VehicleId] = linkedVehicle with { LegalEntityId = parentEntity.EntityId };
                return;
            }
            if (snap.InvestmentPortfolios.TryGetValue(link.ChildNodeId, out var entityPortfolio))
            {
                snap.InvestmentPortfolios[entityPortfolio.InvestmentPortfolioId] = entityPortfolio with { EntityId = parentEntity.EntityId };
                return;
            }
        }
        if (snap.InvestmentPortfolios.TryGetValue(link.ParentNodeId, out var parentPortfolio)
            && snap.LinkedAccountIds.Contains(link.ChildNodeId))
        {
            snap.InvestmentPortfolios[parentPortfolio.InvestmentPortfolioId] = parentPortfolio with { AccountIds = AppendUnique(parentPortfolio.AccountIds, link.ChildNodeId) };
        }
    }

    // ── Static utilities ──────────────────────────────────────────────────────


    private static FundStructureNodeKindDto ResolveKnownNodeKind(
        Guid nodeId,
        IReadOnlyDictionary<Guid, FundStructureNodeKindDto> nodeKinds)
    {
        if (nodeKinds.TryGetValue(nodeId, out var kind))
        {
            return kind;
        }

        throw new InvalidOperationException($"Node {nodeId} was not found.");
    }

    private static Dictionary<Guid, FundStructureNodeKindDto> CaptureNodeKinds(MutableSnapshot snap)
    {
        var nodeKinds = new Dictionary<Guid, FundStructureNodeKindDto>();
        foreach (var nodeId in snap.Organizations.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.Organization;
        foreach (var nodeId in snap.Businesses.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.Business;
        foreach (var nodeId in snap.Clients.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.Client;
        foreach (var nodeId in snap.Funds.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.Fund;
        foreach (var nodeId in snap.Sleeves.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.Sleeve;
        foreach (var nodeId in snap.Vehicles.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.Vehicle;
        foreach (var nodeId in snap.Entities.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.Entity;
        foreach (var nodeId in snap.InvestmentPortfolios.Keys) nodeKinds[nodeId] = FundStructureNodeKindDto.InvestmentPortfolio;
        foreach (var nodeId in snap.LinkedAccountIds) nodeKinds[nodeId] = FundStructureNodeKindDto.Account;
        foreach (var link in snap.OwnershipLinks.Values)
        {
            nodeKinds.TryAdd(link.ParentNodeId, FundStructureNodeKindDto.Account);
            nodeKinds.TryAdd(link.ChildNodeId, FundStructureNodeKindDto.Account);
        }

        return nodeKinds;
    }

    private static OwnershipLinkDto MakeAutoLink(Guid parentId, Guid childId, OwnershipRelationshipTypeDto rel, DateTimeOffset from, string? notes) =>
        new(Guid.NewGuid(), parentId, childId, rel, OwnershipPercent: null, IsPrimary: true, from, EffectiveTo: null, notes);

    private static void EnsureUniqueNode(Guid nodeId, MutableSnapshot snap)
    {
        if (snap.Organizations.ContainsKey(nodeId) || snap.Businesses.ContainsKey(nodeId)
            || snap.Clients.ContainsKey(nodeId) || snap.Funds.ContainsKey(nodeId)
            || snap.Sleeves.ContainsKey(nodeId) || snap.Vehicles.ContainsKey(nodeId)
            || snap.Entities.ContainsKey(nodeId) || snap.InvestmentPortfolios.ContainsKey(nodeId)
            || snap.LinkedAccountIds.Contains(nodeId))
            throw new InvalidOperationException($"Node {nodeId} already exists.");
    }

    private static void EnsureExistsInDict<T>(Dictionary<Guid, T> dict, Guid id, string typeName)
    {
        if (!dict.ContainsKey(id))
            throw new InvalidOperationException($"{typeName} {id} was not found.");
    }

    private static IReadOnlyList<Guid> AppendUnique(IReadOnlyList<Guid> source, Guid value)
    {
        if (source.Contains(value)) return source;
        var list = source.ToList();
        list.Add(value);
        return list;
    }

    private static InvestmentPortfolioSummaryDto ApplyOperatingParent(
        InvestmentPortfolioSummaryDto p, Guid? clientId = null, Guid? fundId = null, Guid? sleeveId = null, Guid? vehicleId = null)
    {
        var count = new[] { clientId.HasValue, fundId.HasValue, sleeveId.HasValue, vehicleId.HasValue }.Count(static x => x);
        if (count > 1) throw new InvalidOperationException("A portfolio cannot be linked to more than one operating parent.");
        if (clientId.HasValue) return p with { ClientId = clientId, FundId = null, SleeveId = null, VehicleId = null };
        if (fundId.HasValue) return p with { ClientId = null, FundId = fundId, SleeveId = null, VehicleId = null };
        if (sleeveId.HasValue) return p with { ClientId = null, FundId = null, SleeveId = sleeveId, VehicleId = null };
        if (vehicleId.HasValue) return p with { ClientId = null, FundId = null, SleeveId = null, VehicleId = vehicleId };
        return p;
    }

    private async Task<IReadOnlyList<AccountSummaryDto>> GetVisibleAccountsAsync(bool activeOnly, DateTimeOffset asOf, CancellationToken ct)
    {
        var accounts = await _fundAccountService
            .QueryAccountsAsync(new AccountStructureQuery(ActiveOnly: false), ct)
            .ConfigureAwait(false);
        return accounts.Where(a => IsVisible(a.IsActive, a.EffectiveFrom, a.EffectiveTo, activeOnly, asOf)).ToList();
    }

    private async Task<FundStructureSharedDataAccessDto?> GetSharedDataAccessAsync(CancellationToken ct) =>
        _sharedDataAccessService is null
            ? null
            : await _sharedDataAccessService.GetSharedDataAccessAsync(ct).ConfigureAwait(false);

    private static IReadOnlyList<AccountSummaryDto> AttachSharedDataAccess(
        IReadOnlyList<AccountSummaryDto> accounts, FundStructureSharedDataAccessDto? sda) =>
        sda is null || accounts.Count == 0 ? accounts : accounts.Select(a => a with { SharedDataAccess = sda }).ToList();

    private static IReadOnlyList<InvestmentPortfolioSummaryDto> AttachSharedDataAccess(
        IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios, FundStructureSharedDataAccessDto? sda) =>
        sda is null || portfolios.Count == 0 ? portfolios : portfolios.Select(p => p with { SharedDataAccess = sda }).ToList();

    private static IReadOnlyList<T> FilterVisible<T>(IReadOnlyList<T> source, bool activeOnly, DateTimeOffset asOf,
        Func<T, (DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo)> selector) where T : notnull =>
        source.Where(item => { var w = selector(item); return IsVisible(true, w.EffectiveFrom, w.EffectiveTo, activeOnly, asOf); }).ToList();

    private static bool IsVisible(bool isActive, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, bool activeOnly, DateTimeOffset asOf)
    {
        if (activeOnly && !isActive) return false;
        if (effectiveFrom > asOf) return false;
        if (effectiveTo.HasValue && effectiveTo.Value < asOf) return false;
        return true;
    }

    private static bool IsAdvisoryPortfolio(InvestmentPortfolioSummaryDto p) =>
        p.ClientId.HasValue || (!p.FundId.HasValue && !p.SleeveId.HasValue && !p.VehicleId.HasValue);

    private static bool IsFundPortfolio(InvestmentPortfolioSummaryDto p) =>
        p.FundId.HasValue || p.SleeveId.HasValue || p.VehicleId.HasValue;

    private static StructureScope FilterForOrganizationScope(
        StructureSnapshot snapshot, IReadOnlyList<AccountSummaryDto> accounts,
        Guid? organizationId, Guid? businessId, bool activeOnly, DateTimeOffset asOf)
    {
        var organizations = FilterVisible(snapshot.Organizations, activeOnly, asOf, static o => (o.EffectiveFrom, o.EffectiveTo));
        var businesses = FilterVisible(snapshot.Businesses, activeOnly, asOf, static b => (b.EffectiveFrom, b.EffectiveTo));
        var clients = FilterVisible(snapshot.Clients, activeOnly, asOf, static c => (c.EffectiveFrom, c.EffectiveTo));
        var funds = FilterVisible(snapshot.Funds, activeOnly, asOf, static f => (f.EffectiveFrom, f.EffectiveTo));
        var sleeves = FilterVisible(snapshot.Sleeves, activeOnly, asOf, static s => (s.EffectiveFrom, s.EffectiveTo));
        var vehicles = FilterVisible(snapshot.Vehicles, activeOnly, asOf, static v => (v.EffectiveFrom, v.EffectiveTo));
        var entities = FilterVisible(snapshot.Entities, activeOnly, asOf, static e => (e.EffectiveFrom, e.EffectiveTo));
        var portfolios = FilterVisible(snapshot.InvestmentPortfolios, activeOnly, asOf, static p => (p.EffectiveFrom, p.EffectiveTo));
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, activeOnly, asOf, static l => (l.EffectiveFrom, l.EffectiveTo));
        var activeAssignments = FilterVisible(snapshot.Assignments, activeOnly, asOf, static a => (a.EffectiveFrom, a.EffectiveTo));

        if (businessId.HasValue) businesses = businesses.Where(b => b.BusinessId == businessId.Value).ToList();
        if (organizationId.HasValue) organizations = organizations.Where(o => o.OrganizationId == organizationId.Value).ToList();

        var orgIds = organizations.Select(static o => o.OrganizationId).ToHashSet();
        businesses = businesses.Where(b => orgIds.Count == 0 || orgIds.Contains(b.OrganizationId)).ToList();
        var bizIds = businesses.Select(static b => b.BusinessId).ToHashSet();

        clients = clients.Where(c => bizIds.Contains(c.BusinessId)).ToList();
        funds = funds.Where(f => f.BusinessId.HasValue && bizIds.Contains(f.BusinessId.Value)).ToList();
        var fundIds = funds.Select(static f => f.FundId).ToHashSet();
        sleeves = sleeves.Where(s => fundIds.Contains(s.FundId)).ToList();
        var sleeveIds = sleeves.Select(static s => s.SleeveId).ToHashSet();
        vehicles = vehicles.Where(v => fundIds.Contains(v.FundId)).ToList();
        var vehicleIds = vehicles.Select(static v => v.VehicleId).ToHashSet();
        portfolios = portfolios.Where(p => bizIds.Contains(p.BusinessId)).ToList();
        var portfolioIds = portfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet();
        var entityIds = vehicles.Select(static v => v.LegalEntityId)
            .Concat(funds.SelectMany(static f => f.EntityIds))
            .Concat(portfolios.Where(static p => p.EntityId.HasValue).Select(static p => p.EntityId!.Value))
            .Concat(accounts.Where(static a => a.EntityId.HasValue).Select(static a => a.EntityId!.Value))
            .ToHashSet();
        entities = entities.Where(e =>
            entityIds.Contains(e.EntityId)
            || activeLinks.Any(link => link.ChildNodeId == e.EntityId
                && (bizIds.Contains(link.ParentNodeId) || fundIds.Contains(link.ParentNodeId)
                    || vehicleIds.Contains(link.ParentNodeId) || portfolioIds.Contains(link.ParentNodeId))))
            .ToList();

        var scopedAccounts = GetScopeAccounts(accounts, activeLinks,
            entities.Select(static e => e.EntityId).ToHashSet(), fundIds, sleeveIds, vehicleIds, portfolioIds).ToList();
        var accountIds = scopedAccounts.Select(static a => a.AccountId).ToHashSet();
        var nodeIds = orgIds.Concat<Guid>(bizIds)
            .Concat(clients.Select(static c => c.ClientId))
            .Concat(fundIds).Concat(sleeveIds).Concat(vehicleIds)
            .Concat(entities.Select(static e => e.EntityId))
            .Concat(portfolioIds).Concat(accountIds).ToHashSet();
        activeLinks = activeLinks.Where(l => nodeIds.Contains(l.ParentNodeId) && nodeIds.Contains(l.ChildNodeId)).ToList();
        activeAssignments = activeAssignments.Where(a => nodeIds.Contains(a.NodeId)).ToList();

        return new StructureScope(organizations, businesses, clients, funds, sleeves, vehicles,
            entities, portfolios, scopedAccounts, activeLinks, activeAssignments);
    }

    private static IReadOnlyList<FundStructureNodeDto> BuildNodes(StructureScope scope) =>
        scope.Organizations.Select(static o => new FundStructureNodeDto(o.OrganizationId, FundStructureNodeKindDto.Organization, o.Code, o.Name, o.Description, o.IsActive, o.EffectiveFrom, o.EffectiveTo))
            .Concat(scope.Businesses.Select(static b => new FundStructureNodeDto(b.BusinessId, FundStructureNodeKindDto.Business, b.Code, b.Name, b.Description, b.IsActive, b.EffectiveFrom, b.EffectiveTo)))
            .Concat(scope.Clients.Select(static c => new FundStructureNodeDto(c.ClientId, FundStructureNodeKindDto.Client, c.Code, c.Name, c.Description, c.IsActive, c.EffectiveFrom, c.EffectiveTo)))
            .Concat(scope.Funds.Select(static f => new FundStructureNodeDto(f.FundId, FundStructureNodeKindDto.Fund, f.Code, f.Name, f.Description, f.IsActive, f.EffectiveFrom, f.EffectiveTo)))
            .Concat(scope.Sleeves.Select(static s => new FundStructureNodeDto(s.SleeveId, FundStructureNodeKindDto.Sleeve, s.Code, s.Name, s.Mandate, s.IsActive, s.EffectiveFrom, s.EffectiveTo)))
            .Concat(scope.Vehicles.Select(static v => new FundStructureNodeDto(v.VehicleId, FundStructureNodeKindDto.Vehicle, v.Code, v.Name, v.Description, v.IsActive, v.EffectiveFrom, v.EffectiveTo)))
            .Concat(scope.Entities.Select(static e => new FundStructureNodeDto(e.EntityId, FundStructureNodeKindDto.Entity, e.Code, e.Name, e.Description, e.IsActive, e.EffectiveFrom, e.EffectiveTo)))
            .Concat(scope.InvestmentPortfolios.Select(static p => new FundStructureNodeDto(p.InvestmentPortfolioId, FundStructureNodeKindDto.InvestmentPortfolio, p.Code, p.Name, p.Description, p.IsActive, p.EffectiveFrom, p.EffectiveTo)))
            .Concat(scope.Accounts.Select(static a => new FundStructureNodeDto(a.AccountId, FundStructureNodeKindDto.Account, a.AccountCode, a.DisplayName, a.Institution, a.IsActive, a.EffectiveFrom, a.EffectiveTo)))
            .ToList();

    private static IEnumerable<AccountSummaryDto> GetScopeAccounts(
        IEnumerable<AccountSummaryDto> accounts, IReadOnlyList<OwnershipLinkDto> links,
        IReadOnlyCollection<Guid> entityIds, IReadOnlyCollection<Guid> fundIds,
        IReadOnlyCollection<Guid> sleeveIds, IReadOnlyCollection<Guid> vehicleIds,
        IReadOnlyCollection<Guid> portfolioIds)
    {
        var entitySet = entityIds as ISet<Guid> ?? entityIds.ToHashSet();
        var fundSet = fundIds as ISet<Guid> ?? fundIds.ToHashSet();
        var sleeveSet = sleeveIds as ISet<Guid> ?? sleeveIds.ToHashSet();
        var vehicleSet = vehicleIds as ISet<Guid> ?? vehicleIds.ToHashSet();
        var portfolioSet = portfolioIds as ISet<Guid> ?? portfolioIds.ToHashSet();
        var scopeNodeIds = entitySet.Concat<Guid>(fundSet).Concat(sleeveSet).Concat(vehicleSet).Concat(portfolioSet).ToHashSet();
        return accounts.Where(a =>
            (a.EntityId.HasValue && entitySet.Contains(a.EntityId.Value))
            || (a.FundId.HasValue && fundSet.Contains(a.FundId.Value))
            || (a.SleeveId.HasValue && sleeveSet.Contains(a.SleeveId.Value))
            || (a.VehicleId.HasValue && vehicleSet.Contains(a.VehicleId.Value))
            || (TryParseGuid(a.PortfolioId, out var pid) && portfolioSet.Contains(pid))
            || links.Any(link => link.ChildNodeId == a.AccountId && scopeNodeIds.Contains(link.ParentNodeId)))
            .DistinctBy(static a => a.AccountId);
    }

    private static bool IsAccountLinkedToAny(Guid accountId, IReadOnlyCollection<Guid> parentIds, IReadOnlyList<OwnershipLinkDto> links) =>
        links.Any(link => link.ChildNodeId == accountId && parentIds.Contains(link.ParentNodeId));

    private static bool IsEntityLinkedToScope(Guid entityId, IReadOnlyCollection<Guid> parentIds, IReadOnlyList<OwnershipLinkDto> links) =>
        links.Any(link => link.ChildNodeId == entityId && parentIds.Contains(link.ParentNodeId));

    private static bool TryParseGuid(string? value, out Guid guid) => Guid.TryParse(value, out guid);

    private static AccountingAccountScope BuildAccountingAccountScope(StructureScope scoped, IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios)
    {
        var portfolioIds = portfolios.Select(static p => p.InvestmentPortfolioId).ToHashSet();
        var fundIds = scoped.Funds.Select(static f => f.FundId).ToHashSet();
        var sleeveIds = scoped.Sleeves.Select(static s => s.SleeveId).ToHashSet();
        var vehicleIds = scoped.Vehicles.Select(static v => v.VehicleId).ToHashSet();
        var scopedEntityIds = scoped.Entities.Select(static e => e.EntityId).ToHashSet();
        var entityParentIds = scoped.Businesses.Select(static b => b.BusinessId)
            .Concat<Guid>(fundIds).Concat(sleeveIds).Concat(vehicleIds).Concat(portfolioIds).ToHashSet();
        var entityIds = scoped.Funds.SelectMany(static f => f.EntityIds)
            .Concat(scoped.Vehicles.Select(static v => v.LegalEntityId))
            .Concat(portfolios.Where(static p => p.EntityId.HasValue).Select(static p => p.EntityId!.Value))
            .Concat(scoped.OwnershipLinks
                .Where(link => scopedEntityIds.Contains(link.ChildNodeId) && entityParentIds.Contains(link.ParentNodeId))
                .Select(static link => link.ChildNodeId))
            .ToHashSet();
        return new AccountingAccountScope(entityIds, fundIds, sleeveIds, vehicleIds, portfolioIds);
    }

    private static bool IsAccountInAccountingScope(AccountSummaryDto account, AccountingStructureQuery query,
        AccountingAccountScope scope, IReadOnlyList<OwnershipLinkDto> ownershipLinks)
    {
        if (IsAccountLinkedToAccountingPortfolios(account, scope.PortfolioIds, ownershipLinks)) return true;
        if (HasAccountingAccountScopeFilter(query)) return MatchesDirectAccountingAccountFilters(account, query);
        return IsAccountInAccountingBusinessScope(account, scope, ownershipLinks);
    }

    private static bool IsAccountLinkedToAccountingPortfolios(AccountSummaryDto a, IReadOnlyCollection<Guid> portfolioIds, IReadOnlyList<OwnershipLinkDto> links) =>
        (TryParseGuid(a.PortfolioId, out var pid) && portfolioIds.Contains(pid)) || IsAccountLinkedToAny(a.AccountId, portfolioIds, links);

    private static bool HasAccountingAccountScopeFilter(AccountingStructureQuery q) =>
        q.ClientId.HasValue || q.FundId.HasValue || q.SleeveId.HasValue || q.VehicleId.HasValue || q.InvestmentPortfolioId.HasValue;

    private static bool MatchesDirectAccountingAccountFilters(AccountSummaryDto a, AccountingStructureQuery q)
    {
        if (q.ClientId.HasValue || q.InvestmentPortfolioId.HasValue) return false;
        var hasDirect = q.FundId.HasValue || q.SleeveId.HasValue || q.VehicleId.HasValue;
        return hasDirect && MatchOpt(a.FundId, q.FundId) && MatchOpt(a.SleeveId, q.SleeveId) && MatchOpt(a.VehicleId, q.VehicleId);
    }

    private static bool MatchOpt(Guid? value, Guid? filter) => !filter.HasValue || value == filter.Value;

    private static bool IsAccountInAccountingBusinessScope(AccountSummaryDto a, AccountingAccountScope scope, IReadOnlyList<OwnershipLinkDto> links) =>
        (a.EntityId.HasValue && scope.EntityIds.Contains(a.EntityId.Value))
        || (a.FundId.HasValue && scope.FundIds.Contains(a.FundId.Value))
        || (a.SleeveId.HasValue && scope.SleeveIds.Contains(a.SleeveId.Value))
        || (a.VehicleId.HasValue && scope.VehicleIds.Contains(a.VehicleId.Value))
        || IsAccountLinkedToAccountingPortfolios(a, scope.PortfolioIds, links)
        || IsAccountLinkedToAny(a.AccountId, scope.EntityIds, links)
        || IsAccountLinkedToAny(a.AccountId, scope.FundIds, links)
        || IsAccountLinkedToAny(a.AccountId, scope.SleeveIds, links)
        || IsAccountLinkedToAny(a.AccountId, scope.VehicleIds, links);

    // ── Private records ───────────────────────────────────────────────────────

    private sealed record StructureSnapshot(
        IReadOnlyList<OrganizationSummaryDto> Organizations,
        IReadOnlyList<BusinessSummaryDto> Businesses,
        IReadOnlyList<ClientSummaryDto> Clients,
        IReadOnlyList<FundSummaryDto> Funds,
        IReadOnlyList<SleeveSummaryDto> Sleeves,
        IReadOnlyList<VehicleSummaryDto> Vehicles,
        IReadOnlyList<LegalEntitySummaryDto> Entities,
        IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
        IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
        IReadOnlyList<FundStructureAssignmentDto> Assignments);

    private sealed record StructureScope(
        IReadOnlyList<OrganizationSummaryDto> Organizations,
        IReadOnlyList<BusinessSummaryDto> Businesses,
        IReadOnlyList<ClientSummaryDto> Clients,
        IReadOnlyList<FundSummaryDto> Funds,
        IReadOnlyList<SleeveSummaryDto> Sleeves,
        IReadOnlyList<VehicleSummaryDto> Vehicles,
        IReadOnlyList<LegalEntitySummaryDto> Entities,
        IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
        IReadOnlyList<AccountSummaryDto> Accounts,
        IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
        IReadOnlyList<FundStructureAssignmentDto> Assignments);

    private sealed record AccountingAccountScope(
        IReadOnlySet<Guid> EntityIds,
        IReadOnlySet<Guid> FundIds,
        IReadOnlySet<Guid> SleeveIds,
        IReadOnlySet<Guid> VehicleIds,
        IReadOnlySet<Guid> PortfolioIds);
}
