using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

public sealed record IngestionOperationsSnapshotDto(
    DateTimeOffset GeneratedAt,
    IngestionOperationsSummaryDto Summary,
    IReadOnlyList<IngestionOperationRowDto> Jobs,
    IReadOnlyList<string> Providers);

public sealed record IngestionOperationsSummaryDto(
    int Total,
    int Queued,
    int Running,
    int Paused,
    int Failed,
    int Completed,
    int Cancelled,
    int Resumable);

public sealed record IngestionOperationRowDto(
    string JobId,
    string WorkloadType,
    string State,
    string Provider,
    IReadOnlyList<string> Symbols,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    double ProgressPercent,
    bool IsResumable,
    int AttemptCount,
    int MaxRetries,
    DateTimeOffset? NextRetryAt,
    string? ErrorMessage,
    string? EvidenceRoute,
    IReadOnlyList<IngestionOperationActionDto> Actions);

public sealed record IngestionOperationDetailDto(
    IngestionOperationRowDto Job,
    IngestionCheckpointDto? Checkpoint,
    IReadOnlyList<IngestionSymbolProgressDto> SymbolProgress,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

public sealed record IngestionCheckpointDto(
    string? LastSymbol,
    DateTimeOffset? LastDate,
    long? LastOffset,
    DateTimeOffset? GapFillWindowStart,
    DateTimeOffset CapturedAt);

public sealed record IngestionSymbolProgressDto(
    string Symbol,
    string State,
    long DataPointsProcessed,
    long ExpectedDataPoints,
    double ProgressPercent,
    DateTimeOffset? LastCommittedAt,
    int RetryCount,
    string? ErrorMessage);

public sealed record IngestionOperationActionDto(
    string Action,
    string Label,
    bool Enabled,
    string? DisabledReason);

public sealed record IngestionOperationActionRequestDto(
    string IdempotencyKey,
    string Rationale);

public sealed record IngestionOperationActionResultDto(
    string JobId,
    string Action,
    string PreviousState,
    string CurrentState,
    DateTimeOffset RecordedAt,
    string? EvidenceVaultId,
    string? EvidenceRoute);

public sealed record StorageAssuranceSnapshotDto(
    DateTimeOffset GeneratedAt,
    StorageHealthSummaryDto Health,
    StorageQualitySummaryDto Quality,
    CanonicalizationAssuranceDto Canonicalization,
    StorageCapacitySummaryDto Capacity,
    IReadOnlyList<StorageTierSummaryDto> Tiers,
    IReadOnlyList<StorageQualityAlertDto> Alerts,
    StorageAssurancePermissionsDto Permissions);

public sealed record StorageHealthSummaryDto(
    string Status,
    string RootLabel,
    long TotalBytes,
    int FileCount,
    bool Readable,
    bool Writable,
    int OrphanCount,
    int TemporaryFileCount,
    string? Message);

public sealed record StorageQualitySummaryDto(
    string Status,
    int FilesAnalyzed,
    double AverageScore,
    int LowQualityFileCount,
    IReadOnlyList<string> Recommendations,
    string? Message);

public sealed record CanonicalizationAssuranceDto(
    bool Enabled,
    long Version,
    long EventsTotal,
    long SuccessTotal,
    long SoftFailTotal,
    long HardFailTotal,
    double MatchRatePercent,
    IReadOnlyList<CanonicalizationProviderSummaryDto> Providers);

public sealed record CanonicalizationProviderSummaryDto(
    string Provider,
    long Total,
    long Success,
    long SoftFail,
    long HardFail,
    double MatchRatePercent);

public sealed record StorageCapacitySummaryDto(
    long UsedBytes,
    long AvailableBytes,
    double UsedPercent,
    int? EstimatedDaysRemaining,
    string Status);

public sealed record StorageTierSummaryDto(
    string Tier,
    int FileCount,
    long TotalBytes);

public sealed record StorageQualityAlertDto(
    string AlertId,
    string Severity,
    string Subject,
    string Message,
    DateTimeOffset DetectedAt);

public sealed record StorageAssurancePermissionsDto(
    bool CanView,
    bool CanRunQualityCheck,
    bool CanMigrate,
    bool CanDelete);

[JsonConverter(typeof(JsonStringEnumConverter<StorageMaintenanceActionDto>))]
public enum StorageMaintenanceActionDto : byte
{
    QualityCheck,
    Cleanup,
    TierMigration
}

public sealed record StorageMaintenancePreviewRequestDto(
    StorageMaintenanceActionDto Action,
    string? RelativePath = null,
    string? TargetTier = null);

public sealed record StorageMaintenanceCandidateDto(
    string CandidateId,
    string RelativePath,
    string Kind,
    long SizeBytes,
    DateTimeOffset LastModifiedAt,
    string Fingerprint);

public sealed record StorageMaintenancePreviewDto(
    string PreviewId,
    StorageMaintenanceActionDto Action,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Digest,
    string ConfirmationText,
    long AffectedBytes,
    IReadOnlyList<StorageMaintenanceCandidateDto> Candidates,
    string? RelativePath,
    string? TargetTier,
    IReadOnlyList<string> Warnings);

public sealed record StorageMaintenanceCommandRequestDto(
    string PreviewId,
    string IdempotencyKey,
    string Rationale,
    string ConfirmationText);

public sealed record StorageMaintenanceItemResultDto(
    string CandidateId,
    string RelativePath,
    string Status,
    string? Message);

public sealed record StorageMaintenanceResultDto(
    string RunId,
    StorageMaintenanceActionDto Action,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    long AffectedBytes,
    IReadOnlyList<StorageMaintenanceItemResultDto> Items,
    IReadOnlyList<string> Warnings,
    string? EvidenceVaultId,
    string? EvidenceRoute);
