using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public interface IAccountingTenantAdministrationProfileStore
{
    Task<AccountingTenantAdministrationProfileDto?> GetAsync(
        string? tenantId,
        string? companyId,
        CancellationToken ct = default);

    Task<AccountingTenantAdministrationProfileDto> UpsertAsync(
        AccountingTenantAdministrationProfileUpsertRequestDto request,
        CancellationToken ct = default);
}

public sealed class InMemoryAccountingTenantAdministrationProfileStore : IAccountingTenantAdministrationProfileStore
{
    private readonly Dictionary<string, AccountingTenantAdministrationProfileDto> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public Task<AccountingTenantAdministrationProfileDto?> GetAsync(
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = BuildKey(tenantId, companyId);
        return Task.FromResult(_profiles.TryGetValue(key, out var profile) ? profile : null);
    }

    public Task<AccountingTenantAdministrationProfileDto> UpsertAsync(
        AccountingTenantAdministrationProfileUpsertRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var profile = FileAccountingTenantAdministrationProfileStore.NormalizeProfile(request);
        _profiles[BuildKey(profile.TenantId, profile.CompanyId)] = profile;
        return Task.FromResult(profile);
    }

    private static string BuildKey(string? tenantId, string? companyId)
        => FileAccountingTenantAdministrationProfileStore.BuildKey(tenantId, companyId);
}

public sealed class FileAccountingTenantAdministrationProfileStore : IAccountingTenantAdministrationProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly TenantAdministrationEvidenceRequirement[] EvidenceRequirements =
    [
        new("tenant scope", profile => profile.TenantScopeConfigured, "tenant-scope", "tenant-storage", "tenant-ledger", "tenant-provider"),
        new("admin role profile", profile => profile.AdminRoleProfileConfigured, "admin-role", "role-profile", "accounting-admin-role"),
        new("scoped access policy", profile => profile.ScopedAccessPoliciesConfigured, "scoped-access", "access-policy", "entitlement"),
        new("reporting group", profile => profile.ReportingGroupsConfigured, "reporting-group", "report-group", "delivery-group"),
        new("accounting admin surface", profile => profile.AccountingAdminSurfaceConfigured, "accounting-admin-surface", "operator-surface", "admin-studio", "setup-surface"),
        new("browser accounting admin surface", profile => profile.BrowserAccountingAdminSurfaceConfigured, "browser-admin-studio", "browser-accounting-admin", "browser-setup"),
        new("WPF accounting admin surface", profile => profile.WpfAccountingAdminSurfaceConfigured, "wpf-admin-studio", "desktop-accounting-admin", "wpf-setup"),
        new("chart administration studio", profile => profile.ChartAdministrationStudioConfigured, "chart-admin", "chart-administration", "chart-of-accounts", "ledger-book-chart"),
        new("rule test promotion studio", profile => profile.RuleTestPromotionStudioConfigured, "rule-test-promotion", "rules-studio", "rule-tests", "promotion-queue"),
        new("close setup studio", profile => profile.CloseSetupStudioConfigured, "close-setup", "close-checklist", "close-calendar", "materiality-policy"),
        new("provider mapping studio", profile => profile.ProviderMappingStudioConfigured, "provider-mapping", "external-gl-mapping", "gl-mapping", "mapping-profile"),
        new("tenant company report group setup studio", profile => profile.TenantCompanyReportGroupSetupStudioConfigured, "tenant-company-report-group", "tenant-company-setup", "report-group-setup", "company-report-group"),
        new("audit review tooling", profile => profile.AuditReviewToolingConfigured, "audit-review", "audit-tooling", "audit-workbench", "evidence-review"),
        new("bulk import/export safeguard", profile => profile.BulkImportExportSafeguardsConfigured, "bulk-import-export", "bulk-import", "bulk-export", "import-export-safeguard"),
        new("performance validation", profile => profile.PerformanceValidationConfigured, "performance-validation", "performance-test", "load-test", "capacity-validation"),
        new("disaster recovery runbook", profile => profile.DisasterRecoveryRunbookConfigured, "disaster-recovery", "dr-runbook", "operating-runbook", "recovery-validation"),
        new("ledger book administration studio", profile => profile.LedgerBookAdministrationStudioConfigured, "ledger-book-admin", "ledger-book-administration", "book-administration", "ledger-book-setup"),
        new("posting rule authoring studio", profile => profile.PostingRuleAuthoringStudioConfigured, "posting-rule-authoring", "posting-rule-studio", "rule-authoring", "posting-rule-setup"),
        new("approval queue studio", profile => profile.ApprovalQueueStudioConfigured, "approval-queue", "promotion-approval", "je-approval", "configuration-approval"),
        new("dimension mapping studio", profile => profile.DimensionMappingStudioConfigured, "dimension-mapping", "dimension-map", "external-dimension-mapping", "gl-dimension-mapping"),
        new("implementation sandbox", profile => profile.ImplementationSandboxConfigured, "implementation-sandbox", "sandbox-validation", "fixture-validation", "implementation-fixture")
    ];

    private readonly string _snapshotPath;
    private readonly ILogger<FileAccountingTenantAdministrationProfileStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAccountingTenantAdministrationProfileStore(
        string snapshotPath,
        ILogger<FileAccountingTenantAdministrationProfileStore> logger)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Accounting tenant administration profile snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AccountingTenantAdministrationProfileDto?> GetAsync(
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, companyId);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Profiles.FirstOrDefault(profile =>
            string.Equals(BuildKey(profile.TenantId, profile.CompanyId), key, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AccountingTenantAdministrationProfileDto> UpsertAsync(
        AccountingTenantAdministrationProfileUpsertRequestDto request,
        CancellationToken ct = default)
    {
        var profile = NormalizeProfile(request);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var key = BuildKey(profile.TenantId, profile.CompanyId);
            var profiles = snapshot.Profiles
                .Where(item => !string.Equals(BuildKey(item.TenantId, item.CompanyId), key, StringComparison.OrdinalIgnoreCase))
                .Append(profile)
                .OrderBy(static item => item.TenantId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.CompanyId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var json = JsonSerializer.Serialize(new AccountingTenantAdministrationProfileSnapshot(profiles), JsonOptions);
            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static AccountingTenantAdministrationProfileDto NormalizeProfile(
        AccountingTenantAdministrationProfileUpsertRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        EnsureHumanOrigin(request.ActionOrigin);
        var tenantId = RequireText(request.Profile.TenantId, "tenant id");
        var companyId = RequireText(request.Profile.CompanyId, "company id");
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
        EnsureTenantCompanyScopedEvidence(request.Profile, tenantId, companyId, evidence);
        var approvalQueueConfigurations = NormalizeApprovalQueueConfigurations(request.Profile.ApprovalQueueConfigurations);
        var dimensionMappingConfigurations = NormalizeDimensionMappingConfigurations(request.Profile.DimensionMappingConfigurations);
        EnsureStudioConfigurations(request.Profile, approvalQueueConfigurations, dimensionMappingConfigurations);

        return request.Profile with
        {
            TenantId = tenantId,
            CompanyId = companyId,
            UpdatedAtUtc = request.Profile.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : request.Profile.UpdatedAtUtc,
            UpdatedBy = actor,
            EvidenceReferences = evidence,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? request.Profile.CorrelationId
                : request.CorrelationId.Trim(),
            ApprovalQueueConfigurations = approvalQueueConfigurations,
            DimensionMappingConfigurations = dimensionMappingConfigurations
        };
    }

    private static void EnsureStudioConfigurations(
        AccountingTenantAdministrationProfileDto profile,
        IReadOnlyList<AccountingApprovalQueueConfigurationDto> approvalQueueConfigurations,
        IReadOnlyList<AccountingDimensionMappingConfigurationDto> dimensionMappingConfigurations)
    {
        if (profile.ApprovalQueueStudioConfigured && approvalQueueConfigurations.Count == 0)
        {
            throw new ArgumentException("Accounting approval queue studio configuration is required when the approval queue studio is configured.");
        }

        if (profile.DimensionMappingStudioConfigured && dimensionMappingConfigurations.Count == 0)
        {
            throw new ArgumentException("Accounting dimension mapping studio configuration is required when the dimension mapping studio is configured.");
        }
    }

    private static IReadOnlyList<AccountingApprovalQueueConfigurationDto> NormalizeApprovalQueueConfigurations(
        IReadOnlyList<AccountingApprovalQueueConfigurationDto>? configurations)
    {
        if (configurations is null || configurations.Count == 0)
        {
            return [];
        }

        return configurations
            .Where(static configuration => configuration is not null)
            .Select(static configuration =>
            {
                var queueId = RequireText(configuration.QueueId, "approval queue id");
                var requiredApprovalCount = configuration.RequiredApprovalCount <= 0
                    ? throw new ArgumentException("Accounting approval queue required approval count must be greater than zero.")
                    : configuration.RequiredApprovalCount;
                return new AccountingApprovalQueueConfigurationDto(
                    queueId,
                    RequireText(configuration.DisplayName, "approval queue display name"),
                    RequireText(configuration.WorkflowKind, "approval queue workflow kind"),
                    RequireText(configuration.RequiredApprovalRole, "approval queue approval role"),
                    requiredApprovalCount,
                    RequireText(configuration.SegregationPolicy, "approval queue segregation policy"),
                    RequireText(configuration.EvidenceRequirement, "approval queue evidence requirement"));
            })
            .GroupBy(static configuration => configuration.QueueId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static configuration => configuration.QueueId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<AccountingDimensionMappingConfigurationDto> NormalizeDimensionMappingConfigurations(
        IReadOnlyList<AccountingDimensionMappingConfigurationDto>? configurations)
    {
        if (configurations is null || configurations.Count == 0)
        {
            return [];
        }

        return configurations
            .Where(static configuration => configuration is not null)
            .Select(static configuration => new AccountingDimensionMappingConfigurationDto(
                RequireText(configuration.MappingId, "dimension mapping id"),
                RequireText(configuration.DisplayName, "dimension mapping display name"),
                RequireText(configuration.ProviderId, "dimension mapping provider id"),
                configuration.MeridianDimensions ?? throw new ArgumentException("Accounting dimension mapping Meridian dimensions are required."),
                configuration.ProviderDimensions ?? throw new ArgumentException("Accounting dimension mapping provider dimensions are required."),
                RequireText(configuration.EvidenceRequirement, "dimension mapping evidence requirement")))
            .GroupBy(static configuration => configuration.MappingId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static configuration => configuration.MappingId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string BuildKey(string? tenantId, string? companyId)
    {
        var tenant = TrimOrNull(tenantId);
        var company = TrimOrNull(companyId);
        return tenant is null || company is null ? string.Empty : $"{tenant}|{company}";
    }

    private async Task<AccountingTenantAdministrationProfileSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountingTenantAdministrationProfileSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new AccountingTenantAdministrationProfileSnapshot([]);
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            return await JsonSerializer
                .DeserializeAsync<AccountingTenantAdministrationProfileSnapshot>(stream, JsonOptions, ct)
                .ConfigureAwait(false) ?? new AccountingTenantAdministrationProfileSnapshot([]);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to read accounting tenant administration profile snapshot {SnapshotPath}", _snapshotPath);
            return new AccountingTenantAdministrationProfileSnapshot([]);
        }
    }

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Accounting tenant administration profile {label} is required.")
            : value.Trim();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new ArgumentException("Only a human operator can certify accounting tenant administration profiles.", nameof(actionOrigin));
        }
    }

    private static void EnsureTenantCompanyScopedEvidence(
        AccountingTenantAdministrationProfileDto profile,
        string tenantId,
        string companyId,
        IReadOnlyList<string> evidenceReferences)
    {
        if (!DeclaresTenantAdministrationCertification(profile))
        {
            return;
        }

        if (evidenceReferences.Count == 0)
        {
            throw new ArgumentException("Accounting tenant administration evidence is required.");
        }

        if (!evidenceReferences.Any(reference =>
                ReferencesScope(reference, tenantId) &&
                ReferencesScope(reference, companyId)))
        {
            throw new ArgumentException("Accounting tenant administration evidence must identify the selected tenant and company.");
        }

        EnsureDeclaredControlEvidence(profile, tenantId, companyId, evidenceReferences);
    }

    private static void EnsureDeclaredControlEvidence(
        AccountingTenantAdministrationProfileDto profile,
        string tenantId,
        string companyId,
        IReadOnlyList<string> evidenceReferences)
    {
        foreach (var requirement in EvidenceRequirements)
        {
            EnsureControlEvidence(requirement, profile, tenantId, companyId, evidenceReferences);
        }
    }

    private static void EnsureControlEvidence(
        TenantAdministrationEvidenceRequirement requirement,
        AccountingTenantAdministrationProfileDto profile,
        string tenantId,
        string companyId,
        IReadOnlyList<string> evidenceReferences)
    {
        if (!requirement.IsConfigured(profile))
        {
            return;
        }

        if (!evidenceReferences.Any(reference =>
                ReferencesScope(reference, tenantId) &&
                ReferencesScope(reference, companyId) &&
                reference.Contains("tenant-admin", StringComparison.OrdinalIgnoreCase) &&
                (reference.Contains("tenant-administration/full", StringComparison.OrdinalIgnoreCase) ||
                 reference.Contains("tenant-admin/full", StringComparison.OrdinalIgnoreCase) ||
                 requirement.Aliases.Any(alias => reference.Contains(alias, StringComparison.OrdinalIgnoreCase)))))
        {
            throw new ArgumentException($"Accounting tenant administration evidence must include retained {requirement.Label} evidence.");
        }
    }

    private static bool DeclaresTenantAdministrationCertification(AccountingTenantAdministrationProfileDto profile)
        => profile.TenantScopeConfigured ||
           profile.AdminRoleProfileConfigured ||
           profile.ScopedAccessPoliciesConfigured ||
           profile.ReportingGroupsConfigured ||
           profile.AccountingAdminSurfaceConfigured ||
           profile.BrowserAccountingAdminSurfaceConfigured ||
           profile.WpfAccountingAdminSurfaceConfigured ||
           profile.ChartAdministrationStudioConfigured ||
           profile.RuleTestPromotionStudioConfigured ||
           profile.CloseSetupStudioConfigured ||
           profile.ProviderMappingStudioConfigured ||
           profile.TenantCompanyReportGroupSetupStudioConfigured ||
           profile.AuditReviewToolingConfigured ||
           profile.BulkImportExportSafeguardsConfigured ||
           profile.PerformanceValidationConfigured ||
           profile.DisasterRecoveryRunbookConfigured ||
           profile.LedgerBookAdministrationStudioConfigured ||
           profile.PostingRuleAuthoringStudioConfigured ||
           profile.ApprovalQueueStudioConfigured ||
           profile.DimensionMappingStudioConfigured ||
           profile.ImplementationSandboxConfigured;

    private static bool ReferencesScope(string? reference, string value)
        => !string.IsNullOrWhiteSpace(reference) &&
           reference.Contains(value, StringComparison.OrdinalIgnoreCase);

    private sealed record TenantAdministrationEvidenceRequirement(
        string Label,
        Func<AccountingTenantAdministrationProfileDto, bool> IsConfigured,
        params string[] Aliases);

    private sealed record AccountingTenantAdministrationProfileSnapshot(
        IReadOnlyList<AccountingTenantAdministrationProfileDto> Profiles);
}
