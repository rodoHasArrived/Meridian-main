using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyRateFixingActionDto>))]
public enum IntercompanyRateFixingActionDto { Propose, Approve, Reject }

/// <summary>One proposed or approved fixing value. Its digest binds rate, source and justification together.</summary>
public sealed record IntercompanyRateFixingValueDto(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal RateFraction,
    string SourceReference, string Justification, string ProposedBy, DateTimeOffset ProposedAt, string ValueDigest);

public sealed record IntercompanyRateFixingEventDto(Guid EventId, Guid RequestId, IntercompanyRateFixingActionDto Action,
    string Actor, DateTimeOffset RecordedAt, string ValueDigest,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

/// <summary>
/// One index observation date in one tenant, retained as append-only versions. Only <see cref="Approved"/> sets
/// accrual rates; a correction is a new pending value that replaces it only when a second person approves it.
/// </summary>
public sealed record IntercompanyRateFixingDto(Guid FixingId, string TenantId, string IndexReference, DateOnly FixingDate,
    long Version, IntercompanyRateFixingValueDto? Approved, IntercompanyRateFixingValueDto? Pending,
    IReadOnlyList<IntercompanyRateFixingEventDto> Events);

/// <summary>Proposes a first value or a correction. <c>ExpectedVersion</c> is 0 for a date with no record yet.</summary>
public sealed record ProposeIntercompanyRateFixingRequest(Guid RequestId, string IndexReference, DateOnly FixingDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal RateFraction,
    string SourceReference, string Justification, long ExpectedVersion);

/// <summary>Approves or rejects exactly the pending value the reviewer saw.</summary>
public sealed record DecideIntercompanyRateFixingRequest(Guid RequestId, IntercompanyRateFixingActionDto Action,
    long ExpectedVersion, string ExpectedValueDigest, string? Reason = null);

/// <summary>An approved value as the accrual calculator receives it.</summary>
public sealed record IntercompanyApprovedRateFixingDto(Guid FixingId, string IndexReference, DateOnly FixingDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal RateFraction,
    string ValueDigest);

/// <summary>Append-only, tenant-scoped retention of fixing versions.</summary>
public interface IIntercompanyRateFixingStore
{
    Task<IntercompanyRateFixingDto?> GetAsync(string tenantId, Guid fixingId, CancellationToken ct = default);
    /// <summary>Current versions for one normalized index over the inclusive fixing-date range.</summary>
    Task<IReadOnlyList<IntercompanyRateFixingDto>> ListAsync(string tenantId, string indexReference, DateOnly from, DateOnly to, CancellationToken ct = default);
    /// <summary>Retains <paramref name="next"/> only when the current version still equals <paramref name="expectedVersion"/>.</summary>
    Task<IntercompanyRateFixingDto> AppendAsync(IntercompanyRateFixingDto next, long expectedVersion, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IntercompanyRateFixingDto))]
[JsonSerializable(typeof(IntercompanyRateFixingDto[]))]
[JsonSerializable(typeof(IReadOnlyList<IntercompanyRateFixingDto>))]
[JsonSerializable(typeof(ProposeIntercompanyRateFixingRequest))]
[JsonSerializable(typeof(DecideIntercompanyRateFixingRequest))]
public sealed partial class IntercompanyRateFixingJsonContext : JsonSerializerContext;
