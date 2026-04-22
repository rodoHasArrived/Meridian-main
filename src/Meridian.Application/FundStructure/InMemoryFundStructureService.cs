using System.Text.Json;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.CashFlowInterop;
using Meridian.Storage.Archival;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Thread-safe governance structure service backed by an in-memory working set
/// with optional durable JSON snapshot persistence for local-first workflows.
/// </summary>
public sealed class InMemoryFundStructureService : IFundStructureService
{
    private static readonly StringComparer AssignmentComparer = StringComparer.OrdinalIgnoreCase;
    private const string DefaultCashFlowCurrency = "USD";
    private const string SecurityMasterInstrumentAssignmentType = "SecurityMasterInstrument";
    private const string SecurityInstrumentAssignmentType = "SecurityInstrument";
    private const string SecurityMasterRuleSourceKind = "SecurityMasterRule";
    private const string SecurityMasterCorporateActionSourceKind = "SecurityMasterCorporateAction";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly IFundAccountService _fundAccountService;
    private readonly IGovernanceSharedDataAccessService? _sharedDataAccessService;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly string? _persistencePath;
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly Dictionary<Guid, OrganizationSummaryDto> _organizations = new();
    private readonly Dictionary<Guid, BusinessSummaryDto> _businesses = new();
    private readonly Dictionary<Guid, ClientSummaryDto> _clients = new();
    private readonly Dictionary<Guid, FundSummaryDto> _funds = new();
    private readonly Dictionary<Guid, SleeveSummaryDto> _sleeves = new();
    private readonly Dictionary<Guid, VehicleSummaryDto> _vehicles = new();
    private readonly Dictionary<Guid, LegalEntitySummaryDto> _entities = new();
    private readonly Dictionary<Guid, InvestmentPortfolioSummaryDto> _investmentPortfolios = new();
    private readonly Dictionary<Guid, OwnershipLinkDto> _ownershipLinks = new();
    private readonly Dictionary<Guid, FundStructureAssignmentDto> _assignments = new();
    private readonly HashSet<Guid> _linkedAccountIds = [];
    private long _stateVersion;
    private long _persistedVersion;

    public InMemoryFundStructureService(IFundAccountService fundAccountService)
        : this(fundAccountService, sharedDataAccessService: null, securityMasterQueryService: null, persistencePath: null)
    {
    }

    public InMemoryFundStructureService(IFundAccountService fundAccountService, string? persistencePath)
        : this(fundAccountService, sharedDataAccessService: null, securityMasterQueryService: null, persistencePath)
    {
    }

    public InMemoryFundStructureService(
        IFundAccountService fundAccountService,
        IGovernanceSharedDataAccessService? sharedDataAccessService,
        string? persistencePath)
        : this(fundAccountService, sharedDataAccessService, securityMasterQueryService: null, persistencePath)
    {
    }

    public InMemoryFundStructureService(
        IFundAccountService fundAccountService,
        IGovernanceSharedDataAccessService? sharedDataAccessService,
        ISecurityMasterQueryService? securityMasterQueryService,
        string? persistencePath)
    {
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _sharedDataAccessService = sharedDataAccessService;
        _securityMasterQueryService = securityMasterQueryService;
        _persistencePath = string.IsNullOrWhiteSpace(persistencePath) ? null : persistencePath;
        LoadState();
    }

    private sealed record PersistedState(
        int Version,
        List<OrganizationSummaryDto> Organizations,
        List<BusinessSummaryDto> Businesses,
        List<ClientSummaryDto> Clients,
        List<FundSummaryDto> Funds,
        List<SleeveSummaryDto> Sleeves,
        List<VehicleSummaryDto> Vehicles,
        List<LegalEntitySummaryDto> Entities,
        List<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
        List<OwnershipLinkDto> OwnershipLinks,
        List<FundStructureAssignmentDto> Assignments,
        List<Guid> LinkedAccountIds);

    public async Task<OrganizationSummaryDto> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.OrganizationId);
            _organizations[request.OrganizationId] = summary;
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<BusinessSummaryDto> CreateBusinessAsync(
        CreateBusinessRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.BusinessId);
            EnsureOrganizationExistsLocked(request.OrganizationId);
            _businesses[request.BusinessId] = summary;
            CreateAutoLinkLocked(
                request.OrganizationId,
                request.BusinessId,
                OwnershipRelationshipTypeDto.Owns,
                request.EffectiveFrom,
                notes: "Organization root");
            summary = _businesses[request.BusinessId];
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<ClientSummaryDto> CreateClientAsync(
        CreateClientRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.ClientId);
            EnsureBusinessExistsLocked(request.BusinessId);
            _clients[request.ClientId] = summary;
            CreateAutoLinkLocked(
                request.BusinessId,
                request.ClientId,
                OwnershipRelationshipTypeDto.Advises,
                request.EffectiveFrom,
                notes: "Advisory client");
            summary = _clients[request.ClientId];
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<FundSummaryDto> CreateFundAsync(
        CreateFundRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.FundId);
            if (request.BusinessId.HasValue)
            {
                EnsureBusinessExistsLocked(request.BusinessId.Value);
            }

            _funds[request.FundId] = summary;

            if (request.BusinessId.HasValue)
            {
                CreateAutoLinkLocked(
                    request.BusinessId.Value,
                    request.FundId,
                    OwnershipRelationshipTypeDto.Operates,
                    request.EffectiveFrom,
                    notes: "Fund operating business");
                summary = _funds[request.FundId];
            }

            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<SleeveSummaryDto> CreateSleeveAsync(
        CreateSleeveRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.SleeveId);
            EnsureFundExistsLocked(request.FundId);
            _sleeves[request.SleeveId] = summary;
            CreateAutoLinkLocked(
                request.FundId,
                request.SleeveId,
                OwnershipRelationshipTypeDto.AllocatesTo,
                request.EffectiveFrom,
                notes: "Fund sleeve");
            summary = _sleeves[request.SleeveId];
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<VehicleSummaryDto> CreateVehicleAsync(
        CreateVehicleRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.VehicleId);
            EnsureFundExistsLocked(request.FundId);
            EnsureEntityExistsLocked(request.LegalEntityId);
            _vehicles[request.VehicleId] = summary;
            CreateAutoLinkLocked(
                request.FundId,
                request.VehicleId,
                OwnershipRelationshipTypeDto.Owns,
                request.EffectiveFrom,
                notes: "Fund vehicle");
            CreateAutoLinkLocked(
                request.LegalEntityId,
                request.VehicleId,
                OwnershipRelationshipTypeDto.Owns,
                request.EffectiveFrom,
                notes: "Legal entity vehicle");
            summary = _vehicles[request.VehicleId];
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<LegalEntitySummaryDto> CreateLegalEntityAsync(
        CreateLegalEntityRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.EntityId);
            _entities[request.EntityId] = summary;
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<InvestmentPortfolioSummaryDto> CreateInvestmentPortfolioAsync(
        CreateInvestmentPortfolioRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        EnsureSingleOperatingParent(request);

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            EnsureUniqueNodeLocked(request.InvestmentPortfolioId);
            EnsureBusinessExistsLocked(request.BusinessId);
            if (request.ClientId.HasValue)
            {
                EnsureClientExistsLocked(request.ClientId.Value);
            }

            if (request.FundId.HasValue)
            {
                EnsureFundExistsLocked(request.FundId.Value);
            }

            if (request.SleeveId.HasValue)
            {
                EnsureSleeveExistsLocked(request.SleeveId.Value);
            }

            if (request.VehicleId.HasValue)
            {
                EnsureVehicleExistsLocked(request.VehicleId.Value);
            }

            if (request.EntityId.HasValue)
            {
                EnsureEntityExistsLocked(request.EntityId.Value);
            }

            _investmentPortfolios[request.InvestmentPortfolioId] = summary;

            CreateAutoLinkLocked(
                request.BusinessId,
                request.InvestmentPortfolioId,
                OwnershipRelationshipTypeDto.Operates,
                request.EffectiveFrom,
                notes: "Business operating portfolio");

            if (request.ClientId.HasValue)
            {
                CreateAutoLinkLocked(
                    request.ClientId.Value,
                    request.InvestmentPortfolioId,
                    OwnershipRelationshipTypeDto.Owns,
                    request.EffectiveFrom,
                    notes: "Client investment portfolio");
            }

            if (request.FundId.HasValue)
            {
                CreateAutoLinkLocked(
                    request.FundId.Value,
                    request.InvestmentPortfolioId,
                    OwnershipRelationshipTypeDto.AllocatesTo,
                    request.EffectiveFrom,
                    notes: "Fund investment portfolio");
            }

            if (request.SleeveId.HasValue)
            {
                CreateAutoLinkLocked(
                    request.SleeveId.Value,
                    request.InvestmentPortfolioId,
                    OwnershipRelationshipTypeDto.AllocatesTo,
                    request.EffectiveFrom,
                    notes: "Sleeve investment portfolio");
            }

            if (request.VehicleId.HasValue)
            {
                CreateAutoLinkLocked(
                    request.VehicleId.Value,
                    request.InvestmentPortfolioId,
                    OwnershipRelationshipTypeDto.Owns,
                    request.EffectiveFrom,
                    notes: "Vehicle investment portfolio");
            }

            if (request.EntityId.HasValue)
            {
                CreateAutoLinkLocked(
                    request.EntityId.Value,
                    request.InvestmentPortfolioId,
                    OwnershipRelationshipTypeDto.Owns,
                    request.EffectiveFrom,
                    notes: "Entity overlay");
            }

            summary = _investmentPortfolios[request.InvestmentPortfolioId];
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return summary;
    }

    public async Task<OwnershipLinkDto> LinkNodesAsync(
        LinkFundStructureNodesRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var parentKind = await ResolveNodeKindAsync(request.ParentNodeId, ct).ConfigureAwait(false);
        var childKind = await ResolveNodeKindAsync(request.ChildNodeId, ct).ConfigureAwait(false);

        if (parentKind is null)
        {
            throw new InvalidOperationException($"Parent node {request.ParentNodeId} was not found.");
        }

        if (childKind is null)
        {
            throw new InvalidOperationException($"Child node {request.ChildNodeId} was not found.");
        }

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

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            if (_ownershipLinks.ContainsKey(request.OwnershipLinkId))
            {
                throw new InvalidOperationException(
                    $"Ownership link {request.OwnershipLinkId} already exists.");
            }

            if (parentKind == FundStructureNodeKindDto.Account)
            {
                _linkedAccountIds.Add(request.ParentNodeId);
            }

            if (childKind == FundStructureNodeKindDto.Account)
            {
                _linkedAccountIds.Add(request.ChildNodeId);
            }

            _ownershipLinks[request.OwnershipLinkId] = link;
            ApplyOwnershipLinkLocked(link);
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return link;
    }

    public async Task<FundStructureAssignmentDto> AssignNodeAsync(
        AssignFundStructureNodeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var kind = await ResolveNodeKindAsync(request.NodeId, ct).ConfigureAwait(false);
        if (kind is null)
        {
            throw new InvalidOperationException($"Node {request.NodeId} was not found.");
        }

        var assignment = new FundStructureAssignmentDto(
            request.AssignmentId,
            request.NodeId,
            request.AssignmentType,
            request.AssignmentReference,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.IsPrimary);

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            if (_assignments.ContainsKey(request.AssignmentId))
            {
                throw new InvalidOperationException(
                    $"Assignment {request.AssignmentId} already exists.");
            }

            if (kind == FundStructureNodeKindDto.Account)
            {
                _linkedAccountIds.Add(request.NodeId);
            }

            _assignments[request.AssignmentId] = assignment;
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return assignment;
    }

    public async Task<OrganizationStructureGraphDto> GetOrganizationStructureAsync(
        OrganizationStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snapshot = CreateSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var visibleAccounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false),
            sharedDataAccess);
        var filtered = FilterForOrganizationScope(
            snapshot,
            visibleAccounts,
            query.OrganizationId,
            query.BusinessId,
            query.ActiveOnly,
            asOf);
        var enrichedPortfolios = AttachSharedDataAccess(filtered.InvestmentPortfolios, sharedDataAccess);

        var nodes = BuildNodes(filtered)
            .Where(node => query.NodeId is null || node.NodeId == query.NodeId.Value)
            .Where(node => query.NodeKind is null || node.Kind == query.NodeKind.Value)
            .ToList();

        var nodeIds = nodes.Select(static node => node.NodeId).ToHashSet();
        var links = filtered.OwnershipLinks
            .Where(link => nodeIds.Contains(link.ParentNodeId) && nodeIds.Contains(link.ChildNodeId))
            .ToList();
        var assignments = filtered.Assignments
            .Where(assignment => nodeIds.Contains(assignment.NodeId))
            .ToList();

        return new OrganizationStructureGraphDto(
            filtered.Organizations,
            filtered.Businesses,
            filtered.Clients,
            filtered.Funds,
            filtered.Sleeves,
            filtered.Vehicles,
            filtered.Entities,
            enrichedPortfolios,
            filtered.Accounts,
            nodes,
            links,
            assignments,
            sharedDataAccess);
    }

    public async Task<FundStructureGraphDto> GetFundStructureGraphAsync(
        FundStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snapshot = CreateSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var visibleAccounts = await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false);
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, query.ActiveOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));
        var activeAssignments = FilterVisible(snapshot.Assignments, query.ActiveOnly, asOf, static assignment => (assignment.EffectiveFrom, assignment.EffectiveTo));
        var funds = FilterVisible(snapshot.Funds, query.ActiveOnly, asOf, static fund => (fund.EffectiveFrom, fund.EffectiveTo));

        if (query.FundId.HasValue)
        {
            funds = funds.Where(fund => fund.FundId == query.FundId.Value).ToList();
        }

        var fundIds = funds.Select(static fund => fund.FundId).ToHashSet();
        var sleeves = FilterVisible(snapshot.Sleeves, query.ActiveOnly, asOf, static sleeve => (sleeve.EffectiveFrom, sleeve.EffectiveTo))
            .Where(sleeve => fundIds.Contains(sleeve.FundId))
            .ToList();
        var sleeveIds = sleeves.Select(static sleeve => sleeve.SleeveId).ToHashSet();
        var vehicles = FilterVisible(snapshot.Vehicles, query.ActiveOnly, asOf, static vehicle => (vehicle.EffectiveFrom, vehicle.EffectiveTo))
            .Where(vehicle => fundIds.Contains(vehicle.FundId))
            .ToList();
        var vehicleIds = vehicles.Select(static vehicle => vehicle.VehicleId).ToHashSet();
        var portfolioIds = FilterVisible(snapshot.InvestmentPortfolios, query.ActiveOnly, asOf, static portfolio => (portfolio.EffectiveFrom, portfolio.EffectiveTo))
            .Where(portfolio =>
                (portfolio.FundId.HasValue && fundIds.Contains(portfolio.FundId.Value))
                || (portfolio.SleeveId.HasValue && sleeveIds.Contains(portfolio.SleeveId.Value))
                || (portfolio.VehicleId.HasValue && vehicleIds.Contains(portfolio.VehicleId.Value)))
            .Select(static portfolio => portfolio.InvestmentPortfolioId)
            .ToHashSet();
        var entityIds = vehicles.Select(static vehicle => vehicle.LegalEntityId)
            .Concat(funds.SelectMany(static fund => fund.EntityIds))
            .ToHashSet();

        var accounts = visibleAccounts
            .Where(account =>
                (account.FundId.HasValue && fundIds.Contains(account.FundId.Value))
                || (account.SleeveId.HasValue && sleeveIds.Contains(account.SleeveId.Value))
                || (account.VehicleId.HasValue && vehicleIds.Contains(account.VehicleId.Value))
                || (account.EntityId.HasValue && entityIds.Contains(account.EntityId.Value))
                || IsAccountLinkedToAny(account.AccountId, fundIds, activeLinks)
                || IsAccountLinkedToAny(account.AccountId, sleeveIds, activeLinks)
                || IsAccountLinkedToAny(account.AccountId, vehicleIds, activeLinks)
                || IsAccountLinkedToAny(account.AccountId, portfolioIds, activeLinks))
            .ToList();
        var entities = FilterVisible(snapshot.Entities, query.ActiveOnly, asOf, static entity => (entity.EffectiveFrom, entity.EffectiveTo))
            .Where(entity => entityIds.Contains(entity.EntityId) || IsEntityLinkedToScope(entity.EntityId, fundIds, activeLinks))
            .ToList();

        var nodes = BuildNodes(new StructureScope(
                Organizations: [],
                Businesses: [],
                Clients: [],
                Funds: funds,
                Sleeves: sleeves,
                Vehicles: vehicles,
                Entities: entities,
                InvestmentPortfolios: [],
                Accounts: accounts,
                OwnershipLinks: activeLinks,
                Assignments: activeAssignments))
            .Where(node => node.Kind is FundStructureNodeKindDto.Fund
                or FundStructureNodeKindDto.Sleeve
                or FundStructureNodeKindDto.Vehicle
                or FundStructureNodeKindDto.Entity
                or FundStructureNodeKindDto.Account)
            .Where(node => query.NodeId is null || node.NodeId == query.NodeId.Value)
            .Where(node => query.NodeKind is null || node.Kind == query.NodeKind.Value)
            .ToList();

        var nodeIds = nodes.Select(static node => node.NodeId).ToHashSet();
        var links = activeLinks
            .Where(link => nodeIds.Contains(link.ParentNodeId) && nodeIds.Contains(link.ChildNodeId))
            .ToList();
        var assignments = activeAssignments
            .Where(assignment => nodeIds.Contains(assignment.NodeId))
            .ToList();

        return new FundStructureGraphDto(nodes, links, assignments);
    }

    public async Task<AdvisoryStructureViewDto?> GetAdvisoryViewAsync(
        AdvisoryStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snapshot = CreateSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var accounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false),
            sharedDataAccess);
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, query.ActiveOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));

        var business = FilterVisible(snapshot.Businesses, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .FirstOrDefault(candidate => candidate.BusinessId == query.BusinessId);
        if (business is null)
        {
            return null;
        }

        if (query.OrganizationId.HasValue && business.OrganizationId != query.OrganizationId.Value)
        {
            return null;
        }

        var organization = FilterVisible(snapshot.Organizations, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .FirstOrDefault(candidate => candidate.OrganizationId == business.OrganizationId);
        if (organization is null)
        {
            return null;
        }

        var clients = FilterVisible(snapshot.Clients, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(candidate => query.ClientId is null || candidate.ClientId == query.ClientId.Value)
            .ToList();
        var portfolios = FilterVisible(snapshot.InvestmentPortfolios, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(IsAdvisoryPortfolio)
            .Where(candidate => query.InvestmentPortfolioId is null || candidate.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        var enrichedPortfolios = AttachSharedDataAccess(portfolios, sharedDataAccess);

        var clientViews = clients
            .Select(client =>
            {
                var clientPortfolios = enrichedPortfolios
                    .Where(portfolio => portfolio.ClientId == client.ClientId)
                    .ToList();
                var portfolioIds = clientPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
                var clientAccounts = GetScopeAccounts(
                        accounts,
                        activeLinks,
                        entityIds: Array.Empty<Guid>(),
                        fundIds: Array.Empty<Guid>(),
                        sleeveIds: Array.Empty<Guid>(),
                        vehicleIds: Array.Empty<Guid>(),
                        portfolioIds)
                    .ToList();

                return new AdvisoryClientViewDto(client, clientPortfolios, clientAccounts);
            })
            .ToList();

        var unassignedPortfolios = portfolios
            .Where(portfolio => portfolio.ClientId is null)
            .ToList();
        unassignedPortfolios = enrichedPortfolios
            .Where(portfolio => portfolio.ClientId is null)
            .ToList();
        var assignedAccountIds = clientViews
            .SelectMany(static view => view.Accounts)
            .Select(static account => account.AccountId)
            .ToHashSet();
        var unassignedAccounts = GetScopeAccounts(
                accounts,
                activeLinks,
                entityIds: Array.Empty<Guid>(),
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                vehicleIds: Array.Empty<Guid>(),
                unassignedPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet())
            .Where(account => !assignedAccountIds.Contains(account.AccountId))
            .ToList();

        return new AdvisoryStructureViewDto(
            organization,
            business,
            clientViews,
            unassignedPortfolios,
            unassignedAccounts,
            sharedDataAccess);
    }

    public async Task<FundOperatingViewDto?> GetFundOperatingViewAsync(
        FundOperatingStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snapshot = CreateSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var accounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false),
            sharedDataAccess);
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, query.ActiveOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));

        var business = FilterVisible(snapshot.Businesses, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .FirstOrDefault(candidate => candidate.BusinessId == query.BusinessId);
        if (business is null)
        {
            return null;
        }

        if (query.OrganizationId.HasValue && business.OrganizationId != query.OrganizationId.Value)
        {
            return null;
        }

        var organization = FilterVisible(snapshot.Organizations, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .FirstOrDefault(candidate => candidate.OrganizationId == business.OrganizationId);
        if (organization is null)
        {
            return null;
        }

        var funds = FilterVisible(snapshot.Funds, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(candidate => query.FundId is null || candidate.FundId == query.FundId.Value)
            .ToList();
        var fundIds = funds.Select(static fund => fund.FundId).ToHashSet();
        var allSleeves = FilterVisible(snapshot.Sleeves, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => fundIds.Contains(candidate.FundId))
            .Where(candidate => query.SleeveId is null || candidate.SleeveId == query.SleeveId.Value)
            .ToList();
        var allVehicles = FilterVisible(snapshot.Vehicles, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => fundIds.Contains(candidate.FundId))
            .Where(candidate => query.VehicleId is null || candidate.VehicleId == query.VehicleId.Value)
            .ToList();
        var allPortfolios = FilterVisible(snapshot.InvestmentPortfolios, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .Where(candidate => candidate.BusinessId == business.BusinessId)
            .Where(IsFundPortfolio)
            .Where(candidate => query.InvestmentPortfolioId is null || candidate.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        var enrichedPortfolios = AttachSharedDataAccess(allPortfolios, sharedDataAccess);
        var entities = FilterVisible(snapshot.Entities, query.ActiveOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo))
            .ToDictionary(static entity => entity.EntityId);

        var fundSlices = new List<FundOperatingSliceDto>();
        foreach (var fund in funds)
        {
            var sleeves = allSleeves.Where(sleeve => sleeve.FundId == fund.FundId).ToList();
            var vehicles = allVehicles.Where(vehicle => vehicle.FundId == fund.FundId).ToList();
            var directPortfolios = enrichedPortfolios
                .Where(portfolio => portfolio.FundId == fund.FundId && portfolio.SleeveId is null && portfolio.VehicleId is null)
                .ToList();
            var directAccounts = GetScopeAccounts(
                    accounts,
                    activeLinks,
                    fund.EntityIds.ToHashSet(),
                    fundIds: new[] { fund.FundId },
                    sleeveIds: Array.Empty<Guid>(),
                    vehicleIds: Array.Empty<Guid>(),
                    directPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet())
                .Where(account => account.SleeveId is null && account.VehicleId is null)
                .ToList();

            var sleeveSlices = sleeves
                .Select(sleeve =>
                {
                    var portfolios = enrichedPortfolios.Where(portfolio => portfolio.SleeveId == sleeve.SleeveId).ToList();
                    var sleeveAccounts = GetScopeAccounts(
                        accounts,
                        activeLinks,
                        entityIds: Array.Empty<Guid>(),
                        fundIds: Array.Empty<Guid>(),
                        sleeveIds: new[] { sleeve.SleeveId },
                        vehicleIds: Array.Empty<Guid>(),
                        portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet()).ToList();

                    return new FundSleeveOperatingViewDto(sleeve, portfolios, sleeveAccounts);
                })
                .ToList();

            var vehicleSlices = vehicles
                .Select(vehicle =>
                {
                    var portfolios = enrichedPortfolios.Where(portfolio => portfolio.VehicleId == vehicle.VehicleId).ToList();
                    var vehicleAccounts = GetScopeAccounts(
                        accounts,
                        activeLinks,
                        entityIds: new[] { vehicle.LegalEntityId },
                        fundIds: Array.Empty<Guid>(),
                        sleeveIds: Array.Empty<Guid>(),
                        vehicleIds: new[] { vehicle.VehicleId },
                        portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet()).ToList();

                    entities.TryGetValue(vehicle.LegalEntityId, out var entity);
                    return new VehicleOperatingViewDto(vehicle, entity, portfolios, vehicleAccounts);
                })
                .ToList();

            fundSlices.Add(new FundOperatingSliceDto(
                fund,
                directPortfolios,
                directAccounts,
                sleeveSlices,
                vehicleSlices));
        }

        var assignedPortfolioIds = fundSlices
            .SelectMany(static slice => slice.InvestmentPortfolios)
            .Concat(fundSlices.SelectMany(static slice => slice.Sleeves).SelectMany(static sleeve => sleeve.InvestmentPortfolios))
            .Concat(fundSlices.SelectMany(static slice => slice.Vehicles).SelectMany(static vehicle => vehicle.InvestmentPortfolios))
            .Select(static portfolio => portfolio.InvestmentPortfolioId)
            .ToHashSet();
        var unassignedPortfolios = enrichedPortfolios
            .Where(portfolio => !assignedPortfolioIds.Contains(portfolio.InvestmentPortfolioId))
            .ToList();
        var assignedAccountIds = fundSlices
            .SelectMany(static slice => slice.Accounts)
            .Concat(fundSlices.SelectMany(static slice => slice.Sleeves).SelectMany(static sleeve => sleeve.Accounts))
            .Concat(fundSlices.SelectMany(static slice => slice.Vehicles).SelectMany(static vehicle => vehicle.Accounts))
            .Select(static account => account.AccountId)
            .ToHashSet();
        var unassignedAccounts = GetScopeAccounts(
                accounts,
                activeLinks,
                entityIds: Array.Empty<Guid>(),
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                vehicleIds: Array.Empty<Guid>(),
                unassignedPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet())
            .Where(account => !assignedAccountIds.Contains(account.AccountId))
            .ToList();

        return new FundOperatingViewDto(
            organization,
            business,
            fundSlices,
            unassignedPortfolios,
            unassignedAccounts,
            sharedDataAccess);
    }

    public async Task<AccountingStructureViewDto> GetAccountingViewAsync(
        AccountingStructureQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var snapshot = CreateSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var visibleAccounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false),
            sharedDataAccess);
        var scoped = FilterForOrganizationScope(
            snapshot,
            visibleAccounts,
            query.OrganizationId,
            query.BusinessId,
            query.ActiveOnly,
            asOf);

        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => query.ClientId is null || portfolio.ClientId == query.ClientId.Value)
            .Where(portfolio => query.FundId is null || portfolio.FundId == query.FundId.Value)
            .Where(portfolio => query.SleeveId is null || portfolio.SleeveId == query.SleeveId.Value)
            .Where(portfolio => query.VehicleId is null || portfolio.VehicleId == query.VehicleId.Value)
            .Where(portfolio => query.InvestmentPortfolioId is null || portfolio.InvestmentPortfolioId == query.InvestmentPortfolioId.Value)
            .ToList();
        portfolios = AttachSharedDataAccess(portfolios, sharedDataAccess).ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var accounts = scoped.Accounts
            .Where(account => query.LedgerReference is null || string.Equals(account.LedgerReference, query.LedgerReference, StringComparison.OrdinalIgnoreCase))
            .Where(account =>
                portfolioIds.Count == 0
                || (TryParseGuid(account.PortfolioId, out var portfolioId) && portfolioIds.Contains(portfolioId))
                || IsAccountLinkedToAny(account.AccountId, portfolioIds, scoped.OwnershipLinks))
            .ToList();

        var organization = query.OrganizationId.HasValue
            ? scoped.Organizations.FirstOrDefault(candidate => candidate.OrganizationId == query.OrganizationId.Value)
            : scoped.Organizations.FirstOrDefault();
        var business = query.BusinessId.HasValue
            ? scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == query.BusinessId.Value)
            : scoped.Businesses.FirstOrDefault();
        var portfolioById = portfolios.ToDictionary(static portfolio => portfolio.InvestmentPortfolioId);
        var ledgerAssignments = LedgerGroupingRules.BuildLedgerAssignments(scoped.Assignments);

        var ledgerGroups = accounts
            .GroupBy(account => LedgerGroupingRules.ResolveLedgerGroupId(account, ledgerAssignments))
            .Select(group =>
            {
                var accountIds = group.Select(static account => account.AccountId).ToList();
                var relatedPortfolioIds = group
                    .Select(account => TryParseGuid(account.PortfolioId, out var portfolioId) ? portfolioId : Guid.Empty)
                    .Where(static portfolioId => portfolioId != Guid.Empty)
                    .Concat(scoped.OwnershipLinks
                        .Where(link => accountIds.Contains(link.ChildNodeId) && portfolioById.ContainsKey(link.ParentNodeId))
                        .Select(static link => link.ParentNodeId))
                    .Distinct()
                    .ToList();
                var relatedPortfolios = relatedPortfolioIds
                    .Where(portfolioById.ContainsKey)
                    .Select(portfolioId => portfolioById[portfolioId])
                    .ToList();

                return new LedgerGroupSummaryDto(
                    group.Key,
                    DisplayName: group.Key.Value,
                    accountIds,
                    relatedPortfolioIds,
                    relatedPortfolios.Where(static portfolio => portfolio.ClientId.HasValue).Select(static portfolio => portfolio.ClientId!.Value).Distinct().ToList(),
                    group.Where(static account => account.FundId.HasValue).Select(static account => account.FundId!.Value)
                        .Concat(relatedPortfolios.Where(static portfolio => portfolio.FundId.HasValue).Select(static portfolio => portfolio.FundId!.Value))
                        .Distinct()
                        .ToList(),
                    group.Where(static account => account.SleeveId.HasValue).Select(static account => account.SleeveId!.Value)
                        .Concat(relatedPortfolios.Where(static portfolio => portfolio.SleeveId.HasValue).Select(static portfolio => portfolio.SleeveId!.Value))
                        .Distinct()
                        .ToList(),
                    group.Where(static account => account.VehicleId.HasValue).Select(static account => account.VehicleId!.Value)
                        .Concat(relatedPortfolios.Where(static portfolio => portfolio.VehicleId.HasValue).Select(static portfolio => portfolio.VehicleId!.Value))
                        .Distinct()
                        .ToList(),
                    SharedDataAccess: sharedDataAccess);
            })
            .OrderBy(static group => group.DisplayName)
            .ToList();

        return new AccountingStructureViewDto(
            organization,
            business,
            portfolios,
            accounts,
            ledgerGroups,
            sharedDataAccess);
    }

    public async Task<GovernanceCashFlowViewDto?> GetCashFlowViewAsync(
        GovernanceCashFlowQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        ValidateCashFlowQuery(query);

        var snapshot = CreateSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var historicalDays = Math.Max(1, query.HistoricalDays);
        var forecastDays = Math.Max(1, query.ForecastDays);
        var bucketDays = Math.Max(1, query.BucketDays);
        var historicalWindowStart = StartOfDayUtc(asOf).AddDays(-(historicalDays - 1));
        var projectionWindowStart = StartOfDayUtc(asOf).AddDays(1);
        var projectionWindowEnd = projectionWindowStart.AddDays(forecastDays);
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var visibleAccounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false),
            sharedDataAccess);
        var scoped = FilterForOrganizationScope(
            snapshot,
            visibleAccounts,
            query.OrganizationId,
            query.BusinessId,
            query.ActiveOnly,
            asOf);
        var ledgerAssignments = BuildLedgerAssignments(scoped.Assignments);
        var resolvedScope = ResolveCashFlowScope(query, scoped, ledgerAssignments);
        if (resolvedScope is null)
        {
            return null;
        }

        var currency = ResolveCashFlowCurrency(query.Currency, resolvedScope);
        var accountWindows = resolvedScope.Accounts.Count == 0
            ? Array.Empty<AccountCashFlowWindow>()
            : await Task.WhenAll(resolvedScope.Accounts.Select(account =>
                BuildAccountCashFlowWindowAsync(
                    account,
                    scoped,
                    currency,
                    historicalWindowStart,
                    asOf,
                    projectionWindowStart,
                    projectionWindowEnd,
                    forecastDays,
                    bucketDays,
                    ct))).ConfigureAwait(false);

        var accountViews = accountWindows
            .Select(window => new GovernanceCashFlowAccountViewDto(
                window.Account.AccountId,
                window.Account.AccountCode,
                window.Account.DisplayName,
                window.Account.BaseCurrency,
                window.Account.LedgerReference,
                window.CurrentCashBalance,
                window.RealizedNetFlow,
                window.ProjectedNetFlow,
                window.RealizedEntries.Count,
                window.ProjectedEntries.Count,
                window.LatestSnapshotDate,
                window.UsedTrendFallback,
                window.SecurityProjectedEntryCount,
                window.UsedSecurityMasterRules,
                window.Account.SharedDataAccess))
            .OrderBy(static account => account.DisplayName)
            .ToList();

        var realizedEntries = accountWindows
            .SelectMany(static window => window.RealizedEntries)
            .OrderBy(static entry => entry.EventDate)
            .ThenBy(static entry => entry.AccountDisplayName)
            .ToList();
        var projectedEntries = accountWindows
            .SelectMany(static window => window.ProjectedEntries)
            .OrderBy(static entry => entry.EventDate)
            .ThenBy(static entry => entry.AccountDisplayName)
            .ToList();

        var realizedLadder = BuildCashFlowLadder(
            historicalWindowStart,
            historicalDays,
            bucketDays,
            currency,
            realizedEntries);
        var projectedLadder = BuildCashFlowLadder(
            projectionWindowStart,
            forecastDays,
            bucketDays,
            currency,
            projectedEntries);
        var varianceBuckets = BuildVarianceBuckets(realizedLadder, projectedLadder);
        var currentCashBalance = accountViews.Sum(static account => account.CurrentCashBalance);
        var varianceSummary = new GovernanceCashFlowVarianceSummaryDto(
            RealizedInflows: realizedLadder.TotalProjectedInflows,
            RealizedOutflows: realizedLadder.TotalProjectedOutflows,
            RealizedNetFlow: realizedLadder.NetPosition,
            ProjectedInflows: projectedLadder.TotalProjectedInflows,
            ProjectedOutflows: projectedLadder.TotalProjectedOutflows,
            ProjectedNetFlow: projectedLadder.NetPosition,
            VarianceAmount: projectedLadder.NetPosition - realizedLadder.NetPosition,
            ComparisonBasis: "Projected next window vs trailing realized window");

        return new GovernanceCashFlowViewDto(
            resolvedScope.Scope,
            asOf,
            historicalWindowStart,
            projectionWindowEnd,
            currency,
            historicalDays,
            forecastDays,
            bucketDays,
            accountViews.Count,
            currentCashBalance,
            currentCashBalance + projectedLadder.NetPosition,
            accountViews,
            realizedEntries,
            projectedEntries,
            realizedLadder,
            projectedLadder,
            varianceSummary,
            varianceBuckets,
            sharedDataAccess,
            projectedEntries.Count(static entry => entry.SecurityId.HasValue));
    }

    private static void ValidateCashFlowQuery(GovernanceCashFlowQuery query)
    {
        if (query.HistoricalDays <= 0)
        {
            throw new InvalidOperationException("HistoricalDays must be at least 1.");
        }

        if (query.ForecastDays <= 0)
        {
            throw new InvalidOperationException("ForecastDays must be at least 1.");
        }

        if (query.BucketDays <= 0)
        {
            throw new InvalidOperationException("BucketDays must be at least 1.");
        }
    }

    private static IReadOnlyDictionary<Guid, LedgerGroupId> BuildLedgerAssignments(
        IReadOnlyList<FundStructureAssignmentDto> assignments) =>
        LedgerGroupingRules.BuildLedgerAssignments(assignments);

    private static ResolvedCashFlowScope? ResolveCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped,
        IReadOnlyDictionary<Guid, LedgerGroupId> ledgerAssignments)
    {
        return query.ScopeKind switch
        {
            GovernanceCashFlowScopeKindDto.Organization => ResolveOrganizationCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Business => ResolveBusinessCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Client => ResolveClientCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Fund => ResolveFundCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Sleeve => ResolveSleeveCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Vehicle => ResolveVehicleCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.InvestmentPortfolio => ResolveInvestmentPortfolioCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.Account => ResolveAccountCashFlowScope(query, scoped),
            GovernanceCashFlowScopeKindDto.LedgerGroup => ResolveLedgerGroupCashFlowScope(query, scoped, ledgerAssignments),
            _ => throw new InvalidOperationException($"Unsupported governance cash-flow scope '{query.ScopeKind}'.")
        };
    }

    private static ResolvedCashFlowScope? ResolveOrganizationCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var organization = ResolveSelectedScope(
            query.OrganizationId,
            scoped.Organizations,
            static candidate => candidate.OrganizationId,
            "organization");
        if (organization is null)
        {
            return null;
        }

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Organization,
                organization.Name,
                organization.OrganizationId,
                query.BusinessId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                scoped.Accounts.Select(static account => account.AccountId).ToList(),
                scoped.InvestmentPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToList()),
            scoped.Accounts,
            organization.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveBusinessCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var business = ResolveSelectedScope(
            query.BusinessId,
            scoped.Businesses,
            static candidate => candidate.BusinessId,
            "business");
        if (business is null)
        {
            return null;
        }

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Business,
                business.Name,
                business.OrganizationId,
                business.BusinessId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                scoped.Accounts.Select(static account => account.AccountId).ToList(),
                scoped.InvestmentPortfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToList()),
            scoped.Accounts,
            business.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveClientCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var client = ResolveSelectedScope(
            query.ClientId,
            scoped.Clients,
            static candidate => candidate.ClientId,
            "client");
        if (client is null)
        {
            return null;
        }

        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => portfolio.ClientId == client.ClientId)
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds: Array.Empty<Guid>(),
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                vehicleIds: Array.Empty<Guid>(),
                portfolioIds)
            .ToList();
        var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == client.BusinessId);

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Client,
                client.Name,
                business?.OrganizationId,
                client.BusinessId,
                client.ClientId,
                null,
                null,
                null,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            client.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveFundCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var fund = ResolveSelectedScope(
            query.FundId,
            scoped.Funds,
            static candidate => candidate.FundId,
            "fund");
        if (fund is null)
        {
            return null;
        }

        var sleeves = scoped.Sleeves.Where(candidate => candidate.FundId == fund.FundId).ToList();
        var sleeveIds = sleeves.Select(static sleeve => sleeve.SleeveId).ToHashSet();
        var vehicles = scoped.Vehicles.Where(candidate => candidate.FundId == fund.FundId).ToList();
        var vehicleIds = vehicles.Select(static vehicle => vehicle.VehicleId).ToHashSet();
        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio =>
                portfolio.FundId == fund.FundId
                || (portfolio.SleeveId.HasValue && sleeveIds.Contains(portfolio.SleeveId.Value))
                || (portfolio.VehicleId.HasValue && vehicleIds.Contains(portfolio.VehicleId.Value)))
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var entityIds = fund.EntityIds
            .Concat(vehicles.Select(static vehicle => vehicle.LegalEntityId))
            .Concat(portfolios.Where(static portfolio => portfolio.EntityId.HasValue).Select(static portfolio => portfolio.EntityId!.Value))
            .ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds,
                [fund.FundId],
                sleeveIds,
                vehicleIds,
                portfolioIds)
            .ToList();

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Fund,
                fund.Name,
                query.OrganizationId,
                fund.BusinessId,
                null,
                fund.FundId,
                null,
                null,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            fund.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveSleeveCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var sleeve = ResolveSelectedScope(
            query.SleeveId,
            scoped.Sleeves,
            static candidate => candidate.SleeveId,
            "sleeve");
        if (sleeve is null)
        {
            return null;
        }

        var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == sleeve.FundId);
        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => portfolio.SleeveId == sleeve.SleeveId)
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var entityIds = portfolios
            .Where(static portfolio => portfolio.EntityId.HasValue)
            .Select(static portfolio => portfolio.EntityId!.Value)
            .ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds,
                fundIds: Array.Empty<Guid>(),
                [sleeve.SleeveId],
                vehicleIds: Array.Empty<Guid>(),
                portfolioIds)
            .ToList();

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Sleeve,
                sleeve.Name,
                query.OrganizationId,
                fund?.BusinessId,
                null,
                fund?.FundId,
                sleeve.SleeveId,
                null,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            fund?.BaseCurrency ?? accounts.FirstOrDefault()?.BaseCurrency ?? DefaultCashFlowCurrency);
    }

    private static ResolvedCashFlowScope? ResolveVehicleCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var vehicle = ResolveSelectedScope(
            query.VehicleId,
            scoped.Vehicles,
            static candidate => candidate.VehicleId,
            "vehicle");
        if (vehicle is null)
        {
            return null;
        }

        var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == vehicle.FundId);
        var portfolios = scoped.InvestmentPortfolios
            .Where(portfolio => portfolio.VehicleId == vehicle.VehicleId)
            .ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var entityIds = portfolios
            .Where(static portfolio => portfolio.EntityId.HasValue)
            .Select(static portfolio => portfolio.EntityId!.Value)
            .Append(vehicle.LegalEntityId)
            .ToHashSet();
        var accounts = GetScopeAccounts(
                scoped.Accounts,
                scoped.OwnershipLinks,
                entityIds,
                fundIds: Array.Empty<Guid>(),
                sleeveIds: Array.Empty<Guid>(),
                [vehicle.VehicleId],
                portfolioIds)
            .ToList();

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Vehicle,
                vehicle.Name,
                query.OrganizationId,
                fund?.BusinessId,
                null,
                fund?.FundId,
                null,
                vehicle.VehicleId,
                null,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds.ToList()),
            accounts,
            vehicle.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveInvestmentPortfolioCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var portfolio = ResolveSelectedScope(
            query.InvestmentPortfolioId,
            scoped.InvestmentPortfolios,
            static candidate => candidate.InvestmentPortfolioId,
            "investment portfolio");
        if (portfolio is null)
        {
            return null;
        }

        var accounts = scoped.Accounts
            .Where(account =>
                (TryParseGuid(account.PortfolioId, out var portfolioId) && portfolioId == portfolio.InvestmentPortfolioId)
                || IsAccountLinkedToAny(account.AccountId, [portfolio.InvestmentPortfolioId], scoped.OwnershipLinks))
            .DistinctBy(static account => account.AccountId)
            .ToList();
        var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == portfolio.BusinessId);

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.InvestmentPortfolio,
                portfolio.Name,
                business?.OrganizationId,
                portfolio.BusinessId,
                portfolio.ClientId,
                portfolio.FundId,
                portfolio.SleeveId,
                portfolio.VehicleId,
                portfolio.InvestmentPortfolioId,
                null,
                null,
                accounts.Select(static account => account.AccountId).ToList(),
                [portfolio.InvestmentPortfolioId]),
            accounts,
            portfolio.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveAccountCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped)
    {
        var account = ResolveSelectedScope(
            query.AccountId,
            scoped.Accounts,
            static candidate => candidate.AccountId,
            "account");
        if (account is null)
        {
            return null;
        }

        var portfolioIds = new List<Guid>();
        if (TryParseGuid(account.PortfolioId, out var portfolioId))
        {
            portfolioIds.Add(portfolioId);
        }

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.Account,
                account.DisplayName,
                query.OrganizationId,
                query.BusinessId,
                null,
                account.FundId,
                account.SleeveId,
                account.VehicleId,
                portfolioIds.FirstOrDefault() == Guid.Empty ? null : portfolioIds.FirstOrDefault(),
                account.AccountId,
                null,
                [account.AccountId],
                portfolioIds),
            [account],
            account.BaseCurrency);
    }

    private static ResolvedCashFlowScope? ResolveLedgerGroupCashFlowScope(
        GovernanceCashFlowQuery query,
        StructureScope scoped,
        IReadOnlyDictionary<Guid, LedgerGroupId> ledgerAssignments)
    {
        if (!query.LedgerGroupId.HasValue)
        {
            throw new InvalidOperationException("LedgerGroup scope requires LedgerGroupId.");
        }

        var accounts = scoped.Accounts
            .Where(account => LedgerGroupingRules.ResolveLedgerGroupId(account, ledgerAssignments) == query.LedgerGroupId.Value)
            .ToList();
        var portfolioIds = GetRelatedPortfolioIds(accounts, scoped.OwnershipLinks, scoped.InvestmentPortfolios);

        return new ResolvedCashFlowScope(
            new GovernanceCashFlowScopeDto(
                GovernanceCashFlowScopeKindDto.LedgerGroup,
                query.LedgerGroupId.Value.Value,
                query.OrganizationId,
                query.BusinessId,
                null,
                null,
                null,
                null,
                null,
                null,
                query.LedgerGroupId,
                accounts.Select(static account => account.AccountId).ToList(),
                portfolioIds),
            accounts,
            accounts.FirstOrDefault()?.BaseCurrency ?? DefaultCashFlowCurrency);
    }

    private static T? ResolveSelectedScope<T>(
        Guid? requestedId,
        IReadOnlyList<T> candidates,
        Func<T, Guid> idSelector,
        string scopeLabel)
    {
        if (requestedId.HasValue)
        {
            return candidates.FirstOrDefault(candidate => idSelector(candidate) == requestedId.Value);
        }

        return candidates.Count switch
        {
            0 => default,
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                $"{scopeLabel} scope requires an explicit identifier when multiple {scopeLabel}s are available.")
        };
    }

    private static IReadOnlyList<Guid> GetRelatedPortfolioIds(
        IReadOnlyList<AccountSummaryDto> accounts,
        IReadOnlyList<OwnershipLinkDto> links,
        IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios)
    {
        if (accounts.Count == 0 || portfolios.Count == 0)
        {
            return [];
        }

        var portfolioById = portfolios.ToDictionary(static portfolio => portfolio.InvestmentPortfolioId);
        var accountIds = accounts.Select(static account => account.AccountId).ToHashSet();

        return accounts
            .Select(account => TryParseGuid(account.PortfolioId, out var portfolioId) ? portfolioId : Guid.Empty)
            .Where(static portfolioId => portfolioId != Guid.Empty)
            .Concat(links
                .Where(link => accountIds.Contains(link.ChildNodeId) && portfolioById.ContainsKey(link.ParentNodeId))
                .Select(static link => link.ParentNodeId))
            .Distinct()
            .ToList();
    }

    private static string ResolveCashFlowCurrency(
        string? requestedCurrency,
        ResolvedCashFlowScope scope)
    {
        if (!string.IsNullOrWhiteSpace(requestedCurrency))
        {
            return requestedCurrency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(scope.BaseCurrency))
        {
            return scope.BaseCurrency.Trim().ToUpperInvariant();
        }

        var accountCurrency = scope.Accounts
            .Select(static account => account.BaseCurrency)
            .FirstOrDefault(static currency => !string.IsNullOrWhiteSpace(currency));

        return string.IsNullOrWhiteSpace(accountCurrency)
            ? DefaultCashFlowCurrency
            : accountCurrency.Trim().ToUpperInvariant();
    }

    private async Task<AccountCashFlowWindow> BuildAccountCashFlowWindowAsync(
        AccountSummaryDto account,
        StructureScope scoped,
        string currency,
        DateTimeOffset historicalWindowStart,
        DateTimeOffset asOf,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        int forecastDays,
        int bucketDays,
        CancellationToken ct)
    {
        var historicalStartDate = DateOnly.FromDateTime(historicalWindowStart.UtcDateTime);
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        var projectionEndDate = DateOnly.FromDateTime(projectionWindowEnd.UtcDateTime);
        var balanceHistoryTask = _fundAccountService.GetBalanceHistoryAsync(
            account.AccountId,
            historicalStartDate,
            asOfDate,
            ct);
        var bankLinesTask = _fundAccountService.GetBankStatementLinesAsync(
            account.AccountId,
            historicalStartDate,
            projectionEndDate,
            ct);

        await Task.WhenAll(balanceHistoryTask, bankLinesTask).ConfigureAwait(false);

        var balanceHistory = (await balanceHistoryTask.ConfigureAwait(false))
            .Where(snapshot => MatchesCurrency(snapshot.Currency, currency))
            .OrderBy(snapshot => snapshot.AsOfDate)
            .ThenBy(snapshot => snapshot.RecordedAt)
            .ToList();
        var latestSnapshot = balanceHistory
            .OrderByDescending(static snapshot => snapshot.AsOfDate)
            .ThenByDescending(static snapshot => snapshot.RecordedAt)
            .FirstOrDefault();
        var bankLines = (await bankLinesTask.ConfigureAwait(false))
            .Where(line => MatchesCurrency(line.Currency, currency))
            .OrderBy(line => line.StatementDate)
            .ThenBy(line => line.ValueDate)
            .ToList();

        var historicalWindowEndExclusive = StartOfDayUtc(asOf).AddDays(1);
        var realizedBankLines = bankLines
            .Where(line =>
            {
                var eventDate = ToDateTimeOffset(line.ValueDate);
                return eventDate >= historicalWindowStart && eventDate < historicalWindowEndExclusive;
            })
            .ToList();
        var projectedBankLines = bankLines
            .Where(line =>
            {
                var eventDate = ToDateTimeOffset(line.ValueDate);
                return eventDate >= projectionWindowStart && eventDate < projectionWindowEnd;
            })
            .ToList();

        var realizedEntries = BuildRealizedEntries(
            account,
            realizedBankLines,
            balanceHistory,
            currency,
            historicalWindowStart,
            historicalWindowEndExclusive);
        var projectedEntryBuild = await BuildProjectedEntriesAsync(
            account,
            scoped,
            projectedBankLines,
            latestSnapshot,
            balanceHistory,
            currency,
            projectionWindowStart,
            projectionWindowEnd,
            forecastDays,
            bucketDays,
            ct).ConfigureAwait(false);
        var currentCashBalance = latestSnapshot?.CashBalance
            ?? GetLatestRunningBalance(bankLines, asOf, currency)
            ?? 0m;

        return new AccountCashFlowWindow(
            account,
            currentCashBalance,
            latestSnapshot?.AsOfDate,
            realizedEntries,
            projectedEntryBuild.Entries,
            realizedEntries.Sum(static entry => entry.Amount),
            projectedEntryBuild.Entries.Sum(static entry => entry.Amount),
            projectedEntryBuild.UsedTrendFallback,
            projectedEntryBuild.SecurityProjectedEntryCount,
            projectedEntryBuild.UsedSecurityMasterRules);
    }

    private static IReadOnlyList<GovernanceCashFlowEntryDto> BuildRealizedEntries(
        AccountSummaryDto account,
        IReadOnlyList<BankStatementLineDto> realizedBankLines,
        IReadOnlyList<AccountBalanceSnapshotDto> balanceHistory,
        string currency,
        DateTimeOffset windowStart,
        DateTimeOffset windowEndExclusive)
    {
        var bankEntries = realizedBankLines
            .Select(line => new GovernanceCashFlowEntryDto(
                EventDate: ToDateTimeOffset(line.ValueDate),
                Amount: line.Amount,
                Currency: currency,
                EventKind: NormalizeCashFlowEventKind(line.TransactionType, line.Amount),
                SourceKind: "BankStatement",
                AccountId: account.AccountId,
                AccountDisplayName: account.DisplayName,
                LedgerReference: account.LedgerReference,
                Description: line.Description,
                IsProjected: false))
            .OrderBy(static entry => entry.EventDate)
            .ToList();

        if (bankEntries.Count > 0)
        {
            return bankEntries;
        }

        return balanceHistory
            .Zip(balanceHistory.Skip(1), (previous, current) => new { previous, current })
            .Select(pair => new
            {
                pair.current,
                Delta = pair.current.CashBalance - pair.previous.CashBalance
            })
            .Where(item => item.Delta != 0m)
            .Select(item => new GovernanceCashFlowEntryDto(
                EventDate: ToDateTimeOffset(item.current.AsOfDate),
                Amount: item.Delta,
                Currency: currency,
                EventKind: "BalanceSnapshotDelta",
                SourceKind: "BalanceSnapshot",
                AccountId: account.AccountId,
                AccountDisplayName: account.DisplayName,
                LedgerReference: account.LedgerReference,
                Description: $"Cash balance delta captured on {item.current.AsOfDate:yyyy-MM-dd}.",
                IsProjected: false))
            .Where(entry => entry.EventDate >= windowStart && entry.EventDate < windowEndExclusive)
            .OrderBy(static entry => entry.EventDate)
            .ToList();
    }

    private async Task<ProjectedEntryBuildResult> BuildProjectedEntriesAsync(
        AccountSummaryDto account,
        StructureScope scoped,
        IReadOnlyList<BankStatementLineDto> projectedBankLines,
        AccountBalanceSnapshotDto? latestSnapshot,
        IReadOnlyList<AccountBalanceSnapshotDto> balanceHistory,
        string currency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        int forecastDays,
        int bucketDays,
        CancellationToken ct)
    {
        var entries = projectedBankLines
            .Select(line => new GovernanceCashFlowEntryDto(
                EventDate: ToDateTimeOffset(line.ValueDate),
                Amount: line.Amount,
                Currency: currency,
                EventKind: NormalizeCashFlowEventKind(line.TransactionType, line.Amount),
                SourceKind: "BankStatement",
                AccountId: account.AccountId,
                AccountDisplayName: account.DisplayName,
                LedgerReference: account.LedgerReference,
                Description: line.Description,
                IsProjected: true))
            .ToList();

        var securityRuleProjection = await BuildSecurityMasterProjectedEntriesAsync(
            account,
            scoped,
            latestSnapshot,
            currency,
            projectionWindowStart,
            projectionWindowEnd,
            entries,
            ct).ConfigureAwait(false);
        if (securityRuleProjection.Entries.Count > 0)
        {
            entries.AddRange(securityRuleProjection.Entries);
        }

        if (latestSnapshot is not null)
        {
            if (!securityRuleProjection.ConsumedPendingSettlement
                && latestSnapshot.PendingSettlement.HasValue
                && latestSnapshot.PendingSettlement.Value != 0m)
            {
                entries.Add(new GovernanceCashFlowEntryDto(
                    projectionWindowStart,
                    latestSnapshot.PendingSettlement.Value,
                    currency,
                    "PendingSettlement",
                    "BalanceSnapshot",
                    account.AccountId,
                    account.DisplayName,
                    account.LedgerReference,
                    "Pending settlement carried forward from the latest balance snapshot.",
                    IsProjected: true));
            }

            if (!securityRuleProjection.ConsumedAccruedInterest
                && latestSnapshot.AccruedInterest.HasValue
                && latestSnapshot.AccruedInterest.Value != 0m)
            {
                entries.Add(new GovernanceCashFlowEntryDto(
                    projectionWindowStart,
                    latestSnapshot.AccruedInterest.Value,
                    currency,
                    "Coupon",
                    "BalanceSnapshot",
                    account.AccountId,
                    account.DisplayName,
                    account.LedgerReference,
                    "Accrued interest carried forward from the latest balance snapshot.",
                    IsProjected: true));
            }
        }

        entries = entries
            .Where(entry => entry.EventDate >= projectionWindowStart && entry.EventDate < projectionWindowEnd)
            .OrderBy(static entry => entry.EventDate)
            .ToList();

        if (entries.Count > 0)
        {
            return new ProjectedEntryBuildResult(
                entries,
                UsedTrendFallback: false,
                SecurityProjectedEntryCount: entries.Count(static entry => entry.SecurityId.HasValue),
                UsedSecurityMasterRules: securityRuleProjection.Entries.Count > 0);
        }

        if (balanceHistory.Count < 2)
        {
            return new ProjectedEntryBuildResult(
                entries,
                UsedTrendFallback: false,
                SecurityProjectedEntryCount: 0,
                UsedSecurityMasterRules: false);
        }

        var firstSnapshot = balanceHistory.First();
        var lastSnapshot = balanceHistory.Last();
        var elapsedDays = Math.Max(1, lastSnapshot.AsOfDate.DayNumber - firstSnapshot.AsOfDate.DayNumber);
        var averageDailyChange = (lastSnapshot.CashBalance - firstSnapshot.CashBalance) / elapsedDays;
        if (averageDailyChange == 0m)
        {
            return new ProjectedEntryBuildResult(
                entries,
                UsedTrendFallback: false,
                SecurityProjectedEntryCount: 0,
                UsedSecurityMasterRules: false);
        }

        var bucketCount = Math.Max(1, (int)Math.Ceiling(forecastDays / (double)bucketDays));
        for (var bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
        {
            var bucketOffset = bucketIndex * bucketDays;
            var remainingDays = forecastDays - bucketOffset;
            if (remainingDays <= 0)
            {
                break;
            }

            var daysInBucket = Math.Min(bucketDays, remainingDays);
            entries.Add(new GovernanceCashFlowEntryDto(
                projectionWindowStart.AddDays(bucketOffset),
                averageDailyChange * daysInBucket,
                currency,
                "BalanceTrend",
                "BalanceTrend",
                account.AccountId,
                account.DisplayName,
                account.LedgerReference,
                $"Trend projection derived from {elapsedDays} day(s) of cash-balance history.",
                IsProjected: true));
        }

        var orderedEntries = entries.OrderBy(static entry => entry.EventDate).ToList();
        return new ProjectedEntryBuildResult(
            orderedEntries,
            UsedTrendFallback: true,
            SecurityProjectedEntryCount: 0,
            UsedSecurityMasterRules: false);
    }

    private async Task<SecurityMasterProjectionResult> BuildSecurityMasterProjectedEntriesAsync(
        AccountSummaryDto account,
        StructureScope scoped,
        AccountBalanceSnapshotDto? latestSnapshot,
        string currency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        IReadOnlyList<GovernanceCashFlowEntryDto> existingEntries,
        CancellationToken ct)
    {
        if (_securityMasterQueryService is null)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var assignments = ResolveSecurityMasterInstrumentAssignmentsForAccount(account, scoped);
        if (assignments.Count == 0)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var projections = await Task.WhenAll(assignments.Select(assignment =>
            ProjectSecurityMasterAssignmentAsync(
                assignment,
                account,
                latestSnapshot,
                currency,
                projectionWindowStart,
                projectionWindowEnd,
                existingEntries,
                ct))).ConfigureAwait(false);

        return new SecurityMasterProjectionResult(
            projections
                .SelectMany(static projection => projection.Entries)
                .OrderBy(static entry => entry.EventDate)
                .ThenBy(static entry => entry.EventKind)
                .ToList(),
            projections.Any(static projection => projection.ConsumedAccruedInterest),
            projections.Any(static projection => projection.ConsumedPendingSettlement));
    }

    private IReadOnlyList<SecurityMasterInstrumentAssignment> ResolveSecurityMasterInstrumentAssignmentsForAccount(
        AccountSummaryDto account,
        StructureScope scoped)
    {
        var nodeIds = ResolveSecurityAssignmentNodeIdsForAccount(account, scoped);
        if (nodeIds.Count == 0)
        {
            return [];
        }

        var priorityByNodeId = nodeIds
            .Select((nodeId, index) => new { nodeId, index })
            .ToDictionary(static item => item.nodeId, static item => item.index);

        return scoped.Assignments
            .Where(assignment => priorityByNodeId.ContainsKey(assignment.NodeId))
            .Where(assignment => IsSecurityInstrumentAssignment(assignment.AssignmentType))
            .Select(assignment => TryParseSecurityMasterInstrumentAssignment(assignment, priorityByNodeId[assignment.NodeId]))
            .Where(static assignment => assignment is not null)
            .Cast<SecurityMasterInstrumentAssignment>()
            .OrderByDescending(static assignment => assignment.IsPrimary)
            .ThenBy(static assignment => assignment.Priority)
            .ThenBy(static assignment => assignment.EffectiveFrom)
            .GroupBy(static assignment => assignment.SecurityId)
            .Select(static group => group.First())
            .ToList();
    }

    private static IReadOnlyList<Guid> ResolveSecurityAssignmentNodeIdsForAccount(
        AccountSummaryDto account,
        StructureScope scoped)
    {
        IReadOnlyList<Guid> nodeIds = [account.AccountId];

        if (TryParseGuid(account.PortfolioId, out var portfolioId))
        {
            nodeIds = AppendUnique(nodeIds, portfolioId);
            var portfolio = scoped.InvestmentPortfolios.FirstOrDefault(candidate => candidate.InvestmentPortfolioId == portfolioId);
            if (portfolio is not null)
            {
                if (portfolio.ClientId.HasValue)
                {
                    nodeIds = AppendUnique(nodeIds, portfolio.ClientId.Value);
                }

                nodeIds = AppendUnique(nodeIds, portfolio.BusinessId);
                var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == portfolio.BusinessId);
                if (business is not null)
                {
                    nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                }
            }
        }

        if (account.EntityId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.EntityId.Value);
        }

        if (account.VehicleId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.VehicleId.Value);
            var vehicle = scoped.Vehicles.FirstOrDefault(candidate => candidate.VehicleId == account.VehicleId.Value);
            if (vehicle is not null)
            {
                nodeIds = AppendUnique(nodeIds, vehicle.FundId);
                nodeIds = AppendUnique(nodeIds, vehicle.LegalEntityId);
                var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == vehicle.FundId);
                if (fund?.BusinessId is Guid businessId)
                {
                    nodeIds = AppendUnique(nodeIds, businessId);
                    var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == businessId);
                    if (business is not null)
                    {
                        nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                    }
                }
            }
        }

        if (account.SleeveId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.SleeveId.Value);
            var sleeve = scoped.Sleeves.FirstOrDefault(candidate => candidate.SleeveId == account.SleeveId.Value);
            if (sleeve is not null)
            {
                nodeIds = AppendUnique(nodeIds, sleeve.FundId);
                var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == sleeve.FundId);
                if (fund?.BusinessId is Guid businessId)
                {
                    nodeIds = AppendUnique(nodeIds, businessId);
                    var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == businessId);
                    if (business is not null)
                    {
                        nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                    }
                }
            }
        }

        if (account.FundId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.FundId.Value);
            var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == account.FundId.Value);
            if (fund?.BusinessId is Guid businessId)
            {
                nodeIds = AppendUnique(nodeIds, businessId);
                var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == businessId);
                if (business is not null)
                {
                    nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                }
            }
        }

        return nodeIds;
    }

    private static bool IsSecurityInstrumentAssignment(string assignmentType) =>
        AssignmentComparer.Equals(assignmentType, SecurityMasterInstrumentAssignmentType)
        || AssignmentComparer.Equals(assignmentType, SecurityInstrumentAssignmentType);

    private static SecurityMasterInstrumentAssignment? TryParseSecurityMasterInstrumentAssignment(
        FundStructureAssignmentDto assignment,
        int priority)
    {
        if (TryParseGuid(assignment.AssignmentReference, out var securityId))
        {
            return new SecurityMasterInstrumentAssignment(
                assignment.AssignmentId,
                assignment.NodeId,
                securityId,
                IncomeAmount: null,
                PrincipalAmount: null,
                Units: null,
                FirstProjectedDate: null,
                Currency: null,
                Notes: null,
                priority,
                assignment.IsPrimary,
                assignment.EffectiveFrom);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SecurityMasterInstrumentAssignmentPayload>(
                assignment.AssignmentReference,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null || payload.SecurityId == Guid.Empty)
            {
                return null;
            }

            var firstProjectedDate = payload.FirstProjectedDate ?? payload.NextPaymentDate;
            return new SecurityMasterInstrumentAssignment(
                assignment.AssignmentId,
                assignment.NodeId,
                payload.SecurityId,
                payload.IncomeAmount ?? payload.ExpectedCashAmount,
                payload.PrincipalAmount,
                payload.Units,
                firstProjectedDate,
                payload.Currency,
                payload.Notes,
                priority,
                assignment.IsPrimary,
                assignment.EffectiveFrom);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<SecurityMasterProjectionResult> ProjectSecurityMasterAssignmentAsync(
        SecurityMasterInstrumentAssignment assignment,
        AccountSummaryDto account,
        AccountBalanceSnapshotDto? latestSnapshot,
        string currency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        IReadOnlyList<GovernanceCashFlowEntryDto> existingEntries,
        CancellationToken ct)
    {
        if (_securityMasterQueryService is null)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var economicTask = _securityMasterQueryService.GetEconomicDefinitionByIdAsync(assignment.SecurityId, ct);
        var corporateActionsTask = assignment.Units.HasValue && assignment.Units.Value != 0m
            ? _securityMasterQueryService.GetCorporateActionsAsync(assignment.SecurityId, ct)
            : Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        await Task.WhenAll(economicTask, corporateActionsTask).ConfigureAwait(false);

        var economic = await economicTask.ConfigureAwait(false);
        if (economic is null)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var effectiveCurrency = ResolveSecurityMasterProjectionCurrency(assignment, economic, account);
        if (!MatchesCurrency(effectiveCurrency, currency))
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var allEntries = existingEntries.ToList();
        var projectedEntries = new List<GovernanceCashFlowEntryDto>();
        var corporateActions = await corporateActionsTask.ConfigureAwait(false);
        var hasDividendActionInWindow = false;

        if (assignment.Units.HasValue && assignment.Units.Value != 0m)
        {
            foreach (var action in corporateActions
                .Where(action => AssignmentComparer.Equals(action.EventType, "Dividend"))
                .Where(action => action.DividendPerShare.HasValue))
            {
                var eventDate = ToDateTimeOffset(action.PayDate ?? action.ExDate);
                if (eventDate < projectionWindowStart || eventDate >= projectionWindowEnd)
                {
                    continue;
                }

                var amount = assignment.Units.Value * action.DividendPerShare!.Value;
                if (amount == 0m)
                {
                    continue;
                }

                var entry = new GovernanceCashFlowEntryDto(
                    eventDate,
                    amount,
                    currency,
                    "Dividend",
                    SecurityMasterCorporateActionSourceKind,
                    account.AccountId,
                    account.DisplayName,
                    account.LedgerReference,
                    $"{economic.DisplayName} dividend projected from Security Master corporate action.",
                    IsProjected: true,
                    SecurityId: assignment.SecurityId,
                    SecurityDisplayName: economic.DisplayName,
                    SecurityTypeName: economic.TypeName);
                if (TryAddSecurityMasterProjectedEntry(allEntries, projectedEntries, entry))
                {
                    hasDividendActionInWindow = true;
                }
            }
        }

        var recurringEventKind = DetermineSecurityIncomeEventKind(economic);
        var consumedAccruedInterest = false;
        if (!string.IsNullOrWhiteSpace(recurringEventKind)
            && !(hasDividendActionInWindow && recurringEventKind.Equals("Dividend", StringComparison.OrdinalIgnoreCase)))
        {
            var recurringAmount = assignment.IncomeAmount;
            if (!recurringAmount.HasValue
                && recurringEventKind.Equals("Coupon", StringComparison.OrdinalIgnoreCase)
                && latestSnapshot?.AccruedInterest is decimal accruedInterest
                && accruedInterest != 0m)
            {
                recurringAmount = accruedInterest;
                consumedAccruedInterest = true;
            }

            if (recurringAmount.HasValue && recurringAmount.Value != 0m)
            {
                foreach (var dueDate in BuildProjectedScheduleDates(assignment, economic, projectionWindowStart, projectionWindowEnd))
                {
                    var entry = new GovernanceCashFlowEntryDto(
                        dueDate,
                        recurringAmount.Value,
                        currency,
                        recurringEventKind,
                        SecurityMasterRuleSourceKind,
                        account.AccountId,
                        account.DisplayName,
                        account.LedgerReference,
                        $"{economic.DisplayName} {recurringEventKind.ToLowerInvariant()} projected from Security Master terms.",
                        IsProjected: true,
                        SecurityId: assignment.SecurityId,
                        SecurityDisplayName: economic.DisplayName,
                        SecurityTypeName: economic.TypeName);
                    TryAddSecurityMasterProjectedEntry(allEntries, projectedEntries, entry);
                }
            }
        }

        var maturityDate = ReadDateOnly(ReadObject(economic.EconomicTerms, "maturity"), "maturityDate");
        var consumedPendingSettlement = false;
        if (maturityDate.HasValue)
        {
            var maturityEventDate = ToDateTimeOffset(maturityDate.Value);
            if (maturityEventDate >= projectionWindowStart && maturityEventDate < projectionWindowEnd)
            {
                var principalAmount = assignment.PrincipalAmount;
                if (!principalAmount.HasValue
                    && latestSnapshot?.PendingSettlement is decimal pendingSettlement
                    && pendingSettlement != 0m)
                {
                    principalAmount = pendingSettlement;
                    consumedPendingSettlement = true;
                }

                if (principalAmount.HasValue && principalAmount.Value != 0m)
                {
                    var entry = new GovernanceCashFlowEntryDto(
                        maturityEventDate,
                        principalAmount.Value,
                        currency,
                        "PrincipalMaturity",
                        SecurityMasterRuleSourceKind,
                        account.AccountId,
                        account.DisplayName,
                        account.LedgerReference,
                        $"{economic.DisplayName} principal maturity projected from Security Master terms.",
                        IsProjected: true,
                        SecurityId: assignment.SecurityId,
                        SecurityDisplayName: economic.DisplayName,
                        SecurityTypeName: economic.TypeName);
                    TryAddSecurityMasterProjectedEntry(allEntries, projectedEntries, entry);
                }
            }
        }

        return new SecurityMasterProjectionResult(
            projectedEntries,
            consumedAccruedInterest && projectedEntries.Any(entry => entry.EventKind.Equals("Coupon", StringComparison.OrdinalIgnoreCase)),
            consumedPendingSettlement && projectedEntries.Any(entry => entry.EventKind.Equals("PrincipalMaturity", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryAddSecurityMasterProjectedEntry(
        List<GovernanceCashFlowEntryDto> allEntries,
        List<GovernanceCashFlowEntryDto> projectedEntries,
        GovernanceCashFlowEntryDto candidate)
    {
        var exists = allEntries.Any(existing =>
            existing.AccountId == candidate.AccountId
            && existing.SecurityId == candidate.SecurityId
            && existing.EventDate == candidate.EventDate
            && existing.Amount == candidate.Amount
            && string.Equals(existing.EventKind, candidate.EventKind, StringComparison.OrdinalIgnoreCase)
            && MatchesCurrency(existing.Currency, candidate.Currency));

        if (exists)
        {
            return false;
        }

        allEntries.Add(candidate);
        projectedEntries.Add(candidate);
        return true;
    }

    private static IReadOnlyList<DateTimeOffset> BuildProjectedScheduleDates(
        SecurityMasterInstrumentAssignment assignment,
        SecurityEconomicDefinitionRecord economic,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd)
    {
        var payment = ReadObject(economic.EconomicTerms, "payment");
        var coupon = ReadObject(economic.EconomicTerms, "coupon");
        var accrual = ReadObject(economic.EconomicTerms, "accrual");
        var maturity = ReadObject(economic.EconomicTerms, "maturity");
        var frequency = ReadString(payment, "paymentFrequency")
            ?? ReadString(coupon, "paymentFrequency");
        var anchorDate = assignment.FirstProjectedDate
            ?? ReadDateOnly(accrual, "accrualStartDate")
            ?? ReadDateOnly(maturity, "issueDate")
            ?? ReadDateOnly(maturity, "effectiveDate");

        if (string.IsNullOrWhiteSpace(frequency))
        {
            if (!anchorDate.HasValue)
            {
                return [];
            }

            var anchored = ToDateTimeOffset(anchorDate.Value);
            return anchored >= projectionWindowStart && anchored < projectionWindowEnd
                ? [anchored]
                : [];
        }

        var schedule = new List<DateTimeOffset>();
        var nextDate = anchorDate ?? DateOnly.FromDateTime(projectionWindowStart.UtcDateTime);
        var guard = 0;
        while (guard < 256)
        {
            guard++;
            var eventDate = ToDateTimeOffset(nextDate);
            if (eventDate >= projectionWindowStart && eventDate < projectionWindowEnd)
            {
                schedule.Add(eventDate);
            }

            if (eventDate >= projectionWindowEnd)
            {
                break;
            }

            if (!TryAdvanceByFrequency(nextDate, frequency, out nextDate))
            {
                break;
            }
        }

        return schedule;
    }

    private static bool TryAdvanceByFrequency(DateOnly current, string? frequency, out DateOnly next)
    {
        next = current;
        if (string.IsNullOrWhiteSpace(frequency))
        {
            return false;
        }

        switch (frequency.Trim().ToLowerInvariant())
        {
            case "daily":
                next = current.AddDays(1);
                return true;
            case "weekly":
                next = current.AddDays(7);
                return true;
            case "biweekly":
            case "bi-weekly":
                next = current.AddDays(14);
                return true;
            case "monthly":
                next = current.AddMonths(1);
                return true;
            case "quarterly":
                next = current.AddMonths(3);
                return true;
            case "semiannual":
            case "semi-annual":
            case "semiannually":
            case "semi-annually":
            case "halfyearly":
            case "half-yearly":
                next = current.AddMonths(6);
                return true;
            case "annual":
            case "annually":
            case "yearly":
                next = current.AddYears(1);
                return true;
            default:
                return false;
        }
    }

    private static string? DetermineSecurityIncomeEventKind(SecurityEconomicDefinitionRecord economic)
    {
        var payment = ReadObject(economic.EconomicTerms, "payment");
        var coupon = ReadObject(economic.EconomicTerms, "coupon");
        if (ReadString(payment, "paymentFrequency") is not null
            || ReadString(coupon, "paymentFrequency") is not null
            || ReadDecimal(coupon, "couponRate").HasValue)
        {
            return "Coupon";
        }

        var equityBehavior = ReadObject(economic.EconomicTerms, "equityBehavior");
        if (ReadString(equityBehavior, "distributionType") is not null
            || string.Equals(economic.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(economic.AssetClass, "Fund", StringComparison.OrdinalIgnoreCase))
        {
            return "Dividend";
        }

        return null;
    }

    private static string ResolveSecurityMasterProjectionCurrency(
        SecurityMasterInstrumentAssignment assignment,
        SecurityEconomicDefinitionRecord economic,
        AccountSummaryDto account)
    {
        if (!string.IsNullOrWhiteSpace(assignment.Currency))
        {
            return assignment.Currency.Trim().ToUpperInvariant();
        }

        var payment = ReadObject(economic.EconomicTerms, "payment");
        var paymentCurrency = ReadString(payment, "paymentCurrency");
        if (!string.IsNullOrWhiteSpace(paymentCurrency))
        {
            return paymentCurrency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(economic.Currency))
        {
            return economic.Currency.Trim().ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(account.BaseCurrency)
            ? DefaultCashFlowCurrency
            : account.BaseCurrency.Trim().ToUpperInvariant();
    }

    private static JsonElement? ReadObject(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : null;

    private static string? ReadString(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static int? ReadInt32(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : null;

    private static decimal? ReadDecimal(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
                ? property.GetDecimal()
                : null;

    private static DateOnly? ReadDateOnly(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(property.GetString(), out var parsed)
                ? parsed
                : null;

    private static decimal? GetLatestRunningBalance(
        IReadOnlyList<BankStatementLineDto> bankLines,
        DateTimeOffset asOf,
        string currency) =>
        bankLines
            .Where(line => MatchesCurrency(line.Currency, currency))
            .Where(line => ToDateTimeOffset(line.ValueDate) <= asOf)
            .OrderByDescending(line => line.StatementDate)
            .ThenByDescending(line => line.ValueDate)
            .Select(line => line.RunningBalance)
            .FirstOrDefault(balance => balance.HasValue);

    private static GovernanceCashFlowLadderDto BuildCashFlowLadder(
        DateTimeOffset anchor,
        int windowDays,
        int bucketDays,
        string currency,
        IReadOnlyList<GovernanceCashFlowEntryDto> entries)
    {
        var effectiveBucketDays = Math.Max(1, bucketDays);
        var effectiveWindowDays = Math.Max(1, windowDays);
        var windowEnd = anchor.AddDays(effectiveWindowDays);
        var inputs = entries
            .Where(entry => MatchesCurrency(entry.Currency, currency))
            .Where(entry => entry.EventDate >= anchor && entry.EventDate < windowEnd)
            .Select((entry, index) => new CashFlowProjectionInput
            {
                FlowId = CreateSyntheticFlowId(entry.AccountId, entry.EventKind, entry.EventDate, index),
                SecurityGuid = entry.SecurityId ?? Guid.Empty,
                EventKindLabel = entry.EventKind,
                ExpectedAmount = entry.Amount,
                ExpectedCurrency = currency,
                DueDate = entry.EventDate,
                IsPrincipalFlow = IsPrincipalCashFlow(entry.EventKind),
                IsIncomeFlow = IsIncomeCashFlow(entry.EventKind),
                Notes = entry.Description ?? string.Empty
            })
            .ToArray();
        var ladder = CashFlowProjector.BuildLadder(anchor, currency, effectiveBucketDays, inputs);
        var bucketMap = ladder.Buckets.ToDictionary(bucket => bucket.BucketStart);
        var bucketCount = Math.Max(1, (int)Math.Ceiling(effectiveWindowDays / (double)effectiveBucketDays));
        var buckets = new List<GovernanceCashFlowBucketDto>(bucketCount);

        for (var bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
        {
            var bucketStart = anchor.AddDays(bucketIndex * effectiveBucketDays);
            var bucketEnd = bucketStart.AddDays(Math.Min(effectiveBucketDays, effectiveWindowDays - (bucketIndex * effectiveBucketDays)));
            if (!bucketMap.TryGetValue(bucketStart, out var bucket))
            {
                buckets.Add(new GovernanceCashFlowBucketDto(
                    bucketIndex,
                    bucketStart,
                    bucketEnd,
                    0m,
                    0m,
                    0m,
                    currency,
                    0));
                continue;
            }

            buckets.Add(new GovernanceCashFlowBucketDto(
                bucketIndex,
                bucketStart,
                bucketEnd,
                bucket.ProjectedInflows,
                bucket.ProjectedOutflows,
                bucket.NetFlow,
                currency,
                bucket.EventCount));
        }

        return new GovernanceCashFlowLadderDto(
            anchor,
            windowEnd,
            currency,
            effectiveBucketDays,
            buckets.Sum(static bucket => bucket.ProjectedInflows),
            buckets.Sum(static bucket => bucket.ProjectedOutflows),
            buckets.Sum(static bucket => bucket.NetFlow),
            buckets);
    }

    private static IReadOnlyList<GovernanceCashFlowVarianceBucketDto> BuildVarianceBuckets(
        GovernanceCashFlowLadderDto realizedLadder,
        GovernanceCashFlowLadderDto projectedLadder)
    {
        var maxBucketCount = Math.Max(realizedLadder.Buckets.Count, projectedLadder.Buckets.Count);
        var buckets = new List<GovernanceCashFlowVarianceBucketDto>(maxBucketCount);

        for (var index = 0; index < maxBucketCount; index++)
        {
            var realizedBucket = index < realizedLadder.Buckets.Count
                ? realizedLadder.Buckets[index]
                : CreateEmptyBucket(realizedLadder, index);
            var projectedBucket = index < projectedLadder.Buckets.Count
                ? projectedLadder.Buckets[index]
                : CreateEmptyBucket(projectedLadder, index);

            buckets.Add(new GovernanceCashFlowVarianceBucketDto(
                BucketIndex: index,
                RealizedBucketStart: realizedBucket.BucketStart,
                RealizedBucketEnd: realizedBucket.BucketEnd,
                ProjectedBucketStart: projectedBucket.BucketStart,
                ProjectedBucketEnd: projectedBucket.BucketEnd,
                RealizedInflows: realizedBucket.ProjectedInflows,
                RealizedOutflows: realizedBucket.ProjectedOutflows,
                RealizedNetFlow: realizedBucket.NetFlow,
                ProjectedInflows: projectedBucket.ProjectedInflows,
                ProjectedOutflows: projectedBucket.ProjectedOutflows,
                ProjectedNetFlow: projectedBucket.NetFlow,
                VarianceAmount: projectedBucket.NetFlow - realizedBucket.NetFlow,
                RealizedEventCount: realizedBucket.EventCount,
                ProjectedEventCount: projectedBucket.EventCount,
                Currency: projectedBucket.Currency));
        }

        return buckets;
    }

    private static GovernanceCashFlowBucketDto CreateEmptyBucket(
        GovernanceCashFlowLadderDto ladder,
        int bucketIndex)
    {
        var bucketStart = ladder.AsOf.AddDays(bucketIndex * ladder.BucketDays);
        var bucketEnd = bucketStart.AddDays(ladder.BucketDays);
        return new GovernanceCashFlowBucketDto(
            bucketIndex,
            bucketStart,
            bucketEnd,
            0m,
            0m,
            0m,
            ladder.Currency,
            0);
    }

    private static DateTimeOffset StartOfDayUtc(DateTimeOffset timestamp) =>
        new(timestamp.UtcDateTime.Date, TimeSpan.Zero);

    private static DateTimeOffset ToDateTimeOffset(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);

    private static bool MatchesCurrency(string? left, string right) =>
        string.Equals(left?.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCashFlowEventKind(string? transactionType, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(transactionType))
        {
            return amount >= 0m ? "Proceeds" : "Fee";
        }

        if (transactionType.Contains("dividend", StringComparison.OrdinalIgnoreCase))
        {
            return "Dividend";
        }

        if (transactionType.Contains("interest", StringComparison.OrdinalIgnoreCase))
        {
            return "Coupon";
        }

        if (transactionType.Contains("fee", StringComparison.OrdinalIgnoreCase))
        {
            return "Fee";
        }

        if (transactionType.Contains("principal", StringComparison.OrdinalIgnoreCase))
        {
            return "Principal";
        }

        return transactionType.Trim();
    }

    private static bool IsPrincipalCashFlow(string eventKind) =>
        eventKind.Contains("principal", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("maturity", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("redemption", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("settlement", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncomeCashFlow(string eventKind) =>
        eventKind.Contains("coupon", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("dividend", StringComparison.OrdinalIgnoreCase)
        || eventKind.Contains("interest", StringComparison.OrdinalIgnoreCase);

    private static Guid CreateSyntheticFlowId(
        Guid accountId,
        string eventKind,
        DateTimeOffset eventDate,
        int ordinal)
    {
        var payload = $"{accountId:D}|{eventKind}|{eventDate:O}|{ordinal}";
        return new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    private async Task<FundStructureSharedDataAccessDto?> GetSharedDataAccessAsync(CancellationToken ct)
        => _sharedDataAccessService is null
            ? null
            : await _sharedDataAccessService.GetSharedDataAccessAsync(ct).ConfigureAwait(false);

    private static IReadOnlyList<AccountSummaryDto> AttachSharedDataAccess(
        IReadOnlyList<AccountSummaryDto> accounts,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        if (sharedDataAccess is null || accounts.Count == 0)
        {
            return accounts;
        }

        return accounts
            .Select(account => account with { SharedDataAccess = sharedDataAccess })
            .ToList();
    }

    private static IReadOnlyList<InvestmentPortfolioSummaryDto> AttachSharedDataAccess(
        IReadOnlyList<InvestmentPortfolioSummaryDto> portfolios,
        FundStructureSharedDataAccessDto? sharedDataAccess)
    {
        if (sharedDataAccess is null || portfolios.Count == 0)
        {
            return portfolios;
        }

        return portfolios
            .Select(portfolio => portfolio with { SharedDataAccess = sharedDataAccess })
            .ToList();
    }

    private StructureSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            return new StructureSnapshot(
                _organizations.Values.ToList(),
                _businesses.Values.ToList(),
                _clients.Values.ToList(),
                _funds.Values.ToList(),
                _sleeves.Values.ToList(),
                _vehicles.Values.ToList(),
                _entities.Values.ToList(),
                _investmentPortfolios.Values.ToList(),
                _ownershipLinks.Values.ToList(),
                _assignments.Values.ToList());
        }
    }

    private (long Version, string Json)? CaptureSnapshotLocked()
    {
        if (_persistencePath is null)
        {
            return null;
        }

        _stateVersion++;
        var json = JsonSerializer.Serialize(
            new PersistedState(
                Version: 1,
                Organizations: _organizations.Values.ToList(),
                Businesses: _businesses.Values.ToList(),
                Clients: _clients.Values.ToList(),
                Funds: _funds.Values.ToList(),
                Sleeves: _sleeves.Values.ToList(),
                Vehicles: _vehicles.Values.ToList(),
                Entities: _entities.Values.ToList(),
                InvestmentPortfolios: _investmentPortfolios.Values.ToList(),
                OwnershipLinks: _ownershipLinks.Values.ToList(),
                Assignments: _assignments.Values.ToList(),
                LinkedAccountIds: _linkedAccountIds.ToList()),
            JsonOptions);

        return (_stateVersion, json);
    }

    private async Task PersistSnapshotAsync((long Version, string Json)? snapshot, CancellationToken ct)
    {
        if (snapshot is null || _persistencePath is null)
        {
            return;
        }

        await _persistGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (snapshot.Value.Version <= _persistedVersion)
            {
                return;
            }

            await AtomicFileWriter.WriteAsync(_persistencePath, snapshot.Value.Json, ct).ConfigureAwait(false);
            _persistedVersion = snapshot.Value.Version;
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private void LoadState()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistencePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json, JsonOptions);
            if (state is null)
            {
                return;
            }

            foreach (var organization in state.Organizations)
            {
                _organizations[organization.OrganizationId] = organization;
            }

            foreach (var business in state.Businesses)
            {
                _businesses[business.BusinessId] = business;
            }

            foreach (var client in state.Clients)
            {
                _clients[client.ClientId] = client;
            }

            foreach (var fund in state.Funds)
            {
                _funds[fund.FundId] = fund;
            }

            foreach (var sleeve in state.Sleeves)
            {
                _sleeves[sleeve.SleeveId] = sleeve;
            }

            foreach (var vehicle in state.Vehicles)
            {
                _vehicles[vehicle.VehicleId] = vehicle;
            }

            foreach (var entity in state.Entities)
            {
                _entities[entity.EntityId] = entity;
            }

            foreach (var portfolio in state.InvestmentPortfolios)
            {
                _investmentPortfolios[portfolio.InvestmentPortfolioId] = portfolio;
            }

            foreach (var link in state.OwnershipLinks)
            {
                _ownershipLinks[link.OwnershipLinkId] = link;
            }

            foreach (var assignment in state.Assignments)
            {
                _assignments[assignment.AssignmentId] = assignment;
            }

            foreach (var linkedAccountId in state.LinkedAccountIds)
            {
                _linkedAccountIds.Add(linkedAccountId);
            }

            _stateVersion = 1;
            _persistedVersion = 1;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Preserve startup availability for malformed or missing local snapshots.
        }
    }

    private async Task<IReadOnlyList<AccountSummaryDto>> GetVisibleAccountsAsync(
        bool activeOnly,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var accounts = await _fundAccountService
            .QueryAccountsAsync(new AccountStructureQuery(ActiveOnly: false), ct)
            .ConfigureAwait(false);

        return accounts
            .Where(account => IsVisible(account.IsActive, account.EffectiveFrom, account.EffectiveTo, activeOnly, asOf))
            .ToList();
    }

    private static StructureScope FilterForOrganizationScope(
        StructureSnapshot snapshot,
        IReadOnlyList<AccountSummaryDto> accounts,
        Guid? organizationId,
        Guid? businessId,
        bool activeOnly,
        DateTimeOffset asOf)
    {
        var organizations = FilterVisible(snapshot.Organizations, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var businesses = FilterVisible(snapshot.Businesses, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var clients = FilterVisible(snapshot.Clients, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var funds = FilterVisible(snapshot.Funds, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var sleeves = FilterVisible(snapshot.Sleeves, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var vehicles = FilterVisible(snapshot.Vehicles, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var entities = FilterVisible(snapshot.Entities, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var portfolios = FilterVisible(snapshot.InvestmentPortfolios, activeOnly, asOf, static candidate => (candidate.EffectiveFrom, candidate.EffectiveTo));
        var activeLinks = FilterVisible(snapshot.OwnershipLinks, activeOnly, asOf, static link => (link.EffectiveFrom, link.EffectiveTo));
        var activeAssignments = FilterVisible(snapshot.Assignments, activeOnly, asOf, static assignment => (assignment.EffectiveFrom, assignment.EffectiveTo));

        if (businessId.HasValue)
        {
            businesses = businesses.Where(candidate => candidate.BusinessId == businessId.Value).ToList();
        }

        if (organizationId.HasValue)
        {
            organizations = organizations.Where(candidate => candidate.OrganizationId == organizationId.Value).ToList();
        }

        var organizationIds = organizations.Select(static organization => organization.OrganizationId).ToHashSet();
        businesses = businesses
            .Where(candidate => organizationIds.Count == 0 || organizationIds.Contains(candidate.OrganizationId))
            .ToList();
        var businessIds = businesses.Select(static business => business.BusinessId).ToHashSet();

        clients = clients.Where(candidate => businessIds.Contains(candidate.BusinessId)).ToList();
        funds = funds.Where(candidate => candidate.BusinessId.HasValue && businessIds.Contains(candidate.BusinessId.Value)).ToList();
        var fundIds = funds.Select(static fund => fund.FundId).ToHashSet();
        sleeves = sleeves.Where(candidate => fundIds.Contains(candidate.FundId)).ToList();
        var sleeveIds = sleeves.Select(static sleeve => sleeve.SleeveId).ToHashSet();
        vehicles = vehicles.Where(candidate => fundIds.Contains(candidate.FundId)).ToList();
        var vehicleIds = vehicles.Select(static vehicle => vehicle.VehicleId).ToHashSet();
        portfolios = portfolios.Where(candidate => businessIds.Contains(candidate.BusinessId)).ToList();
        var portfolioIds = portfolios.Select(static portfolio => portfolio.InvestmentPortfolioId).ToHashSet();
        var entityIds = vehicles.Select(static vehicle => vehicle.LegalEntityId)
            .Concat(funds.SelectMany(static fund => fund.EntityIds))
            .Concat(portfolios.Where(static portfolio => portfolio.EntityId.HasValue).Select(static portfolio => portfolio.EntityId!.Value))
            .Concat(accounts.Where(static account => account.EntityId.HasValue).Select(static account => account.EntityId!.Value))
            .ToHashSet();
        entities = entities
            .Where(candidate =>
                entityIds.Contains(candidate.EntityId)
                || activeLinks.Any(link => link.ChildNodeId == candidate.EntityId
                    && (businessIds.Contains(link.ParentNodeId)
                        || fundIds.Contains(link.ParentNodeId)
                        || vehicleIds.Contains(link.ParentNodeId)
                        || portfolioIds.Contains(link.ParentNodeId))))
            .ToList();

        var scopedAccounts = GetScopeAccounts(
            accounts,
            activeLinks,
            entities.Select(static entity => entity.EntityId).ToHashSet(),
            fundIds,
            sleeveIds,
            vehicleIds,
            portfolioIds)
            .ToList();
        var accountIds = scopedAccounts.Select(static account => account.AccountId).ToHashSet();
        var nodeIds = organizationIds
            .Concat(businessIds)
            .Concat(clients.Select(static client => client.ClientId))
            .Concat(fundIds)
            .Concat(sleeveIds)
            .Concat(vehicleIds)
            .Concat(entities.Select(static entity => entity.EntityId))
            .Concat(portfolioIds)
            .Concat(accountIds)
            .ToHashSet();
        activeLinks = activeLinks
            .Where(link => nodeIds.Contains(link.ParentNodeId) && nodeIds.Contains(link.ChildNodeId))
            .ToList();
        activeAssignments = activeAssignments
            .Where(assignment => nodeIds.Contains(assignment.NodeId))
            .ToList();

        return new StructureScope(
            organizations,
            businesses,
            clients,
            funds,
            sleeves,
            vehicles,
            entities,
            portfolios,
            scopedAccounts,
            activeLinks,
            activeAssignments);
    }

    private static IReadOnlyList<FundStructureNodeDto> BuildNodes(StructureScope scope)
    {
        return scope.Organizations
            .Select(static organization => new FundStructureNodeDto(
                organization.OrganizationId,
                FundStructureNodeKindDto.Organization,
                organization.Code,
                organization.Name,
                organization.Description,
                organization.IsActive,
                organization.EffectiveFrom,
                organization.EffectiveTo))
            .Concat(scope.Businesses.Select(static business => new FundStructureNodeDto(
                business.BusinessId,
                FundStructureNodeKindDto.Business,
                business.Code,
                business.Name,
                business.Description,
                business.IsActive,
                business.EffectiveFrom,
                business.EffectiveTo)))
            .Concat(scope.Clients.Select(static client => new FundStructureNodeDto(
                client.ClientId,
                FundStructureNodeKindDto.Client,
                client.Code,
                client.Name,
                client.Description,
                client.IsActive,
                client.EffectiveFrom,
                client.EffectiveTo)))
            .Concat(scope.Funds.Select(static fund => new FundStructureNodeDto(
                fund.FundId,
                FundStructureNodeKindDto.Fund,
                fund.Code,
                fund.Name,
                fund.Description,
                fund.IsActive,
                fund.EffectiveFrom,
                fund.EffectiveTo)))
            .Concat(scope.Sleeves.Select(static sleeve => new FundStructureNodeDto(
                sleeve.SleeveId,
                FundStructureNodeKindDto.Sleeve,
                sleeve.Code,
                sleeve.Name,
                sleeve.Mandate,
                sleeve.IsActive,
                sleeve.EffectiveFrom,
                sleeve.EffectiveTo)))
            .Concat(scope.Vehicles.Select(static vehicle => new FundStructureNodeDto(
                vehicle.VehicleId,
                FundStructureNodeKindDto.Vehicle,
                vehicle.Code,
                vehicle.Name,
                vehicle.Description,
                vehicle.IsActive,
                vehicle.EffectiveFrom,
                vehicle.EffectiveTo)))
            .Concat(scope.Entities.Select(static entity => new FundStructureNodeDto(
                entity.EntityId,
                FundStructureNodeKindDto.Entity,
                entity.Code,
                entity.Name,
                entity.Description,
                entity.IsActive,
                entity.EffectiveFrom,
                entity.EffectiveTo)))
            .Concat(scope.InvestmentPortfolios.Select(static portfolio => new FundStructureNodeDto(
                portfolio.InvestmentPortfolioId,
                FundStructureNodeKindDto.InvestmentPortfolio,
                portfolio.Code,
                portfolio.Name,
                portfolio.Description,
                portfolio.IsActive,
                portfolio.EffectiveFrom,
                portfolio.EffectiveTo)))
            .Concat(scope.Accounts.Select(static account => new FundStructureNodeDto(
                account.AccountId,
                FundStructureNodeKindDto.Account,
                account.AccountCode,
                account.DisplayName,
                account.Institution,
                account.IsActive,
                account.EffectiveFrom,
                account.EffectiveTo)))
            .ToList();
    }

    private static IReadOnlyList<T> FilterVisible<T>(
        IReadOnlyList<T> source,
        bool activeOnly,
        DateTimeOffset asOf,
        Func<T, (DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo)> selector)
        where T : notnull
    {
        return source
            .Where(item =>
            {
                var window = selector(item);
                return IsVisible(isActive: true, window.EffectiveFrom, window.EffectiveTo, activeOnly, asOf);
            })
            .ToList();
    }

    private static bool IsVisible(
        bool isActive,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        bool activeOnly,
        DateTimeOffset asOf)
    {
        if (activeOnly && !isActive)
        {
            return false;
        }

        if (effectiveFrom > asOf)
        {
            return false;
        }

        if (effectiveTo.HasValue && effectiveTo.Value < asOf)
        {
            return false;
        }

        return true;
    }

    private static bool IsAdvisoryPortfolio(InvestmentPortfolioSummaryDto portfolio) =>
        portfolio.ClientId.HasValue || (!portfolio.FundId.HasValue && !portfolio.SleeveId.HasValue && !portfolio.VehicleId.HasValue);

    private static bool IsFundPortfolio(InvestmentPortfolioSummaryDto portfolio) =>
        portfolio.FundId.HasValue || portfolio.SleeveId.HasValue || portfolio.VehicleId.HasValue;

    private static IEnumerable<AccountSummaryDto> GetScopeAccounts(
        IEnumerable<AccountSummaryDto> accounts,
        IReadOnlyList<OwnershipLinkDto> links,
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyCollection<Guid> fundIds,
        IReadOnlyCollection<Guid> sleeveIds,
        IReadOnlyCollection<Guid> vehicleIds,
        IReadOnlyCollection<Guid> portfolioIds)
    {
        var entitySet = entityIds as ISet<Guid> ?? entityIds.ToHashSet();
        var fundSet = fundIds as ISet<Guid> ?? fundIds.ToHashSet();
        var sleeveSet = sleeveIds as ISet<Guid> ?? sleeveIds.ToHashSet();
        var vehicleSet = vehicleIds as ISet<Guid> ?? vehicleIds.ToHashSet();
        var portfolioSet = portfolioIds as ISet<Guid> ?? portfolioIds.ToHashSet();
        var scopeNodeIds = entitySet
            .Concat(fundSet)
            .Concat(sleeveSet)
            .Concat(vehicleSet)
            .Concat(portfolioSet)
            .ToHashSet();

        return accounts
            .Where(account =>
                (account.EntityId.HasValue && entitySet.Contains(account.EntityId.Value))
                || (account.FundId.HasValue && fundSet.Contains(account.FundId.Value))
                || (account.SleeveId.HasValue && sleeveSet.Contains(account.SleeveId.Value))
                || (account.VehicleId.HasValue && vehicleSet.Contains(account.VehicleId.Value))
                || (TryParseGuid(account.PortfolioId, out var portfolioId) && portfolioSet.Contains(portfolioId))
                || links.Any(link => link.ChildNodeId == account.AccountId && scopeNodeIds.Contains(link.ParentNodeId)))
            .DistinctBy(static account => account.AccountId);
    }

    private static bool IsAccountLinkedToAny(
        Guid accountId,
        IReadOnlyCollection<Guid> parentIds,
        IReadOnlyList<OwnershipLinkDto> links) =>
        links.Any(link => link.ChildNodeId == accountId && parentIds.Contains(link.ParentNodeId));

    private static bool IsEntityLinkedToScope(
        Guid entityId,
        IReadOnlyCollection<Guid> parentIds,
        IReadOnlyList<OwnershipLinkDto> links) =>
        links.Any(link => link.ChildNodeId == entityId && parentIds.Contains(link.ParentNodeId));

    private static bool TryParseGuid(string? value, out Guid guid) =>
        Guid.TryParse(value, out guid);

    private static void EnsureSingleOperatingParent(CreateInvestmentPortfolioRequest request)
    {
        var populated = new[]
        {
            request.ClientId.HasValue,
            request.FundId.HasValue,
            request.SleeveId.HasValue,
            request.VehicleId.HasValue
        }.Count(static hasValue => hasValue);

        if (populated > 1)
        {
            throw new InvalidOperationException(
                "Investment portfolios can only have one operating parent (client, fund, sleeve, or vehicle).");
        }
    }

    private async Task<FundStructureNodeKindDto?> ResolveNodeKindAsync(Guid nodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (TryGetStoredNodeKindLocked(nodeId, out var kind))
            {
                return kind;
            }
        }

        var account = await _fundAccountService.GetAccountAsync(nodeId, ct).ConfigureAwait(false);
        return account is not null
            ? FundStructureNodeKindDto.Account
            : null;
    }

    private bool TryGetStoredNodeKindLocked(Guid nodeId, out FundStructureNodeKindDto kind)
    {
        if (_organizations.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.Organization;
            return true;
        }

        if (_businesses.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.Business;
            return true;
        }

        if (_clients.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.Client;
            return true;
        }

        if (_funds.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.Fund;
            return true;
        }

        if (_sleeves.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.Sleeve;
            return true;
        }

        if (_vehicles.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.Vehicle;
            return true;
        }

        if (_investmentPortfolios.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.InvestmentPortfolio;
            return true;
        }

        if (_entities.ContainsKey(nodeId))
        {
            kind = FundStructureNodeKindDto.Entity;
            return true;
        }

        if (_linkedAccountIds.Contains(nodeId))
        {
            kind = FundStructureNodeKindDto.Account;
            return true;
        }

        kind = default;
        return false;
    }

    private void EnsureUniqueNodeLocked(Guid nodeId)
    {
        if (TryGetStoredNodeKindLocked(nodeId, out _))
        {
            throw new InvalidOperationException($"Node {nodeId} already exists.");
        }
    }

    private void EnsureOrganizationExistsLocked(Guid organizationId)
    {
        if (!_organizations.ContainsKey(organizationId))
        {
            throw new InvalidOperationException($"Organization {organizationId} was not found.");
        }
    }

    private void EnsureBusinessExistsLocked(Guid businessId)
    {
        if (!_businesses.ContainsKey(businessId))
        {
            throw new InvalidOperationException($"Business {businessId} was not found.");
        }
    }

    private void EnsureClientExistsLocked(Guid clientId)
    {
        if (!_clients.ContainsKey(clientId))
        {
            throw new InvalidOperationException($"Client {clientId} was not found.");
        }
    }

    private void EnsureFundExistsLocked(Guid fundId)
    {
        if (!_funds.ContainsKey(fundId))
        {
            throw new InvalidOperationException($"Fund {fundId} was not found.");
        }
    }

    private void EnsureSleeveExistsLocked(Guid sleeveId)
    {
        if (!_sleeves.ContainsKey(sleeveId))
        {
            throw new InvalidOperationException($"Sleeve {sleeveId} was not found.");
        }
    }

    private void EnsureVehicleExistsLocked(Guid vehicleId)
    {
        if (!_vehicles.ContainsKey(vehicleId))
        {
            throw new InvalidOperationException($"Vehicle {vehicleId} was not found.");
        }
    }

    private void EnsureEntityExistsLocked(Guid entityId)
    {
        if (!_entities.ContainsKey(entityId))
        {
            throw new InvalidOperationException($"Entity {entityId} was not found.");
        }
    }

    private OwnershipLinkDto CreateAutoLinkLocked(
        Guid parentNodeId,
        Guid childNodeId,
        OwnershipRelationshipTypeDto relationshipType,
        DateTimeOffset effectiveFrom,
        string? notes)
    {
        var link = new OwnershipLinkDto(
            Guid.NewGuid(),
            parentNodeId,
            childNodeId,
            relationshipType,
            OwnershipPercent: null,
            IsPrimary: true,
            effectiveFrom,
            EffectiveTo: null,
            notes);
        _ownershipLinks[link.OwnershipLinkId] = link;
        ApplyOwnershipLinkLocked(link);
        return link;
    }

    private void ApplyOwnershipLinkLocked(OwnershipLinkDto link)
    {
        if (_organizations.TryGetValue(link.ParentNodeId, out var organization)
            && _businesses.TryGetValue(link.ChildNodeId, out var business))
        {
            _organizations[organization.OrganizationId] = organization with
            {
                BusinessIds = AppendUnique(organization.BusinessIds, business.BusinessId)
            };
            _businesses[business.BusinessId] = business with { OrganizationId = organization.OrganizationId };
            return;
        }

        if (_businesses.TryGetValue(link.ParentNodeId, out business))
        {
            if (_clients.TryGetValue(link.ChildNodeId, out var client))
            {
                _businesses[business.BusinessId] = business with
                {
                    ClientIds = AppendUnique(business.ClientIds, client.ClientId)
                };
                _clients[client.ClientId] = client with { BusinessId = business.BusinessId };
                return;
            }

            if (_funds.TryGetValue(link.ChildNodeId, out var fund))
            {
                _businesses[business.BusinessId] = business with
                {
                    FundIds = AppendUnique(business.FundIds, fund.FundId)
                };
                _funds[fund.FundId] = fund with { BusinessId = business.BusinessId };
                return;
            }

            if (_investmentPortfolios.TryGetValue(link.ChildNodeId, out var businessPortfolio))
            {
                _businesses[business.BusinessId] = business with
                {
                    InvestmentPortfolioIds = AppendUnique(
                        business.InvestmentPortfolioIds,
                        businessPortfolio.InvestmentPortfolioId)
                };
                _investmentPortfolios[businessPortfolio.InvestmentPortfolioId] = businessPortfolio with
                {
                    BusinessId = business.BusinessId
                };
                return;
            }
        }

        if (_clients.TryGetValue(link.ParentNodeId, out var parentClient)
            && _investmentPortfolios.TryGetValue(link.ChildNodeId, out var clientPortfolio))
        {
            _clients[parentClient.ClientId] = parentClient with
            {
                InvestmentPortfolioIds = AppendUnique(
                    parentClient.InvestmentPortfolioIds,
                    clientPortfolio.InvestmentPortfolioId)
            };
            _investmentPortfolios[clientPortfolio.InvestmentPortfolioId] =
                ApplyOperatingParent(clientPortfolio, clientId: parentClient.ClientId);
            return;
        }

        if (_funds.TryGetValue(link.ParentNodeId, out var parentFund))
        {
            if (_sleeves.TryGetValue(link.ChildNodeId, out var sleeve))
            {
                _funds[parentFund.FundId] = parentFund with
                {
                    SleeveIds = AppendUnique(parentFund.SleeveIds, sleeve.SleeveId)
                };
                _sleeves[sleeve.SleeveId] = sleeve with { FundId = parentFund.FundId };
                return;
            }

            if (_vehicles.TryGetValue(link.ChildNodeId, out var vehicle))
            {
                _funds[parentFund.FundId] = parentFund with
                {
                    VehicleIds = AppendUnique(parentFund.VehicleIds, vehicle.VehicleId)
                };
                _vehicles[vehicle.VehicleId] = vehicle with { FundId = parentFund.FundId };
                return;
            }

            if (_entities.TryGetValue(link.ChildNodeId, out var entity))
            {
                _funds[parentFund.FundId] = parentFund with
                {
                    EntityIds = AppendUnique(parentFund.EntityIds, entity.EntityId)
                };
                return;
            }

            if (_investmentPortfolios.TryGetValue(link.ChildNodeId, out var fundPortfolio))
            {
                _funds[parentFund.FundId] = parentFund with
                {
                    InvestmentPortfolioIds = AppendUnique(
                        parentFund.InvestmentPortfolioIds,
                        fundPortfolio.InvestmentPortfolioId)
                };
                _investmentPortfolios[fundPortfolio.InvestmentPortfolioId] =
                    ApplyOperatingParent(fundPortfolio, fundId: parentFund.FundId);
                return;
            }

            if (_linkedAccountIds.Contains(link.ChildNodeId))
            {
                _funds[parentFund.FundId] = parentFund with
                {
                    AccountIds = AppendUnique(parentFund.AccountIds, link.ChildNodeId)
                };
                return;
            }
        }

        if (_sleeves.TryGetValue(link.ParentNodeId, out var parentSleeve))
        {
            if (_investmentPortfolios.TryGetValue(link.ChildNodeId, out var sleevePortfolio))
            {
                _sleeves[parentSleeve.SleeveId] = parentSleeve with
                {
                    InvestmentPortfolioIds = AppendUnique(
                        parentSleeve.InvestmentPortfolioIds,
                        sleevePortfolio.InvestmentPortfolioId)
                };
                _investmentPortfolios[sleevePortfolio.InvestmentPortfolioId] =
                    ApplyOperatingParent(sleevePortfolio, sleeveId: parentSleeve.SleeveId);
                return;
            }

            if (_linkedAccountIds.Contains(link.ChildNodeId))
            {
                _sleeves[parentSleeve.SleeveId] = parentSleeve with
                {
                    AccountIds = AppendUnique(parentSleeve.AccountIds, link.ChildNodeId)
                };
                return;
            }
        }

        if (_vehicles.TryGetValue(link.ParentNodeId, out var parentVehicle))
        {
            if (_investmentPortfolios.TryGetValue(link.ChildNodeId, out var vehiclePortfolio))
            {
                _vehicles[parentVehicle.VehicleId] = parentVehicle with
                {
                    InvestmentPortfolioIds = AppendUnique(
                        parentVehicle.InvestmentPortfolioIds,
                        vehiclePortfolio.InvestmentPortfolioId)
                };
                _investmentPortfolios[vehiclePortfolio.InvestmentPortfolioId] =
                    ApplyOperatingParent(vehiclePortfolio, vehicleId: parentVehicle.VehicleId);
                return;
            }

            if (_linkedAccountIds.Contains(link.ChildNodeId))
            {
                _vehicles[parentVehicle.VehicleId] = parentVehicle with
                {
                    AccountIds = AppendUnique(parentVehicle.AccountIds, link.ChildNodeId)
                };
                return;
            }
        }

        if (_entities.TryGetValue(link.ParentNodeId, out var parentEntity))
        {
            if (_vehicles.TryGetValue(link.ChildNodeId, out var linkedVehicle))
            {
                _vehicles[linkedVehicle.VehicleId] = linkedVehicle with { LegalEntityId = parentEntity.EntityId };
                return;
            }

            if (_investmentPortfolios.TryGetValue(link.ChildNodeId, out var entityPortfolio))
            {
                _investmentPortfolios[entityPortfolio.InvestmentPortfolioId] = entityPortfolio with
                {
                    EntityId = parentEntity.EntityId
                };
                return;
            }
        }

        if (_investmentPortfolios.TryGetValue(link.ParentNodeId, out var parentPortfolio)
            && _linkedAccountIds.Contains(link.ChildNodeId))
        {
            _investmentPortfolios[parentPortfolio.InvestmentPortfolioId] = parentPortfolio with
            {
                AccountIds = AppendUnique(parentPortfolio.AccountIds, link.ChildNodeId)
            };
        }
    }

    private static InvestmentPortfolioSummaryDto ApplyOperatingParent(
        InvestmentPortfolioSummaryDto portfolio,
        Guid? clientId = null,
        Guid? fundId = null,
        Guid? sleeveId = null,
        Guid? vehicleId = null)
    {
        var populated = new[] { clientId.HasValue, fundId.HasValue, sleeveId.HasValue, vehicleId.HasValue }
            .Count(static hasValue => hasValue);
        if (populated > 1)
        {
            throw new InvalidOperationException(
                "A portfolio cannot be linked to more than one operating parent.");
        }

        if (clientId.HasValue)
        {
            return portfolio with
            {
                ClientId = clientId,
                FundId = null,
                SleeveId = null,
                VehicleId = null
            };
        }

        if (fundId.HasValue)
        {
            return portfolio with
            {
                ClientId = null,
                FundId = fundId,
                SleeveId = null,
                VehicleId = null
            };
        }

        if (sleeveId.HasValue)
        {
            return portfolio with
            {
                ClientId = null,
                FundId = null,
                SleeveId = sleeveId,
                VehicleId = null
            };
        }

        if (vehicleId.HasValue)
        {
            return portfolio with
            {
                ClientId = null,
                FundId = null,
                SleeveId = null,
                VehicleId = vehicleId
            };
        }

        return portfolio;
    }

    private static IReadOnlyList<Guid> AppendUnique(IReadOnlyList<Guid> source, Guid value)
    {
        if (source.Contains(value))
        {
            return source;
        }

        var updated = source.ToList();
        updated.Add(value);
        return updated;
    }

    private sealed record ResolvedCashFlowScope(
        GovernanceCashFlowScopeDto Scope,
        IReadOnlyList<AccountSummaryDto> Accounts,
        string BaseCurrency);

    private sealed record AccountCashFlowWindow(
        AccountSummaryDto Account,
        decimal CurrentCashBalance,
        DateOnly? LatestSnapshotDate,
        IReadOnlyList<GovernanceCashFlowEntryDto> RealizedEntries,
        IReadOnlyList<GovernanceCashFlowEntryDto> ProjectedEntries,
        decimal RealizedNetFlow,
        decimal ProjectedNetFlow,
        bool UsedTrendFallback,
        int SecurityProjectedEntryCount,
        bool UsedSecurityMasterRules);

    private sealed record ProjectedEntryBuildResult(
        IReadOnlyList<GovernanceCashFlowEntryDto> Entries,
        bool UsedTrendFallback,
        int SecurityProjectedEntryCount,
        bool UsedSecurityMasterRules);

    private sealed record SecurityMasterProjectionResult(
        IReadOnlyList<GovernanceCashFlowEntryDto> Entries,
        bool ConsumedAccruedInterest,
        bool ConsumedPendingSettlement)
    {
        public static SecurityMasterProjectionResult Empty { get; } = new([], false, false);
    }

    private sealed record SecurityMasterInstrumentAssignment(
        Guid AssignmentId,
        Guid NodeId,
        Guid SecurityId,
        decimal? IncomeAmount,
        decimal? PrincipalAmount,
        decimal? Units,
        DateOnly? FirstProjectedDate,
        string? Currency,
        string? Notes,
        int Priority,
        bool IsPrimary,
        DateTimeOffset EffectiveFrom);

    private sealed record SecurityMasterInstrumentAssignmentPayload(
        Guid SecurityId,
        decimal? IncomeAmount = null,
        decimal? ExpectedCashAmount = null,
        decimal? PrincipalAmount = null,
        decimal? Units = null,
        DateOnly? FirstProjectedDate = null,
        DateOnly? NextPaymentDate = null,
        string? Currency = null,
        string? Notes = null);

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
}
