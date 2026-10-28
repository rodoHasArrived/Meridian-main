using System.Text.Json;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.Composition;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Logging;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;
using Serilog;
using static Meridian.Contracts.Text.TextPrimitives;
using static Meridian.Application.FundStructure.FundStructureProjector;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Thread-safe governance structure service backed by an in-memory working set
/// with optional durable JSON snapshot persistence for local-first workflows.
/// </summary>
public sealed partial class InMemoryFundStructureService : INonProductionOnlyService, IFundStructureService
{
    private static readonly ILogger Log = LoggingSetup.ForContext<InMemoryFundStructureService>();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly IFundAccountService _fundAccountService;
    private readonly IGovernanceSharedDataAccessService? _sharedDataAccessService;
    private readonly FundStructureCashFlowReadService _cashFlowReadService;
    private readonly IFundStructureStateStore _stateStore;
    private readonly IFundStructurePolicyService _policyService;
    private readonly bool _persistenceEnabled;
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
        _cashFlowReadService = new FundStructureCashFlowReadService(fundAccountService, securityMasterQueryService);
        _persistenceEnabled = !string.IsNullOrWhiteSpace(persistencePath);
        _stateStore = _persistenceEnabled
            ? new JsonFileFundStructureStateStore(persistencePath!)
            : new InMemoryFundStructureStateStore();
        _policyService = new FundStructurePolicyService();
        LoadState();
    }

    internal sealed record PersistedState(
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
            request.Description,
            request.LegalForm,
            request.LifecycleStatus,
            request.RegistrationNumber,
            NormalizeBeneficialOwners(request.BeneficialOwners),
            NormalizeLifecycleEvents(request.LifecycleEvents));

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

    public async Task<LegalEntitySummaryDto> UpdateLegalEntityProfileAsync(
        UpdateLegalEntityProfileRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        LegalEntitySummaryDto updated;
        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            if (!_entities.TryGetValue(request.EntityId, out var existing))
            {
                throw new InvalidOperationException($"LegalEntity {request.EntityId} was not found.");
            }

            updated = NormalizeLegalEntity(existing with
            {
                EntityType = request.EntityType ?? existing.EntityType,
                LegalForm = request.LegalForm ?? existing.LegalForm,
                Code = CleanRequired(request.Code, existing.Code),
                Name = CleanRequired(request.Name, existing.Name),
                Jurisdiction = CleanRequired(request.Jurisdiction, existing.Jurisdiction),
                BaseCurrency = CleanRequired(request.BaseCurrency, existing.BaseCurrency).ToUpperInvariant(),
                IsActive = request.IsActive ?? existing.IsActive,
                EffectiveFrom = request.EffectiveFrom ?? existing.EffectiveFrom,
                EffectiveTo = request.EffectiveTo ?? existing.EffectiveTo,
                Description = NormalizeOptional(request.Description) ?? existing.Description,
                RegistrationNumber = NormalizeOptional(request.RegistrationNumber) ?? existing.RegistrationNumber,
                LifecycleStatus = request.LifecycleStatus ?? existing.LifecycleStatus,
                BeneficialOwners = request.BeneficialOwners is null
                    ? existing.BeneficialOwners
                    : NormalizeBeneficialOwners(request.BeneficialOwners),
                LifecycleEvents = AppendLifecycleEvent(existing.LifecycleEvents, request.LifecycleEvent)
            });
            _entities[request.EntityId] = updated;
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<InvestmentPortfolioSummaryDto> CreateInvestmentPortfolioAsync(
        CreateInvestmentPortfolioRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        _policyService.EnsureSingleOperatingParent(request);

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

            var nodeKinds = CaptureNodeKindsLocked();
            nodeKinds[request.ParentNodeId] = parentKind.Value;
            nodeKinds[request.ChildNodeId] = childKind.Value;
            _policyService.ValidateOwnershipLink(
                link,
                parentKind.Value,
                childKind.Value,
                _ownershipLinks.Values.ToList(),
                nodeKinds);

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

    public async Task<OwnershipLinkDto> UpdateOwnershipLinkAsync(
        UpdateOwnershipLinkRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        (OwnershipLinkDto Link, (long Version, string Json)? Snapshot) result;
        lock (_gate)
        {
            if (!_ownershipLinks.TryGetValue(request.OwnershipLinkId, out var existing))
            {
                throw new InvalidOperationException($"Ownership link {request.OwnershipLinkId} was not found.");
            }

            var updated = existing with
            {
                RelationshipType = request.RelationshipType,
                OwnershipPercent = request.OwnershipPercent,
                IsPrimary = request.IsPrimary,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo,
                Notes = request.Notes
            };
            var nodeKinds = CaptureNodeKindsLocked();
            var parentKind = ResolveKnownNodeKind(existing.ParentNodeId, nodeKinds);
            var childKind = ResolveKnownNodeKind(existing.ChildNodeId, nodeKinds);
            _policyService.ValidateOwnershipUpdate(
                existing,
                updated,
                parentKind,
                childKind,
                _ownershipLinks.Values.ToList(),
                nodeKinds);

            _ownershipLinks[updated.OwnershipLinkId] = updated;
            RebuildOwnershipProjectionsLocked();
            result = (updated, CaptureSnapshotLocked());
        }

        await PersistSnapshotAsync(result.Snapshot, ct).ConfigureAwait(false);
        return result.Link;
    }

    public async Task<OwnershipLinkDto> ExpireOwnershipLinkAsync(
        ExpireOwnershipLinkRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        (OwnershipLinkDto Link, (long Version, string Json)? Snapshot) result;
        lock (_gate)
        {
            if (!_ownershipLinks.TryGetValue(request.OwnershipLinkId, out var existing))
            {
                throw new InvalidOperationException($"Ownership link {request.OwnershipLinkId} was not found.");
            }

            _policyService.ValidateOwnershipExpiration(existing, request.EffectiveTo);

            var notes = string.IsNullOrWhiteSpace(request.Notes) ? existing.Notes : request.Notes;
            var expired = existing with { EffectiveTo = request.EffectiveTo, Notes = notes };
            _ownershipLinks[expired.OwnershipLinkId] = expired;
            RebuildOwnershipProjectionsLocked();
            result = (expired, CaptureSnapshotLocked());
        }

        await PersistSnapshotAsync(result.Snapshot, ct).ConfigureAwait(false);
        return result.Link;
    }

    public async Task<OwnershipLinkDto> ReplaceOwnershipLinkAsync(
        ReplaceOwnershipLinkRequest request,
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

        (OwnershipLinkDto Link, (long Version, string Json)? Snapshot) result;
        lock (_gate)
        {
            if (!_ownershipLinks.TryGetValue(request.OwnershipLinkId, out var existing))
            {
                throw new InvalidOperationException($"Ownership link {request.OwnershipLinkId} was not found.");
            }

            if (_ownershipLinks.ContainsKey(request.ReplacementOwnershipLinkId))
            {
                throw new InvalidOperationException($"Ownership link {request.ReplacementOwnershipLinkId} already exists.");
            }

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
            var nodeKinds = CaptureNodeKindsLocked();
            nodeKinds[request.ParentNodeId] = parentKind.Value;
            nodeKinds[request.ChildNodeId] = childKind.Value;
            _policyService.ValidateOwnershipReplacement(
                existing,
                expiredExisting,
                replacement,
                parentKind.Value,
                childKind.Value,
                _ownershipLinks.Values.ToList(),
                nodeKinds);

            _ownershipLinks[existing.OwnershipLinkId] = expiredExisting;
            if (parentKind == FundStructureNodeKindDto.Account)
            {
                _linkedAccountIds.Add(request.ParentNodeId);
            }

            if (childKind == FundStructureNodeKindDto.Account)
            {
                _linkedAccountIds.Add(request.ChildNodeId);
            }

            _ownershipLinks[replacement.OwnershipLinkId] = replacement;
            RebuildOwnershipProjectionsLocked();
            result = (replacement, CaptureSnapshotLocked());
        }

        await PersistSnapshotAsync(result.Snapshot, ct).ConfigureAwait(false);
        return result.Link;
    }

    public Task<OwnershipGraphValidationResultDto> ValidateOwnershipGraphAsync(
        ValidateOwnershipGraphRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
            var issues = ValidateOwnershipGraphLocked(request.ActiveOnly, asOf);
            var result = new OwnershipGraphValidationResultDto(issues.Count == 0, asOf, issues);
            return Task.FromResult(result);
        }
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

        var normalizedAssignmentReference = LedgerGroupingRules.NormalizeAssignmentReference(
            request.AssignmentType,
            request.AssignmentReference);

        var assignment = new FundStructureAssignmentDto(
            request.AssignmentId,
            request.NodeId,
            request.AssignmentType,
            normalizedAssignmentReference,
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
        return FundStructureProjector.ProjectOrganizationStructure(snapshot, visibleAccounts, query, asOf, sharedDataAccess);
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
        return FundStructureProjector.ProjectFundStructureGraph(snapshot, visibleAccounts, query, asOf);
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
        return FundStructureProjector.ProjectAdvisoryView(snapshot, accounts, query, asOf, sharedDataAccess);
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
        return FundStructureProjector.ProjectFundOperatingView(snapshot, accounts, query, asOf, sharedDataAccess);
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
        return FundStructureProjector.ProjectAccountingView(snapshot, visibleAccounts, query, asOf, sharedDataAccess);
    }

    public async Task<GovernanceCashFlowViewDto?> GetCashFlowViewAsync(
        GovernanceCashFlowQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        _policyService.ValidateCashFlowQuery(query);

        var snapshot = CreateSnapshot();
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var sharedDataAccess = await GetSharedDataAccessAsync(ct).ConfigureAwait(false);
        var visibleAccounts = AttachSharedDataAccess(
            await GetVisibleAccountsAsync(query.ActiveOnly, asOf, ct).ConfigureAwait(false),
            sharedDataAccess);
        return await _cashFlowReadService.GetViewAsync(
            query, snapshot, visibleAccounts, asOf, sharedDataAccess, ct).ConfigureAwait(false);
    }

    private async Task<FundStructureSharedDataAccessDto?> GetSharedDataAccessAsync(CancellationToken ct)
        => _sharedDataAccessService is null
            ? null
            : await _sharedDataAccessService.GetSharedDataAccessAsync(ct).ConfigureAwait(false);

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
        if (!_persistenceEnabled)
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
        if (snapshot is null || !_persistenceEnabled)
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

            await _stateStore.SaveAsync(snapshot.Value.Json, ct).ConfigureAwait(false);
            _persistedVersion = snapshot.Value.Version;
        }
        finally
        {
            _persistGate.Release();
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

    private Dictionary<Guid, FundStructureNodeKindDto> CaptureNodeKindsLocked()
    {
        var nodeKinds = new Dictionary<Guid, FundStructureNodeKindDto>();
        foreach (var nodeId in _organizations.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Organization;
        foreach (var nodeId in _businesses.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Business;
        foreach (var nodeId in _clients.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Client;
        foreach (var nodeId in _funds.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Fund;
        foreach (var nodeId in _sleeves.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Sleeve;
        foreach (var nodeId in _vehicles.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Vehicle;
        foreach (var nodeId in _entities.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Entity;
        foreach (var nodeId in _investmentPortfolios.Keys)
            nodeKinds[nodeId] = FundStructureNodeKindDto.InvestmentPortfolio;
        foreach (var nodeId in _linkedAccountIds)
            nodeKinds[nodeId] = FundStructureNodeKindDto.Account;
        foreach (var link in _ownershipLinks.Values)
        {
            nodeKinds.TryAdd(link.ParentNodeId, FundStructureNodeKindDto.Account);
            nodeKinds.TryAdd(link.ChildNodeId, FundStructureNodeKindDto.Account);
        }

        return nodeKinds;
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

    private void RebuildOwnershipProjectionsLocked()
    {
        foreach (var (id, organization) in _organizations.ToList())
        {
            _organizations[id] = organization with { BusinessIds = [] };
        }

        foreach (var (id, business) in _businesses.ToList())
        {
            _businesses[id] = business with
            {
                ClientIds = [],
                FundIds = [],
                InvestmentPortfolioIds = []
            };
        }

        foreach (var (id, client) in _clients.ToList())
        {
            _clients[id] = client with { InvestmentPortfolioIds = [] };
        }

        foreach (var (id, fund) in _funds.ToList())
        {
            _funds[id] = fund with
            {
                BusinessId = null,
                SleeveIds = [],
                VehicleIds = [],
                EntityIds = [],
                InvestmentPortfolioIds = [],
                AccountIds = []
            };
        }

        foreach (var (id, sleeve) in _sleeves.ToList())
        {
            _sleeves[id] = sleeve with
            {
                InvestmentPortfolioIds = [],
                AccountIds = []
            };
        }

        foreach (var (id, vehicle) in _vehicles.ToList())
        {
            _vehicles[id] = vehicle with
            {
                InvestmentPortfolioIds = [],
                AccountIds = []
            };
        }

        foreach (var (id, portfolio) in _investmentPortfolios.ToList())
        {
            _investmentPortfolios[id] = portfolio with
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
        foreach (var link in _ownershipLinks.Values
            .Where(link => OwnershipGraphValidation.IsOwnershipLinkVisible(link, activeOnly: true, now))
            .OrderBy(static link => link.EffectiveFrom)
            .ThenBy(static link => link.OwnershipLinkId))
        {
            ApplyOwnershipLinkLocked(link);
        }
    }

    private List<OwnershipGraphValidationIssueDto> ValidateOwnershipGraphLocked(bool activeOnly, DateTimeOffset asOf)
    {
        var links = _ownershipLinks.Values
            .Where(link => OwnershipGraphValidation.IsOwnershipLinkVisible(link, activeOnly, asOf))
            .ToList();
        return OwnershipGraphValidation.Validate(links, nodeId => TryGetStoredNodeKindLocked(nodeId, out _));
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

    private static LegalEntitySummaryDto NormalizeLegalEntity(LegalEntitySummaryDto entity) =>
        entity with
        {
            Code = CleanRequired(entity.Code, entity.Code),
            Name = CleanRequired(entity.Name, entity.Name),
            Jurisdiction = CleanRequired(entity.Jurisdiction, entity.Jurisdiction),
            BaseCurrency = CleanRequired(entity.BaseCurrency, entity.BaseCurrency).ToUpperInvariant(),
            RegistrationNumber = NormalizeOptional(entity.RegistrationNumber),
            Description = NormalizeOptional(entity.Description),
            BeneficialOwners = NormalizeBeneficialOwners(entity.BeneficialOwners),
            LifecycleEvents = NormalizeLifecycleEvents(entity.LifecycleEvents)
        };

    private static IReadOnlyList<BeneficialOwnerSummaryDto> NormalizeBeneficialOwners(
        IReadOnlyList<BeneficialOwnerSummaryDto>? owners) =>
        owners is null
            ? []
            : owners
                .Where(static owner => !string.IsNullOrWhiteSpace(owner.OwnerName))
                .Select(static owner => owner with
                {
                    OwnerName = owner.OwnerName.Trim(),
                    OwnerIdentifier = NormalizeOptional(owner.OwnerIdentifier),
                    Notes = NormalizeOptional(owner.Notes)
                })
                .ToList();

    private static IReadOnlyList<LegalEntityLifecycleEventDto> NormalizeLifecycleEvents(
        IReadOnlyList<LegalEntityLifecycleEventDto>? events) =>
        events is null
            ? []
            : events
                .Where(static lifecycleEvent => !string.IsNullOrWhiteSpace(lifecycleEvent.RecordedBy)
                    && !string.IsNullOrWhiteSpace(lifecycleEvent.Summary))
                .Select(static lifecycleEvent => lifecycleEvent with
                {
                    RecordedBy = lifecycleEvent.RecordedBy.Trim(),
                    Summary = lifecycleEvent.Summary.Trim(),
                    EvidenceReference = NormalizeOptional(lifecycleEvent.EvidenceReference)
                })
                .OrderBy(static lifecycleEvent => lifecycleEvent.OccurredAt)
                .ThenBy(static lifecycleEvent => lifecycleEvent.EventId)
                .ToList();

    private static IReadOnlyList<LegalEntityLifecycleEventDto> AppendLifecycleEvent(
        IReadOnlyList<LegalEntityLifecycleEventDto>? current,
        LegalEntityLifecycleEventDto? lifecycleEvent)
    {
        var events = NormalizeLifecycleEvents(current).ToList();
        if (lifecycleEvent is not null)
        {
            events.AddRange(NormalizeLifecycleEvents([lifecycleEvent]));
        }

        return events
            .GroupBy(static item => item.EventId)
            .Select(static group => group.Last())
            .OrderBy(static item => item.OccurredAt)
            .ThenBy(static item => item.EventId)
            .ToList();
    }

    private static string CleanRequired(string? candidate, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
        return value.Trim();
    }


}
