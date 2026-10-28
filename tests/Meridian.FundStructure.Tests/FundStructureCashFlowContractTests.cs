using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Cash-flow behavior shared by the local and PostgreSQL service facades. PostgreSQL storage is
/// faked here; these tests exercise scope selection and read orchestration, not database durability.
/// </summary>
public sealed class FundStructureCashFlowContractTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = new(2026, 04, 07, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashFlow_PreservesCurrencyWindowsAmountsAndSharedMetadata(bool postgres)
    {
        var metadata = new SharedDataAccess();
        var fixture = await SeedAsync(postgres, metadata);
        await RecordBalanceAsync(fixture, new DateOnly(2026, 04, 01), 10_000m);
        await RecordBalanceAsync(fixture, new DateOnly(2026, 04, 07), 11_000m);
        await IngestAsync(fixture,
            (new DateOnly(2026, 03, 31), -9_000m, "USD"),
            (new DateOnly(2026, 04, 01), 1_000m, "USD"),
            (new DateOnly(2026, 04, 07), -100m, "USD"),
            (new DateOnly(2026, 04, 08), 200m, "USD"),
            (new DateOnly(2026, 04, 14), -50m, "USD"),
            (new DateOnly(2026, 04, 15), 9_999m, "USD"),
            (new DateOnly(2026, 04, 10), 8_888m, "EUR"));

        var view = await fixture.Service.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.InvestmentPortfolio,
            InvestmentPortfolioId: fixture.PortfolioId, AsOf: AsOf, Currency: " usd ",
            HistoricalDays: 7, ForecastDays: 7, BucketDays: 3));

        Assert.NotNull(view);
        Assert.Equal(AsOf, view.AsOf);
        Assert.Equal("USD", view.Currency);
        Assert.Equal(new DateTimeOffset(2026, 04, 01, 0, 0, 0, TimeSpan.Zero), view.HistoricalWindowStart);
        Assert.Equal(new DateTimeOffset(2026, 04, 15, 0, 0, 0, TimeSpan.Zero), view.ProjectionWindowEnd);
        Assert.Equal(fixture.PortfolioId, view.Scope.InvestmentPortfolioId);
        Assert.Equal(fixture.AccountId, Assert.Single(view.Scope.AccountIds));
        Assert.Equal(1, view.AccountCount);
        Assert.Equal(11_000m, view.CurrentCashBalance);
        Assert.Equal(900m, view.RealizedLadder.NetPosition);
        Assert.Equal(150m, view.ProjectedLadder.NetPosition);
        Assert.Equal(-750m, view.VarianceSummary.VarianceAmount);
        Assert.Equal(11_150m, view.ProjectedClosingCashBalance);
        Assert.Equal(2, view.RealizedEntries.Count);
        Assert.Equal(2, view.ProjectedEntries.Count);
        Assert.All(view.RealizedEntries, entry => Assert.False(entry.IsProjected));
        Assert.All(view.ProjectedEntries, entry =>
        {
            Assert.True(entry.IsProjected);
            Assert.Equal("BankStatement", entry.SourceKind);
            Assert.Equal("USD", entry.Currency);
        });
        Assert.Same(metadata.Value, view.SharedDataAccess);
        var account = Assert.Single(view.Accounts);
        Assert.Same(metadata.Value, account.SharedDataAccess);
        Assert.False(account.UsedTrendFallback);
        Assert.False(account.UsedSecurityMasterRules);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashFlow_LabelsTrendFallbackAndKeepsPartialFinalBucket(bool postgres)
    {
        var fixture = await SeedAsync(postgres);
        await RecordBalanceAsync(fixture, new DateOnly(2026, 04, 01), 50_000m);
        await RecordBalanceAsync(fixture, new DateOnly(2026, 04, 07), 56_000m);

        var view = await fixture.Service.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account, AccountId: fixture.AccountId, AsOf: AsOf,
            HistoricalDays: 7, ForecastDays: 7, BucketDays: 3));

        Assert.NotNull(view);
        Assert.Equal(6_000m, view.RealizedLadder.NetPosition);
        Assert.Equal(7_000m, view.ProjectedLadder.NetPosition);
        Assert.Equal(1_000m, view.VarianceSummary.VarianceAmount);
        Assert.Equal(63_000m, view.ProjectedClosingCashBalance);
        Assert.True(Assert.Single(view.Accounts).UsedTrendFallback);
        Assert.All(view.ProjectedEntries, entry => Assert.Equal("BalanceTrend", entry.SourceKind));
        Assert.Equal(new[] { 3_000m, 3_000m, 1_000m }, view.ProjectedEntries.Select(entry => entry.Amount));
        Assert.Equal(3, view.ProjectedLadder.Buckets.Count);
        Assert.Equal(view.ProjectionWindowEnd, view.ProjectedLadder.Buckets[^1].BucketEnd);
        Assert.Null(view.SharedDataAccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashFlow_DoesNotIncludeAccountsWhoseOperatingPortfolioIsNotInScope(bool postgres)
    {
        var fixture = await SeedAsync(postgres);
        var other = await AddPortfolioAccountAsync(fixture, "OTHER");
        await fixture.Accounts.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            other.AccountId, new DateOnly(2026, 04, 07), "USD", 999_999m, "bank", "test"));
        await RecordBalanceAsync(fixture, new DateOnly(2026, 04, 07), 100m);

        var view = await fixture.Service.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.InvestmentPortfolio,
            InvestmentPortfolioId: fixture.PortfolioId, AsOf: AsOf));

        Assert.NotNull(view);
        Assert.Equal(fixture.AccountId, Assert.Single(view.Accounts).AccountId);
        Assert.Equal(100m, view.CurrentCashBalance);
        Assert.DoesNotContain(other.AccountId, view.Scope.AccountIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashFlow_UsesCapturedStructureWhileSharedMetadataReadIsPending(bool postgres)
    {
        var metadata = new SharedDataAccess { Block = true };
        var fixture = await SeedAsync(postgres, metadata);
        var query = new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Organization, OrganizationId: fixture.OrganizationId, AsOf: AsOf);
        var pending = fixture.Service.GetCashFlowViewAsync(query);
        await metadata.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Guid laterAccountId;
        try
        {
            var later = await AddPortfolioAccountAsync(fixture, "LATER");
            laterAccountId = later.AccountId;
        }
        finally
        {
            metadata.Release.TrySetResult();
        }

        var captured = await pending;
        Assert.NotNull(captured);
        Assert.Equal(fixture.AccountId, Assert.Single(captured.Accounts).AccountId);
        Assert.DoesNotContain(laterAccountId, captured.Scope.AccountIds);

        var current = await fixture.Service.GetCashFlowViewAsync(query);
        Assert.NotNull(current);
        Assert.Equal(2, current.AccountCount);
        Assert.Contains(current.Accounts, account => account.AccountId == laterAccountId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashFlow_UnknownScopeReturnsNull(bool postgres)
    {
        var fixture = await SeedAsync(postgres);

        Assert.Null(await fixture.Service.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Organization, OrganizationId: Guid.NewGuid(), AsOf: AsOf)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashFlow_RejectsForeignParentedAccountsEvenWhenAnotherParentIsVisible(bool failClosed)
    {
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        var accounts = new InMemoryFundAccountService();
        var posture = failClosed
            ? TenantScopeEnforcementOptions.FailClosed
            : TenantScopeEnforcementOptions.DeploymentBoundary;
        var alpha = new PostgresFundStructureService(store, accounts, new FundStructurePolicyService(),
            tenantAccessor: new TenantAccessor("alpha"), tenantScope: posture);
        var beta = new PostgresFundStructureService(store, accounts, new FundStructurePolicyService(),
            tenantAccessor: new TenantAccessor("beta"), tenantScope: posture);
        var alphaScope = await SeedFundAsync(alpha, "ALPHA");
        var betaScope = await SeedFundAsync(beta, "BETA");
        var alphaEntity = await alpha.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            Guid.NewGuid(), LegalEntityTypeDto.LimitedPartner, "ALPHA-LP", "Alpha entity", "US", "USD",
            EffectiveFrom, "test"));
        var mixed = await accounts.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(), AccountTypeDto.Custody, "MIXED", "Foreign fund, visible entity", "USD",
            EffectiveFrom, "test", FundId: betaScope.FundId, EntityId: alphaEntity.EntityId));
        var own = await accounts.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(), AccountTypeDto.Custody, "OWN", "Alpha account", "USD",
            EffectiveFrom, "test", FundId: alphaScope.FundId));
        await accounts.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            mixed.AccountId, new DateOnly(2026, 04, 07), "USD", 999_999m, "bank", "test"));
        await accounts.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            own.AccountId, new DateOnly(2026, 04, 07), "USD", 100m, "bank", "test"));

        var view = await alpha.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Organization, OrganizationId: alphaScope.OrganizationId, AsOf: AsOf));

        Assert.NotNull(view);
        Assert.Equal(own.AccountId, Assert.Single(view.Accounts).AccountId);
        Assert.Equal(100m, view.CurrentCashBalance);
        Assert.DoesNotContain(mixed.AccountId, view.Scope.AccountIds);
        Assert.Null(await alpha.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account, AccountId: mixed.AccountId, AsOf: AsOf)));
    }

    [Theory]
    [InlineData(false, -1, 7, 7)]
    [InlineData(true, -1, 7, 7)]
    [InlineData(false, 7, -1, 7)]
    [InlineData(true, 7, -1, 7)]
    [InlineData(false, 7, 7, 0)]
    [InlineData(true, 7, 7, 0)]
    [InlineData(false, FundStructurePolicyService.MaxCashFlowWindowDays + 1, 7, 7)]
    [InlineData(true, FundStructurePolicyService.MaxCashFlowWindowDays + 1, 7, 7)]
    [InlineData(false, 7, FundStructurePolicyService.MaxCashFlowWindowDays + 1, 7)]
    [InlineData(true, 7, FundStructurePolicyService.MaxCashFlowWindowDays + 1, 7)]
    [InlineData(false, 7, 7, FundStructurePolicyService.MaxCashFlowBucketDays + 1)]
    [InlineData(true, 7, 7, FundStructurePolicyService.MaxCashFlowBucketDays + 1)]
    public async Task CashFlow_InvalidWindowIsRejectedBeforeSharedReads(
        bool postgres, int historicalDays, int forecastDays, int bucketDays)
    {
        var metadata = new SharedDataAccess();
        var fixture = await SeedAsync(postgres, metadata);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.GetCashFlowViewAsync(
            new GovernanceCashFlowQuery(GovernanceCashFlowScopeKindDto.Account,
                AccountId: fixture.AccountId, AsOf: AsOf,
                HistoricalDays: historicalDays, ForecastDays: forecastDays, BucketDays: bucketDays)));
        Assert.False(metadata.Entered.Task.IsCompleted);
    }

    [Theory]
    [InlineData(false, 0, 7, 1, 7)]
    [InlineData(true, 0, 7, 1, 7)]
    [InlineData(false, 7, 0, 7, 1)]
    [InlineData(true, 7, 0, 7, 1)]
    [InlineData(false, 0, 0, 1, 1)]
    [InlineData(true, 0, 0, 1, 1)]
    public async Task CashFlow_ZeroDayWindowsRetainTheOneDayMinimum(
        bool postgres, int historicalDays, int forecastDays, int expectedHistoricalDays, int expectedForecastDays)
    {
        var fixture = await SeedAsync(postgres);

        var view = await fixture.Service.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account, AccountId: fixture.AccountId, AsOf: AsOf,
            HistoricalDays: historicalDays, ForecastDays: forecastDays));

        Assert.NotNull(view);
        Assert.Equal(expectedHistoricalDays, view.HistoricalDays);
        Assert.Equal(expectedForecastDays, view.ForecastDays);
        var asOfDay = new DateTimeOffset(2026, 04, 07, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(asOfDay.AddDays(1 - expectedHistoricalDays), view.HistoricalWindowStart);
        Assert.Equal(asOfDay.AddDays(1 + expectedForecastDays), view.ProjectionWindowEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CashFlow_PreCanceledQueryDoesNotReadSharedMetadata(bool postgres)
    {
        var metadata = new SharedDataAccess();
        var fixture = await SeedAsync(postgres, metadata);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.GetCashFlowViewAsync(
            new GovernanceCashFlowQuery(GovernanceCashFlowScopeKindDto.Account,
                AccountId: fixture.AccountId, AsOf: AsOf), cancellation.Token));
        Assert.False(metadata.Entered.Task.IsCompleted);
    }

    private static async Task<Fixture> SeedAsync(bool postgres, SharedDataAccess? metadata = null)
    {
        var accounts = new InMemoryFundAccountService();
        IFundStructureService service = postgres
            ? new PostgresFundStructureService(new FakeFundStructureStore(), accounts,
                new FundStructurePolicyService(), metadata)
            : new InMemoryFundStructureService(accounts, metadata, persistencePath: null);
        var fixture = new Fixture(service, accounts, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid());
        await service.CreateOrganizationAsync(new CreateOrganizationRequest(
            fixture.OrganizationId, "ORG", "Organization", "USD", EffectiveFrom, "test"));
        await service.CreateBusinessAsync(new CreateBusinessRequest(
            fixture.BusinessId, fixture.OrganizationId, BusinessKindDto.FinancialAdvisor,
            "ADV", "Advisory", "USD", EffectiveFrom, "test"));
        await service.CreateClientAsync(new CreateClientRequest(
            fixture.ClientId, fixture.BusinessId, "CLIENT", "Client", "USD", EffectiveFrom, "test"));
        await AddPortfolioAccountAsync(fixture, "CORE", fixture.PortfolioId, fixture.AccountId);
        return fixture;
    }

    private static async Task<(Guid PortfolioId, Guid AccountId)> AddPortfolioAccountAsync(
        Fixture fixture, string code, Guid? portfolioId = null, Guid? accountId = null)
    {
        var portfolio = portfolioId ?? Guid.NewGuid();
        var account = accountId ?? Guid.NewGuid();
        await fixture.Service.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            portfolio, fixture.BusinessId, code, $"{code} portfolio", "USD", EffectiveFrom, "test",
            ClientId: fixture.ClientId));
        await fixture.Accounts.CreateAccountAsync(new CreateAccountRequest(
            account, AccountTypeDto.Custody, $"{code}-A", $"{code} account", "USD", EffectiveFrom, "test",
            PortfolioId: portfolio.ToString("D"), LedgerReference: "CASH-BOOK"));
        await fixture.Service.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(), portfolio, account, OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "test"));
        return (portfolio, account);
    }

    private static Task<AccountBalanceSnapshotDto> RecordBalanceAsync(Fixture fixture, DateOnly date, decimal amount)
        => fixture.Accounts.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            fixture.AccountId, date, "USD", amount, "bank", "test"));

    private static async Task<(Guid OrganizationId, Guid FundId)> SeedFundAsync(
        IFundStructureService service, string code)
    {
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        await service.CreateOrganizationAsync(new CreateOrganizationRequest(
            organizationId, code, code, "USD", EffectiveFrom, "test"));
        await service.CreateBusinessAsync(new CreateBusinessRequest(
            businessId, organizationId, BusinessKindDto.FundManager, $"{code}-B", code, "USD", EffectiveFrom, "test"));
        await service.CreateFundAsync(new CreateFundRequest(
            fundId, $"{code}-F", code, "USD", EffectiveFrom, "test", BusinessId: businessId));
        return (organizationId, fundId);
    }

    private static Task<BankStatementBatchDto> IngestAsync(
        Fixture fixture, params (DateOnly Date, decimal Amount, string Currency)[] entries)
    {
        var batchId = Guid.NewGuid();
        return fixture.Accounts.IngestBankStatementAsync(new IngestBankStatementRequest(
            batchId, fixture.AccountId, new DateOnly(2026, 04, 07), "Test Bank", "cash.csv",
            entries.Select((entry, index) => new BankStatementLineDto(
                Guid.NewGuid(), batchId, fixture.AccountId, entry.Date, entry.Date, entry.Amount,
                entry.Currency, entry.Amount > 0m ? "Contribution" : "Fee", $"Cash {index}", $"REF-{index}", null))
                .ToArray(),
            "test"));
    }

    private sealed record Fixture(
        IFundStructureService Service,
        InMemoryFundAccountService Accounts,
        Guid OrganizationId,
        Guid BusinessId,
        Guid ClientId,
        Guid PortfolioId,
        Guid AccountId);

    private sealed class TenantAccessor(string tenantId) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenantId;
    }

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
