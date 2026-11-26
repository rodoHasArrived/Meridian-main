using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    Task<ApiResponse<IntercompanyAgreementWorkQueueDto>> GetAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAgreementWorkQueueDto>.Fail("Agreement lifecycle queue is unavailable.", 503));
    Task<ApiResponse<IntercompanyAgreementWorkItemDto>> ObserveAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueObserveRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAgreementWorkItemDto>.Fail("Lifecycle observations cannot be retained.", 503));
    Task<ApiResponse<IntercompanyAgreementWorkItemDto>> UpdateAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueUpdateRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAgreementWorkItemDto>.Fail("Lifecycle case actions are unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string WorkQueueRoute = "/api/ledger/intercompany/work-queue";

    public async Task<ApiResponse<IntercompanyAgreementWorkQueueDto>> GetAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyAgreementWorkQueueDto>($"{WorkQueueRoute}?{WorkQueueQuery(scope)}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } queue || queue.LedgerBookId != scope.LedgerBookId
            || scope.FundProfileId is not null && queue.FundProfileId != scope.FundProfileId
            || queue.AsOf != scope.AsOf || queue.ExpiryHorizonDays != scope.ExpiryHorizonDays
            || queue.Cases is null || queue.Guarantees is null || queue.Diagnostics is null
            || queue.Cases.Any(item => item is null || item.LedgerBookId != scope.LedgerBookId || item.History is null))
            ? ApiResponse<IntercompanyAgreementWorkQueueDto>.Fail("The lifecycle queue belongs to a different book, date or incomplete history.", 502)
            : response;
    }

    public async Task<ApiResponse<IntercompanyAgreementWorkItemDto>> ObserveAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueObserveRequestDto request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<IntercompanyAgreementWorkItemDto>($"{WorkQueueRoute}/observe?{WorkQueueQuery(scope)}", request, ct).ConfigureAwait(false);
        return WorkQueueReceipt(response, scope, request.CaseId, request.CommandId);
    }

    public async Task<ApiResponse<IntercompanyAgreementWorkItemDto>> UpdateAgreementWorkQueueAsync(IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueUpdateRequestDto request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<IntercompanyAgreementWorkItemDto>($"{WorkQueueRoute}/update?{WorkQueueQuery(scope)}", request, ct).ConfigureAwait(false);
        return WorkQueueReceipt(response, scope, request.CaseId, request.CommandId, request.Action.ToString());
    }

    private static ApiResponse<IntercompanyAgreementWorkItemDto> WorkQueueReceipt(ApiResponse<IntercompanyAgreementWorkItemDto> response,
        IntercompanyObligationMonitorRequestDto scope, string caseId, Guid commandId, string? action = null)
        => response.Success && (response.Data is not { } item || item.CaseId != caseId || item.LedgerBookId != scope.LedgerBookId
            || !item.IsTracked || item.Version <= 0 || item.History is null
            || !item.History.Any(step => step.CommandId == commandId && (action is null || step.Action == action)))
            ? ApiResponse<IntercompanyAgreementWorkItemDto>.Fail("The lifecycle response does not contain this case action's retained receipt.", 502)
            : response;

    private static string WorkQueueQuery(IntercompanyObligationMonitorRequestDto scope)
    {
        if (scope.LedgerBookId is not { } book || book == Guid.Empty)
            throw new ArgumentException("Select an explicit ledger book for the lifecycle queue.", nameof(scope));
        var query = new List<string>
        {
            $"ledgerBookId={book:D}", $"asOf={scope.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"expiryHorizonDays={scope.ExpiryHorizonDays.ToString(CultureInfo.InvariantCulture)}"
        };
        if (!string.IsNullOrWhiteSpace(scope.FundProfileId))
            query.Add("fundProfileId=" + Uri.EscapeDataString(scope.FundProfileId));
        if (scope.EntityId.HasValue)
            query.Add($"entityId={scope.EntityId:D}");
        return string.Join("&", query);
    }
}
