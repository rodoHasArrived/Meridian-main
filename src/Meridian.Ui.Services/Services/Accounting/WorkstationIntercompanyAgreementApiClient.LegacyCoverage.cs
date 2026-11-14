using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public sealed record IntercompanyLegacyCoverageCandidate(Guid LedgerBookId, Guid JournalEntryId, Guid EntryId,
    string SourceLineDigest, string AccountName, string AccountType, decimal Debit, decimal Credit, DateOnly EffectiveDate);

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    Task<ApiResponse<IntercompanyLegacyCoverageCandidate[]>> GetLegacyCoverageCandidatesAsync(Guid bookId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyLegacyCoverageCandidate[]>.Fail("Legacy source authority is unavailable.", 503));
    Task<ApiResponse<LegacyIntercompanyAttributionDto[]>> GetLegacyCoverageHistoryAsync(Guid bookId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<LegacyIntercompanyAttributionDto[]>.Fail("Legacy coverage history is unavailable.", 503));
    Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Fail("Accounting profiles are unavailable.", 503));
    Task<ApiResponse<IntercompanyAccountBindingDto[]>> GetCoverageBindingsAsync(Guid bookId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAccountBindingDto[]>.Fail("Account binding authority is unavailable.", 503));
    Task<ApiResponse<LegacyIntercompanyAttributionDto>> AppendLegacyCoverageAsync(AppendLegacyIntercompanyAttributionRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<LegacyIntercompanyAttributionDto>.Fail("Legacy coverage review is unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    public async Task<ApiResponse<IntercompanyLegacyCoverageCandidate[]>> GetLegacyCoverageCandidatesAsync(Guid bookId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyLegacyCoverageCandidate[]>($"/api/ledger/intercompany/legacy-attributions/candidates?ledgerBookId={bookId:D}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is null || response.Data.Any(x => x.LedgerBookId != bookId))
            ? ApiResponse<IntercompanyLegacyCoverageCandidate[]>.Fail("Legacy source lines belong to a different book.", 502) : response;
    }
    public async Task<ApiResponse<LegacyIntercompanyAttributionDto[]>> GetLegacyCoverageHistoryAsync(Guid bookId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<LegacyIntercompanyAttributionDto[]>($"/api/ledger/intercompany/legacy-attributions?ledgerBookId={bookId:D}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is null || response.Data.Any(x => x.LedgerBookId != bookId))
            ? ApiResponse<LegacyIntercompanyAttributionDto[]>.Fail("Legacy coverage history belongs to a different book.", 502) : response;
    }
    public async Task<ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>> GetCoverageProfilesAsync(Guid bookId, DateOnly asOf, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<ResolvedIntercompanyAccountingArrangementDto[]>($"/api/ledger/intercompany-accounting-arrangements?bookId={bookId:D}&asOf={asOf:yyyy-MM-dd}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is null || response.Data.Any(x => !x.PartyMappings.Any(p => p.LedgerBookId == bookId)))
            ? ApiResponse<ResolvedIntercompanyAccountingArrangementDto[]>.Fail("Accounting profiles belong to a different book.", 502) : response;
    }
    public async Task<ApiResponse<IntercompanyAccountBindingDto[]>> GetCoverageBindingsAsync(Guid bookId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyAccountBindingDto[]>($"/api/ledger/intercompany/account-bindings/history?ledgerBookId={bookId:D}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is null || response.Data.Any(x => x.LedgerBookId != bookId))
            ? ApiResponse<IntercompanyAccountBindingDto[]>.Fail("Account bindings belong to a different book.", 502) : response;
    }
    public async Task<ApiResponse<LegacyIntercompanyAttributionDto>> AppendLegacyCoverageAsync(AppendLegacyIntercompanyAttributionRequest request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<LegacyIntercompanyAttributionDto>("/api/ledger/intercompany/legacy-attributions", request, ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } retained || retained.AttributionId != request.Attribution.AttributionId
            || retained.LedgerBookId != request.Attribution.LedgerBookId || retained.JournalEntryId != request.Attribution.JournalEntryId || retained.LineId != request.Attribution.LineId
            || retained.SourceLineDigest != request.Attribution.SourceLineDigest || retained.IntercompanyAgreementId != request.Attribution.IntercompanyAgreementId
            || retained.IntercompanyAgreementVersion != request.Attribution.IntercompanyAgreementVersion || retained.IntercompanyAgreementPayloadHash != request.Attribution.IntercompanyAgreementPayloadHash
            || retained.ReviewMode != request.Attribution.ReviewMode || retained.Revoked != request.Attribution.Revoked)
            ? ApiResponse<LegacyIntercompanyAttributionDto>.Fail("The server returned coverage for a different source identity.", 502) : response;
    }
}
