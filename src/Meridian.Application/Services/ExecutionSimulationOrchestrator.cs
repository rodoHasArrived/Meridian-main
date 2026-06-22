using System.Globalization;
using System.Text.Json;
using Meridian.DataIntegration.Historical;

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
    int L2EventCount,
    int TradeEventCount,
    decimal FillRate,
    decimal AvgSlippageBps,
    decimal ConfidenceScore,
    string ConfidenceGrade,
    bool IsInferred,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedAtUtc,
    string OutputDirectory);

public interface IExecutionSimulationOrchestrator
{
    Task<ExecutionSimulationResult> RunAsync(ExecutionSimulationRequest request, CancellationToken ct = default);
}

public sealed class ExecutionSimulationOrchestrator(HistoricalDataQueryService queryService) : IExecutionSimulationOrchestrator
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<ExecutionSimulationResult> RunAsync(ExecutionSimulationRequest request, CancellationToken ct = default)
    {
        Directory.CreateDirectory(request.OutputDirectory);

        var symbols = request.Symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToArray();
        var now = DateTimeOffset.UtcNow;

        var fillTape = new List<string>();
        var lifecycle = new List<string>();
        var diagnostics = new List<string>();
        var totalEvents = 0;
        var totalL2Events = 0;
        var totalTradeEvents = 0;
        var confidenceScores = new List<decimal>();
        var warnings = new List<string>();

        foreach (var symbol in symbols)
        {
            var query = new HistoricalDataQuery(symbol, request.FromDate, request.ToDate);
            var result = await queryService.QueryAsync(query, ct).ConfigureAwait(false);
            var ordered = result.Records.OrderBy(r => r.Timestamp).ThenBy(r => r.SourceFile, StringComparer.Ordinal).ToList();

            var queueState = InferredQueueState.Initial(symbol);
            var ordinal = 0;
            foreach (var record in ordered)
            {
                var tod = TimeOnly.FromDateTime(record.Timestamp.UtcDateTime);
                if (request.WindowStart.HasValue && tod < request.WindowStart.Value)
                    continue;
                if (request.WindowEnd.HasValue && tod > request.WindowEnd.Value)
                    continue;

                queueState = queueState.Advance(record);
                if (queueState.IsL2Event)
                {
                    totalL2Events++;
                }
                if (queueState.IsTradeEvent)
                {
                    totalTradeEvents++;
                }
                confidenceScores.Add(queueState.ConfidenceScore);

                var orderId = $"{symbol}-{ordinal:D6}";
                fillTape.Add(JsonSerializer.Serialize(new
                {
                    symbol,
                    orderId,
                    timestampUtc = record.Timestamp,
                    price = 100m + ordinal,
                    quantity = 1m,
                    dryRun = request.DryRun,
                    isInferred = true,
                    confidenceScore = queueState.ConfidenceScore,
                    inferredQueueAheadAtFill = queueState.EstimatedQueueAhead,
                    fillReason = queueState.FillReason
                }));
                lifecycle.Add(JsonSerializer.Serialize(new
                {
                    symbol,
                    orderId,
                    state = request.DryRun ? "validated" : "filled",
                    timestampUtc = record.Timestamp,
                    isInferred = true,
                    confidenceGrade = queueState.ConfidenceGrade
                }));
                diagnostics.Add(JsonSerializer.Serialize(new
                {
                    symbol,
                    orderId,
                    timestampUtc = record.Timestamp,
                    eventType = record.EventType,
                    displayedSize = queueState.DisplayedSize,
                    tradeQuantity = queueState.TradeQuantity,
                    estimatedQueueAhead = queueState.EstimatedQueueAhead,
                    confidenceScore = queueState.ConfidenceScore,
                    confidenceGrade = queueState.ConfidenceGrade,
                    isInferred = true,
                    inferenceReason = queueState.InferenceReason,
                    source = record.SourceFile
                }));
                ordinal++;
                totalEvents++;
            }
        }

        if (totalEvents == 0)
        {
            warnings.Add("No matching events were found for the requested simulation window.");
        }
        if (totalL2Events == 0)
        {
            warnings.Add("No L2 order-book events were found; queue estimates use conservative trade-only fallback.");
        }
        if (totalTradeEvents == 0)
        {
            warnings.Add("No trade events were found; fill likelihood uses displayed-depth fallback only.");
        }

        var confidenceScore = confidenceScores.Count == 0
            ? 0m
            : Math.Round(confidenceScores.Average(), 4);
        var confidenceGrade = GradeConfidence(confidenceScore);
        var fillRate = totalEvents == 0 ? 0m : 1m;
        var avgSlippageBps = totalEvents == 0 ? 0m : Math.Round((1m - confidenceScore) * 10m, 4);

        await File.WriteAllLinesAsync(Path.Combine(request.OutputDirectory, "fill-tape.jsonl"), fillTape, ct);
        await File.WriteAllLinesAsync(Path.Combine(request.OutputDirectory, "order-lifecycle.jsonl"), lifecycle, ct);
        await File.WriteAllLinesAsync(Path.Combine(request.OutputDirectory, "queue-diagnostics.jsonl"), diagnostics, ct);

        var summary = new ExecutionSimulationSummary(
            symbols,
            request.FromDate?.ToString("yyyy-MM-dd"),
            request.ToDate?.ToString("yyyy-MM-dd"),
            request.DryRun,
            totalEvents,
            totalL2Events,
            totalTradeEvents,
            fillRate,
            avgSlippageBps,
            confidenceScore,
            confidenceGrade,
            IsInferred: true,
            warnings,
            now,
            request.OutputDirectory);
        await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "summary.json"), JsonSerializer.Serialize(summary, Json), ct);

        return new ExecutionSimulationResult(request.OutputDirectory, symbols.Length, totalEvents, request.DryRun);
    }

    private static string GradeConfidence(decimal score) =>
        score >= 0.80m ? "HIGH" :
        score >= 0.50m ? "MEDIUM" :
        "LOW";

    private sealed record InferredQueueState(
        string Symbol,
        decimal DisplayedSize,
        decimal TradeQuantity,
        decimal EstimatedQueueAhead,
        decimal ConfidenceScore,
        string ConfidenceGrade,
        string FillReason,
        string InferenceReason,
        bool IsL2Event,
        bool IsTradeEvent)
    {
        public static InferredQueueState Initial(string symbol) =>
            new(symbol, 0m, 0m, 0m, 0.25m, "LOW", "heuristic-fallback", "No prior book state.", IsL2Event: false, IsTradeEvent: false);

        public InferredQueueState Advance(HistoricalDataRecord record)
        {
            var eventType = record.EventType ?? string.Empty;
            var isTrade = eventType.Contains("Trade", StringComparison.OrdinalIgnoreCase);
            var isL2 = eventType.Contains("L2", StringComparison.OrdinalIgnoreCase) ||
                       eventType.Contains("OrderBook", StringComparison.OrdinalIgnoreCase) ||
                       eventType.Contains("Depth", StringComparison.OrdinalIgnoreCase);
            var displayedSize = TryReadDisplayedSize(record.RawJson, out var observedDisplayedSize)
                ? observedDisplayedSize
                : DisplayedSize;
            var tradeQuantity = isTrade && TryReadTradeQuantity(record.RawJson, out var observedTradeQuantity)
                ? observedTradeQuantity
                : 0m;

            var estimatedQueueAhead = EstimatedQueueAhead;
            var reason = "Heuristic carry-forward from prior event.";
            var score = ConfidenceScore;

            if (isL2)
            {
                estimatedQueueAhead = displayedSize;
                reason = "Displayed L2 depth initializes inferred queue ahead.";
                score = isTrade ? 0.90m : 0.82m;
            }
            else if (isTrade)
            {
                estimatedQueueAhead = Math.Max(0m, EstimatedQueueAhead - tradeQuantity);
                reason = "Trade consumption depletes inferred queue ahead.";
                score = DisplayedSize > 0m ? 0.72m : 0.40m;
            }
            else
            {
                score = DisplayedSize > 0m ? 0.55m : 0.25m;
            }

            return this with
            {
                DisplayedSize = displayedSize,
                TradeQuantity = tradeQuantity,
                EstimatedQueueAhead = estimatedQueueAhead,
                ConfidenceScore = score,
                ConfidenceGrade = GradeConfidence(score),
                FillReason = isTrade ? "contra-trade-consumption" : isL2 ? "displayed-depth-observation" : "heuristic-fallback",
                InferenceReason = reason,
                IsL2Event = isL2,
                IsTradeEvent = isTrade
            };
        }

        private static bool TryReadDisplayedSize(string? rawJson, out decimal displayedSize)
        {
            displayedSize = 0m;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (TryReadDepthFromElement(root, out displayedSize))
            {
                return true;
            }

            return root.TryGetProperty("payload", out var payload) &&
                   payload.ValueKind == JsonValueKind.Object &&
                   TryReadDepthFromElement(payload, out displayedSize);
        }

        private static bool TryReadDepthFromElement(JsonElement element, out decimal displayedSize)
        {
            displayedSize = 0m;
            var found = false;
            if (element.TryGetProperty("bids", out var bids) && bids.ValueKind == JsonValueKind.Array)
            {
                displayedSize += SumLevelSizes(bids);
                found = true;
            }
            if (element.TryGetProperty("asks", out var asks) && asks.ValueKind == JsonValueKind.Array)
            {
                displayedSize += SumLevelSizes(asks);
                found = true;
            }
            if (element.TryGetProperty("size", out var size) && TryGetDecimal(size, out var singleLevelSize))
            {
                displayedSize += singleLevelSize;
                found = true;
            }
            if (element.TryGetProperty("quantity", out var quantity) && TryGetDecimal(quantity, out var singleLevelQuantity))
            {
                displayedSize += singleLevelQuantity;
                found = true;
            }

            return found;
        }

        private static decimal SumLevelSizes(JsonElement levels)
        {
            var total = 0m;
            foreach (var level in levels.EnumerateArray())
            {
                if (level.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (level.TryGetProperty("size", out var size) && TryGetDecimal(size, out var levelSize))
                {
                    total += levelSize;
                }
                else if (level.TryGetProperty("quantity", out var quantity) && TryGetDecimal(quantity, out var levelQuantity))
                {
                    total += levelQuantity;
                }
            }

            return total;
        }

        private static bool TryReadTradeQuantity(string? rawJson, out decimal quantity)
        {
            quantity = 0m;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            return TryReadQuantityFromElement(root, out quantity) ||
                   root.TryGetProperty("payload", out var payload) &&
                   payload.ValueKind == JsonValueKind.Object &&
                   TryReadQuantityFromElement(payload, out quantity);
        }

        private static bool TryReadQuantityFromElement(JsonElement element, out decimal quantity)
        {
            if (element.TryGetProperty("quantity", out var quantityElement) && TryGetDecimal(quantityElement, out quantity))
            {
                return true;
            }
            if (element.TryGetProperty("size", out var sizeElement) && TryGetDecimal(sizeElement, out quantity))
            {
                return true;
            }

            quantity = 0m;
            return false;
        }

        private static bool TryGetDecimal(JsonElement element, out decimal value)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
            {
                return true;
            }
            if (element.ValueKind == JsonValueKind.String &&
                decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            value = 0m;
            return false;
        }
    }
}
