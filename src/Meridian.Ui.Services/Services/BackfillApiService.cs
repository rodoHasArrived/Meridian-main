using System.Collections.Generic;
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
        return await _apiClient.GetAsync<BackfillHealthResponse>(UiApiRoutes.BackfillHealth, ct);
    }

    /// <summary>
    /// Runs an immediate gap-fill operation.
    /// </summary>
    public async Task<BackfillExecutionResponse?> RunGapFillAsync(
        string[] symbols,
        int lookbackDays = 30,
        string priority = "High",
        CancellationToken ct = default)
    {
        var request = new
        {
            Symbols = symbols,
            LookbackDays = lookbackDays,
            Priority = priority
        };

        var backfillClient = _apiClient.GetBackfillClient();
        var response = await _apiClient.PostWithResponseAsync<BackfillExecutionResponse>(
            UiApiRoutes.BackfillGapFill,
            request,
            ct,
            backfillClient);
        return response.Data;
    }

    /// <summary>
    /// Gets available backfill presets.
    /// </summary>
    public async Task<List<BackfillPreset>> GetPresetsAsync(CancellationToken ct = default)
    {
        var result = await _apiClient.GetAsync<List<BackfillPreset>>(UiApiRoutes.BackfillPresets, ct);
        return result ?? new List<BackfillPreset>();
    }

    /// <summary>
    /// Gets backfill execution history.
    /// </summary>
    public async Task<List<BackfillExecution>> GetExecutionHistoryAsync(int limit = 50, CancellationToken ct = default)
    {
        var result = await _apiClient.GetAsync<List<BackfillExecution>>(
            UiApiRoutes.WithQuery(UiApiRoutes.BackfillExecutions, $"limit={limit}"), ct);
        return result ?? new List<BackfillExecution>();
    }

    /// <summary>
    /// Gets backfill statistics.
    /// </summary>
    public async Task<BackfillStatistics?> GetStatisticsAsync(int? hours = null, CancellationToken ct = default)
    {
        var route = hours.HasValue
            ? UiApiRoutes.WithQuery(UiApiRoutes.BackfillStatistics, $"hours={hours.Value}")
            : UiApiRoutes.BackfillStatistics;

        return await _apiClient.GetAsync<BackfillStatistics>(route, ct);
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
