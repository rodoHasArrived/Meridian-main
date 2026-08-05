using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Thin desktop client for the shared workstation evidence endpoints. The desktop lane owns
/// presentation only; subject resolution, packet assembly, completeness evaluation, and manifest
/// retention all stay server-side behind the same routes the browser workbench consumes.
/// </summary>
public interface IEvidenceWorkbenchApiClient
{
    Task<ApiResponse<EvidenceSubjectDto[]>> ListSubjectsAsync(CancellationToken ct = default);

    Task<ApiResponse<EvidencePacketDto>> GetPacketAsync(
        string subjectKind,
        string subjectId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<ApiResponse<EvidenceCompletenessDto>> ValidatePacketAsync(
        string subjectKind,
        string subjectId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<ApiResponse<EvidencePacketExportResponse>> ExportManifestAsync(
        string subjectKind,
        string subjectId,
        EvidencePacketExportRequest request,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<ApiResponse<EvidenceVaultRequestListEntryDto[]>> ListOpenRequestListsAsync(
        string? subjectKind,
        string? subjectId,
        int maxResults,
        CancellationToken ct = default);

    Task<ApiResponse<EvidenceVaultDocumentEntryDto[]>> ListDocumentsAsync(
        string? subjectKind,
        string? subjectId,
        int maxResults,
        CancellationToken ct = default);
}

public sealed class EvidenceWorkbenchApiClient : IEvidenceWorkbenchApiClient
{
    private readonly ApiClientService _apiClient;

    public EvidenceWorkbenchApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ApiResponse<EvidenceSubjectDto[]>> ListSubjectsAsync(CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<EvidenceSubjectDto[]>(UiApiRoutes.WorkstationEvidenceSubjects, ct);

    public Task<ApiResponse<EvidencePacketDto>> GetPacketAsync(
        string subjectKind,
        string subjectId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<EvidencePacketDto>(
            SubjectRoute(UiApiRoutes.WorkstationEvidenceSubjectPacket, subjectKind, subjectId, ledgerBookId),
            ct);

    public Task<ApiResponse<EvidenceCompletenessDto>> ValidatePacketAsync(
        string subjectKind,
        string subjectId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<EvidenceCompletenessDto>(
            SubjectRoute(UiApiRoutes.WorkstationEvidenceSubjectValidate, subjectKind, subjectId, ledgerBookId),
            body: null,
            ct: ct);

    public Task<ApiResponse<EvidencePacketExportResponse>> ExportManifestAsync(
        string subjectKind,
        string subjectId,
        EvidencePacketExportRequest request,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<EvidencePacketExportResponse>(
            SubjectRoute(UiApiRoutes.WorkstationEvidenceSubjectExportManifest, subjectKind, subjectId, ledgerBookId),
            request,
            ct: ct);

    public Task<ApiResponse<EvidenceVaultRequestListEntryDto[]>> ListOpenRequestListsAsync(
        string? subjectKind,
        string? subjectId,
        int maxResults,
        CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<EvidenceVaultRequestListEntryDto[]>(
            UiApiRoutes.WithQuery(
                UiApiRoutes.WorkstationEvidenceVaultRequestLists,
                BuildScopedQuery(subjectKind, subjectId, maxResults, "status=Open")),
            ct);

    public Task<ApiResponse<EvidenceVaultDocumentEntryDto[]>> ListDocumentsAsync(
        string? subjectKind,
        string? subjectId,
        int maxResults,
        CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<EvidenceVaultDocumentEntryDto[]>(
            UiApiRoutes.WithQuery(
                UiApiRoutes.WorkstationEvidenceVaultDocuments,
                BuildScopedQuery(subjectKind, subjectId, maxResults)),
            ct);

    private static string SubjectRoute(string route, string subjectKind, string subjectId, Guid? ledgerBookId = null)
    {
        var resolved = UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(route, "subjectKind", subjectKind),
            "subjectId",
            subjectId);

        // Book-scoped subjects (e.g. private-capital fund events) resolve their packet, validate,
        // and export routes against the owning ledger book; unscoped subjects omit the query.
        return ledgerBookId is null
            ? resolved
            : UiApiRoutes.WithQuery(resolved, $"ledgerBookId={Uri.EscapeDataString(ledgerBookId.Value.ToString("D"))}");
    }

    private static string BuildScopedQuery(
        string? subjectKind,
        string? subjectId,
        int maxResults,
        string? leadingClause = null)
    {
        var clauses = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(leadingClause))
        {
            clauses.Add(leadingClause);
        }

        if (!string.IsNullOrWhiteSpace(subjectKind))
        {
            clauses.Add($"subjectKind={Uri.EscapeDataString(subjectKind.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            clauses.Add($"subjectId={Uri.EscapeDataString(subjectId.Trim())}");
        }

        clauses.Add($"maxResults={maxResults}");
        return string.Join("&", clauses);
    }
}
