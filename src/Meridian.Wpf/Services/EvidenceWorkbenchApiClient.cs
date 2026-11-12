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
    /// <summary>Scope-level preparer assertions, separate from evidence readiness and approval.</summary>
    Task<ApiResponse<SinglePrincipalAttestationWorkspaceDto>> GetAttestationsAsync(CancellationToken ct = default)
        => Task.FromResult(ApiResponse<SinglePrincipalAttestationWorkspaceDto>.Fail(
            "Preparer attestation history is unavailable from this host.", 501));

    Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>> GetAttestationSubjectAsync(
        string vaultId, string documentId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>.Fail("Attestation preview is unavailable from this host.", 501));

    Task<ApiResponse<string>> ReadAttestationProofExportAsync(Guid receiptId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<string>.Fail("Original attestation proof is unavailable from this host.", 501));

    Task<ApiResponse<SinglePrincipalDeclarationDto>> ChangeAttestationDeclarationAsync(
        ChangeSinglePrincipalDeclarationRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<SinglePrincipalDeclarationDto>.Fail("Declaration administration is unavailable from this host.", 501));

    Task<ApiResponse<PreparerAttestationReceiptDto>> RecordAttestationAsync(
        RecordPreparerAttestationRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<PreparerAttestationReceiptDto>.Fail("Preparer acknowledgment is unavailable from this host.", 501));

    Task<ApiResponse<PreparerAttestationRevocationDto>> RevokeAttestationAsync(
        RevokePreparerAttestationRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<PreparerAttestationRevocationDto>.Fail("Assertion revocation is unavailable from this host.", 501));

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

/// <summary>
/// Adapts shared evidence subject, packet, validation, document, and request-list APIs for the desktop workbench.
/// </summary>
public sealed class EvidenceWorkbenchApiClient : IEvidenceWorkbenchApiClient
{
    private readonly ApiClientService _apiClient;

    public EvidenceWorkbenchApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ApiResponse<SinglePrincipalAttestationWorkspaceDto>> GetAttestationsAsync(CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<SinglePrincipalAttestationWorkspaceDto>(
            "/api/workstation/evidence/attestations", ct);

    public Task<ApiResponse<SinglePrincipalAttestationSubjectSnapshotDto>> GetAttestationSubjectAsync(
        string vaultId, string documentId, CancellationToken ct = default)
        => _apiClient.GetWithResponseAsync<SinglePrincipalAttestationSubjectSnapshotDto>(
            "/api/workstation/evidence/attestations/subjects?vaultId=" + Uri.EscapeDataString(vaultId)
            + "&documentId=" + Uri.EscapeDataString(documentId), ct);

    public async Task<ApiResponse<string>> ReadAttestationProofExportAsync(Guid receiptId, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<System.Text.Json.JsonDocument>(
            $"/api/workstation/evidence/attestations/receipts/{receiptId:D}/export", ct).ConfigureAwait(false);
        if (!response.Success || response.Data is null)
            return ApiResponse<string>.Fail(response.ErrorMessage ?? "Original proof export is unavailable.", response.StatusCode, response.IsConnectionError);
        using var document = response.Data;
        return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            ? ApiResponse<string>.Ok(document.RootElement.GetRawText(), response.StatusCode)
            : ApiResponse<string>.Fail("The original proof export is malformed.");
    }

    public Task<ApiResponse<SinglePrincipalDeclarationDto>> ChangeAttestationDeclarationAsync(
        ChangeSinglePrincipalDeclarationRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<SinglePrincipalDeclarationDto>(
            "/api/workstation/evidence/attestations/declaration", request, ct: ct);

    public Task<ApiResponse<PreparerAttestationReceiptDto>> RecordAttestationAsync(
        RecordPreparerAttestationRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<PreparerAttestationReceiptDto>(
            "/api/workstation/evidence/attestations/receipts", request, ct: ct);

    public Task<ApiResponse<PreparerAttestationRevocationDto>> RevokeAttestationAsync(
        RevokePreparerAttestationRequestDto request, CancellationToken ct = default)
        => _apiClient.PostWithResponseAsync<PreparerAttestationRevocationDto>(
            "/api/workstation/evidence/attestations/revocations", request, ct: ct);

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
