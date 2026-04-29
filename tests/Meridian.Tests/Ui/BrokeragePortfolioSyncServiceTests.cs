using FluentAssertions;
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

    private static (BrokeragePortfolioSyncService Service, ServiceProvider Provider) CreateService(
        string root,
        IBrokeragePortfolioSync portfolioAdapter,
        IBrokerageActivitySync activityAdapter,
        bool includeSecurityLookup)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFundAccountService, InMemoryFundAccountService>();
        if (includeSecurityLookup)
        {
            services.AddSingleton<ISecurityReferenceLookup>(new StaticSecurityReferenceLookup());
        }
        var serviceProvider = services.BuildServiceProvider();

        var syncService = new BrokeragePortfolioSyncService(
            new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "alpaca"),
            catalogs: [],
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
