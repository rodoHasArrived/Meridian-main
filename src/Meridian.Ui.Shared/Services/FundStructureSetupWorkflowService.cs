using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compatibility facade for desktop and browser setup callers. The application-layer
/// <see cref="IFundStructureSetupService"/> owns validation, preview, reuse detection, and commit orchestration.
/// </summary>
public sealed class FundStructureSetupWorkflowService
{
    public const string AccountHandoffAssignmentType = FundStructureSetupService.AccountHandoffAssignmentType;

    private readonly IFundStructureSetupService _setupService;

    [ActivatorUtilitiesConstructor]
    public FundStructureSetupWorkflowService(IFundStructureSetupService setupService)
    {
        _setupService = setupService ?? throw new ArgumentNullException(nameof(setupService));
    }

    public FundStructureSetupWorkflowService(IFundStructureService fundStructureService)
        : this(new FundStructureSetupService(fundStructureService))
    {
    }

    public FundStructureSetupValidationSummaryDto Validate(FundStructureSetupDraftDto? draft)
        => Preview(draft).ValidationSummary;

    public FundStructureSetupPreviewDto Preview(FundStructureSetupDraftDto? draft)
        => _setupService.PreviewAsync(ToRequest(draft)).GetAwaiter().GetResult();

    public async Task<FundStructureSetupCommitResultDto> CommitAsync(FundStructureSetupDraftDto draft, CancellationToken ct = default)
        => await _setupService.CommitAsync(new FundStructureSetupDraftRequest(draft), ct).ConfigureAwait(false);

    public async Task<FundStructureSetupResultDto> CreateAsync(FundStructureSetupDraftDto draft, CancellationToken ct = default)
    {
        var commit = await CommitAsync(draft, ct).ConfigureAwait(false);
        if (!commit.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", commit.ValidationSummary.Issues.Where(static issue => issue.IsBlocking).Select(static issue => issue.Message)));
        }

        var organization = commit.Entities.First(entity => entity.Alias == FundStructureSetupNodeAlias.Organization);
        var business = commit.Entities.First(entity => entity.Alias == FundStructureSetupNodeAlias.BusinessLane);
        var clientOrFund = commit.Entities.First(entity => entity.Alias == FundStructureSetupNodeAlias.ClientOrFund);
        var legalEntity = commit.Entities.First(entity => entity.Alias == FundStructureSetupNodeAlias.LegalEntity);
        var vehicle = commit.Entities.First(entity => entity.Alias == FundStructureSetupNodeAlias.Vehicle);
        var portfolio = commit.Entities.First(entity => entity.Alias == FundStructureSetupNodeAlias.InvestmentPortfolio);
        var effectiveFrom = draft.EffectiveFrom ?? DateTimeOffset.UtcNow;
        var currency = string.IsNullOrWhiteSpace(draft.Organization.BaseCurrency) ? "USD" : draft.Organization.BaseCurrency.Trim().ToUpperInvariant();

        return new FundStructureSetupResultDto(
            new OrganizationSummaryDto(organization.NodeId, organization.Code, organization.Name, currency, true, effectiveFrom, null, [], null),
            new BusinessSummaryDto(business.NodeId, organization.NodeId, draft.BusinessLane.BusinessKind, business.Code, business.Name, currency, true, effectiveFrom, null, [], [], [], null),
            clientOrFund.Kind == FundStructureNodeKindDto.Client ? new ClientSummaryDto(clientOrFund.NodeId, business.NodeId, clientOrFund.Code, clientOrFund.Name, currency, true, effectiveFrom, null, [], null, draft.ClientOrFund.ClientSegmentKind) : null,
            clientOrFund.Kind == FundStructureNodeKindDto.Fund ? new FundSummaryDto(clientOrFund.NodeId, business.NodeId, clientOrFund.Code, clientOrFund.Name, currency, true, effectiveFrom, null, [], [], [], [], [], null) : null,
            new LegalEntitySummaryDto(legalEntity.NodeId, draft.LegalEntity.EntityType, legalEntity.Code, legalEntity.Name, draft.LegalEntity.Jurisdiction, currency, true, effectiveFrom, null, null),
            new VehicleSummaryDto(vehicle.NodeId, clientOrFund.NodeId, legalEntity.NodeId, vehicle.Code, vehicle.Name, currency, true, effectiveFrom, null, [], [], null),
            new InvestmentPortfolioSummaryDto(portfolio.NodeId, business.NodeId, portfolio.Code, portfolio.Name, currency, true, effectiveFrom, null, null, clientOrFund.Kind == FundStructureNodeKindDto.Fund ? clientOrFund.NodeId : null, null, vehicle.NodeId, legalEntity.NodeId, [], null),
            commit.OwnershipLinks,
            commit.AccountHandoffAssignment!,
            commit.Graph,
            commit.ValidationSummary);
    }

    private static FundStructureSetupDraftRequest ToRequest(FundStructureSetupDraftDto? draft)
        => new(draft ?? EmptyDraft());

    private static FundStructureSetupDraftDto EmptyDraft()
        => new(
            new FundStructureSetupOrganizationDraftDto(null, string.Empty, string.Empty, string.Empty),
            new FundStructureSetupBusinessDraftDto(null, BusinessKindDto.FundManager, string.Empty, string.Empty, string.Empty),
            new FundStructureSetupClientOrFundDraftDto(null, null, false, string.Empty, string.Empty, string.Empty),
            new FundStructureSetupLegalEntityDraftDto(null, LegalEntityTypeDto.Fund, string.Empty, string.Empty, string.Empty, string.Empty),
            new FundStructureSetupVehicleDraftDto(null, string.Empty, string.Empty, string.Empty),
            new FundStructureSetupInvestmentPortfolioDraftDto(null, string.Empty, string.Empty, string.Empty),
            new FundStructureSetupAccountHandoffDraftDto(string.Empty, string.Empty, AccountTypeDto.Brokerage, string.Empty));
}
