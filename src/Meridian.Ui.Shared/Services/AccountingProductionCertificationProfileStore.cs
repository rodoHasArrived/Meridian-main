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
            !evidenceReferences.Any(reference =>
                ReferencesScope(reference, tenantId) &&
                ReferencesScope(reference, companyId) &&
                ReferencesScope(reference, fundProfileId) &&
                ReferencesLedgerBook(reference, profile.LedgerBookId) &&
                ReferencesDimensionScope(reference)))
        {
            throw new ArgumentException("Accounting production certification evidence must identify the selected tenant, company, fund profile, ledger book, and certified dimension scope on the same retained artifact.");
        }

        EnsureDeclaredControlEvidence(profile, evidenceReferences, tenantId, companyId, fundProfileId, profile.LedgerBookId);
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

    private static void EnsureDeclaredControlEvidence(
        AccountingProductionCertificationProfileDto profile,
        IReadOnlyList<string> evidenceReferences,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid? ledgerBookId)
    {
        EnsureControlEvidence(
            profile.PostingRulesLedgerBookNativeCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "posting-rule workflow",
            "posting-rules",
            "posting-rule",
            "rules-studio",
            "posting-candidate");
        EnsureControlEvidence(
            profile.JournalLifecycleLedgerBookNativeCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "journal-entry lifecycle workflow",
            "journal-lifecycle",
            "journal-entry",
            "je-lifecycle",
            "manual-journal");
        EnsureControlEvidence(
            profile.CloseReportingLedgerBookNativeCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "close and reporting workflow",
            "close-reporting",
            "close-management",
            "report-package",
            "restatement");
        EnsureControlEvidence(
            profile.ExternalGlLedgerBookNativeCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "external-GL workflow",
            "external-gl",
            "external-ledger",
            "gl-export",
            "gl-import");
        EnsureControlEvidence(
            profile.ReconciliationLedgerBookNativeCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "reconciliation workflow",
            "reconciliation",
            "break-queue",
            "statement-reconciliation",
            "reconciliation-case");
        EnsureControlEvidence(
            profile.DirectLendingLedgerBookNativeCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "direct-lending workflow",
            "direct-lending",
            "loan-account",
            "borrower",
            "direct-lending-projection");
        EnsureControlEvidence(
            profile.StrategyLedgerReadLedgerBookNativeCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "strategy ledger-read workflow",
            "strategy-ledger",
            "strategy-run",
            "run-ledger",
            "strategy-ledger-read");
        EnsureDimensionControlEvidence(
            profile.PeriodReportDimensionQueriesCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "period-report dimension query",
            "period-report",
            "period-reports",
            "trial-balance",
            "financial-statement",
            "nav",
            "investor-package");
        EnsureDimensionControlEvidence(
            profile.CrossPeriodReportDimensionQueriesCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "cross-period dimension query",
            "cross-period",
            "comparative",
            "roll-forward");
        EnsureDimensionControlEvidence(
            profile.JournalQueryDimensionFiltersCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "journal dimension filter",
            "journal-query",
            "journal-filter",
            "journal-dimension",
            "ledger-journal");
        EnsureDimensionControlEvidence(
            profile.ExternalExportDimensionMappingCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "external export dimension mapping",
            "external-export",
            "export-dimension",
            "external-gl-mapping",
            "gl-export");
        EnsureDimensionControlEvidence(
            profile.LedgerLineDimensionsPersistedCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "ledger-line dimension persistence",
            "ledger-line",
            "line-dimension",
            "posted-ledger-line",
            "journal-line-dimension");
        EnsureDimensionControlEvidence(
            profile.TrialBalanceDimensionFiltersCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "trial-balance dimension filter",
            "trial-balance-filter",
            "trial-balance-dimension",
            "ledger-report-filter");
        EnsureDimensionControlEvidence(
            profile.ReportPackageDimensionProvenanceCertified,
            evidenceReferences,
            tenantId,
            companyId,
            fundProfileId,
            ledgerBookId,
            "report-package dimension provenance",
            "report-package-provenance",
            "report-line-provenance",
            "package-dimension",
            "nav-package");
    }

    private static void EnsureControlEvidence(
        bool certified,
        IReadOnlyList<string> evidenceReferences,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid? ledgerBookId,
        string label,
        params string[] aliases)
    {
        if (!certified)
        {
            return;
        }

        if (!evidenceReferences.Any(reference =>
                ReferencesRolloutScope(reference, tenantId, companyId, fundProfileId, ledgerBookId) &&
                (ReferencesAlias(reference, "production-certification/full") ||
                 ReferencesAlias(reference, "workflow-certification/full") ||
                 aliases.Any(alias => ReferencesAlias(reference, alias)))))
        {
            throw new ArgumentException($"Accounting production certification evidence must include retained {label} evidence on the same tenant, company, fund, and ledger-book artifact.");
        }
    }

    private static void EnsureDimensionControlEvidence(
        bool certified,
        IReadOnlyList<string> evidenceReferences,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid? ledgerBookId,
        string label,
        params string[] aliases)
    {
        if (!certified)
        {
            return;
        }

        if (!evidenceReferences.Any(reference =>
                ReferencesRolloutScope(reference, tenantId, companyId, fundProfileId, ledgerBookId) &&
                ReferencesDimensionScope(reference) &&
                (ReferencesAlias(reference, "production-certification/full") ||
                 ReferencesAlias(reference, "dimensions/full") ||
                 ReferencesAlias(reference, "dimensions/report-query-certification/full") ||
                 aliases.Any(alias => ReferencesAlias(reference, alias)))))
        {
            throw new ArgumentException($"Accounting production certification evidence must include retained {label} evidence on the same tenant, company, fund, ledger-book, and dimension-scope artifact.");
        }
    }

    private static bool ReferencesAlias(string? reference, string alias)
        => !string.IsNullOrWhiteSpace(reference) &&
           reference.Contains(alias, StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesRolloutScope(
        string? reference,
        string tenantId,
        string companyId,
        string fundProfileId,
        Guid? ledgerBookId)
        => ReferencesScope(reference, tenantId) &&
           ReferencesScope(reference, companyId) &&
           ReferencesScope(reference, fundProfileId) &&
           ReferencesLedgerBook(reference, ledgerBookId);

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
        return ReferencesScopedValue(reference, "ledger-book:", ledgerBookText) ||
               ReferencesScopedValue(reference, "ledger-book/", ledgerBookText) ||
               ReferencesScopedValue(reference, "book:", ledgerBookText) ||
               ReferencesScopedValue(reference, "ledgerBookId=", ledgerBookText) ||
               ReferencesScopedValue(reference, "ledgerBookId:", ledgerBookText) ||
               ReferencesScopedValue(reference, "ledgerBookId/", ledgerBookText) ||
               ReferencesScopedValue(reference, "ledger-book:", compactLedgerBookText) ||
               ReferencesScopedValue(reference, "ledger-book/", compactLedgerBookText) ||
               ReferencesScopedValue(reference, "book:", compactLedgerBookText) ||
               ReferencesScopedValue(reference, "ledgerBookId=", compactLedgerBookText) ||
               ReferencesScopedValue(reference, "ledgerBookId:", compactLedgerBookText) ||
               ReferencesScopedValue(reference, "ledgerBookId/", compactLedgerBookText);
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
           reference[index] is '/' or ':' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' or ' ' or '\t' or '\r' or '\n';

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
