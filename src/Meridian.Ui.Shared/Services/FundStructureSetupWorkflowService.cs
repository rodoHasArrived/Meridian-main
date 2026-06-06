using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Shared orchestration for operator-driven entity setup. Browser endpoints and desktop view models call this
/// service so setup validation, command composition, and graph preview stay server-owned.
/// </summary>
public sealed class FundStructureSetupWorkflowService
{
    public const string AccountHandoffAssignmentType = "AccountHandoff";

    private readonly IFundStructureService _fundStructureService;

    public FundStructureSetupWorkflowService(IFundStructureService fundStructureService)
    {
        _fundStructureService = fundStructureService ?? throw new ArgumentNullException(nameof(fundStructureService));
    }

    public FundStructureSetupValidationSummaryDto Validate(FundStructureSetupDraftDto? draft)
    {
        var issues = new List<FundStructureSetupValidationIssueDto>();
        if (draft is null)
        {
            issues.Add(Blocker("draft.required", "Setup draft is required.", "$"));
            return new FundStructureSetupValidationSummaryDto(false, issues);
        }

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
            {
                issues.Add(Blocker("ownership.selfLink", "Initial ownership link parent and child must be different.", $"initialOwnershipLinks[{index}]"));
            }

            if (link.OwnershipPercent is < 0 or > 100)
            {
                issues.Add(Blocker("ownership.percentRange", "Ownership percent must be between 0 and 100.", $"initialOwnershipLinks[{index}].ownershipPercent"));
            }
        }

        return new FundStructureSetupValidationSummaryDto(issues.All(static issue => !issue.IsBlocking), issues);
    }

    public FundStructureSetupPreviewDto Preview(FundStructureSetupDraftDto? draft)
    {
        var validation = Validate(draft);
        if (draft is null)
        {
            return new FundStructureSetupPreviewDto(Array.Empty<FundStructureNodeDto>(), Array.Empty<FundStructureSetupPreviewLinkDto>(), validation);
        }

        var effectiveFrom = ResolveEffectiveFrom(draft);
        var ids = ResolveIds(draft);
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

    public Task<FundStructureSetupResultDto> CreateAsync(FundStructureSetupDraftDto draft, CancellationToken ct = default)
        => CreateAsync(draft, requestedBy: null, ct);

    public async Task<FundStructureSetupResultDto> CreateAsync(FundStructureSetupDraftDto draft, string? requestedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ct.ThrowIfCancellationRequested();

        var validation = Validate(draft);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Issues.Where(static issue => issue.IsBlocking).Select(static issue => issue.Message)));
        }

        var effectiveFrom = ResolveEffectiveFrom(draft);
        var auditActor = string.IsNullOrWhiteSpace(requestedBy)
            ? (string.IsNullOrWhiteSpace(draft.RequestedBy) ? "entity-setup" : draft.RequestedBy.Trim())
            : requestedBy.Trim();
        var ids = ResolveIds(draft);

        var organization = await _fundStructureService.CreateOrganizationAsync(
            new CreateOrganizationRequest(ids.OrganizationId, Clean(draft.Organization.Code), Clean(draft.Organization.Name), CleanCurrency(draft.Organization.BaseCurrency), effectiveFrom, auditActor, CleanOptional(draft.Organization.Description)),
            ct).ConfigureAwait(false);

        var business = await _fundStructureService.CreateBusinessAsync(
            new CreateBusinessRequest(ids.BusinessId, organization.OrganizationId, draft.BusinessLane.BusinessKind, Clean(draft.BusinessLane.Code), Clean(draft.BusinessLane.Name), CleanCurrency(draft.BusinessLane.BaseCurrency), effectiveFrom, auditActor, CleanOptional(draft.BusinessLane.Description)),
            ct).ConfigureAwait(false);

        ClientSummaryDto? client = null;
        FundSummaryDto? fund = null;
        if (draft.ClientOrFund.CreateClient)
        {
            client = await _fundStructureService.CreateClientAsync(
                new CreateClientRequest(ids.ClientOrFundId, business.BusinessId, Clean(draft.ClientOrFund.Code), Clean(draft.ClientOrFund.Name), CleanCurrency(draft.ClientOrFund.BaseCurrency), effectiveFrom, auditActor, CleanOptional(draft.ClientOrFund.Description), draft.ClientOrFund.ClientSegmentKind),
                ct).ConfigureAwait(false);
        }
        else
        {
            fund = await _fundStructureService.CreateFundAsync(
                new CreateFundRequest(ids.ClientOrFundId, Clean(draft.ClientOrFund.Code), Clean(draft.ClientOrFund.Name), CleanCurrency(draft.ClientOrFund.BaseCurrency), effectiveFrom, auditActor, CleanOptional(draft.ClientOrFund.Description), business.BusinessId),
                ct).ConfigureAwait(false);
        }

        var legalEntity = await _fundStructureService.CreateLegalEntityAsync(
            new CreateLegalEntityRequest(ids.EntityId, draft.LegalEntity.EntityType, Clean(draft.LegalEntity.Code), Clean(draft.LegalEntity.Name), Clean(draft.LegalEntity.Jurisdiction), CleanCurrency(draft.LegalEntity.BaseCurrency), effectiveFrom, auditActor, CleanOptional(draft.LegalEntity.Description)),
            ct).ConfigureAwait(false);

        var vehicleFundId = fund?.FundId ?? ids.ClientOrFundId;
        if (fund is null)
        {
            fund = await _fundStructureService.CreateFundAsync(
                new CreateFundRequest(Guid.NewGuid(), $"{Clean(draft.ClientOrFund.Code)}-OPS", $"{Clean(draft.ClientOrFund.Name)} operating fund", CleanCurrency(draft.ClientOrFund.BaseCurrency), effectiveFrom, auditActor, "Auto-created operating fund for client setup handoff.", business.BusinessId),
                ct).ConfigureAwait(false);
            vehicleFundId = fund.FundId;
        }

        var vehicle = await _fundStructureService.CreateVehicleAsync(
            new CreateVehicleRequest(ids.VehicleId, vehicleFundId, legalEntity.EntityId, Clean(draft.Vehicle.Code), Clean(draft.Vehicle.Name), CleanCurrency(draft.Vehicle.BaseCurrency), effectiveFrom, auditActor, CleanOptional(draft.Vehicle.Description)),
            ct).ConfigureAwait(false);

        var portfolio = await _fundStructureService.CreateInvestmentPortfolioAsync(
            new CreateInvestmentPortfolioRequest(
                ids.InvestmentPortfolioId,
                business.BusinessId,
                Clean(draft.InvestmentPortfolio.Code),
                Clean(draft.InvestmentPortfolio.Name),
                CleanCurrency(draft.InvestmentPortfolio.BaseCurrency),
                effectiveFrom,
                auditActor,
                ClientId: client?.ClientId,
                FundId: fund?.FundId,
                EntityId: legalEntity.EntityId,
                Description: CleanOptional(draft.InvestmentPortfolio.Description)),
            ct).ConfigureAwait(false);

        var links = new List<OwnershipLinkDto>();
        foreach (var link in draft.InitialOwnershipLinks ?? Array.Empty<FundStructureSetupOwnershipLinkDraftDto>())
        {
            links.Add(await _fundStructureService.LinkNodesAsync(
                new LinkFundStructureNodesRequest(link.OwnershipLinkId ?? Guid.NewGuid(), ResolveAlias(link.Parent, ids), ResolveAlias(link.Child, ids), link.RelationshipType, effectiveFrom, auditActor, link.OwnershipPercent, link.IsPrimary, CleanOptional(link.Notes)),
                ct).ConfigureAwait(false));
        }

        var handoffReference = string.Join("|", new[]
        {
            Clean(draft.AccountHandoff.AccountCode),
            Clean(draft.AccountHandoff.DisplayName),
            draft.AccountHandoff.AccountType.ToString(),
            CleanCurrency(draft.AccountHandoff.BaseCurrency),
            CleanOptional(draft.AccountHandoff.Institution) ?? string.Empty,
            CleanOptional(draft.AccountHandoff.LedgerReference) ?? string.Empty
        });

        var assignment = await _fundStructureService.AssignNodeAsync(
            new AssignFundStructureNodeRequest(Guid.NewGuid(), portfolio.InvestmentPortfolioId, AccountHandoffAssignmentType, handoffReference, effectiveFrom, auditActor, IsPrimary: true),
            ct).ConfigureAwait(false);

        var graph = await _fundStructureService.GetFundStructureGraphAsync(new FundStructureQuery(ActiveOnly: true, AsOf: effectiveFrom), ct).ConfigureAwait(false);
        return new FundStructureSetupResultDto(organization, business, client, fund, legalEntity, vehicle, portfolio, links, assignment, graph, validation);
    }

    private static IEnumerable<FundStructureSetupPreviewLinkDto> BuildPreviewLinks(FundStructureSetupDraftDto draft)
    {
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.Organization, FundStructureSetupNodeAlias.BusinessLane, OwnershipRelationshipTypeDto.Owns, null, true, "Organization root");
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.BusinessLane, FundStructureSetupNodeAlias.ClientOrFund, draft.ClientOrFund.CreateClient ? OwnershipRelationshipTypeDto.Advises : OwnershipRelationshipTypeDto.Operates, null, true, "Primary setup lineage");
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.ClientOrFund, FundStructureSetupNodeAlias.Vehicle, OwnershipRelationshipTypeDto.Owns, null, true, "Vehicle setup lineage");
        yield return new FundStructureSetupPreviewLinkDto(FundStructureSetupNodeAlias.Vehicle, FundStructureSetupNodeAlias.InvestmentPortfolio, OwnershipRelationshipTypeDto.AllocatesTo, null, true, "Portfolio handoff");
        foreach (var link in draft.InitialOwnershipLinks ?? Array.Empty<FundStructureSetupOwnershipLinkDraftDto>())
        {
            yield return new FundStructureSetupPreviewLinkDto(link.Parent, link.Child, link.RelationshipType, link.OwnershipPercent, link.IsPrimary, link.Notes);
        }
    }

    private static FundStructureNodeDto Node(Guid id, FundStructureNodeKindDto kind, string code, string name, string? description, DateTimeOffset effectiveFrom)
        => new(id, kind, Clean(code), Clean(name), CleanOptional(description), true, effectiveFrom, null);

    private static DateTimeOffset ResolveEffectiveFrom(FundStructureSetupDraftDto draft) => draft.EffectiveFrom ?? DateTimeOffset.UtcNow;

    private static ResolvedIds ResolveIds(FundStructureSetupDraftDto draft)
        => new(
            draft.Organization.OrganizationId ?? Guid.NewGuid(),
            draft.BusinessLane.BusinessId ?? Guid.NewGuid(),
            (draft.ClientOrFund.CreateClient ? draft.ClientOrFund.ClientId : draft.ClientOrFund.FundId) ?? Guid.NewGuid(),
            draft.LegalEntity.EntityId ?? Guid.NewGuid(),
            draft.Vehicle.VehicleId ?? Guid.NewGuid(),
            draft.InvestmentPortfolio.InvestmentPortfolioId ?? Guid.NewGuid());

    private static Guid ResolveAlias(FundStructureSetupNodeAlias alias, ResolvedIds ids)
        => alias switch
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
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Blocker("required", message, path));
        }
    }

    private static void RequireCurrency(string? value, string path, ICollection<FundStructureSetupValidationIssueDto> issues)
    {
        Require(value, path, "Currency is required.", issues);
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length != 3)
        {
            issues.Add(Blocker("currency.format", "Currency must be a 3-character ISO code.", path));
        }
    }

    private static FundStructureSetupValidationIssueDto Blocker(string code, string message, string path)
        => new(code, message, path, true);

    private static string Clean(string value) => value.Trim();

    private static string CleanCurrency(string value) => value.Trim().ToUpperInvariant();

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ResolvedIds(Guid OrganizationId, Guid BusinessId, Guid ClientOrFundId, Guid EntityId, Guid VehicleId, Guid InvestmentPortfolioId);
}
