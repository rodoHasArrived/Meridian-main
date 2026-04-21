using Meridian.Application.EnvironmentDesign;
using Meridian.Contracts.EnvironmentDesign;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class EnvironmentDesignerServiceTests
{
    [Fact]
    public async Task PublishAsync_ShouldPersistRuntimeAndPreserveStableNodeIdentityAcrossRepublishAndRollback()
    {
        var tempDirectory = CreateTempDirectory();
        var persistencePath = Path.Combine(tempDirectory, "environment-designer.json");

        try
        {
            var service = new EnvironmentDesignerService(persistencePath);
            var draft = await service.CreateDraftAsync(CreateDraftRequest("Advisory Practice Starter", includeAccount: true));

            var version1 = await service.PublishAsync(new EnvironmentPublishPlanDto(draft.DraftId, "test"));
            var fundRuntimeId = version1.RuntimeNodeMappings["flagship-fund"];
            var accountRuntimeId = version1.RuntimeNodeMappings["fund-account"];

            var updatedDefinition = draft.Definition with
            {
                Nodes =
                [
                    .. draft.Definition.Nodes.Select(node =>
                        string.Equals(node.NodeDefinitionId, "flagship-fund", StringComparison.OrdinalIgnoreCase)
                            ? node with { Name = "Northwind Flagship Fund II" }
                            : node),
                    new EnvironmentNodeDefinitionDto(
                        "investor-portfolio",
                        EnvironmentNodeKind.InvestmentPortfolio,
                        "PORT-001",
                        "Investor Managed Account",
                        "USD",
                        ParentNodeDefinitionId: "investor-client",
                        ParentRelationshipType: OwnershipRelationshipTypeDto.Owns,
                        LaneId: "advisory-lane")
                ]
            };

            var savedDraft = await service.SaveDraftAsync(draft with
            {
                Definition = updatedDefinition,
                UpdatedBy = "test"
            });

            var version2 = await service.PublishAsync(new EnvironmentPublishPlanDto(savedDraft.DraftId, "test"));
            Assert.Equal(fundRuntimeId, version2.RuntimeNodeMappings["flagship-fund"]);
            Assert.Equal(accountRuntimeId, version2.RuntimeNodeMappings["fund-account"]);
            Assert.Contains(version2.Runtime.ContextMappings, mapping => mapping.LaneId == "advisory-lane");

            var reloaded = new EnvironmentDesignerService(persistencePath);
            var current = await reloaded.GetCurrentPublishedVersionAsync(savedDraft.Definition.OrganizationId);

            Assert.NotNull(current);
            Assert.Equal(version2.VersionId, current!.VersionId);
            Assert.Equal(fundRuntimeId, current.RuntimeNodeMappings["flagship-fund"]);
            Assert.Contains(current.Runtime.OrganizationGraph.Clients, client => client.ClientSegmentKind == ClientSegmentKind.FamilyOffice);

            await reloaded.RollbackAsync(new RollbackEnvironmentVersionRequest(version1.VersionId, "test"));
            var rolledBack = await reloaded.GetCurrentPublishedVersionAsync(savedDraft.Definition.OrganizationId);

            Assert.NotNull(rolledBack);
            Assert.Equal(version1.VersionId, rolledBack!.VersionId);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldBlockRemovingPublishedAccountWithoutRemap()
    {
        var service = new EnvironmentDesignerService(persistencePath: null);
        var draft = await service.CreateDraftAsync(CreateDraftRequest("Destructive Delete Validation", includeAccount: true));
        await service.PublishAsync(new EnvironmentPublishPlanDto(draft.DraftId, "test"));

        var removedAccountDraft = await service.SaveDraftAsync(draft with
        {
            Definition = draft.Definition with
            {
                Nodes = draft.Definition.Nodes
                    .Where(node => !string.Equals(node.NodeDefinitionId, "fund-account", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            },
            UpdatedBy = "test"
        });

        var validation = await service.ValidateAsync(
            removedAccountDraft,
            new EnvironmentPublishPlanDto(removedAccountDraft.DraftId, "test"));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, issue =>
            issue.Code == "destructive-delete-blocked" &&
            string.Equals(issue.NodeDefinitionId, "fund-account", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListDraftsAsync_WithCancelledToken_ShouldThrow()
    {
        var service = new EnvironmentDesignerService(persistencePath: null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ListDraftsAsync(cts.Token));
    }

    private static CreateEnvironmentDraftRequest CreateDraftRequest(string name, bool includeAccount)
    {
        var organizationId = Guid.NewGuid();
        var nodes = new List<EnvironmentNodeDefinitionDto>
        {
            new("org-root", EnvironmentNodeKind.Organization, "ADV-ORG", "Northwind Advisory Group", "USD"),
            new("advisory-business", EnvironmentNodeKind.Business, "ADV-HYB", "Advisory Operating Shell", "USD",
                ParentNodeDefinitionId: "org-root",
                ParentRelationshipType: OwnershipRelationshipTypeDto.Owns,
                LaneId: "advisory-lane",
                BusinessKind: BusinessKindDto.Hybrid),
            new("investor-client", EnvironmentNodeKind.Client, "CLI-IND-001", "Individual Investor Mandate", "USD",
                ParentNodeDefinitionId: "advisory-business",
                ParentRelationshipType: OwnershipRelationshipTypeDto.Advises,
                LaneId: "advisory-lane",
                ClientSegmentKind: ClientSegmentKind.IndividualInvestor),
            new("family-office-client", EnvironmentNodeKind.Client, "CLI-FO-001", "Canyon Family Office", "USD",
                ParentNodeDefinitionId: "advisory-business",
                ParentRelationshipType: OwnershipRelationshipTypeDto.Advises,
                LaneId: "advisory-lane",
                ClientSegmentKind: ClientSegmentKind.FamilyOffice),
            new("flagship-fund", EnvironmentNodeKind.Fund, "FUND-001", "Northwind Flagship Fund", "USD",
                ParentNodeDefinitionId: "advisory-business",
                ParentRelationshipType: OwnershipRelationshipTypeDto.Operates,
                LaneId: "advisory-lane")
        };

        if (includeAccount)
        {
            nodes.Add(new EnvironmentNodeDefinitionDto(
                "fund-account",
                EnvironmentNodeKind.Account,
                "ACCT-FUND-001",
                "Prime Brokerage Main",
                "USD",
                ParentNodeDefinitionId: "flagship-fund",
                ParentRelationshipType: OwnershipRelationshipTypeDto.CustodiesFor,
                LaneId: "advisory-lane",
                AccountType: AccountTypeDto.PrimeBroker,
                Institution: "Northwind Prime",
                LedgerReference: "FUND-TB"));
        }

        return new CreateEnvironmentDraftRequest(
            DraftId: Guid.NewGuid(),
            Name: name,
            CreatedBy: "test",
            Definition: new OrganizationEnvironmentDefinitionDto(
                OrganizationId: organizationId,
                OrganizationNodeDefinitionId: "org-root",
                OrganizationCode: "ADV-ORG",
                OrganizationName: "Northwind Advisory Group",
                BaseCurrency: "USD",
                Lanes:
                [
                    new EnvironmentLaneDefinitionDto(
                        LaneId: "advisory-lane",
                        Name: "Advisory Practice",
                        Archetype: EnvironmentLaneArchetype.AdvisoryPractice,
                        DefaultWorkspaceId: "governance",
                        DefaultLandingPageTag: "GovernanceShell",
                        RootNodeDefinitionId: "advisory-business",
                        DefaultContextNodeDefinitionId: "advisory-business",
                        AllowedManagedScopeKinds:
                        [
                            EnvironmentManagedScopeKind.IndividualInvestor,
                            EnvironmentManagedScopeKind.FamilyOffice,
                            EnvironmentManagedScopeKind.Fund
                        ],
                        Description: "Hybrid advisory umbrella for individuals, family offices, and funds.")
                ],
                Nodes: nodes,
                Relationships: []),
            Notes: "Created by environment designer service tests.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-environment-designer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
