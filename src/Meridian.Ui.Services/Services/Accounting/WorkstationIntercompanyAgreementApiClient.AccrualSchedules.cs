using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>Retained accrual schedules of one book, read from the same route the browser workstation uses.</summary>
    Task<ApiResponse<IntercompanyAccrualScheduleDto[]>> GetAccrualSchedulesAsync(Guid bookId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto[]>.Fail("Intercompany accrual schedules are unavailable.", 503));
    /// <summary>Proposes a schedule for review. It drafts nothing until another operator approves it.</summary>
    Task<ApiResponse<IntercompanyAccrualScheduleDto>> ProposeAccrualScheduleAsync(ProposeIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto>.Fail("Intercompany accrual schedule proposals are unavailable.", 503));
    /// <summary>Approves, rejects or disables exactly the schedule version the reviewer saw.</summary>
    Task<ApiResponse<IntercompanyAccrualScheduleDto>> DecideAccrualScheduleAsync(Guid scheduleId, Guid bookId, DecideIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleDto>.Fail("Intercompany accrual schedule decisions are unavailable.", 503));
    /// <summary>Dry run: the completed periods a run would draft as of a date. Nothing is created.</summary>
    Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> GetAccrualScheduleDueAsync(Guid scheduleId, Guid bookId, DateOnly asOf, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleRunDto>.Fail("Intercompany accrual schedule previews are unavailable.", 503));
    /// <summary>Runs an active schedule now. It drafts for ordinary review only; nothing is approved or posted.</summary>
    Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> RunAccrualScheduleAsync(Guid scheduleId, Guid bookId, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyAccrualScheduleRunDto>.Fail("Intercompany accrual schedule runs are unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string AccrualScheduleRoute = "/api/ledger/intercompany/accrual-schedules";

    public async Task<ApiResponse<IntercompanyAccrualScheduleDto[]>> GetAccrualSchedulesAsync(Guid bookId, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyAccrualScheduleDto[]>($"{AccrualScheduleRoute}?ledgerBookId={bookId:D}", ct).ConfigureAwait(false);
        if (!response.Success)
            return response;
        if (response.Data is null)
            return ApiResponse<IntercompanyAccrualScheduleDto[]>.Ok([]);
        // A listing that mixes in another book's schedules is not this book's answer.
        return response.Data.Any(schedule => schedule is null || schedule.LedgerBookId != bookId || schedule.Events is null)
            ? ApiResponse<IntercompanyAccrualScheduleDto[]>.Fail("The server returned accrual schedules of a different book.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyAccrualScheduleDto>> ProposeAccrualScheduleAsync(ProposeIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<IntercompanyAccrualScheduleDto>(AccrualScheduleRoute, request, ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } schedule || schedule.LedgerBookId != request.LedgerBookId
            || schedule.Kind != request.Kind || schedule.ProfileArrangementId != request.ProfileArrangementId
            || schedule.PrincipalArrangementId != request.PrincipalArrangementId
            || !Recorded(schedule, request.RequestId, IntercompanyAccrualScheduleActionDto.Propose))
            ? ApiResponse<IntercompanyAccrualScheduleDto>.Fail("The server returned a schedule for a different book, profile or proposal.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyAccrualScheduleDto>> DecideAccrualScheduleAsync(Guid scheduleId, Guid bookId, DecideIntercompanyAccrualScheduleRequest request, CancellationToken ct = default)
    {
        var response = await apiClient.PostWithResponseAsync<IntercompanyAccrualScheduleDto>(
            $"{AccrualScheduleRoute}/{scheduleId:D}/decisions?ledgerBookId={bookId:D}", request, ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } schedule || schedule.ScheduleId != scheduleId || schedule.LedgerBookId != bookId
            || !Recorded(schedule, request.RequestId, request.Action))
            ? ApiResponse<IntercompanyAccrualScheduleDto>.Fail("The server returned a schedule for a different book or decision.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> GetAccrualScheduleDueAsync(Guid scheduleId, Guid bookId, DateOnly asOf, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyAccrualScheduleRunDto>(
            $"{AccrualScheduleRoute}/{scheduleId:D}/due?ledgerBookId={bookId:D}&asOf={asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } run || run.ScheduleId != scheduleId || run.AsOf != asOf || run.Executed || run.Windows is null)
            ? ApiResponse<IntercompanyAccrualScheduleRunDto>.Fail("The server returned due periods for a different schedule or date.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyAccrualScheduleRunDto>> RunAccrualScheduleAsync(Guid scheduleId, Guid bookId, CancellationToken ct = default)
    {
        // The run carries no body; the server dates it and records the schedule as the drafts' preparer.
        var response = await apiClient.PostWithResponseAsync<IntercompanyAccrualScheduleRunDto>(
            $"{AccrualScheduleRoute}/{scheduleId:D}/run?ledgerBookId={bookId:D}", null, ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } run || run.ScheduleId != scheduleId || !run.Executed || run.Windows is null)
            ? ApiResponse<IntercompanyAccrualScheduleRunDto>.Fail("The server returned a run of a different schedule.", 502) : response;
    }

    // A mutation's answer must record this request's identity for this action; anything else is another command's result.
    private static bool Recorded(IntercompanyAccrualScheduleDto schedule, Guid requestId, IntercompanyAccrualScheduleActionDto action)
        => schedule.Events is not null && schedule.Events.Any(step => step.RequestId == requestId && step.Action == action);
}
