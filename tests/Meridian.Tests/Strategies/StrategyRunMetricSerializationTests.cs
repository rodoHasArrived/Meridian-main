using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Operations;
using Meridian.Storage.Operations;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;

namespace Meridian.Tests.Strategies;

public sealed class StrategyRunMetricSerializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WinningRun_RetainsInfiniteRatiosAndExactBookedLotEvidence(bool noDownside)
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-metric-history-{Guid.NewGuid():N}");
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("winning-strategy", "Winning strategy", RunType.Backtest);
            var result = CreateWinningResult(noDownside);
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(started.Complete(result));

            var replayed = await new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot))
                .GetRunByIdAsync(started.RunId);

            replayed!.Metrics!.Metrics.ProfitFactor.Should().Be(double.PositiveInfinity);
            replayed.Metrics.Metrics.SortinoRatio.Should().Be(noDownside ? double.PositiveInfinity : 1.5d);
            double.IsNaN(replayed.Metrics.Metrics.Xirr).Should().BeTrue();
            var lot = replayed.Metrics.Snapshots.Single().Accounts.Values.Single().ClosedLots!.Single();
            lot.CostBasis.Should().Be(100m);
            lot.EntryCommission.Should().Be(2m);
            lot.ExitCommission.Should().Be(3m);
            lot.RealizedPnl.Should().Be(20m, "the exact basis must survive instead of the rounded entry quote");
            var records = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            });
            using var snapshot = JsonDocument.Parse(records.Last().Data["strategyRunSnapshotJson"]);
            var metrics = snapshot.RootElement.GetProperty("metrics").GetProperty("metrics");
            metrics.GetProperty("profitFactor").GetString().Should().Be("Infinity");
            metrics.GetProperty("xirr").ValueKind.Should().Be(JsonValueKind.Null,
                "durable history retains unavailable XIRR as null while replay restores the SDK NaN sentinel");
            if (noDownside)
                metrics.GetProperty("sortinoRatio").GetString().Should().Be("Infinity");
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("SharpeRatio")]
    [InlineData("CalmarRatio")]
    [InlineData("WinRate")]
    public async Task UndefinedNonfiniteMetric_RejectsDurableAppendWithoutChangingHistory(string field)
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-invalid-metric-{Guid.NewGuid():N}");
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("invalid-strategy", "Invalid strategy", RunType.Backtest);
            await store.RecordRunAsync(started);
            var result = CreateWinningResult(noDownside: true);
            var invalid = field switch
            {
                "SharpeRatio" => result.Metrics with { SharpeRatio = double.NaN },
                "CalmarRatio" => result.Metrics with { CalmarRatio = double.PositiveInfinity },
                "WinRate" => result.Metrics with { WinRate = double.NegativeInfinity },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };

            var append = () => store.RecordRunAsync(started.Complete(result with { Metrics = invalid }));

            await append.Should().ThrowAsync<ArgumentException>();
            (await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            })).Should().ContainSingle();
            var replayed = await new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot))
                .GetRunByIdAsync(started.RunId);
            replayed!.EndedAt.Should().BeNull();
            replayed.Metrics.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static BacktestResult CreateWinningResult(bool noDownside)
    {
        var from = new DateOnly(2026, 1, 2);
        var to = from.AddDays(1);
        var opened = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var closed = opened.AddDays(1);
        var account = FinancialAccount.CreateDefaultBrokerage(1_000m, 0d, 0d);
        var lot = new ClosedLot(Guid.NewGuid(), "XYZ", 3, 100m / 3m, opened, Guid.NewGuid(),
            40m, closed, Guid.NewGuid(), account.AccountId)
        {
            CostBasis = 100m,
            EntryCommission = 2m,
            ExitCommission = 3m
        };
        var snapshot = new PortfolioSnapshot(closed, to, 1_015m, 0m, 0m, 0m, 1_015m, 0.015m,
            new Dictionary<string, Position>(),
            new Dictionary<string, FinancialAccountSnapshot>
            {
                [account.AccountId] = new(account.AccountId, account.DisplayName, account.Kind, account.Institution,
                    1_015m, 0m, 0m, 0m, 1_015m, new Dictionary<string, Position>(), account.Rules!, [], [lot])
            }, []);
        var metrics = new BacktestMetrics(1_000m, 1_015m, 20m, 15m, 0.015m, 0.15m,
            1.2d, noDownside ? double.PositiveInfinity : 1.5d, 0d, 0m, 0m, 0,
            double.PositiveInfinity, 1d, 1, 1, 0, 5m, 0m, 0m, double.NaN,
            new Dictionary<string, SymbolAttribution>
            {
                ["XYZ"] = new("XYZ", 20m, 0m, 2, 5m, 0m)
            });
        return new BacktestResult(new BacktestRequest(from, to, InitialCash: 1_000m),
            new HashSet<string> { "XYZ" }, [snapshot], [], [], metrics, new Meridian.Ledger.Ledger(),
            TimeSpan.FromSeconds(1), 2);
    }
}
