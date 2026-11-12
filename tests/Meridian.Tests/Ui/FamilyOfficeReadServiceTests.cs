using System.Net;
using System.Net.Http.Json;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using NSubstitute;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class FamilyOfficeReadServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_WhenNoFamilyOfficeConfigured_ReturnsEmptyStateGuidance()
    {
        var fundAccounts = new InMemoryFundAccountService();
        var fundStructure = new InMemoryFundStructureService(fundAccounts);
        var service = new FamilyOfficeReadService(fundStructure, fundAccounts);

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal("NotConfigured", overview.Readiness.Status);
        Assert.Empty(overview.Entities);
        Assert.Contains("Configure a FamilyOffice client", overview.Readiness.Blockers[0]);
        Assert.Equal("Missing", overview.BalanceSheet.EvidenceCompleteness);
    }

    [Fact]
    public async Task GetOverviewAsync_AssemblesBalancesOwnershipAndDegradedWarnings()
    {
        var fundAccounts = new InMemoryFundAccountService();
        var fundStructure = new InMemoryFundStructureService(fundAccounts);
        var now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        await fundStructure.CreateOrganizationAsync(new CreateOrganizationRequest(
            organizationId, "ORG", "Meridian Family Office", "USD", now.AddYears(-1), "test"), CancellationToken.None);
        await fundStructure.CreateBusinessAsync(new CreateBusinessRequest(
            businessId, organizationId, BusinessKindDto.Hybrid, "SFO", "SFO Advisers", "USD", now.AddYears(-1), "test"), CancellationToken.None);
        await fundStructure.CreateClientAsync(new CreateClientRequest(
            clientId, businessId, "FAM", "Founders Family", "USD", now.AddYears(-1), "test", ClientSegmentKind: ClientSegmentKind.FamilyOffice), CancellationToken.None);
        await fundStructure.CreateFundAsync(new CreateFundRequest(
            fundId, "FND", "Founders Holdings", "USD", now.AddYears(-1), "test", BusinessId: businessId), CancellationToken.None);
        await fundStructure.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            entityId, LegalEntityTypeDto.LimitedPartner, "TRUST", "Founders Trust", "US-DE", "USD", now.AddYears(-1), "test"), CancellationToken.None);
        await fundStructure.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(), fundId, entityId, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", OwnershipPercent: 100m), CancellationToken.None);
        await fundStructure.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            portfolioId, businessId, "FAM-PORT", "Family portfolio", "USD", now.AddYears(-1), "test",
            FundId: fundId, EntityId: entityId), CancellationToken.None);
        await fundStructure.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(), clientId, portfolioId, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", OwnershipPercent: 100m), CancellationToken.None);

        await fundAccounts.CreateAccountAsync(new CreateAccountRequest(
            accountId,
            AccountTypeDto.Custody,
            "CUST-1",
            "Main Custody",
            "USD",
            now.AddYears(-1),
            "test",
            FundId: fundId,
            EntityId: entityId,
            Institution: "Test Custodian", PortfolioId: portfolioId.ToString("D")), CancellationToken.None);
        await fundAccounts.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            accountId,
            new DateOnly(2026, 5, 25),
            "USD",
            CashBalance: 250_000m,
            Source: "custodian",
            SecuritiesMarketValue: 750_000m,
            AccruedInterest: 1_250m,
            ExternalReference: "statement-20260525"), CancellationToken.None);

        var service = new FamilyOfficeReadService(fundStructure, fundAccounts, timeProvider: new FixedTimeProvider(now));

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(clientId.ToString("D"), overview.FamilyOfficeId);
        Assert.Contains(overview.Entities, entity => entity.EntityId == entityId.ToString("D"));
        Assert.Contains(overview.OwnershipGraph.Edges, edge => edge.OwnershipPercent == 100m);
        Assert.Single(overview.Accounts);
        Assert.Equal(250_000m, overview.BalanceSheet.CashAndEquivalents);
        Assert.Equal(750_000m, overview.BalanceSheet.MarketableSecurities);
        Assert.Equal("Degraded", overview.Readiness.Status);
        Assert.Contains(overview.Readiness.Blockers, warning => warning.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overview.Readiness.Blockers, warning => warning.Contains("ownership", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overview.Readiness.Blockers, warning => warning.Contains("unreconciled", StringComparison.OrdinalIgnoreCase));

        var date = new DateOnly(2026, 5, 25);
        var batch = Guid.NewGuid();
        await fundAccounts.IngestBankStatementAsync(new(batch, accountId, date, "Bank", null,
            [new(Guid.NewGuid(), batch, accountId, date, date, 249_000m, "USD", "credit", "closing", null, 249_000m)], "ops"));
        var original = await fundAccounts.ReconcileAccountAsync(new(accountId, date, "ops"));
        var broken = await service.GetOverviewAsync(CancellationToken.None);
        Assert.Equal("OpenBreaks", Assert.Single(broken.Accounts).ReconciliationStatus);
        Assert.Equal(1, broken.Readiness.OpenExceptionCount);

        await fundAccounts.RecordBalanceSnapshotAsync(new(accountId, date, "USD", 249_000m, "custodian",
            SecuritiesMarketValue: 750_000m, ExternalReference: "statement-corrected"));
        await fundAccounts.ReconcileAccountAsync(new(accountId, date, "ops"));
        var corrected = await service.GetOverviewAsync(CancellationToken.None);
        Assert.Equal("Reconciled", Assert.Single(corrected.Accounts).ReconciliationStatus);
        Assert.Equal(0, corrected.Readiness.OpenExceptionCount);
        Assert.DoesNotContain(corrected.Readiness.Blockers, warning => warning.Contains("unreconciled", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, (await fundAccounts.GetReconciliationRunsAsync(accountId))
            .Single(run => run.ReconciliationRunId == original.ReconciliationRunId).TotalBreaks);
    }

    [Fact]
    public async Task GetOverviewAsync_WhenAccountsExistWithoutFundAccountReadService_ReturnsDegradedGuidance()
    {
        var fundAccounts = new InMemoryFundAccountService();
        var fundStructure = new InMemoryFundStructureService(fundAccounts);
        var now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        await fundStructure.CreateOrganizationAsync(new CreateOrganizationRequest(
            organizationId, "ORG", "Meridian Family Office", "USD", now.AddYears(-1), "test"), CancellationToken.None);
        await fundStructure.CreateBusinessAsync(new CreateBusinessRequest(
            businessId, organizationId, BusinessKindDto.Hybrid, "SFO", "SFO Advisers", "USD", now.AddYears(-1), "test"), CancellationToken.None);
        await fundStructure.CreateClientAsync(new CreateClientRequest(
            clientId, businessId, "FAM", "Founders Family", "USD", now.AddYears(-1), "test", ClientSegmentKind: ClientSegmentKind.FamilyOffice), CancellationToken.None);
        await fundStructure.CreateFundAsync(new CreateFundRequest(
            fundId, "FND", "Founders Holdings", "USD", now.AddYears(-1), "test", BusinessId: businessId), CancellationToken.None);
        await fundStructure.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            entityId, LegalEntityTypeDto.LimitedPartner, "TRUST", "Founders Trust", "US-DE", "USD", now.AddYears(-1), "test"), CancellationToken.None);
        await fundStructure.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(), fundId, entityId, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", OwnershipPercent: 100m), CancellationToken.None);
        await fundStructure.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            portfolioId, businessId, "FAM-PORT", "Family portfolio", "USD", now.AddYears(-1), "test",
            FundId: fundId, EntityId: entityId), CancellationToken.None);
        await fundStructure.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(), clientId, portfolioId, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", OwnershipPercent: 100m), CancellationToken.None);
        await fundAccounts.CreateAccountAsync(new CreateAccountRequest(
            accountId,
            AccountTypeDto.Custody,
            "CUST-1",
            "Main Custody",
            "USD",
            now.AddYears(-1),
            "test",
            FundId: fundId,
            EntityId: entityId,
            Institution: "Test Custodian", PortfolioId: portfolioId.ToString("D")), CancellationToken.None);

        var service = new FamilyOfficeReadService(fundStructure, fundAccountService: null, timeProvider: new FixedTimeProvider(now));

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal("Degraded", overview.Readiness.Status);
        Assert.Empty(overview.Accounts);
        Assert.Contains(overview.Readiness.Blockers, warning => warning.Contains("fund-account read service is unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overview.Readiness.Blockers, warning => warning.Contains("balances, cash, liabilities, reconciliation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetOverviewAsync_RequiresClientSelectionAndDoesNotExpandScopeThroughOwnershipLinks()
    {
        var accounts = new InMemoryFundAccountService();
        var structure = new InMemoryFundStructureService(accounts);
        var now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");
        var org = Guid.NewGuid();
        var business = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new(org, "ORG", "Organization", "USD", now.AddYears(-1), "test"));
        await structure.CreateBusinessAsync(new(business, org, BusinessKindDto.Hybrid, "B", "Business", "USD", now.AddYears(-1), "test"));
        var clientIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var entityIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var fundIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var portfolioIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Guid selectedLinkId = Guid.Empty;
        for (var index = 0; index < 2; index++)
        {
            await structure.CreateClientAsync(new(clientIds[index], business, $"C{index}", $"Family {index}", "USD", now.AddYears(-1), "test", ClientSegmentKind: ClientSegmentKind.FamilyOffice));
            await structure.CreateFundAsync(new(fundIds[index], $"F{index}", $"Fund {index}", "USD", now.AddYears(-1), "test", BusinessId: business));
            await structure.CreateLegalEntityAsync(new(entityIds[index], LegalEntityTypeDto.LimitedPartner, $"E{index}", $"Entity {index}", "US", "USD", now.AddYears(-1), "test"));
            await structure.LinkNodesAsync(new(Guid.NewGuid(), fundIds[index], entityIds[index], OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", index == 0 ? 100m : 60m));
            await structure.CreateInvestmentPortfolioAsync(new(portfolioIds[index], business, $"P{index}", $"Portfolio {index}", "USD", now.AddYears(-1), "test", FundId: fundIds[index], EntityId: entityIds[index]));
            var link = await structure.LinkNodesAsync(new(Guid.NewGuid(), clientIds[index], portfolioIds[index], OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", 60m, Notes: "Operator notes, not a document id"));
            if (index == 0)
                selectedLinkId = link.OwnershipLinkId;
        }
        // This is legal ownership evidence, not permission to include the sibling entity.
        await structure.LinkNodesAsync(new(Guid.NewGuid(), fundIds[0], entityIds[1], OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", 40m));
        await structure.ExpireOwnershipLinkAsync(new(selectedLinkId, now.AddYears(1), "test", "Operator notes, not a document id"));
        var graph = await structure.GetOrganizationStructureAsync(new(ActiveOnly: true, AsOf: now));
        // A valid read can retain the fund operating parent and legal Client -> Portfolio
        // association without redundant client metadata. The direct retained link is decisive.
        graph = graph with
        {
            Clients = graph.Clients.Select(client => client with { InvestmentPortfolioIds = [] }).ToArray(),
            InvestmentPortfolios = graph.InvestmentPortfolios.Select(portfolio => portfolio with
            {
                ClientId = null,
                FundId = fundIds[Array.IndexOf(portfolioIds, portfolio.InvestmentPortfolioId)],
                SleeveId = null,
                VehicleId = null
            }).ToArray()
        };
        var reader = Substitute.For<IFundStructureService>();
        reader.GetOrganizationStructureAsync(Arg.Any<OrganizationStructureQuery>(), Arg.Any<CancellationToken>()).Returns(graph);
        var service = new FamilyOfficeReadService(reader, accounts, timeProvider: new FixedTimeProvider(now));
        var unselected = await service.GetOverviewAsync();
        Assert.Empty(unselected.Entities);
        Assert.Equal(2, unselected.AvailableClients!.Count);
        var selected = await service.GetOverviewAsync(familyOfficeId: clientIds[0]);
        Assert.Contains(selected.OwnershipGraph.Nodes, node => node.NodeId == fundIds[0].ToString("D"));
        Assert.Contains(selected.OwnershipGraph.Nodes, node => node.NodeId == portfolioIds[0].ToString("D"));
        Assert.DoesNotContain(selected.OwnershipGraph.Nodes, node => node.NodeId == entityIds[1].ToString("D"));
        Assert.DoesNotContain(selected.OwnershipGraph.Nodes, node => node.NodeId == fundIds[1].ToString("D"));
        Assert.DoesNotContain(selected.OwnershipGraph.Edges, edge => edge.TargetNodeId == entityIds[1].ToString("D"));
        Assert.All(selected.OwnershipGraph.Edges, edge =>
        {
            Assert.Contains(selected.OwnershipGraph.Nodes, node => node.NodeId == edge.SourceNodeId);
            Assert.Contains(selected.OwnershipGraph.Nodes, node => node.NodeId == edge.TargetNodeId);
        });
        var retained = Assert.Single(selected.OwnershipGraph.Edges.Where(edge => edge.SourceNodeId == clientIds[0].ToString("D") && edge.TargetNodeId == portfolioIds[0].ToString("D")));
        Assert.Null(retained.SourceDocumentId);
        Assert.Equal("Operator notes, not a document id", retained.Notes);
        Assert.Equal(DateOnly.FromDateTime(now.AddYears(-1).UtcDateTime), retained.EffectiveFrom);
        Assert.Equal(DateOnly.FromDateTime(now.AddYears(1).UtcDateTime), retained.EffectiveTo);
        Assert.Equal(60m, retained.OwnershipPercent);
        Assert.Contains(selected.Readiness.Blockers, warning => warning.Contains("boundary"));
        Assert.Empty((await service.GetOverviewAsync(familyOfficeId: Guid.NewGuid())).Entities);
    }

    [Fact]
    public async Task GetOverviewAsync_SharedFundAndEntityDoNotAdmitSiblingClientAccounts()
    {
        var accounts = new InMemoryFundAccountService();
        var structure = new InMemoryFundStructureService(accounts);
        var now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");
        var org = Guid.NewGuid();
        var business = Guid.NewGuid();
        var fund = Guid.NewGuid();
        var entity = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new(org, "ORG", "Organization", "USD", now.AddYears(-1), "test"));
        await structure.CreateBusinessAsync(new(business, org, BusinessKindDto.Hybrid, "B", "Business", "USD", now.AddYears(-1), "test"));
        await structure.CreateFundAsync(new(fund, "F", "Shared fund", "USD", now.AddYears(-1), "test", BusinessId: business));
        await structure.CreateLegalEntityAsync(new(entity, LegalEntityTypeDto.LimitedPartner, "E", "Shared entity", "US", "USD", now.AddYears(-1), "test"));
        await structure.LinkNodesAsync(new(Guid.NewGuid(), fund, entity, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", 100m));
        var clients = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var accountIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        for (var index = 0; index < 2; index++)
        {
            var portfolio = Guid.NewGuid();
            await structure.CreateClientAsync(new(clients[index], business, $"C{index}", $"Family {index}", "USD", now.AddYears(-1), "test", ClientSegmentKind: ClientSegmentKind.FamilyOffice));
            await structure.CreateInvestmentPortfolioAsync(new(portfolio, business, $"P{index}", $"Portfolio {index}", "USD", now.AddYears(-1), "test", FundId: fund, EntityId: entity));
            await structure.LinkNodesAsync(new(Guid.NewGuid(), clients[index], portfolio, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", 60m));
            await accounts.CreateAccountAsync(new(accountIds[index], AccountTypeDto.Custody, $"A{index}", $"Account {index}", "USD", now.AddYears(-1), "test", FundId: fund, EntityId: entity, PortfolioId: portfolio.ToString("D")));
            await accounts.RecordBalanceSnapshotAsync(new(accountIds[index], new DateOnly(2026, 5, 29), "USD", index == 0 ? 100m : 900m, "synthetic-source", ExternalReference: $"statement-{index}"));
        }
        var result = await new FamilyOfficeReadService(structure, accounts, timeProvider: new FixedTimeProvider(now)).GetOverviewAsync(familyOfficeId: clients[0]);
        Assert.Equal(accountIds[0].ToString("D"), Assert.Single(result.Accounts).AccountId);
        Assert.Equal(100m, result.BalanceSheet.CashAndEquivalents);
        Assert.DoesNotContain(result.OwnershipGraph.Nodes, node => node.NodeId == accountIds[1].ToString("D"));
        Assert.DoesNotContain(result.PublicAssets, asset => asset.AccountId == accountIds[1].ToString("D"));
        Assert.Contains(result.Readiness.Blockers, warning => warning.Contains("unweighted"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetOverviewAsync_RetainsSelectedVehicleOrSleeveContextWithoutWideningAccounts(bool useVehicle)
    {
        var accounts = new InMemoryFundAccountService();
        var structure = new InMemoryFundStructureService(accounts);
        var now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");
        var org = Guid.NewGuid();
        var business = Guid.NewGuid();
        var client = Guid.NewGuid();
        var fund = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var portfolio = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new(org, "ORG", "Organization", "USD", now.AddYears(-1), "test"));
        await structure.CreateBusinessAsync(new(business, org, BusinessKindDto.Hybrid, "B", "Business", "USD", now.AddYears(-1), "test"));
        await structure.CreateClientAsync(new(client, business, "C", "Family", "USD", now.AddYears(-1), "test", ClientSegmentKind: ClientSegmentKind.FamilyOffice));
        await structure.CreateFundAsync(new(fund, "F", "Fund", "USD", now.AddYears(-1), "test", BusinessId: business));
        await structure.CreateLegalEntityAsync(new(entity, LegalEntityTypeDto.LimitedPartner, "E", "Entity", "US", "USD", now.AddYears(-1), "test"));
        if (useVehicle)
            await structure.CreateVehicleAsync(new(parent, fund, entity, "V", "Vehicle", "USD", now.AddYears(-1), "test"));
        else
            await structure.CreateSleeveAsync(new(parent, fund, "S", "Sleeve", now.AddYears(-1), "test"));
        await structure.CreateInvestmentPortfolioAsync(new(portfolio, business, "P", "Portfolio", "USD", now.AddYears(-1), "test", SleeveId: useVehicle ? null : parent, VehicleId: useVehicle ? parent : null, EntityId: entity));
        await structure.LinkNodesAsync(new(Guid.NewGuid(), client, portfolio, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", 100m));
        var result = await new FamilyOfficeReadService(structure, accounts, timeProvider: new FixedTimeProvider(now)).GetOverviewAsync();
        Assert.Contains(result.OwnershipGraph.Nodes, node => node.NodeId == parent.ToString("D") && node.NodeType == (useVehicle ? "Vehicle" : "Sleeve"));
        Assert.Contains(result.OwnershipGraph.Nodes, node => node.NodeId == fund.ToString("D"));
        Assert.Contains(result.OwnershipGraph.Edges, edge => edge.SourceNodeId == parent.ToString("D") && edge.TargetNodeId == portfolio.ToString("D"));
        Assert.Empty(result.Accounts);
        Assert.All(result.OwnershipGraph.Edges, edge =>
        {
            Assert.Contains(result.OwnershipGraph.Nodes, node => node.NodeId == edge.SourceNodeId);
            Assert.Contains(result.OwnershipGraph.Nodes, node => node.NodeId == edge.TargetNodeId);
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
