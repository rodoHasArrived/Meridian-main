using System.Buffers.Binary;
using System.Text;
using Meridian.Backtesting.Sdk;
using Meridian.Ledger;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Runtime;

namespace Meridian.QuantScript.Tests;

public sealed class QuantScriptWorkerProtocolTests
{
    [Fact]
    public async Task ReadAsync_MalformedWorkerPayload_FailsClosed()
    {
        var malformed = Encoding.UTF8.GetBytes("{");
        var frame = new byte[sizeof(int) + malformed.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, malformed.Length);
        malformed.AsSpan().CopyTo(frame.AsSpan(sizeof(int)));
        await using var stream = new MemoryStream(frame);

        var act = () => QuantScriptWorkerProtocol.ReadAsync(stream, 1_024, CancellationToken.None);

        await act.Should().ThrowAsync<WorkerProtocolException>()
            .WithMessage("*malformed JSON*");
    }

    [Fact]
    public async Task ReadAsync_DeclaredOversizedWorkerPayload_RejectsBeforeAllocation()
    {
        var frame = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 1_025);
        await using var stream = new MemoryStream(frame);

        var act = () => QuantScriptWorkerProtocol.ReadAsync(stream, 1_024, CancellationToken.None);

        await act.Should().ThrowAsync<WorkerProtocolException>()
            .WithMessage("*outside the allowed range*");
    }

    [Fact]
    public async Task WriteThenRead_VersionedEnvelope_RoundTrips()
    {
        await using var stream = new MemoryStream();
        await QuantScriptWorkerProtocol.WriteAsync(
            stream,
            QuantScriptWorkerProtocol.Result,
            "correlation",
            new WorkerFatalError("none"),
            4_096,
            CancellationToken.None);
        stream.Position = 0;

        var envelope = await QuantScriptWorkerProtocol.ReadAsync(stream, 4_096, CancellationToken.None);

        envelope.Version.Should().Be(QuantScriptWorkerProtocol.Version);
        envelope.Kind.Should().Be(QuantScriptWorkerProtocol.Result);
        envelope.CorrelationId.Should().Be("correlation");
        QuantScriptWorkerProtocol.ReadPayload<WorkerFatalError>(envelope).Message.Should().Be("none");
    }

    [Fact]
    public void WorkerResult_MissingRequiredCollection_FailsClosed()
    {
        var result = new WorkerScriptRunResult(
            Success: false,
            ElapsedTicks: 0,
            CompileTimeTicks: 0,
            CompilationErrors: [],
            RuntimeDiagnostics: [],
            RuntimeError: null,
            ConsoleOutput: string.Empty,
            Metrics: [],
            Plots: null!,
            Trades: [],
            CapturedBacktests: [],
            RuntimeParameters: []);

        Action act = result.Validate;

        act.Should().Throw<WorkerProtocolException>()
            .WithMessage("*required collections*");
    }

    [Fact]
    public async Task WorkerResult_FullBacktestResult_RoundTripsThroughGeneratedContext()
    {
        var journalId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2024-01-02T14:30:00Z");
        const string description = "Backtest opening balance";
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(new JournalEntry(
            journalId,
            timestamp,
            description,
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset),
                    100_000m,
                    0m,
                    description),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    new LedgerAccount("Opening Equity", LedgerAccountType.Equity),
                    0m,
                    100_000m,
                    description)
            ],
            new JournalEntryMetadata(
                ActivityType: "Backtest",
                Symbol: "SPY",
                StrategyId: "strategy-1",
                Tags: new Dictionary<string, string> { ["source"] = "quant-script" })));

        var request = new BacktestRequest(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            Symbols: ["SPY"]);
        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 101_000m,
            GrossPnl: 1_010m,
            NetPnl: 1_000m,
            TotalReturn: 0.01m,
            AnnualizedReturn: 0.12m,
            SharpeRatio: 1.5,
            SortinoRatio: 1.8,
            CalmarRatio: 1.2,
            MaxDrawdown: 500m,
            MaxDrawdownPercent: 0.005m,
            MaxDrawdownRecoveryDays: 2,
            ProfitFactor: 2.0,
            WinRate: 0.6,
            TotalTrades: 5,
            WinningTrades: 3,
            LosingTrades: 2,
            TotalCommissions: 10m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.12,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                ["SPY"] = new("SPY", 1_000m, 0m, 5, 10m, 0m)
            });
        var backtest = new BacktestResult(
            request,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY" },
            [],
            [],
            [],
            metrics,
            ledger,
            TimeSpan.FromSeconds(2),
            31,
            EngineMetadata: new BacktestEngineMetadata("MeridianNative", "run-1"));
        var workerResult = new WorkerScriptRunResult(
            true,
            TimeSpan.FromSeconds(2).Ticks,
            TimeSpan.FromMilliseconds(200).Ticks,
            [],
            [],
            null,
            "complete",
            [new KeyValuePair<string, string>("Sharpe", "1.5")],
            [],
            [],
            [backtest],
            []);

        await using var stream = new MemoryStream();
        await QuantScriptWorkerProtocol.WriteAsync(
            stream,
            QuantScriptWorkerProtocol.Result,
            "backtest",
            new WorkerExecutionResponse(workerResult),
            1024 * 1024,
            CancellationToken.None);
        stream.Position = 0;

        var envelope = await QuantScriptWorkerProtocol.ReadAsync(
            stream,
            1024 * 1024,
            CancellationToken.None);
        var roundTrip = QuantScriptWorkerProtocol.ReadPayload<WorkerExecutionResponse>(envelope).Result;

        roundTrip.Validate();
        roundTrip.CapturedBacktests.Should().ContainSingle();
        var captured = roundTrip.CapturedBacktests[0];
        captured.Request.Should().BeEquivalentTo(request);
        captured.Universe.Should().BeEquivalentTo("SPY");
        captured.Metrics.Should().BeEquivalentTo(metrics);
        captured.EngineMetadata.Should().BeEquivalentTo(backtest.EngineMetadata);
        captured.Ledger.Journal.Should().ContainSingle();
        captured.Ledger.Journal[0].Metadata.StrategyId.Should().Be("strategy-1");
        captured.Ledger.GetBalance(new LedgerAccount("Cash", LedgerAccountType.Asset)).Should().Be(100_000m);
    }
}
