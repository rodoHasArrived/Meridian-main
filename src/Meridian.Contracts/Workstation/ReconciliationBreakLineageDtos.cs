using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>Source observations do not resolve or approve governed casework.</summary>
public sealed record ReconciliationBreakLineageDto(
    string LineageId,
    string ComparisonScopeId,
    string OccurrenceId,
    int OccurrenceNumber,
    string ObservationState,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset OccurrenceFirstObservedAt,
    DateTimeOffset LastObservedAt,
    string LastObservedRunId,
    DateTimeOffset? ClearedAt = null,
    string? ClearedByRunId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdentityScopeId { get; init; }
}

/// <summary>Exact authority and source population covered by a completed comparison.</summary>
public sealed record ReconciliationRunObservationScope(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    string FundAccountId,
    Guid LedgerBookId,
    string AccountingPeriodId,
    string SourceSystem,
    string ExternalAccountId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LineageSourceIdentity { get; init; }
}

public sealed record ReconciliationBreakObservation(string BreakId, string SourceSubjectId);

public sealed record ReconciliationCompletedRunObservation(
    string RunId,
    ReconciliationRunObservationScope Scope,
    DateTimeOffset ObservedAt,
    bool Succeeded,
    bool CompletePopulation,
    IReadOnlyList<ReconciliationBreakObservation> Breaks);
