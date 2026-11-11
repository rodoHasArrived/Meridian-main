using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class OperatorTriageException(string status, string message) : InvalidOperationException(message)
{
    public string Status { get; } = status;
}

/// <summary>Triage changes attention metadata; it never advances the underlying readiness gate.</summary>
public static class OperatorTriagePolicy
{
    public static OperatorTriageState ObserveSeverity(OperatorTriageState state, OperatorWorkItemToneDto tone, DateTimeOffset now)
    {
        var invalidated = state.Personal.Where(personal => personal.SnoozedUntil > now
            && (tone == OperatorWorkItemToneDto.Critical || (int?)personal.SnoozedTone < (int)tone)).ToArray();
        if (invalidated.Length == 0) return state;
        return state with
        {
            Version = state.Version + 1,
            Personal = state.Personal.Select(personal => invalidated.Contains(personal)
                ? personal with { SnoozedUntil = null, SnoozedTone = null } : personal).ToArray(),
            Activity = state.Activity.Concat(invalidated.Select(personal => new OperatorTriageActivity(
                $"severity:{state.Version + 1}:{personal.ActorId}", "system:severity-escalation", OperatorTriageAction.Unsnooze,
                "Snooze cancelled because the source severity increased.", now, personal.ActorId))).ToArray()
        };
    }

    public static bool IsSnoozed(OperatorTriagePersonalState? personal, OperatorWorkItemToneDto tone, DateTimeOffset now)
        => tone != OperatorWorkItemToneDto.Critical && personal?.SnoozedUntil > now && (int?)personal.SnoozedTone >= (int)tone;

    public static OperatorTriageState Apply(OperatorTriageState state, string actorId, OperatorTriageCommand command,
        OperatorWorkItemToneDto tone, DateTimeOffset now, bool sourceOwnsAssignment, out bool replayed)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 128)
            throw new OperatorTriageException("Rejected", "A valid actor and command identity are required.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
        var receipt = state.Receipts.FirstOrDefault(r => r.ActorId == actorId && r.CommandId == command.CommandId);
        replayed = receipt is not null;
        if (receipt is not null)
        {
            if (receipt.Fingerprint != fingerprint)
                throw new OperatorTriageException("Conflict", "This command identity was already used for different inputs.");
            return state;
        }
        if (command.ExpectedVersion != state.Version)
            throw new OperatorTriageException("Conflict", "The item changed. Refresh it before retrying.");
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 2000)
            throw new OperatorTriageException("Rejected", "A reason of 1–2000 characters is required.");
        var personal = state.Personal.FirstOrDefault(p => p.ActorId == actorId)
            ?? new OperatorTriagePersonalState(actorId, null, null, null);
        var assignee = state.AssigneeId;
        switch (command.Action)
        {
            case OperatorTriageAction.Assign:
                if (string.IsNullOrWhiteSpace(command.AssigneeId) || command.AssigneeId.Length > 256)
                    throw new OperatorTriageException("Rejected", "Select a valid operator to assign.");
                if (!sourceOwnsAssignment) assignee = command.AssigneeId;
                break;
            case OperatorTriageAction.Acknowledge:
                personal = personal with { AcknowledgedAt = now };
                break;
            case OperatorTriageAction.Snooze:
                if (tone == OperatorWorkItemToneDto.Critical)
                    throw new OperatorTriageException("Rejected", "Blocked or critical work cannot be snoozed.");
                if (command.SnoozedUntil is null || command.SnoozedUntil <= now || command.SnoozedUntil > now.AddDays(7))
                    throw new OperatorTriageException("Rejected", "Snooze requires a future expiry within seven days.");
                personal = personal with { SnoozedUntil = command.SnoozedUntil, SnoozedTone = tone };
                break;
            case OperatorTriageAction.Unsnooze:
                personal = personal with { SnoozedUntil = null, SnoozedTone = null };
                break;
            default:
                throw new OperatorTriageException("Rejected", "Unsupported triage action.");
        }
        return new OperatorTriageState(state.Version + 1, assignee,
            state.Personal.Where(p => p.ActorId != actorId).Append(personal).ToArray(),
            state.Activity.Append(new OperatorTriageActivity(command.CommandId, actorId, command.Action, command.Reason.Trim(), now,
                AssigneeId: command.Action == OperatorTriageAction.Assign ? command.AssigneeId : null,
                SnoozedUntil: command.Action == OperatorTriageAction.Snooze ? command.SnoozedUntil : null)).ToArray(),
            state.Receipts.Append(new OperatorTriageReceipt(actorId, command.CommandId, fingerprint)).ToArray());
    }
}
