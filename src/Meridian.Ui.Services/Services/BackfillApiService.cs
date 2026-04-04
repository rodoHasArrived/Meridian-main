using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing backfill operations via the core API.
/// </summary>
public sealed class BackfillApiService
{
    private readonly ApiClientService _apiClient;

    public BackfillApiService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Gets the list of available backfill providers.
    /// </summary>
    public async Task<List<BackfillProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
    {
        var result = await _apiClient.GetAsync<List<BackfillProviderInfo>>(UiApiRoutes.BackfillProviders, ct);
        return result ?? new List<BackfillProviderInfo>();
    }

    /// <summary>
    /// Gets the last backfill operation status.
    /// </summary>
    public async Task<BackfillResultDto?> GetLastStatusAsync(CancellationToken ct = default)
    {
        return await _apiClient.GetAsync<BackfillResultDto>(UiApiRoutes.BackfillStatus, ct);
    }

    /// <summary>
    /// Runs a backfill operation for the specified symbols.
    /// </summary>
    public async Task<BackfillResultDto?> RunBackfillAsync(
        string provider,
        string[] symbols,
        string? from,
        string? to,
        string granularity = "Daily",
        CancellationToken ct = default)
    {
        var request = new BackfillRequest
        {
            Provider = provider,
            Symbols = symbols,
            From = from,
            To = to,
            Granularity = granularity
        };

        var backfillClient = _apiClient.GetBackfillClient();
        var response = await _apiClient.PostWithResponseAsync<BackfillResultDto>(
            UiApiRoutes.BackfillRun,
            request,
            ct,
            backfillClient);

        if (response.Success)
        {
            return response.Data;
        }

        return new BackfillResultDto
        {
            Success = false,
            Error = response.ErrorMessage ?? "Backfill request failed"
        };
    }

    /// <summary>
    /// Checks the health of backfill providers.
    /// </summary>
    public async Task<BackfillHealthResponse?> CheckProviderHealthAsync(CancellationToken ct = default)
    {
        var result = await _apiClient.GetAsync<BackfillHealthEnvelope>(UiApiRoutes.BackfillHealth, ct);
        if (result == null)
        {
            return null;
        }

        return new BackfillHealthResponse
        {
            IsHealthy = result.Healthy,
            Providers = new Dictionary<string, BackfillProviderHealth>()
        };
    }

    /// <summary>
    /// Runs an immediate gap-fill operation.
    /// </summary>
    public async Task<BackfillResultDto?> RunGapFillAsync(
        string[] symbols,
        int lookbackDays = 30,
        string priority = "High",
        CancellationToken ct = default)
    {
        var request = new
        {
            Symbols = symbols,
            Provider = "composite",
            From = DateOnly.FromDateTime(System.DateTime.UtcNow.AddDays(-lookbackDays)),
            To = DateOnly.FromDateTime(System.DateTime.UtcNow)
        };

        var backfillClient = _apiClient.GetBackfillClient();
        var response = await _apiClient.PostWithResponseAsync<BackfillResultDto>(
            UiApiRoutes.BackfillGapFill,
            request,
            ct,
            backfillClient);
        return response.Success ? response.Data : null;
    }

    /// <summary>
    /// Gets available backfill presets.
    /// </summary>
    public async Task<List<BackfillPreset>> GetPresetsAsync(CancellationToken ct = default)
    {
        var result = await _apiClient.GetAsync<BackfillPresetsEnvelope>(UiApiRoutes.BackfillPresets, ct);
        if (result?.Presets == null)
        {
            return new List<BackfillPreset>();
        }

        return result.Presets
            .Select(p => new BackfillPreset
            {
                Name = p.Name ?? string.Empty,
                DisplayName = p.Name ?? string.Empty,
                Description = p.Description ?? string.Empty,
                CronExpression = p.CronExpression ?? string.Empty,
                LookbackDays = 30
            })
            .ToList();
    }

    /// <summary>
    /// Gets backfill execution history.
    /// </summary>
    public async Task<List<BackfillExecution>> GetExecutionHistoryAsync(int limit = 50, CancellationToken ct = default)
    {
        var result = await _apiClient.GetAsync<BackfillExecutionsEnvelope>(
            UiApiRoutes.WithQuery(UiApiRoutes.BackfillExecutions, $"limit={limit}"), ct);
        if (result?.Executions == null)
        {
            return new List<BackfillExecution>();
        }

        return result.Executions
            .Select(exec => new BackfillExecution
            {
                Id = exec.ExecutionId ?? string.Empty,
                ScheduleId = exec.ScheduleId ?? string.Empty,
                Status = exec.Status ?? "Pending",
                StartedAt = exec.StartedAt?.UtcDateTime ?? exec.ScheduledAt.UtcDateTime,
                CompletedAt = exec.CompletedAt?.UtcDateTime,
                SymbolsProcessed = exec.Symbols?.Count ?? 0,
                BarsDownloaded = (int)Math.Min(int.MaxValue, exec.BarsRetrieved),
                ErrorMessage = exec.ErrorMessage
            })
            .ToList();
    }

    /// <summary>
    /// Gets backfill statistics.
    /// </summary>
    public async Task<BackfillStatistics?> GetStatisticsAsync(int? hours = null, CancellationToken ct = default)
    {
        var route = hours.HasValue
            ? UiApiRoutes.WithQuery(UiApiRoutes.BackfillStatistics, $"hours={hours.Value}")
            : UiApiRoutes.BackfillStatistics;

        var result = await _apiClient.GetAsync<BackfillStatisticsEnvelope>(route, ct);
        if (result?.Executions == null)
        {
            return null;
        }

        return new BackfillStatistics
        {
            TotalExecutions = result.Executions.TotalExecutions,
            SuccessfulExecutions = result.Executions.SuccessfulExecutions,
            FailedExecutions = result.Executions.FailedExecutions,
            TotalBarsDownloaded = result.Executions.TotalBarsDownloaded,
            AverageExecutionTimeSeconds = result.Executions.AverageExecutionTimeSeconds,
            LastSuccessfulExecution = result.Executions.LastSuccessfulExecution?.UtcDateTime
        };
    }

    /// <summary>
    /// Gets real gap analysis for a symbol from the quality monitoring service.
    /// Returns null when the backend is unreachable or has no data for the symbol.
    /// </summary>
    public async Task<SymbolGapAnalysisDto?> GetSymbolGapAnalysisAsync(
        string symbol,
        string? date = null,
        CancellationToken ct = default)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.QualityGapsBySymbol, "symbol", symbol);
        if (date != null)
            route = UiApiRoutes.WithQuery(route, $"date={Uri.EscapeDataString(date)}");

        return await _apiClient.GetAsync<SymbolGapAnalysisDto>(route, ct);
    }

    /// <summary>
    /// Gets all configured backfill schedules.
    /// </summary>
    public async Task<List<BackfillScheduleSummary>> GetSchedulesAsync(CancellationToken ct = default)
    {
        var result = await _apiClient.GetAsync<BackfillSchedulesEnvelope>(UiApiRoutes.BackfillSchedules, ct);
        return result?.Schedules ?? new List<BackfillScheduleSummary>();
    }

    /// <summary>
    /// Creates a new backfill schedule.
    /// </summary>
    public async Task<BackfillScheduleSummary?> CreateScheduleAsync(
        BackfillScheduleCreateRequest request,
        CancellationToken ct = default)
    {
        return await _apiClient.PostAsync<BackfillScheduleSummary>(UiApiRoutes.BackfillSchedules, request, ct);
    }

    /// <summary>
    /// Deletes a schedule by id.
    /// </summary>
    public async Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.BackfillSchedulesById, "id", scheduleId);
        var response = await _apiClient.DeleteWithResponseAsync<DeleteScheduleResponse>(route, ct);
        return response.Success;
    }

    /// <summary>
    /// Triggers a schedule immediately.
    /// </summary>
    public async Task<BackfillExecutionResponse?> RunScheduleNowAsync(string scheduleId, CancellationToken ct = default)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.BackfillSchedulesRun, "id", scheduleId);
        return await _apiClient.PostAsync<BackfillExecutionResponse>(route, body: null, ct);
    }
}

/// <summary>
/// Minimal DTO for deserializing the /api/quality/gaps/{symbol} response.
/// Only the fields used by the gap analysis UI are mapped here.
/// </summary>
public sealed class SymbolGapAnalysisDto
{
    public string Symbol { get; set; } = "";
    public int TotalGaps { get; set; }
    /// <summary>0–100 percentage of trading time with data present.</summary>
    public double DataAvailabilityPercent { get; set; }
}

public sealed class BackfillScheduleCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string CronExpression { get; set; } = "0 6 * * *";
    public string TimeZoneId { get; set; } = "UTC";
    public int BackfillType { get; set; }
    public List<string> Symbols { get; set; } = new();
    public int LookbackDays { get; set; } = 30;
    public int Granularity { get; set; } = 6;
    public List<string> PreferredProviders { get; set; } = new();
}

public sealed class BackfillScheduleSummary
{
    public string ScheduleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";
    public string BackfillType { get; set; } = "GapFill";
    public List<string> Symbols { get; set; } = new();
    public int LookbackDays { get; set; } = 30;
    public string Granularity { get; set; } = "Daily";
    public List<string> PreferredProviders { get; set; } = new();
    public DateTimeOffset? NextExecutionAt { get; set; }
}

file sealed class BackfillHealthEnvelope
{
    public bool Healthy { get; set; }
}

file sealed class BackfillPresetsEnvelope
{
    public List<BackfillPresetEnvelopeItem>? Presets { get; set; }
}

file sealed class BackfillPresetEnvelopeItem
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
}

file sealed class BackfillExecutionsEnvelope
{
    public List<BackfillExecutionEnvelopeItem>? Executions { get; set; }
}

file sealed class BackfillExecutionEnvelopeItem
{
    public string? ExecutionId { get; set; }
    public string? ScheduleId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<string>? Symbols { get; set; }
    public long BarsRetrieved { get; set; }
    public string? ErrorMessage { get; set; }
}

file sealed class BackfillStatisticsEnvelope
{
    public BackfillStatisticsPayload? Executions { get; set; }
}

file sealed class BackfillStatisticsPayload
{
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public long TotalBarsDownloaded { get; set; }
    public float AverageExecutionTimeSeconds { get; set; }
    public DateTimeOffset? LastSuccessfulExecution { get; set; }
}

file sealed class BackfillSchedulesEnvelope
{
    public List<BackfillScheduleSummary>? Schedules { get; set; }
}

file sealed class DeleteScheduleResponse
{
}
