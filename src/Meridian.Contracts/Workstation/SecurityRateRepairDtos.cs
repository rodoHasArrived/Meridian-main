namespace Meridian.Contracts.Workstation;

/// <summary>Real accounting scope retained by a governed rate repair; period IDs are never synthesized.</summary>
public sealed record SecurityRateRepairScopeDto(Guid FundAccountId, Guid LedgerBookId, string FundProfileId, string PeriodId);

/// <summary>Evidence applicability, rather than value magnitude, defines a repair cohort.</summary>
public sealed record SecurityRateRepairCohortDto(string SourceSystem, string SourceField, string SourceSchemaVersion, string RateRole);

public sealed record SecurityRateRepairCandidateDto(
    Guid SecurityId, string DisplayName, string AssetClass, long Version, string SourceSystem,
    string SourceField, string? SourceSchemaVersion, string? Issuer, string? RawValue,
    string? CurrentUnit, IReadOnlyList<string> AllowedUnits, bool Eligible,
    string? BlockerCode, string? BlockerReason, string FixPath)
{
    public string RateRole { get; init; } = "AnnualRate";
}

public sealed record SecurityRateRepairActionDto(string Command, bool Enabled, string? DisabledReason);

public sealed record SecurityRateRepairDiscoveryDto(
    DateTimeOffset AsOfUtc, IReadOnlyList<SecurityRateRepairCandidateDto> Candidates,
    IReadOnlyList<SecurityRateRepairActionDto> Actions)
{
    public int Skip { get; init; }
    public int PageSize { get; init; }
    public int ScannedCount { get; init; }
    public int? NextSkip { get; init; }
    public bool IsComplete { get; init; }
    public string ScopeLabel { get; init; } = "Authenticated deployment Security Master; ordered by display name and security identity.";
    public IReadOnlyList<SecurityRateRepairScanIssueDto> Issues { get; init; } = [];
}

public sealed record SecurityRateRepairScanIssueDto(Guid? SecurityId, string Code, string Message);

public sealed record SecurityRateRepairSelectionDto(Guid SecurityId, long ExpectedVersion);

public sealed record CreateSecurityRateRepairBatchRequest(
    Guid RequestId, SecurityRateRepairScopeDto Scope, SecurityRateRepairCohortDto Cohort,
    IReadOnlyList<SecurityRateRepairSelectionDto> Items, string Unit, DateTimeOffset EffectiveFrom,
    string EvidenceId, string Justification, bool EvidenceAppliesToAllRecords);

public sealed record SecurityRateRepairBatchCommandRequest(
    long ExpectedVersion, string? Reviewer = null, string? Rationale = null);

public sealed record SecurityRateRepairItemDto(
    Guid SecurityId, long BaseVersion, string DisplayName, string AssetClass, string? RawValue,
    string State, Guid? RevisionId, Guid? WorkflowId, string? ImpactReceiptId,
    string? ApprovalReceiptId, string? ErrorCode, string? Error);

public sealed record SecurityRateRepairBatchDto(
    Guid BatchId, long Version, DateTimeOffset CreatedAtUtc, string CreatedBy,
    SecurityRateRepairScopeDto Scope, SecurityRateRepairCohortDto Cohort, string Unit,
    DateTimeOffset EffectiveFrom, string EvidenceId, string EvidenceSha256, string Justification,
    IReadOnlyList<SecurityRateRepairItemDto> Items, IReadOnlyList<SecurityRateRepairActionDto> Actions);
