using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<CloseDeadlineAnchorDto>))]
public enum CloseDeadlineAnchorDto { PeriodStart, PeriodEnd }
[JsonConverter(typeof(JsonStringEnumConverter<CloseDeadlineDayCountDto>))]
public enum CloseDeadlineDayCountDto { CalendarDays, BusinessDays }
[JsonConverter(typeof(JsonStringEnumConverter<CloseDeadlineAdjustmentDto>))]
public enum CloseDeadlineAdjustmentDto { None, FollowingBusinessDay, PrecedingBusinessDay }

/// <summary>Calendar dates, not instants. Offset excludes the anchor; adjustment follows the offset.</summary>
public sealed record CloseDeadlineRuleDto(string TaskId, CloseDeadlineAnchorDto Anchor, int OffsetDays,
    CloseDeadlineDayCountDto DayCount, CloseDeadlineAdjustmentDto Adjustment);

/// <summary>An explicit, immutable operator-selected calendar; Sunday is 0 and Saturday is 6.</summary>
public sealed record ClosePreparationCalendarDto(string CalendarId, string Version,
    IReadOnlyList<int> WeekendDays, IReadOnlyList<DateOnly> Holidays);

public sealed record CaptureClosePlanTemplateRequestDto(Guid SourceWorkflowId, string Name,
    IReadOnlyList<CloseDeadlineRuleDto> DeadlineRules, ClosePreparationCalendarDto Calendar,
    Guid? TemplateId = null, string? Actor = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record ClosePlanTemplateTaskDto(CloseTaskConfigurationDto Configuration,
    DateOnly SourceDueDate, CloseDeadlineRuleDto DeadlineRule);

public sealed record ClosePreparationHistoryDto(string EventType, DateTimeOffset OccurredAtUtc,
    string Actor, string Description);

public sealed record ClosePlanPreparationLineageDto(Guid TemplateId, int TemplateVersion,
    Guid SourceWorkflowId, Guid TargetPeriodId, DateOnly PeriodStart, DateOnly PeriodEnd,
    DateTimeOffset CreatedAtUtc, string CreatedBy, IReadOnlyList<ClosePreparationHistoryDto> History);

public sealed record ClosePlanTemplateDto(Guid TemplateId, int Version, string Name,
    Guid SourceWorkflowId, Guid SourceLedgerBookId, string SourcePeriodId, Guid FundAccountId,
    string FundProfileId, string SourceAccountingPolicyId, string SourceAccountingPolicyVersion,
    MaterialityPolicyDto MaterialityPolicy, ClosePreparationCalendarDto Calendar,
    IReadOnlyList<ClosePlanTemplateTaskDto> Tasks, DateTimeOffset CapturedAtUtc, string CapturedBy,
    IReadOnlyList<ClosePreparationHistoryDto> History);

/// <summary>Explicit owner assignment for this preparation; it does not certify directory membership.</summary>
public sealed record ClosePreparationOwnerMappingDto(string SourceOwner, string TargetOwner);

public sealed record PreviewClosePreparationRequestDto(Guid TemplateId, int TemplateVersion,
    Guid TargetLedgerBookId, Guid TargetPeriodId, IReadOnlyList<ClosePreparationOwnerMappingDto> OwnerMappings,
    bool AcknowledgePolicyChange = false, string? Actor = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record ClosePreparationIssueDto(string Code, string Message, bool IsBlocking, string? TaskId = null);

public sealed record ClosePreparationTaskDto(string TaskId, string DisplayName, string SourceOwner,
    string Owner, DateOnly SourceDueDate, DateOnly DueDate, CloseDeadlineRuleDto DeadlineRule,
    IReadOnlyList<CloseTaskDependencyConfigurationDto> Dependencies,
    IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> SignOffRequirements, bool OwnerChanged);

public sealed record ClosePreparationPreviewDto(Guid PreviewId, Guid TemplateId, int TemplateVersion,
    Guid SourceWorkflowId, LedgerBookDto TargetBook, LedgerPeriodDto TargetPeriod,
    ClosePreparationCalendarDto Calendar, IReadOnlyList<ClosePreparationTaskDto> Tasks,
    IReadOnlyList<ClosePreparationIssueDto> Issues, bool PolicyChanged, bool CanCreate,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed record CreatePreparedClosePlanRequestDto(Guid PreviewId, string IdempotencyKey,
    string? Actor = null, OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record PreparedClosePlanResultDto(Guid WorkflowId, Guid TemplateId, int TemplateVersion,
    Guid SourceWorkflowId, Guid TargetLedgerBookId, Guid TargetPeriodId, DateTimeOffset CreatedAtUtc,
    string CreatedBy, bool WasAlreadyCreated, ClosePeriodPlanDto Plan,
    IReadOnlyList<ClosePreparationHistoryDto> History);
