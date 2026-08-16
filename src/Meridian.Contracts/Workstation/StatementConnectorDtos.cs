using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Describes an available custodian/broker statement connector so workstation clients can
/// offer file import and remote fetch entry points.
/// </summary>
public sealed record StatementConnectorDescriptorDto(
    string ConnectorId,
    string DisplayName,
    IReadOnlyList<string> FileExtensions,
    bool SupportsFileImport,
    bool SupportsRemoteFetch,
    bool RequiresMappingProfile,
    string? DefaultProfileId);

/// <summary>Declarative statement mapping profile shared with workstation clients.</summary>
public sealed record StatementMappingProfileDto(
    int SchemaVersion,
    string ProfileId,
    string DisplayName,
    string Format,
    StatementMappingProfileCsvOptionsDto? Csv,
    string? Culture,
    IReadOnlyList<string>? DateFormats,
    IReadOnlyList<StatementMappingProfileFieldDto> Fields,
    IReadOnlyList<StatementMappingProfileActivityCodeDto> ActivityCodes,
    string? LastAcceptedFingerprint,
    bool IsBuiltIn,
    string? Notes);

public sealed record StatementMappingProfileCsvOptionsDto(
    string Delimiter,
    string Quote,
    bool HasHeader);

public sealed record StatementMappingProfileFieldDto(
    string CanonicalField,
    string SourceColumn,
    IReadOnlyList<string>? Aliases,
    bool Required);

public sealed record StatementMappingProfileActivityCodeDto(
    string SourceCode,
    string CanonicalActivityType);

/// <summary>Confidence tier for one detected source column in the import preview.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementColumnConfidenceDto>))]
public enum StatementColumnConfidenceDto : byte
{
    Exact = 0,
    Alias = 1,
    Fuzzy = 2,
    Unmapped = 3
}

public sealed record StatementColumnMappingDto(
    string SourceColumn,
    string? CanonicalField,
    StatementColumnConfidenceDto Confidence,
    decimal Score,
    string Rationale);

public sealed record StatementImportIssueDto(
    string Code,
    string Severity,
    int? RowNumber,
    string? Field,
    string Message);

/// <summary>One canonical record sample shown in the import preview, grouped by kind.</summary>
public sealed record StatementRecordPreviewDto(
    string Kind,
    string Account,
    string Symbol,
    decimal Quantity,
    decimal Price,
    decimal CashAmount,
    string ActivityType,
    string TradeDate,
    string? SettlementDate,
    string? Currency,
    decimal? FeesCommission,
    string? ExternalTransactionId,
    string? ActivityCategory = null,
    string? ActivitySubtype = null,
    string? ProviderActivityCode = null,
    string? RelatedTransactionId = null,
    string? OrderId = null,
    string? Description = null);

/// <summary>Provider-reported margin and restriction evidence shown before import.</summary>
public sealed record StatementAccountSnapshotPreviewDto(
    string ProviderId = default!,
    string AccountId = default!,
    DateTimeOffset AsOf = default,
    string Currency = default!,
    string Status = default!,
    string MarginRegime = default!,
    decimal Cash = default,
    decimal Equity = default,
    decimal BuyingPower = default,
    decimal? InitialMargin = null,
    decimal? MaintenanceMargin = null,
    decimal? ExcessLiquidity = null,
    decimal? MarginLoan = null,
    decimal? Multiplier = null,
    bool TradingBlocked = default,
    bool TransfersBlocked = default,
    bool AccountBlocked = default,
    bool ShortingEnabled = default,
    int? OptionsApprovedLevel = null,
    int? OptionsTradingLevel = null,
    IReadOnlyList<string> Restrictions = default!);

public sealed record StatementActivitySubtypeSummaryDto(
    string Category = default!,
    string Subtype = default!,
    int RecordCount = default);

public sealed record StatementActivityCompletenessDto(
    string? LastEventId = null,
    DateTimeOffset? HighWatermark = null,
    int PageCount = default,
    int SourceRecordCount = default,
    bool IsComplete = default);

/// <summary>
/// Per-kind record breakdown so operators importing a mixed statement see exactly what
/// lands in each lane (positions, transactions, cash balances, fees, dividends).
/// </summary>
public sealed record StatementKindSummaryDto(
    string Kind,
    int RecordCount,
    IReadOnlyList<StatementRecordPreviewDto> SampleRecords);

/// <summary>A catalog profile ranked against the detected columns of an uploaded statement.</summary>
public sealed record StatementProfileSuggestionDto(
    string ProfileId,
    string DisplayName,
    decimal Score);

public sealed record StatementImportPreviewDto(
    string ConnectorId,
    string ConnectorDisplayName,
    string? ProfileId,
    string FileName,
    long FileSizeBytes,
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyList<StatementColumnMappingDto> ColumnMappings,
    int RecordCount,
    IReadOnlyList<StatementKindSummaryDto> KindSummaries,
    IReadOnlyList<StatementImportIssueDto> Issues,
    IReadOnlyList<StatementProfileSuggestionDto> ProfileSuggestions,
    string Status,
    string NextAction = default!)
{
    public IReadOnlyList<StatementAccountSnapshotPreviewDto>? AccountSnapshots { get; init; } = [];
    public IReadOnlyList<StatementActivitySubtypeSummaryDto>? ActivitySubtypeSummaries { get; init; } = [];
    public IReadOnlyList<StatementActivityCompletenessDto>? ActivityCompleteness { get; init; } = [];
    public int? TaxLotCount { get; init; }
    public int? BorrowPositionCount { get; init; }
}

public sealed record StatementImportCommitResultDto(
    string RunId,
    bool Duplicate,
    int RecordCount,
    IReadOnlyList<StatementKindSummaryDto> KindSummaries,
    int BreakCount,
    int CaseCount,
    string RetainedSourcePath,
    string RetainedCanonicalPath,
    string Status,
    string NextAction)
{
    public EvidenceVaultIdentityDto? EvidenceVaultIdentity { get; init; }
    public string? EvidenceWorkbenchRoute { get; init; }
    public string? ReconciliationRoute { get; init; }
    public IReadOnlyList<string> BreakIds { get; init; } = [];
    public IReadOnlyList<string> CaseIds { get; init; } = [];
    public IReadOnlyList<string> ReconciliationCaseRoutes { get; init; } = [];
    public IReadOnlyList<StatementImportReconciliationCaseLinkDto> ReconciliationCaseLinks { get; init; } = [];
    public IReadOnlyList<string> NextActions { get; init; } = [];
    public string? RetainedCanonicalEvidencePath { get; init; }
    public string? StatementReconciliationReportWorkflowId { get; init; }
    public string? StatementReconciliationReportStatusRoute { get; init; }
    public Guid? OperationsWorkflowId { get; init; }
    public StatementReconciliationAccountingScopeDto? AccountingScope { get; init; }
}

/// <summary>
/// Direct reconciliation case handoff returned after statement import so workstation clients can
/// render case actions without relying on parallel route arrays or rebuilding case routes.
/// </summary>
public sealed record StatementImportReconciliationCaseLinkDto(
    string CaseId,
    string? BreakId,
    string Route,
    string Label,
    string Status,
    string Priority,
    string Reason,
    string SuggestedNextAction);

/// <summary>Status of the durable statement reconciliation report workflow.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementReconciliationReportWorkflowStatusDto>))]
public enum StatementReconciliationReportWorkflowStatusDto : byte
{
    InputRetained = 0,
    Importing = 1,
    AwaitingReconciliation = 2,
    RenderingReconciliationReport = 3,
    Completed = 4,
    Failed = 5
}

/// <summary>One immutable, hash-verified artifact produced by the statement reconciliation report workflow.</summary>
public sealed record StatementReconciliationReportArtifactDto(
    string ArtifactId = default!,
    string ArtifactKind = default!,
    string FileName = default!,
    string ContentType = default!,
    long ByteLength = default,
    string ContentHashSha256 = default!,
    string DownloadRoute = default!,
    DateTimeOffset RetainedAtUtc = default);

/// <summary>
/// One superseded artifact generation retained for immutable workflow and audit history.
/// Artifact descriptors preserve the exact metadata that was current for the generation; their
/// original download routes are audit metadata and do not authorize historical download.
/// </summary>
public sealed record StatementReconciliationReportArtifactGenerationDto(
    int Generation = default,
    IReadOnlyList<StatementReconciliationReportArtifactDto> Artifacts = default!,
    string ManifestFileName = default!,
    long ManifestByteLength = default,
    string ManifestContentHashSha256 = default!,
    DateTimeOffset GeneratedAtUtc = default,
    DateTimeOffset ArchivedAtUtc = default,
    IReadOnlyList<string> EvidenceReferences = default!,
    string ArchiveReceiptContentHashSha256 = default!);

/// <summary>
/// Exact accounting authority bound to a statement import before it can enter close casework.
/// </summary>
public sealed record StatementReconciliationAccountingScopeDto(
    string FundProfileId,
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    DateOnly AsOfDate);

/// <summary>
/// Durable workflow projection shared by browser and WPF clients. It exposes retained evidence and
/// recovery routes without exposing server file-system paths.
/// </summary>
public sealed record StatementReconciliationReportWorkflowDto(
    string WorkflowId = default!,
    StatementReconciliationReportWorkflowStatusDto Status = default,
    long Version = default,
    string TenantId = default!,
    string? CompanyId = default,
    string SourceInstitution = default!,
    string FundAccountId = default!,
    string ExternalAccountId = default!,
    DateOnly PeriodStart = default,
    DateOnly PeriodEnd = default,
    string? StatementRunId = default,
    EvidenceVaultIdentityDto? EvidenceVaultIdentity = default,
    IReadOnlyList<StatementReconciliationReportArtifactDto> RetainedArtifacts = default!,
    IReadOnlyList<string> EvidenceReferences = default!,
    int BreakCount = default,
    int CaseCount = default,
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset UpdatedAtUtc = default,
    DateTimeOffset? CompletedAtUtc = default,
    string? FailureReason = default,
    string? RecoveryAction = default,
    string StatusRoute = default!,
    string ResumeRoute = default!)
{
    /// <summary>
    /// Server-verified fund, ledger-book, period, and as-of scope. Null denotes a legacy
    /// reconciliation-only workflow that cannot be used as close or certified-reporting evidence.
    /// </summary>
    public StatementReconciliationAccountingScopeDto? AccountingScope { get; init; }

    /// <summary>
    /// Existing Operations Continuity workflow that owns posting, casework, approval, and close.
    /// The statement operation does not duplicate that lifecycle.
    /// </summary>
    public Guid? OperationsWorkflowId { get; init; }

    /// <summary>
    /// Monotonic number of the latest artifact generation produced by this workflow. When the
    /// workflow is awaiting reconciliation after a reopen, the latest generation is historical and
    /// <see cref="RetainedArtifacts"/> remains empty until a new current generation completes.
    /// </summary>
    public int ArtifactGeneration { get; init; }

    /// <summary>
    /// Immutable metadata, manifest hashes, descriptors, and audit evidence for superseded artifact
    /// generations. Current artifact authority remains exclusively in <see cref="RetainedArtifacts"/>.
    /// </summary>
    public IReadOnlyList<StatementReconciliationReportArtifactGenerationDto> ArtifactHistory { get; init; } = [];
}

/// <summary>
/// Source-compatibility status contract for clients compiled before the operation was renamed.
/// Newly persisted and returned workflows use <see cref="StatementReconciliationReportWorkflowStatusDto"/>.
/// </summary>
[Obsolete("Use StatementReconciliationReportWorkflowStatusDto.")]
[JsonConverter(typeof(JsonStringEnumConverter<StatementToReportWorkflowStatusDto>))]
public enum StatementToReportWorkflowStatusDto : byte
{
    InputRetained = 0,
    Importing = 1,
    AwaitingReconciliation = 2,
    RenderingReport = 3,
    Completed = 4,
    Failed = 5
}

/// <summary>Source-compatibility artifact contract for pre-rename clients.</summary>
[Obsolete("Use StatementReconciliationReportArtifactDto.")]
public sealed record StatementToReportArtifactDto(
    string ArtifactId,
    string ArtifactKind,
    string FileName,
    string ContentType,
    long ByteLength,
    string ContentHashSha256,
    string DownloadRoute,
    DateTimeOffset RetainedAtUtc);

/// <summary>
/// Source-compatibility workflow contract for pre-rename clients. Legacy HTTP routes project this
/// wire shape directly over the canonical statement reconciliation report operation.
/// </summary>
[Obsolete("Use StatementReconciliationReportWorkflowDto.")]
public sealed record StatementToReportWorkflowDto(
    string WorkflowId,
    StatementToReportWorkflowStatusDto Status,
    long Version,
    string TenantId,
    string? CompanyId,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? StatementRunId,
    EvidenceVaultIdentityDto? EvidenceVaultIdentity,
    IReadOnlyList<StatementToReportArtifactDto> RetainedArtifacts,
    IReadOnlyList<string> EvidenceReferences,
    int BreakCount,
    int CaseCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureReason,
    string? RecoveryAction,
    string StatusRoute,
    string ResumeRoute);

/// <summary>A persisted scheduled-fetch configuration for a fetch-capable connector.</summary>
public sealed record StatementFetchScheduleDto(
    string ScheduleId,
    string ConnectorId,
    string ExternalAccountId,
    string FundAccountId,
    string SourceInstitution,
    string? MappingProfileId,
    string ToleranceProfileId,
    int CadenceHours,
    bool Enabled,
    DateTimeOffset? LastRunAtUtc,
    string? LastRunStatus,
    DateTimeOffset? NextDueAtUtc,
    string SourceKind,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    StatementReconciliationAccountingScopeDto? AccountingScope = null);

public sealed record StatementFetchScheduleUpsertRequestDto(
    string? ScheduleId,
    string ConnectorId,
    string ExternalAccountId,
    string FundAccountId,
    string SourceInstitution,
    string? MappingProfileId,
    string? ToleranceProfileId,
    int CadenceHours,
    bool Enabled,
    string? SourceKind = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null);
