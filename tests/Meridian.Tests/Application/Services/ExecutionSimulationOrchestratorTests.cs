using FluentAssertions;
using Meridian.Application.Services;

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
    }
}
