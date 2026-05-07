using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Accounts;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the fund-ops brokerage sync lane against account drift, credential outages, and
/// operator-cancelled provider calls.
/// </summary>
public sealed class BrokeragePortfolioSyncServiceTests
{
    [Fact]
    public async Task Scenario_MultiAccountAllocation_BrokerageSyncPersistsProjectionCursorRawSnapshotAndCoverage()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("alpaca"),
                new FixedActivityAdapter("alpaca"),
                includeSecurityLookup: true);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();

            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "BRK-001",
                    "Primary Brokerage",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-10),
                    "tests"),
                cts.Token);

            var status = await service.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-123", "ops-review"),
                cts.Token);

            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Healthy);
            status.IsLinked.Should().BeTrue();
            status.PositionCount.Should().Be(1);
            status.OpenOrderCount.Should().Be(1);
            status.FillCount.Should().Be(1);
            status.CashTransactionCount.Should().Be(1);
            status.SecurityMissingCount.Should().Be(0);

            var projectionPath = Path.Combine(root, "projections", fundAccountId.ToString("N"), "current.json");
            var cursorPath = Path.Combine(root, "cursors", $"{fundAccountId:N}.json");
            File.Exists(projectionPath).Should().BeTrue("operators need durable brokerage projections after restart");
            File.Exists(cursorPath).Should().BeTrue("re-sync should resume from a durable cursor");
            Directory.GetFiles(Path.Combine(root, "raw", "alpaca", "PA-123"), "*.json").Should().ContainSingle();

            var positions = await service.GetPositionsAsync(fundAccountId, cts.Token);
            positions.Should().ContainSingle(position =>
                position.Symbol == "AAPL" &&
                position.Security != null &&
                position.MarketValue == 18750m);

            var view = await service.GetActivityAsync(fundAccountId, cts.Token);
            view.Should().NotBeNull();
            view!.Orders.Should().ContainSingle(order => order.OrderId == "ord-open-1");
            view.Fills.Should().ContainSingle(fill => fill.OrderId == "ord-fill-1");
            view.CashTransactions.Should().ContainSingle(cash => cash.TransactionType == "DIV");

            var restoredStatus = await service.GetStatusAsync(fundAccountId, cts.Token);
            restoredStatus.Health.Should().Be(WorkstationBrokerageSyncHealth.Healthy);
            restoredStatus.LastSuccessfulSyncAt.Should().Be(status.LastSuccessfulSyncAt);

            var latestBalance = await fundAccountService.GetLatestBalanceSnapshotAsync(fundAccountId, cts.Token);
            latestBalance.Should().NotBeNull();
            latestBalance!.CashBalance.Should().Be(50000m);
            latestBalance.SecuritiesMarketValue.Should().Be(75000m);

            var reconciliationRuns = await fundAccountService.GetReconciliationRunsAsync(fundAccountId, cts.Token);
            reconciliationRuns.Should().ContainSingle();
            reconciliationRuns[0].Status.Should().NotBeNullOrWhiteSpace();

            var reconciliationResults = await fundAccountService.GetReconciliationResultsAsync(reconciliationRuns[0].ReconciliationRunId, cts.Token);
            reconciliationResults.Should().Contain(result => result.Category == "Cash");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_LinkedFundAccount_BrokerageStatusUsesStatusDtoBeforeLegacyProjectionExists()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("alpaca"),
                new FixedActivityAdapter("alpaca"),
                includeSecurityLookup: true);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();

            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "BRK-LINKED",
                    "Linked Brokerage",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-1),
                    "tests",
                    Institution: "alpaca",
                    PortfolioId: "portfolio-linked",
                    CustodianDetails: new CustodianAccountDetailsDto(
                        "PA-LINKED",
                        DtcParticipantCode: null,
                        CrestMemberCode: null,
                        EuroclearAccountNumber: null,
                        ClearstreamAccountNumber: null,
                        PrimebrokerGiveupCode: null,
                        SafekeepingLocation: null,
                        ServiceAgreementReference: null)),
                cts.Token);

            var status = await service.GetStatusAsync(fundAccountId, cts.Token);

            status.FundAccountId.Should().Be(fundAccountId);
            status.ProviderId.Should().Be("alpaca");
            status.ExternalAccountId.Should().Be("PA-LINKED");
            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Stale);
            status.IsLinked.Should().BeTrue();
            status.IsStale.Should().BeTrue();
            status.LastSuccessfulSyncAt.Should().BeNull();
            status.Warnings.Should().Contain("Brokerage account is linked, but no sync has been run.");
            File.Exists(Path.Combine(root, "projections", fundAccountId.ToString("N"), "current.json"))
                .Should().BeFalse("status readiness must not depend on the deprecated standalone projection before first sync");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderAccountIdWithPathCharacters_BrokerageSyncUsesPortableRawSnapshotPath()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("alpaca"),
                new FixedActivityAdapter("alpaca"),
                includeSecurityLookup: true);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();

            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "BRK-PORTABLE",
                    "Portable Brokerage",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-1),
                    "tests"),
                cts.Token);

            var status = await service.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA:123/OPS?", "ops-review"),
                cts.Token);

            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Healthy);
            Directory.GetFiles(Path.Combine(root, "raw", "alpaca", "PA_123_OPS_"), "*.json")
                .Should()
                .ContainSingle("raw brokerage evidence paths must be stable across Windows, Linux, and macOS");
            Directory.Exists(Path.Combine(root, "raw", "alpaca", "PA:123"))
                .Should()
                .BeFalse("provider account ids must not create OS-specific nested raw snapshot folders");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_BrokerageCredentialOutage_BrokerageSyncReportsFailedProjectionAndWarnings()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = CreateService(
                root,
                new ThrowingPortfolioAdapter("alpaca", "Alpaca credentials are missing."),
                new ThrowingActivityAdapter("alpaca", "Alpaca credentials are missing."),
                includeSecurityLookup: false);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var status = await service.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-404", "ops-review"),
                cts.Token);

            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Failed);
            status.LastSuccessfulSyncAt.Should().BeNull();
            status.LastError.Should().Contain("Alpaca credentials are missing.");
            status.Warnings.Should().Contain(warning => warning.Contains("Portfolio snapshot failed", StringComparison.OrdinalIgnoreCase));
            status.Warnings.Should().Contain(warning => warning.Contains("Activity snapshot failed", StringComparison.OrdinalIgnoreCase));

            var projectionPath = Path.Combine(root, "projections", fundAccountId.ToString("N"), "current.json");
            File.Exists(projectionPath).Should().BeTrue("failed sync status must survive restart for the operator");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_BrokerageFreshnessWindow_ExpiredProjectionReturnsStaleStatusWarning()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("alpaca"),
                new FixedActivityAdapter("alpaca"),
                includeSecurityLookup: true,
                staleAfter: TimeSpan.Zero);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();
            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "BRK-STALE",
                    "Stale Brokerage",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-2),
                    "tests"),
                cts.Token);

            var synced = await service.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-STALE", "ops-review"),
                cts.Token);
            synced.Health.Should().Be(WorkstationBrokerageSyncHealth.Healthy);
            synced.IsStale.Should().BeFalse();

            var restored = await service.GetStatusAsync(fundAccountId, cts.Token);

            restored.Health.Should().Be(WorkstationBrokerageSyncHealth.Stale);
            restored.IsStale.Should().BeTrue();
            restored.LastSuccessfulSyncAt.Should().Be(synced.LastSuccessfulSyncAt);
            restored.Warnings.Should().Contain("Brokerage sync is stale.");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderFeedInterruption_BrokerageSyncHonorsCancellationBeforePersistence()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, _) = CreateService(
                root,
                new FixedPortfolioAdapter("alpaca"),
                new FixedActivityAdapter("alpaca"),
                includeSecurityLookup: true);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            Func<Task> act = async () => await service.RunSyncAsync(
                    Guid.NewGuid(),
                    new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-CANCEL", "ops-review"),
                    cts.Token)
                .ConfigureAwait(false);

            await act.Should().ThrowAsync<OperationCanceledException>();
            Directory.Exists(Path.Combine(root, "projections")).Should().BeFalse();
            Directory.Exists(Path.Combine(root, "raw")).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_RunDerivedAndAccountSyncSnapshots_ContinuityDeltaIsEmitted()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("alpaca"),
                new FixedActivityAdapter("alpaca"),
                includeSecurityLookup: true);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();
            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "BRK-CONT",
                    "Continuity Brokerage",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-2),
                    "tests"),
                cts.Token);
            await fundAccountService.RecordBalanceSnapshotAsync(
                new RecordAccountBalanceSnapshotRequest(
                    fundAccountId,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    "USD",
                    CashBalance: 49900m,
                    Source: "run-derived:paper",
                    RecordedBy: "tests"),
                cts.Token);

            _ = await service.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-CONT", "ops-review"),
                cts.Token);

            var runs = await fundAccountService.GetReconciliationRunsAsync(fundAccountId, cts.Token);
            var results = await fundAccountService.GetReconciliationResultsAsync(runs[0].ReconciliationRunId, cts.Token);
            results.Should().ContainSingle(r => r.CheckLabel == "RunVsAccountSyncCashContinuity" && r.Category == "Continuity" && r.IsMatch == false);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_NullSourceSnapshotPresent_ReconciliationStillCompletes()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("alpaca"),
                new FixedActivityAdapter("alpaca"),
                includeSecurityLookup: true);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();
            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "BRK-NULLSRC",
                    "Null Source Brokerage",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-2),
                    "tests"),
                cts.Token);

            var snapshotBody = $$"""
                                 {
                                   "accountId": "{{fundAccountId}}",
                                   "asOfDate": "{{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}}",
                                   "currency": "USD",
                                   "cashBalance": 49900,
                                   "source": null,
                                   "recordedBy": "tests"
                                 }
                                 """;
            var request = JsonSerializer.Deserialize(
                snapshotBody,
                FundStructureContractsJsonContext.Default.RecordAccountBalanceSnapshotRequest);
            request.Should().NotBeNull();

            await fundAccountService.RecordBalanceSnapshotAsync(request!, cts.Token);
            var status = await service.RunSyncAsync(
                fundAccountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-NULLSRC", "ops-review"),
                cts.Token);

            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Healthy);
            var reconciliationRuns = await fundAccountService.GetReconciliationRunsAsync(fundAccountId, cts.Token);
            reconciliationRuns.Should().ContainSingle(run => run.ReconciliationRunId != Guid.Empty);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_RobinhoodThreeAccountLinks_BrokerageSyncPreservesAccountKindsAndHouseholdRollup()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("robinhood"),
                new FixedActivityAdapter("robinhood"),
                includeSecurityLookup: true);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();
            var accountSpecs = new[]
            {
                new { AccountId = Guid.NewGuid(), Code = "RH-ROTH", Name = "Robinhood Roth IRA", Kind = BrokerageAccountKindDto.RothIra },
                new { AccountId = Guid.NewGuid(), Code = "RH-TRAD", Name = "Robinhood Traditional IRA", Kind = BrokerageAccountKindDto.TraditionalIra },
                new { AccountId = Guid.NewGuid(), Code = "RH-TAX", Name = "Robinhood Brokerage", Kind = BrokerageAccountKindDto.TaxableBrokerage }
            };

            foreach (var spec in accountSpecs)
            {
                await fundAccountService.CreateAccountAsync(
                    new CreateAccountRequest(
                        spec.AccountId,
                        AccountTypeDto.Brokerage,
                        spec.Code,
                        spec.Name,
                        "USD",
                        DateTimeOffset.UtcNow.AddDays(-30),
                        "tests"),
                    cts.Token);

                var link = await service.LinkAccountAsync(
                    spec.AccountId,
                    new BrokerageAccountLinkRequestDto("robinhood", spec.Code, spec.Name, "ops-review", spec.Kind),
                    cts.Token);

                link.Should().NotBeNull();
                link!.AccountKind.Should().Be(spec.Kind);

                var preSyncStatus = await service.GetStatusAsync(spec.AccountId, cts.Token);
                preSyncStatus.ProviderId.Should().Be("robinhood");
                preSyncStatus.ExternalAccountId.Should().Be(spec.Code);
                preSyncStatus.AccountKind.Should().Be(spec.Kind);

                var status = await service.RunSyncAsync(spec.AccountId, request: null, cts.Token);
                status.Health.Should().Be(WorkstationBrokerageSyncHealth.Healthy);
                status.AccountKind.Should().Be(spec.Kind);
            }

            var household = await service.GetHouseholdAsync("robinhood", cts.Token);

            household.ProviderId.Should().Be("robinhood");
            household.Accounts.Should().HaveCount(3);
            household.Accounts.Select(static account => account.AccountKind).Should().BeEquivalentTo([
                BrokerageAccountKindDto.RothIra,
                BrokerageAccountKindDto.TraditionalIra,
                BrokerageAccountKindDto.TaxableBrokerage
            ]);
            household.TotalEquity.Should().Be(375000m);
            household.TotalCash.Should().Be(150000m);
            household.Positions.Should().HaveCount(3);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_RobinhoodCashFlowAndPerformance_BrokerageSyncReportsCashAdjustedHistory()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("robinhood"),
                new FixedActivityAdapter("robinhood"),
                includeSecurityLookup: true);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();

            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "RH-ROTH",
                    "Robinhood Roth IRA",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-30),
                    "tests"),
                cts.Token);
            await service.LinkAccountAsync(
                fundAccountId,
                new BrokerageAccountLinkRequestDto("robinhood", "RH-ROTH", "Robinhood Roth IRA", "ops-review", BrokerageAccountKindDto.RothIra),
                cts.Token);
            await fundAccountService.RecordBalanceSnapshotAsync(
                new RecordAccountBalanceSnapshotRequest(
                    fundAccountId,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    "USD",
                    CashBalance: 49000m,
                    SecuritiesMarketValue: 71000m,
                    Source: "brokerage-sync:robinhood",
                    RecordedBy: "tests",
                    ExternalReference: "RH-ROTH"),
                cts.Token);

            _ = await service.RunSyncAsync(fundAccountId, request: null, cts.Token);

            var cashFlow = await service.GetCashFlowAsync(fundAccountId, null, null, cts.Token);
            cashFlow.AccountKind.Should().Be(BrokerageAccountKindDto.RothIra);
            cashFlow.TransactionCount.Should().Be(1);
            cashFlow.NetCashFlow.Should().Be(42.50m);
            cashFlow.Entries.Should().ContainSingle(entry => entry.Category == "Dividend");

            var performance = await service.GetPerformanceAsync(fundAccountId, null, null, cts.Token);
            performance.HasSufficientHistory.Should().BeTrue();
            performance.BeginningEquity.Should().Be(120000m);
            performance.EndingEquity.Should().Be(125000m);
            performance.NetCashFlow.Should().Be(42.50m);
            performance.CashAdjustedReturn.Should().Be(4957.50m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_CashOnlyBalanceHistory_BrokeragePerformanceIncludesHistoryWithoutSecuritiesValue()
    {
        var root = CreateTempRoot();
        try
        {
            var (service, serviceProvider) = CreateService(
                root,
                new FixedPortfolioAdapter("robinhood"),
                new FixedActivityAdapter("robinhood"),
                includeSecurityLookup: true);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var fundAccountService = serviceProvider.GetRequiredService<IFundAccountService>();

            await fundAccountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "RH-CASHONLY",
                    "Robinhood Cash History",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-30),
                    "tests"),
                cts.Token);
            await service.LinkAccountAsync(
                fundAccountId,
                new BrokerageAccountLinkRequestDto("robinhood", "RH-CASHONLY", "Robinhood Cash History", "ops-review", BrokerageAccountKindDto.TaxableBrokerage),
                cts.Token);
            await fundAccountService.RecordBalanceSnapshotAsync(
                new RecordAccountBalanceSnapshotRequest(
                    fundAccountId,
                    new DateOnly(2026, 1, 1),
                    "USD",
                    CashBalance: 10000m,
                    Source: "manual-opening-balance",
                    RecordedBy: "tests"),
                cts.Token);
            await fundAccountService.RecordBalanceSnapshotAsync(
                new RecordAccountBalanceSnapshotRequest(
                    fundAccountId,
                    new DateOnly(2026, 2, 1),
                    "USD",
                    CashBalance: 11000m,
                    SecuritiesMarketValue: 14000m,
                    Source: "brokerage-sync:robinhood",
                    RecordedBy: "tests",
                    ExternalReference: "RH-CASHONLY"),
                cts.Token);

            var performance = await service.GetPerformanceAsync(
                fundAccountId,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1),
                cts.Token);

            performance.HasSufficientHistory.Should().BeTrue();
            performance.Points.Should().HaveCount(2);
            performance.Points[0].Equity.Should().Be(10000m, "cash-only history snapshots should still contribute usable beginning equity");
            performance.Points[1].Equity.Should().Be(25000m);
            performance.BeginningEquity.Should().Be(10000m);
            performance.EndingEquity.Should().Be(25000m);
            performance.CashAdjustedReturn.Should().Be(15000m);
            performance.Warnings.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_AccountReadModelContracts_BrokerageSyncUsesSplitQueryAndManagementContracts()
    {
        var root = CreateTempRoot();
        try
        {
            var accountService = new InMemoryFundAccountService();
            var services = new ServiceCollection();
            services.AddSingleton<IAccountQueryService>(accountService);
            services.AddSingleton<IAccountManagementService>(accountService);
            services.AddSingleton<ISecurityReferenceLookup>(new StaticSecurityReferenceLookup());
            await using var serviceProvider = services.BuildServiceProvider();
            var service = new BrokeragePortfolioSyncService(
                new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "robinhood"),
                catalogs: [],
                portfolioAdapters: [new FixedPortfolioAdapter("robinhood")],
                activityAdapters: [new FixedActivityAdapter("robinhood")],
                services: serviceProvider,
                logger: NullLogger<BrokeragePortfolioSyncService>.Instance);
            var fundAccountId = Guid.NewGuid();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await accountService.CreateAccountAsync(
                new CreateAccountRequest(
                    fundAccountId,
                    AccountTypeDto.Brokerage,
                    "RH-QRY",
                    "Robinhood query-only account",
                    "USD",
                    DateTimeOffset.UtcNow.AddDays(-30),
                    "tests"),
                cts.Token);
            await accountService.RecordBalanceSnapshotAsync(
                new RecordAccountBalanceSnapshotRequest(
                    fundAccountId,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    "USD",
                    CashBalance: 100000m,
                    Source: "import:cash-only",
                    RecordedBy: "tests"),
                cts.Token);

            var link = await service.LinkAccountAsync(
                fundAccountId,
                new BrokerageAccountLinkRequestDto("robinhood", "RH-QRY", "Robinhood query-only account", "ops-review", BrokerageAccountKindDto.TaxableBrokerage),
                cts.Token);
            var status = await service.RunSyncAsync(fundAccountId, request: null, cts.Token);
            var latestSnapshot = await accountService.GetLatestBalanceSnapshotAsync(fundAccountId, cts.Token);
            var performance = await service.GetPerformanceAsync(fundAccountId, null, null, cts.Token);
            var reconciliationRuns = await accountService.GetReconciliationRunsAsync(fundAccountId, cts.Token);

            link.Should().NotBeNull("brokerage links should resolve accounts through the query contract");
            status.Health.Should().Be(WorkstationBrokerageSyncHealth.Healthy);
            latestSnapshot.Should().NotBeNull("brokerage sync should record account snapshots through the management contract");
            latestSnapshot!.SecuritiesMarketValue.Should().Be(75000m);
            performance.HasSufficientHistory.Should().BeTrue("performance should read balance history from the query contract");
            performance.BeginningEquity.Should().Be(100000m, "cash-only history must treat missing securities value as zero");
            performance.EndingEquity.Should().Be(125000m);
            reconciliationRuns.Should().ContainSingle("snapshot enrichment should still trigger account reconciliation through the management contract");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }
    private static (BrokeragePortfolioSyncService Service, ServiceProvider Provider) CreateService(
        string root,
        IBrokeragePortfolioSync portfolioAdapter,
        IBrokerageActivitySync activityAdapter,
        bool includeSecurityLookup,
        TimeSpan? staleAfter = null,
        IReadOnlyList<IBrokerageAccountCatalog>? catalogs = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFundAccountService, InMemoryFundAccountService>();
        if (includeSecurityLookup)
        {
            services.AddSingleton<ISecurityReferenceLookup>(new StaticSecurityReferenceLookup());
        }
        var serviceProvider = services.BuildServiceProvider();

        var syncService = new BrokeragePortfolioSyncService(
            new BrokeragePortfolioSyncOptions(root, staleAfter ?? TimeSpan.FromMinutes(30), "alpaca"),
            catalogs: catalogs ?? [],
            portfolioAdapters: [portfolioAdapter],
            activityAdapters: [activityAdapter],
            services: serviceProvider,
            logger: NullLogger<BrokeragePortfolioSyncService>.Instance);
        return (syncService, serviceProvider);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-brokerage-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedPortfolioAdapter(string providerId) : IBrokeragePortfolioSync
    {
        public string ProviderId { get; } = providerId;

        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var account = new BrokerageExternalAccountDto(
                ProviderId,
                externalAccountId,
                "Alpaca Paper PA-123",
                "active",
                "USD",
                DateTimeOffset.UtcNow);

            return Task.FromResult(new BrokeragePortfolioSnapshotDto(
                account,
                new BrokerageBalanceSnapshotDto(50000m, 125000m, 95000m, "USD"),
                [
                    new BrokeragePositionSnapshotDto(
                        "AAPL",
                        100m,
                        180m,
                        187.50m,
                        18750m,
                        750m,
                        "equity",
                        Description: "Apple Inc.",
                        PositionId: "pos-aapl")
                ],
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class FixedActivityAdapter(string providerId) : IBrokerageActivitySync
    {
        public string ProviderId { get; } = providerId;

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                ProviderId,
                externalAccountId,
                DateTimeOffset.UtcNow,
                [
                    new BrokerageOrderSnapshotDto(
                        "ord-open-1",
                        "client-open-1",
                        "AAPL",
                        OrderSide.Buy,
                        OrderType.Limit,
                        OrderStatus.Accepted,
                        25m,
                        0m,
                        185m,
                        null,
                        DateTimeOffset.UtcNow.AddMinutes(-8))
                ],
                [
                    new BrokerageFillSnapshotDto(
                        "fill-1",
                        "ord-fill-1",
                        "AAPL",
                        OrderSide.Buy,
                        10m,
                        184.25m,
                        DateTimeOffset.UtcNow.AddMinutes(-12),
                        "XNAS",
                        0m)
                ],
                [
                    new BrokerageCashTransactionDto(
                        "cash-1",
                        "DIV",
                        42.50m,
                        "USD",
                        DateTimeOffset.UtcNow.AddDays(-1),
                        "AAPL",
                        "Dividend")
                ]));
        }
    }

    private sealed class ThrowingPortfolioAdapter(string providerId, string message) : IBrokeragePortfolioSync
    {
        public string ProviderId { get; } = providerId;

        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ThrowingActivityAdapter(string providerId, string message) : IBrokerageActivitySync
    {
        public string ProviderId { get; } = providerId;

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StaticSecurityReferenceLookup : ISecurityReferenceLookup
    {
        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<WorkstationSecurityReference?>(string.Equals(symbol, "AAPL", StringComparison.OrdinalIgnoreCase)
                ? new WorkstationSecurityReference(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Apple Inc.",
                    "equity",
                    "USD",
                    SecurityStatusDto.Active,
                    "AAPL")
                : null);
        }
    }
}
