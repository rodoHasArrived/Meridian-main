using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Api;

/// <summary>
/// Shared HTTP client for UI-facing endpoints (web dashboard + desktop).
/// </summary>
public sealed class UiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private string _baseUrl;

    public UiApiClient(HttpClient httpClient, string baseUrl, JsonSerializerOptions? jsonOptions = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:8080" : baseUrl.TrimEnd('/');
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string BaseUrl => _baseUrl;

    public void UpdateBaseUrl(string baseUrl)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:8080" : baseUrl.TrimEnd('/');
    }

    // ============================================================
    // Status endpoints
    // ============================================================

    public async Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
        => await GetAsync<StatusResponse>(UiApiRoutes.Status, ct).ConfigureAwait(false);

    public async Task<HealthCheckResponse?> GetHealthAsync(CancellationToken ct = default)
        => await GetAsync<HealthCheckResponse>(UiApiRoutes.Health, ct).ConfigureAwait(false);

    public async Task<HealthCheckResponse?> GetHealthDetailedAsync(CancellationToken ct = default)
        => await GetAsync<HealthCheckResponse>(UiApiRoutes.HealthDetailed, ct).ConfigureAwait(false);

    // ============================================================
    // Backfill endpoints
    // ============================================================

    public async Task<List<BackfillProviderInfo>?> GetBackfillProvidersAsync(CancellationToken ct = default)
        => await GetAsync<List<BackfillProviderInfo>>(UiApiRoutes.BackfillProviders, ct).ConfigureAwait(false);

    public async Task<BackfillResultDto?> GetBackfillStatusAsync(CancellationToken ct = default)
        => await GetAsync<BackfillResultDto>(UiApiRoutes.BackfillStatus, ct).ConfigureAwait(false);

    public async Task<BackfillResultDto?> RunBackfillAsync(BackfillRequest request, CancellationToken ct = default)
        => await PostAsync<BackfillResultDto>(UiApiRoutes.BackfillRun, request, ct).ConfigureAwait(false);

    public async Task<BackfillHealthResponse?> GetBackfillHealthAsync(CancellationToken ct = default)
        => await GetAsync<BackfillHealthResponse>(UiApiRoutes.BackfillHealth, ct).ConfigureAwait(false);

    public async Task<SymbolResolutionResponse?> ResolveSymbolAsync(string symbol, CancellationToken ct = default)
        => await GetAsync<SymbolResolutionResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.BackfillResolve, "symbol", symbol), ct).ConfigureAwait(false);

    public async Task<BackfillResultDto?> RunGapFillAsync(GapFillRequest request, CancellationToken ct = default)
        => await PostAsync<BackfillResultDto>(UiApiRoutes.BackfillGapFill, request, ct).ConfigureAwait(false);

    public async Task<List<BackfillPreset>?> GetBackfillPresetsAsync(CancellationToken ct = default)
        => await GetAsync<List<BackfillPreset>>(UiApiRoutes.BackfillPresets, ct).ConfigureAwait(false);

    public async Task<List<BackfillExecution>?> GetBackfillExecutionsAsync(int limit = 50, CancellationToken ct = default)
        => await GetAsync<List<BackfillExecution>>(
            UiApiRoutes.WithQuery(UiApiRoutes.BackfillExecutions, $"limit={limit}"), ct).ConfigureAwait(false);

    public async Task<BackfillStatistics?> GetBackfillStatisticsAsync(int? hours = null, CancellationToken ct = default)
    {
        var route = hours.HasValue
            ? UiApiRoutes.WithQuery(UiApiRoutes.BackfillStatistics, $"hours={hours.Value}")
            : UiApiRoutes.BackfillStatistics;
        return await GetAsync<BackfillStatistics>(route, ct).ConfigureAwait(false);
    }

    // ============================================================
    // Provider endpoints
    // ============================================================

    public async Task<T?> GetProviderStatusAsync<T>(CancellationToken ct = default) where T : class
        => await GetAsync<T>(UiApiRoutes.ProviderStatus, ct).ConfigureAwait(false);

    public async Task<T?> GetProviderByIdAsync<T>(string providerName, CancellationToken ct = default) where T : class
        => await GetAsync<T>(
            UiApiRoutes.WithParam(UiApiRoutes.ProviderById, "providerName", providerName), ct).ConfigureAwait(false);

    public async Task<T?> GetProviderRateLimitsAsync<T>(CancellationToken ct = default) where T : class
        => await GetAsync<T>(UiApiRoutes.ProviderRateLimits, ct).ConfigureAwait(false);

    public async Task<T?> GetProviderCapabilitiesAsync<T>(CancellationToken ct = default) where T : class
        => await GetAsync<T>(UiApiRoutes.ProviderCapabilities, ct).ConfigureAwait(false);

    // ============================================================
    // Options / Derivatives endpoints
    // ============================================================

    public async Task<OptionsExpirationsResponse?> GetOptionsExpirationsAsync(string underlyingSymbol, CancellationToken ct = default)
        => await GetAsync<OptionsExpirationsResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.OptionsExpirations, "underlyingSymbol", underlyingSymbol), ct).ConfigureAwait(false);

    public async Task<OptionsStrikesResponse?> GetOptionsStrikesAsync(string underlyingSymbol, string expiration, CancellationToken ct = default)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.OptionsStrikes, "underlyingSymbol", underlyingSymbol);
        route = route.Replace("{expiration}", expiration);
        return await GetAsync<OptionsStrikesResponse>(route, ct).ConfigureAwait(false);
    }

    public async Task<OptionsChainResponse?> GetOptionsChainAsync(
        string underlyingSymbol,
        string? expiration = null,
        int? strikeRange = null,
        CancellationToken ct = default)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.OptionsChains, "underlyingSymbol", underlyingSymbol);
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(expiration))
            queryParts.Add($"expiration={expiration}");
        if (strikeRange.HasValue)
            queryParts.Add($"strikeRange={strikeRange.Value}");
        if (queryParts.Count > 0)
            route = UiApiRoutes.WithQuery(route, string.Join("&", queryParts));
        return await GetAsync<OptionsChainResponse>(route, ct).ConfigureAwait(false);
    }

    public async Task<List<OptionQuoteDto>?> GetOptionsQuotesByUnderlyingAsync(string underlyingSymbol, CancellationToken ct = default)
        => await GetAsync<List<OptionQuoteDto>>(
            UiApiRoutes.WithParam(UiApiRoutes.OptionsQuotesByUnderlying, "underlyingSymbol", underlyingSymbol), ct).ConfigureAwait(false);

    public async Task<OptionsSummaryResponse?> GetOptionsSummaryAsync(CancellationToken ct = default)
        => await GetAsync<OptionsSummaryResponse>(UiApiRoutes.OptionsSummary, ct).ConfigureAwait(false);

    public async Task<List<string>?> GetOptionsTrackedUnderlyingsAsync(CancellationToken ct = default)
    {
        var response = await GetAsync<OptionsTrackedUnderlyingsResponse>(
            UiApiRoutes.OptionsTrackedUnderlyings, ct).ConfigureAwait(false);
        return response?.Underlyings?.ToList();
    }

    public async Task<OptionsChainResponse?> RefreshOptionsChainAsync(OptionsRefreshRequest request, CancellationToken ct = default)
        => await PostAsync<OptionsChainResponse>(UiApiRoutes.OptionsRefresh, request, ct).ConfigureAwait(false);

    // ============================================================
    // Operations continuity + governance lifecycle endpoints
    // ============================================================

    public async Task<List<OperationsContinuityWorkflowSummaryDto>?> GetOperationsContinuityWorkflowsAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default)
    {
        var query = new List<string>(3);
        if (fundAccountId.HasValue)
        {
            query.Add($"fundAccountId={Uri.EscapeDataString(fundAccountId.Value.ToString())}");
        }

        if (!string.IsNullOrWhiteSpace(periodId))
        {
            query.Add($"periodId={Uri.EscapeDataString(periodId)}");
        }

        if (status.HasValue)
        {
            query.Add($"status={Uri.EscapeDataString(status.Value.ToString())}");
        }

        var route = query.Count == 0
            ? UiApiRoutes.OperationsContinuity
            : UiApiRoutes.WithQuery(UiApiRoutes.OperationsContinuity, string.Join("&", query));

        return await GetAsync<List<OperationsContinuityWorkflowSummaryDto>>(route, ct).ConfigureAwait(false);
    }

    public async Task<OperationsContinuityWorkflowDto?> GetOperationsContinuityWorkflowAsync(
        Guid workflowId,
        CancellationToken ct = default)
        => await GetAsync<OperationsContinuityWorkflowDto>(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflowId.ToString()),
            ct).ConfigureAwait(false);

    public async Task<List<OperationsTimelineEntryDto>?> GetOperationsContinuityTimelineAsync(
        Guid workflowId,
        CancellationToken ct = default)
        => await GetAsync<List<OperationsTimelineEntryDto>>(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityTimeline, "workflowId", workflowId.ToString()),
            ct).ConfigureAwait(false);

    public async Task<ApiResponse<OperationsTransitionResultDto>> SubmitOperationsContinuityApprovalAsync(
        Guid workflowId,
        OperationsSubmitApprovalRequestDto request,
        CancellationToken ct = default)
        => await PostWithResponseAsync<OperationsTransitionResultDto>(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityApprovalSubmit, "workflowId", workflowId.ToString()),
            request,
            ct).ConfigureAwait(false);

    public async Task<ApiResponse<OperationsTransitionResultDto>> ApproveOperationsContinuityWorkflowAsync(
        Guid workflowId,
        OperationsApprovalDecisionRequestDto request,
        CancellationToken ct = default)
        => await PostWithResponseAsync<OperationsTransitionResultDto>(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityApprovalApprove, "workflowId", workflowId.ToString()),
            request,
            ct).ConfigureAwait(false);

    public async Task<ApiResponse<OperationsTransitionResultDto>> RejectOperationsContinuityWorkflowAsync(
        Guid workflowId,
        OperationsRejectWorkflowRequestDto request,
        CancellationToken ct = default)
        => await PostWithResponseAsync<OperationsTransitionResultDto>(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityApprovalReject, "workflowId", workflowId.ToString()),
            request,
            ct).ConfigureAwait(false);

    public async Task<ApiResponse<OperationsTransitionResultDto>> CloseOperationsContinuityWorkflowAsync(
        Guid workflowId,
        OperationsCloseWorkflowRequestDto request,
        CancellationToken ct = default)
        => await PostWithResponseAsync<OperationsTransitionResultDto>(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityClose, "workflowId", workflowId.ToString()),
            request,
            ct).ConfigureAwait(false);

    public async Task<ApiResponse<OperationsTransitionResultDto>> ReopenOperationsContinuityWorkflowAsync(
        Guid workflowId,
        OperationsReopenWorkflowRequestDto request,
        CancellationToken ct = default)
        => await PostWithResponseAsync<OperationsTransitionResultDto>(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityReopen, "workflowId", workflowId.ToString()),
            request,
            ct).ConfigureAwait(false);

    public async Task<List<ReconciliationBreakQueueItem>?> GetReconciliationBreakQueueAsync(CancellationToken ct = default)
        => await GetAsync<List<ReconciliationBreakQueueItem>>(UiApiRoutes.ReconciliationBreakQueue, ct).ConfigureAwait(false);

    public async Task<List<FundReportPackHistoryItemDto>?> GetFundReportPackHistoryAsync(
        string fundProfileId,
        int limit = 20,
        CancellationToken ct = default)
        => await GetAsync<List<FundReportPackHistoryItemDto>>(
            UiApiRoutes.WithQuery(
                UiApiRoutes.FundReportPacks,
                $"fundProfileId={Uri.EscapeDataString(fundProfileId)}&limit={limit}"),
            ct).ConfigureAwait(false);

    public async Task<FundReportPackSnapshotDto?> GetFundReportPackAsync(Guid reportId, CancellationToken ct = default)
        => await GetAsync<FundReportPackSnapshotDto>(
            UiApiRoutes.WithParam(UiApiRoutes.FundReportPackById, "reportId", reportId.ToString()),
            ct).ConfigureAwait(false);

    public async Task<EvidencePacketDto?> GetEvidencePacketAsync(
        string subjectKind,
        string subjectId,
        CancellationToken ct = default)
        => await GetAsync<EvidencePacketDto>(
            UiApiRoutes.WithParam(
                UiApiRoutes.WithParam(UiApiRoutes.WorkstationEvidenceSubjectPacket, "subjectKind", subjectKind),
                "subjectId",
                subjectId),
            ct).ConfigureAwait(false);

    public async Task<EvidenceGraphDto?> GetEvidenceGraphAsync(
        string subjectKind,
        string subjectId,
        CancellationToken ct = default)
        => await GetAsync<EvidenceGraphDto>(
            UiApiRoutes.WithParam(
                UiApiRoutes.WithParam(UiApiRoutes.WorkstationEvidenceSubjectGraph, "subjectKind", subjectKind),
                "subjectId",
                subjectId),
            ct).ConfigureAwait(false);

    // ============================================================
    // Generic HTTP methods
    // ============================================================

    public async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    public async Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        try
        {
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return ApiResponse<T>.Fail(json, (int)response.StatusCode);
            }

            var data = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return ApiResponse<T>.Ok(data!, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return ApiResponse<T>.Fail($"Connection failed: {ex.Message}", 0, isConnectionError: true);
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object? payload = null, CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        using var content = payload == null
            ? null
            : new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    public async Task<ApiResponse<T>> PostWithResponseAsync<T>(
        string endpoint,
        object? payload = null,
        CancellationToken ct = default) where T : class
    {
        var url = BuildUrl(endpoint);
        try
        {
            using var content = payload == null
                ? null
                : new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return ApiResponse<T>.Fail(json, (int)response.StatusCode);
            }

            var data = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return ApiResponse<T>.Ok(data!, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return ApiResponse<T>.Fail($"Connection failed: {ex.Message}", 0, isConnectionError: true);
        }
    }

    private string BuildUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return _baseUrl;

        var path = endpoint.StartsWith('/') ? endpoint : $"/{endpoint}";
        return $"{_baseUrl}{path}";
    }
}
