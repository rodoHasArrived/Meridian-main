using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Source attribution for a Security Master passport field value. Operator-authored values carry a
/// retained justification; provider-sourced values originate from ingest/provider evidence.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityMasterEditOrigin>))]
public enum SecurityMasterEditOrigin
{
    Provider = 0,
    Operator = 1
}

/// <summary>
/// Governed-write lifecycle state for a Security Master passport revision:
/// draft → submitted → approved → published (or rejected).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityMasterRevisionStateDto>))]
public enum SecurityMasterRevisionStateDto
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Published = 3,
    Rejected = 4
}

/// <summary>
/// Operator-authored, effective-dated edit to a single passport field. A non-empty
/// <see cref="Justification"/> is required for operator-origin edits — missing provider data stays
/// review-required and is never fabricated as complete.
/// </summary>
public sealed record UpdateSecurityFieldRequest(
    Guid SecurityId,
    long ExpectedVersion,
    string FieldPath,
    string? NewValue,
    DateTimeOffset EffectiveFrom,
    string Actor,
    string Justification,
    string? SourceRecordId = null,
    string? FundProfileId = null,
    string? CorrelationId = null);

/// <summary>
/// Records an operator's resolution of a Security Master source conflict. When
/// <see cref="ChosenWinnerSource"/> diverges from the policy winner, <see cref="AcknowledgePolicyDeviation"/>
/// must be set and the deviation is retained as the audited artifact.
/// </summary>
public sealed record ResolveSourceConflictRequest(
    Guid SecurityId,
    Guid ConflictId,
    long ExpectedVersion,
    string ChosenWinnerSource,
    string Actor,
    string Reason,
    bool AcknowledgePolicyDeviation = false,
    string? CorrelationId = null);

/// <summary>Transitions a draft revision into the governed approval gate.</summary>
public sealed record SubmitSecurityMasterRevisionRequest(
    Guid SecurityId,
    Guid RevisionId,
    string Actor,
    string? Note,
    string? FundProfileId = null);

/// <summary>Publishes an approved revision (durable append + downstream propagation).</summary>
public sealed record PublishSecurityMasterRevisionRequest(
    Guid SecurityId,
    Guid RevisionId,
    string Actor,
    string ApproverActor,
    string? CorrelationId = null);

/// <summary>Result of a field edit / submit transition.</summary>
public sealed record SecurityMasterEditResultDto(
    Guid SecurityId,
    Guid RevisionId,
    long NewVersion,
    SecurityMasterRevisionStateDto State,
    SecurityMasterChangeHistoryItemDto ChangeEntry);

/// <summary>Result of a governed source-conflict resolution, including any policy deviation.</summary>
public sealed record SecurityMasterConflictResolutionDto(
    Guid ConflictId,
    string PolicyWinnerSource,
    string ChosenWinnerSource,
    bool IsPolicyDeviation,
    string Reason,
    long NewVersion);

/// <summary>
/// Result of publishing a revision. When the edit affects a hard-closed period with report-pack
/// exposure, <see cref="RestatementRequired"/> is set and <see cref="RestatementCandidates"/> carries
/// the report packs an operator may restate via the governed report-pack lifecycle.
/// </summary>
public sealed record SecurityMasterPublishResultDto(
    Guid SecurityId,
    Guid RevisionId,
    long NewVersion,
    bool RestatementRequired,
    IReadOnlyList<RestatementCandidateDto> RestatementCandidates,
    IReadOnlyList<string> InvalidatedProjections);

/// <summary>
/// A report pack proposed for restatement because an upstream reference-data edit affected a
/// closed period it consumed. The operator approves the restatement via the existing report-pack path.
/// </summary>
public sealed record RestatementCandidateDto(
    Guid ReportId,
    Guid PriorVersionReportId,
    string PeriodLabel,
    string Summary,
    IReadOnlyList<ReportPackChangedLineDto> ChangedLines);

/// <summary>
/// Deterministic conflict-authority decision: the default winner the policy computes, the rule that
/// selected it, and whether the conflict remains eligible for bulk resolution.
/// </summary>
public sealed record SecurityMasterConflictAuthorityDecision(
    string PolicyWinnerSource,
    string Rule,
    string Rationale,
    bool IsBulkEligible);

/// <summary>
/// Domain event raised after a revision is durably published. Carries the affected ledger books so
/// period-aware propagation can resolve each book's lock status from the authoritative ledger period.
/// </summary>
public sealed record SecurityMasterRevisionPublishedEvent(
    Guid SecurityId,
    Guid RevisionId,
    long Version,
    DateTimeOffset EffectiveFrom,
    IReadOnlyList<string> ChangedFields,
    SecurityMasterDownstreamImpactDto DownstreamImpact,
    IReadOnlyList<Guid> AffectedLedgerBookIds,
    string Actor,
    string? CorrelationId);
