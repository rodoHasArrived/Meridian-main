using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Validates and commits an entity setup draft as one application workflow.
/// PostgreSQL deployments should pair this service with the PostgreSQL fund-structure service/store so the
/// storage layer can promote this unit of work to an explicit transaction when store-level batch support is available.
/// </summary>
public sealed class FundStructureSetupService : IFundStructureSetupService
{
    public const string AccountHandoffAssignmentType = "AccountHandoff";

    private readonly IFundStructureService _fundStructureService;

    public FundStructureSetupService(IFundStructureService fundStructureService)
    {
        _fundStructureService = fundStructureService ?? throw new ArgumentNullException(nameof(fundStructureService));
    }

    public async Task<FundStructureSetupPreviewDto> PreviewAsync(FundStructureSetupDraftRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var validation = await ValidateAsync(request, ct).ConfigureAwait(false);
        var draft = request.Draft;
        if (draft is null)
        {
            return new FundStructureSetupPreviewDto([], [], validation);
        }

        var effectiveFrom = ResolveEffectiveFrom(draft);
        var existing = await LoadExistingAsync(effectiveFrom, ct).ConfigureAwait(false);
        var ids = ResolveIds(draft, existing, request.ReuseExistingEntities);
        var clientOrFundKind = draft.ClientOrFund.CreateClient ? FundStructureNodeKindDto.Client : FundStructureNodeKindDto.Fund;
        var nodes = new[]
        {
            Node(ids.OrganizationId, FundStructureNodeKindDto.Organization, draft.Organization.Code, draft.Organization.Name, draft.Organization.Description, effectiveFrom),
            Node(ids.BusinessId, FundStructureNodeKindDto.Business, draft.BusinessLane.Code, draft.BusinessLane.Name, draft.BusinessLane.Description, effectiveFrom),
            Node(ids.ClientOrFundId, clientOrFundKind, draft.ClientOrFund.Code, draft.ClientOrFund.Name, draft.ClientOrFund.Description, effectiveFrom),
            Node(ids.EntityId, FundStructureNodeKindDto.Entity, draft.LegalEntity.Code, draft.LegalEntity.Name, draft.LegalEntity.Description, effectiveFrom),
            Node(ids.VehicleId, FundStructureNodeKindDto.Vehicle, draft.Vehicle.Code, draft.Vehicle.Name, draft.Vehicle.Description, effectiveFrom),
            Node(ids.InvestmentPortfolioId, FundStructureNodeKindDto.InvestmentPortfolio, draft.InvestmentPortfolio.Code, draft.InvestmentPortfolio.Name, draft.InvestmentPortfolio.Description, effectiveFrom)
        };

        return new FundStructureSetupPreviewDto(nodes, BuildPreviewLinks(draft).ToArray(), validation);
    }

    public async Task<FundStructureSetupCommitResultDto> CommitAsync(FundStructureSetupDraftRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var draft = request.Draft;
        var validation = await ValidateAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var graph = await _fundStructureService.GetFundStructureGraphAsync(new FundStructureQuery(ActiveOnly: true), ct).ConfigureAwait(false);
            return new FundStructureSetupCommitResultDto(false, [], [], null, graph, validation, validation.Issues.Select(static issue => issue.Message).ToArray());
        }

        var effectiveFrom = ResolveEffectiveFrom(draft);
        var requestedBy = string.IsNullOrWhiteSpace(draft.RequestedBy) ? "entity-setup" : draft.RequestedBy.Trim();
        var existing = await LoadExistingAsync(effectiveFrom, ct).ConfigureAwait(false);
        var ids = ResolveIds(draft, existing, request.ReuseExistingEntities);
        var entities = new List<FundStructureSetupEntityResolutionDto>();
        var messages = new List<string>();

        var organization = Get(existing.Organizations, ids.OrganizationId)
            ?? await _fundStructureService.CreateOrganizationAsync(
                new CreateOrganizationRequest(ids.OrganizationId, Clean(draft.Organization.Code), Clean(draft.Organization.Name), CleanCurrency(draft.Organization.BaseCurrency), effectiveFrom, requestedBy, CleanOptional(draft.Organization.Description)), ct).ConfigureAwait(false);
        AddResolution(entities, FundStructureSetupNodeAlias.Organization, FundStructureNodeKindDto.Organization, organization.OrganizationId, organization.Code, organization.Name, !existing.Organizations.ContainsKey(organization.OrganizationId));

        var business = Get(existing.Businesses, ids.BusinessId)
            ?? await _fundStructureService.CreateBusinessAsync(
                new CreateBusinessRequest(ids.BusinessId, organization.OrganizationId, draft.BusinessLane.BusinessKind, Clean(draft.BusinessLane.Code), Clean(draft.BusinessLane.Name), CleanCurrency(draft.BusinessLane.BaseCurrency), effectiveFrom, requestedBy, CleanOptional(draft.BusinessLane.Description)), ct).ConfigureAwait(false);
        AddResolution(entities, FundStructureSetupNodeAlias.BusinessLane, FundStructureNodeKindDto.Business, business.BusinessId, business.Code, business.Name, !existing.Businesses.ContainsKey(business.BusinessId));

        ClientSummaryDto? client = null;
        FundSummaryDto? fund = null;
        if (draft.ClientOrFund.CreateClient)
        {
            client = Get(existing.Clients, ids.ClientOrFundId)
                ?? await _fundStructureService.CreateClientAsync(
                    new CreateClientRequest(ids.ClientOrFundId, business.BusinessId, Clean(draft.ClientOrFund.Code), Clean(draft.ClientOrFund.Name), CleanCurrency(draft.ClientOrFund.BaseCurrency), effectiveFrom, requestedBy, CleanOptional(draft.ClientOrFund.Description), draft.ClientOrFund.ClientSegmentKind), ct).ConfigureAwait(false);
            AddResolution(entities, FundStructureSetupNodeAlias.ClientOrFund, FundStructureNodeKindDto.Client, client.ClientId, client.Code, client.Name, !existing.Clients.ContainsKey(client.ClientId));
        }
        else
        {
            fund = Get(existing.Funds, ids.ClientOrFundId)
                ?? await _fundStructureService.CreateFundAsync(
                    new CreateFundRequest(ids.ClientOrFundId, Clean(draft.ClientOrFund.Code), Clean(draft.ClientOrFund.Name), CleanCurrency(draft.ClientOrFund.BaseCurrency), effectiveFrom, requestedBy, CleanOptional(draft.ClientOrFund.Description), business.BusinessId), ct).ConfigureAwait(false);
            AddResolution(entities, FundStructureSetupNodeAlias.ClientOrFund, FundStructureNodeKindDto.Fund, fund.FundId, fund.Code, fund.Name, !existing.Funds.ContainsKey(fund.FundId));
        }

        var legalEntity = Get(existing.Entities, ids.EntityId)
            ?? await _fundStructureService.CreateLegalEntityAsync(
                new CreateLegalEntityRequest(ids.EntityId, draft.LegalEntity.EntityType, Clean(draft.LegalEntity.Code), Clean(draft.LegalEntity.Name), Clean(draft.LegalEntity.Jurisdiction), CleanCurrency(draft.LegalEntity.BaseCurrency), effectiveFrom, requestedBy, CleanOptional(draft.LegalEntity.Description)), ct).ConfigureAwait(false);
        AddResolution(entities, FundStructureSetupNodeAlias.LegalEntity, FundStructureNodeKindDto.Entity, legalEntity.EntityId, legalEntity.Code, legalEntity.Name, !existing.Entities.ContainsKey(legalEntity.EntityId));

        var vehicleFundId = fund?.FundId ?? ids.ClientOrFundId;
        if (fund is null)
        {
            fund = await _fundStructureService.CreateFundAsync(
                new CreateFundRequest(Guid.NewGuid(), $"{Clean(draft.ClientOrFund.Code)}-OPS", $"{Clean(draft.ClientOrFund.Name)} operating fund", CleanCurrency(draft.ClientOrFund.BaseCurrency), effectiveFrom, requestedBy, "Auto-created operating fund for client setup handoff.", business.BusinessId), ct).ConfigureAwait(false);
            vehicleFundId = fund.FundId;
            messages.Add($"Created operating fund {fund.Code} for client setup handoff.");
        }

        var vehicle = Get(existing.Vehicles, ids.VehicleId)
            ?? await _fundStructureService.CreateVehicleAsync(
                new CreateVehicleRequest(ids.VehicleId, vehicleFundId, legalEntity.EntityId, Clean(draft.Vehicle.Code), Clean(draft.Vehicle.Name), CleanCurrency(draft.Vehicle.BaseCurrency), effectiveFrom, requestedBy, CleanOptional(draft.Vehicle.Description)), ct).ConfigureAwait(false);
        AddResolution(entities, FundStructureSetupNodeAlias.Vehicle, FundStructureNodeKindDto.Vehicle, vehicle.VehicleId, vehicle.Code, vehicle.Name, !existing.Vehicles.ContainsKey(vehicle.VehicleId));

        var portfolio = Get(existing.InvestmentPortfolios, ids.InvestmentPortfolioId)
            ?? await _fundStructureService.CreateInvestmentPortfolioAsync(
                new CreateInvestmentPortfolioRequest(ids.InvestmentPortfolioId, business.BusinessId, Clean(draft.InvestmentPortfolio.Code), Clean(draft.InvestmentPortfolio.Name), CleanCurrency(draft.InvestmentPortfolio.BaseCurrency), effectiveFrom, requestedBy, client?.ClientId, fund?.FundId, null, vehicle.VehicleId, legalEntity.EntityId, CleanOptional(draft.InvestmentPortfolio.Description)), ct).ConfigureAwait(false);
        AddResolution(entities, FundStructureSetupNodeAlias.InvestmentPortfolio, FundStructureNodeKindDto.InvestmentPortfolio, portfolio.InvestmentPortfolioId, portfolio.Code, portfolio.Name, !existing.InvestmentPortfolios.ContainsKey(portfolio.InvestmentPortfolioId));

        existing = await LoadExistingAsync(effectiveFrom, ct).ConfigureAwait(false);
        var links = new List<OwnershipLinkDto>();
        foreach (var link in draft.InitialOwnershipLinks ?? Array.Empty<FundStructureSetupOwnershipLinkDraftDto>())
        {
            var parentId = ResolveAlias(link.Parent, ids);
            var childId = ResolveAlias(link.Child, ids);
            var reusable = existing.OwnershipLinks.Values.FirstOrDefault(candidate => candidate.ParentNodeId == parentId
                && candidate.ChildNodeId == childId
                && candidate.RelationshipType == link.RelationshipType
                && candidate.OwnershipPercent == link.OwnershipPercent
                && candidate.IsPrimary == link.IsPrimary);
            if (reusable is not null)
            {
                links.Add(reusable);
                continue;
            }

            links.Add(await _fundStructureService.LinkNodesAsync(
                new LinkFundStructureNodesRequest(link.OwnershipLinkId ?? Guid.NewGuid(), parentId, childId, link.RelationshipType, effectiveFrom, requestedBy, link.OwnershipPercent, link.IsPrimary, CleanOptional(link.Notes)), ct).ConfigureAwait(false));
        }

        var handoffReference = BuildHandoffReference(draft);
        existing = await LoadExistingAsync(effectiveFrom, ct).ConfigureAwait(false);
        var assignment = existing.Assignments.Values.FirstOrDefault(candidate => candidate.NodeId == portfolio.InvestmentPortfolioId
            && string.Equals(candidate.AssignmentType, AccountHandoffAssignmentType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.AssignmentReference, handoffReference, StringComparison.OrdinalIgnoreCase))
            ?? await _fundStructureService.AssignNodeAsync(
                new AssignFundStructureNodeRequest(Guid.NewGuid(), portfolio.InvestmentPortfolioId, AccountHandoffAssignmentType, handoffReference, effectiveFrom, requestedBy, IsPrimary: true), ct).ConfigureAwait(false);

        var committedGraph = await _fundStructureService.GetFundStructureGraphAsync(new FundStructureQuery(ActiveOnly: true, AsOf: effectiveFrom), ct).ConfigureAwait(false);
        var setupNodeIds = entities.Select(static entity => entity.NodeId).ToHashSet();
        var committedLinks = committedGraph.OwnershipLinks
            .Where(link => setupNodeIds.Contains(link.ParentNodeId) || setupNodeIds.Contains(link.ChildNodeId))
            .Concat(links)
            .DistinctBy(static link => link.OwnershipLinkId)
            .ToArray();
        messages.Add($"Committed setup draft with {entities.Count(static entity => entity.WasCreated)} created and {entities.Count(static entity => !entity.WasCreated)} reused entities.");
        return new FundStructureSetupCommitResultDto(true, entities, committedLinks, assignment, committedGraph, validation, messages);
    }

    private async Task<FundStructureSetupValidationSummaryDto> ValidateAsync(FundStructureSetupDraftRequest request, CancellationToken ct)
    {
        var issues = new List<FundStructureSetupValidationIssueDto>();
        if (request.Draft is null)
        {
            issues.Add(Blocker("draft.required", "Setup draft is required.", "$"));
            return new FundStructureSetupValidationSummaryDto(false, issues);
        }

        var draft = request.Draft;
        Require(draft.Organization.Code, "organization.code", "Organization code is required.", issues);
        Require(draft.Organization.Name, "organization.name", "Organization name is required.", issues);
        RequireCurrency(draft.Organization.BaseCurrency, "organization.baseCurrency", issues);
        Require(draft.BusinessLane.Code, "businessLane.code", "Business lane code is required.", issues);
        Require(draft.BusinessLane.Name, "businessLane.name", "Business lane name is required.", issues);
        RequireCurrency(draft.BusinessLane.BaseCurrency, "businessLane.baseCurrency", issues);
        Require(draft.ClientOrFund.Code, "clientOrFund.code", "Client or fund code is required.", issues);
        Require(draft.ClientOrFund.Name, "clientOrFund.name", "Client or fund name is required.", issues);
        RequireCurrency(draft.ClientOrFund.BaseCurrency, "clientOrFund.baseCurrency", issues);
        Require(draft.LegalEntity.Code, "legalEntity.code", "Legal entity code is required.", issues);
        Require(draft.LegalEntity.Name, "legalEntity.name", "Legal entity name is required.", issues);
        Require(draft.LegalEntity.Jurisdiction, "legalEntity.jurisdiction", "Legal entity jurisdiction is required.", issues);
        RequireCurrency(draft.LegalEntity.BaseCurrency, "legalEntity.baseCurrency", issues);
        Require(draft.Vehicle.Code, "vehicle.code", "Vehicle code is required.", issues);
        Require(draft.Vehicle.Name, "vehicle.name", "Vehicle name is required.", issues);
        RequireCurrency(draft.Vehicle.BaseCurrency, "vehicle.baseCurrency", issues);
        Require(draft.InvestmentPortfolio.Code, "investmentPortfolio.code", "Investment portfolio code is required.", issues);
        Require(draft.InvestmentPortfolio.Name, "investmentPortfolio.name", "Investment portfolio name is required.", issues);
        RequireCurrency(draft.InvestmentPortfolio.BaseCurrency, "investmentPortfolio.baseCurrency", issues);
        Require(draft.AccountHandoff.AccountCode, "accountHandoff.accountCode", "Account handoff account code is required.", issues);
        Require(draft.AccountHandoff.DisplayName, "accountHandoff.displayName", "Account handoff display name is required.", issues);
        RequireCurrency(draft.AccountHandoff.BaseCurrency, "accountHandoff.baseCurrency", issues);

        foreach (var (link, index) in (draft.InitialOwnershipLinks ?? Array.Empty<FundStructureSetupOwnershipLinkDraftDto>()).Select((link, index) => (link, index)))
        {
            if (link.Parent == link.Child)
                issues.Add(Blocker("ownership.selfLink", "Initial ownership link parent and child must be different.", $"initialOwnershipLinks[{index}]"));
            if (link.OwnershipPercent is < 0 or > 100)
                issues.Add(Blocker("ownership.percentRange", "Ownership percent must be between 0 and 100.", $"initialOwnershipLinks[{index}].ownershipPercent"));
        }

        if (issues.Any(static issue => issue.IsBlocking))
            return new FundStructureSetupValidationSummaryDto(false, issues);

        var previewLinks = BuildPreviewLinks(draft).ToArray();
        var previewIds = ResolveIds(draft, await LoadExistingAsync(ResolveEffectiveFrom(draft), ct).ConfigureAwait(false), request.ReuseExistingEntities);
        foreach (var (link, index) in previewLinks.Select((link, index) => (link, index)))
        {
            var graphResult = await _fundStructureService.ValidateOwnershipGraphAsync(new ValidateOwnershipGraphRequest(ResolveEffectiveFrom(draft), ActiveOnly: true), ct).ConfigureAwait(false);
            if (!graphResult.IsValid)
            {
                foreach (var graphIssue in graphResult.Issues)
                    issues.Add(Blocker(graphIssue.Code, graphIssue.Message, $"ownershipLinks[{index}]"));
            }
        }

        return new FundStructureSetupValidationSummaryDto(issues.All(static issue => !issue.IsBlocking), issues);
    }

    private async Task<ExistingStructure> LoadExistingAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var graph = await _fundStructureService.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: true, AsOf: asOf), ct).ConfigureAwait(false);
        return new ExistingStructure(
            graph.Organizations.ToDictionary(static item => item.OrganizationId),
            graph.Businesses.ToDictionary(static item => item.BusinessId),
            graph.Clients.ToDictionary(static item => item.ClientId),
            graph.Funds.ToDictionary(static item => item.FundId),
            graph.Vehicles.ToDictionary(static item => item.VehicleId),
            graph.Entities.ToDictionary(static item => item.EntityId),
            graph.InvestmentPortfolios.ToDictionary(static item => item.InvestmentPortfolioId),
            graph.OwnershipLinks.ToDictionary(static item => item.OwnershipLinkId),
            graph.Assignments.ToDictionary(static item => item.AssignmentId));
    }


    private static T? Get<T>(IReadOnlyDictionary<Guid, T> values, Guid id) where T : class
        => values.TryGetValue(id, out var value) ? value : null;

    private static ResolvedIds ResolveIds(FundStructureSetupDraftDto draft, ExistingStructure existing, bool reuseExisting)
    {
        var clientOrFundId = draft.ClientOrFund.CreateClient
            ? draft.ClientOrFund.ClientId ?? (reuseExisting ? Find(existing.Clients.Values, draft.ClientOrFund.Code, draft.ClientOrFund.Name) : null)
            : draft.ClientOrFund.FundId ?? (reuseExisting ? Find(existing.Funds.Values, draft.ClientOrFund.Code, draft.ClientOrFund.Name) : null);

        return new ResolvedIds(
            draft.Organization.OrganizationId ?? (reuseExisting ? Find(existing.Organizations.Values, draft.Organization.Code, draft.Organization.Name) : null) ?? Guid.NewGuid(),
            draft.BusinessLane.BusinessId ?? (reuseExisting ? Find(existing.Businesses.Values, draft.BusinessLane.Code, draft.BusinessLane.Name) : null) ?? Guid.NewGuid(),
            clientOrFundId ?? Guid.NewGuid(),
            draft.LegalEntity.EntityId ?? (reuseExisting ? Find(existing.Entities.Values, draft.LegalEntity.Code, draft.LegalEntity.Name) : null) ?? Guid.NewGuid(),
            draft.Vehicle.VehicleId ?? (reuseExisting ? Find(existing.Vehicles.Values, draft.Vehicle.Code, draft.Vehicle.Name) : null) ?? Guid.NewGuid(),
            draft.InvestmentPortfolio.InvestmentPortfolioId ?? (reuseExisting ? Find(existing.InvestmentPortfolios.Values, draft.InvestmentPortfolio.Code, draft.InvestmentPortfolio.Name) : null) ?? Guid.NewGuid());
    }

    private static Guid? Find<T>(IEnumerable<T> summaries, string code, string name) where T : notnull
    {
        foreach (var summary in summaries)
        {
            var node = ToNode(summary);
            if (Same(node.Code, code) || Same(node.Name, name))
                return node.NodeId;
        }
        return null;
    }

    private static FundStructureNodeDto ToNode<T>(T summary) => summary switch
    {
        OrganizationSummaryDto item => Node(item.OrganizationId, FundStructureNodeKindDto.Organization, item.Code, item.Name, item.Description, item.EffectiveFrom),
        BusinessSummaryDto item => Node(item.BusinessId, FundStructureNodeKindDto.Business, item.Code, item.Name, item.Description, item.EffectiveFrom),
        ClientSummaryDto item => Node(item.ClientId, FundStructureNodeKindDto.Client, item.Code, item.Name, item.Description, item.EffectiveFrom),
        FundSummaryDto item => Node(item.FundId, FundStructureNodeKindDto.Fund, item.Code, item.Name, item.Description, item.EffectiveFrom),
        VehicleSummaryDto item => Node(item.VehicleId, FundStructureNodeKindDto.Vehicle, item.Code, item.Name, item.Description, item.EffectiveFrom),
        LegalEntitySummaryDto item => Node(item.EntityId, FundStructureNodeKindDto.Entity, item.Code, item.Name, item.Description, item.EffectiveFrom),
        InvestmentPortfolioSummaryDto item => Node(item.InvestmentPortfolioId, FundStructureNodeKindDto.InvestmentPortfolio, item.Code, item.Name, item.Description, item.EffectiveFrom),
        _ => throw new ArgumentOutOfRangeException(nameof(summary), summary, null)
    };

    private static void AddResolution(ICollection<FundStructureSetupEntityResolutionDto> target, FundStructureSetupNodeAlias alias, FundStructureNodeKindDto kind, Guid id, string code, string name, bool wasCreated)
        => target.Add(new FundStructureSetupEntityResolutionDto(alias, kind, id, code, name, wasCreated, wasCreated ? "Created" : "Reused existing entity"));

    private static IEnumerable<FundStructureSetupPreviewLinkDto> BuildPreviewLinks(FundStructureSetupDraftDto draft)
    {
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.Organization, FundStructureSetupNodeAlias.BusinessLane, OwnershipRelationshipTypeDto.Owns, null, true, "Organization root");
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.BusinessLane, FundStructureSetupNodeAlias.ClientOrFund, draft.ClientOrFund.CreateClient ? OwnershipRelationshipTypeDto.Advises : OwnershipRelationshipTypeDto.Operates, null, true, "Primary setup lineage");
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.ClientOrFund, FundStructureSetupNodeAlias.Vehicle, OwnershipRelationshipTypeDto.Owns, null, true, "Vehicle setup lineage");
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.Vehicle, FundStructureSetupNodeAlias.InvestmentPortfolio, OwnershipRelationshipTypeDto.AllocatesTo, null, true, "Portfolio handoff");
        foreach (var link in draft.InitialOwnershipLinks ?? Array.Empty<FundStructureSetupOwnershipLinkDraftDto>())
            yield return new FundStructureSetupPreviewLinkDto(link.Parent, link.Child, link.RelationshipType, link.OwnershipPercent, link.IsPrimary, link.Notes);
    }

    private static string BuildHandoffReference(FundStructureSetupDraftDto draft)
        => string.Join("|", new[]
        {
            Clean(draft.AccountHandoff.AccountCode), Clean(draft.AccountHandoff.DisplayName), draft.AccountHandoff.AccountType.ToString(),
            CleanCurrency(draft.AccountHandoff.BaseCurrency), CleanOptional(draft.AccountHandoff.Institution) ?? string.Empty,
            CleanOptional(draft.AccountHandoff.LedgerReference) ?? string.Empty
        });

    private static FundStructureNodeDto Node(Guid id, FundStructureNodeKindDto kind, string code, string name, string? description, DateTimeOffset effectiveFrom)
        => new(id, kind, Clean(code), Clean(name), CleanOptional(description), true, effectiveFrom, null);
    private static DateTimeOffset ResolveEffectiveFrom(FundStructureSetupDraftDto draft) => draft.EffectiveFrom ?? DateTimeOffset.UtcNow;
    private static Guid ResolveAlias(FundStructureSetupNodeAlias alias, ResolvedIds ids) => alias switch
    {
        FundStructureSetupNodeAlias.Organization => ids.OrganizationId,
        FundStructureSetupNodeAlias.BusinessLane => ids.BusinessId,
        FundStructureSetupNodeAlias.ClientOrFund => ids.ClientOrFundId,
        FundStructureSetupNodeAlias.LegalEntity => ids.EntityId,
        FundStructureSetupNodeAlias.Vehicle => ids.VehicleId,
        FundStructureSetupNodeAlias.InvestmentPortfolio => ids.InvestmentPortfolioId,
        _ => throw new ArgumentOutOfRangeException(nameof(alias), alias, null)
    };
    private static void Require(string? value, string path, string message, ICollection<FundStructureSetupValidationIssueDto> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) issues.Add(Blocker("required", message, path));
    }
    private static void RequireCurrency(string? value, string path, ICollection<FundStructureSetupValidationIssueDto> issues)
    {
        Require(value, path, "Currency is required.", issues);
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length != 3) issues.Add(Blocker("currency.format", "Currency must be a 3-character ISO code.", path));
    }
    private static FundStructureSetupValidationIssueDto Blocker(string code, string message, string path) => new(code, message, path, true);
    private static bool Same(string left, string right) => string.Equals(Clean(left), Clean(right), StringComparison.OrdinalIgnoreCase);
    private static string Clean(string value) => value.Trim();
    private static string CleanCurrency(string value) => value.Trim().ToUpperInvariant();
    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ResolvedIds(Guid OrganizationId, Guid BusinessId, Guid ClientOrFundId, Guid EntityId, Guid VehicleId, Guid InvestmentPortfolioId);
    private sealed record ExistingStructure(
        IReadOnlyDictionary<Guid, OrganizationSummaryDto> Organizations,
        IReadOnlyDictionary<Guid, BusinessSummaryDto> Businesses,
        IReadOnlyDictionary<Guid, ClientSummaryDto> Clients,
        IReadOnlyDictionary<Guid, FundSummaryDto> Funds,
        IReadOnlyDictionary<Guid, VehicleSummaryDto> Vehicles,
        IReadOnlyDictionary<Guid, LegalEntitySummaryDto> Entities,
        IReadOnlyDictionary<Guid, InvestmentPortfolioSummaryDto> InvestmentPortfolios,
        IReadOnlyDictionary<Guid, OwnershipLinkDto> OwnershipLinks,
        IReadOnlyDictionary<Guid, FundStructureAssignmentDto> Assignments);
}
