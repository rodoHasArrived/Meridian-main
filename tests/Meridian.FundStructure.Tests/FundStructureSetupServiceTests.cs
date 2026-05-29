using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Guards the onboarding setup workflow that commits a complete fund-structure draft without partial writes.
/// </summary>
public sealed class FundStructureSetupServiceTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scenario_NewFundSetup_CommitsValidatedDraftInDependencyOrder()
    {
        var structure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var setup = new FundStructureSetupService(structure);
        var request = CreateDraft();

        var preview = await setup.PreviewSetupAsync(request);
        var result = await setup.CommitSetupAsync(request);
        var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false));

        Assert.True(preview.IsValid);
        Assert.Equal(5, preview.CreateNodeCount);
        Assert.True(result.IsCommitted);
        Assert.Equal(5, result.CreatedNodeIds.Count);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Organization && node.Code == "SETUP-ORG");
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.InvestmentPortfolio && node.Code == "SETUP-PORT");
    }

    [Fact]
    public async Task Scenario_InvalidFundSetup_ReturnsValidationFailureWithoutPartialWrites()
    {
        var structure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var setup = new FundStructureSetupService(structure);
        var request = CreateDraft(links: [
            new FundStructureSetupLinkDraftDto("org", "business", OwnershipRelationshipTypeDto.Owns),
            new FundStructureSetupLinkDraftDto("business", "fund", OwnershipRelationshipTypeDto.Operates)
        ]);

        var result = await setup.CommitSetupAsync(request);
        var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.ValidationMessages, message => message.Code == "portfolio.business.required");
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Links);
    }

    [Fact]
    public async Task Scenario_RetryFundSetup_ReusesExistingEntitiesAndDoesNotDuplicateNodes()
    {
        var structure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var setup = new FundStructureSetupService(structure);
        var request = CreateDraft();

        var first = await setup.CommitSetupAsync(request);
        var retry = await setup.CommitSetupAsync(request);
        var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false));

        Assert.True(first.IsCommitted);
        Assert.True(retry.IsCommitted);
        Assert.Empty(retry.CreatedNodeIds);
        Assert.Equal(5, retry.ReusedNodeIds.Count);
        Assert.Equal(5, graph.Nodes.Count);
    }

    [Fact]
    public async Task Scenario_MixedExistingAndNewFundSetup_ReusesExistingOrganizationAndCreatesMissingChildren()
    {
        var structure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        await structure.CreateOrganizationAsync(new CreateOrganizationRequest(
            Guid.NewGuid(),
            "SETUP-ORG",
            "Setup Organization",
            "USD",
            EffectiveFrom,
            "test"));
        var setup = new FundStructureSetupService(structure);

        var result = await setup.CommitSetupAsync(CreateDraft());
        var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false));

        Assert.True(result.IsCommitted);
        Assert.Equal(4, result.CreatedNodeIds.Count);
        Assert.Single(result.ReusedNodeIds);
        Assert.Single(graph.Organizations);
        Assert.Contains(graph.Businesses, business => business.Code == "SETUP-BIZ");
    }

    private static FundStructureSetupDraftRequest CreateDraft(
        IReadOnlyList<FundStructureSetupLinkDraftDto>? links = null) => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        EffectiveFrom,
        "setup-test",
        [
            new FundStructureSetupNodeDraftDto("org", FundStructureNodeKindDto.Organization, "SETUP-ORG", "Setup Organization", "USD"),
            new FundStructureSetupNodeDraftDto("business", FundStructureNodeKindDto.Business, "SETUP-BIZ", "Setup Business", "USD", BusinessKind: BusinessKindDto.Hybrid),
            new FundStructureSetupNodeDraftDto("entity", FundStructureNodeKindDto.Entity, "SETUP-ENT", "Setup Fund LP", "USD", LegalEntityType: LegalEntityTypeDto.Fund, Jurisdiction: "Delaware"),
            new FundStructureSetupNodeDraftDto("fund", FundStructureNodeKindDto.Fund, "SETUP-FUND", "Setup Fund", "USD"),
            new FundStructureSetupNodeDraftDto("portfolio", FundStructureNodeKindDto.InvestmentPortfolio, "SETUP-PORT", "Setup Portfolio", "USD")
        ],
        links ?? [
            new FundStructureSetupLinkDraftDto("org", "business", OwnershipRelationshipTypeDto.Owns),
            new FundStructureSetupLinkDraftDto("business", "fund", OwnershipRelationshipTypeDto.Operates),
            new FundStructureSetupLinkDraftDto("business", "portfolio", OwnershipRelationshipTypeDto.Operates),
            new FundStructureSetupLinkDraftDto("fund", "portfolio", OwnershipRelationshipTypeDto.AllocatesTo),
            new FundStructureSetupLinkDraftDto("entity", "portfolio", OwnershipRelationshipTypeDto.Owns)
        ]);
}
