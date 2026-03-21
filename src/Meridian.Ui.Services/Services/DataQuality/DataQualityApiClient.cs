using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Concrete quality API client built on the shared <see cref="ApiClientService"/> infrastructure.
/// This is the single long-term HTTP pattern for the data-quality feature area.
/// </summary>
public sealed class DataQualityApiClient : DataQualityServiceBase, IDataQualityApiClient
{
    private const string QualityGapRepairRoute = "/api/quality/gaps/{0}/repair";
    private const string QualityRepairAllGapsRoute = "/api/quality/gaps/repair-all";

    private readonly ApiClientService _apiClient;

    public DataQualityApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient;
    }

    public Task<QualityDashboardResponse?> GetDashboardAsync(CancellationToken ct = default)
        => GetAsync<QualityDashboardResponse>(UiApiRoutes.QualityDashboard, ct);

    public async Task<IReadOnlyList<QualityGapResponse>> GetGapsAsync(int count, CancellationToken ct = default)
        => await GetAsync<List<QualityGapResponse>>(
            UiApiRoutes.WithQuery(UiApiRoutes.QualityGaps, $"count={count}"),
            ct)
           ?? new List<QualityGapResponse>();

    public async Task<IReadOnlyList<QualityAnomalyResponse>> GetAnomaliesAsync(int count, CancellationToken ct = default)
        => await GetAsync<List<QualityAnomalyResponse>>(
            UiApiRoutes.WithQuery(UiApiRoutes.QualityAnomalies, $"count={count}"),
            ct)
           ?? new List<QualityAnomalyResponse>();

    public Task<QualityLatencyStatisticsResponse?> GetLatencyStatisticsAsync(CancellationToken ct = default)
        => GetAsync<QualityLatencyStatisticsResponse>(UiApiRoutes.QualityLatencyStatistics, ct);

    public Task<QualityProviderComparisonResponse?> GetProviderComparisonAsync(string symbol, CancellationToken ct = default)
        => GetAsync<QualityProviderComparisonResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.QualityComparison, "symbol", symbol),
            ct);

    public async Task<bool> AcknowledgeAnomalyAsync(string anomalyId, CancellationToken ct = default)
    {
        var (success, _) = await PostWithResponseAsync<QualityActionResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.QualityAnomaliesAcknowledge, "anomalyId", anomalyId),
            null,
            ct);
        return success;
    }

    public async Task<bool> RepairGapAsync(string gapId, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<QualityActionResponse>(
            string.Format(QualityGapRepairRoute, Uri.EscapeDataString(gapId)),
            null,
            ct);
        return response.Success;
    }

    public async Task<bool> RepairAllGapsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<QualityActionResponse>(
            QualityRepairAllGapsRoute,
            null,
            ct);
        return response.Success;
    }

    protected override Task<T?> GetAsync<T>(string endpoint, CancellationToken ct) where T : class
        => _apiClient.GetAsync<T>(endpoint, ct);

    protected override Task<T?> PostAsync<T>(string endpoint, object? body, CancellationToken ct) where T : class
        => _apiClient.PostAsync<T>(endpoint, body, ct);

    protected override async Task<(bool Success, T? Data)> PostWithResponseAsync<T>(string endpoint, object? body, CancellationToken ct)
        where T : class
    {
        var response = await _apiClient.PostWithResponseAsync<T>(endpoint, body, ct);
        return (response.Success, response.Data);
    }
}
