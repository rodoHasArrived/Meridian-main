using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Identifies the originating source of structured cash flow projections.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StructuredCashFlowSourceKind>))]
public enum StructuredCashFlowSourceKind
{
    MIAC,
    MoodysAnalytics,
    ClientProvided,
    CalculatedBullet,
    CalculatedSinker
}

/// <summary>
/// Rate-scenario under which cash flows are projected.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StructuredCashFlowScenario>))]
public enum StructuredCashFlowScenario
{
    Base,
    Up100,
    Up200,
    Up300,
    Down100,
    Down200,
    Down300,
    Stress
}

/// <summary>
/// Per-security assignment of the authoritative cash flow source.
/// </summary>
public sealed record SecurityCashFlowSourceDto(
    Guid SecurityId,
    StructuredCashFlowSourceKind SourceKind,
    DateTimeOffset? LastUpdatedUtc,
    bool IsClientOverride,
    string? ClientConfirmedBy,
    DateTimeOffset? ClientConfirmedAt);

/// <summary>
/// A single period in a structured cash flow schedule.
/// </summary>
public sealed record StructuredCashFlowScheduleEntry(
    DateTimeOffset PeriodDate,
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal Factor);

/// <summary>
/// Full projected cash flow schedule for a security under one rate scenario.
/// </summary>
public sealed record StructuredCashFlowProjectionDto(
    Guid SecurityId,
    StructuredCashFlowSourceKind SourceKind,
    StructuredCashFlowScenario Scenario,
    DateTimeOffset AsOf,
    IReadOnlyList<StructuredCashFlowScheduleEntry> Schedule);

/// <summary>
/// Request to assign or update the cash flow source for a security.
/// </summary>
public sealed record UpsertCashFlowSourceRequest(
    Guid SecurityId,
    StructuredCashFlowSourceKind SourceKind,
    bool IsClientOverride,
    string? ClientConfirmedBy,
    string Actor,
    // When true, a non-client update is allowed to replace an existing client-provided source.
    // This is the explicit operator path for clearing a stale or expired client override.
    bool Force = false);
