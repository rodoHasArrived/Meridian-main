using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Ledger;

/// <summary>Explicit observation scope. Amounts come only from the canonical posted balance schedule.</summary>
/// <remarks>EntityId is an optional membership assertion. The native bilateral schedule, including its authorized
/// counterpart rows, remains intact; EntityId is not an entity-only filter.</remarks>
public sealed record IntercompanyObligationMonitorRequestDto(
    string? FundProfileId, Guid? LedgerBookId, DateOnly AsOf, int ExpiryHorizonDays = 30, Guid? EntityId = null);

public sealed record IntercompanyObligationFindingDto(
    string Code, string Severity, string Message, Guid? EntityId = null,
    Guid? AgreementId = null, int? AgreementVersion = null, Guid? LedgerBookId = null,
    Guid? JournalEntryId = null, string? DrillThrough = null);

/// <summary>Retained source authority for this observation, separate from readiness or posting authority.</summary>
public sealed record IntercompanyObligationSourceVersionDto(
    string SourceKind, string SourceId, string Version, string ContentHash);

/// <summary>Advisory result only. IsComplete describes observation evidence and never changes close eligibility.</summary>
public sealed record IntercompanyObligationMonitorDto(
    DateOnly AsOf, int ExpiryHorizonDays, DateTimeOffset EvaluatedAtUtc,
    string Status, bool IsComplete, string SourceFingerprint,
    IReadOnlyList<IntercompanyObligationSourceVersionDto> SourceVersions,
    IReadOnlyList<IntercompanyObligationFindingDto> Findings,
    IntercompanyBalanceScheduleDto? BalanceSchedule,
    IReadOnlyList<IntercompanyAgreementDetailDto> Guarantees);

public interface IIntercompanyObligationMonitorService
{
    Task<IntercompanyObligationMonitorDto> EvaluateAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto request, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntercompanyObligationMonitorRequestDto))]
[JsonSerializable(typeof(IntercompanyObligationMonitorDto))]
[JsonSerializable(typeof(IntercompanyObligationFindingDto[]))]
[JsonSerializable(typeof(IntercompanyAgreementDetailDto))]
[JsonSerializable(typeof(IReadOnlyList<IntercompanyObligationSourceVersionDto>))]
public partial class IntercompanyObligationMonitorJsonContext : JsonSerializerContext;
