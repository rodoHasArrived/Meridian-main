using Meridian.Application.ResultTypes;
using Meridian.Application.Services;

namespace Meridian.Application.Commands;

internal sealed class SimulationCommands(IExecutionSimulationOrchestrator orchestrator) : ICliCommand
{
    public bool CanHandle(string[] args) => CliArguments.HasFlag(args, "--simulate-execution");

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var symbolsRaw = CliArguments.RequireValue(args, "--symbols", "--symbols <comma-separated symbols>");
        if (string.IsNullOrWhiteSpace(symbolsRaw))
        {
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        var fromRaw = CliArguments.GetValue(args, "--sim-from") ?? CliArguments.GetValue(args, "--from");
        var toRaw = CliArguments.GetValue(args, "--sim-to") ?? CliArguments.GetValue(args, "--to");
        var windowStartRaw = CliArguments.GetValue(args, "--sim-window-start");
        var windowEndRaw = CliArguments.GetValue(args, "--sim-window-end");
        var outputDir = CliArguments.GetValue(args, "--sim-output-dir") ?? Path.Combine("artifacts", "simulation", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

        DateOnly? from = DateOnly.TryParse(fromRaw, out var f) ? f : null;
        DateOnly? to = DateOnly.TryParse(toRaw, out var t) ? t : null;
        TimeOnly? windowStart = TimeOnly.TryParse(windowStartRaw, out var ws) ? ws : null;
        TimeOnly? windowEnd = TimeOnly.TryParse(windowEndRaw, out var we) ? we : null;

        var request = new ExecutionSimulationRequest(
            symbolsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            from,
            to,
            windowStart,
            windowEnd,
            CliArguments.HasFlag(args, "--dry-run"),
            outputDir);

        var result = await orchestrator.RunAsync(request, ct).ConfigureAwait(false);
        Console.WriteLine($"Simulation completed. symbols={result.SymbolsProcessed}, events={result.EventCount}, dryRun={result.DryRun}");
        Console.WriteLine($"Artifacts: {result.OutputDirectory}");
        return CliResult.Ok();
    }
}
