using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Characterizes the shared projections through both persistence boundaries, including the
/// account/metadata reads that deliberately remain outside the pure projector.
/// </summary>
public sealed class FundStructureProjectionContractTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 01, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EffectiveFrom = AsOf.AddDays(-1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Views_PreserveAccountLinksLedgerAssignmentsAndSharedMetadata(bool postgres)
    {
        var metadata = new SharedDataAccess();
        var fixture = await SeedAsync(postgres, metadata);
        var service = fixture.Service;

        var organization = await service.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationId, AsOf: AsOf));
        Assert.Equal(2, organization.Businesses.Count);
        Assert.Contains(organization.Accounts, account => account.AccountId == fixture.LinkedAccountId);
        Assert.All(organization.InvestmentPortfolios,
            portfolio => Assert.Equal(metadata.Value, portfolio.SharedDataAccess));

        var advisory = await service.GetAdvisoryViewAsync(
            new AdvisoryStructureQuery(fixture.AdvisoryBusinessId, AsOf: AsOf));
        Assert.NotNull(advisory);
        var client = Assert.Single(advisory.Clients);
        Assert.Equal(fixture.AdvisoryPortfolioId, Assert.Single(client.InvestmentPortfolios).InvestmentPortfolioId);
        Assert.Equal(fixture.AdvisoryAccountId, Assert.Single(client.Accounts).AccountId);
        Assert.Equal(metadata.Value, advisory.SharedDataAccess);

        var operating = await service.GetFundOperatingViewAsync(
            new FundOperatingStructureQuery(fixture.FundBusinessId, AsOf: AsOf));
        Assert.NotNull(operating);
        var fund = Assert.Single(operating.Funds);
        Assert.Equal(fixture.FundId, fund.Fund.FundId);
        Assert.Contains(fund.Accounts, account => account.AccountId == fixture.LinkedAccountId);
        Assert.Equal(fixture.SleeveId, Assert.Single(fund.Sleeves).Sleeve.SleeveId);
        Assert.Equal(fixture.VehicleId, Assert.Single(fund.Vehicles).Vehicle.VehicleId);

        var accounting = await service.GetAccountingViewAsync(
            new AccountingStructureQuery(
                BusinessId: fixture.FundBusinessId,
                InvestmentPortfolioId: fixture.FundPortfolioId,
                AsOf: AsOf));
        Assert.Equal(fixture.LinkedAccountId, Assert.Single(accounting.Accounts).AccountId);
        var ledgerGroup = Assert.Single(accounting.LedgerGroups);
        Assert.Equal("ASSIGNED-BOOK", ledgerGroup.DisplayName);
        Assert.Equal(fixture.FundPortfolioId, Assert.Single(ledgerGroup.InvestmentPortfolioIds));
        Assert.Equal(metadata.Value, ledgerGroup.SharedDataAccess);
        Assert.Contains(accounting.Assignments, assignment => assignment.AssignmentId == fixture.AssignmentId);

        var compatibilityGraph = await service.GetFundStructureGraphAsync(
            new FundStructureQuery(FundId: fixture.FundId, AsOf: AsOf));
        Assert.Contains(compatibilityGraph.Nodes, node => node.NodeId == fixture.LinkedAccountId);
        Assert.DoesNotContain(compatibilityGraph.Nodes,
            node => node.Kind is FundStructureNodeKindDto.Organization
                or FundStructureNodeKindDto.Business or FundStructureNodeKindDto.InvestmentPortfolio);
        Assert.Equal(fixture.AssignmentId, Assert.Single(compatibilityGraph.Assignments).AssignmentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NodeFilters_TrimEdgesAndAssignmentsWithoutRemovingSummaryCollections(bool postgres)
    {
        var fixture = await SeedAsync(postgres);
        var graph = await fixture.Service.GetOrganizationStructureAsync(new OrganizationStructureQuery(
            OrganizationId: fixture.OrganizationId,
            NodeId: fixture.LinkedAccountId,
            NodeKind: FundStructureNodeKindDto.Account,
            AsOf: AsOf));

        Assert.Equal(fixture.LinkedAccountId, Assert.Single(graph.Nodes).NodeId);
        Assert.Empty(graph.OwnershipLinks);
        Assert.Equal(fixture.AssignmentId, Assert.Single(graph.Assignments).AssignmentId);
        Assert.Equal(fixture.OrganizationId, Assert.Single(graph.Organizations).OrganizationId);
        Assert.Equal(2, graph.InvestmentPortfolios.Count);

        var incompatible = await fixture.Service.GetFundStructureGraphAsync(new FundStructureQuery(
            FundId: fixture.FundId, NodeId: fixture.LinkedAccountId,
            NodeKind: FundStructureNodeKindDto.Fund, AsOf: AsOf));
        Assert.Empty(incompatible.Nodes);
        Assert.Empty(incompatible.OwnershipLinks);
        Assert.Empty(incompatible.Assignments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EffectiveWindows_KeepInclusiveAssignmentEndAndRejectMismatchedOrganization(bool postgres)
    {
        var fixture = await SeedAsync(postgres);
        var atEnd = await fixture.Service.GetAccountingViewAsync(
            new AccountingStructureQuery(BusinessId: fixture.FundBusinessId, AsOf: AsOf));
        Assert.Contains(atEnd.LedgerGroups, group => group.DisplayName == "ASSIGNED-BOOK");

        var afterEnd = await fixture.Service.GetAccountingViewAsync(
            new AccountingStructureQuery(BusinessId: fixture.FundBusinessId, AsOf: AsOf.AddTicks(1)));
        Assert.DoesNotContain(afterEnd.Assignments, assignment => assignment.AssignmentId == fixture.AssignmentId);
        Assert.DoesNotContain(afterEnd.LedgerGroups, group => group.DisplayName == "ASSIGNED-BOOK");
        Assert.Contains(afterEnd.LedgerGroups, group => group.DisplayName == "FALLBACK-BOOK");

        var beforeStart = await fixture.Service.GetFundStructureGraphAsync(
            new FundStructureQuery(FundId: fixture.FundId, AsOf: EffectiveFrom.AddTicks(-1)));
        Assert.Empty(beforeStart.Nodes);
        Assert.Null(await fixture.Service.GetAdvisoryViewAsync(
            new AdvisoryStructureQuery(fixture.AdvisoryBusinessId, OrganizationId: Guid.NewGuid(), AsOf: AsOf)));
        Assert.Null(await fixture.Service.GetFundOperatingViewAsync(
            new FundOperatingStructureQuery(fixture.FundBusinessId, OrganizationId: Guid.NewGuid(), AsOf: AsOf)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_UsesCapturedStructureWhileSharedMetadataReadIsPending(bool postgres)
    {
        var metadata = new SharedDataAccess { Block = true };
        var fixture = await SeedAsync(postgres, metadata);
        var pending = fixture.Service.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationId, AsOf: AsOf));
        await metadata.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var laterFundId = Guid.NewGuid();
        try
        {
            await fixture.Service.CreateFundAsync(new CreateFundRequest(
                laterFundId, "LATER", "Later fund", "USD", EffectiveFrom, "test",
                BusinessId: fixture.FundBusinessId));
        }
        finally
        {
            metadata.Release.TrySetResult();
        }

        var captured = await pending;
        Assert.Equal(fixture.FundId, Assert.Single(captured.Funds).FundId);
        Assert.DoesNotContain(laterFundId,
            Assert.Single(captured.Businesses, business => business.BusinessId == fixture.FundBusinessId).FundIds);

        var current = await fixture.Service.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationId, AsOf: AsOf));
        Assert.Contains(current.Funds, fund => fund.FundId == laterFundId);
    }

    private static async Task<Fixture> SeedAsync(bool postgres, SharedDataAccess? metadata = null)
    {
        var accounts = new InMemoryFundAccountService();
        IFundStructureService service = postgres
            ? new PostgresFundStructureService(new FakeFundStructureStore(), accounts,
                new FundStructurePolicyService(), metadata)
            : new InMemoryFundStructureService(accounts, metadata, persistencePath: null);
        var fixture = new Fixture(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var clientId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        await service.CreateOrganizationAsync(new CreateOrganizationRequest(
            fixture.OrganizationId, "ORG", "Organization", "USD", EffectiveFrom, "test"));
        await service.CreateBusinessAsync(new CreateBusinessRequest(
            fixture.AdvisoryBusinessId, fixture.OrganizationId, BusinessKindDto.FinancialAdvisor,
            "ADV", "Advisory", "USD", EffectiveFrom, "test"));
        await service.CreateBusinessAsync(new CreateBusinessRequest(
            fixture.FundBusinessId, fixture.OrganizationId, BusinessKindDto.FundManager,
            "FM", "Funds", "USD", EffectiveFrom, "test"));
        await service.CreateClientAsync(new CreateClientRequest(
            clientId, fixture.AdvisoryBusinessId, "CLIENT", "Client", "USD", EffectiveFrom, "test"));
        await service.CreateFundAsync(new CreateFundRequest(
            fixture.FundId, "FUND", "Fund", "USD", EffectiveFrom, "test",
            BusinessId: fixture.FundBusinessId));
        await service.CreateSleeveAsync(new CreateSleeveRequest(
            fixture.SleeveId, fixture.FundId, "SLEEVE", "Sleeve", EffectiveFrom, "test"));
        await service.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            entityId, LegalEntityTypeDto.Vehicle, "ENTITY", "Entity", "US", "USD", EffectiveFrom, "test"));
        await service.CreateVehicleAsync(new CreateVehicleRequest(
            fixture.VehicleId, fixture.FundId, entityId, "VEHICLE", "Vehicle", "USD", EffectiveFrom, "test"));
        await service.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            fixture.AdvisoryPortfolioId, fixture.AdvisoryBusinessId, "ADV-P", "Advisory portfolio",
            "USD", EffectiveFrom, "test", ClientId: clientId));
        await service.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            fixture.FundPortfolioId, fixture.FundBusinessId, "FUND-P", "Fund portfolio",
            "USD", EffectiveFrom, "test", FundId: fixture.FundId));

        await accounts.CreateAccountAsync(new CreateAccountRequest(
            fixture.AdvisoryAccountId, AccountTypeDto.Custody, "ADV-A", "Advisory account",
            "USD", EffectiveFrom, "test", PortfolioId: fixture.AdvisoryPortfolioId.ToString("D")));
        await accounts.CreateAccountAsync(new CreateAccountRequest(
            fixture.LinkedAccountId, AccountTypeDto.Custody, "LINK-A", "Linked account",
            "USD", EffectiveFrom, "test", LedgerReference: "FALLBACK-BOOK"));
        await service.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(), fixture.FundPortfolioId, fixture.LinkedAccountId,
            OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "test"));
        await service.AssignNodeAsync(new AssignFundStructureNodeRequest(
            fixture.AssignmentId, fixture.LinkedAccountId, "LedgerGroup", "assigned-book",
            EffectiveFrom, "test", IsPrimary: true, EffectiveTo: AsOf));
        return fixture;
    }

    private sealed record Fixture(
        IFundStructureService Service,
        Guid OrganizationId,
        Guid AdvisoryBusinessId,
        Guid FundBusinessId,
        Guid FundId,
        Guid SleeveId,
        Guid VehicleId,
        Guid AdvisoryPortfolioId,
        Guid FundPortfolioId,
        Guid AdvisoryAccountId,
        Guid LinkedAccountId,
        Guid AssignmentId);

    private sealed class SharedDataAccess : IGovernanceSharedDataAccessService
    {
        public FundStructureSharedDataAccessDto Value { get; } = new(
            new SecurityMasterAccessSummaryDto(true, "Available", true, true, true),
            new HistoricalPriceAccessSummaryDto(true, false, 0, [], "No stored prices"),
            new BackfillAccessSummaryDto(true, false, 1, null, null, null, null, null, 0, 0, "Available"));

        public bool Block { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<FundStructureSharedDataAccessDto> GetSharedDataAccessAsync(CancellationToken ct = default)
        {
            Entered.TrySetResult();
            if (Block)
                await Release.Task.WaitAsync(ct);
            return Value;
        }
    }
}
