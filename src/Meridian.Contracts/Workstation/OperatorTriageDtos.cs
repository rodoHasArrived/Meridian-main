using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<OperatorTriageAction>))]
public enum OperatorTriageAction { Assign, Acknowledge, Snooze, Unsnooze }

public sealed record OperatorTriageCommand(
    string CommandId,
    OperatorTriageAction Action,
    long ExpectedVersion,
    string Reason,
    string? AssigneeId = null,
    DateTimeOffset? SnoozedUntil = null,
    long? ExpectedSourceVersion = null);

public sealed record OperatorTriageBulkItem(string WorkItemId, long ExpectedVersion, long? ExpectedSourceVersion = null);
public sealed record OperatorTriageBulkCommand(
    string CommandId, OperatorTriageAction Action, string Reason, string AssigneeId,
    IReadOnlyList<OperatorTriageBulkItem> Items);

public sealed record OperatorTriageActivity(
    string CommandId, string ActorId, OperatorTriageAction Action, string Reason, DateTimeOffset OccurredAt,
    string? SubjectActorId = null, string? AssigneeId = null, DateTimeOffset? SnoozedUntil = null);
public sealed record OperatorTriageCapabilities(
    bool CanAssign, bool CanAcknowledge, bool CanSnooze, bool CanUnsnooze, string? DisabledReason);
public sealed record OperatorTriageItemDto(
    string WorkItemId, string Label, string Detail, string? Workspace, string? TargetRoute,
    OperatorWorkItemToneDto Tone, DateTimeOffset CreatedAt, string? OwnerId, DateTimeOffset? DueAt,
    long Version, long? SourceVersion, DateTimeOffset? AcknowledgedAt, DateTimeOffset? SnoozedUntil,
    OperatorTriageCapabilities Capabilities, IReadOnlyList<OperatorTriageActivity> Activity,
    Guid? FundAccountId = null, string? RunId = null, string? Scope = null,
    OperatorWorkItemKindDto? Kind = null, string? TargetPageTag = null);
public sealed record OperatorInboxTriageDto(
    DateTimeOffset AsOf, string ActorId, IReadOnlyList<OperatorTriageItemDto> Items,
    bool PersistenceAvailable, string? UnavailableReason);
public sealed record OperatorTriageResult(
    string Status, string WorkItemId, OperatorTriageItemDto? Item = null, string? Error = null, bool Replayed = false);
public sealed record OperatorTriageBulkResult(IReadOnlyList<OperatorTriageResult> Results);

/// <summary>Metadata only: never a copy of the source work item or financial workflow.</summary>
public sealed record OperatorTriageState(
    long Version, string? AssigneeId,
    IReadOnlyList<OperatorTriagePersonalState> Personal,
    IReadOnlyList<OperatorTriageActivity> Activity,
    IReadOnlyList<OperatorTriageReceipt> Receipts)
{
    public static OperatorTriageState Empty => new(0, null, [], [], []);
}
public sealed record OperatorTriagePersonalState(
    string ActorId, DateTimeOffset? AcknowledgedAt, DateTimeOffset? SnoozedUntil, OperatorWorkItemToneDto? SnoozedTone);
public sealed record OperatorTriageReceipt(string ActorId, string CommandId, string Fingerprint);
public sealed record OperatorTriageScope(string TenantId, string CompanyId);

/// <summary>
/// Serializes one item's metadata update. Implementations persist state, activity and replay receipt
/// together. The callback may delegate a source-owned assignment using its idempotent command.
/// </summary>
public interface IOperatorTriageStore
{
    Task<OperatorTriageState> GetAsync(OperatorTriageScope scope, string workItemId, CancellationToken ct = default);
    Task<OperatorTriageState> UpdateAsync(OperatorTriageScope scope, string workItemId,
        Func<OperatorTriageState, CancellationToken, Task<OperatorTriageState>> update, CancellationToken ct = default);
}
