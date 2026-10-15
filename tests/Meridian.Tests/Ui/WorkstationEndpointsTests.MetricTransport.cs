using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_MetricTransport_WinningMetricsShouldSurviveDurableReloadAndComparisonAndEquityResponses()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-metric-transport-{Guid.NewGuid():N}");
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var writer = new StrategyRunStore(history);
            var winning = BuildWinningMetricTransportRun("transport-winning");
            var baseline = BuildWinningMetricTransportRun("transport-baseline");
            baseline = baseline with
            {
                Metrics = baseline.Metrics! with
                {
                    Metrics = baseline.Metrics.Metrics with { Xirr = 0.2d }
                }
            };
            await writer.RecordRunAsync(winning);
            await writer.RecordRunAsync(baseline);
            var records = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{winning.RunId}"
            });
            using var retained = JsonDocument.Parse(records.Last().Data["strategyRunSnapshotJson"]);
            retained.RootElement.GetProperty("metrics").GetProperty("metrics").GetProperty("xirr")
                .ValueKind.Should().Be(JsonValueKind.Null, "the durable-only converter retains unavailable XIRR as null");
            await using var app = await CreateAppAsync(services =>
            {
                RegisterRunReadServices(services);
                // Exercise a fresh repository replay rather than the writer's in-memory projection.
                services.AddSingleton<IStrategyRepository>(new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot)));
            }, currentUserPermissions: UserPermission.ViewStrategies);
            var client = app.GetTestClient();

            var compare = await client.GetAsync("/api/strategies/runs/compare?ids=transport-winning,transport-baseline");

            compare.StatusCode.Should().Be(HttpStatusCode.OK);
            var comparisonBody = await compare.Content.ReadAsStringAsync();
            using var comparisonJson = JsonDocument.Parse(comparisonBody);
            var row = comparisonJson.RootElement.EnumerateArray()
                .Single(item => item.GetProperty("runId").GetString() == winning.RunId);
            row.GetProperty("profitFactor").GetString().Should().Be("Infinity");
            row.GetProperty("sortinoRatio").GetString().Should().Be("Infinity");
            row.GetProperty("xirr").GetString().Should().Be("NaN");
            row.GetProperty("equityCurve").GetProperty("sortinoRatio").GetString().Should().Be("Infinity");
            row.GetProperty("totalTrades").GetInt32().Should().Be(1);
            row.GetProperty("winningTrades").GetInt32().Should().Be(1);
            row.GetProperty("losingTrades").GetInt32().Should().Be(0);
            row.GetProperty("sharpeRatio").GetDouble().Should().Be(1.2d);
            var typedRows = JsonSerializer.Deserialize<List<RunComparisonDto>>(comparisonBody, ServerJsonOptions);
            var typedWinning = typedRows!.Single(item => item.RunId == winning.RunId);
            typedWinning.ProfitFactor.Should().Be(double.PositiveInfinity);
            typedWinning.SortinoRatio.Should().Be(double.PositiveInfinity);
            double.IsNaN(typedWinning.Xirr!.Value).Should().BeTrue();
            typedRows.Single(item => item.RunId == baseline.RunId).Xirr.Should().Be(0.2d);

            var equity = await client.GetAsync($"/api/workstation/runs/{winning.RunId}/equity-curve");

            equity.StatusCode.Should().Be(HttpStatusCode.OK);
            var curve = await equity.Content.ReadFromJsonAsync<EquityCurveSummary>(ServerJsonOptions);
            curve!.SortinoRatio.Should().Be(double.PositiveInfinity);
            curve.Points.Select(point => point.TotalEquity).Should().Equal(1_000m, 1_010m, 1_020m);
            curve.Points.Should().OnlyContain(point => point.DailyReturn >= 0m);

            // The same equity summary is nested in the shared portfolio drill-in projection.
            var portfolio = await app.Services.GetRequiredService<StrategyRunReadService>()
                .GetPortfolioDrillInAsync(winning.RunId, new StrategyRunReadScope("tenant-test", "tenant-test"));
            using var portfolioJson = JsonDocument.Parse(JsonSerializer.Serialize(portfolio, ServerJsonOptions));
            portfolioJson.RootElement.GetProperty("drawdownProfile").GetProperty("sortinoRatio")
                .GetString().Should().Be("Infinity");
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("comparison")]
    [InlineData("equity")]
    public async Task MapWorkstationEndpoints_MetricTransport_ShouldKeepUndefinedNonfiniteMetricsStrict(string surface)
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var invalid = BuildWinningMetricTransportRun("transport-invalid");
        invalid = invalid with
        {
            Metrics = invalid.Metrics! with { Metrics = invalid.Metrics.Metrics with { SharpeRatio = double.NaN } }
        };
        // The explicit in-memory seam admits malformed legacy evidence; transport must still
        // reject an undefined Sharpe value instead of broadly allowing every named float.
        await store.RecordRunAsync(invalid);
        await store.RecordRunAsync(BuildWinningMetricTransportRun("transport-valid"));
        var client = app.GetTestClient();
        var route = surface == "comparison"
            ? "/api/strategies/runs/compare?ids=transport-invalid,transport-valid"
            : "/api/workstation/runs/transport-invalid/equity-curve";

        var response = await client.GetAsync(route);

        // Minimal hosting's development exception middleware converts the serialization
        // exception into an HTTP failure instead of propagating it through TestServer.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var error = await response.Content.ReadAsStringAsync();
        error.Should().Contain("System.ArgumentException")
            .And.Contain("cannot be written as valid JSON",
                "an undefined Sharpe value must fail serialization rather than become a successful named-float payload");
    }

    private static StrategyRunEntry BuildWinningMetricTransportRun(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var equities = new[] { 1_000m, 1_010m, 1_020m };
        var snapshots = equities.Select((equity, index) => new PortfolioSnapshot(
            Timestamp: startedAt.AddDays(index),
            Date: DateOnly.FromDateTime(startedAt.AddDays(index).UtcDateTime),
            Cash: index == 1 ? 900m : equity,
            MarginBalance: 0m,
            LongMarketValue: index == 1 ? 110m : 0m,
            ShortMarketValue: 0m,
            TotalEquity: equity,
            DailyReturn: index == 0 ? 0m : (equity - equities[index - 1]) / equities[index - 1],
            Positions: index == 1
                ? new Dictionary<string, Position> { ["SPY"] = new("SPY", 1, 100m, 10m, 0m) }
                : new Dictionary<string, Position>(),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: [])).ToArray();
        var metrics = new BacktestMetrics(1_000m, 1_020m, 20m, 20m, 0.02m, 0.2m,
            1.2d, double.PositiveInfinity, 0d, 0m, 0m, 0, double.PositiveInfinity,
            1d, 1, 1, 0, 0m, 0m, 0m, double.NaN,
            new Dictionary<string, SymbolAttribution> { ["SPY"] = new("SPY", 20m, 0m, 2, 0m, 0m) });
        var fills = new[]
        {
            new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "SPY", 1, 100m, 0m, startedAt.AddHours(1)),
            new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "SPY", -1, 120m, 0m, startedAt.AddDays(2))
        };
        var result = new BacktestResult(new BacktestRequest(snapshots[0].Date, snapshots[^1].Date,
                Symbols: ["SPY"], InitialCash: 1_000m, RiskFreeRate: 0d),
            new HashSet<string> { "SPY" }, snapshots, [], fills, metrics, new Meridian.Ledger.Ledger(),
            TimeSpan.FromSeconds(1), 3);
        return StrategyRunEntry.Start("metric-transport-strategy", "Metric transport strategy", RunType.Backtest, runId)
            .Complete(result);
    }
}
