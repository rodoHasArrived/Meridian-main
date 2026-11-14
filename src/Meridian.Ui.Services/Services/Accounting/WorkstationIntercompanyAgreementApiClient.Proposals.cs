using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>Server-resolved permissions for the signed-in account; never inferred on the workstation.</summary>
public sealed record IntercompanyOperationCapabilities(bool CanManageAgreements = false, bool CanManageLifecycle = false,
    bool CanManageLedger = false, bool CanViewLedger = false);

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyOperationCapabilities>.Fail("Intercompany permissions are unavailable.", 503));
    Task<ApiResponse<IntercompanyProposalDto[]>> GetCounterpartProposalsAsync(Guid sourceBookId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyProposalDto[]>.Fail("Counterpart delivery records are unavailable.", 503));
    Task<ApiResponse<IntercompanyBreakDto[]>> GetCounterpartBreaksAsync(Guid sourceBookId, DateOnly asOf, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyBreakDto[]>.Fail("Counterpart booking breaks are unavailable.", 503));
    Task<ApiResponse<IntercompanyProposalDto>> RetryCounterpartProposalAsync(Guid proposalId, RetryIntercompanyProposalRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyProposalDto>.Fail("Counterpart delivery retry is unavailable.", 503));
    Task<ApiResponse<IntercompanyProposalDto>> LinkCounterpartProposalAsync(Guid proposalId, LinkIntercompanyCounterpartRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyProposalDto>.Fail("Counterpart linking is unavailable.", 503));
    Task<ApiResponse<IntercompanyRefreshPreviewDto>> PreviewCounterpartRefreshAsync(Guid proposalId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyRefreshPreviewDto>.Fail("Counterpart repricing preview is unavailable.", 503));
    Task<ApiResponse<ManualJournalEntryDraftDto>> RefreshCounterpartProposalAsync(Guid proposalId, RefreshIntercompanyProposalRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<ManualJournalEntryDraftDto>.Fail("Counterpart repricing is unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string ProposalRoute = "/api/ledger/intercompany/proposals";

    public Task<ApiResponse<IntercompanyOperationCapabilities>> GetIntercompanyCapabilitiesAsync(CancellationToken ct = default)
        => apiClient.GetWithResponseAsync<IntercompanyOperationCapabilities>("/api/ledger/intercompany/capabilities", ct);

    public async Task<ApiResponse<IntercompanyProposalDto[]>> GetCounterpartProposalsAsync(Guid sourceBookId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyProposalDto[]>($"{ProposalRoute}?sourceBookId={sourceBookId:D}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is null || response.Data.Any(x => x.Source is null || x.Source.SourceBookId != sourceBookId))
            ? ApiResponse<IntercompanyProposalDto[]>.Fail("Counterpart delivery records belong to a different source book.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyBreakDto[]>> GetCounterpartBreaksAsync(Guid sourceBookId, DateOnly asOf, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyBreakDto[]>(
            $"/api/ledger/intercompany/breaks?sourceBookId={sourceBookId:D}&asOf={asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", ct).ConfigureAwait(false);
        return response.Success && response.Data is null
            ? ApiResponse<IntercompanyBreakDto[]>.Fail("The server returned no counterpart break observation.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyProposalDto>> RetryCounterpartProposalAsync(Guid proposalId, RetryIntercompanyProposalRequest request, CancellationToken ct = default)
        => SameProposal(await apiClient.PostWithResponseAsync<IntercompanyProposalDto>($"{ProposalRoute}/{proposalId:D}/retry", request, ct).ConfigureAwait(false), proposalId);

    public async Task<ApiResponse<IntercompanyProposalDto>> LinkCounterpartProposalAsync(Guid proposalId, LinkIntercompanyCounterpartRequest request, CancellationToken ct = default)
    {
        var response = SameProposal(await apiClient.PostWithResponseAsync<IntercompanyProposalDto>($"{ProposalRoute}/{proposalId:D}/link", request, ct).ConfigureAwait(false), proposalId);
        return response.Success && (response.Data!.LatestLink is not { } link || link.ProposalId != proposalId
            || link.TargetBookId != request.TargetBookId || link.TargetJournalId != request.TargetJournalId)
            ? ApiResponse<IntercompanyProposalDto>.Fail("The server did not retain the requested counterpart link.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyRefreshPreviewDto>> PreviewCounterpartRefreshAsync(Guid proposalId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyRefreshPreviewDto>($"{ProposalRoute}/{proposalId:D}/refresh-preview", ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } preview || preview.Draft is null || string.IsNullOrWhiteSpace(preview.Fingerprint)
            || preview.CurrentDraft is { } current && current.JournalEntryId != preview.Draft.JournalEntryId)
            ? ApiResponse<IntercompanyRefreshPreviewDto>.Fail("The repricing preview does not describe one retained counterpart draft.", 502) : response;
    }

    public async Task<ApiResponse<ManualJournalEntryDraftDto>> RefreshCounterpartProposalAsync(Guid proposalId, RefreshIntercompanyProposalRequest request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<ManualJournalEntryDraftDto>($"{ProposalRoute}/{proposalId:D}/refresh", request, ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } draft || draft.IntercompanyProposalId != proposalId || draft.IntercompanyRefreshRequestId != request.RequestId)
            ? ApiResponse<ManualJournalEntryDraftDto>.Fail("The server returned a draft for a different counterpart or repricing request.", 502) : response;
    }

    private static ApiResponse<IntercompanyProposalDto> SameProposal(ApiResponse<IntercompanyProposalDto> response, Guid proposalId)
        => response.Success && (response.Data is not { Source: { } source } || source.ProposalId != proposalId)
            ? ApiResponse<IntercompanyProposalDto>.Fail("The server returned a different counterpart delivery record.", 502) : response;
}
