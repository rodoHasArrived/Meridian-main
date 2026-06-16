using System.Net;
using System.Net.Http.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
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
            Institution: "Test Custodian"), CancellationToken.None);
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
            Institution: "Test Custodian"), CancellationToken.None);

        var service = new FamilyOfficeReadService(fundStructure, fundAccountService: null, timeProvider: new FixedTimeProvider(now));

        var overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal("Degraded", overview.Readiness.Status);
        Assert.Empty(overview.Accounts);
        Assert.Contains(overview.Readiness.Blockers, warning => warning.Contains("fund-account read service is unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overview.Readiness.Blockers, warning => warning.Contains("balances, cash, liabilities, reconciliation", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
