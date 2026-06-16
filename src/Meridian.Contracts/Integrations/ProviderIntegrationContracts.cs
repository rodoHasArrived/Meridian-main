using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Integrations;

[JsonConverter(typeof(JsonStringEnumConverter<IntegrationTypeDto>))]
public enum IntegrationTypeDto
{
    Rest = 0,
    OpenApiRest = 1,
    GraphQl = 2,
    Webhook = 3,
    SftpFile = 4,
    ManualUpload = 5,
    Hybrid = 6,
    StreamingTemplate = 7,
    CertifiedTradingAdapter = 8
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderCapabilityKindDto>))]
public enum ProviderCapabilityKindDto
{
    Accounts = 0,
    Balances = 1,
    Positions = 2,
    Holdings = 3,
    Transactions = 4,
    TaxLots = 5,
    SecurityReferenceData = 6,
    MarketPrices = 7,
    CorporateActions = 8,
    Documents = 9,
    Alerts = 10,
    Events = 11,
    OrderPreview = 12,
    OrderPlacement = 13,
    OrderCancellation = 14,
    OrderStatus = 15,
    Executions = 16
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationActivationStateDto>))]
public enum ProviderIntegrationActivationStateDto
{
    Draft = 0,
    Tested = 1,
    DryRunPassed = 2,
    PendingApproval = 3,
    Active = 4,
    Paused = 5,
    Failed = 6,
    Retired = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationProcessingStatusDto>))]
public enum ProviderIntegrationProcessingStatusDto
{
    Received = 0,
    Parsed = 1,
    Mapped = 2,
    Validated = 3,
    Quarantined = 4,
    Loaded = 5,
    Published = 6,
    Blocked = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationAuthTypeDto>))]
public enum ProviderIntegrationAuthTypeDto
{
    None = 0,
    ApiKey = 1,
    BearerToken = 2,
    OAuth2 = 3,
    ClientCredentials = 4,
    Basic = 5,
    Certificate = 6,
    CustomHeader = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationHttpMethodDto>))]
public enum ProviderIntegrationHttpMethodDto
{
    Get = 0,
    Post = 1,
    Put = 2,
    Patch = 3,
    Delete = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationPaginationTypeDto>))]
public enum ProviderIntegrationPaginationTypeDto
{
    None = 0,
    PageNumber = 1,
    Offset = 2,
    Cursor = 3,
    NextUrl = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationCursorTypeDto>))]
public enum ProviderIntegrationCursorTypeDto
{
    None = 0,
    Timestamp = 1,
    Date = 2,
    CursorToken = 3,
    PageNumber = 4,
    Offset = 5,
    Watermark = 6,
    FullSnapshot = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderMappingConfidenceDto>))]
public enum ProviderMappingConfidenceDto
{
    Low = 0,
    Medium = 1,
    High = 2,
    Approved = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationIssueSeverityDto>))]
public enum ProviderIntegrationIssueSeverityDto
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderIntegrationQuarantineResolutionActionDto>))]
public enum ProviderIntegrationQuarantineResolutionActionDto
{
    ReviewOnly = 0,
    ReplayAfterMappingChange = 1,
    IgnoreProviderRecord = 2,
    MarkAsCashPosition = 3
}

public sealed record ProviderIntegrationAuthConfigDto(
    ProviderIntegrationAuthTypeDto Type,
    string? TokenUrl,
    IReadOnlyList<string> Scopes,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ProviderCapabilityDto(
    ProviderCapabilityKindDto Capability,
    bool Enabled,
    bool RequiresCertifiedAdapter,
    IReadOnlyList<string> RequiredCanonicalFields);

public sealed record EndpointDependencyDto(
    string EndpointKey,
    string OutputPath,
    string ParameterName);

public sealed record EndpointPaginationDto(
    ProviderIntegrationPaginationTypeDto Type,
    string? CursorPath,
    string? CursorParam,
    string? NextUrlPath,
    int? PageSize);

public sealed record EndpointResponseShapeDto(
    string RecordsPath,
    string? SchemaFingerprint,
    IReadOnlyList<string> RequiredPaths);

public sealed record EndpointDefinitionDto(
    string EndpointKey,
    ProviderCapabilityKindDto Capability,
    ProviderIntegrationHttpMethodDto Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query,
    string? RequestBodyTemplate,
    EndpointDependencyDto? DependsOn,
    EndpointPaginationDto Pagination,
    EndpointResponseShapeDto Response);

public sealed record TransformRuleDto(
    string Type,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record FieldMappingDto(
    ProviderCapabilityKindDto Capability,
    string SourcePath,
    string TargetField,
    TransformRuleDto? Transform,
    bool Required,
    ProviderMappingConfidenceDto Confidence,
    string? DefaultValue,
    string? ConstantValue);

public sealed record ValidationRuleDto(
    ProviderCapabilityKindDto Capability,
    string RuleCode,
    ProviderIntegrationIssueSeverityDto Severity,
    string Message,
    IReadOnlyList<string> TargetFields);

public sealed record SyncScheduleDto(
    string Mode,
    string Frequency,
    string? Time,
    string Timezone,
    ProviderIntegrationCursorTypeDto CursorType,
    string? CursorField,
    string? FullRefreshFrequency);

public sealed record ProviderIntegrationActivationPolicyDto(
    bool RequiresAuthenticationTest,
    bool RequiresEndpointTest,
    bool RequiresDryRun,
    bool RequiresApproval,
    bool ProductionWriteCapabilitiesAllowed,
    IReadOnlyList<string> RequiredIssueCodes);

public sealed record ProviderIntegrationActivationIssueDto(
    string Code,
    ProviderIntegrationIssueSeverityDto Severity,
    string Message,
    ProviderCapabilityKindDto? Capability,
    string? SuggestedFix);

public sealed record ProviderIntegrationActivationReadinessDto(
    bool IsReady,
    IReadOnlyList<ProviderIntegrationActivationIssueDto> Issues,
    IReadOnlyList<string> RequiredEvidence);

public sealed record ProviderIntegrationTemplateCatalogEntryDto(
    string ManifestId,
    string ProviderId,
    string DisplayName,
    IntegrationTypeDto IntegrationType,
    IReadOnlyList<ProviderCapabilityKindDto> Capabilities,
    string Summary,
    bool RequiresCredentials);

public sealed record ProviderIntegrationManifestDto(
    string ManifestId,
    int ManifestVersion,
    string ProviderId,
    string DisplayName,
    IntegrationTypeDto IntegrationType,
    string Environment,
    ProviderIntegrationAuthConfigDto Auth,
    IReadOnlyList<ProviderCapabilityDto> Capabilities,
    IReadOnlyList<EndpointDefinitionDto> Endpoints,
    IReadOnlyList<FieldMappingDto> FieldMappings,
    SyncScheduleDto Sync,
    IReadOnlyList<ValidationRuleDto> ValidationRules,
    ProviderIntegrationActivationPolicyDto Activation,
    ProviderIntegrationActivationStateDto State,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? ChangeReason);

public sealed record ProviderConnectionDto(
    string ConnectionId,
    string ProviderId,
    string ManifestId,
    string ConnectionName,
    string Environment,
    ProviderIntegrationActivationStateDto State,
    string CredentialSecretRef,
    IReadOnlyList<ProviderCapabilityKindDto> EnabledCapabilities,
    string OwnerUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ApprovalEvidenceId);

public sealed record RawIngestionPayloadDto(
    string PayloadId,
    string ProviderId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    string EndpointKey,
    string SyncRunId,
    DateTimeOffset ReceivedAt,
    IReadOnlyDictionary<string, string> RequestMetadata,
    JsonElement RawPayload,
    string MappingVersion,
    ProviderIntegrationProcessingStatusDto ProcessingStatus);

public sealed record ValidationIssueDto(
    string Code,
    ProviderIntegrationIssueSeverityDto Severity,
    string Message,
    string? TargetField,
    string? SuggestedFix);

public sealed record QuarantinedRecordDto(
    string QuarantineRecordId,
    string SyncRunId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    JsonElement RawRecord,
    JsonElement? MappedRecord,
    IReadOnlyList<ValidationIssueDto> ValidationErrors,
    ProviderIntegrationProcessingStatusDto Status,
    DateTimeOffset CreatedAt);

public sealed record ProviderIntegrationQuarantineIssueGroupDto(
    string IssueCode,
    ProviderIntegrationIssueSeverityDto Severity,
    string? TargetField,
    string Message,
    string? SuggestedFix,
    int RecordCount);

public sealed record ProviderIntegrationQuarantineDecisionDto(
    string DecisionId,
    string SyncRunId,
    string QuarantineRecordId,
    string ConnectionId,
    ProviderIntegrationQuarantineResolutionActionDto Action,
    string ReviewedBy,
    DateTimeOffset ReviewedAt,
    string? Note);

public sealed record ProviderIntegrationQuarantineReviewDto(
    string ConnectionId,
    IReadOnlyList<string> SyncRunIds,
    IReadOnlyList<QuarantinedRecordDto> Records,
    IReadOnlyList<ProviderIntegrationQuarantineIssueGroupDto> IssueGroups,
    IReadOnlyList<ProviderIntegrationQuarantineDecisionDto> Decisions,
    int TotalQuarantinedRecords,
    int CriticalIssueCount,
    int WarningIssueCount);

public sealed record ProviderIntegrationQuarantineResolutionRequestDto(
    string ConnectionId,
    string SyncRunId,
    string QuarantineRecordId,
    ProviderIntegrationQuarantineResolutionActionDto Action,
    string ReviewedBy,
    DateTimeOffset ReviewedAt,
    string? Note);

public sealed record ProviderIntegrationQuarantineResolutionResultDto(
    bool Resolved,
    QuarantinedRecordDto Record,
    ProviderIntegrationQuarantineDecisionDto Decision,
    string? Message);

public sealed record ProviderIntegrationQuarantineReplayRequestDto(
    string ReplaySyncRunId,
    string SourceSyncRunId,
    string ManifestId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    IReadOnlyList<string> QuarantineRecordIds,
    string RequestedBy,
    DateTimeOffset RequestedAt);

public sealed record ProviderIntegrationQuarantineReplayResultDto(
    string ReplaySyncRunId,
    string RawPayloadId,
    ProviderCapabilityKindDto Capability,
    int RecordsReplayed,
    int RecordsAccepted,
    int RecordsRequarantined,
    ProviderIntegrationProcessingStatusDto Status,
    IReadOnlyList<ValidationIssueDto> Issues);

public sealed record IntegrationStagingRecordDto(
    string StagingRecordId,
    string SyncRunId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    string RawPayloadId,
    string? SourceRecordId,
    string DedupeKey,
    JsonElement MappedRecord,
    IReadOnlyList<ValidationIssueDto> ValidationWarnings,
    ProviderIntegrationProcessingStatusDto Status,
    DateTimeOffset CreatedAt);

public sealed record ProviderIntegrationSyncRunDto(
    string SyncRunId,
    string ManifestId,
    string ConnectionId,
    string ProviderId,
    ProviderCapabilityKindDto Capability,
    string EndpointKey,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    ProviderIntegrationProcessingStatusDto Status,
    int RecordsReceived,
    int RecordsAccepted,
    int RecordsQuarantined,
    string? RawPayloadId,
    IReadOnlyList<ValidationIssueDto> Issues);

public sealed record ProviderIntegrationSyncRunEvidenceDto(
    string SyncRunId,
    ProviderCapabilityKindDto Capability,
    string EndpointKey,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    ProviderIntegrationProcessingStatusDto Status,
    int RecordsReceived,
    int RecordsAccepted,
    int RecordsQuarantined,
    int DurableStagingRecordCount,
    int DurableQuarantinedRecordCount,
    int CriticalIssueCount,
    int WarningIssueCount,
    string? RawPayloadId,
    IReadOnlyList<ValidationIssueDto> Issues);

public sealed record ProviderIntegrationConnectionMonitorDto(
    string ConnectionId,
    string ManifestId,
    string ProviderId,
    string DisplayName,
    string ConnectionName,
    string Environment,
    ProviderIntegrationActivationStateDto State,
    IReadOnlyList<ProviderCapabilityKindDto> EnabledCapabilities,
    ProviderIntegrationSyncRunEvidenceDto? LastSyncRun,
    IReadOnlyList<ProviderIntegrationSyncRunEvidenceDto> RecentSyncRuns,
    int RecentRecordsReceived,
    int RecentRecordsAccepted,
    int RecentRecordsQuarantined,
    int DurableStagingRecordCount,
    int DurableQuarantinedRecordCount,
    bool HasCriticalIssues);

public sealed record ProviderIntegrationSyncPlanRequestDto(
    string ConnectionId,
    DateTimeOffset EvaluatedAt);

public sealed record ProviderIntegrationSyncPlanItemDto(
    ProviderCapabilityKindDto Capability,
    string? EndpointKey,
    string ScheduleMode,
    string Frequency,
    string Timezone,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? NextEligibleSyncAt,
    bool IsDue,
    bool IsBlocked,
    string Reason,
    IReadOnlyList<ValidationIssueDto> Issues);

public sealed record ProviderIntegrationSyncPlanDto(
    string ConnectionId,
    string ManifestId,
    string ProviderId,
    string ConnectionName,
    ProviderIntegrationActivationStateDto ConnectionState,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<ProviderIntegrationSyncPlanItemDto> Items,
    int DueCount,
    int BlockedCount);

public sealed record ManualCsvProviderIntegrationDryRunRequestDto(
    string SyncRunId,
    string ManifestId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    string FileName,
    string CsvContent,
    string RequestedBy,
    DateTimeOffset RequestedAt);

public sealed record ProviderIntegrationRestDryRunRequestDto(
    string SyncRunId,
    string ManifestId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    string EndpointKey,
    IReadOnlyDictionary<string, string> PathParameters,
    IReadOnlyDictionary<string, string> QueryParameters,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    int MaxPages);

public sealed record ProviderIntegrationOpenApiImportRequestDto(
    string ManifestId,
    string ProviderId,
    string DisplayName,
    string Environment,
    ProviderIntegrationAuthTypeDto AuthType,
    string? TokenUrl,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<ProviderCapabilityKindDto> Capabilities,
    string OpenApiDocumentJson,
    string ImportedBy,
    DateTimeOffset ImportedAt,
    string? ChangeReason);

public sealed record ProviderIntegrationOpenApiImportResultDto(
    bool Imported,
    ProviderIntegrationManifestDto Manifest,
    ProviderIntegrationActivationReadinessDto Readiness,
    IReadOnlyList<ValidationIssueDto> Issues,
    string? Message);

public sealed record ProviderIntegrationSchemaDriftIssueDto(
    string Code,
    ProviderIntegrationIssueSeverityDto Severity,
    ProviderCapabilityKindDto Capability,
    string EndpointKey,
    string JsonPath,
    string Message,
    string SuggestedFix);

public sealed record ProviderIntegrationSchemaDriftCheckRequestDto(
    string ManifestId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    string EndpointKey,
    string SyncRunId,
    string RawPayloadId,
    string CheckedBy,
    DateTimeOffset CheckedAt);

public sealed record ProviderIntegrationSchemaDriftCheckResultDto(
    string ManifestId,
    string ConnectionId,
    ProviderCapabilityKindDto Capability,
    string EndpointKey,
    string SyncRunId,
    string RawPayloadId,
    bool DriftDetected,
    bool ShouldPauseCapability,
    int RecordsInspected,
    IReadOnlyList<ProviderIntegrationSchemaDriftIssueDto> Issues);

public sealed record ProviderIntegrationDryRunResultDto(
    string SyncRunId,
    string RawPayloadId,
    ProviderCapabilityKindDto Capability,
    int RecordsReceived,
    int RecordsAccepted,
    int RecordsQuarantined,
    ProviderIntegrationProcessingStatusDto Status,
    IReadOnlyList<ValidationIssueDto> Issues);

public sealed record ProviderIntegrationSetupSaveRequestDto(
    ProviderIntegrationManifestDto Manifest,
    ProviderConnectionDto Connection,
    string SavedBy,
    DateTimeOffset SavedAt,
    string? ChangeReason);

public sealed record ProviderIntegrationSetupSaveResultDto(
    bool Saved,
    string ManifestId,
    string ConnectionId,
    ProviderIntegrationActivationStateDto ManifestState,
    ProviderIntegrationActivationStateDto ConnectionState,
    ProviderIntegrationActivationReadinessDto Readiness,
    string? Message);

public sealed record ProviderIntegrationActivationRequestDto(
    string ManifestId,
    string ConnectionId,
    string ApprovedBy,
    DateTimeOffset ApprovedAt,
    string ApprovalEvidenceId,
    string? ChangeReason);

public sealed record ProviderIntegrationActivationResultDto(
    bool Activated,
    string ManifestId,
    string ConnectionId,
    ProviderIntegrationActivationStateDto ManifestState,
    ProviderIntegrationActivationStateDto ConnectionState,
    ProviderIntegrationActivationReadinessDto Readiness,
    string? ApprovalEvidenceId,
    string? Message);

public interface IProviderIntegrationManifestStore
{
    Task SaveManifestAsync(ProviderIntegrationManifestDto manifest, CancellationToken ct = default);

    Task<ProviderIntegrationManifestDto?> GetManifestAsync(string manifestId, CancellationToken ct = default);

    Task<IReadOnlyList<ProviderIntegrationManifestDto>> ListManifestsAsync(CancellationToken ct = default);

    Task SaveConnectionAsync(ProviderConnectionDto connection, CancellationToken ct = default);

    Task<ProviderConnectionDto?> GetConnectionAsync(string connectionId, CancellationToken ct = default);

    Task SaveRawPayloadAsync(RawIngestionPayloadDto payload, CancellationToken ct = default);

    Task<RawIngestionPayloadDto?> GetRawPayloadAsync(string syncRunId, string payloadId, CancellationToken ct = default);

    Task SaveQuarantinedRecordAsync(QuarantinedRecordDto record, CancellationToken ct = default);

    Task<IReadOnlyList<QuarantinedRecordDto>> ListQuarantinedRecordsAsync(string syncRunId, CancellationToken ct = default);

    Task SaveQuarantineDecisionAsync(ProviderIntegrationQuarantineDecisionDto decision, CancellationToken ct = default);

    Task<IReadOnlyList<ProviderIntegrationQuarantineDecisionDto>> ListQuarantineDecisionsAsync(string syncRunId, CancellationToken ct = default);

    Task SaveStagingRecordAsync(IntegrationStagingRecordDto record, CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationStagingRecordDto>> ListStagingRecordsAsync(string syncRunId, CancellationToken ct = default);

    Task SaveSyncRunAsync(ProviderIntegrationSyncRunDto syncRun, CancellationToken ct = default);

    Task<ProviderIntegrationSyncRunDto?> GetSyncRunAsync(string syncRunId, CancellationToken ct = default);

    Task<IReadOnlyList<ProviderIntegrationSyncRunDto>> ListSyncRunsAsync(string connectionId, CancellationToken ct = default);
}

public interface IProviderIntegrationTenantManifestStoreFactory
{
    IProviderIntegrationManifestStore ForTenant(string tenantId);
}
