using System.Text.Json;
using Meridian.Application.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class InMemoryFundStructureServiceTests
{
    [Fact]
    public async Task FundAccountService_PersistsAccountsAndBalanceSnapshotsAcrossRestart()
    {
        var tempDirectory = CreateTempDirectory();
        var persistencePath = Path.Combine(tempDirectory, "fund-accounts.json");
        var now = new DateTimeOffset(2026, 04, 07, 15, 30, 0, TimeSpan.Zero);
        var accountId = Guid.NewGuid();

        try
        {
            var originalService = new InMemoryFundAccountService(persistencePath);
            await originalService.CreateAccountAsync(new CreateAccountRequest(
                AccountId: accountId,
                AccountType: AccountTypeDto.Brokerage,
                AccountCode: "ADV-ACCT-001",
                DisplayName: "Acme Advisory Custody",
                BaseCurrency: "USD",
                EffectiveFrom: now,
                CreatedBy: "test",
                PortfolioId: Guid.NewGuid().ToString("D"),
                LedgerReference: "ADVISORY-TB"));
            await originalService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                AccountId: accountId,
                AsOfDate: new DateOnly(2026, 04, 07),
                Currency: "USD",
                CashBalance: 125_000m,
                Source: "custodian",
                RecordedBy: "test",
                SecuritiesMarketValue: 875_000m));

            var reloadedService = new InMemoryFundAccountService(persistencePath);
            var account = await reloadedService.GetAccountAsync(accountId);
            var latestSnapshot = await reloadedService.GetLatestBalanceSnapshotAsync(accountId);

            Assert.NotNull(account);
            Assert.Equal("ADVISORY-TB", account!.LedgerReference);
            Assert.Equal(AccountTypeDto.Brokerage, account.AccountType);
            Assert.NotNull(latestSnapshot);
            Assert.Equal(125_000m, latestSnapshot!.CashBalance);
            Assert.Equal(875_000m, latestSnapshot.SecuritiesMarketValue);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task CreateBusiness_WithoutOrganization_ThrowsInvalidOperation()
    {
        var service = CreateStructureService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBusinessAsync(
            new CreateBusinessRequest(
                BusinessId: Guid.NewGuid(),
                OrganizationId: Guid.NewGuid(),
                BusinessKind: BusinessKindDto.FinancialAdvisor,
                Code: "ADV",
                Name: "Advisor Business",
                BaseCurrency: "USD",
                EffectiveFrom: DateTimeOffset.UtcNow,
                CreatedBy: "test")));
    }

    [Fact]
    public async Task CreateOrganization_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var service = CreateStructureService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.CreateOrganizationAsync(
            new CreateOrganizationRequest(
                OrganizationId: Guid.NewGuid(),
                Code: "ORG",
                Name: "Meridian Wealth",
                BaseCurrency: "USD",
                EffectiveFrom: DateTimeOffset.UtcNow,
                CreatedBy: "test"),
            cts.Token));
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_ReturnsAdvisorAndFundBusinessesUnderOneOrganization()
    {
        var fixture = await CreateHybridFixtureAsync();

        var graph = await fixture.StructureService.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationId));

        Assert.Single(graph.Organizations);
        Assert.Equal(2, graph.Businesses.Count);
        Assert.Single(graph.Clients);
        Assert.Single(graph.Funds);
        Assert.Equal(4, graph.InvestmentPortfolios.Count);
        Assert.Equal(4, graph.Accounts.Count);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Organization);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Business);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Client);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.InvestmentPortfolio);
    }

    [Fact]
    public async Task GetAdvisoryViewAsync_ReturnsClientPortfolioAndAccount()
    {
        var fixture = await CreateHybridFixtureAsync();

        var view = await fixture.StructureService.GetAdvisoryViewAsync(
            new AdvisoryStructureQuery(fixture.AdvisoryBusinessId));

        Assert.NotNull(view);
        Assert.Equal(fixture.OrganizationId, view!.Organization.OrganizationId);
        Assert.Equal(fixture.AdvisoryBusinessId, view.Business.BusinessId);
        Assert.Single(view.Clients);
        Assert.Single(view.Clients[0].InvestmentPortfolios);
        Assert.Single(view.Clients[0].Accounts);
        Assert.Equal(fixture.AdvisoryPortfolioId, view.Clients[0].InvestmentPortfolios[0].InvestmentPortfolioId);
        Assert.Equal(fixture.AdvisoryAccountId, view.Clients[0].Accounts[0].AccountId);
        Assert.Empty(view.UnassignedInvestmentPortfolios);
    }

    [Fact]
    public async Task GetFundOperatingViewAsync_ReturnsFundSleeveAndVehicleSlices()
    {
        var fixture = await CreateHybridFixtureAsync();

        var view = await fixture.StructureService.GetFundOperatingViewAsync(
            new FundOperatingStructureQuery(fixture.FundBusinessId));

        Assert.NotNull(view);
        Assert.Single(view!.Funds);

        var fundSlice = view.Funds[0];
        Assert.Equal(fixture.FundId, fundSlice.Fund.FundId);
        Assert.Single(fundSlice.InvestmentPortfolios);
        Assert.Single(fundSlice.Accounts);
        Assert.Single(fundSlice.Sleeves);
        Assert.Single(fundSlice.Vehicles);
        Assert.Equal(fixture.DirectFundPortfolioId, fundSlice.InvestmentPortfolios[0].InvestmentPortfolioId);
        Assert.Equal(fixture.FundAccountId, fundSlice.Accounts[0].AccountId);
        Assert.Equal(fixture.SleevePortfolioId, fundSlice.Sleeves[0].InvestmentPortfolios[0].InvestmentPortfolioId);
        Assert.Equal(fixture.VehiclePortfolioId, fundSlice.Vehicles[0].InvestmentPortfolios[0].InvestmentPortfolioId);
    }

    [Fact]
    public async Task GetAccountingViewAsync_GroupsAccountsByLedgerReference()
    {
        var fixture = await CreateHybridFixtureAsync();

        var view = await fixture.StructureService.GetAccountingViewAsync(
            new AccountingStructureQuery(BusinessId: fixture.FundBusinessId));

        Assert.Equal(3, view.LedgerGroups.Count);
        Assert.Contains(view.LedgerGroups, group =>
            group.DisplayName == "FUND-TB"
            && group.AccountIds.Contains(fixture.FundAccountId));
        Assert.Contains(view.LedgerGroups, group =>
            group.DisplayName == "SLEEVE-TB"
            && group.AccountIds.Contains(fixture.SleeveAccountId));
        Assert.Contains(view.LedgerGroups, group =>
            group.DisplayName == "VEHICLE-TB"
            && group.AccountIds.Contains(fixture.VehicleAccountId));
    }

    [Fact]
    public async Task GetCashFlowViewAsync_InvestmentPortfolio_ReturnsRealizedProjectedAndVariance()
    {
        var fixture = await CreateHybridFixtureAsync();
        var asOf = new DateTimeOffset(2026, 04, 07, 12, 0, 0, TimeSpan.Zero);

        await fixture.AccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            fixture.AdvisoryAccountId,
            new DateOnly(2026, 04, 01),
            "USD",
            100_000m,
            "bank",
            "test"));
        await fixture.AccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            fixture.AdvisoryAccountId,
            new DateOnly(2026, 04, 07),
            "USD",
            112_000m,
            "bank",
            "test",
            AccruedInterest: 250m,
            PendingSettlement: 1_500m));
        await fixture.AccountService.IngestBankStatementAsync(new IngestBankStatementRequest(
            Guid.NewGuid(),
            fixture.AdvisoryAccountId,
            new DateOnly(2026, 04, 07),
            "Acme Bank",
            "advisory-cash.csv",
            [
                new BankStatementLineDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    fixture.AdvisoryAccountId,
                    new DateOnly(2026, 04, 03),
                    new DateOnly(2026, 04, 03),
                    10_000m,
                    "USD",
                    "Contribution",
                    "Capital contribution",
                    "BANK-001",
                    110_000m),
                new BankStatementLineDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    fixture.AdvisoryAccountId,
                    new DateOnly(2026, 04, 05),
                    new DateOnly(2026, 04, 05),
                    -3_000m,
                    "USD",
                    "Fee",
                    "Advisory fee",
                    "BANK-002",
                    107_000m),
                new BankStatementLineDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    fixture.AdvisoryAccountId,
                    new DateOnly(2026, 04, 10),
                    new DateOnly(2026, 04, 10),
                    5_000m,
                    "USD",
                    "Dividend",
                    "Upcoming dividend",
                    "BANK-003",
                    null),
                new BankStatementLineDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    fixture.AdvisoryAccountId,
                    new DateOnly(2026, 04, 12),
                    new DateOnly(2026, 04, 12),
                    -1_200m,
                    "USD",
                    "Fee",
                    "Upcoming advisory fee",
                    "BANK-004",
                    null)
            ],
            "test"));

        var view = await fixture.StructureService.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.InvestmentPortfolio,
            InvestmentPortfolioId: fixture.AdvisoryPortfolioId,
            AsOf: asOf,
            HistoricalDays: 7,
            ForecastDays: 7,
            BucketDays: 7));

        Assert.NotNull(view);
        Assert.Equal(GovernanceCashFlowScopeKindDto.InvestmentPortfolio, view!.Scope.ScopeKind);
        Assert.Equal(fixture.AdvisoryPortfolioId, view.Scope.InvestmentPortfolioId);
        Assert.Equal(1, view.AccountCount);
        Assert.Equal(112_000m, view.CurrentCashBalance);
        Assert.Equal(7_000m, view.RealizedLadder.NetPosition);
        Assert.Equal(5_550m, view.ProjectedLadder.NetPosition);
        Assert.Equal(-1_450m, view.VarianceSummary.VarianceAmount);
        Assert.Equal(117_550m, view.ProjectedClosingCashBalance);
        Assert.Contains(view.ProjectedEntries, entry => entry.SourceKind == "BalanceSnapshot" && entry.EventKind == "PendingSettlement");
        Assert.Contains(view.ProjectedEntries, entry => entry.SourceKind == "BankStatement" && entry.Amount == 5_000m);
        Assert.False(view.Accounts[0].UsedTrendFallback);
    }

    [Fact]
    public async Task GetCashFlowViewAsync_Account_UsesBalanceTrendFallbackWhenProjectedEntriesAreMissing()
    {
        var fixture = await CreateHybridFixtureAsync();
        var asOf = new DateTimeOffset(2026, 04, 07, 12, 0, 0, TimeSpan.Zero);

        await fixture.AccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            fixture.SleeveAccountId,
            new DateOnly(2026, 04, 01),
            "USD",
            50_000m,
            "custody",
            "test"));
        await fixture.AccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            fixture.SleeveAccountId,
            new DateOnly(2026, 04, 07),
            "USD",
            56_000m,
            "custody",
            "test"));

        var view = await fixture.StructureService.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account,
            AccountId: fixture.SleeveAccountId,
            AsOf: asOf,
            HistoricalDays: 7,
            ForecastDays: 7,
            BucketDays: 7));

        Assert.NotNull(view);
        Assert.Equal(GovernanceCashFlowScopeKindDto.Account, view!.Scope.ScopeKind);
        Assert.Equal(fixture.SleeveAccountId, view.Scope.AccountId);
        Assert.Equal(56_000m, view.CurrentCashBalance);
        Assert.Equal(6_000m, view.RealizedLadder.NetPosition);
        Assert.Equal(7_000m, view.ProjectedLadder.NetPosition);
        Assert.Equal(1_000m, view.VarianceSummary.VarianceAmount);
        Assert.True(view.Accounts[0].UsedTrendFallback);
        Assert.Contains(view.ProjectedEntries, entry => entry.SourceKind == "BalanceTrend");
    }

    [Fact]
    public async Task GetCashFlowViewAsync_Account_ProjectsSecurityMasterInstrumentRulesWithoutPositionData()
    {
        var bondSecurityId = Guid.NewGuid();
        var preferredSecurityId = Guid.NewGuid();
        var securityMasterQueryService = new FakeSecurityMasterQueryService(
            economicDefinitions:
            [
                CreateEconomicDefinition(
                    bondSecurityId,
                    displayName: "Meridian Bond 2026",
                    assetClass: "FixedIncome",
                    typeName: "CorporateBond",
                    currency: "USD",
                    economicTerms: new
                    {
                        maturity = new
                        {
                            effectiveDate = new DateOnly(2026, 01, 01),
                            issueDate = new DateOnly(2026, 01, 01),
                            maturityDate = new DateOnly(2026, 04, 18)
                        },
                        coupon = new
                        {
                            couponType = "Fixed",
                            couponRate = 0.06m,
                            paymentFrequency = "Monthly"
                        },
                        payment = new
                        {
                            paymentFrequency = "Monthly",
                            paymentCurrency = "USD"
                        }
                    }),
                CreateEconomicDefinition(
                    preferredSecurityId,
                    displayName: "Meridian Preferred",
                    assetClass: "Equity",
                    typeName: "PreferredEquity",
                    currency: "USD",
                    economicTerms: new
                    {
                        equityBehavior = new
                        {
                            distributionType = "CashDividend"
                        }
                    })
            ],
            corporateActionsBySecurity:
            new Dictionary<Guid, IReadOnlyList<CorporateActionDto>>
            {
                [preferredSecurityId] =
                [
                    new CorporateActionDto(
                        Guid.NewGuid(),
                        preferredSecurityId,
                        "Dividend",
                        new DateOnly(2026, 04, 14),
                        new DateOnly(2026, 04, 15),
                        1.25m,
                        "USD",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null)
                ]
            });
        var fixture = await CreateHybridFixtureAsync(securityMasterQueryService: securityMasterQueryService);
        var asOf = new DateTimeOffset(2026, 04, 07, 12, 0, 0, TimeSpan.Zero);
        var effectiveFrom = new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

        await fixture.AccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            fixture.SleeveAccountId,
            new DateOnly(2026, 04, 07),
            "USD",
            56_000m,
            "custody",
            "test"));

        await fixture.StructureService.AssignNodeAsync(new AssignFundStructureNodeRequest(
            Guid.NewGuid(),
            fixture.SleeveAccountId,
            "SecurityMasterInstrument",
            JsonSerializer.Serialize(new
            {
                securityId = bondSecurityId,
                incomeAmount = 750m,
                principalAmount = 15_000m,
                firstProjectedDate = new DateOnly(2026, 04, 12)
            }),
            effectiveFrom,
            "test",
            IsPrimary: true));
        await fixture.StructureService.AssignNodeAsync(new AssignFundStructureNodeRequest(
            Guid.NewGuid(),
            fixture.SleevePortfolioId,
            "SecurityMasterInstrument",
            JsonSerializer.Serialize(new
            {
                securityId = preferredSecurityId,
                units = 200m
            }),
            effectiveFrom,
            "test"));

        var view = await fixture.StructureService.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account,
            AccountId: fixture.SleeveAccountId,
            AsOf: asOf,
            HistoricalDays: 7,
            ForecastDays: 14,
            BucketDays: 7));

        Assert.NotNull(view);
        Assert.False(view!.Accounts[0].UsedTrendFallback);
        Assert.True(view.Accounts[0].UsedSecurityMasterRules);
        Assert.Equal(3, view.Accounts[0].SecurityProjectedEntryCount);
        Assert.Equal(3, view.SecurityProjectedEntryCount);
        Assert.Contains(view.ProjectedEntries, entry =>
            entry.SourceKind == "SecurityMasterRule"
            && entry.EventKind == "Coupon"
            && entry.SecurityId == bondSecurityId
            && entry.Amount == 750m
            && entry.EventDate == new DateTimeOffset(2026, 04, 12, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains(view.ProjectedEntries, entry =>
            entry.SourceKind == "SecurityMasterRule"
            && entry.EventKind == "PrincipalMaturity"
            && entry.SecurityId == bondSecurityId
            && entry.Amount == 15_000m
            && entry.EventDate == new DateTimeOffset(2026, 04, 18, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains(view.ProjectedEntries, entry =>
            entry.SourceKind == "SecurityMasterCorporateAction"
            && entry.EventKind == "Dividend"
            && entry.SecurityId == preferredSecurityId
            && entry.Amount == 250m
            && entry.EventDate == new DateTimeOffset(2026, 04, 15, 0, 0, 0, TimeSpan.Zero));
        Assert.DoesNotContain(view.ProjectedEntries, entry => entry.SourceKind == "BalanceTrend");
    }

    [Fact]
    public async Task GetCashFlowViewAsync_Account_FallsBackWhenSecurityMasterInstrumentDefinitionIsMissing()
    {
        var fixture = await CreateHybridFixtureAsync(securityMasterQueryService: new FakeSecurityMasterQueryService());
        var asOf = new DateTimeOffset(2026, 04, 07, 12, 0, 0, TimeSpan.Zero);
        var effectiveFrom = new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

        await fixture.AccountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            fixture.SleeveAccountId,
            new DateOnly(2026, 04, 07),
            "USD",
            56_000m,
            "custody",
            "test",
            AccruedInterest: 500m));
        await fixture.StructureService.AssignNodeAsync(new AssignFundStructureNodeRequest(
            Guid.NewGuid(),
            fixture.SleeveAccountId,
            "SecurityMasterInstrument",
            JsonSerializer.Serialize(new
            {
                securityId = Guid.NewGuid(),
                incomeAmount = 500m,
                firstProjectedDate = new DateOnly(2026, 04, 12)
            }),
            effectiveFrom,
            "test"));

        var view = await fixture.StructureService.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account,
            AccountId: fixture.SleeveAccountId,
            AsOf: asOf,
            HistoricalDays: 7,
            ForecastDays: 7,
            BucketDays: 7));

        Assert.NotNull(view);
        Assert.False(view!.Accounts[0].UsedSecurityMasterRules);
        Assert.Equal(0, view.SecurityProjectedEntryCount);
        Assert.DoesNotContain(view.ProjectedEntries, entry => entry.SourceKind == "SecurityMasterRule");
        Assert.Contains(view.ProjectedEntries, entry =>
            entry.SourceKind == "BalanceSnapshot"
            && entry.EventKind == "Coupon"
            && entry.Amount == 500m);
    }

    [Fact]
    public async Task GovernanceViews_ExposeSharedDataAccessAcrossStructureAccountAndLedgerViews()
    {
        var sharedDataAccess = CreateSharedDataAccess(isSecurityMasterAvailable: true, hasHistoricalPrices: true);
        var fixture = await CreateHybridFixtureAsync(sharedDataAccessService: new FakeGovernanceSharedDataAccessService(sharedDataAccess));

        var organizationGraph = await fixture.StructureService.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(OrganizationId: fixture.OrganizationId));
        var advisoryView = await fixture.StructureService.GetAdvisoryViewAsync(
            new AdvisoryStructureQuery(fixture.AdvisoryBusinessId));
        var fundView = await fixture.StructureService.GetFundOperatingViewAsync(
            new FundOperatingStructureQuery(fixture.FundBusinessId));
        var accountingView = await fixture.StructureService.GetAccountingViewAsync(
            new AccountingStructureQuery(BusinessId: fixture.FundBusinessId));

        Assert.NotNull(organizationGraph.SharedDataAccess);
        Assert.NotNull(advisoryView);
        Assert.NotNull(fundView);
        Assert.NotNull(accountingView.SharedDataAccess);

        Assert.True(organizationGraph.SharedDataAccess!.SecurityMaster.IsAvailable);
        Assert.True(organizationGraph.SharedDataAccess.HistoricalPrices.HasStoredData);
        Assert.True(organizationGraph.SharedDataAccess.Backfill.IsAvailable);

        var advisoryAccount = advisoryView!.Clients[0].Accounts[0];
        var advisoryPortfolio = advisoryView.Clients[0].InvestmentPortfolios[0];
        Assert.Equal(sharedDataAccess, advisoryAccount.SharedDataAccess);
        Assert.Equal(sharedDataAccess, advisoryPortfolio.SharedDataAccess);
        Assert.Equal(sharedDataAccess, advisoryView.SharedDataAccess);

        var fundAccount = fundView!.Funds[0].Accounts[0];
        Assert.Equal(sharedDataAccess, fundAccount.SharedDataAccess);
        Assert.Equal(sharedDataAccess, fundView.SharedDataAccess);

        var fundLedgerGroup = Assert.Single(accountingView.LedgerGroups, group => group.DisplayName == "FUND-TB");
        Assert.Equal(sharedDataAccess, fundLedgerGroup.SharedDataAccess);
        Assert.Equal(sharedDataAccess, accountingView.SharedDataAccess);
    }

    [Fact]
    public async Task GovernanceViews_PreserveShape_WhenSharedDataAccessIsUnavailable()
    {
        var sharedDataAccess = CreateSharedDataAccess(
            isSecurityMasterAvailable: false,
            hasHistoricalPrices: false,
            lastBackfillRunSucceeded: false);
        var fixture = await CreateHybridFixtureAsync(
            sharedDataAccessService: new FakeGovernanceSharedDataAccessService(sharedDataAccess));

        var advisoryView = await fixture.StructureService.GetAdvisoryViewAsync(
            new AdvisoryStructureQuery(fixture.AdvisoryBusinessId));

        Assert.NotNull(advisoryView);
        var advisoryAccount = advisoryView!.Clients[0].Accounts[0];
        Assert.NotNull(advisoryAccount.SharedDataAccess);
        Assert.False(advisoryAccount.SharedDataAccess!.SecurityMaster.IsAvailable);
        Assert.False(advisoryAccount.SharedDataAccess.HistoricalPrices.HasStoredData);
        Assert.False(advisoryAccount.SharedDataAccess.Backfill.LastRunSucceeded);
    }

    [Fact]
    public async Task GetAccountingViewAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var fixture = await CreateHybridFixtureAsync(
            sharedDataAccessService: new FakeGovernanceSharedDataAccessService(CreateSharedDataAccess()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.StructureService.GetAccountingViewAsync(
            new AccountingStructureQuery(BusinessId: fixture.FundBusinessId),
            cts.Token));
    }

    [Fact]
    public async Task GetFundStructureGraphAsync_ReturnsCompatibilityGraphWithoutOrganizationNodes()
    {
        var fixture = await CreateHybridFixtureAsync();

        var graph = await fixture.StructureService.GetFundStructureGraphAsync(
            new FundStructureQuery(FundId: fixture.FundId));

        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Fund);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Sleeve);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Vehicle);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Entity);
        Assert.Contains(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Account);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Organization);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Business);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.Client);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == FundStructureNodeKindDto.InvestmentPortfolio);
    }

    [Fact]
    public async Task HybridGovernanceGraph_PersistsAcrossRestart()
    {
        var tempDirectory = CreateTempDirectory();
        var accountPersistencePath = Path.Combine(tempDirectory, "fund-accounts.json");
        var structurePersistencePath = Path.Combine(tempDirectory, "fund-structure.json");

        try
        {
            var fixture = await CreateHybridFixtureAsync(accountPersistencePath, structurePersistencePath);

            var reloadedAccountService = new InMemoryFundAccountService(accountPersistencePath);
            var reloadedStructureService = CreateStructureService(reloadedAccountService, structurePersistencePath);

            var organizationGraph = await reloadedStructureService.GetOrganizationStructureAsync(
                new OrganizationStructureQuery(OrganizationId: fixture.OrganizationId));
            var advisoryView = await reloadedStructureService.GetAdvisoryViewAsync(
                new AdvisoryStructureQuery(fixture.AdvisoryBusinessId));
            var fundView = await reloadedStructureService.GetFundOperatingViewAsync(
                new FundOperatingStructureQuery(fixture.FundBusinessId));
            var accountingView = await reloadedStructureService.GetAccountingViewAsync(
                new AccountingStructureQuery(BusinessId: fixture.FundBusinessId));

            Assert.Single(organizationGraph.Organizations);
            Assert.Equal(2, organizationGraph.Businesses.Count);
            Assert.Equal(4, organizationGraph.Accounts.Count);

            Assert.NotNull(advisoryView);
            Assert.Single(advisoryView!.Clients);
            Assert.Single(advisoryView.Clients[0].Accounts);
            Assert.Equal(fixture.AdvisoryAccountId, advisoryView.Clients[0].Accounts[0].AccountId);

            Assert.NotNull(fundView);
            Assert.Single(fundView!.Funds);
            Assert.Single(fundView.Funds[0].Sleeves);
            Assert.Single(fundView.Funds[0].Vehicles);
            Assert.Equal(fixture.FundAccountId, fundView.Funds[0].Accounts[0].AccountId);

            Assert.Contains(accountingView.LedgerGroups, group =>
                group.DisplayName == "FUND-TB"
                && group.AccountIds.Contains(fixture.FundAccountId));
            Assert.Contains(accountingView.LedgerGroups, group =>
                group.DisplayName == "SLEEVE-TB"
                && group.AccountIds.Contains(fixture.SleeveAccountId));
            Assert.Contains(accountingView.LedgerGroups, group =>
                group.DisplayName == "VEHICLE-TB"
                && group.AccountIds.Contains(fixture.VehicleAccountId));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static InMemoryFundStructureService CreateStructureService(
        InMemoryFundAccountService? accountService = null,
        string? persistencePath = null,
        IGovernanceSharedDataAccessService? sharedDataAccessService = null,
        ISecurityMasterQueryService? securityMasterQueryService = null) =>
        new(accountService ?? new InMemoryFundAccountService(), sharedDataAccessService, securityMasterQueryService, persistencePath);

    private static async Task<HybridFixture> CreateHybridFixtureAsync(
        string? accountPersistencePath = null,
        string? structurePersistencePath = null,
        IGovernanceSharedDataAccessService? sharedDataAccessService = null,
        ISecurityMasterQueryService? securityMasterQueryService = null)
    {
        var accountService = new InMemoryFundAccountService(accountPersistencePath);
        var structureService = CreateStructureService(accountService, structurePersistencePath, sharedDataAccessService, securityMasterQueryService);
        var now = new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

        var organizationId = Guid.NewGuid();
        var advisoryBusinessId = Guid.NewGuid();
        var fundBusinessId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var sleeveId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var advisoryPortfolioId = Guid.NewGuid();
        var directFundPortfolioId = Guid.NewGuid();
        var sleevePortfolioId = Guid.NewGuid();
        var vehiclePortfolioId = Guid.NewGuid();

        await structureService.CreateOrganizationAsync(new CreateOrganizationRequest(
            organizationId,
            "ORG-001",
            "Meridian Platform",
            "USD",
            now,
            "test"));

        await structureService.CreateBusinessAsync(new CreateBusinessRequest(
            advisoryBusinessId,
            organizationId,
            BusinessKindDto.FinancialAdvisor,
            "ADV-001",
            "Meridian Advisory",
            "USD",
            now,
            "test"));

        await structureService.CreateBusinessAsync(new CreateBusinessRequest(
            fundBusinessId,
            organizationId,
            BusinessKindDto.FundManager,
            "FUND-OPS-001",
            "Meridian Funds",
            "USD",
            now,
            "test"));

        await structureService.CreateClientAsync(new CreateClientRequest(
            clientId,
            advisoryBusinessId,
            "CLIENT-001",
            "Acme Family Office",
            "USD",
            now,
            "test"));

        await structureService.CreateFundAsync(new CreateFundRequest(
            fundId,
            "FUND-001",
            "Meridian Credit Fund",
            "USD",
            now,
            "test",
            BusinessId: fundBusinessId));

        await structureService.CreateLegalEntityAsync(new CreateLegalEntityRequest(
            entityId,
            LegalEntityTypeDto.Vehicle,
            "ENTITY-001",
            "Meridian SPV I",
            "DE",
            "USD",
            now,
            "test"));

        await structureService.CreateSleeveAsync(new CreateSleeveRequest(
            sleeveId,
            fundId,
            "SLEEVE-001",
            "Opportunistic Sleeve",
            now,
            "test"));

        await structureService.CreateVehicleAsync(new CreateVehicleRequest(
            vehicleId,
            fundId,
            entityId,
            "VEHICLE-001",
            "Meridian SPV I",
            "USD",
            now,
            "test"));

        await structureService.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            advisoryPortfolioId,
            advisoryBusinessId,
            "PORT-ADV-001",
            "Acme Core Portfolio",
            "USD",
            now,
            "test",
            ClientId: clientId));

        await structureService.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            directFundPortfolioId,
            fundBusinessId,
            "PORT-FUND-001",
            "Fund Core Book",
            "USD",
            now,
            "test",
            FundId: fundId));

        await structureService.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            sleevePortfolioId,
            fundBusinessId,
            "PORT-SLEEVE-001",
            "Sleeve Book",
            "USD",
            now,
            "test",
            SleeveId: sleeveId));

        await structureService.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
            vehiclePortfolioId,
            fundBusinessId,
            "PORT-VEHICLE-001",
            "Vehicle Book",
            "USD",
            now,
            "test",
            VehicleId: vehicleId,
            EntityId: entityId));

        var advisoryAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Brokerage,
            AccountCode: "ADV-ACCT-001",
            DisplayName: "Acme Advisory Custody",
            BaseCurrency: "USD",
            EffectiveFrom: now,
            CreatedBy: "test",
            PortfolioId: advisoryPortfolioId.ToString("D"),
            LedgerReference: "ADVISORY-TB"));

        var fundAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "FUND-ACCT-001",
            DisplayName: "Fund Core Custody",
            BaseCurrency: "USD",
            EffectiveFrom: now,
            CreatedBy: "test",
            FundId: fundId,
            PortfolioId: directFundPortfolioId.ToString("D"),
            LedgerReference: "FUND-TB"));

        var sleeveAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "SLEEVE-ACCT-001",
            DisplayName: "Sleeve Custody",
            BaseCurrency: "USD",
            EffectiveFrom: now,
            CreatedBy: "test",
            SleeveId: sleeveId,
            PortfolioId: sleevePortfolioId.ToString("D"),
            LedgerReference: "SLEEVE-TB"));

        var vehicleAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: "VEHICLE-ACCT-001",
            DisplayName: "Vehicle Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: now,
            CreatedBy: "test",
            EntityId: entityId,
            VehicleId: vehicleId,
            PortfolioId: vehiclePortfolioId.ToString("D"),
            LedgerReference: "VEHICLE-TB"));

        await structureService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(),
            directFundPortfolioId,
            fundAccount.AccountId,
            OwnershipRelationshipTypeDto.Operates,
            now,
            "test"));

        await structureService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(),
            sleevePortfolioId,
            sleeveAccount.AccountId,
            OwnershipRelationshipTypeDto.Operates,
            now,
            "test"));

        await structureService.LinkNodesAsync(new LinkFundStructureNodesRequest(
            Guid.NewGuid(),
            vehiclePortfolioId,
            vehicleAccount.AccountId,
            OwnershipRelationshipTypeDto.Operates,
            now,
            "test"));

        return new HybridFixture(
            accountService,
            structureService,
            organizationId,
            advisoryBusinessId,
            fundBusinessId,
            fundId,
            advisoryPortfolioId,
            directFundPortfolioId,
            sleevePortfolioId,
            vehiclePortfolioId,
            advisoryAccount.AccountId,
            fundAccount.AccountId,
            sleeveAccount.AccountId,
            vehicleAccount.AccountId);
    }

    private sealed record HybridFixture(
        InMemoryFundAccountService AccountService,
        InMemoryFundStructureService StructureService,
        Guid OrganizationId,
        Guid AdvisoryBusinessId,
        Guid FundBusinessId,
        Guid FundId,
        Guid AdvisoryPortfolioId,
        Guid DirectFundPortfolioId,
        Guid SleevePortfolioId,
        Guid VehiclePortfolioId,
        Guid AdvisoryAccountId,
        Guid FundAccountId,
        Guid SleeveAccountId,
        Guid VehicleAccountId);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MeridianFundStructureTests", Guid.NewGuid().ToString("N"));
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

    private static FundStructureSharedDataAccessDto CreateSharedDataAccess(
        bool isSecurityMasterAvailable = true,
        bool hasHistoricalPrices = true,
        bool lastBackfillRunSucceeded = true)
        => new(
            new SecurityMasterAccessSummaryDto(
                IsAvailable: isSecurityMasterAvailable,
                AvailabilityDescription: isSecurityMasterAvailable
                    ? "Security Master is available."
                    : "Security Master is unavailable in tests.",
                InstrumentDefinitionsAccessible: isSecurityMasterAvailable,
                EconomicDefinitionsAccessible: isSecurityMasterAvailable,
                TradingParametersAccessible: isSecurityMasterAvailable),
            new HistoricalPriceAccessSummaryDto(
                IsAvailable: true,
                HasStoredData: hasHistoricalPrices,
                AvailableSymbolCount: hasHistoricalPrices ? 2 : 0,
                SampleSymbols: hasHistoricalPrices ? ["AAPL", "MSFT"] : [],
                AvailabilityDescription: hasHistoricalPrices
                    ? "Historical price data is available for 2 symbol(s)."
                    : "Historical price query service is available but no stored symbols were found."),
            new BackfillAccessSummaryDto(
                IsAvailable: true,
                IsActive: false,
                ProviderCount: 2,
                LastProvider: "stooq",
                LastFrom: new DateOnly(2026, 01, 01),
                LastTo: new DateOnly(2026, 04, 07),
                LastCompletedUtc: new DateTimeOffset(2026, 04, 07, 18, 0, 0, TimeSpan.Zero),
                LastRunSucceeded: lastBackfillRunSucceeded,
                SymbolCheckpointCount: hasHistoricalPrices ? 2 : 0,
                SymbolBarCountCount: hasHistoricalPrices ? 2 : 0,
                AvailabilityDescription: "Backfill services are available with 2 configured provider(s)."));

    private static SecurityEconomicDefinitionRecord CreateEconomicDefinition(
        Guid securityId,
        string displayName,
        string assetClass,
        string typeName,
        string currency,
        object economicTerms)
        => new(
            securityId,
            assetClass,
            AssetFamily: null,
            SubType: typeName,
            TypeName: typeName,
            IssuerType: null,
            RiskCountry: "US",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: currency,
            Classification: JsonSerializer.SerializeToElement(new
            {
                assetClass,
                typeName
            }),
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName,
                currency
            }),
            EconomicTerms: JsonSerializer.SerializeToElement(economicTerms),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                asOf = new DateTimeOffset(2026, 04, 07, 0, 0, 0, TimeSpan.Zero)
            }),
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, displayName, true, new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero), null, null)
            ],
            LegacyAssetClass: assetClass,
            LegacyAssetSpecificTerms: null);

    private sealed class FakeGovernanceSharedDataAccessService : IGovernanceSharedDataAccessService
    {
        private readonly FundStructureSharedDataAccessDto _sharedDataAccess;

        public FakeGovernanceSharedDataAccessService(FundStructureSharedDataAccessDto sharedDataAccess)
        {
            _sharedDataAccess = sharedDataAccess;
        }

        public Task<FundStructureSharedDataAccessDto> GetSharedDataAccessAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_sharedDataAccess);
        }
    }

    private sealed class FakeSecurityMasterQueryService : ISecurityMasterQueryService
    {
        private readonly IReadOnlyDictionary<Guid, SecurityEconomicDefinitionRecord> _economicDefinitions;
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<CorporateActionDto>> _corporateActionsBySecurity;

        public FakeSecurityMasterQueryService(
            IReadOnlyList<SecurityEconomicDefinitionRecord>? economicDefinitions = null,
            IReadOnlyDictionary<Guid, IReadOnlyList<CorporateActionDto>>? corporateActionsBySecurity = null)
        {
            _economicDefinitions = (economicDefinitions ?? [])
                .ToDictionary(static definition => definition.SecurityId);
            _corporateActionsBySecurity = corporateActionsBySecurity ?? new Dictionary<Guid, IReadOnlyList<CorporateActionDto>>();
        }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(_economicDefinitions.TryGetValue(securityId, out var definition) ? definition : null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(_corporateActionsBySecurity.TryGetValue(securityId, out var actions) ? actions : []);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }
}
