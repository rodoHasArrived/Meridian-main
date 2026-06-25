using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Platform.Results;
using Meridian.Application.Services;

namespace Meridian.Tests.Application.Commands;

public class SimulationCommandsTests
{
    [Fact]
    public void CanHandle_WhenSimulateFlagPresent()
    {
        var cmd = new SimulationCommands(new FakeOrchestrator());
        cmd.CanHandle(["--simulate-execution", "--symbols", "AAPL"]).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Fails_WhenSymbolsMissing()
    {
        var cmd = new SimulationCommands(new FakeOrchestrator());
        var result = await CommandTestConsole.CaptureErrorAsync(
            () => cmd.ExecuteAsync(["--simulate-execution"]));
        result.Error.Should().Be(ErrorCode.RequiredFieldMissing);
    }

    private sealed class FakeOrchestrator : IExecutionSimulationOrchestrator
    {
        public Task<ExecutionSimulationResult> RunAsync(ExecutionSimulationRequest request, CancellationToken ct = default)
            => Task.FromResult(new ExecutionSimulationResult(request.OutputDirectory, request.Symbols.Count, 0, request.DryRun));
    }
}
