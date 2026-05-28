using System.Text.Json;

namespace Meridian.Application.Services;

public sealed record ExecutionSimulationRequest(
    IReadOnlyList<string> Symbols,
    DateOnly? FromDate,
    DateOnly? ToDate,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd,
    bool DryRun,
    string OutputDirectory);

public sealed record ExecutionSimulationResult(
    string OutputDirectory,
    int SymbolsProcessed,
    int EventCount,
    bool DryRun);

internal sealed record ExecutionSimulationSummary(
    IReadOnlyList<string> Symbols,
    string? FromDate,
    string? ToDate,
    bool DryRun,
    int EventCount,
    DateTimeOffset GeneratedAtUtc,
    string OutputDirectory);

public interface IExecutionSimulationOrchestrator
{
    Task<ExecutionSimulationResult> RunAsync(ExecutionSimulationRequest request, CancellationToken ct = default);
}

public sealed class ExecutionSimulationOrchestrator(HistoricalDataQueryService queryService) : IExecutionSimulationOrchestrator
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<ExecutionSimulationResult> RunAsync(ExecutionSimulationRequest request, CancellationToken ct = default)
    {
        Directory.CreateDirectory(request.OutputDirectory);

        var symbols = request.Symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToArray();
        var now = DateTimeOffset.UtcNow;

        var fillTape = new List<string>();
        var lifecycle = new List<string>();
        var diagnostics = new List<string>();
        var totalEvents = 0;

        foreach (var symbol in symbols)
        {
            var query = new HistoricalDataQuery(symbol, request.FromDate, request.ToDate, Limit: 250);
            var result = await queryService.QueryAsync(query, ct).ConfigureAwait(false);
            var ordered = result.Records.OrderBy(r => r.Timestamp).ThenBy(r => r.SourceFile, StringComparer.Ordinal).ToList();

            var ordinal = 0;
            foreach (var record in ordered)
            {
                var tod = TimeOnly.FromDateTime(record.Timestamp.UtcDateTime);
                if (request.WindowStart.HasValue && tod < request.WindowStart.Value) continue;
                if (request.WindowEnd.HasValue && tod > request.WindowEnd.Value) continue;

                var orderId = $"{symbol}-{ordinal:D6}";
                fillTape.Add(JsonSerializer.Serialize(new { symbol, orderId, timestampUtc = record.Timestamp, price = 100m + ordinal, quantity = 1m, dryRun = request.DryRun }));
                lifecycle.Add(JsonSerializer.Serialize(new { symbol, orderId, state = request.DryRun ? "validated" : "filled", timestampUtc = record.Timestamp }));
                diagnostics.Add(JsonSerializer.Serialize(new { symbol, orderId, queuePosition = ordinal + 1, source = record.SourceFile }));
                ordinal++;
                totalEvents++;
            }
        }

        await File.WriteAllLinesAsync(Path.Combine(request.OutputDirectory, "fill-tape.jsonl"), fillTape, ct);
        await File.WriteAllLinesAsync(Path.Combine(request.OutputDirectory, "order-lifecycle.jsonl"), lifecycle, ct);
        await File.WriteAllLinesAsync(Path.Combine(request.OutputDirectory, "queue-diagnostics.jsonl"), diagnostics, ct);

        var summary = new ExecutionSimulationSummary(
            symbols,
            request.FromDate?.ToString("yyyy-MM-dd"),
            request.ToDate?.ToString("yyyy-MM-dd"),
            request.DryRun,
            totalEvents,
            now,
            request.OutputDirectory);
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "summary.json"), JsonSerializer.Serialize(summary, Json), ct);

        return new ExecutionSimulationResult(request.OutputDirectory, symbols.Length, totalEvents, request.DryRun);
    }
}
