using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IAccountingProductionCertificationProfileStore
{
    Task<AccountingProductionCertificationProfileDto?> GetAsync(
        string? tenantId,
        string? companyId,
        string? fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct = default);

    Task<AccountingProductionCertificationProfileDto> UpsertAsync(
        AccountingProductionCertificationProfileUpsertRequestDto request,
        CancellationToken ct = default);
}

public sealed class InMemoryAccountingProductionCertificationProfileStore : IAccountingProductionCertificationProfileStore
{
    private readonly Dictionary<string, AccountingProductionCertificationProfileDto> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public Task<AccountingProductionCertificationProfileDto?> GetAsync(
        string? tenantId,
        string? companyId,
        string? fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = BuildKey(tenantId, companyId, fundProfileId, ledgerBookId);
        return Task.FromResult(_profiles.TryGetValue(key, out var profile) ? profile : null);
    }

    public Task<AccountingProductionCertificationProfileDto> UpsertAsync(
        AccountingProductionCertificationProfileUpsertRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var profile = FileAccountingProductionCertificationProfileStore.NormalizeProfile(request);
        _profiles[BuildKey(profile.TenantId, profile.CompanyId, profile.FundProfileId, profile.LedgerBookId)] = profile;
        return Task.FromResult(profile);
    }

    private static string BuildKey(string? tenantId, string? companyId, string? fundProfileId, Guid? ledgerBookId)
        => FileAccountingProductionCertificationProfileStore.BuildKey(tenantId, companyId, fundProfileId, ledgerBookId);
}

public sealed class FileAccountingProductionCertificationProfileStore :
    JsonFileSnapshotStore<FileAccountingProductionCertificationProfileStore.AccountingProductionCertificationProfileSnapshot>,
    IAccountingProductionCertificationProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<FileAccountingProductionCertificationProfileStore> _logger;

    public FileAccountingProductionCertificationProfileStore(
        string snapshotPath,
        ILogger<FileAccountingProductionCertificationProfileStore> logger)
        : base(
            string.IsNullOrWhiteSpace(snapshotPath)
                ? throw new ArgumentException("Accounting production certification profile snapshot path is required.", nameof(snapshotPath))
                : snapshotPath,
            JsonOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override AccountingProductionCertificationProfileSnapshot CreateEmptySnapshot() => new([]);

    protected override AccountingProductionCertificationProfileSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger.LogWarning(exception, "Failed to read accounting production certification profile snapshot {SnapshotPath}", SnapshotPath);
        return new AccountingProductionCertificationProfileSnapshot([]);
    }

    public async Task<AccountingProductionCertificationProfileDto?> GetAsync(
        string? tenantId,
        string? companyId,
        string? fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, companyId, fundProfileId, ledgerBookId);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return await ReadSnapshotAsync(
            snapshot => snapshot.Profiles.FirstOrDefault(profile =>
                string.Equals(BuildKey(profile.TenantId, profile.CompanyId, profile.FundProfileId, profile.LedgerBookId), key, StringComparison.OrdinalIgnoreCase)),
            ct).ConfigureAwait(false);
    }

    public async Task<AccountingProductionCertificationProfileDto> UpsertAsync(
        AccountingProductionCertificationProfileUpsertRequestDto request,
        CancellationToken ct = default)
    {
        var profile = NormalizeProfile(request);
        return await UpdateSnapshotAsync(
            snapshot =>
            {
                var key = BuildKey(profile.TenantId, profile.CompanyId, profile.FundProfileId, profile.LedgerBookId);
                var profiles = snapshot.Profiles
                    .Where(item => !string.Equals(BuildKey(item.TenantId, item.CompanyId, item.FundProfileId, item.LedgerBookId), key, StringComparison.OrdinalIgnoreCase))
                    .Append(profile)
                    .OrderBy(static item => item.TenantId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.CompanyId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.FundProfileId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.LedgerBookId?.ToString("D") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return (new AccountingProductionCertificationProfileSnapshot(profiles), profile);
            },
            ct).ConfigureAwait(false);
    }

    internal static AccountingProductionCertificationProfileDto NormalizeProfile(
        AccountingProductionCertificationProfileUpsertRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        EnsureHumanOrigin(request.ActionOrigin);
        var tenantId = RequireText(request.Profile.TenantId, "tenant id");
        var companyId = RequireText(request.Profile.CompanyId, "company id");
        var fundProfileId = RequireText(request.Profile.FundProfileId, "fund profile id");
        var actor = string.IsNullOrWhiteSpace(request.Actor)
            ? RequireText(request.Profile.UpdatedBy, "actor")
            : request.Actor.Trim();
        var evidence = request.Profile.EvidenceReferences
            .Concat(request.EvidenceLinks)
            .Append(string.IsNullOrWhiteSpace(request.CorrelationId) ? null : $"correlation:{request.CorrelationId!.Trim()}")
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var retainedEvidence = NormalizeRetainedEvidence(
            request.Profile.RetainedEvidence.Concat(request.RetainedEvidence));
        EnsureCertificationArtifacts(request.Profile, tenantId, companyId, fundProfileId, retainedEvidence);
        EnsureRolloutScopedEvidence(request.Profile, tenantId, companyId, fundProfileId, retainedEvidence);

        return request.Profile with
        {
            TenantId = tenantId,
            CompanyId = companyId,
            FundProfileId = fundProfileId,
            UpdatedAtUtc = request.Profile.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : request.Profile.UpdatedAtUtc,
            UpdatedBy = actor,
            EvidenceReferences = evidence,
            RetainedEvidence = retainedEvidence,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? request.Profile.CorrelationId
                : request.CorrelationId.Trim()
        };
    }

    private static void EnsureCertificationArtifacts(
        AccountingProductionCertificationProfileDto profile,
        string tenantId,
        string companyId,
        string fundProfileId,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence)
    {
        foreach (var artifact in profile.WorkflowCertificationArtifacts.Where(static artifact => artifact.Status == AccountingCertificationArtifactStatusDto.Certified))
        {
            EnsureArtifactText(artifact.CertificationId, "workflow certification id");
            EnsureArtifactText(artifact.CertifiedBy, "workflow certified-by");
            EnsureArtifactText(artifact.SourceService, "workflow source service");
            if (artifact.CertifiedAtUtc == default || artifact.CertifiedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Accounting workflow certification artifact certified-at timestamp must be present and UTC.");
            }

            if (artifact.LedgerBookId == Guid.Empty ||
                !profile.LedgerBookId.HasValue ||
                artifact.LedgerBookId != profile.LedgerBookId.Value ||
                !string.Equals(NormalizeFundProfileId(artifact.FundProfileId), fundProfileId, StringComparison.OrdinalIgnoreCase) ||
                !ArtifactScopeMatches(artifact.TenantId, tenantId) ||
                !ArtifactScopeMatches(artifact.CompanyId, companyId))
            {
                throw new ArgumentException("Accounting workflow certification artifact scope must match the selected tenant, company, fund profile, and ledger book.");
            }

            if (artifact.Lanes.Count == 0 || artifact.Lanes.Any(static lane => lane.Status != AccountingCertificationArtifactLaneStatusDto.Passed))
            {
                throw new ArgumentException("Certified accounting workflow artifacts must include passed lane results.");
            }

            EnsureArtifactRetainedEvidence(
                retainedEvidence,
                AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
                artifact.CertificationId,
                "workflow");
        }

        foreach (var artifact in profile.DimensionalCertificationArtifacts.Where(static artifact => artifact.Status == AccountingCertificationArtifactStatusDto.Certified))
        {
            EnsureArtifactText(artifact.CertificationId, "dimensional certification id");
            EnsureArtifactText(artifact.CertifiedBy, "dimensional certified-by");
            EnsureArtifactText(artifact.SourceService, "dimensional source service");
            EnsureArtifactText(artifact.DimensionScopeEvidenceKey, "dimensional scope evidence key");
            if (artifact.CertifiedAtUtc == default || artifact.CertifiedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Accounting dimensional certification artifact certified-at timestamp must be present and UTC.");
            }

            if (artifact.LedgerBookId == Guid.Empty ||
                !profile.LedgerBookId.HasValue ||
                artifact.LedgerBookId != profile.LedgerBookId.Value ||
                !string.Equals(NormalizeFundProfileId(artifact.FundProfileId), fundProfileId, StringComparison.OrdinalIgnoreCase) ||
                !ArtifactScopeMatches(artifact.TenantId, tenantId) ||
                !ArtifactScopeMatches(artifact.CompanyId, companyId))
            {
                throw new ArgumentException("Accounting dimensional certification artifact scope must match the selected tenant, company, fund profile, and ledger book.");
            }

            if (artifact.Lanes.Count == 0 || artifact.Lanes.Any(static lane => lane.Status != AccountingCertificationArtifactLaneStatusDto.Passed))
            {
                throw new ArgumentException("Certified accounting dimensional artifacts must include passed lane results.");
            }

            EnsureArtifactRetainedEvidence(
                retainedEvidence,
                AccountingProductionCertificationEvidenceSubjectTypes.DimensionalArtifact,
                artifact.CertificationId,
                "dimensional");
        }

        foreach (var artifact in profile.TenantAdminCertificationArtifacts.Where(static artifact => artifact.Status == AccountingCertificationArtifactStatusDto.Certified))
        {
            EnsureArtifactText(artifact.CertificationId, "tenant administration certification id");
            EnsureArtifactText(artifact.CertifiedBy, "tenant administration certified-by");
            EnsureArtifactText(artifact.SourceService, "tenant administration source service");
            if (artifact.CertifiedAtUtc == default || artifact.CertifiedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Accounting tenant administration certification artifact certified-at timestamp must be present and UTC.");
            }

            if (!string.Equals(NormalizeFundProfileId(artifact.FundProfileId), fundProfileId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(TrimOrNull(artifact.TenantId), tenantId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(TrimOrNull(artifact.CompanyId), companyId, StringComparison.OrdinalIgnoreCase) ||
                !profile.LedgerBookId.HasValue ||
                artifact.LedgerBookId != profile.LedgerBookId.Value)
            {
                throw new ArgumentException("Accounting tenant administration certification artifact scope must match the selected tenant, company, fund profile, and ledger book.");
            }

            if (artifact.Lanes.Count == 0 || artifact.Lanes.Any(static lane => lane.Status != AccountingCertificationArtifactLaneStatusDto.Passed))
            {
                throw new ArgumentException("Certified accounting tenant administration artifacts must include passed lane results.");
            }

            EnsureArtifactRetainedEvidence(
                retainedEvidence,
                AccountingProductionCertificationEvidenceSubjectTypes.TenantAdministrationArtifact,
                artifact.CertificationId,
                "tenant administration");
        }
    }

    private static void EnsureArtifactRetainedEvidence(
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence,
        string subjectType,
        string certificationId,
        string label)
    {
        if (!retainedEvidence.Any(evidence =>
                AccountingProductionCertificationEvidenceValidator.BindsTo(
                    evidence,
                    subjectType,
                    certificationId)))
        {
            throw new ArgumentException($"Accounting {label} certification artifact requires complete retained evidence bound to its certification id.");
        }
    }

    private static void EnsureArtifactText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Accounting production certification artifact {label} is required.");
        }
    }

    private static bool ArtifactScopeMatches(string? artifactScope, string requiredScope)
        => !string.IsNullOrWhiteSpace(artifactScope) &&
           string.Equals(TrimOrNull(artifactScope), requiredScope, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFundProfileId(string? value)
        => TrimOrNull(value) ?? "default-fund";

    internal static string BuildKey(string? tenantId, string? companyId, string? fundProfileId, Guid? ledgerBookId)
    {
        var tenant = TrimOrNull(tenantId);
        var company = TrimOrNull(companyId);
        var fund = TrimOrNull(fundProfileId);
        return tenant is null || company is null || fund is null
            ? string.Empty
            : $"{tenant}|{company}|{fund}|{ledgerBookId?.ToString("D") ?? "fund"}";
    }

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Accounting production certification profile {label} is required.")
            : value.Trim();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<RetainedEvidenceIdentityDto> NormalizeRetainedEvidence(
        IEnumerable<RetainedEvidenceIdentityDto> retainedEvidence)
    {
        var candidates = retainedEvidence.ToArray();
        var issues = candidates
            .SelectMany(item => RetainedEvidenceIdentityValidator.Validate(item)
                .Select(issue => $"{item?.EvidenceId ?? "<missing>"}: {issue}"))
            .ToArray();
        if (issues.Length > 0)
        {
            throw new ArgumentException($"Accounting production certification retained evidence is incomplete: {string.Join(" ", issues)}");
        }

        var normalized = candidates
            .Where(static item => item is not null)
            .Select(static item => item!)
            .DistinctBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Any(AccountingProductionCertificationEvidenceValidator.IsSynthesized))
        {
            throw new ArgumentException("Accounting production certification retained evidence cannot be synthesized from readiness or production-profile state.");
        }

        if (normalized.Any(static item =>
                AccountingProductionCertificationEvidenceValidator.IsLegacyFullToken(item.EvidenceUri)))
        {
            throw new ArgumentException("Accounting production certification retained evidence cannot use a legacy full-token URI.");
        }

        return normalized;
    }

    private static void EnsureRolloutScopedEvidence(
        AccountingProductionCertificationProfileDto profile,
        string tenantId,
        string companyId,
        string fundProfileId,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence)
    {
        if (!DeclaresProductionCertification(profile))
        {
            return;
        }

        if (retainedEvidence.Count == 0)
        {
            throw new ArgumentException("Accounting production certification requires complete retained evidence identity, hash, source, review, effective-date, and version metadata.");
        }

        if (!profile.LedgerBookId.HasValue)
        {
            throw new ArgumentException("Accounting production certification profile ledger book is required.");
        }

        EnsureDeclaredControlEvidence(profile, retainedEvidence);
    }

    private static bool DeclaresProductionCertification(AccountingProductionCertificationProfileDto profile)
        => profile.PostingRulesLedgerBookNativeCertified ||
           profile.JournalLifecycleLedgerBookNativeCertified ||
           profile.CloseReportingLedgerBookNativeCertified ||
           profile.ClosePlanConfigurationLedgerBookNativeCertified ||
           profile.ExternalGlLedgerBookNativeCertified ||
           profile.PeriodReportDimensionQueriesCertified ||
           profile.CrossPeriodReportDimensionQueriesCertified ||
           profile.JournalQueryDimensionFiltersCertified ||
           profile.ExternalExportDimensionMappingCertified ||
           profile.ReconciliationLedgerBookNativeCertified ||
           profile.DirectLendingLedgerBookNativeCertified ||
           profile.StrategyLedgerReadLedgerBookNativeCertified ||
           profile.LedgerLineDimensionsPersistedCertified ||
           profile.TrialBalanceDimensionFiltersCertified ||
           profile.ReportPackageDimensionProvenanceCertified;

    private static void EnsureDeclaredControlEvidence(
        AccountingProductionCertificationProfileDto profile,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence)
    {
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.PostingRulesLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.PostingRules, "posting-rule workflow");
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.JournalLifecycleLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.JournalLifecycle, "journal-entry lifecycle workflow");
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.CloseReportingLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.CloseReporting, "close and reporting workflow");
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.ClosePlanConfigurationLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.ClosePlanConfiguration, "close-plan configuration workflow");
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.ExternalGlLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.ExternalGl, "external-GL workflow");
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.ReconciliationLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.Reconciliation, "reconciliation workflow");
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.DirectLendingLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.DirectLendingProjection, "direct-lending workflow");
        EnsureWorkflowControlEvidence(profile, retainedEvidence, profile.StrategyLedgerReadLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.StrategyLedgerReads, "strategy ledger-read workflow");

        EnsureDimensionalControlEvidence(profile, retainedEvidence, profile.PeriodReportDimensionQueriesCertified, AccountingDimensionalCertificationLaneKindDto.PeriodReports, "period-report dimension query");
        EnsureDimensionalControlEvidence(profile, retainedEvidence, profile.CrossPeriodReportDimensionQueriesCertified, AccountingDimensionalCertificationLaneKindDto.CrossPeriodReports, "cross-period dimension query");
        EnsureDimensionalControlEvidence(profile, retainedEvidence, profile.JournalQueryDimensionFiltersCertified, AccountingDimensionalCertificationLaneKindDto.JournalFilters, "journal dimension filter");
        EnsureDimensionalControlEvidence(profile, retainedEvidence, profile.ExternalExportDimensionMappingCertified, AccountingDimensionalCertificationLaneKindDto.ExternalExportMappings, "external export dimension mapping");
        EnsureDimensionalControlEvidence(profile, retainedEvidence, profile.LedgerLineDimensionsPersistedCertified, AccountingDimensionalCertificationLaneKindDto.LedgerLinePersistence, "ledger-line dimension persistence");
        EnsureDimensionalControlEvidence(profile, retainedEvidence, profile.TrialBalanceDimensionFiltersCertified, AccountingDimensionalCertificationLaneKindDto.TrialBalanceFilters, "trial-balance dimension filter");
        EnsureDimensionalControlEvidence(profile, retainedEvidence, profile.ReportPackageDimensionProvenanceCertified, AccountingDimensionalCertificationLaneKindDto.ReportPackageProvenance, "report-package dimension provenance");
    }

    private static void EnsureWorkflowControlEvidence(
        AccountingProductionCertificationProfileDto profile,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence,
        bool certified,
        AccountingWorkflowCertificationLaneKindDto laneKind,
        string label)
    {
        if (!certified)
        {
            return;
        }

        if (!profile.WorkflowCertificationArtifacts.Any(artifact =>
                artifact.Status == AccountingCertificationArtifactStatusDto.Certified &&
                artifact.Lanes.Any(lane =>
                    lane.Kind == laneKind &&
                    lane.Status == AccountingCertificationArtifactLaneStatusDto.Passed) &&
                retainedEvidence.Any(evidence =>
                    AccountingProductionCertificationEvidenceValidator.BindsTo(
                        evidence,
                        AccountingProductionCertificationEvidenceSubjectTypes.WorkflowArtifact,
                        artifact.CertificationId))))
        {
            throw new ArgumentException($"Accounting production certification requires a scoped, passed {label} artifact with complete retained evidence bound to the same certification id.");
        }
    }

    private static void EnsureDimensionalControlEvidence(
        AccountingProductionCertificationProfileDto profile,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence,
        bool certified,
        AccountingDimensionalCertificationLaneKindDto laneKind,
        string label)
    {
        if (!certified)
        {
            return;
        }

        if (!profile.DimensionalCertificationArtifacts.Any(artifact =>
                artifact.Status == AccountingCertificationArtifactStatusDto.Certified &&
                !string.IsNullOrWhiteSpace(artifact.DimensionScopeEvidenceKey) &&
                artifact.Lanes.Any(lane =>
                    lane.Kind == laneKind &&
                    lane.Status == AccountingCertificationArtifactLaneStatusDto.Passed) &&
                retainedEvidence.Any(evidence =>
                    AccountingProductionCertificationEvidenceValidator.BindsTo(
                        evidence,
                        AccountingProductionCertificationEvidenceSubjectTypes.DimensionalArtifact,
                        artifact.CertificationId))))
        {
            throw new ArgumentException($"Accounting production certification requires a scoped, passed {label} artifact with complete retained evidence bound to the same certification id.");
        }
    }

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new ArgumentException("Only a human operator can certify accounting production readiness profiles.", nameof(actionOrigin));
        }
    }

    public sealed record AccountingProductionCertificationProfileSnapshot(
        IReadOnlyList<AccountingProductionCertificationProfileDto> Profiles);
}
