using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
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

public sealed class FileAccountingProductionCertificationProfileStore : IAccountingProductionCertificationProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly ILogger<FileAccountingProductionCertificationProfileStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAccountingProductionCertificationProfileStore(
        string snapshotPath,
        ILogger<FileAccountingProductionCertificationProfileStore> logger)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Accounting production certification profile snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Profiles.FirstOrDefault(profile =>
            string.Equals(BuildKey(profile.TenantId, profile.CompanyId, profile.FundProfileId, profile.LedgerBookId), key, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AccountingProductionCertificationProfileDto> UpsertAsync(
        AccountingProductionCertificationProfileUpsertRequestDto request,
        CancellationToken ct = default)
    {
        var profile = NormalizeProfile(request);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var key = BuildKey(profile.TenantId, profile.CompanyId, profile.FundProfileId, profile.LedgerBookId);
            var profiles = snapshot.Profiles
                .Where(item => !string.Equals(BuildKey(item.TenantId, item.CompanyId, item.FundProfileId, item.LedgerBookId), key, StringComparison.OrdinalIgnoreCase))
                .Append(profile)
                .OrderBy(static item => item.TenantId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.CompanyId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.FundProfileId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.LedgerBookId?.ToString("D") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var json = JsonSerializer.Serialize(new AccountingProductionCertificationProfileSnapshot(profiles), JsonOptions);
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
        EnsureRolloutScopedEvidence(request.Profile, tenantId, companyId, fundProfileId, evidence);

        return request.Profile with
        {
            TenantId = tenantId,
            CompanyId = companyId,
            FundProfileId = fundProfileId,
            UpdatedAtUtc = request.Profile.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : request.Profile.UpdatedAtUtc,
            UpdatedBy = actor,
            EvidenceReferences = evidence,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? request.Profile.CorrelationId
                : request.CorrelationId.Trim()
        };
    }

    internal static string BuildKey(string? tenantId, string? companyId, string? fundProfileId, Guid? ledgerBookId)
    {
        var tenant = TrimOrNull(tenantId);
        var company = TrimOrNull(companyId);
        var fund = TrimOrNull(fundProfileId);
        return tenant is null || company is null || fund is null
            ? string.Empty
            : $"{tenant}|{company}|{fund}|{ledgerBookId?.ToString("D") ?? "fund"}";
    }

    private async Task<AccountingProductionCertificationProfileSnapshot> ReadSnapshotAsync(CancellationToken ct)
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

    private async Task<AccountingProductionCertificationProfileSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new AccountingProductionCertificationProfileSnapshot([]);
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            return await JsonSerializer
                .DeserializeAsync<AccountingProductionCertificationProfileSnapshot>(stream, JsonOptions, ct)
                .ConfigureAwait(false) ?? new AccountingProductionCertificationProfileSnapshot([]);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to read accounting production certification profile snapshot {SnapshotPath}", _snapshotPath);
            return new AccountingProductionCertificationProfileSnapshot([]);
        }
    }

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Accounting production certification profile {label} is required.")
            : value.Trim();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureRolloutScopedEvidence(
        AccountingProductionCertificationProfileDto profile,
        string tenantId,
        string companyId,
        string fundProfileId,
        IReadOnlyList<string> evidenceReferences)
    {
        if (!DeclaresProductionCertification(profile))
        {
            return;
        }

        if (evidenceReferences.Count == 0)
        {
            throw new ArgumentException("Accounting production certification evidence is required.");
        }

        if (!profile.LedgerBookId.HasValue)
        {
            throw new ArgumentException("Accounting production certification profile ledger book is required.");
        }

        if (!evidenceReferences.Any(reference =>
                ReferencesScope(reference, tenantId) &&
                ReferencesScope(reference, companyId) &&
                ReferencesScope(reference, fundProfileId) &&
                ReferencesLedgerBook(reference, profile.LedgerBookId)))
        {
            throw new ArgumentException("Accounting production certification evidence must identify the selected tenant, company, fund profile, and ledger book.");
        }

        if (DeclaresDimensionalReportingCertification(profile) &&
            !evidenceReferences.Any(ReferencesDimensionScope))
        {
            throw new ArgumentException("Accounting production certification evidence must identify the certified dimension scope.");
        }
    }

    private static bool DeclaresProductionCertification(AccountingProductionCertificationProfileDto profile)
        => profile.PostingRulesLedgerBookNativeCertified ||
           profile.JournalLifecycleLedgerBookNativeCertified ||
           profile.CloseReportingLedgerBookNativeCertified ||
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

    private static bool DeclaresDimensionalReportingCertification(AccountingProductionCertificationProfileDto profile)
        => profile.PeriodReportDimensionQueriesCertified ||
           profile.CrossPeriodReportDimensionQueriesCertified ||
           profile.JournalQueryDimensionFiltersCertified ||
           profile.ExternalExportDimensionMappingCertified ||
           profile.LedgerLineDimensionsPersistedCertified ||
           profile.TrialBalanceDimensionFiltersCertified ||
           profile.ReportPackageDimensionProvenanceCertified;

    private static bool ReferencesScope(string? reference, string value)
        => !string.IsNullOrWhiteSpace(reference) &&
           reference.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesDimensionScope(string? reference)
        => !string.IsNullOrWhiteSpace(reference) &&
           (reference.Contains("dimension-scope:", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("dimension-scope/", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("ledger-dimension-set:", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("ledger-dimension-set/", StringComparison.OrdinalIgnoreCase));

    private static bool ReferencesLedgerBook(string? reference, Guid? ledgerBookId)
    {
        if (!ledgerBookId.HasValue)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var ledgerBookText = ledgerBookId.Value.ToString("D");
        var compactLedgerBookText = ledgerBookId.Value.ToString("N");
        return reference.Contains($"ledger-book:{ledgerBookText}", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains($"ledger-book/{ledgerBookText}", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains($"book:{ledgerBookText}", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains($"ledger-book:{compactLedgerBookText}", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains($"ledger-book/{compactLedgerBookText}", StringComparison.OrdinalIgnoreCase) ||
               reference.Contains($"book:{compactLedgerBookText}", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new ArgumentException("Only a human operator can certify accounting production readiness profiles.", nameof(actionOrigin));
        }
    }

    private sealed record AccountingProductionCertificationProfileSnapshot(
        IReadOnlyList<AccountingProductionCertificationProfileDto> Profiles);
}
