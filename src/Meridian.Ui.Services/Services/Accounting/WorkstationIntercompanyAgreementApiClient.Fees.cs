using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>Read-only fixed management-fee computation over the operator's selection; the server resolves every input.</summary>
    Task<ApiResponse<IntercompanyFeeAccrualProposalDto>> PreviewFeeAccrualAsync(IntercompanyFeeAccrualRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyFeeAccrualProposalDto>.Fail("Governed intercompany fee accrual is unavailable.", 503));
    /// <summary>Retains the previewed fee as a source-book review draft. It never approves or posts.</summary>
    Task<ApiResponse<ManualJournalEntryDraftDto>> CreateFeeAccrualDraftAsync(CreateIntercompanyFeeAccrualDraftRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<ManualJournalEntryDraftDto>.Fail("Governed intercompany fee drafting is unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string FeePreviewRoute = "/api/ledger/intercompany/fees/preview";
    private const string FeeDraftRoute = "/api/ledger/intercompany/fees/drafts";

    public async Task<ApiResponse<IntercompanyFeeAccrualProposalDto>> PreviewFeeAccrualAsync(IntercompanyFeeAccrualRequestDto request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<IntercompanyFeeAccrualProposalDto>(FeePreviewRoute, request, ct).ConfigureAwait(false);
        if (!response.Success)
            return response;
        // A preview for another window, rule or profile, or a "ready" one without its evidence, is not this request's answer.
        return response.Data is not { Computation: { } computation, Diagnostics: not null } proposal
            || computation.AccrualFrom != request.AccrualFrom || computation.AccrualTo != request.AccrualTo
            || computation.Slices is null || computation.Diagnostics is null
            || computation.RuleVersion != IntercompanyFeeAccrualRules.RuleVersion
            || (computation.IsReady && (computation.Amount is null || string.IsNullOrWhiteSpace(computation.Currency)
                || proposal.Evidence is not { } evidence || proposal.Draft is not { } draft || string.IsNullOrWhiteSpace(proposal.EvidenceDigest)
                || evidence.SourceBookId != request.SourceBookId || evidence.AccountingArrangementId != request.FeeArrangementId
                || draft.LedgerBookId != request.SourceBookId || draft.JournalEntryId != evidence.DraftId))
            ? ApiResponse<IntercompanyFeeAccrualProposalDto>.Fail("The server returned a fee preview for a different request or without its evidence.", 502)
            : response;
    }

    public async Task<ApiResponse<ManualJournalEntryDraftDto>> CreateFeeAccrualDraftAsync(CreateIntercompanyFeeAccrualDraftRequest request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<ManualJournalEntryDraftDto>(FeeDraftRoute, request, ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } draft || draft.LedgerBookId != request.Accrual.SourceBookId
            || !string.Equals(draft.IntercompanyAccrualEvidenceDigest, request.ExpectedEvidenceDigest, StringComparison.Ordinal))
            ? ApiResponse<ManualJournalEntryDraftDto>.Fail("The server returned a draft for a different book or fee computation.", 502) : response;
    }
}
