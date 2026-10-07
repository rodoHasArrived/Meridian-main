namespace Meridian.Contracts.Workstation;

/// <summary>Fixed scope of a bounded, read-only accounting onboarding exercise.</summary>
public sealed record OnboardingScopeDto(
    string TenantId, string CompanyId, string EntityId, string FundProfileId,
    Guid LedgerBookId, IReadOnlyList<string> AccountIds, DateOnly StartDate, DateOnly EndDate);

/// <summary>Owner-defined coverage and review policy, retained on every comparison.</summary>
public sealed record OnboardingCriteriaDto(
    IReadOnlyList<DateOnly> RequiredDates,
    IReadOnlyList<string> RequiredKinds,
    decimal BalanceTolerance, decimal PositionTolerance, decimal NavTolerance,
    decimal MinimumCoveragePercent,
    IReadOnlyList<string> RequiredReviewerIds,
    int MinimumReviewers,
    bool RequireCloseReadiness,
    string ReviewInstructions);

public sealed record CreateOnboardingWorkspaceRequestDto(
    string Name, OnboardingScopeDto Scope, OnboardingCriteriaDto Criteria);

public sealed record UpdateOnboardingCriteriaRequestDto(int ExpectedVersion, OnboardingCriteriaDto Criteria);

/// <summary>Only source identities are accepted from clients; amounts and readiness come from services.</summary>
public sealed record CaptureOnboardingComparisonRequestDto(
    int ExpectedVersion, DateOnly AsOfDate, string ProviderId, string ImportId,
    string MappingProfileId, string MappingVersion, string? Notes = null);

public sealed record OnboardingSourceSelectionDto(
    string ProviderId, string ImportId, DateOnly AsOfDate, string MappingProfileId, string MappingVersion);

public sealed record AssignOnboardingDifferenceRequestDto(
    int ExpectedVersion, string OwnerId, string Notes, IReadOnlyList<string> EvidenceIds);

public sealed record ReviewOnboardingWorkspaceRequestDto(
    int ExpectedVersion, int DataRevision, string Decision, string Notes, IReadOnlyList<string> EvidenceIds);

public sealed record FreezeOnboardingPacketRequestDto(int ExpectedVersion);

/// <summary>Content is copied into the run. A hash alone is not a reproducible source snapshot.</summary>
public sealed record OnboardingSourceSnapshotDto(
    string SnapshotId, string SourceKind, string SourceId, string Version,
    string ContentHash, DateTimeOffset CapturedAtUtc, DateOnly AsOfDate,
    string MappingVersion, string PayloadJson, IReadOnlyList<string> EvidenceIds);

/// <summary>Null means missing, never zero. All kinds retain the selected financial account identity.</summary>
public sealed record OnboardingObservationDto(
    string Kind, string AccountId, string Currency, string? InstrumentId,
    decimal? MeridianAmount, decimal? ExternalAmount,
    IReadOnlyList<string> SnapshotIds, IReadOnlyList<string> EvidenceIds,
    string? MissingReason = null);

public sealed record OnboardingMissingSourceDto(string Code, string SourceKind, string? AccountId, string Message);

/// <summary>Trusted read-only adapter result; this model is never an HTTP write payload.</summary>
public sealed record OnboardingSourceCaptureDto(
    IReadOnlyList<OnboardingSourceSnapshotDto> Snapshots,
    IReadOnlyList<OnboardingObservationDto> Observations,
    IReadOnlyList<OnboardingMissingSourceDto> MissingSources,
    CloseReadinessProjectionDto? CloseReadiness);

public sealed record OnboardingCriteriaRevisionDto(
    int DataRevision, OnboardingCriteriaDto Criteria, string ActorId, DateTimeOffset RecordedAtUtc);

/// <summary>Difference lineage is keyed by kind, account, currency and instrument, never by amount.</summary>
public sealed record OnboardingDifferenceDto(
    string DifferenceKey, string Kind, string AccountId, string Currency, string? InstrumentId,
    decimal? MeridianAmount, decimal? ExternalAmount, decimal? Difference, decimal Tolerance,
    string Status, string OwnerId, string FirstSeenComparisonId, string LastSeenComparisonId,
    IReadOnlyList<string> EvidenceIds, string? MissingReason);

public sealed record OnboardingComparisonDto(
    string ComparisonId, int Sequence, DateOnly AsOfDate, int DataRevision,
    string ActorId, DateTimeOffset CapturedAtUtc, string? Notes,
    string ProviderId, string ImportId, string MappingProfileId, string MappingVersion,
    OnboardingCriteriaDto Criteria, OnboardingSourceCaptureDto Inputs,
    IReadOnlyList<OnboardingDifferenceDto> Differences,
    decimal CoveragePercent, string ContentHash, string? PreviousContentHash,
    string AlgorithmVersion, string? BaselineComparisonId = null);

public sealed record OnboardingDifferenceAssignmentDto(
    string DifferenceKey, string OwnerId, string Notes, IReadOnlyList<string> EvidenceIds,
    string ActorId, DateTimeOffset RecordedAtUtc, int DataRevision);

public sealed record OnboardingReviewDto(
    string ReviewId, int DataRevision, string ReviewerId, string Decision,
    string Notes, IReadOnlyList<string> EvidenceIds, DateTimeOffset RecordedAtUtc);

public sealed record OnboardingPeriodDifferenceDto(
    string ComparisonId, DateOnly AsOfDate, OnboardingDifferenceDto Difference);

public sealed record OnboardingReadinessDto(
    string Status, bool IsReady, int UnresolvedDifferenceCount,
    IReadOnlyList<DateOnly> MissingDates, IReadOnlyList<OnboardingMissingSourceDto> MissingSources,
    IReadOnlyList<string> Blockers, int ApprovedReviewers, int RequiredReviewers,
    IReadOnlyList<OnboardingPeriodDifferenceDto> UnresolvedDifferences);

public sealed record OnboardingPacketContentDto(
    string WorkspaceId, string Name, string OwnerId, OnboardingScopeDto Scope,
    int WorkspaceVersion, int DataRevision, OnboardingCriteriaDto Criteria,
    IReadOnlyList<OnboardingCriteriaRevisionDto> CriteriaHistory,
    IReadOnlyList<OnboardingComparisonDto> Comparisons,
    IReadOnlyList<OnboardingDifferenceDto> CurrentDifferences,
    IReadOnlyList<OnboardingDifferenceAssignmentDto> Assignments,
    IReadOnlyList<OnboardingReviewDto> Reviews, OnboardingReadinessDto Readiness,
    string AuthorityPosture);

/// <summary>A frozen support artifact, never an accounting-authority transition.</summary>
public sealed record OnboardingReadinessPacketDto(
    string PacketId, DateTimeOffset FrozenAtUtc, string FrozenBy,
    OnboardingPacketContentDto Content, string ContentHash, string HashAlgorithm);

public sealed record OnboardingWorkspaceDto(
    string WorkspaceId, string Name, string OwnerId, OnboardingScopeDto Scope,
    int Version, int DataRevision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    OnboardingCriteriaDto Criteria, IReadOnlyList<OnboardingCriteriaRevisionDto> CriteriaHistory,
    IReadOnlyList<OnboardingComparisonDto> Comparisons,
    IReadOnlyList<OnboardingDifferenceDto> CurrentDifferences,
    IReadOnlyList<OnboardingDifferenceAssignmentDto> Assignments,
    IReadOnlyList<OnboardingReviewDto> Reviews,
    IReadOnlyList<OnboardingReadinessPacketDto> Packets,
    OnboardingReadinessDto Readiness, string AuthorityPosture);
