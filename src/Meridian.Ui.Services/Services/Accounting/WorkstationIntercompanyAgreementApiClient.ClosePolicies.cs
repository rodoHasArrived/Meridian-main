using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>The book's confirmation close policies (ADR-032), every retained status.</summary>
    Task<ApiResponse<IntercompanyConfirmationClosePolicyDto[]>> GetConfirmationClosePoliciesAsync(Guid ledgerBookId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyConfirmationClosePolicyDto[]>.Fail("Confirmation close policies are unavailable.", 503));
    Task<ApiResponse<IntercompanyConfirmationClosePolicyDto>> ProposeConfirmationClosePolicyAsync(ProposeIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyConfirmationClosePolicyDto>.Fail("Confirmation close policies are unavailable.", 503));
    Task<ApiResponse<IntercompanyConfirmationClosePolicyDto>> DecideConfirmationClosePolicyAsync(Guid policyId, Guid ledgerBookId,
        DecideIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyConfirmationClosePolicyDto>.Fail("Confirmation close policies are unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string ClosePolicyRoute = "/api/ledger/intercompany/confirmation-close-policies";

    public async Task<ApiResponse<IntercompanyConfirmationClosePolicyDto[]>> GetConfirmationClosePoliciesAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyConfirmationClosePolicyDto[]>($"{ClosePolicyRoute}?ledgerBookId={ledgerBookId:D}", ct).ConfigureAwait(false);
        return response.Success && (response.Data ?? []).Any(item => item.LedgerBookId != ledgerBookId)
            ? ApiResponse<IntercompanyConfirmationClosePolicyDto[]>.Fail("The close policies belong to a different book.", 502)
            : response.Success && response.Data is null ? ApiResponse<IntercompanyConfirmationClosePolicyDto[]>.Ok([]) : response;
    }

    public async Task<ApiResponse<IntercompanyConfirmationClosePolicyDto>> ProposeConfirmationClosePolicyAsync(ProposeIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
        => Recorded(await apiClient.PostWithResponseAsync<IntercompanyConfirmationClosePolicyDto>(ClosePolicyRoute, request, ct).ConfigureAwait(false),
            request.LedgerBookId, request.RequestId);

    public async Task<ApiResponse<IntercompanyConfirmationClosePolicyDto>> DecideConfirmationClosePolicyAsync(Guid policyId, Guid ledgerBookId,
        DecideIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
    {
        var response = Recorded(await apiClient.PostWithResponseAsync<IntercompanyConfirmationClosePolicyDto>(
            $"{ClosePolicyRoute}/{policyId:D}/decisions?ledgerBookId={ledgerBookId:D}", request, ct).ConfigureAwait(false), ledgerBookId, request.RequestId);
        return response.Success && response.Data!.PolicyId != policyId
            ? ApiResponse<IntercompanyConfirmationClosePolicyDto>.Fail("The server returned a different close policy.", 502) : response;
    }

    private static ApiResponse<IntercompanyConfirmationClosePolicyDto> Recorded(ApiResponse<IntercompanyConfirmationClosePolicyDto> response, Guid ledgerBookId, Guid requestId)
        => response.Success && (response.Data is not { } policy || policy.LedgerBookId != ledgerBookId || policy.Events.All(step => step.RequestId != requestId))
            ? ApiResponse<IntercompanyConfirmationClosePolicyDto>.Fail("The close-policy response does not record this request for this book.", 502) : response;
}
