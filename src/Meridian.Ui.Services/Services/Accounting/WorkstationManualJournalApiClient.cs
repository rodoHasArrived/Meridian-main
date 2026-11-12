using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>The existing journal workbench through the authenticated server's authority.</summary>
public interface IWorkstationManualJournalApiClient : IManualJournalEntryWorkbenchService, IManualJournalEntryLifecycleService;

public sealed class WorkstationManualJournalApiClient(ApiClientService api) : IWorkstationManualJournalApiClient
{
    public async Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
        => Unwrap(await api.GetWithResponseAsync<LedgerBookDto[]>(UiApiRoutes.LedgerBooks, ct).ConfigureAwait(false))
            .Select(book => book.FundProfileId).Distinct(StringComparer.Ordinal).ToArray();

    public async Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(string? fundProfileId = null, Guid? ledgerBookId = null,
        CancellationToken ct = default, string? tenantId = null, string? companyId = null)
    {
        var result = Unwrap(await api.GetWithResponseAsync<ManualJournalEntryWorkbenchDto>(
            UiApiRoutes.LedgerManualJournalEntryWorkbench + Scope(fundProfileId, ledgerBookId), ct).ConfigureAwait(false));
        if (fundProfileId is not null && result.FundProfileId != fundProfileId || ledgerBookId.HasValue && result.LedgerBookId != ledgerBookId)
            throw new InvalidOperationException("The journal workbench response does not match the selected fund and book.");
        return result;
    }

    public async Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(string? fundProfileId = null, Guid? ledgerBookId = null,
        CancellationToken ct = default, string? tenantId = null, string? companyId = null)
        => Unwrap(await api.GetWithResponseAsync<PrivateCapitalActivityProjectionDto>(
            UiApiRoutes.LedgerPrivateCapitalActivity + Scope(fundProfileId, ledgerBookId), ct).ConfigureAwait(false));

    public Task<ManualJournalEntryDraftDto> SaveDraftAsync(SaveManualJournalEntryDraftRequest request, CancellationToken ct = default)
        => Post<ManualJournalEntryDraftDto>(UiApiRoutes.LedgerManualJournalEntryDrafts,
            request with { TenantId = null, CompanyId = null, ReportGroupPrincipalIds = null,
                Draft = request.Draft with { TenantId = null, CompanyId = null } }, ct);

    public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(ValidateManualJournalEntryDraftRequest request, CancellationToken ct = default)
        => Post<ManualJournalEntryDraftDto>(UiApiRoutes.LedgerManualJournalEntryValidate,
            request with { TenantId = null, CompanyId = null, Draft = request.Draft with { TenantId = null, CompanyId = null } }, ct);

    public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(SubmitManualJournalEntryApprovalRequest request, CancellationToken ct = default)
        => Post<ManualJournalEntryDraftDto>(UiApiRoutes.LedgerManualJournalEntrySubmitApproval,
            request with { TenantId = null, CompanyId = null, ReportGroupPrincipalIds = null }, ct);

    public Task<ManualJournalEntryDraftDto> AttachEvidenceAsync(AttachManualJournalEntryEvidenceRequest request, CancellationToken ct = default)
        => Post<ManualJournalEntryDraftDto>(UiApiRoutes.LedgerManualJournalEntryEvidence,
            request with { TenantId = null, CompanyId = null, ReportGroupPrincipalIds = null }, ct);

    public Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleActionAsync(JournalEntryLifecycleActionRequestDto request, CancellationToken ct = default)
        => Post<JournalEntryLifecycleActionResultDto>(UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            request with { TenantId = null, CompanyId = null, ReportGroupPrincipalIds = null }, ct);

    private async Task<T> Post<T>(string route, object request, CancellationToken ct) where T : class
        => Unwrap(await api.PostWithResponseAsync<T>(route, request, ct).ConfigureAwait(false));

    private static string Scope(string? fund, Guid? book)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(fund)) values.Add("fundProfileId=" + Uri.EscapeDataString(fund));
        if (book.HasValue) values.Add($"ledgerBookId={book:D}");
        return values.Count == 0 ? string.Empty : "?" + string.Join("&", values);
    }

    private static T Unwrap<T>(ApiResponse<T> response) where T : class
        => response.Success && response.Data is not null ? response.Data
            : throw new InvalidOperationException(response.ErrorMessage ?? "The governed journal workbench is unavailable.");
}
