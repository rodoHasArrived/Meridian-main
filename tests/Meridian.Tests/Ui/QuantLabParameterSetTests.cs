using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Ui.Shared.Endpoints;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the identity-significant canonicalization of recorded Quant Lab run parameters.
/// </summary>
/// <remarks>
/// The recorded parameter set feeds the run's input hash, so what it canonicalizes away is what
/// run identity can no longer distinguish. Symbol order is engine-significant — the engine replays
/// symbols in request order and breaks same-timestamp ties by stream index — so these tests pin
/// that the recorded parameters preserve it.
/// </remarks>
public sealed class QuantLabParameterSetTests
{
    private static BacktestResult Result(params string[] symbols)
    {
        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 100_000m, GrossPnl: 0m, NetPnl: 0m,
            TotalReturn: 0m, AnnualizedReturn: 0m, SharpeRatio: 0, SortinoRatio: 0,
            CalmarRatio: 0, MaxDrawdown: 0m, MaxDrawdownPercent: 0m, MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 0, WinRate: 0, TotalTrades: 0, WinningTrades: 0, LosingTrades: 0,
            TotalCommissions: 0m, TotalMarginInterest: 0m, TotalShortRebates: 0m, Xirr: 0,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 1, 1),
                To: new DateOnly(2026, 6, 30),
                Symbols: symbols,
                InitialCash: 100_000m),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(1),
            TotalEventsProcessed: 0);
    }

    [Fact]
    public void Symbols_AreRecordedInReplayOrderNotSortedOrder()
    {
        // [SPY, AAPL] and [AAPL, SPY] can fill differently when two symbols share a timestamp, so
        // sorting them into one canonical value gave two materially different runs one identity.
        var parameters = QuantLabEndpoints.BuildParameterSet(Result("SPY", "AAPL"), index: 0, scope: null);

        parameters["symbols"].Should().Be("SPY,AAPL");
    }

    [Fact]
    public void Symbols_AreNormalizedTheWayTheEngineNormalizesThem()
    {
        // The engine trims, uppercases, and drops repeat occurrences while keeping first-seen
        // order; the recorded identity mirrors that so equivalent requests still hash alike.
        var parameters = QuantLabEndpoints.BuildParameterSet(
            Result(" spy ", "AAPL", "SPY", "  "), index: 0, scope: null);

        parameters["symbols"].Should().Be("SPY,AAPL");
    }

    [Fact]
    public void DifferentSymbolOrders_ProduceDifferentParameterValues()
    {
        var forward = QuantLabEndpoints.BuildParameterSet(Result("SPY", "AAPL"), index: 0, scope: null);
        var reversed = QuantLabEndpoints.BuildParameterSet(Result("AAPL", "SPY"), index: 0, scope: null);

        forward["symbols"].Should().NotBe(reversed["symbols"]);
    }
}
