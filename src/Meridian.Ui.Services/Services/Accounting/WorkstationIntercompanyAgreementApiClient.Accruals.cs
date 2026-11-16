using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>Read-only computation over the operator's selection; the server resolves every economic input.</summary>
    Task<ApiResponse<IntercompanyAccrualProposalDto>> PreviewInterestAccrualAsync(IntercompanyAccrualRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAccrualProposalDto>.Fail("Governed intercompany accrual is unavailable.", 503));
    /// <summary>Retains the previewed computation as a source-book review draft. It never approves or posts.</summary>
    Task<ApiResponse<ManualJournalEntryDraftDto>> CreateInterestAccrualDraftAsync(CreateIntercompanyAccrualDraftRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<ManualJournalEntryDraftDto>.Fail("Governed intercompany accrual drafting is unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string AccrualRoute = "/api/ledger/intercompany/accruals";

    public async Task<ApiResponse<IntercompanyAccrualProposalDto>> PreviewInterestAccrualAsync(IntercompanyAccrualRequestDto request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<IntercompanyAccrualProposalDto>($"{AccrualRoute}/preview", request, ct).ConfigureAwait(false);
        if (!response.Success)
            return response;
        // A preview for another window, or a "ready" one without the evidence the draft is bound to, is not this request's answer.
        return response.Data is not { Computation: { } computation, Diagnostics: not null } proposal
            || computation.AccrualFrom != request.AccrualFrom || computation.AccrualTo != request.AccrualTo
            || computation.Slices is null || computation.Diagnostics is null
            || computation.RuleVersion != IntercompanyAccrualRules.RuleVersion
            || (computation.IsReady && (computation.Amount is null || string.IsNullOrWhiteSpace(computation.Currency)
                || proposal.Evidence is not { } evidence || proposal.Draft is not { } draft || string.IsNullOrWhiteSpace(proposal.EvidenceDigest)
                || evidence.SourceBookId != request.SourceBookId || evidence.AccountingArrangementId != request.InterestArrangementId
                || evidence.Principal?.PrincipalArrangementId != request.PrincipalArrangementId
                || draft.LedgerBookId != request.SourceBookId || draft.JournalEntryId != evidence.DraftId))
            ? ApiResponse<IntercompanyAccrualProposalDto>.Fail("The server returned an accrual preview for a different request or without its evidence.", 502)
            : response;
    }

    public async Task<ApiResponse<ManualJournalEntryDraftDto>> CreateInterestAccrualDraftAsync(CreateIntercompanyAccrualDraftRequest request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<ManualJournalEntryDraftDto>($"{AccrualRoute}/drafts", request, ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } draft || draft.LedgerBookId != request.Accrual.SourceBookId
            || !string.Equals(draft.IntercompanyAccrualEvidenceDigest, request.ExpectedEvidenceDigest, StringComparison.Ordinal))
            ? ApiResponse<ManualJournalEntryDraftDto>.Fail("The server returned a draft for a different book or accrual computation.", 502) : response;
    }
}
