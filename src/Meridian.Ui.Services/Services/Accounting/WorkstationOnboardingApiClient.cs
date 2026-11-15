using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>
/// Desktop transport for bounded onboarding workspaces and their shadow-mode parallel run (W11-SHADOW-001).
/// It uses the same routes and contracts as the browser workstation. The server derives tenant, company and
/// actor from the authenticated session; mutations also need a human-operator session.
/// </summary>
public interface IWorkstationOnboardingApiClient
{
    Task<ApiResponse<IReadOnlyList<OnboardingWorkspaceDto>>> ListWorkspacesAsync(CancellationToken ct = default);
    Task<ApiResponse<OnboardingWorkspaceDto>> GetWorkspaceAsync(string workspaceId, CancellationToken ct = default);
    Task<ApiResponse<OnboardingWorkspaceDto>> DesignateShadowAsync(string workspaceId, DesignateShadowBookRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<OnboardingWorkspaceDto>> WithdrawShadowAsync(string workspaceId, WithdrawShadowBookRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<OnboardingWorkspaceDto>> ImportOpeningBalancesAsync(string workspaceId, ImportOnboardingOpeningBalancesRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<OnboardingOpeningBalanceReportDto>> GetOpeningReportAsync(string workspaceId, CancellationToken ct = default);
    Task<ApiResponse<OnboardingWorkspaceDto>> ExplainDifferenceAsync(string workspaceId, string differenceKey, ExplainOnboardingDifferenceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<OnboardingParallelRunCertificateDto>> IssueCertificateAsync(string workspaceId, IssueParallelRunCertificateRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<OnboardingParallelRunCertificateDto>> GetCertificateAsync(string workspaceId, string certificateId, CancellationToken ct = default);
    Task<ApiResponse<OnboardingWorkspaceDto>> PromoteAsync(string workspaceId, PromoteShadowBookRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<BookOfRecordStatusDto>> GetBookOfRecordStatusAsync(Guid ledgerBookId, CancellationToken ct = default);
}

public sealed class WorkstationOnboardingApiClient(ApiClientService apiClient) : IWorkstationOnboardingApiClient
{
    public async Task<ApiResponse<IReadOnlyList<OnboardingWorkspaceDto>>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<List<OnboardingWorkspaceDto>>(UiApiRoutes.OnboardingWorkspaces, ct).ConfigureAwait(false);
        return response.Success
            ? ApiResponse<IReadOnlyList<OnboardingWorkspaceDto>>.Ok(response.Data ?? [], response.StatusCode)
            : Failed<IReadOnlyList<OnboardingWorkspaceDto>, List<OnboardingWorkspaceDto>>(response);
    }

    public async Task<ApiResponse<OnboardingWorkspaceDto>> GetWorkspaceAsync(string workspaceId, CancellationToken ct = default)
        => Same(workspaceId, Normalize(await apiClient.GetWithResponseAsync<OnboardingWorkspaceDto>(
            Route(UiApiRoutes.OnboardingWorkspace, workspaceId), ct).ConfigureAwait(false)));

    public async Task<ApiResponse<OnboardingWorkspaceDto>> DesignateShadowAsync(string workspaceId,
        DesignateShadowBookRequestDto request, CancellationToken ct = default)
        => Same(workspaceId, Normalize(await apiClient.PostWithResponseAsync<OnboardingWorkspaceDto>(
            Route(UiApiRoutes.OnboardingShadowDesignation, workspaceId), request, ct).ConfigureAwait(false)));

    public async Task<ApiResponse<OnboardingWorkspaceDto>> WithdrawShadowAsync(string workspaceId,
        WithdrawShadowBookRequestDto request, CancellationToken ct = default)
        => Same(workspaceId, Normalize(await apiClient.PostWithResponseAsync<OnboardingWorkspaceDto>(
            Route(UiApiRoutes.OnboardingShadowWithdrawal, workspaceId), request, ct).ConfigureAwait(false)));

    public async Task<ApiResponse<OnboardingWorkspaceDto>> ImportOpeningBalancesAsync(string workspaceId,
        ImportOnboardingOpeningBalancesRequestDto request, CancellationToken ct = default)
        => Same(workspaceId, Normalize(await apiClient.PostWithResponseAsync<OnboardingWorkspaceDto>(
            Route(UiApiRoutes.OnboardingOpeningBalances, workspaceId), request, ct).ConfigureAwait(false)));

    public async Task<ApiResponse<OnboardingOpeningBalanceReportDto>> GetOpeningReportAsync(string workspaceId, CancellationToken ct = default)
        => Normalize(await apiClient.GetWithResponseAsync<OnboardingOpeningBalanceReportDto>(
            Route(UiApiRoutes.OnboardingOpeningBalanceReport, workspaceId), ct).ConfigureAwait(false));

    public async Task<ApiResponse<OnboardingWorkspaceDto>> ExplainDifferenceAsync(string workspaceId, string differenceKey,
        ExplainOnboardingDifferenceRequestDto request, CancellationToken ct = default)
    {
        var route = UiApiRoutes.WithParam(Route(UiApiRoutes.OnboardingDifferenceExplanation, workspaceId), "differenceKey", differenceKey);
        return Same(workspaceId, Normalize(await apiClient.PostWithResponseAsync<OnboardingWorkspaceDto>(route, request, ct).ConfigureAwait(false)));
    }

    public async Task<ApiResponse<OnboardingParallelRunCertificateDto>> IssueCertificateAsync(string workspaceId,
        IssueParallelRunCertificateRequestDto request, CancellationToken ct = default)
    {
        var response = Normalize(await apiClient.PostWithResponseAsync<OnboardingParallelRunCertificateDto>(
            Route(UiApiRoutes.OnboardingCertificates, workspaceId), request, ct).ConfigureAwait(false));
        return response.Success && response.Data?.Content.WorkspaceId != workspaceId
            ? ApiResponse<OnboardingParallelRunCertificateDto>.Fail("The server returned a certificate for a different workspace.", 502)
            : response;
    }

    public async Task<ApiResponse<OnboardingParallelRunCertificateDto>> GetCertificateAsync(string workspaceId,
        string certificateId, CancellationToken ct = default)
    {
        var route = UiApiRoutes.WithParam(Route(UiApiRoutes.OnboardingCertificate, workspaceId), "certificateId", certificateId);
        var response = Normalize(await apiClient.GetWithResponseAsync<OnboardingParallelRunCertificateDto>(route, ct).ConfigureAwait(false));
        return response.Success && (response.Data?.CertificateId != certificateId || response.Data.Content.WorkspaceId != workspaceId)
            ? ApiResponse<OnboardingParallelRunCertificateDto>.Fail("The server returned a different certificate.", 502)
            : response;
    }

    public async Task<ApiResponse<OnboardingWorkspaceDto>> PromoteAsync(string workspaceId,
        PromoteShadowBookRequestDto request, CancellationToken ct = default)
        => Same(workspaceId, Normalize(await apiClient.PostWithResponseAsync<OnboardingWorkspaceDto>(
            Route(UiApiRoutes.OnboardingPromotion, workspaceId), request, ct).ConfigureAwait(false)));

    public async Task<ApiResponse<BookOfRecordStatusDto>> GetBookOfRecordStatusAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        var response = Normalize(await apiClient.GetWithResponseAsync<BookOfRecordStatusDto>(
            UiApiRoutes.WithParam(UiApiRoutes.OnboardingBookOfRecordStatus, "ledgerBookId", ledgerBookId.ToString("D")), ct).ConfigureAwait(false));
        return response.Success && response.Data?.LedgerBookId != ledgerBookId
            ? ApiResponse<BookOfRecordStatusDto>.Fail("The server returned the status of a different ledger book.", 502)
            : response;
    }

    /// <summary>Onboarding refusals carry <c>{ "error": "..." }</c>; surface that sentence instead of the raw body.</summary>
    public static string? ReadErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return body;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
        }
        catch (JsonException)
        {
        }

        return body;
    }

    private static string Route(string template, string workspaceId)
        => UiApiRoutes.WithParam(template, "workspaceId", workspaceId);

    private static ApiResponse<T> Normalize<T>(ApiResponse<T> response) where T : class
        => response.Success
            ? response.Data is null ? ApiResponse<T>.Fail("The server returned an empty onboarding response.", 502) : response
            : Failed<T, T>(response);

    private static ApiResponse<OnboardingWorkspaceDto> Same(string workspaceId, ApiResponse<OnboardingWorkspaceDto> response)
        => response.Success && response.Data!.WorkspaceId != workspaceId
            ? ApiResponse<OnboardingWorkspaceDto>.Fail("The server returned a different onboarding workspace.", 502)
            : response;

    private static ApiResponse<TResult> Failed<TResult, TSource>(ApiResponse<TSource> response)
        where TResult : class where TSource : class
        => new()
        {
            Success = false,
            StatusCode = response.StatusCode,
            ErrorMessage = ReadErrorMessage(response.ErrorMessage),
            IsConnectionError = response.IsConnectionError
        };
}
