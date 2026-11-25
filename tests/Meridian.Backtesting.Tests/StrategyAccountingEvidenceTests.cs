using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Portfolio;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;

namespace Meridian.Backtesting.Tests;

public sealed class StrategyAccountingEvidenceTests
{
    [Theory]
    [InlineData("long", 10, 100, 130, 10, 1000)]
    [InlineData("split", 10, 100, 65, 20, 1000)]
    [InlineData("short", -10, 100, 90, -10, 1000)]
    [InlineData("face", 100000, 101.25, 102.25, 100000, 101250)]
    [InlineData("cross", 10, 100, 100, -5, 550)]
    public async Task GeneratedRun_ReconcilesNativeQuantityAndCostAtEconomicCutoff(string scenario,
        long quantity, double price, double mark, long finalQuantity, int expectedBasis)
    {
        var day = new DateOnly(2026, 11, 21);
        var at = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var request = new BacktestRequest(day, day, ["TEST"], InitialCash: 1000000m);
        var ledger = new BacktestLedger(LedgerPolicy.Trading());
        var unit = scenario == "face" ? SecurityLotUnitKind.FaceValue : SecurityLotUnitKind.Shares;
        var portfolio = new SimulatedPortfolio(request.ResolveAccounts(), request.DefaultBrokerageAccountId, new FixedCommissionModel(), ledger, at,
            new Dictionary<string, (SecurityLotUnitKind, decimal)> { ["TEST"] = (unit, 1m) })
        { SecurityIdentity = _ => Guid.Parse("d368cf50-af0b-4fb4-a8d4-8ba467b9e3b4") };
        var fill = new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "TEST", quantity, (decimal)price, 0m, at.AddHours(1));
        var fills = new List<FillEvent> { fill };
        portfolio.ProcessFill(fill);
        if (scenario == "split")
            portfolio.ApplyAssetEvent(new(at.AddHours(2), "TEST", AssetEventType.Split, PositionFactor: 2m, ReferencePrice: 50m));
        if (scenario == "cross")
        {
            var crossingFill = new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "TEST", -15, 110m, 0m, at.AddHours(2));
            portfolio.ProcessFill(crossingFill);
            fills.Add(crossingFill);
        }
        portfolio.UpdateLastPrice("TEST", (decimal)mark);
        var snapshot = portfolio.TakeSnapshot(at.AddHours(20), day);
        var result = new BacktestResult(request, new HashSet<string> { "TEST" }, [snapshot], snapshot.DayCashFlows, fills,
            BacktestMetricsEngine.Compute([snapshot], snapshot.DayCashFlows, fills, request), ledger, TimeSpan.Zero, 1);
        var entry = StrategyRunEntry.Start("accounting", "Accounting test", RunType.Backtest) with
        { RunId = "accounting-" + scenario, Metrics = result, EndedAt = at.AddYears(1) };
        var store = new StrategyRunStore();
        await store.RecordRunAsync(entry);
        var lookup = new SecurityLookup();
        var read = new StrategyRunReadService(store, new PortfolioReadService(lookup), new LedgerReadService(lookup));
        var service = new ReconciliationRunService(read, new(), new InMemoryReconciliationRunRepository());
        var detail = await service.RunAsync(new(entry.RunId));
        detail.Should().NotBeNull();
        detail!.Breaks.Should().BeEmpty();
        detail.Matches.Should().Contain(match => match.CheckId == "net-equity" && match.Variance == 0m);
        detail.Matches.SelectMany(match => match.Measures).Should().ContainSingle(measure => measure.Kind == ReconciliationBreakMeasureKindDto.Quantity
            && measure.Expected == finalQuantity && measure.Actual == finalQuantity);
        detail.Matches.SelectMany(match => match.Measures).Should().ContainSingle(measure => measure.Kind == ReconciliationBreakMeasureKindDto.CostBasis
            && measure.Expected == expectedBasis && measure.Actual == expectedBasis && measure.Unit == "USD");
        detail.Summary.LedgerAsOf.Should().Be(snapshot.Timestamp);
        snapshot.FunctionalCurrency.Should().Be("USD");
    }

    [Fact]
    public void LegacySnapshotJson_DoesNotAcquireAFunctionalCurrency()
    {
        var snapshot = JsonSerializer.Deserialize<FinancialAccountSnapshot>("{\"AccountId\":\"legacy\",\"DisplayName\":\"Legacy\",\"Kind\":0,\"Positions\":{}}")!;
        snapshot.FunctionalCurrency.Should().BeNull();
        JsonSerializer.Serialize(snapshot).Should().NotContain("FunctionalCurrency");
    }

    private sealed class SecurityLookup : ISecurityReferenceLookup
    {
        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult<WorkstationSecurityReference?>(new(Guid.Parse("d368cf50-af0b-4fb4-a8d4-8ba467b9e3b4"), "Synthetic", "Equity", "USD", SecurityStatusDto.Active, symbol));
    }
}
