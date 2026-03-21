using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

// ============================================================
// Backfill Health and Provider Status DTOs
// ============================================================

/// <summary>
/// Response model for backfill provider health check.
/// </summary>
public sealed class BackfillHealthResponse
{
    /// <summary>
    /// Gets or sets whether the backfill system reports healthy status.
    /// </summary>
    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Gets or sets per-provider health details keyed by provider identifier.
    /// </summary>
    [JsonPropertyName("providers")]
    public Dictionary<string, BackfillProviderHealth>? Providers { get; set; }
}

/// <summary>
/// Individual backfill provider health status.
/// </summary>
public sealed class BackfillProviderHealth
{
    /// <summary>
    /// Gets or sets whether the provider is currently available.
    /// </summary>
    [JsonPropertyName("isAvailable")]
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Gets or sets the most recent latency measurement in milliseconds.
    /// </summary>
    [JsonPropertyName("latencyMs")]
    public float? LatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the last error message reported by the provider, if any.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the last time the provider was checked.
    /// </summary>
    [JsonPropertyName("lastChecked")]
    public DateTime? LastChecked { get; set; }
}

// ============================================================
// Symbol Resolution DTOs
// ============================================================

/// <summary>
/// Symbol resolution result for backfill operations.
/// </summary>
public sealed class SymbolResolutionResponse
{
    /// <summary>
    /// Gets or sets the original symbol requested for resolution.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved symbol returned by the provider.
    /// </summary>
    [JsonPropertyName("resolvedSymbol")]
    public string? ResolvedSymbol { get; set; }

    /// <summary>
    /// Gets or sets the exchange associated with the resolved symbol.
    /// </summary>
    [JsonPropertyName("exchange")]
    public string? Exchange { get; set; }

    /// <summary>
    /// Gets or sets the currency code associated with the resolved symbol.
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>
    /// Gets or sets the security type for the resolved symbol.
    /// </summary>
    [JsonPropertyName("securityType")]
    public string? SecurityType { get; set; }

    /// <summary>
    /// Gets or sets provider-specific symbol mapping values.
    /// </summary>
    [JsonPropertyName("providerMappings")]
    public Dictionary<string, string>? ProviderMappings { get; set; }
}

// ============================================================
// Backfill Execution DTOs
// ============================================================

/// <summary>
/// Response for backfill execution initiation.
/// </summary>
public sealed class BackfillExecutionResponse
{
    /// <summary>
    /// Gets or sets the execution identifier for the backfill request.
    /// </summary>
    [JsonPropertyName("executionId")]
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current status of the backfill execution.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Gets or sets the timestamp when the execution started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the list of symbols included in the execution.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Backfill execution record for history tracking.
/// </summary>
public sealed class BackfillExecution
{
    /// <summary>
    /// Gets or sets the unique execution identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schedule identifier that triggered the execution.
    /// </summary>
    [JsonPropertyName("scheduleId")]
    public string ScheduleId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current execution status.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Gets or sets the time the execution started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the time the execution completed, if finished.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of symbols processed by the execution.
    /// </summary>
    [JsonPropertyName("symbolsProcessed")]
    public int SymbolsProcessed { get; set; }

    /// <summary>
    /// Gets or sets the number of bars downloaded during execution.
    /// </summary>
    [JsonPropertyName("barsDownloaded")]
    public int BarsDownloaded { get; set; }

    /// <summary>
    /// Gets or sets the error message if the execution failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ============================================================
// Backfill Preset DTOs
// ============================================================

/// <summary>
/// Backfill preset definition for scheduled operations.
/// </summary>
public sealed class BackfillPreset
{
    /// <summary>
    /// Gets or sets the preset identifier.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the friendly display name for the preset.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the preset.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cron expression that schedules the preset.
    /// </summary>
    [JsonPropertyName("cronExpression")]
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lookback window in days for backfill.
    /// </summary>
    [JsonPropertyName("lookbackDays")]
    public int LookbackDays { get; set; }
}

// ============================================================
// Backfill Statistics DTOs
// ============================================================

/// <summary>
/// Backfill statistics summary.
/// </summary>
public sealed class BackfillStatistics
{
    /// <summary>
    /// Gets or sets the total number of backfill executions.
    /// </summary>
    [JsonPropertyName("totalExecutions")]
    public int TotalExecutions { get; set; }

    /// <summary>
    /// Gets or sets the number of successful executions.
    /// </summary>
    [JsonPropertyName("successfulExecutions")]
    public int SuccessfulExecutions { get; set; }

    /// <summary>
    /// Gets or sets the number of failed executions.
    /// </summary>
    [JsonPropertyName("failedExecutions")]
    public int FailedExecutions { get; set; }

    /// <summary>
    /// Gets or sets the total count of bars downloaded.
    /// </summary>
    [JsonPropertyName("totalBarsDownloaded")]
    public long TotalBarsDownloaded { get; set; }

    /// <summary>
    /// Gets or sets the average execution time in seconds.
    /// </summary>
    [JsonPropertyName("averageExecutionTimeSeconds")]
    public float AverageExecutionTimeSeconds { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last successful execution.
    /// </summary>
    [JsonPropertyName("lastSuccessfulExecution")]
    public DateTime? LastSuccessfulExecution { get; set; }
}

// ============================================================
// Gap-Fill Request DTO
// ============================================================

/// <summary>
/// Request for running a gap-fill operation.
/// </summary>
public sealed class GapFillRequest
{
    /// <summary>
    /// Gets or sets the symbols targeted by the gap-fill request.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the lookback window in days for the gap-fill.
    /// </summary>
    [JsonPropertyName("lookbackDays")]
    public int LookbackDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the processing priority for the request.
    /// </summary>
    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "High";
}
