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
    string? Summary = null,
    LedgerDimensionSetDto? Dimensions = null,
    string? TenantId = null,
    string? CompanyId = null)
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
    bool ImplementationSandboxConfigured = false)
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
    bool ReportPackageDimensionProvenanceCertified = false)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}

public sealed record AccountingProductionCertificationProfileUpsertRequestDto(
    AccountingProductionCertificationProfileDto Profile,
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
    IReadOnlyList<AccountingMigrationRunArtifactDto>? MigrationRunArtifacts = null)
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
}

public sealed record AccountingLedgerBookWorkflowReadinessDto(
    Guid? LedgerBookId,
    bool PostingRulesLedgerBookNativeCertified,
    bool JournalLifecycleLedgerBookNativeCertified,
    bool CloseReportingLedgerBookNativeCertified,
    bool ExternalGlLedgerBookNativeCertified,
    bool ReconciliationLedgerBookNativeCertified = false,
    bool DirectLendingLedgerBookNativeCertified = false,
    bool StrategyLedgerReadLedgerBookNativeCertified = false,
    IReadOnlyList<string>? EvidenceReferences = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public int CompletedControlCount =>
        new[]
        {
            HasLedgerBookScope,
            HasLedgerBookScopedEvidence,
            PostingRulesLedgerBookNativeCertified && HasPostingRulesLedgerBookNativeEvidence,
            JournalLifecycleLedgerBookNativeCertified && HasJournalLifecycleLedgerBookNativeEvidence,
            CloseReportingLedgerBookNativeCertified && HasCloseReportingLedgerBookNativeEvidence,
            ExternalGlLedgerBookNativeCertified && HasExternalGlLedgerBookNativeEvidence,
            ReconciliationLedgerBookNativeCertified && HasReconciliationLedgerBookNativeEvidence,
            DirectLendingLedgerBookNativeCertified && HasDirectLendingLedgerBookNativeEvidence,
            StrategyLedgerReadLedgerBookNativeCertified && HasStrategyLedgerReadLedgerBookNativeEvidence
        }.Count(static control => control);

    public int RequiredControlCount => 9;

    public bool HasLedgerBookScope => LedgerBookId.HasValue;

    public bool HasRetainedEvidence => EvidenceReferences.Count > 0;

    public bool HasLedgerBookScopedEvidence =>
        LedgerBookId.HasValue &&
        EvidenceReferences.Any(reference => IsLedgerBookEvidence(reference, LedgerBookId.Value));

    public bool HasPostingRulesLedgerBookNativeEvidence =>
        HasWorkflowEvidence("posting-rules", "posting-rule", "rules-studio", "posting-candidate");

    public bool HasJournalLifecycleLedgerBookNativeEvidence =>
        HasWorkflowEvidence("journal-lifecycle", "journal-entry", "je-lifecycle", "manual-journal");

    public bool HasCloseReportingLedgerBookNativeEvidence =>
        HasWorkflowEvidence("close-reporting", "close-management", "report-package", "restatement");

    public bool HasExternalGlLedgerBookNativeEvidence =>
        HasWorkflowEvidence("external-gl", "external-ledger", "gl-export", "gl-import");

    public bool HasReconciliationLedgerBookNativeEvidence =>
        HasWorkflowEvidence("reconciliation", "break-queue", "statement-reconciliation", "reconciliation-case");

    public bool HasDirectLendingLedgerBookNativeEvidence =>
        HasWorkflowEvidence("direct-lending", "loan-account", "borrower", "direct-lending-projection");

    public bool HasStrategyLedgerReadLedgerBookNativeEvidence =>
        HasWorkflowEvidence("strategy-ledger", "strategy-run", "run-ledger", "strategy-ledger-read");

    private static bool IsLedgerBookEvidence(string? reference, Guid ledgerBookId)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var ledgerBookIdText = ledgerBookId.ToString("D");
        return reference.Contains(ledgerBookIdText, StringComparison.OrdinalIgnoreCase) ||
               reference.Contains($"ledger-book:{ledgerBookIdText}", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasWorkflowEvidence(params string[] aliases)
        => LedgerBookId.HasValue &&
           EvidenceReferences.Any(reference =>
               IsLedgerBookEvidence(reference, LedgerBookId.Value) &&
               (reference.Contains("workflow-certification/full", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("production-certification/full", StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(alias => reference.Contains(alias, StringComparison.OrdinalIgnoreCase))));
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
    IReadOnlyList<string>? EvidenceReferences = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public int CompletedControlCount =>
        new[]
        {
            HasLedgerBookScope,
            HasLedgerBookScopedEvidence,
            PeriodReportDimensionQueriesCertified && HasPeriodReportDimensionQueryEvidence,
            CrossPeriodReportDimensionQueriesCertified && HasCrossPeriodReportDimensionQueryEvidence,
            JournalQueryDimensionFiltersCertified && HasJournalQueryDimensionFilterEvidence,
            ExternalExportDimensionMappingCertified && HasExternalExportDimensionMappingEvidence,
            LedgerLineDimensionsPersistedCertified && HasLedgerLineDimensionPersistenceEvidence,
            TrialBalanceDimensionFiltersCertified && HasTrialBalanceDimensionFilterEvidence,
            ReportPackageDimensionProvenanceCertified && HasReportPackageDimensionProvenanceEvidence
        }.Count(static control => control);

    public int RequiredControlCount => 9;

    public bool HasLedgerBookScope => LedgerBookId.HasValue;

    public bool HasRetainedEvidence => EvidenceReferences.Count > 0;

    public bool HasLedgerBookScopedEvidence =>
        LedgerBookId.HasValue &&
        EvidenceReferences.Any(reference => IsLedgerBookEvidence(reference, LedgerBookId.Value));

    public bool HasPeriodReportDimensionQueryEvidence =>
        HasDimensionEvidence("period-report", "period-reports", "trial-balance", "financial-statement", "nav", "investor-package");

    public bool HasCrossPeriodReportDimensionQueryEvidence =>
        HasDimensionEvidence("cross-period", "comparative", "roll-forward");

    public bool HasJournalQueryDimensionFilterEvidence =>
        HasDimensionEvidence("journal-query", "journal-filter", "journal-dimension", "ledger-journal");

    public bool HasExternalExportDimensionMappingEvidence =>
        HasDimensionEvidence("external-export", "export-dimension", "external-gl-mapping", "gl-export");

    public bool HasLedgerLineDimensionPersistenceEvidence =>
        HasDimensionEvidence("ledger-line", "line-dimension", "posted-ledger-line", "journal-line-dimension");

    public bool HasTrialBalanceDimensionFilterEvidence =>
        HasDimensionEvidence("trial-balance-filter", "trial-balance-dimension", "ledger-report-filter");

    public bool HasReportPackageDimensionProvenanceEvidence =>
        HasDimensionEvidence("report-package-provenance", "report-line-provenance", "package-dimension", "nav-package");

    private static bool IsLedgerBookEvidence(string? reference, Guid ledgerBookId)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var ledgerBookIdText = ledgerBookId.ToString("D");
        return reference.Contains(ledgerBookIdText, StringComparison.OrdinalIgnoreCase) ||
               reference.Contains($"ledger-book:{ledgerBookIdText}", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasDimensionEvidence(params string[] aliases)
        => LedgerBookId.HasValue &&
           EvidenceReferences.Any(reference =>
               IsLedgerBookEvidence(reference, LedgerBookId.Value) &&
               (reference.Contains("dimensions/report-query-certification/full", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("dimensions/full", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("production-certification/full", StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(alias => reference.Contains(alias, StringComparison.OrdinalIgnoreCase))));
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
    bool ImplementationSandboxConfigured = false)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

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

    public bool HasRetainedEvidence => EvidenceReferences.Count > 0;

    public bool HasTenantScopeEvidence =>
        HasTenantAdministrationEvidence("tenant-scope", "tenant-storage", "tenant-ledger", "tenant-provider");

    public bool HasAdminRoleProfileEvidence =>
        HasTenantAdministrationEvidence("admin-role", "role-profile", "accounting-admin-role");

    public bool HasScopedAccessPolicyEvidence =>
        HasTenantAdministrationEvidence("scoped-access", "access-policy", "entitlement");

    public bool HasReportingGroupEvidence =>
        HasTenantAdministrationEvidence("reporting-group", "report-group", "delivery-group");

    public bool HasAccountingAdminSurfaceEvidence =>
        HasTenantAdministrationEvidence("accounting-admin-surface", "operator-surface", "admin-studio", "setup-surface");

    public bool HasBrowserAccountingAdminSurfaceEvidence =>
        HasTenantAdministrationEvidence("browser-admin-studio", "browser-accounting-admin", "browser-setup");

    public bool HasWpfAccountingAdminSurfaceEvidence =>
        HasTenantAdministrationEvidence("wpf-admin-studio", "desktop-accounting-admin", "wpf-setup");

    public bool HasChartAdministrationStudioEvidence =>
        HasTenantAdministrationEvidence("chart-admin", "chart-administration", "chart-of-accounts", "ledger-book-chart");

    public bool HasRuleTestPromotionStudioEvidence =>
        HasTenantAdministrationEvidence("rule-test-promotion", "rules-studio", "rule-tests", "promotion-queue");

    public bool HasCloseSetupStudioEvidence =>
        HasTenantAdministrationEvidence("close-setup", "close-checklist", "close-calendar", "materiality-policy");

    public bool HasProviderMappingStudioEvidence =>
        HasTenantAdministrationEvidence("provider-mapping", "external-gl-mapping", "gl-mapping", "mapping-profile");

    public bool HasTenantCompanyReportGroupSetupStudioEvidence =>
        HasTenantAdministrationEvidence("tenant-company-report-group", "tenant-company-setup", "report-group-setup", "company-report-group");

    public bool HasAuditReviewToolingEvidence =>
        HasTenantAdministrationEvidence("audit-review", "audit-tooling", "audit-workbench", "evidence-review");

    public bool HasBulkImportExportSafeguardsEvidence =>
        HasTenantAdministrationEvidence("bulk-import-export", "bulk-import", "bulk-export", "import-export-safeguard");

    public bool HasPerformanceValidationEvidence =>
        HasTenantAdministrationEvidence("performance-validation", "performance-test", "load-test", "capacity-validation");

    public bool HasDisasterRecoveryRunbookEvidence =>
        HasTenantAdministrationEvidence("disaster-recovery", "dr-runbook", "operating-runbook", "recovery-validation");

    public bool HasLedgerBookAdministrationStudioEvidence =>
        HasTenantAdministrationEvidence("ledger-book-admin", "ledger-book-administration", "book-administration", "ledger-book-setup");

    public bool HasPostingRuleAuthoringStudioEvidence =>
        HasTenantAdministrationEvidence("posting-rule-authoring", "posting-rule-studio", "rule-authoring", "posting-rule-setup");

    public bool HasApprovalQueueStudioEvidence =>
        HasTenantAdministrationEvidence("approval-queue", "promotion-approval", "je-approval", "configuration-approval");

    public bool HasDimensionMappingStudioEvidence =>
        HasTenantAdministrationEvidence("dimension-mapping", "dimension-map", "external-dimension-mapping", "gl-dimension-mapping");

    public bool HasImplementationSandboxEvidence =>
        HasTenantAdministrationEvidence("implementation-sandbox", "sandbox-validation", "fixture-validation", "implementation-fixture");

    private bool HasTenantAdministrationEvidence(params string[] aliases)
        => EvidenceReferences.Any(reference =>
            reference.Contains("tenant-admin", StringComparison.OrdinalIgnoreCase) &&
            (reference.Contains("setup-certified", StringComparison.OrdinalIgnoreCase) ||
             reference.Contains("tenant-administration/full", StringComparison.OrdinalIgnoreCase) ||
             reference.Contains("tenant-admin/full", StringComparison.OrdinalIgnoreCase) ||
             aliases.Any(alias => reference.Contains(alias, StringComparison.OrdinalIgnoreCase))));
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
    IReadOnlyList<string>? BlockingIssueCodes = null)
{
    public IReadOnlyList<string> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];

    public IReadOnlyList<string> BlockingIssueCodes { get; init; } =
        BlockingIssueCodes ?? [];
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
    AccountingTenantAdministrationReadinessDto? TenantAdministration = null)
{
    public IReadOnlyList<AccountingProductionReadinessComponentDto> Components { get; init; } =
        Components ?? [];

    public IReadOnlyList<AccountingProductionReadinessIssueDto> Issues { get; init; } =
        Issues ?? [];

    public IReadOnlyList<AccountingMigrationRunArtifactDto> MigrationRunArtifacts { get; init; } =
        MigrationRunArtifacts ?? [];

    public IReadOnlyList<AccountingMigrationRolloutPlanItemDto> MigrationRolloutPlan { get; init; } =
        MigrationRolloutPlan ?? [];

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
    IReadOnlyList<string>? EvidenceLinks = null,
    string? TenantId = null,
    string? CompanyId = null)
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
    string? CompanyId = null)
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
