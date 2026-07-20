using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Services;
using Meridian.Contracts.Api;
using Meridian.Contracts.Api.Quality;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Concrete quality API client built on the shared <see cref="ApiClientService"/> infrastructure.
/// This is the single long-term HTTP pattern for the data-quality feature area.
/// </summary>
public sealed class DataQualityApiClient : DataQualityServiceBase, IDataQualityApiClient
{
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

    public Task<QualityComparisonResponse?> GetProviderComparisonAsync(string symbol, CancellationToken ct = default)
        => GetAsync<QualityComparisonResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.QualityComparison, "symbol", symbol),
            ct);

    public async Task<bool> AcknowledgeAnomalyAsync(string anomalyId, CancellationToken ct = default)
    {
        var (success, _) = await PostWithResponseAsync<QualityAnomalyAcknowledgementResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.QualityAnomaliesAcknowledge, "anomalyId", anomalyId),
            null,
            ct).ConfigureAwait(false);
        return success;
    }

    public async Task<QualityGapRemediationResponse?> RepairGapAsync(
        string symbol,
        QualityGapRemediationRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<QualityGapRemediationResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.QualityGapsBySymbol, "symbol", symbol),
            request,
            ct).ConfigureAwait(false);
        return response.Success ? response.Data : null;
    }

    protected override async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct) where T : class
        => (await _apiClient.GetWithResponseAsync<T>(endpoint, ct).ConfigureAwait(false)).DataOrLoggedNull("Data quality API GET request");

    protected override async Task<T?> PostAsync<T>(string endpoint, object? body, CancellationToken ct) where T : class
        => (await _apiClient.PostWithResponseAsync<T>(endpoint, body, ct).ConfigureAwait(false)).DataOrLoggedNull("Data quality API POST request");

    protected override async Task<(bool Success, T? Data)> PostWithResponseAsync<T>(string endpoint, object? body, CancellationToken ct)
        where T : class
    {
        var response = await _apiClient.PostWithResponseAsync<T>(endpoint, body, ct).ConfigureAwait(false);
        return (response.Success, response.Data);
    }
}
