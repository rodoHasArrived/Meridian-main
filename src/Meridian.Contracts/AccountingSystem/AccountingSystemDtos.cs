using System.Text.Json.Serialization;
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
    AccountingSystemConnectionMetadataDto? Connection = null);

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
    string? Summary = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingMigrationRunArtifactListDto(
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    IReadOnlyList<AccountingMigrationRunArtifactDto>? Artifacts = null)
{
    public IReadOnlyList<AccountingMigrationRunArtifactDto> Artifacts { get; init; } =
        Artifacts ?? [];
}

public sealed record AccountingMigrationRunArtifactUpsertRequestDto(
    AccountingMigrationRunArtifactDto Artifact,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

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
    string? CorrelationId = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingTenantAdministrationProfileUpsertRequestDto(
    AccountingTenantAdministrationProfileDto Profile,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
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
    IReadOnlyList<string>? TenantAdministrationEvidenceLinks = null,
    bool LedgerBookMigrationCertified = false,
    bool HistoricalJournalBackfillCertified = false,
    bool DimensionalBackfillCertified = false,
    bool AccountingConfigurationPromotionCertified = false,
    bool CloseReportingEvidenceMigrationCertified = false,
    IReadOnlyList<string>? MigrationEvidenceLinks = null,
    IReadOnlyList<AccountingMigrationRunArtifactDto>? MigrationRunArtifacts = null)
{
    public IReadOnlyList<LedgerBookRequiredScopeDto> RequiredLedgerBookScopes { get; init; } =
        RequiredLedgerBookScopes ?? [];

    public IReadOnlyList<string> TenantAdministrationEvidenceLinks { get; init; } =
        TenantAdministrationEvidenceLinks ?? [];

    public IReadOnlyList<string> MigrationEvidenceLinks { get; init; } =
        MigrationEvidenceLinks ?? [];

    public IReadOnlyList<AccountingMigrationRunArtifactDto> MigrationRunArtifacts { get; init; } =
        MigrationRunArtifacts ?? [];
}

public sealed record AccountingTenantAdministrationReadinessDto(
    string? TenantId,
    string? CompanyId,
    bool TenantScopeConfigured,
    bool AdminRoleProfileConfigured,
    bool ScopedAccessPoliciesConfigured,
    bool ReportingGroupsConfigured,
    bool AccountingAdminSurfaceConfigured,
    IReadOnlyList<string>? EvidenceReferences = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public int CompletedControlCount =>
        new[]
        {
            HasTenantScope,
            HasCompanyScope,
            TenantScopeConfigured,
            AdminRoleProfileConfigured,
            ScopedAccessPoliciesConfigured,
            ReportingGroupsConfigured,
            AccountingAdminSurfaceConfigured
        }.Count(static control => control);

    public int RequiredControlCount => 7;

    public bool HasTenantScope => !string.IsNullOrWhiteSpace(TenantId);

    public bool HasCompanyScope => !string.IsNullOrWhiteSpace(CompanyId);

    public bool HasRetainedEvidence => EvidenceReferences.Count > 0;
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
    int ExternalGlProviderCount = 0,
    int CertifiedExternalGlMappingProfileCount = 0,
    bool ExternalGlLivePostingEnabled = false,
    IReadOnlyList<AccountingMigrationRunArtifactDto>? MigrationRunArtifacts = null,
    AccountingTenantAdministrationReadinessDto? TenantAdministration = null)
{
    public IReadOnlyList<AccountingProductionReadinessComponentDto> Components { get; init; } =
        Components ?? [];

    public IReadOnlyList<AccountingProductionReadinessIssueDto> Issues { get; init; } =
        Issues ?? [];

    public IReadOnlyList<AccountingMigrationRunArtifactDto> MigrationRunArtifacts { get; init; } =
        MigrationRunArtifacts ?? [];

    public int CriticalIssueCount => Issues.Count(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

    public int WarningIssueCount => Issues.Count(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Warning);
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
    bool PersistPreview = true);

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
    IReadOnlyList<string> Warnings);

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
    Guid? LedgerBookId = null)
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
    IReadOnlyList<string>? EvidenceLinks = null)
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
    string? CorrelationId = null)
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
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}
