using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

/// <summary>What a schedule drafts. Loan interest today; further governed accrual kinds join as they are composed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccrualScheduleKindDto>))]
public enum IntercompanyAccrualScheduleKindDto { LoanInterest }

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccrualScheduleStatusDto>))]
public enum IntercompanyAccrualScheduleStatusDto { Proposed, Active, Rejected, Disabled }

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccrualScheduleActionDto>))]
public enum IntercompanyAccrualScheduleActionDto { Propose, Approve, Reject, Disable }

public sealed record IntercompanyAccrualScheduleEventDto(Guid EventId, Guid RequestId, IntercompanyAccrualScheduleActionDto Action,
    string Actor, DateTimeOffset RecordedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

/// <summary>
/// An opt-in, reviewed instruction to draft one book's governed accruals for every completed schedule period that ends
/// on or after its approval. It drafts only: every draft still enters ordinary review, and nothing posts. Retained as
/// append-only versions; disabling stops future runs without touching drafts already created.
/// </summary>
public sealed record IntercompanyAccrualScheduleDto(Guid ScheduleId, string TenantId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompanyId,
    Guid LedgerBookId, IntercompanyAccrualScheduleKindDto Kind, Guid ProfileArrangementId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PrincipalArrangementId,
    long Version, IntercompanyAccrualScheduleStatusDto Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? ActiveFrom,
    IReadOnlyList<IntercompanyAccrualScheduleEventDto> Events);

public sealed record ProposeIntercompanyAccrualScheduleRequest(Guid RequestId, Guid LedgerBookId, IntercompanyAccrualScheduleKindDto Kind,
    Guid ProfileArrangementId, Guid? PrincipalArrangementId, string Reason);

/// <summary>Approves, rejects or disables exactly the version the reviewer saw.</summary>
public sealed record DecideIntercompanyAccrualScheduleRequest(Guid RequestId, IntercompanyAccrualScheduleActionDto Action,
    long ExpectedVersion, string? Reason = null);

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccrualScheduleOutcomeDto>))]
public enum IntercompanyAccrualScheduleOutcomeDto { Due, Drafted, AlreadyDrafted, Refused }

/// <summary>One completed schedule period and what a run did, or would do, with it.</summary>
public sealed record IntercompanyAccrualScheduleWindowDto(DateOnly AccrualFrom, DateOnly AccrualTo, IntercompanyAccrualScheduleOutcomeDto Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DraftId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DraftStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);

/// <summary>Due periods of one schedule as of a date; a run lists what it drafted and why anything was refused.</summary>
public sealed record IntercompanyAccrualScheduleRunDto(Guid ScheduleId, DateOnly AsOf, bool Executed,
    IReadOnlyList<IntercompanyAccrualScheduleWindowDto> Windows);

/// <summary>Append-only, tenant-scoped schedule versions.</summary>
public interface IIntercompanyAccrualScheduleStore
{
    Task<IntercompanyAccrualScheduleDto?> GetAsync(string tenantId, Guid scheduleId, CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ListAsync(string tenantId, Guid? ledgerBookId = null, CancellationToken ct = default);
    /// <summary>Every tenant's active schedules, for the background runner only.</summary>
    Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ListActiveAsync(CancellationToken ct = default);
    Task<IntercompanyAccrualScheduleDto> AppendAsync(IntercompanyAccrualScheduleDto next, long expectedVersion, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IntercompanyAccrualScheduleDto))]
[JsonSerializable(typeof(IntercompanyAccrualScheduleDto[]))]
[JsonSerializable(typeof(IReadOnlyList<IntercompanyAccrualScheduleDto>))]
[JsonSerializable(typeof(ProposeIntercompanyAccrualScheduleRequest))]
[JsonSerializable(typeof(DecideIntercompanyAccrualScheduleRequest))]
[JsonSerializable(typeof(IntercompanyAccrualScheduleRunDto))]
public sealed partial class IntercompanyAccrualScheduleJsonContext : JsonSerializerContext;
