using System.Net;
using System.Net.Http.Json;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
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

    [Fact]
    public async Task GetOverviewAsync_UsesRetainedPrivateCapitalWithoutCombiningCurrenciesOrInventingMissingBalances()
    {
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        var accounts = new InMemoryFundAccountService();
        var structure = new InMemoryFundStructureService(accounts);
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var missingCommitment = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new(organizationId, "ORG", "Family Office", "USD", now.AddYears(-1), "test"));
        await structure.CreateBusinessAsync(new(businessId, organizationId, BusinessKindDto.Hybrid, "BUS", "Advisers", "USD", now.AddYears(-1), "test"));
        await structure.CreateClientAsync(new(familyId, businessId, "FAM", "Household", "USD", now.AddYears(-1), "test", ClientSegmentKind: ClientSegmentKind.FamilyOffice));
        await structure.CreateFundAsync(new(fundId, "FND", "Holdings", "USD", now.AddYears(-1), "test", BusinessId: businessId));
        await structure.CreateLegalEntityAsync(new(entityId, LegalEntityTypeDto.LimitedPartner, "LP", "Trust", "US-DE", "USD", now.AddYears(-1), "test"));
        await structure.LinkNodesAsync(new(Guid.NewGuid(), fundId, entityId, OwnershipRelationshipTypeDto.Owns, now.AddYears(-1), "test", OwnershipPercent: 100m));
        await structure.AssignNodeAsync(new(missingCommitment, entityId, "CapitalCommitment", "Missing vehicle", now.AddYears(-1), "test"));
        var retained = new RetainedPrivateSource(familyId.ToString("D"), entityId.ToString("D"));
        var service = new FamilyOfficeReadService(structure, accounts, timeProvider: new FixedTimeProvider(now), privateCapitalSource: retained);
        var overview = await service.GetOverviewAsync();
        Assert.Equal(3, overview.CapitalCommitments.Count);
        var missing = Assert.Single(overview.CapitalCommitments.Where(row => row.CommitmentId == missingCommitment.ToString("D")));
        Assert.False(missing.BalancesAvailable);
        Assert.Equal("Unavailable", missing.BalanceStatus);
        Assert.Equal(90m, overview.BalanceSheet.PrivateAssets);
        Assert.Equal(35m, overview.BalanceSheet.UnfundedCommitments);
        Assert.Single(overview.RecentCapitalActivity);
        Assert.Contains(overview.Readiness.Blockers, reason => reason.Contains("other currencies"));
        Assert.Contains(overview.Readiness.Blockers, reason => reason.Contains("unavailable, not zero"));
        var historical = await service.GetOverviewAsync(ownershipAsOf: DateOnly.FromDateTime(now.AddYears(-2).UtcDateTime));
        Assert.Equal(familyId.ToString("D"), historical.FamilyOfficeId);
        Assert.Equal(overview.AsOfDate, historical.AsOfDate);
        Assert.Equal(overview.CapitalCommitments, historical.CapitalCommitments);
        Assert.Equal(90m, historical.BalanceSheet.PrivateAssets);
        Assert.Contains(historical.Entities, entity => entity.EntityId == entityId.ToString("D"));
        Assert.Empty(historical.OwnershipGraph.Nodes);
        Assert.Contains(historical.Readiness.Blockers, reason => reason.Contains("financial population remains current"));
    }

    [Theory]
    [InlineData("Source absent.")]
    [InlineData("Input malformed.")]
    [InlineData("File unreadable.")]
    public async Task GetOverviewAsync_UnavailablePrivateSourceCannotClaimCompleteEvidenceFromEmptyLists(string warning)
    {
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        var accounts = new InMemoryFundAccountService();
        var structure = new InMemoryFundStructureService(accounts);
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new(organizationId, "ORG", "Family Office", "USD", now.AddYears(-1), "test"));
        await structure.CreateBusinessAsync(new(businessId, organizationId, BusinessKindDto.Hybrid, "BUS", "Advisers", "USD", now.AddYears(-1), "test"));
        await structure.CreateClientAsync(new(familyId, businessId, "FAM", "Household", "USD", now.AddYears(-1), "test", ClientSegmentKind: ClientSegmentKind.FamilyOffice));
        var service = new FamilyOfficeReadService(structure, accounts, timeProvider: new FixedTimeProvider(now),
            privateCapitalSource: new UnavailablePrivateSource(warning));
        var overview = await service.GetOverviewAsync();
        Assert.Empty(overview.CapitalCommitments);
        Assert.Equal("Partial", overview.BalanceSheet.EvidenceCompleteness);
        Assert.Equal("Partial", overview.Readiness.EvidenceCompleteness);
        Assert.Equal("NeedsReview", overview.BalanceSheet.ReconciliationStatus);
        Assert.Contains(warning, overview.Readiness.Blockers);
    }

    private sealed class UnavailablePrivateSource(string warning) : IPrivateCapitalLifecycleSource
    {
        public Task<PrivateCapitalLifecycleRead> ReadAsync(string familyOfficeId, IReadOnlySet<string> entityIds,
            DateOnly asOf, DateTimeOffset knownAt, CancellationToken ct = default,
            Func<DateOnly, CancellationToken, Task<IReadOnlySet<string>>>? historicalEntityScope = null)
            => Task.FromResult(new PrivateCapitalLifecycleRead([], [], [warning], IsAvailable: false));
    }

    private sealed class RetainedPrivateSource(string familyId, string entityId) : IPrivateCapitalLifecycleSource
    {
        public Task<PrivateCapitalLifecycleRead> ReadAsync(string familyOfficeId, IReadOnlySet<string> entityIds,
            DateOnly asOf, DateTimeOffset knownAt, CancellationToken ct = default,
            Func<DateOnly, CancellationToken, Task<IReadOnlySet<string>>>? historicalEntityScope = null)
        {
            Assert.Equal(familyId, familyOfficeId);
            Assert.Contains(entityId, entityIds);
            CapitalCommitmentDto Commitment(string id, string currency) => new(id, entityId, $"Vehicle {id}", "Growth", currency,
                100m, 80m, 35m, 15m, 90m, 2024, "manager-statement", "document-a", asOf, asOf, "Complete", "NotReconciled", null, null);
            var activity = new CapitalActivityDto("activity-a", "commitment-usd", entityId, "Distribution", "USD", 15m,
                asOf, null, null, "Settled", "manager-statement", "document-a", asOf, asOf, "Complete", "NotReconciled", null, null);
            return Task.FromResult(new PrivateCapitalLifecycleRead([Commitment("commitment-usd", "USD"), Commitment("commitment-eur", "EUR")], [activity], []));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
