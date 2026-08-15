using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.AccountingSystem;

public sealed class AccountingSystemIntegrationService
{
    private const string DefaultProviderId = "quickbooks-fixture";
    private const string QuickBooksOnlineProviderId = "quickbooks";
    private const string DefaultFundProfileId = "default-fund";
    private static readonly PlannedAccountingSystemProvider[] PlannedProviders =
    [
        new(
            QuickBooksOnlineProviderId,
            "QuickBooks Online",
            "Live QuickBooks Online OAuth, company selection, and read-only GL import require the QuickBooks Online provider registration.",
            ["QuickBooksAccount", "QuickBooksJournalEntry", "QuickBooksTrialBalance"],
            BuildProviderMappingRequirements(QuickBooksOnlineProviderId)),
        new(
            "xero",
            "Xero",
            "Xero chart, journal, and trial-balance import mapping is planned; live posting remains disabled until a separately approved adapter exists.",
            ["XeroAccount", "XeroManualJournal", "XeroTrialBalance"],
            BuildProviderMappingRequirements("xero-fixture")),
        new(
            "netsuite",
            "NetSuite",
            "NetSuite chart, journal, and trial-balance import mapping is planned; live posting remains disabled until a separately approved adapter exists.",
            ["NetSuiteAccount", "NetSuiteJournalEntry", "NetSuiteTrialBalance"],
            BuildProviderMappingRequirements("netsuite-fixture"))
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly HashSet<string> ExternalGlReconciliationSafeguardIssueCodes =
    [
        "MissingExternalGlReconciliation",
        "ExternalGlReconciliationLedgerBookMismatch",
        "ExternalGlReconciliationPeriodMismatch",
        "ExternalGlReconciliationSnapshotChanged",
        "UnresolvedExternalGlBreaks",
        "MissingExternalGlExportControlEvidence",
        "UnscopedExternalGlExportControlEvidence",
        "LiveExternalPostingProviderEnabled",
        "LiveExternalPostingRetainedPackageEnabled",
        "MissingGeneratedExternalGlExportLines"
    ];

    private readonly IReadOnlyList<IAccountingSystemProvider> _providers;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly ConcurrentDictionary<string, AccountingSystemImportDetailDto> _latestImports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScopedExternalGlMappingProfile> _mappingProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ExternalGlExportPackageDto> _exportPackages = new(StringComparer.OrdinalIgnoreCase);

    public AccountingSystemIntegrationService(
        IEnumerable<IAccountingSystemProvider> providers,
        ILedgerJournalStore? ledgerJournalStore = null)
    {
        _providers = providers?.ToArray() ?? [];
        _ledgerJournalStore = ledgerJournalStore;
    }

    public async Task<IReadOnlyList<AccountingSystemProviderDto>> ListProvidersAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = new List<AccountingSystemProviderDto>(_providers.Count);
        foreach (var provider in _providers)
        {
            rows.Add(await ToProviderDtoAsync(provider, ct).ConfigureAwait(false));
        }

        foreach (var plannedProvider in PlannedProviders)
        {
            if (_providers.Any(provider => string.Equals(provider.ProviderId, plannedProvider.ProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            rows.Add(plannedProvider.ToDto());
        }

        return rows
            .OrderBy(static row => row.State == AccountingSystemProviderStateDto.Available ? 0 : 1)
            .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<IReadOnlyList<ExternalGlMappingProfileDto>> ListMappingProfilesAsync(
        string? providerId = null,
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedProviderId = string.IsNullOrWhiteSpace(providerId) ? null : NormalizeProviderId(providerId);
        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var rows = _mappingProfiles.Values
            .Where(record => normalizedProviderId is null || string.Equals(record.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase))
            .Where(record => normalizedFundProfileId is null || string.Equals(record.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(record => ledgerBookId is null || record.LedgerBookId == ledgerBookId)
            .Where(record => normalizedTenantId is null || string.Equals(record.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(record => normalizedCompanyId is null || string.Equals(record.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .Select(static record => record.Profile)
            .OrderBy(static profile => profile.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ExternalGlMappingProfileDto>>(rows);
    }

    public Task<ExternalGlMappingProfileDto> UpsertMappingProfileAsync(
        AccountingSystemMappingProfileUpsertRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);

        var profileId = RequireText(request.Profile.ProfileId, "Mapping profile id");
        var actor = RequireText(request.Actor, "Actor");
        var providerId = NormalizeProviderId(request.ProviderId ?? request.Profile.ProviderId);
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);
        var evidenceLinks = NormalizeEvidenceReferences(request.EvidenceLinks);
        var certificationState = ResolveMappingProfileCertificationState(
            request.Profile,
            providerId,
            fundProfileId,
            profileId,
            evidenceLinks);
        if (certificationState != AccountingCertificationStateDto.Draft)
        {
            EnsureHumanOrigin(request.ActionOrigin, "certify external GL mapping profiles");
        }

        var normalizedProfile = request.Profile with
        {
            ProfileId = profileId,
            ProviderId = providerId,
            DisplayName = RequireText(request.Profile.DisplayName, "Mapping profile display name"),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CertificationState = certificationState,
            AccountMappings = NormalizeAccountMappings(request.Profile.AccountMappings),
            DimensionMappings = (request.Profile.DimensionMappings ?? [])
                .Select(mapping => mapping with { ProviderId = providerId })
                .ToArray()
        };

        if (normalizedProfile.AccountMappings.Count == 0 && normalizedProfile.DimensionMappings.Count == 0)
        {
            normalizedProfile = normalizedProfile with { CertificationState = AccountingCertificationStateDto.Draft };
        }

        _mappingProfiles[MappingProfileKey(providerId, fundProfileId, request.LedgerBookId, profileId, tenantId, companyId)] =
            new ScopedExternalGlMappingProfile(providerId, fundProfileId, request.LedgerBookId, normalizedProfile, actor, evidenceLinks, tenantId, companyId);

        return Task.FromResult(normalizedProfile);
    }

    public async Task<ExternalGlExportPackageDto> CreateExportPackageAsync(
        AccountingSystemExportPackageRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        EnsureHumanOrigin(request.ActionOrigin, "retain external GL export review packages");
        var actor = RequireText(request.Actor, "Actor");
        var providerId = NormalizeProviderId(request.ProviderId);
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);
        var providerSupportsPosting = ProviderSupportsPosting(providerId);
        var mappingProfile = ResolveMappingProfile(providerId, fundProfileId, request.LedgerBookId, request.MappingProfileId, tenantId, companyId);
        var reconciliation = await TryReconcileLatestAsync(providerId, fundProfileId, request.LedgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
        var periodStart = request.PeriodStart ?? reconciliation?.PeriodStart ?? CurrentMonthStart();
        var periodEnd = request.PeriodEnd ?? reconciliation?.PeriodEnd ?? CurrentMonthEnd(periodStart);
        var requestEvidenceLinks = NormalizeEvidenceReferences(request.EvidenceLinks);
        var generatedLines = BuildGeneratedExportLines(mappingProfile, reconciliation, request.LedgerBookId);
        var reconciliationSnapshotHash = ComputeReconciliationSnapshotHash(reconciliation);
        var validationIssues = BuildExportValidationIssues(
            providerId,
            fundProfileId,
            providerSupportsPosting,
            request.LedgerBookId,
            mappingProfile,
            reconciliation,
            periodStart,
            periodEnd,
            request.RequireBalancedReconciliation,
            requestEvidenceLinks,
            generatedLines,
            packageReconciliationSnapshotHash: reconciliationSnapshotHash);
        var evidenceLinks = BuildExportEvidenceLinks(request, mappingProfile, reconciliation);
        var hasCritical = validationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var certificationState = hasCritical
            ? AccountingCertificationStateDto.Draft
            : AccountingCertificationStateDto.ReadyForReview;
        var safeguardIssueCodes = BuildReconciliationSafeguardIssueCodes(validationIssues);
        var safeguardState = ResolveReconciliationSafeguardState(
            reconciliation?.ReconciliationId,
            reconciliationSnapshotHash,
            safeguardIssueCodes,
            certificationState);
        var certification = new ExternalGlExportCertificationDto(
            $"external-gl-export-cert-{SanitizeId(providerId)}-{SanitizeId(fundProfileId)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            certificationState,
            actor,
            DateTimeOffset.UtcNow,
            hasCritical
                ? "Export package is retained as a guarded review artifact and cannot be certified until validation issues are resolved."
                : "Export package passed automated mapping and reconciliation safeguards and is ready for human certification review.",
            evidenceLinks);

        var package = new ExternalGlExportPackageDto(
            BuildExportPackageId(providerId, fundProfileId, request.LedgerBookId, periodEnd, tenantId, companyId),
            providerId,
            fundProfileId,
            request.LedgerBookId,
            periodStart,
            periodEnd,
            DateTimeOffset.UtcNow,
            actor,
            PostingEnabled: false,
            PostingDisabledReason: "Guarded export package only; live external GL posting remains disabled until a separately approved adapter and release gate publish Meridian-owned ledger entries.",
            request.JournalEntryIds,
            evidenceLinks,
            certification,
            validationIssues,
            generatedLines,
            mappingProfile?.Profile.ProfileId,
            reconciliation?.ReconciliationId,
            request.RequireBalancedReconciliation,
            safeguardState,
            safeguardIssueCodes,
            tenantId,
            companyId,
            reconciliationSnapshotHash);
        _exportPackages[package.ExportPackageId] = package;
        return package;
    }

    public async Task<ExternalGlExportPackageDto?> CertifyExportPackageAsync(
        CertifyAccountingSystemExportPackageRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "certify external GL export packages");
        var exportPackageId = RequireText(request.ExportPackageId, "ExportPackageId");
        var actor = RequireText(request.Actor, "Actor");
        var notes = RequireText(request.Notes, "Notes");
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);
        var evidenceLinks = NormalizeEvidenceReferences(request.EvidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("At least one external GL export certification evidence link is required.");
        }

        if (!HasExportCertificationEvidence(evidenceLinks))
        {
            throw new ArgumentException("External GL export certification requires retained approval, certification, sign-off, or review evidence.");
        }

        if (!TryGetExportPackage(exportPackageId, tenantId, companyId, out var package))
        {
            return null;
        }

        if (!HasExportCertificationEvidenceWithProvenance(package, evidenceLinks))
        {
            throw new ArgumentException("External GL export certification evidence must reference the retained export package id, certification id, export ledger book, and exact export period in the same artifact.");
        }

        if (package.Certification is null)
        {
            throw new InvalidOperationException($"External GL export package '{exportPackageId}' has no certification record.");
        }

        if (package.PostingEnabled || string.IsNullOrWhiteSpace(package.PostingDisabledReason))
        {
            throw new InvalidOperationException(
                $"External GL export package '{exportPackageId}' cannot be certified while live external GL posting is enabled or missing a retained posting-disabled reason.");
        }

        if (package.Certification.State == AccountingCertificationStateDto.Certified)
        {
            throw new InvalidOperationException($"External GL export package '{exportPackageId}' is already certified.");
        }

        if (package.Certification.State != AccountingCertificationStateDto.ReadyForReview)
        {
            throw new InvalidOperationException(
                $"External GL export package '{exportPackageId}' must be ready for review before certification.");
        }

        if (package.ValidationIssues.Any(static issue =>
                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException(
                $"External GL export package '{exportPackageId}' has critical validation issues and cannot be certified.");
        }

        var currentValidationIssues = await BuildCurrentExportCertificationIssuesAsync(package, ct).ConfigureAwait(false);
        if (currentValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            var issueCodes = string.Join(", ", currentValidationIssues
                .Where(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
                .Select(static issue => issue.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"External GL export package '{exportPackageId}' has current mapping or reconciliation blockers and cannot be certified: {issueCodes}.");
        }

        var mergedEvidenceLinks = NormalizeEvidenceReferences(package.EvidenceLinks.Concat(evidenceLinks));
        var certification = package.Certification with
        {
            State = AccountingCertificationStateDto.Certified,
            Actor = actor,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Summary = notes,
            EvidenceLinks = NormalizeEvidenceReferences(package.Certification.EvidenceLinks.Concat(evidenceLinks))
        };
        var certified = package with
        {
            PostingEnabled = false,
            PostingDisabledReason = "Certified guarded export artifact only; live external GL posting remains disabled until a separately approved adapter and release gate publish Meridian-owned ledger entries.",
            EvidenceLinks = mergedEvidenceLinks,
            Certification = certification,
            ReconciliationSafeguardState = ResolveReconciliationSafeguardState(
                package.ReconciliationId,
                package.ReconciliationSnapshotHash,
                package.ReconciliationSafeguardIssueCodes,
                certification.State)
        };
        _exportPackages[certified.ExportPackageId] = certified;
        return certified;
    }

    public async Task<ExternalGlExportPackageManifestDto?> GetExportPackageManifestAsync(
        string exportPackageId,
        string? tenantId = null,
        string? companyId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedExportPackageId = RequireText(exportPackageId, nameof(exportPackageId));
        if (!TryGetExportPackage(normalizedExportPackageId, tenantId, companyId, out var package))
        {
            return null;
        }

        var currentValidationIssues = await BuildCurrentExportCertificationIssuesAsync(package, ct).ConfigureAwait(false);
        return BuildExportPackageManifest(package, currentValidationIssues);
    }

    public Task<IReadOnlyList<ExternalGlExportPackageDto>> ListExportPackagesAsync(
        string? providerId = null,
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        AccountingCertificationStateDto? certificationState = null,
        string? tenantId = null,
        string? companyId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedProviderId = string.IsNullOrWhiteSpace(providerId) ? null : NormalizeProviderId(providerId);
        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);

        var rows = _exportPackages.Values
            .Where(package => normalizedProviderId is null || string.Equals(package.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase))
            .Where(package => normalizedFundProfileId is null || string.Equals(package.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(package => ledgerBookId is null || package.LedgerBookId == ledgerBookId)
            .Where(package => normalizedTenantId is null || string.Equals(package.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(package => normalizedCompanyId is null || string.Equals(package.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .Where(package => certificationState is null || package.Certification?.State == certificationState)
            .OrderByDescending(static package => package.CreatedAtUtc)
            .ThenBy(static package => package.ExportPackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ExternalGlExportPackageDto>>(rows);
    }

    public async Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        request ??= new AccountingSystemImportRequestDto();
        var provider = await ResolveProviderAsync(request.ProviderId, ct).ConfigureAwait(false);
        var detail = await provider.ImportAsync(request, ct).ConfigureAwait(false);
        var tenantId = NormalizeOptional(request.TenantId);
        var companyId = NormalizeOptional(request.CompanyId);
        detail = NormalizeImportedDetail(provider, request, detail, tenantId, companyId);

        if (request.PersistPreview)
        {
            _latestImports[ImportKey(detail.Summary.ProviderId, detail.Summary.FundProfileId, detail.Summary.LedgerBookId, tenantId, companyId)] = detail;
        }

        return detail;
    }

    public async Task<AccountingSystemImportDetailDto> GetLatestImportAsync(
        string? providerId = null,
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedProviderId = await ResolveProviderIdAsync(providerId, ct).ConfigureAwait(false);
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        if (_latestImports.TryGetValue(ImportKey(normalizedProviderId, normalizedFundProfileId, ledgerBookId, normalizedTenantId, normalizedCompanyId), out var detail))
        {
            return detail;
        }

        return await ImportAsync(
            new AccountingSystemImportRequestDto(
                normalizedProviderId,
                normalizedFundProfileId,
                ledgerBookId,
                PersistPreview: true,
                TenantId: normalizedTenantId,
                CompanyId: normalizedCompanyId),
            ct).ConfigureAwait(false);
    }

    public async Task<AccountingSystemReconciliationSummaryDto> ReconcileLatestAsync(
        string? providerId = null,
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var latest = await GetLatestImportAsync(providerId, fundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
        var meridianTotals = await LoadMeridianTotalsAsync(latest.Summary, ct).ConfigureAwait(false);
        var externalRows = latest.TrialBalance.ToDictionary(static row => row.AccountCode, StringComparer.OrdinalIgnoreCase);
        var accountCodes = externalRows.Keys.Concat(meridianTotals.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var rows = new List<AccountingSystemReconciliationRowDto>(accountCodes.Length);

        foreach (var accountCode in accountCodes)
        {
            externalRows.TryGetValue(accountCode, out var external);
            meridianTotals.TryGetValue(accountCode, out var meridian);
            var externalDebit = external?.Debit ?? 0m;
            var externalCredit = external?.Credit ?? 0m;
            var meridianDebit = meridian?.Debit ?? 0m;
            var meridianCredit = meridian?.Credit ?? 0m;
            var variance = (externalDebit - externalCredit) - (meridianDebit - meridianCredit);
            var status = ResolveStatus(external, meridian is not null, variance);
            var rowExternalEvidenceReferences = NormalizeEvidenceReferences([external?.EvidenceRef]);
            var meridianEvidenceReferences = meridian is null ? [] : NormalizeEvidenceReferences(meridian.EvidenceReferences);
            var rowEvidenceReferences = NormalizeEvidenceReferences(rowExternalEvidenceReferences.Concat(meridianEvidenceReferences));

            rows.Add(new AccountingSystemReconciliationRowDto(
                $"gl-recon-{SanitizeId(accountCode)}",
                accountCode,
                external?.AccountName ?? meridian?.AccountName ?? accountCode,
                external?.Currency ?? meridian?.Currency ?? "USD",
                status,
                externalDebit,
                externalCredit,
                meridianDebit,
                meridianCredit,
                variance,
                BuildDetail(status, variance),
                external?.EvidenceRef)
            {
                ExternalEvidenceReferences = rowExternalEvidenceReferences,
                MeridianEvidenceReferences = meridianEvidenceReferences,
                EvidenceReferences = rowEvidenceReferences
            });
        }

        var externalEvidenceReferences = NormalizeEvidenceReferences(
            latest.Summary.EvidenceReferences.Concat(latest.TrialBalance.Select(static row => row.EvidenceRef)));
        var meridianSummaryEvidenceReferences = NormalizeEvidenceReferences(
            meridianTotals.Values.SelectMany(static total => total.EvidenceReferences));
        var summaryEvidenceReferences = NormalizeEvidenceReferences(externalEvidenceReferences.Concat(meridianSummaryEvidenceReferences));
        var breakCounts = AccountingSystemReconciliationBreakCounts.FromRows(rows);

        return new AccountingSystemReconciliationSummaryDto(
            $"gl-recon-{latest.Summary.ImportId}",
            latest.Summary.ImportId,
            latest.Summary.ProviderId,
            latest.Summary.FundProfileId,
            latest.Summary.PeriodStart,
            latest.Summary.PeriodEnd,
            DateTimeOffset.UtcNow,
            rows.Count(static row => row.Status == AccountingSystemReconciliationStatusDto.Matched),
            breakCounts.Total,
            latest.TrialBalance.Sum(static row => row.Debit),
            latest.TrialBalance.Sum(static row => row.Credit),
            meridianTotals.Values.Sum(static row => row.Debit),
            meridianTotals.Values.Sum(static row => row.Credit),
            PostingEnabled: false,
            PostingDisabledReason: "Meridian is the source of all ledger truth; external GL posting/export is disabled until an approved adapter publishes Meridian-owned ledger entries.",
            rows,
            summaryEvidenceReferences,
            latest.Summary.LedgerBookId,
            latest.Summary.ContentHash)
        {
            EvidencePackages = BuildEvidencePackages(
                latest,
                externalEvidenceReferences,
                meridianSummaryEvidenceReferences,
                summaryEvidenceReferences,
                breakCounts,
                rows.Count)
        };
    }

    private ScopedExternalGlMappingProfile? ResolveMappingProfile(
        string providerId,
        string fundProfileId,
        Guid? ledgerBookId,
        string? mappingProfileId,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var scopedCandidates = _mappingProfiles.Values
            .Where(record => string.Equals(record.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Where(record => string.Equals(record.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(record => record.LedgerBookId == ledgerBookId)
            .Where(record => normalizedTenantId is null || string.Equals(record.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(record => normalizedCompanyId is null || string.Equals(record.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase));
        var candidates = scopedCandidates.Any() || ledgerBookId is null
            ? scopedCandidates
            : _mappingProfiles.Values
                .Where(record => string.Equals(record.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .Where(record => string.Equals(record.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
                .Where(static record => record.LedgerBookId is null)
                .Where(record => normalizedTenantId is null || string.Equals(record.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase))
                .Where(record => normalizedCompanyId is null || string.Equals(record.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(mappingProfileId))
        {
            return candidates.FirstOrDefault(record => string.Equals(record.Profile.ProfileId, mappingProfileId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return candidates
            .OrderByDescending(static record => record.Profile.CertificationState == AccountingCertificationStateDto.Certified)
            .ThenByDescending(static record => record.Profile.UpdatedAtUtc)
            .FirstOrDefault();
    }

    private async Task<AccountingSystemReconciliationSummaryDto?> TryReconcileLatestAsync(
        string providerId,
        string fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct,
        string? tenantId = null,
        string? companyId = null)
    {
        if (!_providers.Any(provider => string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        try
        {
            return await ReconcileLatestAsync(providerId, fundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (
            ledgerBookId.HasValue &&
            ex.Message.Contains("returned ledger book", StringComparison.OrdinalIgnoreCase))
        {
            return await ReconcileLatestAsync(providerId, fundProfileId, null, ct, tenantId, companyId).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> BuildCurrentExportCertificationIssuesAsync(
        ExternalGlExportPackageDto package,
        CancellationToken ct)
    {
        var mappingProfile = ResolveMappingProfile(
            package.ProviderId,
            package.FundProfileId,
            package.LedgerBookId,
            package.MappingProfileId,
            package.TenantId,
            package.CompanyId);
        var reconciliation = await TryReconcileLatestAsync(
            package.ProviderId,
            package.FundProfileId,
            package.LedgerBookId,
            ct,
            package.TenantId,
            package.CompanyId).ConfigureAwait(false);
        var generatedLines = BuildGeneratedExportLines(mappingProfile, reconciliation, package.LedgerBookId);
        var currentReconciliationSnapshotHash = ComputeReconciliationSnapshotHash(reconciliation);

        return BuildExportValidationIssues(
            package.ProviderId,
            package.FundProfileId,
            ProviderSupportsPosting(package.ProviderId),
            package.LedgerBookId,
            mappingProfile,
            reconciliation,
            package.PeriodStart,
            package.PeriodEnd,
            package.RequireBalancedReconciliation,
            package.EvidenceLinks,
            generatedLines,
            package.ReconciliationId,
            package.ReconciliationSnapshotHash,
            currentReconciliationSnapshotHash);
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildExportValidationIssues(
        string providerId,
        string fundProfileId,
        bool providerSupportsPosting,
        Guid? exportLedgerBookId,
        ScopedExternalGlMappingProfile? mappingProfile,
        AccountingSystemReconciliationSummaryDto? reconciliation,
        DateOnly periodStart,
        DateOnly periodEnd,
        bool requireBalancedReconciliation,
        IReadOnlyList<string> requestEvidenceLinks,
        IReadOnlyList<ExternalGlExportLineDto> generatedLines,
        string? packageReconciliationId = null,
        string? packageReconciliationSnapshotHash = null,
        string? currentReconciliationSnapshotHash = null)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var mappingProfileLedgerBookMatchesExport = exportLedgerBookId is null ||
            (mappingProfile is not null && mappingProfile.LedgerBookId == exportLedgerBookId);
        if (providerSupportsPosting)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "LiveExternalPostingProviderEnabled",
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL provider advertises live posting capability, so guarded export review cannot proceed under the import-first policy.",
                providerId,
                "Disable live posting capability or register a read-only import/reconciliation provider before creating or certifying guarded export packages."));
        }

        if (exportLedgerBookId is null)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "MissingExternalGlExportLedgerBookScope",
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL export packages must target an explicit Meridian ledger book before review.",
                providerId,
                "Select the Meridian ledger book, import external GL evidence for that same book, and recreate the guarded export package before certification."));
        }

        if (!HasExportPackageControlEvidence(requestEvidenceLinks))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "MissingExternalGlExportControlEvidence",
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL export packages require retained export-control evidence before review.",
                providerId,
                "Attach retained export package, approval, certification, sign-off, or review evidence before creating a review-ready export artifact."));
        }
        else if (!HasExportPackageControlEvidenceWithProvenance(providerId, fundProfileId, exportLedgerBookId, periodStart, periodEnd, requestEvidenceLinks))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "UnscopedExternalGlExportControlEvidence",
                AccountingConfigurationValidationSeverityDto.Critical,
                "External GL export-control evidence must identify export-control intent, the export ledger book, and the export fund, provider/fund scope, or exact export period on the same evidence artifact before review.",
                providerId,
                "Attach retained export-control approval evidence that references the ledger book plus fund, provider/fund mapping scope, or exact export period on the same artifact before creating a review-ready export artifact."));
        }

        if (mappingProfile is null)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "MissingExternalGlMappingProfile",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"No external GL mapping profile is configured for provider '{providerId}'.",
                providerId,
                "Create and certify an account/dimension mapping profile before producing export packages."));
        }
        else if (mappingProfile.Profile.CertificationState != AccountingCertificationStateDto.Certified)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "UncertifiedExternalGlMappingProfile",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"External GL mapping profile '{mappingProfile.Profile.ProfileId}' is not certified.",
                mappingProfile.Profile.ProfileId,
                "Submit the mapping profile for approval and retain certification evidence before export certification."));
        }
        else
        {
            if (exportLedgerBookId is Guid scopedLedgerBookId &&
                mappingProfile.LedgerBookId != scopedLedgerBookId)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ExternalGlMappingProfileLedgerBookMismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"External GL mapping profile '{mappingProfile.Profile.ProfileId}' targets ledger book '{mappingProfile.LedgerBookId?.ToString("D") ?? "unscoped"}', not export package ledger book '{scopedLedgerBookId:D}'.",
                    mappingProfile.Profile.ProfileId,
                    "Certify an external GL mapping profile for the selected ledger book before creating or certifying the guarded export package."));
            }

            if (mappingProfile.Profile.AccountMappings.Count == 0)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "MissingExternalGlAccountMappings",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"External GL mapping profile '{mappingProfile.Profile.ProfileId}' has no account mappings.",
                    mappingProfile.Profile.ProfileId,
                    "Map Meridian GL account paths to external GL account identifiers before export certification."));
            }
            else if (reconciliation is not null)
            {
                foreach (var row in reconciliation.Rows
                    .Where(static row => !string.IsNullOrWhiteSpace(row.AccountCode))
                    .Where(row => !mappingProfile.Profile.AccountMappings.ContainsKey(row.AccountCode))
                    .OrderBy(static row => row.AccountCode, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "MissingExternalGlAccountMapping",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"External GL mapping profile '{mappingProfile.Profile.ProfileId}' does not map reconciled account '{row.AccountCode}'.",
                        row.AccountCode,
                        "Map every reconciled Meridian/external GL account in the export period before export certification."));
                }
            }

            var dimensionMappings = mappingProfile.Profile.DimensionMappings ?? [];
            if (dimensionMappings.Count == 0)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "MissingExternalGlDimensionMappings",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"External GL mapping profile '{mappingProfile.Profile.ProfileId}' has no dimension mappings.",
                    mappingProfile.Profile.ProfileId,
                    "Map Meridian canonical accounting dimensions to external GL dimensions before export certification."));
            }
            else
            {
                foreach (var mapping in dimensionMappings.Where(static mapping => mapping.CertificationState != AccountingCertificationStateDto.Certified))
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "UncertifiedExternalGlDimensionMapping",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"External GL dimension mapping '{mapping.ProfileId}' is not certified.",
                        mapping.ProfileId,
                        "Certify every external GL dimension mapping before export certification."));
                }

                foreach (var mapping in dimensionMappings.Where(static mapping =>
                    !HasRequiredExternalGlDimensionScope(mapping.MeridianDimensions) ||
                    !HasRequiredExternalGlDimensionScope(mapping.ExternalDimensions)))
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        "IncompleteExternalGlDimensionMapping",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"External GL dimension mapping '{mapping.ProfileId}' is missing canonical accounting or external GL dimensional scope.",
                        mapping.ProfileId,
                        "Map fund, entity, ledger book, operating/investment dimensions, customer, vendor, project, and external GL dimensions on both sides before export certification."));
                }
            }
        }

        if (reconciliation is null)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "MissingExternalGlReconciliation",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Provider '{providerId}' has no available import/reconciliation evidence.",
                providerId,
                "Import chart, journal, and trial-balance evidence and reconcile it against Meridian ledger truth before export certification."));
        }
        else
        {
            if (reconciliation.LedgerBookId != exportLedgerBookId)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ExternalGlReconciliationLedgerBookMismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"External GL reconciliation '{reconciliation.ReconciliationId}' targets ledger book '{reconciliation.LedgerBookId?.ToString("D") ?? "unscoped"}', not export package ledger book '{exportLedgerBookId?.ToString("D") ?? "unscoped"}'.",
                    reconciliation.ReconciliationId,
                    "Import and reconcile external GL evidence for the same ledger book before creating or certifying the guarded export package."));
            }

            if (reconciliation.PeriodStart != periodStart || reconciliation.PeriodEnd != periodEnd)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ExternalGlReconciliationPeriodMismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"External GL reconciliation '{reconciliation.ReconciliationId}' covers {reconciliation.PeriodStart:yyyy-MM-dd} through {reconciliation.PeriodEnd:yyyy-MM-dd}, not export period {periodStart:yyyy-MM-dd} through {periodEnd:yyyy-MM-dd}.",
                    reconciliation.ReconciliationId,
                    "Import and reconcile external GL evidence for the exact export period before certification."));
            }

            if (!string.IsNullOrWhiteSpace(packageReconciliationId) &&
                !string.Equals(packageReconciliationId, reconciliation.ReconciliationId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ExternalGlReconciliationSnapshotChanged",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"External GL reconciliation snapshot changed from '{packageReconciliationId}' to '{reconciliation.ReconciliationId}' after export package creation.",
                    reconciliation.ReconciliationId,
                    "Recreate the guarded export package from the latest retained reconciliation before certification."));
            }

            if (!string.IsNullOrWhiteSpace(packageReconciliationSnapshotHash) &&
                !string.IsNullOrWhiteSpace(currentReconciliationSnapshotHash) &&
                !string.Equals(packageReconciliationSnapshotHash, currentReconciliationSnapshotHash, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "ExternalGlReconciliationSnapshotChanged",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "External GL reconciliation snapshot content changed after export package creation.",
                    reconciliation.ReconciliationId,
                    "Recreate the guarded export package from the latest retained reconciliation before certification."));
            }

            if (requireBalancedReconciliation && reconciliation.BreakCount > 0)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "UnresolvedExternalGlBreaks",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"{reconciliation.BreakCount} external GL reconciliation break(s) remain unresolved.",
                    reconciliation.ReconciliationId,
                    "Resolve or approve GL tie-out breaks with retained evidence before export certification."));
            }

            if (mappingProfile?.Profile.CertificationState == AccountingCertificationStateDto.Certified &&
                mappingProfileLedgerBookMatchesExport &&
                generatedLines.Count == 0)
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "MissingGeneratedExternalGlExportLines",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"External GL export package for provider '{providerId}' has no generated Meridian-owned export lines.",
                    reconciliation.ReconciliationId,
                    "Load Meridian ledger journal evidence for the export period and map those accounts before export certification."));
            }
        }

        issues.Add(new AccountingConfigurationValidationIssueDto(
            "LiveExternalPostingDisabled",
            AccountingConfigurationValidationSeverityDto.Info,
            "Live external GL posting is disabled; this operation only creates a guarded export artifact.",
            providerId,
            "Review, approve, and reconcile the export artifact outside Meridian until a later live-posting adapter is explicitly approved."));

        return issues;
    }

    private static IReadOnlyList<string> BuildReconciliationSafeguardIssueCodes(
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues)
        => validationIssues
            .Where(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(static issue => issue.Code)
            .Where(static code => ExternalGlReconciliationSafeguardIssueCodes.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private bool ProviderSupportsPosting(string providerId)
        => _providers.Any(provider =>
            string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
            provider.Capabilities.SupportsPosting);

    private static ExternalGlExportReconciliationSafeguardStateDto ResolveReconciliationSafeguardState(
        string? reconciliationId,
        string? reconciliationSnapshotHash,
        IReadOnlyList<string> safeguardIssueCodes,
        AccountingCertificationStateDto certificationState)
    {
        if (string.IsNullOrWhiteSpace(reconciliationId) ||
            string.IsNullOrWhiteSpace(reconciliationSnapshotHash))
        {
            return ExternalGlExportReconciliationSafeguardStateDto.MissingEvidence;
        }

        if (safeguardIssueCodes.Count > 0)
        {
            return ExternalGlExportReconciliationSafeguardStateDto.Blocked;
        }

        return certificationState == AccountingCertificationStateDto.Certified
            ? ExternalGlExportReconciliationSafeguardStateDto.Certified
            : ExternalGlExportReconciliationSafeguardStateDto.Ready;
    }

    private static bool HasRequiredExternalGlDimensionScope(LedgerDimensionSetDto? dimensions)
        => dimensions is not null &&
           !string.IsNullOrWhiteSpace(dimensions.FundId) &&
           !string.IsNullOrWhiteSpace(dimensions.EntityId) &&
           !string.IsNullOrWhiteSpace(dimensions.SleeveId) &&
           !string.IsNullOrWhiteSpace(dimensions.StrategyId) &&
           !string.IsNullOrWhiteSpace(dimensions.InvestorId) &&
           !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId) &&
           dimensions.InstrumentId.HasValue &&
           !string.IsNullOrWhiteSpace(dimensions.TaxLotId) &&
           !string.IsNullOrWhiteSpace(dimensions.CostCenterId) &&
           !string.IsNullOrWhiteSpace(dimensions.CounterpartyId) &&
           !string.IsNullOrWhiteSpace(dimensions.OrganizationId) &&
           !string.IsNullOrWhiteSpace(dimensions.PortfolioId) &&
           !string.IsNullOrWhiteSpace(dimensions.BookId) &&
           !string.IsNullOrWhiteSpace(dimensions.AccountId) &&
           !string.IsNullOrWhiteSpace(dimensions.CustomerId) &&
           !string.IsNullOrWhiteSpace(dimensions.VendorId) &&
           !string.IsNullOrWhiteSpace(dimensions.ProjectId) &&
           dimensions.ExternalGlDimensions.Count > 0 &&
           dimensions.ExternalGlDimensions.All(static pair =>
               !string.IsNullOrWhiteSpace(pair.Key) &&
               !string.IsNullOrWhiteSpace(pair.Value));

    private static IReadOnlyList<ExternalGlExportLineDto> BuildGeneratedExportLines(
        ScopedExternalGlMappingProfile? mappingProfile,
        AccountingSystemReconciliationSummaryDto? reconciliation,
        Guid? exportLedgerBookId)
    {
        if (mappingProfile is null ||
            mappingProfile.Profile.CertificationState != AccountingCertificationStateDto.Certified ||
            reconciliation is null ||
            (exportLedgerBookId is not null && mappingProfile.LedgerBookId != exportLedgerBookId) ||
            (exportLedgerBookId is not null && reconciliation.LedgerBookId != exportLedgerBookId))
        {
            return [];
        }

        var dimensionMapping = ResolveCertifiedCompleteDimensionMapping(mappingProfile.Profile);
        if (dimensionMapping is null)
        {
            return [];
        }

        return reconciliation.Rows
            .Where(static row => row.MeridianDebit != 0m || row.MeridianCredit != 0m)
            .Where(row => mappingProfile.Profile.AccountMappings.ContainsKey(row.AccountCode))
            .OrderBy(static row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
            .Select(row => new ExternalGlExportLineDto(
                $"external-gl-export-line-{SanitizeId(reconciliation.ReconciliationId)}-{SanitizeId(row.AccountCode)}",
                row.RowId,
                row.Status,
                row.AccountCode,
                mappingProfile.Profile.AccountMappings[row.AccountCode],
                row.AccountName,
                row.Currency,
                row.MeridianDebit,
                row.MeridianCredit,
                row.MeridianDebit - row.MeridianCredit,
                dimensionMapping?.MeridianDimensions,
                dimensionMapping?.ExternalDimensions,
                row.EvidenceReferences))
            .ToArray();
    }

    private static DimensionMappingProfileDto? ResolveCertifiedCompleteDimensionMapping(
        ExternalGlMappingProfileDto profile)
    {
        var dimensionMappings = profile.DimensionMappings ?? [];
        if (dimensionMappings.Count == 0 ||
            dimensionMappings.Any(static mapping =>
                mapping.CertificationState != AccountingCertificationStateDto.Certified ||
                !HasRequiredExternalGlDimensionScope(mapping.MeridianDimensions) ||
                !HasRequiredExternalGlDimensionScope(mapping.ExternalDimensions)))
        {
            return null;
        }

        return dimensionMappings[0];
    }

    private static IReadOnlyList<string> BuildExportEvidenceLinks(
        AccountingSystemExportPackageRequestDto request,
        ScopedExternalGlMappingProfile? mappingProfile,
        AccountingSystemReconciliationSummaryDto? reconciliation)
    {
        var evidence = new List<string>();
        evidence.AddRange(request.EvidenceLinks);
        if (mappingProfile is not null)
        {
            evidence.Add($"external-gl-mapping-profile:{mappingProfile.Profile.ProfileId}");
            evidence.AddRange(mappingProfile.EvidenceLinks);
        }

        if (reconciliation is not null)
        {
            evidence.Add($"external-gl-reconciliation:{reconciliation.ReconciliationId}");
            evidence.AddRange(reconciliation.EvidenceReferences);
            evidence.AddRange(reconciliation.EvidencePackages.Select(static package => package.PackageId));
        }

        return NormalizeEvidenceReferences(evidence);
    }

    private static ExternalGlExportPackageManifestDto BuildExportPackageManifest(
        ExternalGlExportPackageDto package,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> currentValidationIssues)
    {
        var manifestPostingDisabledReason = ResolveManifestPostingDisabledReason(package);
        var validationIssues = MergeExportManifestValidationIssues(
            package.ValidationIssues,
            currentValidationIssues,
            BuildRetainedPostingStateIssues(package));
        var safeguardIssueCodes = BuildReconciliationSafeguardIssueCodes(validationIssues);
        var certificationState = package.Certification?.State ?? AccountingCertificationStateDto.Draft;
        var safeguardState = ResolveReconciliationSafeguardState(
            package.ReconciliationId,
            package.ReconciliationSnapshotHash,
            safeguardIssueCodes,
            certificationState);
        var evidenceLinks = NormalizeEvidenceReferences(
            package.EvidenceLinks.Concat(package.Certification?.EvidenceLinks ?? []));
        var payload = JsonSerializer.Serialize(
            new
            {
                package.ExportPackageId,
                package.ProviderId,
                package.FundProfileId,
                package.LedgerBookId,
                package.TenantId,
                package.CompanyId,
                package.PeriodStart,
                package.PeriodEnd,
                package.MappingProfileId,
                package.ReconciliationId,
                package.ReconciliationSnapshotHash,
                package.RequireBalancedReconciliation,
                ReconciliationSafeguardState = safeguardState,
                ReconciliationSafeguardIssueCodes = safeguardIssueCodes,
                certificationState,
                PostingEnabled = false,
                PostingDisabledReason = manifestPostingDisabledReason,
                generatedLineCount = package.GeneratedLines.Count,
                generatedLines = package.GeneratedLines,
                evidenceLinks,
                validationIssues
            },
            JsonOptions);
        var contentHash = ComputeExportPackageHash(
            package,
            certificationState,
            evidenceLinks,
            validationIssues,
            safeguardState,
            safeguardIssueCodes,
            postingEnabled: false,
            manifestPostingDisabledReason);
        return new ExternalGlExportPackageManifestDto(
            package.ExportPackageId,
            package.ProviderId,
            package.FundProfileId,
            package.LedgerBookId,
            package.PeriodStart,
            package.PeriodEnd,
            certificationState,
            package.Certification?.RecordedAtUtc ?? package.CreatedAtUtc,
            contentHash,
            "application/json",
            $"{SanitizeId(package.ExportPackageId)}.external-gl-export.json",
            ExternalPostingAllowed: false,
            manifestPostingDisabledReason,
            payload,
            package.GeneratedLines,
            evidenceLinks,
            validationIssues,
            package.MappingProfileId,
            package.ReconciliationId,
            package.RequireBalancedReconciliation,
            safeguardState,
            safeguardIssueCodes,
            package.TenantId,
            package.CompanyId,
            package.ReconciliationSnapshotHash);
    }

    private static string ResolveManifestPostingDisabledReason(ExternalGlExportPackageDto package)
        => !package.PostingEnabled && !string.IsNullOrWhiteSpace(package.PostingDisabledReason)
            ? package.PostingDisabledReason
            : "Controlled external GL export manifest only; live external GL posting remains disabled until a separately approved adapter and release gate publish Meridian-owned ledger entries.";

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildRetainedPostingStateIssues(
        ExternalGlExportPackageDto package)
    {
        if (!package.PostingEnabled && !string.IsNullOrWhiteSpace(package.PostingDisabledReason))
        {
            return [];
        }

        return
        [
            new AccountingConfigurationValidationIssueDto(
                "LiveExternalPostingRetainedPackageEnabled",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Retained external GL export package state attempted to enable live posting or removed the posting-disabled reason; manifest output remains posting-disabled.",
                package.ExportPackageId,
                "Recreate the guarded external GL export package from the current import/reconciliation evidence before review or certification.")
        ];
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> MergeExportManifestValidationIssues(
        params IReadOnlyList<AccountingConfigurationValidationIssueDto>[] issueSets)
        => issueSets
            .SelectMany(static issues => issues)
            .GroupBy(
                static issue => string.Join(
                    "\u001f",
                    issue.Code,
                    issue.Severity,
                    issue.TargetId ?? string.Empty,
                    issue.Message,
                    issue.SuggestedAction ?? string.Empty),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static issue => issue.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? ComputeReconciliationSnapshotHash(AccountingSystemReconciliationSummaryDto? reconciliation)
    {
        if (reconciliation is null)
        {
            return null;
        }

        var payload = string.Join(
            "|",
            reconciliation.ReconciliationId,
            reconciliation.ImportId,
            reconciliation.ProviderId,
            reconciliation.FundProfileId,
            reconciliation.LedgerBookId?.ToString("D") ?? string.Empty,
            reconciliation.ImportContentHash ?? string.Empty,
            reconciliation.PeriodStart.ToString("yyyy-MM-dd"),
            reconciliation.PeriodEnd.ToString("yyyy-MM-dd"),
            reconciliation.MatchedCount,
            reconciliation.BreakCount,
            reconciliation.TotalExternalDebits.ToString("0.00", CultureInfo.InvariantCulture),
            reconciliation.TotalExternalCredits.ToString("0.00", CultureInfo.InvariantCulture),
            reconciliation.TotalMeridianDebits.ToString("0.00", CultureInfo.InvariantCulture),
            reconciliation.TotalMeridianCredits.ToString("0.00", CultureInfo.InvariantCulture),
            string.Join(",", reconciliation.Rows
                .OrderBy(static row => row.RowId, StringComparer.OrdinalIgnoreCase)
                .Select(FormatReconciliationRowForHash)),
            string.Join(",", reconciliation.EvidenceReferences.Order(StringComparer.OrdinalIgnoreCase)),
            string.Join(",", reconciliation.EvidencePackages
                .OrderBy(static package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(static package => string.Join(
                    ":",
                    package.PackageId,
                    package.Label,
                    package.Status,
                    package.EvidenceReferenceCount,
                    string.Join("\u001d", package.EvidenceReferences.Order(StringComparer.OrdinalIgnoreCase)),
                    string.Join("\u001d", package.RequiredActions.Order(StringComparer.OrdinalIgnoreCase))))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string ComputeExportPackageHash(
        ExternalGlExportPackageDto package,
        AccountingCertificationStateDto certificationState,
        IReadOnlyList<string> evidenceLinks,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        ExternalGlExportReconciliationSafeguardStateDto safeguardState,
        IReadOnlyList<string> safeguardIssueCodes,
        bool postingEnabled,
        string postingDisabledReason)
    {
        var payload = string.Join(
            "|",
            package.ExportPackageId,
            package.ProviderId,
            package.FundProfileId,
            package.LedgerBookId?.ToString("D") ?? string.Empty,
            package.TenantId ?? string.Empty,
            package.CompanyId ?? string.Empty,
            package.PeriodStart.ToString("yyyy-MM-dd"),
            package.PeriodEnd.ToString("yyyy-MM-dd"),
            package.MappingProfileId ?? string.Empty,
            package.ReconciliationId ?? string.Empty,
            package.ReconciliationSnapshotHash ?? string.Empty,
            package.RequireBalancedReconciliation,
            safeguardState,
            string.Join(",", safeguardIssueCodes.Order(StringComparer.OrdinalIgnoreCase)),
            certificationState,
            postingEnabled,
            postingDisabledReason,
            string.Join(",", package.GeneratedLines
                .OrderBy(static line => line.ExportLineId, StringComparer.OrdinalIgnoreCase)
                .Select(FormatGeneratedExportLineForHash)),
            string.Join(",", validationIssues
                .OrderBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static issue => issue.TargetId, StringComparer.OrdinalIgnoreCase)
                .Select(static issue => $"{issue.Code}:{issue.Severity}:{issue.TargetId}:{issue.Message}:{issue.SuggestedAction}")),
            string.Join(",", evidenceLinks.Order(StringComparer.OrdinalIgnoreCase)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string FormatReconciliationRowForHash(AccountingSystemReconciliationRowDto row)
        => string.Join(
            ":",
            row.RowId,
            row.AccountCode,
            row.AccountName,
            row.Currency,
            row.Status,
            row.ExternalDebit.ToString("0.00", CultureInfo.InvariantCulture),
            row.ExternalCredit.ToString("0.00", CultureInfo.InvariantCulture),
            row.MeridianDebit.ToString("0.00", CultureInfo.InvariantCulture),
            row.MeridianCredit.ToString("0.00", CultureInfo.InvariantCulture),
            row.Variance.ToString("0.00", CultureInfo.InvariantCulture),
            row.Detail,
            row.EvidenceRef ?? string.Empty,
            string.Join(",", row.ExternalEvidenceReferences.Order(StringComparer.OrdinalIgnoreCase)),
            string.Join(",", row.MeridianEvidenceReferences.Order(StringComparer.OrdinalIgnoreCase)),
            string.Join(",", row.EvidenceReferences.Order(StringComparer.OrdinalIgnoreCase)));

    private static string FormatGeneratedExportLineForHash(ExternalGlExportLineDto line)
        => string.Join(
            ":",
            line.ExportLineId,
            line.ReconciliationRowId,
            line.SourceStatus,
            line.MeridianAccountCode,
            line.ExternalAccountId,
            line.AccountName,
            line.Currency,
            line.Debit.ToString("0.00", CultureInfo.InvariantCulture),
            line.Credit.ToString("0.00", CultureInfo.InvariantCulture),
            line.NetAmount.ToString("0.00", CultureInfo.InvariantCulture),
            FormatDimensionsForHash(line.MeridianDimensions),
            FormatDimensionsForHash(line.ExternalDimensions),
            string.Join(",", line.EvidenceLinks.Order(StringComparer.OrdinalIgnoreCase)));

    private static string FormatDimensionsForHash(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return string.Empty;
        }

        var externalGl = dimensions.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{pair.Key.Trim()}={pair.Value.Trim()}");
        var signature = string.Join(
            "\u001e",
            dimensions.FundId ?? string.Empty,
            dimensions.EntityId ?? string.Empty,
            dimensions.SleeveId ?? string.Empty,
            dimensions.StrategyId ?? string.Empty,
            dimensions.InvestorId ?? string.Empty,
            dimensions.CapitalAccountId ?? string.Empty,
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            dimensions.TaxLotId ?? string.Empty,
            dimensions.CostCenterId ?? string.Empty,
            dimensions.CounterpartyId ?? string.Empty,
            string.Join("\u001d", externalGl),
            dimensions.OrganizationId ?? string.Empty,
            dimensions.PortfolioId ?? string.Empty,
            dimensions.BookId ?? string.Empty,
            dimensions.AccountId ?? string.Empty,
            dimensions.CustomerId ?? string.Empty,
            dimensions.VendorId ?? string.Empty,
            dimensions.ProjectId ?? string.Empty);

        return dimensions.PositionId.HasValue
            ? $"{signature}\u001epositionId={dimensions.PositionId.Value:D}"
            : signature;
    }

    private static AccountingSystemImportDetailDto NormalizeImportedDetail(
        IAccountingSystemProvider provider,
        AccountingSystemImportRequestDto request,
        AccountingSystemImportDetailDto detail,
        string? tenantId,
        string? companyId)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(detail.Summary);

        var providerId = NormalizeProviderId(provider.ProviderId);
        var summaryProviderId = NormalizeProviderId(detail.Summary.ProviderId);
        if (!string.Equals(providerId, summaryProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Accounting-system provider '{providerId}' returned import evidence for provider '{summaryProviderId}'.");
        }

        var requestedFundProfileId = string.IsNullOrWhiteSpace(request.FundProfileId)
            ? null
            : NormalizeFundProfileId(request.FundProfileId);
        var summaryFundProfileId = NormalizeFundProfileId(detail.Summary.FundProfileId);
        if (requestedFundProfileId is not null &&
            !string.Equals(requestedFundProfileId, summaryFundProfileId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Accounting-system provider '{providerId}' returned fund profile '{summaryFundProfileId}' for requested fund profile '{requestedFundProfileId}'.");
        }

        if (request.LedgerBookId.HasValue && detail.Summary.LedgerBookId != request.LedgerBookId)
        {
            throw new InvalidOperationException(
                $"Accounting-system provider '{providerId}' returned ledger book '{detail.Summary.LedgerBookId?.ToString("D") ?? "unscoped"}' for requested ledger book '{request.LedgerBookId.Value:D}'.");
        }

        if (request.PeriodStart.HasValue && detail.Summary.PeriodStart != request.PeriodStart.Value)
        {
            throw new InvalidOperationException(
                $"Accounting-system provider '{providerId}' returned period start {detail.Summary.PeriodStart:yyyy-MM-dd} for requested period start {request.PeriodStart.Value:yyyy-MM-dd}.");
        }

        if (request.PeriodEnd.HasValue && detail.Summary.PeriodEnd != request.PeriodEnd.Value)
        {
            throw new InvalidOperationException(
                $"Accounting-system provider '{providerId}' returned period end {detail.Summary.PeriodEnd:yyyy-MM-dd} for requested period end {request.PeriodEnd.Value:yyyy-MM-dd}.");
        }

        if (detail.Summary.PeriodEnd < detail.Summary.PeriodStart)
        {
            throw new InvalidOperationException(
                $"Accounting-system provider '{providerId}' returned an invalid import period ending before it starts.");
        }

        var chartAccounts = detail.ChartAccounts ?? [];
        var journalEntries = detail.JournalEntries ?? [];
        var trialBalance = detail.TrialBalance ?? [];
        EnsureImportCountMatches(providerId, "chart account", detail.Summary.ChartAccountCount, chartAccounts.Count);
        EnsureImportCountMatches(providerId, "journal entry", detail.Summary.JournalEntryCount, journalEntries.Count);
        EnsureImportCountMatches(providerId, "trial-balance line", detail.Summary.TrialBalanceLineCount, trialBalance.Count);
        EnsureBalancedJournalEvidence(providerId, journalEntries);

        var evidenceReferences = NormalizeEvidenceReferences(detail.Summary.EvidenceReferences);
        var warnings = NormalizeEvidenceReferences(detail.Summary.Warnings);
        var normalizedSummary = detail.Summary with
        {
            ProviderId = providerId,
            FundProfileId = summaryFundProfileId,
            State = request.PersistPreview
                ? AccountingSystemImportStateDto.Imported
                : AccountingSystemImportStateDto.Previewed,
            ChartAccountCount = chartAccounts.Count,
            JournalEntryCount = journalEntries.Count,
            TrialBalanceLineCount = trialBalance.Count,
            EvidenceReferences = evidenceReferences,
            Warnings = warnings,
            TenantId = tenantId,
            CompanyId = companyId
        };

        normalizedSummary = normalizedSummary with
        {
            ContentHash = ComputeImportContentHash(
                normalizedSummary,
                chartAccounts,
                journalEntries,
                trialBalance)
        };

        return detail with
        {
            Summary = normalizedSummary,
            ChartAccounts = chartAccounts,
            JournalEntries = journalEntries,
            TrialBalance = trialBalance
        };
    }

    private static void EnsureImportCountMatches(
        string providerId,
        string label,
        int summaryCount,
        int actualCount)
    {
        if (summaryCount != actualCount)
        {
            throw new InvalidOperationException(
                $"Accounting-system provider '{providerId}' returned {label} count {summaryCount}, but the payload contains {actualCount} {label}(s).");
        }
    }

    private static void EnsureBalancedJournalEvidence(
        string providerId,
        IReadOnlyList<AccountingSystemJournalEntryDto> journalEntries)
    {
        foreach (var entry in journalEntries)
        {
            var lineDebitTotal = entry.Lines.Sum(static line => line.Debit);
            var lineCreditTotal = entry.Lines.Sum(static line => line.Credit);
            if (Math.Abs(entry.TotalDebits - entry.TotalCredits) > 0.01m ||
                Math.Abs(lineDebitTotal - lineCreditTotal) > 0.01m ||
                Math.Abs(entry.TotalDebits - lineDebitTotal) > 0.01m ||
                Math.Abs(entry.TotalCredits - lineCreditTotal) > 0.01m)
            {
                throw new InvalidOperationException(
                    $"Accounting-system provider '{providerId}' returned unbalanced journal entry '{entry.ExternalJournalEntryId}'.");
            }
        }
    }

    private static string ComputeImportContentHash(
        AccountingSystemImportSummaryDto summary,
        IReadOnlyList<AccountingSystemChartAccountDto> chartAccounts,
        IReadOnlyList<AccountingSystemJournalEntryDto> journalEntries,
        IReadOnlyList<AccountingSystemTrialBalanceLineDto> trialBalance)
    {
        var payload = string.Join(
            "|",
            summary.ImportId,
            summary.ProviderId,
            summary.FundProfileId,
            summary.LedgerBookId?.ToString("D") ?? string.Empty,
            summary.TenantId ?? string.Empty,
            summary.CompanyId ?? string.Empty,
            summary.PeriodStart.ToString("yyyy-MM-dd"),
            summary.PeriodEnd.ToString("yyyy-MM-dd"),
            string.Join(",", summary.EvidenceReferences.Order(StringComparer.OrdinalIgnoreCase)),
            string.Join(",", chartAccounts
                .OrderBy(static account => account.ExternalAccountId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static account => account.AccountCode, StringComparer.OrdinalIgnoreCase)
                .Select(FormatChartAccountForHash)),
            string.Join(",", journalEntries
                .OrderBy(static entry => entry.ExternalJournalEntryId, StringComparer.OrdinalIgnoreCase)
                .Select(FormatJournalEntryForHash)),
            string.Join(",", trialBalance
                .OrderBy(static line => line.ExternalAccountId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static line => line.AccountCode, StringComparer.OrdinalIgnoreCase)
                .Select(FormatTrialBalanceLineForHash)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string FormatChartAccountForHash(AccountingSystemChartAccountDto account)
        => string.Join(
            ":",
            account.ExternalAccountId,
            account.AccountCode,
            account.DisplayName,
            account.AccountType,
            account.Currency,
            account.IsActive,
            account.ParentExternalAccountId ?? string.Empty,
            account.EvidenceRef ?? string.Empty);

    private static string FormatJournalEntryForHash(AccountingSystemJournalEntryDto entry)
        => string.Join(
            ":",
            entry.ExternalJournalEntryId,
            entry.AccountingDate.ToString("yyyy-MM-dd"),
            entry.Description,
            entry.Currency,
            entry.TotalDebits.ToString("0.00", CultureInfo.InvariantCulture),
            entry.TotalCredits.ToString("0.00", CultureInfo.InvariantCulture),
            entry.EvidenceRef ?? string.Empty,
            string.Join("\u001d", entry.Lines
                .OrderBy(static line => line.ExternalLineId, StringComparer.OrdinalIgnoreCase)
                .Select(FormatJournalLineForHash)));

    private static string FormatJournalLineForHash(AccountingSystemJournalLineDto line)
        => string.Join(
            ":",
            line.ExternalLineId,
            line.ExternalAccountId,
            line.AccountCode,
            line.Description,
            line.Debit.ToString("0.00", CultureInfo.InvariantCulture),
            line.Credit.ToString("0.00", CultureInfo.InvariantCulture),
            line.Currency,
            line.EvidenceRef ?? string.Empty);

    private static string FormatTrialBalanceLineForHash(AccountingSystemTrialBalanceLineDto line)
        => string.Join(
            ":",
            line.ExternalAccountId,
            line.AccountCode,
            line.AccountName,
            line.AccountType,
            line.Debit.ToString("0.00", CultureInfo.InvariantCulture),
            line.Credit.ToString("0.00", CultureInfo.InvariantCulture),
            line.Currency,
            line.AsOfDate.ToString("yyyy-MM-dd"),
            line.EvidenceRef ?? string.Empty);

    private async Task<Dictionary<string, MeridianAccountTotal>> LoadMeridianTotalsAsync(
        AccountingSystemImportSummaryDto summary,
        CancellationToken ct)
    {
        var totals = new Dictionary<string, MeridianAccountTotal>(StringComparer.OrdinalIgnoreCase);
        if (_ledgerJournalStore is null)
        {
            return totals;
        }

        var periods = await _ledgerJournalStore.ListPeriodsAsync(
            ledgerBookId: summary.LedgerBookId,
            fundProfileId: summary.FundProfileId,
            ct: ct).ConfigureAwait(false);

        foreach (var period in periods.Where(period => period.StartDate <= summary.PeriodEnd && period.EndDate >= summary.PeriodStart))
        {
            ct.ThrowIfCancellationRequested();
            var entries = await _ledgerJournalStore.GetByPeriodAsync(period.PeriodId, ct).ConfigureAwait(false);
            foreach (var record in entries)
            {
                foreach (var line in record.Entry.Lines)
                {
                    var accountCode = line.Account.Name;
                    if (!totals.TryGetValue(accountCode, out var total))
                    {
                        total = new MeridianAccountTotal(accountCode, "USD");
                        totals[accountCode] = total;
                    }

                    total.Debit += line.Debit;
                    total.Credit += line.Credit;
                    foreach (var evidenceReference in BuildMeridianEvidenceReferences(record, line))
                    {
                        total.EvidenceReferences.Add(evidenceReference);
                    }
                }
            }
        }

        return totals;
    }

    private static IReadOnlyList<AccountingSystemReconciliationEvidencePackageDto> BuildEvidencePackages(
        AccountingSystemImportDetailDto latest,
        IReadOnlyList<string> externalEvidenceReferences,
        IReadOnlyList<string> meridianEvidenceReferences,
        IReadOnlyList<string> summaryEvidenceReferences,
        AccountingSystemReconciliationBreakCounts breakCounts,
        int rowCount)
    {
        return
        [
            new(
                $"gl-external-evidence:{latest.Summary.ImportId}",
                "External GL import evidence",
                externalEvidenceReferences.Count > 0
                    ? AccountingSystemEvidencePackageStatusDto.Ready
                    : AccountingSystemEvidencePackageStatusDto.Missing,
                externalEvidenceReferences.Count,
                externalEvidenceReferences,
                externalEvidenceReferences.Count > 0
                    ? []
                    : ["Import external chart, journal, and trial-balance evidence before relying on GL reconciliation."]),
            new(
                $"gl-meridian-ledger-evidence:{latest.Summary.ImportId}",
                "Meridian ledger evidence",
                meridianEvidenceReferences.Count > 0
                    ? AccountingSystemEvidencePackageStatusDto.Ready
                    : AccountingSystemEvidencePackageStatusDto.Missing,
                meridianEvidenceReferences.Count,
                meridianEvidenceReferences,
                meridianEvidenceReferences.Count > 0
                    ? []
                    : ["Load Meridian ledger journal evidence for the fund, book, and period before close approval."]),
            new(
                $"gl-reconciliation-tie-out:{latest.Summary.ImportId}",
                "GL reconciliation tie-out",
                ResolveTieOutPackageStatus(rowCount, breakCounts.Total, externalEvidenceReferences.Count, meridianEvidenceReferences.Count),
                summaryEvidenceReferences.Count,
                summaryEvidenceReferences,
                BuildTieOutRequiredActions(breakCounts, externalEvidenceReferences.Count, meridianEvidenceReferences.Count))
        ];
    }

    private static AccountingSystemEvidencePackageStatusDto ResolveTieOutPackageStatus(
        int rowCount,
        int breakCount,
        int externalEvidenceCount,
        int meridianEvidenceCount)
    {
        if (rowCount == 0 || externalEvidenceCount == 0 || meridianEvidenceCount == 0)
        {
            return AccountingSystemEvidencePackageStatusDto.Missing;
        }

        return breakCount == 0
            ? AccountingSystemEvidencePackageStatusDto.Ready
            : AccountingSystemEvidencePackageStatusDto.ReviewRequired;
    }

    private static IReadOnlyList<string> BuildTieOutRequiredActions(
        AccountingSystemReconciliationBreakCounts breakCounts,
        int externalEvidenceCount,
        int meridianEvidenceCount)
    {
        var actions = new List<string>(6);
        if (externalEvidenceCount == 0)
        {
            actions.Add("Import external accounting-system evidence.");
        }

        if (meridianEvidenceCount == 0)
        {
            actions.Add("Load Meridian ledger journal evidence.");
        }

        if (breakCounts.MissingExternal > 0)
        {
            actions.Add($"{FormatCount(breakCounts.MissingExternal, "Meridian ledger account is", "Meridian ledger accounts are")} absent from the external GL import; assign to accounting operations to retain provider support or approved exclusion evidence.");
        }

        if (breakCounts.MissingMeridian > 0)
        {
            actions.Add($"{FormatCount(breakCounts.MissingMeridian, "external GL row is", "external GL rows are")} absent from Meridian ledger evidence; assign to ledger operations before close approval.");
        }

        if (breakCounts.Variance > 0)
        {
            actions.Add($"{FormatCount(breakCounts.Variance, "GL variance row requires", "GL variance rows require")} break resolution and retained approval evidence before close approval.");
        }

        if (breakCounts.ReviewRequired > 0)
        {
            actions.Add($"{FormatCount(breakCounts.ReviewRequired, "GL row requires", "GL rows require")} manual accounting review before close approval.");
        }

        return actions;
    }

    private static string FormatCount(int count, string singular, string plural)
        => count == 1
            ? $"1 {singular}"
            : $"{count} {plural}";

    private async Task<IAccountingSystemProvider> ResolveProviderAsync(string? providerId, CancellationToken ct)
    {
        var normalized = await ResolveProviderIdAsync(providerId, ct).ConfigureAwait(false);
        return _providers.FirstOrDefault(provider => string.Equals(provider.ProviderId, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Accounting system provider '{normalized}' is not available.");
    }

    private async Task<string> ResolveProviderIdAsync(string? providerId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            return NormalizeProviderId(providerId);
        }

        var quickBooks = _providers.FirstOrDefault(static provider =>
            string.Equals(provider.ProviderId, QuickBooksOnlineProviderId, StringComparison.OrdinalIgnoreCase));
        if (quickBooks is IAccountingSystemConnectionMetadataProvider metadataProvider)
        {
            var metadata = await metadataProvider.GetConnectionMetadataAsync(ct).ConfigureAwait(false);
            if (metadata.HasLocalConfig && metadata.MissingFields.Count == 0)
            {
                return QuickBooksOnlineProviderId;
            }
        }

        return DefaultProviderId;
    }

    private static async Task<AccountingSystemProviderDto> ToProviderDtoAsync(
        IAccountingSystemProvider provider,
        CancellationToken ct)
    {
        AccountingSystemConnectionMetadataDto? metadata = null;
        if (provider is IAccountingSystemConnectionMetadataProvider metadataProvider)
        {
            metadata = await metadataProvider.GetConnectionMetadataAsync(ct).ConfigureAwait(false);
        }

        var state = metadata is { HasLocalConfig: false }
            ? AccountingSystemProviderStateDto.Disabled
            : AccountingSystemProviderStateDto.Available;
        var statusLabel = metadata?.StatusLabel ?? "Ready for read-only import";
        var statusDetail = metadata?.StatusDetail ??
            "Read-only external GL import and reconciliation compare provider evidence against Meridian-owned ledger truth.";

        return new(
            provider.ProviderId,
            provider.DisplayName,
            state,
            provider.Capabilities.RequiresCredentials,
            provider.Capabilities.SupportsChartOfAccounts,
            provider.Capabilities.SupportsJournalEntries,
            provider.Capabilities.SupportsTrialBalance,
            provider.Capabilities.SupportsPosting,
            statusLabel,
            statusDetail,
            provider.Capabilities.EvidenceKinds,
            metadata,
            BuildProviderMappingRequirements(provider.ProviderId));
    }

    private static IReadOnlyList<AccountingSystemProviderMappingRequirementDto> BuildProviderMappingRequirements(
        string providerId)
    {
        var normalized = NormalizeProviderId(providerId);
        var accountEvidenceKind = normalized switch
        {
            "xero" or "xero-fixture" => "XeroAccount",
            "netsuite" or "netsuite-fixture" => "NetSuiteAccount",
            _ => "QuickBooksAccount"
        };
        var journalEvidenceKind = normalized switch
        {
            "xero" or "xero-fixture" => "XeroManualJournal",
            "netsuite" or "netsuite-fixture" => "NetSuiteJournalEntry",
            _ => "QuickBooksJournalEntry"
        };
        var trialBalanceEvidenceKind = normalized switch
        {
            "xero" or "xero-fixture" => "XeroTrialBalance",
            "netsuite" or "netsuite-fixture" => "NetSuiteTrialBalance",
            _ => "QuickBooksTrialBalance"
        };
        var dimensionVocabulary = normalized switch
        {
            "xero" or "xero-fixture" => "Xero tracking categories",
            "netsuite" or "netsuite-fixture" => "NetSuite segments, departments, classes, and subsidiaries",
            _ => "QuickBooks classes, locations, and departments"
        };

        return
        [
            new(
                $"{normalized}:account-mapping",
                "Account mapping",
                accountEvidenceKind,
                "Map every reconciled Meridian GL account to a certified external GL account before guarded export review."),
            new(
                $"{normalized}:journal-lineage",
                "Journal lineage",
                journalEvidenceKind,
                "Retain provider journal evidence and Meridian ledger-entry lineage for the exact fund, book, and export period."),
            new(
                $"{normalized}:trial-balance-tie-out",
                "Trial-balance tie-out",
                trialBalanceEvidenceKind,
                "Reconcile provider trial-balance rows against Meridian-owned ledger totals before certification."),
            new(
                $"{normalized}:dimension-mapping",
                "Dimension mapping",
                $"{accountEvidenceKind}:Dimensions",
                $"Certify canonical Meridian dimensions against {dimensionVocabulary} before generated export lines can be review-ready."),
            .. BuildProviderSpecificMappingRequirements(normalized, accountEvidenceKind, journalEvidenceKind)
        ];
    }

    private static IReadOnlyList<AccountingSystemProviderMappingRequirementDto> BuildProviderSpecificMappingRequirements(
        string normalizedProviderId,
        string accountEvidenceKind,
        string journalEvidenceKind)
        => normalizedProviderId switch
        {
            "xero" or "xero-fixture" =>
            [
                new(
                    $"{normalizedProviderId}:tracking-category-options",
                    "Tracking category options",
                    "XeroTrackingCategory",
                    "Retain and certify Xero tracking category option mappings for fund, entity, strategy, cost center, and external GL dimensions before guarded export review."),
                new(
                    $"{normalizedProviderId}:contact-mapping",
                    "Contact mapping",
                    "XeroContact",
                    "Map Meridian counterparty, customer, vendor, investor, and capital-account dimensions to certified Xero contacts for generated export lines."),
                new(
                    $"{normalizedProviderId}:tax-rate-mapping",
                    "Tax rate mapping",
                    "XeroTaxRate",
                    "Certify Xero tax-rate treatment for taxable journal lines so export reviewers can reconcile tax classification without live posting."),
                new(
                    $"{normalizedProviderId}:bank-account-scope",
                    "Bank account scope",
                    accountEvidenceKind,
                    "Identify Xero bank-account and clearing-account mappings for cash activity, transfers, and reconciliation evidence before export certification.")
            ],
            "netsuite" or "netsuite-fixture" =>
            [
                new(
                    $"{normalizedProviderId}:subsidiary-scope",
                    "Subsidiary scope",
                    "NetSuiteSubsidiary",
                    "Map Meridian fund, entity, and ledger-book scope to certified NetSuite subsidiaries before generated export lines can be review-ready."),
                new(
                    $"{normalizedProviderId}:classification-segments",
                    "Classification segments",
                    "NetSuiteSegment",
                    "Retain certified NetSuite department, class, location, and custom-segment mappings for fund, sleeve, strategy, cost center, and external GL dimensions."),
                new(
                    $"{normalizedProviderId}:entity-mapping",
                    "Entity mapping",
                    "NetSuiteEntity",
                    "Map Meridian counterparty, customer, vendor, investor, and capital-account dimensions to NetSuite entities for generated journal-entry evidence."),
                new(
                    $"{normalizedProviderId}:intercompany-controls",
                    "Intercompany controls",
                    journalEvidenceKind,
                    "Retain intercompany elimination and due-to/due-from mapping evidence before NetSuite export packages can be certified.")
            ],
            _ => []
        };

    private static AccountingSystemReconciliationStatusDto ResolveStatus(
        AccountingSystemTrialBalanceLineDto? external,
        bool hasMeridian,
        decimal variance)
    {
        if (external is null)
        {
            return AccountingSystemReconciliationStatusDto.MissingExternal;
        }

        if (!hasMeridian)
        {
            return AccountingSystemReconciliationStatusDto.MissingMeridian;
        }

        return Math.Abs(variance) <= 0.01m
            ? AccountingSystemReconciliationStatusDto.Matched
            : AccountingSystemReconciliationStatusDto.Variance;
    }

    private static string BuildDetail(AccountingSystemReconciliationStatusDto status, decimal variance)
        => status switch
        {
            AccountingSystemReconciliationStatusDto.Matched => "External GL evidence matches Meridian-owned ledger totals within tolerance.",
            AccountingSystemReconciliationStatusDto.MissingExternal => "Meridian-owned ledger truth has activity that is absent from the external GL import.",
            AccountingSystemReconciliationStatusDto.MissingMeridian => "External GL evidence is absent from Meridian-owned ledger truth and requires review before close.",
            AccountingSystemReconciliationStatusDto.Variance => $"External GL evidence and Meridian-owned ledger truth net totals differ by {variance:0.00}.",
            _ => "Review required before close evidence can rely on this row."
        };

    private static IEnumerable<string> BuildMeridianEvidenceReferences(
        LedgerJournalEntryRecord record,
        LedgerEntry line)
    {
        yield return $"ledger-entry:{line.EntryId:D}";
        yield return $"ledger-journal-entry:{record.Entry.JournalEntryId:D}";
        yield return $"ledger-period:{record.PeriodId:D}";

        if (record.SourceEventId.HasValue)
        {
            yield return $"source-event:{record.SourceEventId.Value:D}";
        }

        if (record.SourceJournalEntryId.HasValue)
        {
            yield return $"source-journal-entry:{record.SourceJournalEntryId.Value:D}";
        }
    }

    private static IReadOnlyList<string> NormalizeEvidenceReferences(IEnumerable<string?> evidenceReferences)
        => evidenceReferences
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyDictionary<string, string> NormalizeAccountMappings(IReadOnlyDictionary<string, string>? accountMappings)
        => (accountMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

    private static AccountingCertificationStateDto ResolveMappingProfileCertificationState(
        ExternalGlMappingProfileDto profile,
        string providerId,
        string fundProfileId,
        string profileId,
        IReadOnlyList<string> evidenceLinks)
        => profile.CertificationState == AccountingCertificationStateDto.Certified &&
           !HasMappingProfileCertificationEvidenceWithProvenance(evidenceLinks, providerId, fundProfileId, profileId)
            ? AccountingCertificationStateDto.Draft
            : profile.CertificationState;

    private static bool HasMappingProfileCertificationEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("mapping", StringComparison.OrdinalIgnoreCase) &&
            (link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
             link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
             link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
             link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
             link.Contains("review", StringComparison.OrdinalIgnoreCase)));

    private static bool HasMappingProfileCertificationProvenance(
        IReadOnlyList<string> evidenceLinks,
        string providerId,
        string fundProfileId,
        string profileId)
        => evidenceLinks.Any(link =>
            ReferencesEvidenceToken(link, profileId) ||
            (ReferencesEvidenceToken(link, providerId) &&
             ReferencesEvidenceToken(link, fundProfileId)));

    private static bool HasMappingProfileCertificationEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string providerId,
        string fundProfileId,
        string profileId)
        => evidenceLinks.Any(link =>
            HasMappingProfileCertificationEvidence([link]) &&
            HasMappingProfileCertificationProvenance([link], providerId, fundProfileId, profileId));

    private static bool HasExportCertificationEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasExportCertificationEvidenceWithProvenance(
        ExternalGlExportPackageDto package,
        IReadOnlyList<string> evidenceLinks)
    {
        if (package.Certification is null)
        {
            return false;
        }

        var periodStart = $"{package.PeriodStart:yyyy-MM-dd}";
        var periodEnd = $"{package.PeriodEnd:yyyy-MM-dd}";
        var compactPeriodStart = $"{package.PeriodStart:yyyyMMdd}";
        var compactPeriodEnd = $"{package.PeriodEnd:yyyyMMdd}";
        return evidenceLinks.Any(link =>
            HasExportCertificationEvidence([link]) &&
            ReferencesEvidenceToken(link, package.ExportPackageId) &&
            ReferencesEvidenceToken(link, package.Certification.CertificationId) &&
            HasExplicitLedgerBookEvidence(link, package.LedgerBookId) &&
            ((ReferencesEvidenceToken(link, periodStart) &&
              ReferencesEvidenceToken(link, periodEnd)) ||
             (ReferencesEvidenceToken(link, compactPeriodStart) &&
              ReferencesEvidenceToken(link, compactPeriodEnd))));
    }

    private static bool HasExportPackageControlEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("export", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasExportPackageControlProvenance(
        string providerId,
        string fundProfileId,
        Guid? ledgerBookId,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<string> evidenceLinks)
    {
        var formattedStart = $"{periodStart:yyyy-MM-dd}";
        var formattedEnd = $"{periodEnd:yyyy-MM-dd}";
        var compactStart = $"{periodStart:yyyyMMdd}";
        var compactEnd = $"{periodEnd:yyyyMMdd}";
        return evidenceLinks.Any(link =>
            HasLedgerBookEvidence(link, ledgerBookId) &&
            (ReferencesEvidenceToken(link, fundProfileId) ||
             (ReferencesEvidenceToken(link, providerId) &&
              ReferencesEvidenceToken(link, fundProfileId)) ||
             (ReferencesEvidenceToken(link, formattedStart) &&
              ReferencesEvidenceToken(link, formattedEnd)) ||
             (ReferencesEvidenceToken(link, compactStart) &&
              ReferencesEvidenceToken(link, compactEnd))));
    }

    private static bool HasExportPackageControlEvidenceWithProvenance(
        string providerId,
        string fundProfileId,
        Guid? ledgerBookId,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasExportPackageControlEvidence([link]) &&
            HasExportPackageControlProvenance(providerId, fundProfileId, ledgerBookId, periodStart, periodEnd, [link]));

    private static bool HasLedgerBookEvidence(string link, Guid? ledgerBookId)
    {
        if (ledgerBookId is not Guid scopedLedgerBookId)
        {
            return true;
        }

        return HasExplicitLedgerBookEvidence(link, scopedLedgerBookId);
    }

    private static bool HasExplicitLedgerBookEvidence(string link, Guid? ledgerBookId)
    {
        if (ledgerBookId is not Guid scopedLedgerBookId)
        {
            return true;
        }

        var ledgerBookIdText = scopedLedgerBookId.ToString("D");
        var ledgerBookIdCompact = scopedLedgerBookId.ToString("N");
        return ReferencesScopedValue(link, "ledger-book:", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledger-book/", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledger-book=", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledgerbook:", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledgerbook/", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledgerbook=", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledgerBookId:", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledgerBookId/", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledgerBookId=", ledgerBookIdText) ||
               ReferencesScopedValue(link, "book:", ledgerBookIdText) ||
               ReferencesScopedValue(link, "book/", ledgerBookIdText) ||
               ReferencesScopedValue(link, "book=", ledgerBookIdText) ||
               ReferencesScopedValue(link, "ledger-book:", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledger-book/", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledger-book=", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledgerbook:", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledgerbook/", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledgerbook=", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledgerBookId:", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledgerBookId/", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "ledgerBookId=", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "book:", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "book/", ledgerBookIdCompact) ||
               ReferencesScopedValue(link, "book=", ledgerBookIdCompact);
    }

    private static bool ReferencesScopedValue(string reference, string prefix, string value)
    {
        var searchIndex = 0;
        while (searchIndex < reference.Length)
        {
            var valueIndex = string.IsNullOrEmpty(prefix)
                ? reference.IndexOf(value, searchIndex, StringComparison.OrdinalIgnoreCase)
                : IndexOfScopedValue(reference, prefix, value, searchIndex);
            if (valueIndex < 0)
            {
                return false;
            }

            if (IsEvidenceTokenBoundary(reference, valueIndex + value.Length))
            {
                return true;
            }

            searchIndex = valueIndex + value.Length;
        }

        return false;
    }

    private static bool ReferencesEvidenceToken(string reference, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var searchIndex = 0;
        while (searchIndex < reference.Length)
        {
            var valueIndex = reference.IndexOf(value, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (valueIndex < 0)
            {
                return false;
            }

            if (IsEvidenceTokenBoundary(reference, valueIndex - 1) &&
                IsEvidenceTokenBoundary(reference, valueIndex + value.Length))
            {
                return true;
            }

            searchIndex = valueIndex + value.Length;
        }

        return false;
    }

    private static int IndexOfScopedValue(string reference, string prefix, string value, int searchIndex)
    {
        while (searchIndex < reference.Length)
        {
            var prefixIndex = reference.IndexOf(prefix, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                return -1;
            }

            var valueIndex = prefixIndex + prefix.Length;
            if (reference.Length >= valueIndex + value.Length &&
                string.Compare(reference, valueIndex, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return valueIndex;
            }

            searchIndex = valueIndex;
        }

        return -1;
    }

    private static bool IsEvidenceTokenBoundary(string reference, int index)
        => index < 0 ||
           index >= reference.Length ||
           reference[index] is '/' or ':' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' or ' ' or '\t' or '\r' or '\n';

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException($"Reviewed automation cannot {action}; a human operator must perform this external accounting action.");
        }
    }

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.")
            : value.Trim();

    private static string NormalizeProviderId(string? providerId)
        => string.IsNullOrWhiteSpace(providerId)
            ? DefaultProviderId
            : providerId.Trim().ToLowerInvariant();

    private static string NormalizeFundProfileId(string? fundProfileId)
        => string.IsNullOrWhiteSpace(fundProfileId) ? DefaultFundProfileId : fundProfileId.Trim();

    private static string ImportKey(
        string providerId,
        string fundProfileId,
        Guid? ledgerBookId,
        string? tenantId = null,
        string? companyId = null)
        => $"{NormalizeProviderId(providerId)}|{NormalizeFundProfileId(fundProfileId)}|{ledgerBookId?.ToString("D") ?? "none"}|{BuildTenantPackageScope(tenantId, companyId)}";

    private static string MappingProfileKey(
        string providerId,
        string fundProfileId,
        Guid? ledgerBookId,
        string profileId,
        string? tenantId,
        string? companyId)
        => $"{ImportKey(providerId, fundProfileId, ledgerBookId)}|{BuildTenantPackageScope(tenantId, companyId)}|{profileId.Trim().ToLowerInvariant()}";

    private static string BuildExportPackageId(
        string providerId,
        string fundProfileId,
        Guid? ledgerBookId,
        DateOnly periodEnd,
        string? tenantId,
        string? companyId)
    {
        var tenantScope = BuildTenantPackageScope(tenantId, companyId);
        var bookScope = ledgerBookId.HasValue ? $"-book-{ledgerBookId.Value:N}" : string.Empty;
        var baseId = $"external-gl-export-{SanitizeId(providerId)}-{SanitizeId(fundProfileId)}{bookScope}-{periodEnd:yyyyMMdd}";
        return string.IsNullOrWhiteSpace(tenantScope)
            ? $"{baseId}-{Guid.NewGuid():N}"
            : $"{baseId}-{tenantScope}-{Guid.NewGuid():N}";
    }

    private bool TryGetExportPackage(
        string exportPackageId,
        string? tenantId,
        string? companyId,
        out ExternalGlExportPackageDto package)
    {
        var normalizedExportPackageId = RequireText(exportPackageId, nameof(exportPackageId));
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        if (!_exportPackages.TryGetValue(normalizedExportPackageId, out var match) ||
            !MatchesTenantScope(match, normalizedTenantId, normalizedCompanyId))
        {
            package = default!;
            return false;
        }

        package = match;
        return true;
    }

    private static bool MatchesTenantScope(
        ExternalGlExportPackageDto package,
        string? tenantId,
        string? companyId)
    {
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        return (normalizedTenantId is null ||
                string.Equals(NormalizeOptional(package.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
               && (normalizedCompanyId is null ||
                   string.Equals(NormalizeOptional(package.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildTenantPackageScope(string? tenantId, string? companyId)
    {
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        if (normalizedTenantId is null && normalizedCompanyId is null)
        {
            return string.Empty;
        }

        return $"tenant-{SanitizeId(normalizedTenantId ?? "default")}-company-{SanitizeId(normalizedCompanyId ?? "default")}";
    }

    private static DateOnly CurrentMonthStart()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new DateOnly(today.Year, today.Month, 1);
    }

    private static DateOnly CurrentMonthEnd(DateOnly periodStart)
        => periodStart.AddMonths(1).AddDays(-1);

    private static string SanitizeId(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private sealed record PlannedAccountingSystemProvider(
        string ProviderId,
        string DisplayName,
        string StatusDetail,
        IReadOnlyList<string> EvidenceKinds,
        IReadOnlyList<AccountingSystemProviderMappingRequirementDto> MappingRequirements)
    {
        public AccountingSystemProviderDto ToDto()
            => new(
                ProviderId,
                DisplayName,
                AccountingSystemProviderStateDto.Planned,
                RequiresCredentials: true,
                SupportsChartOfAccounts: true,
                SupportsJournalEntries: true,
                SupportsTrialBalance: true,
                SupportsPosting: false,
                "Import adapter not registered",
                StatusDetail,
                EvidenceKinds,
                MappingRequirements: MappingRequirements);
    }

    private sealed record ScopedExternalGlMappingProfile(
        string ProviderId,
        string FundProfileId,
        Guid? LedgerBookId,
        ExternalGlMappingProfileDto Profile,
        string Actor,
        IReadOnlyList<string> EvidenceLinks,
        string? TenantId = null,
        string? CompanyId = null);

    private sealed class MeridianAccountTotal
    {
        public MeridianAccountTotal(string accountName, string currency)
        {
            AccountName = accountName;
            Currency = currency;
        }

        public string AccountName { get; }

        public string Currency { get; }

        public decimal Debit { get; set; }

        public decimal Credit { get; set; }

        public HashSet<string> EvidenceReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct AccountingSystemReconciliationBreakCounts(
        int MissingExternal,
        int MissingMeridian,
        int Variance,
        int ReviewRequired)
    {
        public int Total => MissingExternal + MissingMeridian + Variance + ReviewRequired;

        public static AccountingSystemReconciliationBreakCounts FromRows(IEnumerable<AccountingSystemReconciliationRowDto> rows)
        {
            var missingExternal = 0;
            var missingMeridian = 0;
            var variance = 0;
            var reviewRequired = 0;

            foreach (var row in rows)
            {
                switch (row.Status)
                {
                    case AccountingSystemReconciliationStatusDto.MissingExternal:
                        missingExternal++;
                        break;
                    case AccountingSystemReconciliationStatusDto.MissingMeridian:
                        missingMeridian++;
                        break;
                    case AccountingSystemReconciliationStatusDto.Variance:
                        variance++;
                        break;
                    case AccountingSystemReconciliationStatusDto.ReviewRequired:
                        reviewRequired++;
                        break;
                }
            }

            return new AccountingSystemReconciliationBreakCounts(
                missingExternal,
                missingMeridian,
                variance,
                reviewRequired);
        }
    }
}
