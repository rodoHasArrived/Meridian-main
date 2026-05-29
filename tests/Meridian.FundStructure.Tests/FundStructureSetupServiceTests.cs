using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;

namespace Meridian.FundStructure.Tests;

/// <summary>Guards the operator entity-onboarding workflow that previews, validates, reuses, and commits a fund structure before account handoff.</summary>
public sealed class FundStructureSetupServiceTests
{
    [Fact]
    public async Task CommitAsync_ValidOperatorSetup_CreatesDependencyOrderedStructureAndAssignment()
    {
        var structureService = CreateStructureService();
        var setupService = new FundStructureSetupService(structureService);

        var result = await setupService.CommitAsync(new FundStructureSetupDraftRequest(CreateDraft()));

        Assert.True(result.Succeeded);
        Assert.Equal(6, result.Entities.Count);
        Assert.All(result.Entities, entity => Assert.True(entity.WasCreated));
        Assert.NotNull(result.AccountHandoffAssignment);
        Assert.Equal(FundStructureSetupService.AccountHandoffAssignmentType, result.AccountHandoffAssignment!.AssignmentType);
        Assert.Contains(result.Graph.Nodes, node => node.Kind == FundStructureNodeKindDto.InvestmentPortfolio && node.Code == "PORT-A");
    }

    [Fact]
    public async Task CommitAsync_InvalidDraft_ReturnsValidationFailureWithoutPartialWrites()
    {
        var structureService = CreateStructureService();
        var setupService = new FundStructureSetupService(structureService);
        var invalid = CreateDraft() with
        {
            Organization = new FundStructureSetupOrganizationDraftDto(null, string.Empty, string.Empty, "US")
        };

        var result = await setupService.CommitAsync(new FundStructureSetupDraftRequest(invalid));
        var graph = await structureService.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false));

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationSummary.Issues, issue => issue.FieldPath == "organization.code");
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.OwnershipLinks);
        Assert.Empty(graph.Assignments);
    }

    [Fact]
    public async Task CommitAsync_RetriedDraft_ReusesExistingEntitiesAndAccountHandoff()
    {
        var structureService = CreateStructureService();
        var setupService = new FundStructureSetupService(structureService);
        var draft = CreateDraft();

        var first = await setupService.CommitAsync(new FundStructureSetupDraftRequest(draft));
        var second = await setupService.CommitAsync(new FundStructureSetupDraftRequest(draft));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.All(second.Entities, entity => Assert.False(entity.WasCreated));
        Assert.Equal(first.AccountHandoffAssignment!.AssignmentId, second.AccountHandoffAssignment!.AssignmentId);
    }

    [Fact]
    public async Task CommitAsync_MixedExistingAndNewDraft_ReusesExistingOrganizationAndCreatesRemainingNodes()
    {
        var structureService = CreateStructureService();
        var setupService = new FundStructureSetupService(structureService);
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var organization = await structureService.CreateOrganizationAsync(new CreateOrganizationRequest(
            Guid.NewGuid(), "ORG-A", "Organization A", "USD", now, "test"));

        var result = await setupService.CommitAsync(new FundStructureSetupDraftRequest(CreateDraft()));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Entities, entity => entity.Alias == FundStructureSetupNodeAlias.Organization && entity.NodeId == organization.OrganizationId && !entity.WasCreated);
        Assert.Contains(result.Entities, entity => entity.Alias == FundStructureSetupNodeAlias.BusinessLane && entity.WasCreated);
        Assert.Contains(result.Entities, entity => entity.Alias == FundStructureSetupNodeAlias.InvestmentPortfolio && entity.WasCreated);
    }

    private static InMemoryFundStructureService CreateStructureService()
        => new(new InMemoryFundAccountService());

    private static FundStructureSetupDraftDto CreateDraft()
        => new(
            new FundStructureSetupOrganizationDraftDto(null, "ORG-A", "Organization A", "USD"),
            new FundStructureSetupBusinessDraftDto(null, BusinessKindDto.FundManager, "BUS-A", "Investment Management A", "USD"),
            new FundStructureSetupClientOrFundDraftDto(null, null, CreateClient: false, "FUND-A", "Flagship Fund A", "USD"),
            new FundStructureSetupLegalEntityDraftDto(null, LegalEntityTypeDto.Fund, "LE-A", "Flagship LP A", "US-DE", "USD"),
            new FundStructureSetupVehicleDraftDto(null, "VEH-A", "Flagship Vehicle A", "USD"),
            new FundStructureSetupInvestmentPortfolioDraftDto(null, "PORT-A", "Core Portfolio A", "USD"),
            new FundStructureSetupAccountHandoffDraftDto("ACCT-A", "Primary brokerage handoff A", AccountTypeDto.Brokerage, "USD", "Broker", "4000"),
            InitialOwnershipLinks: Array.Empty<FundStructureSetupOwnershipLinkDraftDto>(),
            EffectiveFrom: new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero),
            RequestedBy: "test-operator");
}
