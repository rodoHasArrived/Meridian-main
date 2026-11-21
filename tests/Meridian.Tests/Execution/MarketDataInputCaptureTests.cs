using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Execution.Adapters;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Tests.Execution;

public sealed class MarketDataInputCaptureTests
{
    [Fact]
    public async Task QualityMonitor_SnapshotsTheAssessmentRangeTimeAndConfiguredThreshold()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-quality-input", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var from = new DateOnly(2026, 11, 1);
            var to = from.AddDays(10);
            var monitor = new Meridian.Infrastructure.Adapters.Core.DataQualityMonitor(
                new Meridian.Infrastructure.Adapters.Core.DataGapAnalyzer(root), root,
                new Meridian.Infrastructure.Adapters.Core.QualityMonitorOptions { AlertThreshold = .93 });
            var assessment = await monitor.CalculateScoreAsync("SYN", from, to);
            var snapshot = monitor.GetCachedInputEvidence("SYN")!;

            snapshot.AssessmentFrom.Should().Be(from);
            snapshot.AssessmentTo.Should().Be(to);
            snapshot.QualityThreshold.Should().Be(.93);
            snapshot.QualityScore.Should().Be(assessment.OverallScore);
            snapshot.QualityAssessedAt.Should().Be(assessment.CalculatedAt);
            MarketDataInputEvidenceTags.ForObservation(snapshot, to.AddDays(1)).QualityScore.Should().BeNull();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PaperFill_CapturesFrozenSourceQualityFromTheSameObservedPrice()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var cache = new LiveMarketDataCache();
        var sources = new List<MarketDataInputEvidence>
        {
            new(ProviderId: "alpaca", EventReference: "trade:1", ObservedAt: observedAt,
                Repaired: true, GapFilled: true, RepairReceiptId: "repair", QualityScore: .4,
                QualityAssessedAt: observedAt, QualityThreshold: .8,
                AssessmentFrom: DateOnly.FromDateTime(observedAt.UtcDateTime), AssessmentTo: DateOnly.FromDateTime(observedAt.UtcDateTime))
        };
        cache.RecordTrade("SYN", Trade(observedAt, 100m, 1), sources);
        sources[0] = new(ProviderId: "later-provider", QualityScore: 1d);
        await using var gateway = new Meridian.Execution.PaperTradingGateway(
            NullLogger<Meridian.Execution.PaperTradingGateway>.Instance, liveFeed: cache);

        var report = await gateway.SubmitOrderAsync(new OrderRequest
        { Symbol = "SYN", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1m });
        cache.RecordTrade("SYN", Trade(observedAt.AddSeconds(1), 200m, 2), [new(ProviderId: "newer-provider")]);
        var serialized = JsonSerializer.Serialize(report, ExecutionJsonContext.Default.ExecutionReport);
        var reloaded = JsonSerializer.Deserialize(serialized, ExecutionJsonContext.Default.ExecutionReport)!;

        report.FillPrice.Should().Be(100m);
        reloaded.MarketDataEvidence.Should().ContainSingle().Which.ProviderId.Should().Be("alpaca");
        reloaded.MarketDataEvidence![0].QualityScore.Should().Be(.4);
        reloaded.MarketDataEvidence[0].Repaired.Should().BeTrue();
        MarketDataInputEvidenceTags.BuildWarnings([reloaded.MarketDataEvidence]).Select(warning => warning.Kind)
            .Should().BeEquivalentTo(["Repaired", "GapFilled", "LowQuality"]);
    }

    [Fact]
    public void FreshnessFiltering_DoesNotAttributeDiscardedQuoteEvidenceToCurrentTrade()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new LiveMarketDataCache();
        cache.RecordQuote("SYN", new(now.AddMinutes(-10), "SYN", 90m, 10, 91m, 10, null, null, 1),
            [new(ProviderId: "stale-quote", QualityScore: .1)]);
        cache.RecordTrade("SYN", Trade(now, 100m, 2),
            [new(ProviderId: "current-trade", QualityScore: .99, QualityThreshold: .8,
                QualityAssessedAt: now, Repaired: false, GapFilled: false,
                AssessmentFrom: DateOnly.FromDateTime(now.UtcDateTime), AssessmentTo: DateOnly.FromDateTime(now.UtcDateTime))]);

        var observed = new PaperObservationFreshnessPolicy(TimeSpan.FromMinutes(1), () => now).Capture(cache, "SYN");

        observed.MarketDataEvidence.Should().ContainSingle().Which.ProviderId.Should().Be("current-trade");
        MarketDataInputEvidenceTags.BuildWarnings([observed.MarketDataEvidence]).Should().BeEmpty();
    }

    [Fact]
    public void LegacyReportSerialization_OmitsAbsentSourceEvidence()
    {
        var report = new ExecutionReport
        {
            OrderId = "legacy", Symbol = "SYN", Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill, OrderStatus = OrderStatus.Filled
        };
        JsonSerializer.Serialize(report, ExecutionJsonContext.Default.ExecutionReport)
            .Should().NotContain("marketDataEvidence");
    }

    private static Trade Trade(DateTimeOffset timestamp, decimal price, long sequence) =>
        new(timestamp, "SYN", price, 10, AggressorSide.Buy, sequence);
}
