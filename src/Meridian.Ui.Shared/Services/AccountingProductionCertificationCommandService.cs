using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Exact Evidence Vault subject used for evidence that a reviewer has accepted for one accounting
/// production-certification scope. UI-provided URIs are locators only; they are never authority.
/// </summary>
public static class AccountingProductionCertificationAuthoritySubjects
{
    public const string ScopeEvidence = "AccountingProductionCertificationScope";
    public const string EvidenceVaultSubjectKind = "accounting-production-certification";

    public static string BuildScopeSubjectId(
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid ledgerBookId)
        => string.Join(
            "|",
            Escape(tenantId, nameof(tenantId)),
            Escape(companyId, nameof(companyId)),
            Escape(fundProfileId, nameof(fundProfileId)),
            ledgerBookId == Guid.Empty
                ? throw new ArgumentException("A production-certification scope requires a ledger book id.", nameof(ledgerBookId))
                : ledgerBookId.ToString("D", CultureInfo.InvariantCulture));

    private static string Escape(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A production-certification scope component is required.", parameterName)
            : Uri.EscapeDataString(value.Trim());
}

public sealed record AccountingCertificationEvidenceResolutionRequest(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    IReadOnlyList<string> EvidenceReferences);

public sealed record AccountingCertificationEvidenceSource(
    string AuthorityReference,
    RetainedEvidenceIdentityDto Evidence,
    IReadOnlyList<AccountingWorkflowCertificationLaneKindDto> WorkflowLanes,
    IReadOnlyList<AccountingDimensionalCertificationLaneKindDto> DimensionalLanes,
    IReadOnlyList<AccountingTenantAdminCertificationLaneKindDto> TenantAdministrationLanes);

public interface IAccountingProductionCertificationEvidenceAuthority
{
    Task<IReadOnlyList<AccountingCertificationEvidenceSource>> ResolveAsync(
        AccountingCertificationEvidenceResolutionRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves only hash-verified, tenant/company-scoped Evidence Vault manifests. Supported lanes are
/// taken from reviewer-confirmed document fields, not from client-provided certification artifacts.
/// </summary>
public sealed class EvidenceVaultAccountingProductionCertificationEvidenceAuthority(
    IEvidenceArtifactStore evidenceStore) : IAccountingProductionCertificationEvidenceAuthority
{
    public const string WorkflowLanesField = "accounting.certification.workflow-lanes";
    public const string DimensionalLanesField = "accounting.certification.dimensional-lanes";
    public const string TenantAdministrationLanesField = "accounting.certification.tenant-administration-lanes";

    public async Task<IReadOnlyList<AccountingCertificationEvidenceSource>> ResolveAsync(
        AccountingCertificationEvidenceResolutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = RequireText(request.TenantId, "tenant id");
        var companyId = RequireText(request.CompanyId, "company id");
        var fundProfileId = RequireText(request.FundProfileId, "fund profile id");
        if (request.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Accounting production certification requires a ledger book id.");
        }

        var references = request.EvidenceReferences
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (references.Length == 0)
        {
            throw new ArgumentException("Accounting production certification requires Evidence Vault references.");
        }

        var expectedSubjectId = AccountingProductionCertificationAuthoritySubjects.BuildScopeSubjectId(
            tenantId,
            companyId,
            fundProfileId,
            request.LedgerBookId);
        var sources = new List<AccountingCertificationEvidenceSource>();
        foreach (var reference in references)
        {
            ct.ThrowIfCancellationRequested();
            var vaultId = ParseVaultId(reference)
                ?? throw new ArgumentException(
                    $"Accounting production certification evidence reference '{reference}' is not an Evidence Vault id or route.");
            var identity = await evidenceStore
                .TryGetVaultIdentityAsync(vaultId, tenantId, companyId, ct)
                .ConfigureAwait(false);
            if (identity is null ||
                !string.Equals(
                    identity.SubjectKind,
                    AccountingProductionCertificationAuthoritySubjects.EvidenceVaultSubjectKind,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(identity.SubjectId, expectedSubjectId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Accounting production certification evidence '{vaultId}' is missing or does not match the selected tenant, company, fund profile, and ledger book.");
            }

            var documents = identity.Documents
                .Concat(identity.ManifestSnapshot?.Documents ?? [])
                .DistinctBy(static document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var resolved = documents
                .Select(document => BuildSource(reference, identity, document, expectedSubjectId, tenantId, companyId))
                .OfType<AccountingCertificationEvidenceSource>()
                .ToArray();
            if (resolved.Length == 0)
            {
                throw new ArgumentException(
                    $"Accounting production certification evidence '{vaultId}' has no accepted, reviewer-confirmed certification document.");
            }

            sources.AddRange(resolved);
        }

        return sources
            .GroupBy(static source => source.Evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static source => source.Evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AccountingCertificationEvidenceSource? BuildSource(
        string authorityReference,
        EvidenceVaultIdentityDto identity,
        EvidenceDocumentDto document,
        string expectedSubjectId,
        string tenantId,
        string companyId)
    {
        var review = document.ReviewerState;
        var sourceSystem = document.SourceSystem ?? document.SourceRecord?.SourceSystem;
        var sourceReference = document.SourceReference ?? document.SourceRecord?.SourceReference;
        if (review.Status != EvidenceDocumentReviewStatusDto.Accepted ||
            string.IsNullOrWhiteSpace(review.Reviewer) ||
            !review.ReviewedAt.HasValue ||
            review.ReviewedAt.Value.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(sourceSystem) ||
            string.IsNullOrWhiteSpace(sourceReference) ||
            !string.Equals(document.TenantId?.Trim(), tenantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(document.Scope?.Trim(), companyId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var workflowLanes = ParseEnums<AccountingWorkflowCertificationLaneKindDto>(
            review.ConfirmedFields,
            WorkflowLanesField);
        var dimensionalLanes = ParseEnums<AccountingDimensionalCertificationLaneKindDto>(
            review.ConfirmedFields,
            DimensionalLanesField);
        var tenantAdministrationLanes = ParseEnums<AccountingTenantAdminCertificationLaneKindDto>(
            review.ConfirmedFields,
            TenantAdministrationLanesField);
        if (workflowLanes.Count == 0 && dimensionalLanes.Count == 0 && tenantAdministrationLanes.Count == 0)
        {
            return null;
        }

        var retained = new RetainedEvidenceIdentityDto(
            document.DocumentId,
            $"evidence-vault://{Uri.EscapeDataString(identity.VaultId)}/documents/{Uri.EscapeDataString(document.DocumentId)}",
            document.SourceHashSha256,
            sourceSystem,
            sourceReference,
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            review.Reviewer,
            review.ReviewedAt.Value,
            DateOnly.FromDateTime(document.ReceivedAt.UtcDateTime),
            Math.Max(1, document.AuditTrail.Count),
            identity.RetainedAt.ToUniversalTime(),
            review.Reviewer,
            AccountingProductionCertificationAuthoritySubjects.ScopeEvidence,
            expectedSubjectId);
        return RetainedEvidenceIdentityValidator.IsComplete(retained)
            ? new AccountingCertificationEvidenceSource(
                authorityReference,
                retained,
                workflowLanes,
                dimensionalLanes,
                tenantAdministrationLanes)
            : null;
    }

    private static IReadOnlyList<TEnum> ParseEnums<TEnum>(
        IReadOnlyList<EvidenceDocumentConfirmedFieldDto> fields,
        string fieldName)
        where TEnum : struct, Enum
        => fields
            .Where(field => string.Equals(field.FieldName?.Trim(), fieldName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static field => field.ConfirmedValue.Split(
                [',', ';', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(static value => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
                ? (TEnum?)parsed
                : null)
            .OfType<TEnum>()
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();

    private static string? ParseVaultId(string reference)
    {
        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Scheme, "evidence-vault", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(uri.Host)
                    ? uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    : Uri.UnescapeDataString(uri.Host);
            }

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var vaultIndex = Array.FindIndex(
                segments,
                static segment => string.Equals(segment, "vault", StringComparison.OrdinalIgnoreCase));
            return vaultIndex >= 0 && vaultIndex + 1 < segments.Length
                ? Uri.UnescapeDataString(segments[vaultIndex + 1])
                : null;
        }

        var relativeSegments = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var relativeVaultIndex = Array.FindIndex(
            relativeSegments,
            static segment => string.Equals(segment, "vault", StringComparison.OrdinalIgnoreCase));
        return relativeVaultIndex >= 0 && relativeVaultIndex + 1 < relativeSegments.Length
            ? Uri.UnescapeDataString(relativeSegments[relativeVaultIndex + 1])
            : null;
    }

    private static string RequireText(string value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Accounting production certification {label} is required.")
            : value.Trim();
}

/// <summary>
/// The only shared command that creates certified accounting artifacts. Browser and WPF clients
/// submit desired controls and Evidence Vault locators; all status, lane, actor, time, source, and
/// retained-evidence bindings are generated here from authoritative evidence.
/// </summary>
public sealed class AccountingProductionCertificationCommandService(
    IAccountingProductionCertificationProfileStore profileStore,
    IAccountingProductionCertificationEvidenceAuthority evidenceAuthority,
    IAccountingTenantAdministrationProfileStore? tenantAdministrationProfileStore = null,
    TimeProvider? timeProvider = null)
{
    private const string SourceService = nameof(AccountingProductionCertificationCommandService);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<AccountingProductionCertificationProfileDto> CertifyAsync(
        AccountingProductionCertificationProfileUpsertRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        if (request.ActionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new ArgumentException("Only a human operator can certify accounting production controls.");
        }

        var tenantId = RequireText(request.Profile.TenantId, "tenant id");
        var companyId = RequireText(request.Profile.CompanyId, "company id");
        var fundProfileId = RequireText(request.Profile.FundProfileId, "fund profile id");
        var certifiedBy = RequireText(actor, "actor");
        var ledgerBookId = request.Profile.LedgerBookId is { } bookId && bookId != Guid.Empty
            ? bookId
            : throw new ArgumentException("Accounting production certification requires a ledger book id.");

        var workflowLanes = RequestedWorkflowLanes(request.Profile);
        var dimensionalLanes = RequestedDimensionalLanes(request.Profile);
        var tenantProfile = tenantAdministrationProfileStore is null
            ? null
            : await tenantAdministrationProfileStore
                .GetAsync(tenantId, companyId, ct)
                .ConfigureAwait(false);
        var tenantAdministrationLanes = RequestedTenantAdministrationLanes(tenantProfile);
        if (workflowLanes.Count == 0 && dimensionalLanes.Count == 0 && tenantAdministrationLanes.Count == 0)
        {
            throw new ArgumentException("At least one accounting production control must be selected for certification.");
        }

        var evidenceReferences = request.EvidenceLinks
            .Concat(request.Profile.EvidenceReferences)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = await evidenceAuthority.ResolveAsync(
            new AccountingCertificationEvidenceResolutionRequest(
                tenantId,
                companyId,
                fundProfileId,
                ledgerBookId,
                evidenceReferences),
            ct).ConfigureAwait(false);
        ValidateAuthoritySources(sources, tenantId, companyId, fundProfileId, ledgerBookId);
        EnsureCoverage(workflowLanes, sources.SelectMany(static source => source.WorkflowLanes), "workflow");
        EnsureCoverage(dimensionalLanes, sources.SelectMany(static source => source.DimensionalLanes), "dimensional");
        EnsureCoverage(
            tenantAdministrationLanes,
            sources.SelectMany(static source => source.TenantAdministrationLanes),
            "tenant administration");

        var current = await profileStore
            .GetAsync(tenantId, companyId, fundProfileId, ledgerBookId, ct)
            .ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().ToUniversalTime();
        var workflowArtifact = BuildWorkflowArtifact(
            workflowLanes,
            sources,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            certifiedBy,
            request.CorrelationId,
            current,
            now);
        var dimensionalArtifact = BuildDimensionalArtifact(
            dimensionalLanes,
            sources,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            certifiedBy,
            request.CorrelationId,
            current,
            now);
        var tenantArtifact = BuildTenantAdministrationArtifact(
            tenantAdministrationLanes,
            sources,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            certifiedBy,
            request.CorrelationId,
            current,
            now);
        var retainedEvidence = new List<RetainedEvidenceIdentityDto>();
        BindEvidence(retainedEvidence, sources, workflowArtifact, AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact);
        BindEvidence(retainedEvidence, sources, dimensionalArtifact, AccountingProductionCertificationEvidenceSubjectTypes.DimensionalArtifact);
        BindEvidence(retainedEvidence, sources, tenantArtifact, AccountingProductionCertificationEvidenceSubjectTypes.TenantAdministrationArtifact);

        var artifactsUnchanged = SameArtifact(current?.WorkflowCertificationArtifacts, workflowArtifact) &&
                                 SameArtifact(current?.DimensionalCertificationArtifacts, dimensionalArtifact) &&
                                 SameArtifact(current?.TenantAdminCertificationArtifacts, tenantArtifact);
        var profile = new AccountingProductionCertificationProfileDto(
            fundProfileId,
            ledgerBookId,
            request.Profile.PostingRulesLedgerBookNativeCertified,
            request.Profile.JournalLifecycleLedgerBookNativeCertified,
            request.Profile.CloseReportingLedgerBookNativeCertified,
            request.Profile.ExternalGlLedgerBookNativeCertified,
            request.Profile.PeriodReportDimensionQueriesCertified,
            request.Profile.CrossPeriodReportDimensionQueriesCertified,
            request.Profile.JournalQueryDimensionFiltersCertified,
            request.Profile.ExternalExportDimensionMappingCertified,
            artifactsUnchanged && current is not null ? current.UpdatedAtUtc : now,
            certifiedBy,
            EvidenceReferences: sources.Select(static source => source.Evidence.EvidenceUri)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            CorrelationId: string.IsNullOrWhiteSpace(request.CorrelationId) ? null : request.CorrelationId.Trim(),
            TenantId: tenantId,
            CompanyId: companyId,
            ReconciliationLedgerBookNativeCertified: request.Profile.ReconciliationLedgerBookNativeCertified,
            DirectLendingLedgerBookNativeCertified: request.Profile.DirectLendingLedgerBookNativeCertified,
            StrategyLedgerReadLedgerBookNativeCertified: request.Profile.StrategyLedgerReadLedgerBookNativeCertified,
            LedgerLineDimensionsPersistedCertified: request.Profile.LedgerLineDimensionsPersistedCertified,
            TrialBalanceDimensionFiltersCertified: request.Profile.TrialBalanceDimensionFiltersCertified,
            ReportPackageDimensionProvenanceCertified: request.Profile.ReportPackageDimensionProvenanceCertified,
            ClosePlanConfigurationLedgerBookNativeCertified: request.Profile.ClosePlanConfigurationLedgerBookNativeCertified,
            WorkflowCertificationArtifacts: workflowArtifact is null ? [] : [workflowArtifact],
            DimensionalCertificationArtifacts: dimensionalArtifact is null ? [] : [dimensionalArtifact],
            TenantAdminCertificationArtifacts: tenantArtifact is null ? [] : [tenantArtifact],
            RetainedEvidence: retainedEvidence);

        return await profileStore.UpsertAsync(
            new AccountingProductionCertificationProfileUpsertRequestDto(
                profile,
                certifiedBy,
                request.CorrelationId,
                profile.EvidenceReferences,
                OperationsActionOriginDto.HumanOperator,
                retainedEvidence),
            ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<AccountingWorkflowCertificationLaneKindDto> RequestedWorkflowLanes(
        AccountingProductionCertificationProfileDto profile)
    {
        var lanes = new List<AccountingWorkflowCertificationLaneKindDto>();
        AddIf(lanes, profile.PostingRulesLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.PostingRules);
        AddIf(lanes, profile.JournalLifecycleLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.JournalLifecycle);
        AddIf(lanes, profile.CloseReportingLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.CloseReporting);
        AddIf(lanes, profile.ClosePlanConfigurationLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.ClosePlanConfiguration);
        AddIf(lanes, profile.ExternalGlLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.ExternalGl);
        AddIf(lanes, profile.ReconciliationLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.Reconciliation);
        AddIf(lanes, profile.DirectLendingLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.DirectLendingProjection);
        AddIf(lanes, profile.StrategyLedgerReadLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.StrategyLedgerReads);
        return lanes;
    }

    private static IReadOnlyList<AccountingDimensionalCertificationLaneKindDto> RequestedDimensionalLanes(
        AccountingProductionCertificationProfileDto profile)
    {
        var lanes = new List<AccountingDimensionalCertificationLaneKindDto>();
        AddIf(lanes, profile.LedgerLineDimensionsPersistedCertified, AccountingDimensionalCertificationLaneKindDto.LedgerLinePersistence);
        AddIf(lanes, profile.TrialBalanceDimensionFiltersCertified, AccountingDimensionalCertificationLaneKindDto.TrialBalanceFilters);
        AddIf(lanes, profile.PeriodReportDimensionQueriesCertified, AccountingDimensionalCertificationLaneKindDto.PeriodReports);
        AddIf(lanes, profile.CrossPeriodReportDimensionQueriesCertified, AccountingDimensionalCertificationLaneKindDto.CrossPeriodReports);
        AddIf(lanes, profile.JournalQueryDimensionFiltersCertified, AccountingDimensionalCertificationLaneKindDto.JournalFilters);
        AddIf(lanes, profile.ReportPackageDimensionProvenanceCertified, AccountingDimensionalCertificationLaneKindDto.ReportPackageProvenance);
        AddIf(lanes, profile.ExternalExportDimensionMappingCertified, AccountingDimensionalCertificationLaneKindDto.ExternalExportMappings);
        return lanes;
    }

    private static IReadOnlyList<AccountingTenantAdminCertificationLaneKindDto> RequestedTenantAdministrationLanes(
        AccountingTenantAdministrationProfileDto? profile)
    {
        if (profile is null)
        {
            return [];
        }

        var lanes = new List<AccountingTenantAdminCertificationLaneKindDto>();
        AddIf(lanes, profile.TenantScopeConfigured, AccountingTenantAdminCertificationLaneKindDto.TenantScope);
        AddIf(lanes, profile.AdminRoleProfileConfigured, AccountingTenantAdminCertificationLaneKindDto.AdminRoleProfile);
        AddIf(lanes, profile.ScopedAccessPoliciesConfigured, AccountingTenantAdminCertificationLaneKindDto.ScopedAccessPolicies);
        AddIf(lanes, profile.ReportingGroupsConfigured, AccountingTenantAdminCertificationLaneKindDto.ReportingGroups);
        AddIf(lanes, profile.AccountingAdminSurfaceConfigured, AccountingTenantAdminCertificationLaneKindDto.AccountingAdminSurface);
        AddIf(lanes, profile.BrowserAccountingAdminSurfaceConfigured, AccountingTenantAdminCertificationLaneKindDto.BrowserAccountingAdminSurface);
        AddIf(lanes, profile.WpfAccountingAdminSurfaceConfigured, AccountingTenantAdminCertificationLaneKindDto.WpfAccountingAdminSurface);
        AddIf(lanes, profile.ChartAdministrationStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.ChartAdministrationStudio);
        AddIf(lanes, profile.RuleTestPromotionStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.RuleTestPromotionStudio);
        AddIf(lanes, profile.CloseSetupStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.CloseSetupStudio);
        AddIf(lanes, profile.ProviderMappingStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.ProviderMappingStudio);
        AddIf(lanes, profile.TenantCompanyReportGroupSetupStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.TenantCompanyReportGroupSetupStudio);
        AddIf(lanes, profile.AuditReviewToolingConfigured, AccountingTenantAdminCertificationLaneKindDto.AuditReviewTooling);
        AddIf(lanes, profile.BulkImportExportSafeguardsConfigured, AccountingTenantAdminCertificationLaneKindDto.BulkImportExportSafeguards);
        AddIf(lanes, profile.PerformanceValidationConfigured, AccountingTenantAdminCertificationLaneKindDto.PerformanceValidation);
        AddIf(lanes, profile.DisasterRecoveryRunbookConfigured, AccountingTenantAdminCertificationLaneKindDto.DisasterRecoveryRunbook);
        AddIf(lanes, profile.LedgerBookAdministrationStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.LedgerBookAdministrationStudio);
        AddIf(lanes, profile.PostingRuleAuthoringStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.PostingRuleAuthoringStudio);
        AddIf(lanes, profile.ApprovalQueueStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.ApprovalQueueStudio);
        AddIf(lanes, profile.DimensionMappingStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.DimensionMappingStudio);
        AddIf(lanes, profile.ImplementationSandboxConfigured, AccountingTenantAdminCertificationLaneKindDto.ImplementationSandbox);
        return lanes;
    }

    private static void AddIf<T>(ICollection<T> values, bool include, T value)
    {
        if (include)
        {
            values.Add(value);
        }
    }

    private static AccountingWorkflowCertificationArtifactDto? BuildWorkflowArtifact(
        IReadOnlyList<AccountingWorkflowCertificationLaneKindDto> lanes,
        IReadOnlyList<AccountingCertificationEvidenceSource> sources,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid ledgerBookId,
        string actor,
        string? correlationId,
        AccountingProductionCertificationProfileDto? current,
        DateTimeOffset now)
    {
        if (lanes.Count == 0)
        {
            return null;
        }

        var id = BuildCertificationId("workflow", tenantId, companyId, fundProfileId, ledgerBookId, actor, lanes, sources);
        return new AccountingWorkflowCertificationArtifactDto(
            id,
            AccountingCertificationArtifactStatusDto.Certified,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            actor,
            ExistingTimestamp(current?.WorkflowCertificationArtifacts, id) ?? now,
            SourceService,
            lanes.Select(lane => new AccountingWorkflowCertificationLaneDto(
                    lane,
                    AccountingCertificationArtifactLaneStatusDto.Passed,
                    SourcesFor(sources, source => source.WorkflowLanes.Contains(lane))))
                .ToArray(),
            SourcesFor(sources, static source => source.WorkflowLanes.Count > 0),
            CorrelationId: TrimOrNull(correlationId));
    }

    private static AccountingDimensionalCertificationArtifactDto? BuildDimensionalArtifact(
        IReadOnlyList<AccountingDimensionalCertificationLaneKindDto> lanes,
        IReadOnlyList<AccountingCertificationEvidenceSource> sources,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid ledgerBookId,
        string actor,
        string? correlationId,
        AccountingProductionCertificationProfileDto? current,
        DateTimeOffset now)
    {
        if (lanes.Count == 0)
        {
            return null;
        }

        var id = BuildCertificationId("dimensional", tenantId, companyId, fundProfileId, ledgerBookId, actor, lanes, sources);
        return new AccountingDimensionalCertificationArtifactDto(
            id,
            AccountingCertificationArtifactStatusDto.Certified,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            AccountingProductionCertificationAuthoritySubjects.BuildScopeSubjectId(tenantId, companyId, fundProfileId, ledgerBookId),
            actor,
            ExistingTimestamp(current?.DimensionalCertificationArtifacts, id) ?? now,
            SourceService,
            lanes.Select(lane => new AccountingDimensionalCertificationLaneDto(
                    lane,
                    AccountingCertificationArtifactLaneStatusDto.Passed,
                    SourcesFor(sources, source => source.DimensionalLanes.Contains(lane))))
                .ToArray(),
            SourcesFor(sources, static source => source.DimensionalLanes.Count > 0),
            CorrelationId: TrimOrNull(correlationId));
    }

    private static AccountingTenantAdminCertificationArtifactDto? BuildTenantAdministrationArtifact(
        IReadOnlyList<AccountingTenantAdminCertificationLaneKindDto> lanes,
        IReadOnlyList<AccountingCertificationEvidenceSource> sources,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid ledgerBookId,
        string actor,
        string? correlationId,
        AccountingProductionCertificationProfileDto? current,
        DateTimeOffset now)
    {
        if (lanes.Count == 0)
        {
            return null;
        }

        var id = BuildCertificationId("tenant-administration", tenantId, companyId, fundProfileId, ledgerBookId, actor, lanes, sources);
        return new AccountingTenantAdminCertificationArtifactDto(
            id,
            AccountingCertificationArtifactStatusDto.Certified,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            actor,
            ExistingTimestamp(current?.TenantAdminCertificationArtifacts, id) ?? now,
            SourceService,
            lanes.Select(lane => new AccountingTenantAdminCertificationLaneDto(
                    lane,
                    AccountingCertificationArtifactLaneStatusDto.Passed,
                    SourcesFor(sources, source => source.TenantAdministrationLanes.Contains(lane))))
                .ToArray(),
            SourcesFor(sources, static source => source.TenantAdministrationLanes.Count > 0),
            CorrelationId: TrimOrNull(correlationId));
    }

    private static void BindEvidence<TArtifact>(
        ICollection<RetainedEvidenceIdentityDto> destination,
        IReadOnlyList<AccountingCertificationEvidenceSource> sources,
        TArtifact? artifact,
        string subjectType)
        where TArtifact : class
    {
        var certificationId = artifact switch
        {
            AccountingWorkflowCertificationArtifactDto workflow => workflow.CertificationId,
            AccountingDimensionalCertificationArtifactDto dimensional => dimensional.CertificationId,
            AccountingTenantAdminCertificationArtifactDto tenant => tenant.CertificationId,
            null => null,
            _ => throw new InvalidOperationException("Unsupported accounting certification artifact type.")
        };
        if (certificationId is null)
        {
            return;
        }

        foreach (var source in sources)
        {
            destination.Add(source.Evidence with
            {
                EvidenceId = $"{source.Evidence.EvidenceId}:{certificationId[..16]}",
                SubjectType = subjectType,
                SubjectId = certificationId
            });
        }
    }

    private static void ValidateAuthoritySources(
        IReadOnlyList<AccountingCertificationEvidenceSource> sources,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid ledgerBookId)
    {
        if (sources.Count == 0)
        {
            throw new ArgumentException("No authoritative accounting production certification evidence was resolved.");
        }

        var expectedSubjectId = AccountingProductionCertificationAuthoritySubjects.BuildScopeSubjectId(
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId);
        if (sources.Any(source =>
                !RetainedEvidenceIdentityValidator.IsComplete(source.Evidence) ||
                !string.Equals(
                    source.Evidence.SubjectType,
                    AccountingProductionCertificationAuthoritySubjects.ScopeEvidence,
                    StringComparison.Ordinal) ||
                !string.Equals(source.Evidence.SubjectId, expectedSubjectId, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Resolved accounting production certification evidence does not match the selected authority scope.");
        }
    }

    private static void EnsureCoverage<T>(
        IReadOnlyList<T> requested,
        IEnumerable<T> supported,
        string label)
        where T : struct, Enum
    {
        var supportedSet = supported.ToHashSet();
        var missing = requested.Where(item => !supportedSet.Contains(item)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"Authoritative accounting {label} evidence is missing for: {string.Join(", ", missing)}.");
        }
    }

    private static string BuildCertificationId<T>(
        string category,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid ledgerBookId,
        string actor,
        IReadOnlyList<T> lanes,
        IReadOnlyList<AccountingCertificationEvidenceSource> sources)
        where T : struct, Enum
    {
        var canonical = string.Join(
            "\n",
            category,
            tenantId.Trim().ToLowerInvariant(),
            companyId.Trim().ToLowerInvariant(),
            fundProfileId.Trim().ToLowerInvariant(),
            ledgerBookId.ToString("D", CultureInfo.InvariantCulture),
            actor.Trim().ToLowerInvariant(),
            string.Join(",", lanes.OrderBy(static item => item).Select(static item => item.ToString())),
            string.Join(",", sources
                .OrderBy(static source => source.Evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
                .Select(static source =>
                    $"{source.Evidence.EvidenceId}:{source.Evidence.ContentHashSha256}:{source.Evidence.EvidenceVersion}")));
        return $"accounting-certification-{category}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private static IReadOnlyList<string> SourcesFor(
        IEnumerable<AccountingCertificationEvidenceSource> sources,
        Func<AccountingCertificationEvidenceSource, bool> predicate)
        => sources
            .Where(predicate)
            .Select(static source => source.Evidence.EvidenceUri)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static DateTimeOffset? ExistingTimestamp<TArtifact>(
        IReadOnlyList<TArtifact>? artifacts,
        string certificationId)
        => artifacts?
            .Select(artifact => artifact switch
            {
                AccountingWorkflowCertificationArtifactDto workflow when workflow.CertificationId == certificationId => workflow.CertifiedAtUtc,
                AccountingDimensionalCertificationArtifactDto dimensional when dimensional.CertificationId == certificationId => dimensional.CertifiedAtUtc,
                AccountingTenantAdminCertificationArtifactDto tenant when tenant.CertificationId == certificationId => tenant.CertifiedAtUtc,
                _ => (DateTimeOffset?)null
            })
            .FirstOrDefault(static value => value.HasValue);

    private static bool SameArtifact<TArtifact>(IReadOnlyList<TArtifact>? current, TArtifact? next)
    {
        if (next is null)
        {
            return current is null || current.Count == 0;
        }

        var nextId = next switch
        {
            AccountingWorkflowCertificationArtifactDto workflow => workflow.CertificationId,
            AccountingDimensionalCertificationArtifactDto dimensional => dimensional.CertificationId,
            AccountingTenantAdminCertificationArtifactDto tenant => tenant.CertificationId,
            _ => string.Empty
        };
        return current?.Any(item => item switch
        {
            AccountingWorkflowCertificationArtifactDto workflow => workflow.CertificationId == nextId,
            AccountingDimensionalCertificationArtifactDto dimensional => dimensional.CertificationId == nextId,
            AccountingTenantAdminCertificationArtifactDto tenant => tenant.CertificationId == nextId,
            _ => false
        }) == true;
    }

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Accounting production certification {label} is required.")
            : value.Trim();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
