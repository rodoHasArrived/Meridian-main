using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.AccountingSystem;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemProviderStateDto
{
    Available,
    Planned,
    Disabled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemImportStateDto
{
    NotStarted,
    Previewed,
    Imported,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemReconciliationStatusDto
{
    Matched,
    Variance,
    MissingExternal,
    MissingMeridian,
    ReviewRequired
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemEvidencePackageStatusDto
{
    Ready,
    ReviewRequired,
    Missing
}

public sealed record AccountingSystemProviderDto(
    string ProviderId,
    string DisplayName,
    AccountingSystemProviderStateDto State,
    bool RequiresCredentials,
    bool SupportsChartOfAccounts,
    bool SupportsJournalEntries,
    bool SupportsTrialBalance,
    bool SupportsPosting,
    string StatusLabel,
    string StatusDetail,
    IReadOnlyList<string> EvidenceKinds,
    AccountingSystemConnectionMetadataDto? Connection = null,
    IReadOnlyList<AccountingSystemProviderMappingRequirementDto>? MappingRequirements = null)
{
    public IReadOnlyList<AccountingSystemProviderMappingRequirementDto> MappingRequirements { get; init; } =
        MappingRequirements ?? [];
}

public sealed record AccountingSystemProviderMappingRequirementDto(
    string RequirementId,
    string Label,
    string RequiredEvidenceKind,
    string RequiredAction,
    bool RequiredForGuardedExport = true);

public sealed record AccountingSystemConnectionMetadataDto(
    string ProviderId,
    string? Environment,
    string? CompanyId,
    string? CompanyName,
    bool HasLocalConfig,
    bool HasRefreshToken,
    DateTimeOffset? LastConnectedAtUtc,
    string StatusLabel,
    string StatusDetail,
    IReadOnlyList<string> MissingFields);

[JsonConverter(typeof(JsonStringEnumConverter<AccountingProductionReadinessStatusDto>))]
public enum AccountingProductionReadinessStatusDto
{
    Ready = 0,
    ReviewRequired = 1,
    Blocked = 2,
    Unavailable = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingProductionReadinessAreaDto>))]
public enum AccountingProductionReadinessAreaDto
{
    LedgerBooks = 0,
    RulesStudio = 1,
    PostingRules = 2,
    JournalLifecycle = 3,
    DimensionalAccounting = 4,
    ExternalGl = 5,
    CloseReporting = 6,
    TenantAdministration = 7,
    MigrationRollout = 8
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingMigrationRunKindDto>))]
public enum AccountingMigrationRunKindDto
{
    LedgerBookScope = 0,
    HistoricalJournalBackfill = 1,
    DimensionalBackfill = 2,
    AccountingConfigurationPromotion = 3,
    CloseReportingEvidence = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingMigrationRunStatusDto>))]
public enum AccountingMigrationRunStatusDto
{
    Planned = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Certified = 4
}

public sealed record AccountingMigrationRunArtifactDto(
    string RunId,
    AccountingMigrationRunKindDto Kind,
    AccountingMigrationRunStatusDto Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc = null,
    string? Actor = null,
    int MigratedRecordCount = 0,
    int IssueCount = 0,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    string? Summary = null,
    LedgerDimensionSetDto? Dimensions = null,
    string? TenantId = null,
    string? CompanyId = null,
    int? SourceRecordCount = null,
    bool? RowCountReconciled = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingMigrationRunArtifactListDto(
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    IReadOnlyList<AccountingMigrationRunArtifactDto>? Artifacts = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<AccountingMigrationRunArtifactDto> Artifacts { get; init; } =
        Artifacts ?? [];
}

public sealed record AccountingMigrationRunArtifactUpsertRequestDto(
    AccountingMigrationRunArtifactDto Artifact,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingMigrationRunExecutionRequestDto(
    AccountingMigrationRunKindDto Kind,
    string Actor,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    string? RunId = null,
    bool CertifyOnSuccess = false,
    LedgerDimensionSetDto? Dimensions = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    string? TenantId = null,
    string? CompanyId = null,
    int? SourceRecordCount = null,
    int? MigratedRecordCount = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    string? WorkerPlanId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingMigrationRunWorkerPlanDto(
    string PlanId,
    AccountingMigrationRunKindDto Kind,
    string FundProfileId,
    Guid LedgerBookId,
    int SourceRecordCount,
    int MigratedRecordCount,
    LedgerDimensionSetDto? Dimensions = null,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? TenantId = null,
    string? CompanyId = null,
    string? Summary = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingMigrationRunWorkerPlanUpsertRequestDto(
    AccountingMigrationRunWorkerPlanDto Plan,
    string Actor,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record AccountingMigrationRunWorkerPlanListDto(
    string? FundProfileId,
    Guid? LedgerBookId,
    AccountingMigrationRunKindDto? Kind,
    IReadOnlyList<AccountingMigrationRunWorkerPlanDto>? Plans = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<AccountingMigrationRunWorkerPlanDto> Plans { get; init; } =
        Plans ?? [];
}

public sealed record AccountingMigrationRunExecutionIssueDto(
    string Code,
    AccountingConfigurationValidationSeverityDto Severity,
    string Message,
    string SuggestedAction);

public sealed record AccountingMigrationRunExecutionResultDto(
    AccountingMigrationRunArtifactDto Artifact,
    AccountingMigrationRunStatusDto Status,
    bool IsCertified,
    IReadOnlyList<AccountingMigrationRunExecutionIssueDto>? Issues = null,
    IReadOnlyList<string>? EvidenceReferences = null)
{
    public IReadOnlyList<AccountingMigrationRunExecutionIssueDto> Issues { get; init; } =
        Issues ?? [];

    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingApprovalQueueConfigurationDto(
    string QueueId,
    string DisplayName,
    string WorkflowKind,
    string RequiredApprovalRole,
    int RequiredApprovalCount,
    string SegregationPolicy,
    string EvidenceRequirement);

public sealed record AccountingDimensionMappingConfigurationDto(
    string MappingId,
    string DisplayName,
    string ProviderId,
    LedgerDimensionSetDto MeridianDimensions,
    LedgerDimensionSetDto ProviderDimensions,
    string EvidenceRequirement);

public sealed record AccountingTenantAdministrationProfileDto(
    string TenantId,
    string CompanyId,
    bool TenantScopeConfigured,
    bool AdminRoleProfileConfigured,
    bool ScopedAccessPoliciesConfigured,
    bool ReportingGroupsConfigured,
    bool AccountingAdminSurfaceConfigured,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? CorrelationId = null,
    bool BrowserAccountingAdminSurfaceConfigured = false,
    bool WpfAccountingAdminSurfaceConfigured = false,
    bool ChartAdministrationStudioConfigured = false,
    bool RuleTestPromotionStudioConfigured = false,
    bool CloseSetupStudioConfigured = false,
    bool ProviderMappingStudioConfigured = false,
    bool TenantCompanyReportGroupSetupStudioConfigured = false,
    bool AuditReviewToolingConfigured = false,
    bool BulkImportExportSafeguardsConfigured = false,
    bool PerformanceValidationConfigured = false,
    bool DisasterRecoveryRunbookConfigured = false,
    bool LedgerBookAdministrationStudioConfigured = false,
    bool PostingRuleAuthoringStudioConfigured = false,
    bool ApprovalQueueStudioConfigured = false,
    bool DimensionMappingStudioConfigured = false,
    bool ImplementationSandboxConfigured = false,
    IReadOnlyList<AccountingApprovalQueueConfigurationDto>? ApprovalQueueConfigurations = null,
    IReadOnlyList<AccountingDimensionMappingConfigurationDto>? DimensionMappingConfigurations = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingApprovalQueueConfigurationDto> ApprovalQueueConfigurations { get; init; } =
        ApprovalQueueConfigurations ?? [];

    public IReadOnlyList<AccountingDimensionMappingConfigurationDto> DimensionMappingConfigurations { get; init; } =
        DimensionMappingConfigurations ?? [];
}

public sealed record AccountingTenantAdministrationProfileUpsertRequestDto(
    AccountingTenantAdministrationProfileDto Profile,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public enum AccountingCertificationArtifactStatusDto
{
    Draft,
    Certified,
    Rejected,
    Superseded
}

public enum AccountingCertificationArtifactLaneStatusDto
{
    NotTested,
    Passed,
    Warning,
    Failed
}

public enum AccountingWorkflowCertificationLaneKindDto
{
    PostingRules,
    JournalLifecycle,
    CloseReporting,
    ClosePlanConfiguration,
    ExternalGl,
    Reconciliation,
    DirectLendingProjection,
    StrategyLedgerReads
}

public enum AccountingDimensionalCertificationLaneKindDto
{
    LedgerLinePersistence,
    TrialBalanceFilters,
    PeriodReports,
    CrossPeriodReports,
    JournalFilters,
    ReportPackageProvenance,
    ExternalExportMappings
}

public enum AccountingTenantAdminCertificationLaneKindDto
{
    TenantScope,
    AdminRoleProfile,
    ScopedAccessPolicies,
    ReportingGroups,
    AccountingAdminSurface,
    BrowserAccountingAdminSurface,
    WpfAccountingAdminSurface,
    ChartAdministrationStudio,
    RuleTestPromotionStudio,
    CloseSetupStudio,
    ProviderMappingStudio,
    TenantCompanyReportGroupSetupStudio,
    AuditReviewTooling,
    BulkImportExportSafeguards,
    PerformanceValidation,
    DisasterRecoveryRunbook,
    LedgerBookAdministrationStudio,
    PostingRuleAuthoringStudio,
    ApprovalQueueStudio,
    DimensionMappingStudio,
    ImplementationSandbox
}

public sealed record AccountingCertificationArtifactIssueDto(
    string Code,
    AccountingConfigurationValidationSeverityDto Severity,
    string Message,
    string SuggestedAction,
    IReadOnlyList<string>? EvidenceReferences = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingWorkflowCertificationLaneDto(
    AccountingWorkflowCertificationLaneKindDto Kind,
    AccountingCertificationArtifactLaneStatusDto Status,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<AccountingCertificationArtifactIssueDto>? Issues = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingCertificationArtifactIssueDto> Issues { get; init; } =
        Issues ?? [];
}

public sealed record AccountingDimensionalCertificationLaneDto(
    AccountingDimensionalCertificationLaneKindDto Kind,
    AccountingCertificationArtifactLaneStatusDto Status,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<AccountingCertificationArtifactIssueDto>? Issues = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingCertificationArtifactIssueDto> Issues { get; init; } =
        Issues ?? [];
}

public sealed record AccountingTenantAdminCertificationLaneDto(
    AccountingTenantAdminCertificationLaneKindDto Kind,
    AccountingCertificationArtifactLaneStatusDto Status,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<AccountingCertificationArtifactIssueDto>? Issues = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingCertificationArtifactIssueDto> Issues { get; init; } =
        Issues ?? [];
}

public sealed record AccountingWorkflowCertificationArtifactDto(
    string CertificationId,
    AccountingCertificationArtifactStatusDto Status,
    string? TenantId,
    string? CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string CertifiedBy,
    DateTimeOffset CertifiedAtUtc,
    string SourceService,
    IReadOnlyList<AccountingWorkflowCertificationLaneDto>? Lanes = null,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<AccountingCertificationArtifactIssueDto>? Issues = null,
    string? CorrelationId = null)
{
    public IReadOnlyList<AccountingWorkflowCertificationLaneDto> Lanes { get; init; } =
        Lanes ?? [];

    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingCertificationArtifactIssueDto> Issues { get; init; } =
        Issues ?? [];
}

public sealed record AccountingDimensionalCertificationArtifactDto(
    string CertificationId,
    AccountingCertificationArtifactStatusDto Status,
    string? TenantId,
    string? CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string DimensionScopeEvidenceKey,
    string CertifiedBy,
    DateTimeOffset CertifiedAtUtc,
    string SourceService,
    IReadOnlyList<AccountingDimensionalCertificationLaneDto>? Lanes = null,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<AccountingCertificationArtifactIssueDto>? Issues = null,
    string? CorrelationId = null)
{
    public IReadOnlyList<AccountingDimensionalCertificationLaneDto> Lanes { get; init; } =
        Lanes ?? [];

    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingCertificationArtifactIssueDto> Issues { get; init; } =
        Issues ?? [];
}

public sealed record AccountingTenantAdminCertificationArtifactDto(
    string CertificationId,
    AccountingCertificationArtifactStatusDto Status,
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid? LedgerBookId,
    string CertifiedBy,
    DateTimeOffset CertifiedAtUtc,
    string SourceService,
    IReadOnlyList<AccountingTenantAdminCertificationLaneDto>? Lanes = null,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<AccountingCertificationArtifactIssueDto>? Issues = null,
    string? CorrelationId = null)
{
    public IReadOnlyList<AccountingTenantAdminCertificationLaneDto> Lanes { get; init; } =
        Lanes ?? [];

    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingCertificationArtifactIssueDto> Issues { get; init; } =
        Issues ?? [];
}

/// <summary>
/// Stable subject types used to bind retained evidence identities to the typed accounting
/// certification artifact whose scope and passed lanes the evidence supports.
/// </summary>
public static class AccountingProductionCertificationEvidenceSubjectTypes
{
    public const string WorkflowArtifact = "AccountingWorkflowCertificationArtifact";
    public const string DimensionalArtifact = "AccountingDimensionalCertificationArtifact";
    public const string TenantAdministrationArtifact = "AccountingTenantAdminCertificationArtifact";
}

/// <summary>
/// Fail-closed retained-evidence checks for accounting production certification. Evidence URI
/// text is a locator only; authority comes from an exact subject-type and certification-id bind.
/// </summary>
public static class AccountingProductionCertificationEvidenceValidator
{
    public static bool IsEligible(RetainedEvidenceIdentityDto? evidence)
        => RetainedEvidenceIdentityValidator.IsComplete(evidence) &&
           !IsSynthesized(evidence!) &&
           !IsLegacyFullToken(evidence!.EvidenceUri);

    public static bool IsSynthesized(RetainedEvidenceIdentityDto evidence)
        => evidence.EvidenceUri.Contains("retained-production-profile", StringComparison.OrdinalIgnoreCase) ||
           evidence.SourceReference.Contains("retained-production-profile", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(
               evidence.SourceSystem.Trim(),
               nameof(AccountingProductionReadinessRequestDto),
               StringComparison.OrdinalIgnoreCase) ||
           string.Equals(
               evidence.SourceSystem.Trim(),
               "AccountingProductionReadinessService",
               StringComparison.OrdinalIgnoreCase);

    public static bool IsLegacyFullToken(string? evidenceUri)
        => !string.IsNullOrWhiteSpace(evidenceUri) &&
           (evidenceUri.Contains("production-certification/full", StringComparison.OrdinalIgnoreCase) ||
            evidenceUri.Contains("workflow-certification/full", StringComparison.OrdinalIgnoreCase) ||
            evidenceUri.Contains("dimensions/report-query-certification/full", StringComparison.OrdinalIgnoreCase) ||
            evidenceUri.Contains("dimensions/full", StringComparison.OrdinalIgnoreCase) ||
            evidenceUri.Contains("tenant-admin/full", StringComparison.OrdinalIgnoreCase) ||
            evidenceUri.Contains("tenant-administration/full", StringComparison.OrdinalIgnoreCase));

    public static bool BindsTo(
        RetainedEvidenceIdentityDto? evidence,
        string subjectType,
        string certificationId)
        => IsEligible(evidence) &&
           string.Equals(evidence!.SubjectType.Trim(), subjectType, StringComparison.Ordinal) &&
           string.Equals(evidence.SubjectId.Trim(), certificationId.Trim(), StringComparison.Ordinal);
}

public sealed record AccountingProductionCertificationProfileDto(
    string FundProfileId,
    Guid? LedgerBookId,
    bool PostingRulesLedgerBookNativeCertified,
    bool JournalLifecycleLedgerBookNativeCertified,
    bool CloseReportingLedgerBookNativeCertified,
    bool ExternalGlLedgerBookNativeCertified,
    bool PeriodReportDimensionQueriesCertified,
    bool CrossPeriodReportDimensionQueriesCertified,
    bool JournalQueryDimensionFiltersCertified,
    bool ExternalExportDimensionMappingCertified,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? CorrelationId = null,
    string? TenantId = null,
    string? CompanyId = null,
    bool ReconciliationLedgerBookNativeCertified = false,
    bool DirectLendingLedgerBookNativeCertified = false,
    bool StrategyLedgerReadLedgerBookNativeCertified = false,
    bool LedgerLineDimensionsPersistedCertified = false,
    bool TrialBalanceDimensionFiltersCertified = false,
    bool ReportPackageDimensionProvenanceCertified = false,
    bool ClosePlanConfigurationLedgerBookNativeCertified = false,
    IReadOnlyList<AccountingWorkflowCertificationArtifactDto>? WorkflowCertificationArtifacts = null,
    IReadOnlyList<AccountingDimensionalCertificationArtifactDto>? DimensionalCertificationArtifacts = null,
    IReadOnlyList<AccountingTenantAdminCertificationArtifactDto>? TenantAdminCertificationArtifacts = null,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<AccountingWorkflowCertificationArtifactDto> WorkflowCertificationArtifacts { get; init; } =
        WorkflowCertificationArtifacts ?? [];

    public IReadOnlyList<AccountingDimensionalCertificationArtifactDto> DimensionalCertificationArtifacts { get; init; } =
        DimensionalCertificationArtifacts ?? [];

    public IReadOnlyList<AccountingTenantAdminCertificationArtifactDto> TenantAdminCertificationArtifacts { get; init; } =
        TenantAdminCertificationArtifacts ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } =
        RetainedEvidence ?? [];
}

public sealed record AccountingProductionCertificationProfileUpsertRequestDto(
    AccountingProductionCertificationProfileDto Profile,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } =
        RetainedEvidence ?? [];
}

public sealed record AccountingProductionReadinessRequestDto(
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    AccountingBasisKindDto? AccountingBasis = null,
    string? ProviderId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyList<LedgerBookRequiredScopeDto>? RequiredLedgerBookScopes = null,
    bool TenantScopeConfigured = false,
    bool AdminRoleProfileConfigured = false,
    bool ScopedAccessPoliciesConfigured = false,
    bool ReportingGroupsConfigured = false,
    bool AccountingAdminSurfaceConfigured = false,
    bool BrowserAccountingAdminSurfaceConfigured = false,
    bool WpfAccountingAdminSurfaceConfigured = false,
    bool ChartAdministrationStudioConfigured = false,
    bool RuleTestPromotionStudioConfigured = false,
    bool CloseSetupStudioConfigured = false,
    bool ProviderMappingStudioConfigured = false,
    bool TenantCompanyReportGroupSetupStudioConfigured = false,
    bool AuditReviewToolingConfigured = false,
    bool BulkImportExportSafeguardsConfigured = false,
    bool PerformanceValidationConfigured = false,
    bool DisasterRecoveryRunbookConfigured = false,
    bool LedgerBookAdministrationStudioConfigured = false,
    bool PostingRuleAuthoringStudioConfigured = false,
    bool ApprovalQueueStudioConfigured = false,
    bool DimensionMappingStudioConfigured = false,
    bool ImplementationSandboxConfigured = false,
    IReadOnlyList<string>? TenantAdministrationEvidenceLinks = null,
    bool PostingRulesLedgerBookNativeCertified = false,
    bool JournalLifecycleLedgerBookNativeCertified = false,
    bool CloseReportingLedgerBookNativeCertified = false,
    bool ClosePlanConfigurationLedgerBookNativeCertified = false,
    bool ExternalGlLedgerBookNativeCertified = false,
    bool ReconciliationLedgerBookNativeCertified = false,
    bool DirectLendingLedgerBookNativeCertified = false,
    bool StrategyLedgerReadLedgerBookNativeCertified = false,
    IReadOnlyList<string>? LedgerBookWorkflowEvidenceLinks = null,
    bool PeriodReportDimensionQueriesCertified = false,
    bool CrossPeriodReportDimensionQueriesCertified = false,
    bool JournalQueryDimensionFiltersCertified = false,
    bool ExternalExportDimensionMappingCertified = false,
    bool LedgerLineDimensionsPersistedCertified = false,
    bool TrialBalanceDimensionFiltersCertified = false,
    bool ReportPackageDimensionProvenanceCertified = false,
    IReadOnlyList<string>? DimensionalReportingEvidenceLinks = null,
    bool LedgerBookMigrationCertified = false,
    bool HistoricalJournalBackfillCertified = false,
    bool DimensionalBackfillCertified = false,
    bool AccountingConfigurationPromotionCertified = false,
    bool CloseReportingEvidenceMigrationCertified = false,
    IReadOnlyList<string>? MigrationEvidenceLinks = null,
    IReadOnlyList<AccountingMigrationRunArtifactDto>? MigrationRunArtifacts = null,
    IReadOnlyList<AccountingWorkflowCertificationArtifactDto>? WorkflowCertificationArtifacts = null,
    IReadOnlyList<AccountingDimensionalCertificationArtifactDto>? DimensionalCertificationArtifacts = null,
    IReadOnlyList<AccountingTenantAdminCertificationArtifactDto>? TenantAdminCertificationArtifacts = null,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null)
{
    public IReadOnlyList<LedgerBookRequiredScopeDto> RequiredLedgerBookScopes { get; init; } =
        RequiredLedgerBookScopes ?? [];

    public IReadOnlyList<string> TenantAdministrationEvidenceLinks { get; init; } =
        TenantAdministrationEvidenceLinks ?? [];

    public IReadOnlyList<string> LedgerBookWorkflowEvidenceLinks { get; init; } =
        LedgerBookWorkflowEvidenceLinks ?? [];

    public IReadOnlyList<string> DimensionalReportingEvidenceLinks { get; init; } =
        DimensionalReportingEvidenceLinks ?? [];

    public IReadOnlyList<string> MigrationEvidenceLinks { get; init; } =
        MigrationEvidenceLinks ?? [];

    public IReadOnlyList<AccountingMigrationRunArtifactDto> MigrationRunArtifacts { get; init; } =
        MigrationRunArtifacts ?? [];

    public IReadOnlyList<AccountingWorkflowCertificationArtifactDto> WorkflowCertificationArtifacts { get; init; } =
        WorkflowCertificationArtifacts ?? [];

    public IReadOnlyList<AccountingDimensionalCertificationArtifactDto> DimensionalCertificationArtifacts { get; init; } =
        DimensionalCertificationArtifacts ?? [];

    public IReadOnlyList<AccountingTenantAdminCertificationArtifactDto> TenantAdminCertificationArtifacts { get; init; } =
        TenantAdminCertificationArtifacts ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } =
        RetainedEvidence ?? [];
}

public sealed record AccountingLedgerBookWorkflowReadinessDto(
    Guid? LedgerBookId,
    bool PostingRulesLedgerBookNativeCertified,
    bool JournalLifecycleLedgerBookNativeCertified,
    bool CloseReportingLedgerBookNativeCertified,
    bool ClosePlanConfigurationLedgerBookNativeCertified,
    bool ExternalGlLedgerBookNativeCertified,
    bool ReconciliationLedgerBookNativeCertified = false,
    bool DirectLendingLedgerBookNativeCertified = false,
    bool StrategyLedgerReadLedgerBookNativeCertified = false,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null,
    string? TenantId = null,
    string? CompanyId = null,
    string? FundProfileId = null,
    IReadOnlyList<AccountingWorkflowCertificationArtifactDto>? CertificationArtifacts = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } =
        RetainedEvidence ?? [];

    public IReadOnlyList<AccountingWorkflowCertificationArtifactDto> CertificationArtifacts { get; init; } =
        CertificationArtifacts ?? [];

    public int CompletedControlCount =>
        new[]
        {
            HasLedgerBookScope,
            HasLedgerBookScopedEvidence,
            PostingRulesLedgerBookNativeCertified && HasPostingRulesLedgerBookNativeEvidence,
            JournalLifecycleLedgerBookNativeCertified && HasJournalLifecycleLedgerBookNativeEvidence,
            CloseReportingLedgerBookNativeCertified && HasCloseReportingLedgerBookNativeEvidence,
            ClosePlanConfigurationLedgerBookNativeCertified && HasClosePlanConfigurationLedgerBookNativeEvidence,
            ExternalGlLedgerBookNativeCertified && HasExternalGlLedgerBookNativeEvidence,
            ReconciliationLedgerBookNativeCertified && HasReconciliationLedgerBookNativeEvidence,
            DirectLendingLedgerBookNativeCertified && HasDirectLendingLedgerBookNativeEvidence,
            StrategyLedgerReadLedgerBookNativeCertified && HasStrategyLedgerReadLedgerBookNativeEvidence
        }.Count(static control => control);

    public int RequiredControlCount => 10;

    public bool HasLedgerBookScope => LedgerBookId.HasValue;

    public bool HasRetainedEvidence =>
        RetainedEvidence.Any(AccountingProductionCertificationEvidenceValidator.IsEligible);

    public bool HasLedgerBookScopedEvidence =>
        HasRequiredScope &&
        CertificationArtifacts.Any(artifact =>
            IsScopedCertifiedArtifact(artifact) &&
            RetainedEvidence.Any(evidence =>
                AccountingProductionCertificationEvidenceValidator.BindsTo(
                    evidence,
                    AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
                    artifact.CertificationId)));

    public bool HasPostingRulesLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.PostingRules);

    public bool HasJournalLifecycleLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.JournalLifecycle);

    public bool HasCloseReportingLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.CloseReporting);

    public bool HasClosePlanConfigurationLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.ClosePlanConfiguration);

    public bool HasExternalGlLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.ExternalGl);

    public bool HasReconciliationLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.Reconciliation);

    public bool HasDirectLendingLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.DirectLendingProjection);

    public bool HasStrategyLedgerReadLedgerBookNativeEvidence =>
        HasWorkflowEvidence(AccountingWorkflowCertificationLaneKindDto.StrategyLedgerReads);

    private bool HasWorkflowEvidence(params AccountingWorkflowCertificationLaneKindDto[] lanes)
        => HasRequiredScope &&
           CertificationArtifacts.Any(artifact =>
               IsScopedCertifiedArtifact(artifact) &&
               artifact.Lanes.Any(lane =>
                   lanes.Contains(lane.Kind) &&
                   lane.Status == AccountingCertificationArtifactLaneStatusDto.Passed) &&
               RetainedEvidence.Any(evidence =>
                   AccountingProductionCertificationEvidenceValidator.BindsTo(
                       evidence,
                       AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
                       artifact.CertificationId)));

    private bool HasRequiredScope =>
        LedgerBookId.HasValue &&
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(CompanyId) &&
        !string.IsNullOrWhiteSpace(FundProfileId);

    private bool IsScopedCertifiedArtifact(AccountingWorkflowCertificationArtifactDto artifact)
        => artifact.Status == AccountingCertificationArtifactStatusDto.Certified &&
           !string.IsNullOrWhiteSpace(artifact.CertificationId) &&
           !string.IsNullOrWhiteSpace(artifact.CertifiedBy) &&
           artifact.CertifiedAtUtc != default &&
           artifact.CertifiedAtUtc.Offset == TimeSpan.Zero &&
           !string.IsNullOrWhiteSpace(artifact.SourceService) &&
           artifact.LedgerBookId == LedgerBookId &&
           string.Equals(artifact.TenantId?.Trim(), TenantId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(artifact.CompanyId?.Trim(), CompanyId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(artifact.FundProfileId.Trim(), FundProfileId?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record AccountingDimensionalReportingReadinessDto(
    Guid? LedgerBookId,
    bool PeriodReportDimensionQueriesCertified,
    bool CrossPeriodReportDimensionQueriesCertified,
    bool JournalQueryDimensionFiltersCertified,
    bool ExternalExportDimensionMappingCertified,
    bool LedgerLineDimensionsPersistedCertified = false,
    bool TrialBalanceDimensionFiltersCertified = false,
    bool ReportPackageDimensionProvenanceCertified = false,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null,
    string? TenantId = null,
    string? CompanyId = null,
    string? FundProfileId = null,
    IReadOnlyList<AccountingDimensionalCertificationArtifactDto>? CertificationArtifacts = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } =
        RetainedEvidence ?? [];

    public IReadOnlyList<AccountingDimensionalCertificationArtifactDto> CertificationArtifacts { get; init; } =
        CertificationArtifacts ?? [];

    public int CompletedControlCount =>
        new[]
        {
            HasLedgerBookScope,
            HasLedgerBookScopedEvidence,
            HasExplicitDimensionScopeEvidence,
            PeriodReportDimensionQueriesCertified && HasPeriodReportDimensionQueryEvidence,
            CrossPeriodReportDimensionQueriesCertified && HasCrossPeriodReportDimensionQueryEvidence,
            JournalQueryDimensionFiltersCertified && HasJournalQueryDimensionFilterEvidence,
            ExternalExportDimensionMappingCertified && HasExternalExportDimensionMappingEvidence,
            LedgerLineDimensionsPersistedCertified && HasLedgerLineDimensionPersistenceEvidence,
            TrialBalanceDimensionFiltersCertified && HasTrialBalanceDimensionFilterEvidence,
            ReportPackageDimensionProvenanceCertified && HasReportPackageDimensionProvenanceEvidence
        }.Count(static control => control);

    public int RequiredControlCount => 10;

    public bool HasLedgerBookScope => LedgerBookId.HasValue;

    public bool HasRetainedEvidence =>
        RetainedEvidence.Any(AccountingProductionCertificationEvidenceValidator.IsEligible);

    public bool HasLedgerBookScopedEvidence =>
        HasRequiredScope &&
        CertificationArtifacts.Any(artifact =>
            IsScopedCertifiedArtifact(artifact) &&
            RetainedEvidence.Any(evidence =>
                AccountingProductionCertificationEvidenceValidator.BindsTo(
                    evidence,
                    AccountingProductionCertificationEvidenceSubjectTypes.DimensionalArtifact,
                    artifact.CertificationId)));

    public bool HasExplicitDimensionScopeEvidence =>
        HasLedgerBookScopedEvidence &&
        CertificationArtifacts.Any(artifact =>
            IsScopedCertifiedArtifact(artifact) &&
            !string.IsNullOrWhiteSpace(artifact.DimensionScopeEvidenceKey) &&
            RetainedEvidence.Any(evidence =>
                AccountingProductionCertificationEvidenceValidator.BindsTo(
                    evidence,
                    AccountingProductionCertificationEvidenceSubjectTypes.DimensionalArtifact,
                    artifact.CertificationId)));

    public bool HasPeriodReportDimensionQueryEvidence =>
        HasDimensionEvidence(AccountingDimensionalCertificationLaneKindDto.PeriodReports);

    public bool HasCrossPeriodReportDimensionQueryEvidence =>
        HasDimensionEvidence(AccountingDimensionalCertificationLaneKindDto.CrossPeriodReports);

    public bool HasJournalQueryDimensionFilterEvidence =>
        HasDimensionEvidence(AccountingDimensionalCertificationLaneKindDto.JournalFilters);

    public bool HasExternalExportDimensionMappingEvidence =>
        HasDimensionEvidence(AccountingDimensionalCertificationLaneKindDto.ExternalExportMappings);

    public bool HasLedgerLineDimensionPersistenceEvidence =>
        HasDimensionEvidence(AccountingDimensionalCertificationLaneKindDto.LedgerLinePersistence);

    public bool HasTrialBalanceDimensionFilterEvidence =>
        HasDimensionEvidence(AccountingDimensionalCertificationLaneKindDto.TrialBalanceFilters);

    public bool HasReportPackageDimensionProvenanceEvidence =>
        HasDimensionEvidence(AccountingDimensionalCertificationLaneKindDto.ReportPackageProvenance);

    private bool HasDimensionEvidence(params AccountingDimensionalCertificationLaneKindDto[] lanes)
        => HasRequiredScope &&
           CertificationArtifacts.Any(artifact =>
               IsScopedCertifiedArtifact(artifact) &&
               !string.IsNullOrWhiteSpace(artifact.DimensionScopeEvidenceKey) &&
               artifact.Lanes.Any(lane =>
                   lanes.Contains(lane.Kind) &&
                   lane.Status == AccountingCertificationArtifactLaneStatusDto.Passed) &&
               RetainedEvidence.Any(evidence =>
                   AccountingProductionCertificationEvidenceValidator.BindsTo(
                       evidence,
                       AccountingProductionCertificationEvidenceSubjectTypes.DimensionalArtifact,
                       artifact.CertificationId)));

    private bool HasRequiredScope =>
        LedgerBookId.HasValue &&
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(CompanyId) &&
        !string.IsNullOrWhiteSpace(FundProfileId);

    private bool IsScopedCertifiedArtifact(AccountingDimensionalCertificationArtifactDto artifact)
        => artifact.Status == AccountingCertificationArtifactStatusDto.Certified &&
           !string.IsNullOrWhiteSpace(artifact.CertificationId) &&
           !string.IsNullOrWhiteSpace(artifact.CertifiedBy) &&
           artifact.CertifiedAtUtc != default &&
           artifact.CertifiedAtUtc.Offset == TimeSpan.Zero &&
           !string.IsNullOrWhiteSpace(artifact.SourceService) &&
           !string.IsNullOrWhiteSpace(artifact.DimensionScopeEvidenceKey) &&
           artifact.LedgerBookId == LedgerBookId &&
           string.Equals(artifact.TenantId?.Trim(), TenantId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(artifact.CompanyId?.Trim(), CompanyId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(artifact.FundProfileId.Trim(), FundProfileId?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record AccountingTenantAdministrationReadinessDto(
    string? TenantId,
    string? CompanyId,
    bool TenantScopeConfigured,
    bool AdminRoleProfileConfigured,
    bool ScopedAccessPoliciesConfigured,
    bool ReportingGroupsConfigured,
    bool AccountingAdminSurfaceConfigured,
    bool BrowserAccountingAdminSurfaceConfigured,
    bool WpfAccountingAdminSurfaceConfigured,
    bool ChartAdministrationStudioConfigured,
    bool RuleTestPromotionStudioConfigured,
    bool CloseSetupStudioConfigured,
    bool ProviderMappingStudioConfigured,
    bool TenantCompanyReportGroupSetupStudioConfigured,
    IReadOnlyList<string>? EvidenceReferences = null,
    bool AuditReviewToolingConfigured = false,
    bool BulkImportExportSafeguardsConfigured = false,
    bool PerformanceValidationConfigured = false,
    bool DisasterRecoveryRunbookConfigured = false,
    bool LedgerBookAdministrationStudioConfigured = false,
    bool PostingRuleAuthoringStudioConfigured = false,
    bool ApprovalQueueStudioConfigured = false,
    bool DimensionMappingStudioConfigured = false,
    bool ImplementationSandboxConfigured = false,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    IReadOnlyList<AccountingTenantAdminCertificationArtifactDto>? CertificationArtifacts = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } =
        RetainedEvidence ?? [];

    public IReadOnlyList<AccountingTenantAdminCertificationArtifactDto> CertificationArtifacts { get; init; } =
        CertificationArtifacts ?? [];

    public int CompletedControlCount =>
        new[]
        {
            HasTenantScope,
            HasCompanyScope,
            TenantScopeConfigured && HasTenantScopeEvidence,
            AdminRoleProfileConfigured && HasAdminRoleProfileEvidence,
            ScopedAccessPoliciesConfigured && HasScopedAccessPolicyEvidence,
            ReportingGroupsConfigured && HasReportingGroupEvidence,
            AccountingAdminSurfaceConfigured && HasAccountingAdminSurfaceEvidence,
            BrowserAccountingAdminSurfaceConfigured && HasBrowserAccountingAdminSurfaceEvidence,
            WpfAccountingAdminSurfaceConfigured && HasWpfAccountingAdminSurfaceEvidence,
            ChartAdministrationStudioConfigured && HasChartAdministrationStudioEvidence,
            RuleTestPromotionStudioConfigured && HasRuleTestPromotionStudioEvidence,
            CloseSetupStudioConfigured && HasCloseSetupStudioEvidence,
            ProviderMappingStudioConfigured && HasProviderMappingStudioEvidence,
            TenantCompanyReportGroupSetupStudioConfigured && HasTenantCompanyReportGroupSetupStudioEvidence,
            AuditReviewToolingConfigured && HasAuditReviewToolingEvidence,
            BulkImportExportSafeguardsConfigured && HasBulkImportExportSafeguardsEvidence,
            PerformanceValidationConfigured && HasPerformanceValidationEvidence,
            DisasterRecoveryRunbookConfigured && HasDisasterRecoveryRunbookEvidence,
            LedgerBookAdministrationStudioConfigured && HasLedgerBookAdministrationStudioEvidence,
            PostingRuleAuthoringStudioConfigured && HasPostingRuleAuthoringStudioEvidence,
            ApprovalQueueStudioConfigured && HasApprovalQueueStudioEvidence,
            DimensionMappingStudioConfigured && HasDimensionMappingStudioEvidence,
            ImplementationSandboxConfigured && HasImplementationSandboxEvidence
        }.Count(static control => control);

    public int RequiredControlCount => 23;

    public bool HasTenantScope => !string.IsNullOrWhiteSpace(TenantId);

    public bool HasCompanyScope => !string.IsNullOrWhiteSpace(CompanyId);

    public bool HasRetainedEvidence =>
        RetainedEvidence.Any(AccountingProductionCertificationEvidenceValidator.IsEligible);

    public bool HasTenantCompanyScopedEvidence =>
        HasRequiredScope &&
        CertificationArtifacts.Any(artifact =>
            IsScopedCertifiedArtifact(artifact) &&
            RetainedEvidence.Any(evidence =>
                AccountingProductionCertificationEvidenceValidator.BindsTo(
                    evidence,
                    AccountingProductionCertificationEvidenceSubjectTypes.TenantAdministrationArtifact,
                    artifact.CertificationId)));

    public bool HasTenantScopeEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.TenantScope);

    public bool HasAdminRoleProfileEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.AdminRoleProfile);

    public bool HasScopedAccessPolicyEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.ScopedAccessPolicies);

    public bool HasReportingGroupEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.ReportingGroups);

    public bool HasAccountingAdminSurfaceEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.AccountingAdminSurface);

    public bool HasBrowserAccountingAdminSurfaceEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.BrowserAccountingAdminSurface);

    public bool HasWpfAccountingAdminSurfaceEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.WpfAccountingAdminSurface);

    public bool HasChartAdministrationStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.ChartAdministrationStudio);

    public bool HasRuleTestPromotionStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.RuleTestPromotionStudio);

    public bool HasCloseSetupStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.CloseSetupStudio);

    public bool HasProviderMappingStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.ProviderMappingStudio);

    public bool HasTenantCompanyReportGroupSetupStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.TenantCompanyReportGroupSetupStudio);

    public bool HasAuditReviewToolingEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.AuditReviewTooling);

    public bool HasBulkImportExportSafeguardsEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.BulkImportExportSafeguards);

    public bool HasPerformanceValidationEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.PerformanceValidation);

    public bool HasDisasterRecoveryRunbookEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.DisasterRecoveryRunbook);

    public bool HasLedgerBookAdministrationStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.LedgerBookAdministrationStudio);

    public bool HasPostingRuleAuthoringStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.PostingRuleAuthoringStudio);

    public bool HasApprovalQueueStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.ApprovalQueueStudio);

    public bool HasDimensionMappingStudioEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.DimensionMappingStudio);

    public bool HasImplementationSandboxEvidence =>
        HasTenantAdministrationEvidence(AccountingTenantAdminCertificationLaneKindDto.ImplementationSandbox);

    private bool HasTenantAdministrationEvidence(params AccountingTenantAdminCertificationLaneKindDto[] lanes)
        => HasRequiredScope &&
           CertificationArtifacts.Any(artifact =>
               IsScopedCertifiedArtifact(artifact) &&
               artifact.Lanes.Any(lane =>
                   lanes.Contains(lane.Kind) &&
                   lane.Status == AccountingCertificationArtifactLaneStatusDto.Passed) &&
               RetainedEvidence.Any(evidence =>
                   AccountingProductionCertificationEvidenceValidator.BindsTo(
                       evidence,
                       AccountingProductionCertificationEvidenceSubjectTypes.TenantAdministrationArtifact,
                       artifact.CertificationId)));

    private bool HasRequiredScope =>
        HasTenantScope &&
        HasCompanyScope &&
        !string.IsNullOrWhiteSpace(FundProfileId) &&
        LedgerBookId.HasValue;

    private bool IsScopedCertifiedArtifact(AccountingTenantAdminCertificationArtifactDto artifact)
        => artifact.Status == AccountingCertificationArtifactStatusDto.Certified &&
           !string.IsNullOrWhiteSpace(artifact.CertificationId) &&
           !string.IsNullOrWhiteSpace(artifact.CertifiedBy) &&
           artifact.CertifiedAtUtc != default &&
           artifact.CertifiedAtUtc.Offset == TimeSpan.Zero &&
           !string.IsNullOrWhiteSpace(artifact.SourceService) &&
           artifact.LedgerBookId == LedgerBookId &&
           string.Equals(artifact.TenantId.Trim(), TenantId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(artifact.CompanyId.Trim(), CompanyId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(artifact.FundProfileId.Trim(), FundProfileId?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record AccountingProductionReadinessIssueDto(
    string Code,
    AccountingProductionReadinessAreaDto Area,
    AccountingConfigurationValidationSeverityDto Severity,
    string Message,
    string SuggestedAction,
    IReadOnlyList<string>? EvidenceReferences = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingProductionReadinessComponentDto(
    AccountingProductionReadinessAreaDto Area,
    string Label,
    AccountingProductionReadinessStatusDto Status,
    int Score,
    string Summary,
    IReadOnlyList<AccountingProductionReadinessIssueDto>? Issues = null,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? Route = null)
{
    public IReadOnlyList<AccountingProductionReadinessIssueDto> Issues { get; init; } =
        Issues ?? [];

    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingMigrationRolloutPlanItemDto(
    AccountingMigrationRunKindDto Kind,
    string Code,
    string Label,
    bool Certified,
    AccountingProductionReadinessStatusDto Status,
    string ScopeLabel,
    string RequiredAction,
    string? LatestRunId = null,
    AccountingMigrationRunStatusDto? LatestRunStatus = null,
    int MigratedRecordCount = 0,
    int IssueCount = 0,
    IReadOnlyList<string>? EvidenceReferences = null,
    IReadOnlyList<string>? BlockingIssueCodes = null,
    int Sequence = 0,
    IReadOnlyList<string>? DependencyCodes = null,
    string? ActionRoute = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<string> BlockingIssueCodes { get; init; } =
        BlockingIssueCodes ?? [];

    public IReadOnlyList<string> DependencyCodes { get; init; } =
        DependencyCodes ?? [];
}

public sealed record AccountingProductionGapDto(
    string Code,
    string Label,
    AccountingProductionReadinessStatusDto Status,
    AccountingConfigurationValidationSeverityDto HighestSeverity,
    string Summary,
    string RequiredAction,
    IReadOnlyList<AccountingProductionReadinessAreaDto>? Areas = null,
    IReadOnlyList<string>? BlockingIssueCodes = null,
    IReadOnlyList<string>? Routes = null,
    IReadOnlyList<AccountingProductionReadinessIssueDto>? Issues = null)
{
    public IReadOnlyList<AccountingProductionReadinessAreaDto> Areas { get; init; } =
        Areas ?? [];

    public IReadOnlyList<string> BlockingIssueCodes { get; init; } =
        BlockingIssueCodes ?? [];

    public IReadOnlyList<string> Routes { get; init; } =
        Routes ?? [];

    public IReadOnlyList<AccountingProductionReadinessIssueDto> Issues { get; init; } =
        Issues ?? [];
}

public sealed record AccountingProductionReadinessDto(
    DateTimeOffset GeneratedAtUtc,
    string FundProfileId,
    Guid? LedgerBookId,
    AccountingProductionReadinessStatusDto Status,
    int Score,
    IReadOnlyList<AccountingProductionReadinessComponentDto> Components,
    IReadOnlyList<AccountingProductionReadinessIssueDto> Issues,
    LedgerBookRolloutAssessmentDto? LedgerBookRollout = null,
    AccountingRulesStudioSummaryDto? RulesStudioSummary = null,
    AccountingLedgerBookWorkflowReadinessDto? LedgerBookWorkflows = null,
    AccountingDimensionalReportingReadinessDto? DimensionalReporting = null,
    int ExternalGlProviderCount = 0,
    int CertifiedExternalGlMappingProfileCount = 0,
    bool ExternalGlLivePostingEnabled = false,
    IReadOnlyList<AccountingMigrationRunArtifactDto>? MigrationRunArtifacts = null,
    IReadOnlyList<AccountingMigrationRolloutPlanItemDto>? MigrationRolloutPlan = null,
    AccountingTenantAdministrationReadinessDto? TenantAdministration = null,
    IReadOnlyList<AccountingProductionGapDto>? ProductionGaps = null,
    IReadOnlyList<AccountingWorkflowCertificationArtifactDto>? WorkflowCertificationArtifacts = null,
    IReadOnlyList<AccountingDimensionalCertificationArtifactDto>? DimensionalCertificationArtifacts = null,
    IReadOnlyList<AccountingTenantAdminCertificationArtifactDto>? TenantAdminCertificationArtifacts = null)
{
    public IReadOnlyList<AccountingProductionReadinessComponentDto> Components { get; init; } =
        Components ?? [];

    public IReadOnlyList<AccountingProductionReadinessIssueDto> Issues { get; init; } =
        Issues ?? [];

    public IReadOnlyList<AccountingMigrationRunArtifactDto> MigrationRunArtifacts { get; init; } =
        MigrationRunArtifacts ?? [];

    public IReadOnlyList<AccountingMigrationRolloutPlanItemDto> MigrationRolloutPlan { get; init; } =
        MigrationRolloutPlan ?? [];

    public IReadOnlyList<AccountingProductionGapDto> ProductionGaps { get; init; } =
        ProductionGaps ?? [];

    public IReadOnlyList<AccountingWorkflowCertificationArtifactDto> WorkflowCertificationArtifacts { get; init; } =
        WorkflowCertificationArtifacts ?? [];

    public IReadOnlyList<AccountingDimensionalCertificationArtifactDto> DimensionalCertificationArtifacts { get; init; } =
        DimensionalCertificationArtifacts ?? [];

    public IReadOnlyList<AccountingTenantAdminCertificationArtifactDto> TenantAdminCertificationArtifacts { get; init; } =
        TenantAdminCertificationArtifacts ?? [];

    public int CriticalIssueCount => Issues.Count(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

    public int WarningIssueCount => Issues.Count(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Warning);
}

internal static class AccountingProductionReadinessEvidenceScope
{
    public static bool ReferencesLedgerBook(string? reference, Guid ledgerBookId)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var ledgerBookIdText = ledgerBookId.ToString("D");
        var compactLedgerBookIdText = ledgerBookId.ToString("N");
        return ReferencesScopedValue(reference, "ledger-book:", ledgerBookIdText) ||
               ReferencesScopedValue(reference, "ledger-book/", ledgerBookIdText) ||
               ReferencesScopedValue(reference, "book:", ledgerBookIdText) ||
               ReferencesScopedValue(reference, "ledgerBookId=", ledgerBookIdText) ||
               ReferencesScopedValue(reference, "ledgerBookId:", ledgerBookIdText) ||
               ReferencesScopedValue(reference, "ledgerBookId/", ledgerBookIdText) ||
               ReferencesScopedValue(reference, "ledger-book:", compactLedgerBookIdText) ||
               ReferencesScopedValue(reference, "ledger-book/", compactLedgerBookIdText) ||
               ReferencesScopedValue(reference, "book:", compactLedgerBookIdText) ||
               ReferencesScopedValue(reference, "ledgerBookId=", compactLedgerBookIdText) ||
               ReferencesScopedValue(reference, "ledgerBookId:", compactLedgerBookIdText) ||
               ReferencesScopedValue(reference, "ledgerBookId/", compactLedgerBookIdText);
    }

    private static bool ReferencesScopedValue(string reference, string prefix, string value)
    {
        var searchIndex = 0;
        while (searchIndex < reference.Length)
        {
            var prefixIndex = reference.IndexOf(prefix, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                return false;
            }

            var valueIndex = prefixIndex + prefix.Length;
            if (reference.Length >= valueIndex + value.Length &&
                string.Compare(reference, valueIndex, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                IsEvidenceTokenBoundary(reference, valueIndex + value.Length))
            {
                return true;
            }

            searchIndex = valueIndex;
        }

        return false;
    }

    private static bool IsEvidenceTokenBoundary(string reference, int index)
        => index >= reference.Length ||
           reference[index] is '/' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' or ' ' or '\t' or '\r' or '\n';
}

public sealed record AccountingSystemOAuthStartRequestDto(
    string? ClientId = null,
    string? ClientSecret = null,
    string? RedirectUri = null,
    string? Environment = null,
    string? CompanyName = null,
    string? RequestedBy = null);

public sealed record AccountingSystemOAuthStartResultDto(
    string ProviderId,
    bool Success,
    string? AuthorizationUrl,
    string? State,
    string Environment,
    string RedirectUri,
    string? LastError,
    IReadOnlyList<string> Warnings);

public sealed record AccountingSystemOAuthCallbackResultDto(
    string ProviderId,
    bool Success,
    string? CompanyId,
    string? CompanyName,
    DateTimeOffset CompletedAtUtc,
    string? LastError,
    IReadOnlyList<string> Warnings);

public sealed record AccountingSystemImportRequestDto(
    string? ProviderId = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    bool PersistPreview = true,
    string? TenantId = null,
    string? CompanyId = null);

public sealed record AccountingSystemImportSummaryDto(
    string ImportId,
    string ProviderId,
    string ProviderDisplayName,
    string FundProfileId,
    Guid? LedgerBookId,
    AccountingSystemImportStateDto State,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset ImportedAtUtc,
    int ChartAccountCount,
    int JournalEntryCount,
    int TrialBalanceLineCount,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> Warnings,
    string? TenantId = null,
    string? CompanyId = null,
    string? ContentHash = null);

public sealed record AccountingSystemChartAccountDto(
    string ExternalAccountId,
    string AccountCode,
    string DisplayName,
    string AccountType,
    string Currency,
    bool IsActive,
    string? ParentExternalAccountId = null,
    string? EvidenceRef = null);

public sealed record AccountingSystemJournalEntryDto(
    string ExternalJournalEntryId,
    DateOnly AccountingDate,
    string Description,
    string Currency,
    decimal TotalDebits,
    decimal TotalCredits,
    IReadOnlyList<AccountingSystemJournalLineDto> Lines,
    string? EvidenceRef = null);

public sealed record AccountingSystemJournalLineDto(
    string ExternalLineId,
    string ExternalAccountId,
    string AccountCode,
    string Description,
    decimal Debit,
    decimal Credit,
    string Currency,
    string? EvidenceRef = null);

public sealed record AccountingSystemTrialBalanceLineDto(
    string ExternalAccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal Debit,
    decimal Credit,
    string Currency,
    DateOnly AsOfDate,
    string? EvidenceRef = null);

public sealed record AccountingSystemImportDetailDto(
    AccountingSystemImportSummaryDto Summary,
    IReadOnlyList<AccountingSystemChartAccountDto> ChartAccounts,
    IReadOnlyList<AccountingSystemJournalEntryDto> JournalEntries,
    IReadOnlyList<AccountingSystemTrialBalanceLineDto> TrialBalance);

public sealed record AccountingSystemReconciliationEvidencePackageDto(
    string PackageId,
    string Label,
    AccountingSystemEvidencePackageStatusDto Status,
    int EvidenceReferenceCount,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> RequiredActions);

public sealed record AccountingSystemReconciliationSummaryDto(
    string ReconciliationId,
    string ImportId,
    string ProviderId,
    string FundProfileId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAtUtc,
    int MatchedCount,
    int BreakCount,
    decimal TotalExternalDebits,
    decimal TotalExternalCredits,
    decimal TotalMeridianDebits,
    decimal TotalMeridianCredits,
    bool PostingEnabled,
    string PostingDisabledReason,
    IReadOnlyList<AccountingSystemReconciliationRowDto> Rows,
    IReadOnlyList<string> EvidenceReferences,
    Guid? LedgerBookId = null,
    string? ImportContentHash = null)
{
    public IReadOnlyList<AccountingSystemReconciliationEvidencePackageDto> EvidencePackages { get; init; } = [];
}

public sealed record AccountingSystemReconciliationRowDto(
    string RowId,
    string AccountCode,
    string AccountName,
    string Currency,
    AccountingSystemReconciliationStatusDto Status,
    decimal ExternalDebit,
    decimal ExternalCredit,
    decimal MeridianDebit,
    decimal MeridianCredit,
    decimal Variance,
    string Detail,
    string? EvidenceRef = null)
{
    public IReadOnlyList<string> ExternalEvidenceReferences { get; init; } = [];

    public IReadOnlyList<string> MeridianEvidenceReferences { get; init; } = [];

    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];
}

public sealed record AccountingSystemMappingProfileUpsertRequestDto(
    ExternalGlMappingProfileDto Profile,
    string Actor,
    string? ProviderId = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? TenantId = null,
    string? CompanyId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingSystemExportPackageRequestDto(
    string Actor,
    string? ProviderId = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    string? MappingProfileId = null,
    IReadOnlyList<Guid>? JournalEntryIds = null,
    bool RequireBalancedReconciliation = true,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    string? TenantId = null,
    string? CompanyId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<Guid> JournalEntryIds { get; init; } =
        JournalEntryIds ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CertifyAccountingSystemExportPackageRequestDto(
    string ExportPackageId,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}
