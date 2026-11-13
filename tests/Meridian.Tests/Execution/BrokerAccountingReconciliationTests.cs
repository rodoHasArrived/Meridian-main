using FluentAssertions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Execution;

public sealed class BrokerAccountingReconciliationTests
{
    [Fact]
    public async Task FullBrokerSnapshot_ReportsCashBasisMissingAndUnknownActivity()
    {
        var portfolio = new PaperTradingPortfolio([new("synthetic", "Synthetic", AccountKind.Brokerage, 1_000m)]);
        portfolio.ApplyFill("synthetic", new ExecutionReport
        {
            OrderId = "fill-1",
            Symbol = "XYZ",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill,
            FilledQuantity = 0.5m,
            FillPrice = 10m,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
            Timestamp = DateTimeOffset.UtcNow
        });
        var registry = new PortfolioRegistry();
        registry.Register("run", portfolio);
        var provider = new SyntheticBroker();
        var service = new PositionReconciliationService([("synthetic", provider)], registry,
            Microsoft.Extensions.Options.Options.Create(new PositionSyncOptions()), NullLogger<PositionReconciliationService>.Instance);

        var report = (await service.ReconcileAsync()).Single();

        // This guards against truncating a half-share position to zero.
        report.MatchedSymbols.Should().Contain("XYZ");
        report.DivergentSymbols.Should().BeEmpty();
        report.AccountingBreaks.Should().Contain(b => b.Reference == "Cash" && b.LocalAmount == 995m && b.BrokerAmount == 990m);
        report.AccountingBreaks.Should().Contain(b => b.Reference == "CostBasis:XYZ" && b.Category == BrokerAccountingBreakCategory.AmountMismatch);
        report.AccountingBreaks.Should().Contain(b => b.Reference == "Position:UNKNOWN" && b.Category == BrokerAccountingBreakCategory.MissingEntry);
        report.AccountingBreaks.Should().Contain(b => b.Reference == "activity-unknown" && b.Category == BrokerAccountingBreakCategory.UnknownActivity);
        report.IsClean.Should().BeFalse();
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutTryingAnotherProvider()
    {
        var portfolio = new PaperTradingPortfolio([new("synthetic", "Synthetic", AccountKind.Brokerage, 1_000m)]);
        var registry = new PortfolioRegistry();
        registry.Register("run", portfolio);
        var provider = new SyntheticBroker();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new PositionReconciliationService([("synthetic", provider)], registry,
            Microsoft.Extensions.Options.Options.Create(new PositionSyncOptions()), NullLogger<PositionReconciliationService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReconcileAsync(cancellation.Token));
    }

    private sealed class SyntheticBroker : IBrokeragePositionSync, IBrokeragePortfolioSync, IBrokerageActivitySync
    {
        public string ProviderId => "synthetic";
        public Task<IReadOnlyList<BrokeragePositionDto>> GetCurrentPositionsAsync(string accountId, CancellationToken ct = default)
            => throw new InvalidOperationException("Full accounting reconciliation must use one coherent portfolio snapshot.");
        public Task<BrokerageAccountSummaryDto> GetAccountSummaryAsync(string accountId, CancellationToken ct = default)
            => Task.FromResult(new BrokerageAccountSummaryDto(accountId, "Synthetic", 990m));
        public Task<IReadOnlyList<string>> GetAllAccountsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["synthetic"]);
        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(string externalAccountId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new BrokeragePortfolioSnapshotDto(
                new("synthetic", externalAccountId, "Synthetic", "active", "USD", DateTimeOffset.UtcNow),
                new(990m, 1_000m, 990m, "USD"),
                [new("XYZ", 0.5m, 12m, 12m, 6m, 0m, "us_equity"),
                 new("UNKNOWN", 1m, 5m, 5m, 5m, 0m, "us_equity")],
                DateTimeOffset.UtcNow, IsComplete: true));
        }
        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(string externalAccountId, DateTimeOffset? since = null, CancellationToken ct = default)
            => Task.FromResult(new BrokerageActivitySnapshotDto("synthetic", externalAccountId, DateTimeOffset.UtcNow,
                [], [], [], Activities: [new("activity-unknown", "VENDOR_NEW", BrokerageActivityCategory.Other,
                    BrokerageActivitySubtype.Other, DateTimeOffset.UtcNow, "USD", -5m)]));
    }
}
