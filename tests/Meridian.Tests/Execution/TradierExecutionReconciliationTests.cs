using FluentAssertions;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class TradierExecutionReconciliationTests
{
    [Fact]
    public async Task ReconcileAsync_TradierAccountPositionSync_TracksPartialFillDivergenceAndReconciliation()
    {
        var portfolio = new PaperTradingPortfolio([
            new AccountDefinition("tradier-main", "Tradier Main", AccountKind.Brokerage, 50_000m)
        ]);

        // Local account reflects a partially filled options order (1/2 contracts filled).
        portfolio.ApplyFill("tradier-main", new ExecutionReport
        {
            OrderId = "opt-1",
            Symbol = "AAPL  260620C00210000",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.PartialFill,
            FilledQuantity = 1m,
            FillPrice = 4.25m,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.PartiallyFilled,
            Timestamp = DateTimeOffset.UtcNow
        });

        var registry = new PortfolioRegistry();
        registry.Register("run-tradier", portfolio);

        var sync = new StubPositionSync(accountId =>
            Task.FromResult<IReadOnlyList<BrokeragePositionDto>>([
                new("AAPL  260620C00210000", 2m, 4.20m)
            ]));

        var sut = CreateService([("Tradier", (IBrokeragePositionSync)sync)], registry);

        var firstPass = await sut.ReconcileAsync();
        firstPass.Should().ContainSingle();
        firstPass[0].ProviderName.Should().Be("Tradier");
        firstPass[0].DivergentSymbols.Should().ContainSingle("AAPL  260620C00210000");

        // Execution reconciliation after remaining contract fill arrives.
        portfolio.ApplyFill("tradier-main", new ExecutionReport
        {
            OrderId = "opt-1",
            Symbol = "AAPL  260620C00210000",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill,
            FilledQuantity = 1m,
            FillPrice = 4.15m,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
            Timestamp = DateTimeOffset.UtcNow
        });

        var secondPass = await sut.ReconcileAsync();
        secondPass.Should().ContainSingle();
        secondPass[0].MatchedSymbols.Should().ContainSingle("AAPL  260620C00210000");
        secondPass[0].IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task ReconcileAsync_RateLimitFailure_FallsBackToSecondaryProviderAndMaintainsCoverage()
    {
        var portfolio = new PaperTradingPortfolio([
            new AccountDefinition("tradier-main", "Tradier Main", AccountKind.Brokerage, 50_000m)
        ]);
        portfolio.ApplyFill("tradier-main", BuildFill("MSFT", qty: 10m, price: 100m));

        var registry = new PortfolioRegistry();
        registry.Register("run-tradier", portfolio);

        var rateLimited = new StubPositionSync(_ => throw new HttpRequestException("429 Too Many Requests from Tradier"));
        var backup = new StubPositionSync(_ =>
            Task.FromResult<IReadOnlyList<BrokeragePositionDto>>([new("MSFT", 10m, 100m)]));

        var sut = CreateService(
            [("Tradier", (IBrokeragePositionSync)rateLimited), ("AlpacaBackup", backup)],
            registry,
            providerPriority: ["Tradier", "AlpacaBackup"]);

        var reports = await sut.ReconcileAsync();

        reports.Should().ContainSingle();
        reports[0].ProviderName.Should().Be("AlpacaBackup");
        reports[0].IsClean.Should().BeTrue();
        sut.GetLastReconciliationReport()?.ProviderName.Should().Be("AlpacaBackup");
    }

    [Fact]
    public async Task ReconcileAsync_TransientBrokerFailuresAcrossProviders_ReturnsNoReport()
    {
        var portfolio = new PaperTradingPortfolio([
            new AccountDefinition("tradier-main", "Tradier Main", AccountKind.Brokerage, 50_000m)
        ]);
        var registry = new PortfolioRegistry();
        registry.Register("run-tradier", portfolio);

        var transientA = new StubPositionSync(_ => throw new TimeoutException("Tradier transient timeout"));
        var transientB = new StubPositionSync(_ => throw new HttpRequestException("503 service unavailable"));

        var sut = CreateService(
            [("Tradier", (IBrokeragePositionSync)transientA), ("Failover", transientB)],
            registry);

        var reports = await sut.ReconcileAsync();

        reports.Should().BeEmpty();
        sut.GetLastReconciliationReport().Should().BeNull();
    }

    private static PositionReconciliationService CreateService(
        IEnumerable<(string Name, IBrokeragePositionSync Sync)> providers,
        PortfolioRegistry registry,
        IReadOnlyList<string>? providerPriority = null)
    {
        var options = Options.Create(new PositionSyncOptions
        {
            DivergenceTolerance = 0m,
            ProviderPriority = providerPriority ?? []
        });

        return new PositionReconciliationService(
            providers,
            registry,
            options,
            NullLogger<PositionReconciliationService>.Instance);
    }

    private static ExecutionReport BuildFill(string symbol, decimal qty, decimal price) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        Symbol = symbol,
        Side = OrderSide.Buy,
        ReportType = ExecutionReportType.Fill,
        FilledQuantity = qty,
        FillPrice = price,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        Timestamp = DateTimeOffset.UtcNow
    };

    private sealed class StubPositionSync(Func<string, Task<IReadOnlyList<BrokeragePositionDto>>> getPositions) : IBrokeragePositionSync
    {
        public Task<IReadOnlyList<BrokeragePositionDto>> GetCurrentPositionsAsync(string accountId, CancellationToken ct = default)
            => getPositions(accountId);

        public Task<BrokerageAccountSummaryDto> GetAccountSummaryAsync(string accountId, CancellationToken ct = default)
            => Task.FromResult(new BrokerageAccountSummaryDto(accountId, "stub", 0m));

        public Task<IReadOnlyList<string>> GetAllAccountsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
