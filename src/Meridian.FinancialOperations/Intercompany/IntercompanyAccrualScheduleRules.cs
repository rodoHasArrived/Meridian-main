using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>
/// Pure transitions of an accrual schedule. A schedule drafts nothing until someone other than its proposer approves
/// it; it is active from the approval date, and disabling it stops future runs without touching existing drafts.
/// </summary>
public static class IntercompanyAccrualScheduleRules
{
    public const int MaximumReasonLength = 500;

    public static IntercompanyAccrualScheduleDto Propose(string tenantId, string? companyId, string actor,
        ProposeIntercompanyAccrualScheduleRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind))
            throw new ArgumentException("The schedule kind is not supported.");
        if (request.LedgerBookId == Guid.Empty || request.ProfileArrangementId == Guid.Empty)
            throw new ArgumentException("A ledger book and an accounting profile are required.");
        if (request.Kind == IntercompanyAccrualScheduleKindDto.LoanInterest
            && (request.PrincipalArrangementId is not { } principal || principal == Guid.Empty || principal == request.ProfileArrangementId))
            throw new ArgumentException("A loan-interest schedule names its separately reviewed loan principal profile.");
        var reason = Text(request.Reason, "A reason");
        return new(Guid.NewGuid(), tenantId, companyId, request.LedgerBookId, request.Kind, request.ProfileArrangementId,
            request.PrincipalArrangementId, 1, IntercompanyAccrualScheduleStatusDto.Proposed, null,
            [new(Guid.NewGuid(), request.RequestId, IntercompanyAccrualScheduleActionDto.Propose, actor, at, reason)]);
    }

    public static IntercompanyAccrualScheduleDto Decide(IntercompanyAccrualScheduleDto current, string actor,
        DecideIntercompanyAccrualScheduleRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        if (current.Version != request.ExpectedVersion)
            throw new InvalidOperationException("The schedule changed since it was read. Reload it before deciding.");
        var proposer = current.Events.First(item => item.Action == IntercompanyAccrualScheduleActionDto.Propose).Actor;
        IntercompanyAccrualScheduleDto Next(IntercompanyAccrualScheduleStatusDto status, string? reason, DateOnly? activeFrom = null) => current with
        {
            Version = current.Version + 1,
            Status = status,
            ActiveFrom = activeFrom ?? current.ActiveFrom,
            Events = [.. current.Events, new(Guid.NewGuid(), request.RequestId, request.Action, actor, at, reason)]
        };
        return (request.Action, current.Status) switch
        {
            (IntercompanyAccrualScheduleActionDto.Approve, IntercompanyAccrualScheduleStatusDto.Proposed)
                => string.Equals(actor, proposer, StringComparison.OrdinalIgnoreCase)
                    ? throw new InvalidOperationException("A schedule is approved by someone other than the person who proposed it.")
                    : Next(IntercompanyAccrualScheduleStatusDto.Active, null, DateOnly.FromDateTime(at.UtcDateTime)),
            (IntercompanyAccrualScheduleActionDto.Reject, IntercompanyAccrualScheduleStatusDto.Proposed)
                => Next(IntercompanyAccrualScheduleStatusDto.Rejected, Text(request.Reason, "A rejection reason")),
            (IntercompanyAccrualScheduleActionDto.Disable, IntercompanyAccrualScheduleStatusDto.Active)
                => Next(IntercompanyAccrualScheduleStatusDto.Disabled, Text(request.Reason, "A reason for disabling")),
            _ => throw new InvalidOperationException($"A {current.Status} schedule cannot be {request.Action switch
            {
                IntercompanyAccrualScheduleActionDto.Approve => "approved",
                IntercompanyAccrualScheduleActionDto.Reject => "rejected",
                IntercompanyAccrualScheduleActionDto.Disable => "disabled",
                _ => "changed that way"
            }}.")
        };
    }

    /// <summary>True when this request already produced a step of the schedule; a reused identity for another step conflicts.</summary>
    public static bool IsReplay(IntercompanyAccrualScheduleDto? current, Guid requestId, IntercompanyAccrualScheduleActionDto action)
    {
        if (current?.Events.FirstOrDefault(item => item.RequestId == requestId) is not { } step)
            return false;
        return step.Action == action ? true
            : throw new InvalidOperationException("This request identity already recorded a different schedule step.");
    }

    private static string Text(string? value, string label)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length is > 0 and <= MaximumReasonLength
            ? text : throw new ArgumentException($"{label} of 1 to {MaximumReasonLength} characters is required.");
    }
}
