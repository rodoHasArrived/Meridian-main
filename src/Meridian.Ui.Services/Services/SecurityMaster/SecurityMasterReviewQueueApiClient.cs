using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Services.Services.SecurityMaster;

/// <summary>
/// Desktop transport for the Security Master review queues: the cross-security review inbox of pending
/// revisions (review P5/D1), ingest amendments held by source ranking (review A2) and the global
/// exception queue (review D2). It uses the same routes and shared contracts as the browser workstation;
/// the server derives tenant and actor from the session and re-checks every permission.
/// </summary>
public interface ISecurityMasterReviewQueueApiClient
{
    /// <summary>Revisions awaiting action across every security, oldest first.</summary>
    Task<ApiResponse<IReadOnlyList<SecurityMasterPendingRevisionDto>>> ListPendingRevisionsAsync(
        IReadOnlyCollection<SecurityMasterRevisionStateDto> states, CancellationToken ct = default);

    /// <summary>Discards a Draft or Submitted revision so its staged value no longer blocks the field.</summary>
    Task<ApiResponse<SecurityMasterEditResultDto>> DiscardRevisionAsync(
        Guid securityId, Guid revisionId, string? reason, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<SecurityMasterHeldAmendmentDto>>> ListHeldAmendmentsAsync(
        Guid? securityId = null, string? status = null, CancellationToken ct = default);

    /// <summary>Closes a held amendment as Discarded. 204 on success; 409 when it is no longer held.</summary>
    Task<ApiResponse<SecurityMasterQueueAck>> DiscardHeldAmendmentAsync(Guid heldAmendmentId, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<SecurityMasterExceptionCaseQueueItemDto>>> ListExceptionCasesAsync(
        string? status = null, CancellationToken ct = default);

    /// <summary>Reconciles open cases with the current conflicts, ambiguities and violations.</summary>
    Task<ApiResponse<SecurityMasterExceptionSyncResultDto>> SyncExceptionCasesAsync(CancellationToken ct = default);

    /// <summary>Assigns an open case. 204 on success; 409 when the case is no longer open.</summary>
    Task<ApiResponse<SecurityMasterQueueAck>> AssignExceptionCaseAsync(Guid caseId, string assignee, CancellationToken ct = default);
}

/// <summary>Acknowledgement of a queue write that answers with no body (HTTP 204).</summary>
public sealed record SecurityMasterQueueAck(int StatusCode);

public sealed class SecurityMasterReviewQueueApiClient(ApiClientService apiClient) : ISecurityMasterReviewQueueApiClient
{
    private readonly ApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

    public async Task<ApiResponse<IReadOnlyList<SecurityMasterPendingRevisionDto>>> ListPendingRevisionsAsync(
        IReadOnlyCollection<SecurityMasterRevisionStateDto> states, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(states);
        var endpoint = UiApiRoutes.SecurityMasterWorkbenchPendingRevisions;
        if (states.Count > 0)
        {
            endpoint += "?state=" + Uri.EscapeDataString(string.Join(',', states.Distinct().Select(state => state.ToString())));
        }

        return List(await _apiClient.GetWithResponseAsync<List<SecurityMasterPendingRevisionDto>>(endpoint, ct).ConfigureAwait(false));
    }

    public async Task<ApiResponse<SecurityMasterEditResultDto>> DiscardRevisionAsync(
        Guid securityId, Guid revisionId, string? reason, CancellationToken ct = default)
    {
        // Identity fields are overwritten server-side: the path securityId is authoritative and the actor
        // is the session principal, so the desktop sends the business fields only.
        var request = new DiscardSecurityMasterRevisionRequest(securityId, revisionId, Actor: string.Empty,
            Reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        var response = await _apiClient.PostWithResponseAsync<SecurityMasterEditResultDto>(
            UiApiRoutes.WithParam(UiApiRoutes.SecurityMasterWorkbenchDiscard, "securityId", securityId.ToString("D")), request, ct).ConfigureAwait(false);
        if (response.Success && response.Data is null)
        {
            return ApiResponse<SecurityMasterEditResultDto>.Fail("The server returned an empty discard response.", 502);
        }

        return response.Success && response.Data!.RevisionId != revisionId
            ? ApiResponse<SecurityMasterEditResultDto>.Fail("The server discarded a different revision.", 502)
            : response;
    }

    public async Task<ApiResponse<IReadOnlyList<SecurityMasterHeldAmendmentDto>>> ListHeldAmendmentsAsync(
        Guid? securityId = null, string? status = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (securityId is { } id && id != Guid.Empty)
        {
            query.Add("securityId=" + id.ToString("D"));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add("status=" + Uri.EscapeDataString(status.Trim()));
        }

        return List(await _apiClient.GetWithResponseAsync<List<SecurityMasterHeldAmendmentDto>>(
            WithQuery(UiApiRoutes.SecurityMasterHeldAmendments, query), ct).ConfigureAwait(false));
    }

    public async Task<ApiResponse<SecurityMasterQueueAck>> DiscardHeldAmendmentAsync(Guid heldAmendmentId, CancellationToken ct = default)
        => Ack(await _apiClient.PostWithResponseAsync<object>(
            UiApiRoutes.WithParam(UiApiRoutes.SecurityMasterHeldAmendmentDiscard, "heldAmendmentId", heldAmendmentId.ToString("D")),
            body: null, ct).ConfigureAwait(false));

    public async Task<ApiResponse<IReadOnlyList<SecurityMasterExceptionCaseQueueItemDto>>> ListExceptionCasesAsync(
        string? status = null, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(status) ? [] : new List<string> { "status=" + Uri.EscapeDataString(status.Trim()) };
        return List(await _apiClient.GetWithResponseAsync<List<SecurityMasterExceptionCaseQueueItemDto>>(
            WithQuery(UiApiRoutes.SecurityMasterExceptionCases, query), ct).ConfigureAwait(false));
    }

    public async Task<ApiResponse<SecurityMasterExceptionSyncResultDto>> SyncExceptionCasesAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SecurityMasterExceptionSyncResultDto>(
            UiApiRoutes.SecurityMasterExceptionCasesSync, body: null, ct).ConfigureAwait(false);
        return response.Success && response.Data is null
            ? ApiResponse<SecurityMasterExceptionSyncResultDto>.Fail("The server returned an empty sync response.", 502)
            : response;
    }

    public async Task<ApiResponse<SecurityMasterQueueAck>> AssignExceptionCaseAsync(Guid caseId, string assignee, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignee);
        return Ack(await _apiClient.PostWithResponseAsync<object>(
            UiApiRoutes.WithParam(UiApiRoutes.SecurityMasterExceptionCaseAssign, "caseId", caseId.ToString("D")),
            new AssignSecurityMasterExceptionCaseRequest(assignee.Trim()), ct).ConfigureAwait(false));
    }

    private static string WithQuery(string route, IReadOnlyList<string> query)
        => query.Count == 0 ? route : route + "?" + string.Join('&', query);

    private static ApiResponse<IReadOnlyList<T>> List<T>(ApiResponse<List<T>> response)
        => response.Success
            ? ApiResponse<IReadOnlyList<T>>.Ok(response.Data ?? [], response.StatusCode)
            : ApiResponse<IReadOnlyList<T>>.Fail(response.ErrorMessage ?? string.Empty, response.StatusCode, response.IsConnectionError);

    private static ApiResponse<SecurityMasterQueueAck> Ack(ApiResponse<object> response)
        => response.Success
            ? ApiResponse<SecurityMasterQueueAck>.Ok(new SecurityMasterQueueAck(response.StatusCode), response.StatusCode)
            : ApiResponse<SecurityMasterQueueAck>.Fail(response.ErrorMessage ?? string.Empty, response.StatusCode, response.IsConnectionError);
}

/// <summary>
/// Presentation rules shared by the desktop review queues, mirroring the browser inbox so both lanes
/// show the same ages, the same discard eligibility and the server's own SLA verdict.
/// </summary>
public static class SecurityMasterReviewQueuePresentation
{
    /// <summary>The browser inbox's default filter: everything an operator can still act on.</summary>
    public static IReadOnlyList<SecurityMasterRevisionStateDto> InboxStates { get; } =
        [SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved];

    /// <summary>Mirrors the browser <c>formatRevisionAge</c>: under 1h, whole hours below 48h, then days.</summary>
    public static string FormatAge(double ageHours)
    {
        if (double.IsNaN(ageHours) || ageHours < 1)
        {
            return "under 1h";
        }

        return ageHours < 48
            ? Math.Round(ageHours, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "h"
            : Math.Round(ageHours / 24, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "d";
    }

    /// <summary>
    /// Only abandoned Draft and gate-rejected Submitted revisions are discardable from the inbox; an
    /// Approved revision is published or withdrawn through its workflow.
    /// </summary>
    public static bool CanDiscard(SecurityMasterRevisionStateDto state)
        => state is SecurityMasterRevisionStateDto.Draft or SecurityMasterRevisionStateDto.Submitted;

    /// <summary>The SLA cell: the server's breach verdict first, otherwise the UTC due time.</summary>
    public static string DescribeSla(SecurityMasterExceptionCaseQueueItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var due = item.Row.SlaDueAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        return item.SlaBreached ? "Breached (due " + due + ")" : "Due " + due;
    }
}
