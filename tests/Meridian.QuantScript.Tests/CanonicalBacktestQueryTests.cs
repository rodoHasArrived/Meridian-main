using System.Text.Json;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.QuantScript.Runtime;
using Meridian.QuantScript.Plotting;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Meridian.QuantScript.Tests;

public sealed class CanonicalBacktestQueryTests
{
    [Fact]
    public async Task ScriptWorker_RawCanonicalRun_RetainsSourceSnapshotAndBalancedDividendJournals()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canonical-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "XYZ"));
        try
        {
            var id = Guid.NewGuid();
            var start = new DateOnly(2024, 1, 2);
            var security = new SecurityDetailDto(id, "Equity", SecurityStatusDto.Active, "Synthetic", "USD",
                JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [], [], 1, DateTimeOffset.MinValue, null);
            var dividend = new CorporateActionDto(Guid.NewGuid(), id, CorporateActionEventTypes.Dividend,
                start.AddDays(2), start.AddDays(3), 1m, "USD", null, null, null, null, null, null, null);
            using (var writer = new StreamWriter(Path.Combine(root, "XYZ", "XYZ_bars_2024-01-02.jsonl")))
            {
                for (var day = 0; day < 4; day++)
                {
                    var price = day < 2 ? 100m : 99m;
                    var bar = new HistoricalBar("XYZ", start.AddDays(day), price, price, price, price, 1_000_000,
                        "synthetic", day + 1, IsAdjusted: false);
                    writer.WriteLine(JsonSerializer.Serialize(MarketEvent.HistoricalBar(bar.ToTimestampUtc(), "XYZ", bar, "synthetic", day + 1),
                        MarketDataJsonContext.HighPerformanceOptions));
                }
            }
            var data = new Mock<IQuantDataContext>(MockBehavior.Strict);
            data.Setup(source => source.CanonicalSecurityMasterAsync("XYZ", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(security);
            data.Setup(source => source.CanonicalCorporateActionsAsync("XYZ", id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([dividend]);
            var options = Options.Create(new QuantScriptOptions { DefaultDataRoot = root, RunTimeoutSeconds = 30 });
            var logger = new RecordingLogger();
            var runner = new ScriptRunner(new RoslynScriptCompiler(options, NullLogger<RoslynScriptCompiler>.Instance),
                data.Object, new PlotQueue(), options, logger, null,
                new QuantScriptWorkerClient(logger));
            var result = await runner.RunAsync("""
                var bought = false;
                Backtest.WithCorporateActionMode("RawPricesWithCanonicalEvents");
                Backtest.WithSymbols("XYZ").From(new DateOnly(2024, 1, 2)).To(new DateOnly(2024, 1, 5))
                    .WithoutCommission().WithSlippageBasisPoints(0m)
                    .OnBar((bar, ctx) => { if (!bought) { ctx.PlaceMarketOrder("XYZ", 10); bought = true; } })
                    .Run();
                """, new Dictionary<string, object?>());
            result.Success.Should().BeTrue($"{result.RuntimeError}; {string.Join("; ", logger.Messages)}");
            var backtest = result.CapturedBacktests.Should().ContainSingle().Subject;
            backtest.Metrics.FinalEquity.Should().Be(100_000m);
            backtest.CanonicalCorporateActions!.Actions.Should().ContainSingle().Which.Action.CorpActId.Should().Be(dividend.CorpActId);
            backtest.Ledger.Journal.Count(journal => journal.Metadata.ActivityType?.StartsWith("canonical_dividend_", StringComparison.Ordinal) == true).Should().Be(2);
            backtest.Ledger.Journal.Should().OnlyContain(journal => journal.IsBalanced);
            data.VerifyAll();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Adapter_BindsExplicitCutoffAndIdentity_AndPropagatesSourceFailure()
    {
        var id = Guid.NewGuid();
        var cutoff = DateTimeOffset.Parse("2024-01-05T23:59:59Z");
        var security = new SecurityDetailDto(id, "Equity", SecurityStatusDto.Active, "Synthetic", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [], [], 1, DateTimeOffset.MinValue, null);
        var data = new Mock<IQuantDataContext>(MockBehavior.Strict);
        data.Setup(source => source.CanonicalSecurityMasterAsync("XYZ", cutoff, It.IsAny<CancellationToken>())).ReturnsAsync(security);
        data.Setup(source => source.CanonicalCorporateActionsAsync("XYZ", id, cutoff, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Action source unavailable"));
        var adapter = new QuantDataBacktestSecurityMaster(data.Object);
        var resolved = await adapter.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "XYZ", null, asOfUtc: cutoff);
        resolved!.SecurityId.Should().Be(id);
        var read = () => adapter.GetCorporateActionsAsync(id);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("Action source unavailable");
        data.VerifyAll();
    }

    [Fact]
    public async Task StrictHostQuery_WithoutMasterFails_InsteadOfReturningEmptyActions()
    {
        var data = new QuantDataContext(new JsonlMarketDataStore("/nonexistent-synthetic-data"), NullLogger<QuantDataContext>.Instance);
        var strict = () => data.CanonicalCorporateActionsAsync("XYZ", Guid.NewGuid(), DateTimeOffset.UtcNow);
        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*configured Security Master*");
        (await data.CorporateActionsAsync("XYZ")).Should().BeEmpty("the legacy helper remains permissive");
    }

    [Fact]
    public void GeneratedProtocol_PreservesCanonicalRequestAndSnapshotEvidence()
    {
        var cutoff = DateTimeOffset.Parse("2024-01-05T23:59:59Z");
        var id = Guid.NewGuid();
        var request = new WorkerDataRequest(WorkerDataOperation.CanonicalCorporateActions, "XYZ", Timestamp: cutoff, ExpectedSecurityId: id);
        var restored = QuantScriptWorkerProtocol.Deserialize<WorkerDataRequest>(QuantScriptWorkerProtocol.SerializeToElement(request));
        restored.Should().Be(request);
        var snapshot = new BacktestCanonicalActionSnapshot(cutoff, new string('A', 64), []);
        var retained = QuantScriptWorkerProtocol.Deserialize<BacktestCanonicalActionSnapshot>(QuantScriptWorkerProtocol.SerializeToElement(snapshot));
        retained!.ContentHash.Should().Be(snapshot.ContentHash);
        retained.EffectiveThroughUtc.Should().Be(cutoff);
    }

    [Fact]
    public void GeneratedProtocol_PreservesEverySupportedCashFlow_AndRejectsUnknownKinds()
    {
        var at = DateTimeOffset.Parse("2024-01-05T00:00:00Z");
        CashFlowEntry[] flows =
        [
            new TradeCashFlow(at, -100m, "XYZ", 1, 100m, "account"),
            new CommissionCashFlow(at, -1m, "XYZ", Guid.NewGuid(), "account"),
            new MarginInterestCashFlow(at, -2m, -1_000m, 0.05, "account"),
            new ShortRebateCashFlow(at, 1m, "XYZ", 10, 0.02, "account"),
            new DividendCashFlow(at, 10m, "XYZ", 10, 1m, "account"),
            new CashInterestCashFlow(at, 1m, 0.01, "account"),
            new AssetEventCashFlow(at, 10m, "XYZ", AssetEventType.ReturnOfCapital, 10, 1m) { AccountId = "account", BasisReturned = 5m }
        ];
        foreach (var flow in flows)
        {
            var restored = QuantScriptWorkerProtocol.Deserialize<CashFlowEntry>(QuantScriptWorkerProtocol.SerializeToElement(flow));
            restored.Should().Be(flow);
        }
        var unknown = JsonSerializer.SerializeToElement(new { kind = "runtime-type-name", value = new { } });
        var read = () => QuantScriptWorkerProtocol.Deserialize<CashFlowEntry>(unknown);
        read.Should().Throw<JsonException>();
    }

    private sealed class RecordingLogger : ILogger<ScriptRunner>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
