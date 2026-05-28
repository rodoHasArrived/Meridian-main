using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Backtesting.Tests;

public sealed class CanonicalBacktestResultNormalizerTests
{
    [Fact]
    public void FromLeanResult_NormalizesSummaryMetricsAndCoverageMetadata()
    {
        using var doc = JsonDocument.Parse("""
        {
          "Period": { "Start": "2026-01-02T00:00:00Z", "End": "2026-01-31T00:00:00Z" },
          "Portfolio": { "StartingCapital": "250000" },
          "Statistics": {
            "Total Return": "12.5%",
            "Sharpe Ratio": "1.34",
            "Total Trades": "42"
          }
        }
        """);

        var result = CanonicalBacktestResultNormalizer.FromLeanResult(
            doc.RootElement,
            backtestId: "lean-42",
            algorithmName: "Mean Reversion");

        result.EngineMetadata.Should().NotBeNull();
        result.EngineMetadata!.EngineId.Should().Be("Lean");
        result.EngineMetadata.ExternalRunId.Should().Be("lean-42");
        result.EngineMetadata.ResultKind.Should().Be(BacktestResultKinds.SummaryOnly);
        result.EngineMetadata.CoverageWarnings.Should().ContainSingle()
            .Which.Should().Contain("summary metrics only");
        result.Request.From.Should().Be(new DateOnly(2026, 1, 2));
        result.Request.To.Should().Be(new DateOnly(2026, 1, 31));
        result.Metrics.InitialCapital.Should().Be(250_000m);
        result.Metrics.FinalEquity.Should().Be(281_250m);
        result.Metrics.TotalReturn.Should().Be(0.125m);
        result.Metrics.SharpeRatio.Should().Be(1.34d);
        result.Metrics.TotalTrades.Should().Be(42);
        result.Fills.Should().BeEmpty();
        result.CashFlows.Should().BeEmpty();
    }

    [Fact]
    public void FromNative_MarksResultAsFullCanonicalCoverage()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2026, 1, 2),
            To: new DateOnly(2026, 1, 3),
            Symbols: ["AAPL"],
            InitialCash: 100_000m);
        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 101_000m,
            GrossPnl: 1_000m,
            NetPnl: 1_000m,
            TotalReturn: 0.01m,
            AnnualizedReturn: 0.01m,
            SharpeRatio: 1d,
            SortinoRatio: 1d,
            CalmarRatio: 1d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 1m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var source = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: Array.Empty<PortfolioSnapshot>(),
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills: Array.Empty<FillEvent>(),
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(1),
            TotalEventsProcessed: 1);

        var result = CanonicalBacktestResultNormalizer.FromNative(source);

        result.EngineMetadata.Should().Be(new BacktestEngineMetadata("MeridianNative", ResultKind: BacktestResultKinds.Full));
        result.Metrics.Should().BeSameAs(source.Metrics);
    }
}
