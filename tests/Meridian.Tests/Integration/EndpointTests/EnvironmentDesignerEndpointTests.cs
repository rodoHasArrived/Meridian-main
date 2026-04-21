using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.EnvironmentDesign;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class EnvironmentDesignerEndpointTests
{
    private readonly HttpClient _client;

    public EnvironmentDesignerEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task ListDrafts_WhenUsingStatusBackedEndpointMapping_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/environment-designer/drafts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var drafts = await response.Content.ReadFromJsonAsync<IReadOnlyList<EnvironmentDraftDto>>();
        drafts.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishFlow_CreatesVersionAndCurrentRuntime()
    {
        var request = CreateDraftRequest();

        var createResponse = await _client.PostAsJsonAsync("/api/environment-designer/drafts", request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var draft = await createResponse.Content.ReadFromJsonAsync<EnvironmentDraftDto>();
        draft.Should().NotBeNull();

        var validateResponse = await _client.PostAsJsonAsync(
            "/api/environment-designer/validate",
            new ValidateDraftEnvelope(draft!));
        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var validation = await validateResponse.Content.ReadFromJsonAsync<EnvironmentValidationResultDto>();
        validation.Should().NotBeNull();
        validation!.IsValid.Should().BeTrue();

        var publishPlan = new EnvironmentPublishPlanDto(
            DraftId: draft!.DraftId,
            PublishedBy: "endpoint-test",
            VersionLabel: "v001");

        var publishResponse = await _client.PostAsJsonAsync("/api/environment-designer/publish", publishPlan);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var published = await publishResponse.Content.ReadFromJsonAsync<PublishedEnvironmentVersionDto>();
        published.Should().NotBeNull();
        published!.OrganizationName.Should().Be("Northwind Advisory Group");
        published.Runtime.Nodes.Should().NotBeEmpty();

        var versionsCurrentResponse = await _client.GetAsync(
            $"/api/environment-designer/versions/current?organizationId={draft.Definition.OrganizationId:D}");
        versionsCurrentResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentVersion = await versionsCurrentResponse.Content.ReadFromJsonAsync<PublishedEnvironmentVersionDto>();
        currentVersion.Should().NotBeNull();
        currentVersion!.VersionId.Should().Be(published.VersionId);

        var runtimeCurrentResponse = await _client.GetAsync(
            $"/api/environment-designer/runtime/current?organizationId={draft.Definition.OrganizationId:D}");
        runtimeCurrentResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var runtime = await runtimeCurrentResponse.Content.ReadFromJsonAsync<PublishedEnvironmentRuntimeDto>();
        runtime.Should().NotBeNull();
        runtime!.Lanes.Should().ContainSingle(lane => lane.LaneId == "advisory-lane");
        runtime.ContextMappings.Should().NotBeEmpty();
    }

    private static CreateEnvironmentDraftRequest CreateDraftRequest()
    {
        var organizationId = Guid.NewGuid();
        return new CreateEnvironmentDraftRequest(
            DraftId: Guid.NewGuid(),
            Name: "Endpoint Advisory Starter",
            CreatedBy: "endpoint-test",
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
                        Description: "Hybrid advisory umbrella serving individuals, family offices, and funds." )
                ],
                Nodes:
                [
                    new EnvironmentNodeDefinitionDto("org-root", EnvironmentNodeKind.Organization, "ADV-ORG", "Northwind Advisory Group", "USD"),
                    new EnvironmentNodeDefinitionDto("advisory-business", EnvironmentNodeKind.Business, "ADV-HYB", "Advisory Operating Shell", "USD",
                        ParentNodeDefinitionId: "org-root",
                        ParentRelationshipType: OwnershipRelationshipTypeDto.Owns,
                        BusinessKind: BusinessKindDto.Hybrid,
                        LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("investor-client", EnvironmentNodeKind.Client, "CLI-IND-001", "Individual Investor Mandate", "USD",
                        ParentNodeDefinitionId: "advisory-business",
                        ParentRelationshipType: OwnershipRelationshipTypeDto.Advises,
                        ClientSegmentKind: ClientSegmentKind.IndividualInvestor,
                        LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("family-office-client", EnvironmentNodeKind.Client, "CLI-FO-001", "Canyon Family Office", "USD",
                        ParentNodeDefinitionId: "advisory-business",
                        ParentRelationshipType: OwnershipRelationshipTypeDto.Advises,
                        ClientSegmentKind: ClientSegmentKind.FamilyOffice,
                        LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("flagship-fund", EnvironmentNodeKind.Fund, "FUND-001", "Northwind Flagship Fund", "USD",
                        ParentNodeDefinitionId: "advisory-business",
                        ParentRelationshipType: OwnershipRelationshipTypeDto.Operates,
                        LaneId: "advisory-lane"),
                    new EnvironmentNodeDefinitionDto("fund-account", EnvironmentNodeKind.Account, "ACCT-FUND-001", "Prime Brokerage Main", "USD",
                        ParentNodeDefinitionId: "flagship-fund",
                        ParentRelationshipType: OwnershipRelationshipTypeDto.CustodiesFor,
                        LaneId: "advisory-lane",
                        AccountType: AccountTypeDto.PrimeBroker,
                        Institution: "Northwind Prime",
                        LedgerReference: "FUND-TB")
                ],
                Relationships: []),
            Notes: "Created by endpoint tests.");
    }

    private sealed record ValidateDraftEnvelope(
        EnvironmentDraftDto Draft,
        EnvironmentPublishPlanDto? PublishPlan = null);
}
