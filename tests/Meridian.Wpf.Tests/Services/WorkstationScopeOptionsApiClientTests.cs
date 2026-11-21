using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationScopeOptionsApiClientTests
{
    private static readonly Guid EntityId = Guid.Parse("911d0237-5b94-4d31-b594-9d2eec3098f3");
    private static readonly Guid FundId = Guid.Parse("2c4eaa0f-2768-4fe1-bdb2-b4136610891d");
    private static readonly Guid AccountId = Guid.Parse("6f1a4c2e-3b5d-4e7f-8a9b-0c1d2e3f4a5b");

    [Fact]
    public async Task GetScopeOptions_ReadsSharedRouteAndReportsCounts()
    {
        string? path = null;
        using var api = new ApiClientService(new Factory(new Handler(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            path = request.RequestUri!.AbsolutePath;
            return Json(new WorkstationScopeOptionsDto(
                [new WorkstationScopeEntityOptionDto(EntityId, "Northwind Income Fund LP")],
                [new WorkstationScopeFundOptionDto(FundId, "Northwind Income", [EntityId])],
                [new WorkstationScopeAccountOptionDto(AccountId, "Prime brokerage", FundId, EntityId)]));
        })));
        var fundStructure = Substitute.For<IFundStructureService>();

        var result = await new WorkstationScopeOptionsApiClient(api, fundStructure).GetScopeOptionsAsync();

        path.Should().Be(UiApiRoutes.WorkstationScopeOptions);
        result.Source.Should().Be(WorkstationScopeOptionsSource.ScopeOptionsRoute);
        result.IsAvailable.Should().BeTrue();
        result.Options.Entities.Should().ContainSingle().Which.Name.Should().Be("Northwind Income Fund LP");
        result.Options.Funds.Should().ContainSingle().Which.EntityIds.Should().Equal(EntityId);
        var account = result.Options.Accounts.Should().ContainSingle().Subject;
        account.FundId.Should().Be(FundId);
        account.EntityId.Should().Be(EntityId);
        result.StatusMessage.Should().Contain("1 entity").And.Contain("1 fund").And.Contain("1 account");
        _ = fundStructure.DidNotReceiveWithAnyArgs().GetOrganizationStructureAsync(default!, default);
    }

    [Fact]
    public async Task GetScopeOptions_EmptyPayloadSaysNothingIsVisible()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => Json(new WorkstationScopeOptionsDto([], [], [])))));

        var result = await new WorkstationScopeOptionsApiClient(api).GetScopeOptionsAsync();

        result.Source.Should().Be(WorkstationScopeOptionsSource.ScopeOptionsRoute);
        result.StatusMessage.Should().Contain("no entities, funds or accounts are visible to you");
    }

    [Fact]
    public async Task GetScopeOptions_On404_FallsBackToLocalFundStructureGraph()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        var fundStructure = Substitute.For<IFundStructureService>();
        OrganizationStructureQuery? observedQuery = null;
        fundStructure
            .GetOrganizationStructureAsync(Arg.Do<OrganizationStructureQuery>(query => observedQuery = query), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Graph()));

        var result = await new WorkstationScopeOptionsApiClient(api, fundStructure).GetScopeOptionsAsync();

        result.Source.Should().Be(WorkstationScopeOptionsSource.LocalFundStructureGraph);
        result.IsAvailable.Should().BeTrue();
        result.StatusMessage.Should().Contain("not filtered by your grants");
        observedQuery.Should().NotBeNull();
        observedQuery!.ActiveOnly.Should().BeTrue();
        result.Options.Entities.Should().ContainSingle().Which.EntityId.Should().Be(EntityId);
        result.Options.Funds.Should().ContainSingle().Which.EntityIds.Should().Equal(EntityId);
        var account = result.Options.Accounts.Should().ContainSingle().Subject;
        account.AccountId.Should().Be(AccountId);
        account.DisplayName.Should().Be(AccountId.ToString("D"), "a blank display name falls back to the id, as on the host");
        account.FundId.Should().Be(FundId);
        account.EntityId.Should().BeNull();
    }

    [Fact]
    public async Task GetScopeOptions_On404WithoutLocalGraph_ReportsUnavailable()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var result = await new WorkstationScopeOptionsApiClient(api).GetScopeOptionsAsync();

        result.Source.Should().Be(WorkstationScopeOptionsSource.Unavailable);
        result.Options.Accounts.Should().BeEmpty();
        result.StatusMessage.Should().Contain("does not serve scope options");
    }

    [Fact]
    public async Task GetScopeOptions_On403_ReportsRefusalAndDoesNotReadTheGraph()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var fundStructure = Substitute.For<IFundStructureService>();

        var result = await new WorkstationScopeOptionsApiClient(api, fundStructure).GetScopeOptionsAsync();

        result.Source.Should().Be(WorkstationScopeOptionsSource.Unavailable);
        result.StatusMessage.Should().Contain("refused").And.Contain("403");
        _ = fundStructure.DidNotReceiveWithAnyArgs().GetOrganizationStructureAsync(default!, default);
    }

    [Fact]
    public async Task GetScopeOptions_OnConnectionFailure_ReportsHostUnreachable()
    {
        using var api = new ApiClientService(new Factory(new Handler(_ => throw new HttpRequestException("connection refused"))));

        var result = await new WorkstationScopeOptionsApiClient(api).GetScopeOptionsAsync();

        result.Source.Should().Be(WorkstationScopeOptionsSource.Unavailable);
        result.StatusMessage.Should().Contain("could not be reached");
    }

    private static OrganizationStructureGraphDto Graph()
    {
        var from = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);
        return new OrganizationStructureGraphDto(
            Organizations: [],
            Businesses: [],
            Clients: [],
            Funds:
            [
                new FundSummaryDto(FundId, null, "NW-INCOME", "Northwind Income", "USD", true, from, null,
                    SleeveIds: [], VehicleIds: [], EntityIds: [EntityId], InvestmentPortfolioIds: [], AccountIds: [AccountId])
            ],
            Sleeves: [],
            Vehicles: [],
            Entities:
            [
                new LegalEntitySummaryDto(EntityId, LegalEntityTypeDto.Fund, "NW-ENT", "Northwind Income Fund LP", "Delaware", "USD", true, from, null)
            ],
            InvestmentPortfolios: [],
            Accounts:
            [
                new AccountSummaryDto(AccountId, AccountTypeDto.Brokerage, EntityId: null, FundId: FundId, SleeveId: null, VehicleId: null,
                    AccountCode: "NW-PB", DisplayName: " ", BaseCurrency: "USD", Institution: null, IsActive: true,
                    EffectiveFrom: from, EffectiveTo: null, PortfolioId: null, LedgerReference: null, StrategyId: null, RunId: null)
            ],
            Nodes: [],
            OwnershipLinks: [],
            Assignments: []);
    }

    private static HttpResponseMessage Json(WorkstationScopeOptionsDto value) => new(HttpStatusCode.OK)
    {
        // Serialized with the contract's own source-generated context, as the host writes it.
        Content = new StringContent(
            JsonSerializer.Serialize(value, WorkstationScopeOptionsJsonContext.Default.WorkstationScopeOptionsDto),
            Encoding.UTF8,
            "application/json")
    };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
