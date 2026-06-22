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

        return request.Profile with
        {
            TenantId = tenantId,
            CompanyId = companyId,
            UpdatedAtUtc = request.Profile.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : request.Profile.UpdatedAtUtc,
            UpdatedBy = actor,
            EvidenceReferences = evidence,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? request.Profile.CorrelationId
                : request.CorrelationId.Trim()
        };
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

    private sealed record AccountingTenantAdministrationProfileSnapshot(
        IReadOnlyList<AccountingTenantAdministrationProfileDto> Profiles);
}
