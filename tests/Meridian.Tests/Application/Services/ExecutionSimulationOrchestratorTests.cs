using FluentAssertions;
using Meridian.Application.Services;
using System.Text.Json;

namespace Meridian.Tests.Application.Services;

public class ExecutionSimulationOrchestratorTests
{
    [Fact]
    public async Task RunAsync_WritesExpectedArtifacts_AndHonorsDryRun()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "sim-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dataRoot);
        var symbolDir = Path.Combine(dataRoot, "AAPL", "trades", "2026", "01");
        Directory.CreateDirectory(symbolDir);
        var file = Path.Combine(symbolDir, "AAPL_2026-01-02.jsonl");
        await File.WriteAllLinesAsync(file, [
            "{\"symbol\":\"AAPL\",\"timestampUtc\":\"2026-01-02T14:30:00Z\"}",
            "{\"symbol\":\"AAPL\",\"timestampUtc\":\"2026-01-02T14:31:00Z\"}"
        ]);

        var orchestrator = new ExecutionSimulationOrchestrator(new HistoricalDataQueryService(dataRoot));
        var output = Path.Combine(dataRoot, "out");
        var result = await orchestrator.RunAsync(new ExecutionSimulationRequest(["AAPL"], new DateOnly(2026,1,1), new DateOnly(2026,1,31), null, null, true, output));

        result.DryRun.Should().BeTrue();
        File.Exists(Path.Combine(output, "fill-tape.jsonl")).Should().BeTrue();
        File.Exists(Path.Combine(output, "order-lifecycle.jsonl")).Should().BeTrue();
        File.Exists(Path.Combine(output, "summary.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "queue-diagnostics.jsonl")).Should().BeTrue();

        var fillLines = await File.ReadAllLinesAsync(Path.Combine(output, "fill-tape.jsonl"));
        fillLines.Should().BeInAscendingOrder();

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "summary.json")));
        summary.RootElement.GetProperty("isInferred").GetBoolean().Should().BeTrue();
        summary.RootElement.GetProperty("confidenceGrade").GetString().Should().Be("LOW");
        summary.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(warning => warning!.Contains("No L2 order-book events", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_WithL2AndTrades_EmitsInferredQueueDiagnosticsAndConfidence()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "sim-l3-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dataRoot);
        var symbolDir = Path.Combine(dataRoot, "AAPL", "l2", "2026", "01");
        Directory.CreateDirectory(symbolDir);
        var file = Path.Combine(symbolDir, "AAPL_2026-01-02.jsonl");
        await File.WriteAllLinesAsync(file, [
            "{\"symbol\":\"AAPL\",\"type\":\"L2Snapshot\",\"timestamp\":\"2026-01-02T14:30:00Z\",\"bids\":[{\"price\":100.00,\"size\":100}],\"asks\":[{\"price\":100.01,\"size\":120}]}",
            "{\"symbol\":\"AAPL\",\"type\":\"Trade\",\"timestamp\":\"2026-01-02T14:30:01Z\",\"price\":100.01,\"quantity\":40}",
            "{\"symbol\":\"AAPL\",\"type\":\"Trade\",\"timestamp\":\"2026-01-02T14:30:02Z\",\"price\":100.01,\"quantity\":70}"
        ]);

        var orchestrator = new ExecutionSimulationOrchestrator(new HistoricalDataQueryService(dataRoot));
        var output = Path.Combine(dataRoot, "out");
        var result = await orchestrator.RunAsync(new ExecutionSimulationRequest(["AAPL"], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, false, output));

        result.EventCount.Should().Be(3);

        var diagnostics = (await File.ReadAllLinesAsync(Path.Combine(output, "queue-diagnostics.jsonl")))
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        diagnostics.Should().HaveCount(3);
        diagnostics[0].RootElement.GetProperty("isInferred").GetBoolean().Should().BeTrue();
        diagnostics[0].RootElement.GetProperty("estimatedQueueAhead").GetDecimal().Should().Be(220m);
        diagnostics[1].RootElement.GetProperty("estimatedQueueAhead").GetDecimal().Should().Be(180m);
        diagnostics[2].RootElement.GetProperty("estimatedQueueAhead").GetDecimal().Should().Be(110m);

        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "summary.json")));
        summary.RootElement.GetProperty("isInferred").GetBoolean().Should().BeTrue();
        summary.RootElement.GetProperty("l2EventCount").GetInt32().Should().Be(1);
        summary.RootElement.GetProperty("tradeEventCount").GetInt32().Should().Be(2);
        summary.RootElement.GetProperty("confidenceGrade").GetString().Should().Be("MEDIUM");
        summary.RootElement.GetProperty("fillRate").GetDecimal().Should().Be(1m);
        summary.RootElement.GetProperty("avgSlippageBps").GetDecimal().Should().BeGreaterThanOrEqualTo(0m);
    }
}
