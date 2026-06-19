using System.Collections.Concurrent;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;

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
            ["QuickBooksAccount", "QuickBooksJournalEntry", "QuickBooksTrialBalance"]),
        new(
            "xero",
            "Xero",
            "Xero chart, journal, and trial-balance import mapping is planned; live posting remains disabled until a separately approved adapter exists.",
            ["XeroAccount", "XeroManualJournal", "XeroTrialBalance"]),
        new(
            "netsuite",
            "NetSuite",
            "NetSuite chart, journal, and trial-balance import mapping is planned; live posting remains disabled until a separately approved adapter exists.",
            ["NetSuiteAccount", "NetSuiteJournalEntry", "NetSuiteTrialBalance"])
    ];

    private readonly IReadOnlyList<IAccountingSystemProvider> _providers;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly ConcurrentDictionary<string, AccountingSystemImportDetailDto> _latestImports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScopedExternalGlMappingProfile> _mappingProfiles = new(StringComparer.OrdinalIgnoreCase);

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
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedProviderId = string.IsNullOrWhiteSpace(providerId) ? null : NormalizeProviderId(providerId);
        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : NormalizeFundProfileId(fundProfileId);
        var rows = _mappingProfiles.Values
            .Where(record => normalizedProviderId is null || string.Equals(record.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase))
            .Where(record => normalizedFundProfileId is null || string.Equals(record.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(record => ledgerBookId is null || record.LedgerBookId == ledgerBookId)
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
        var normalizedProfile = request.Profile with
        {
            ProfileId = profileId,
            ProviderId = providerId,
            DisplayName = RequireText(request.Profile.DisplayName, "Mapping profile display name"),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CertificationState = request.Profile.CertificationState,
            AccountMappings = NormalizeAccountMappings(request.Profile.AccountMappings),
            DimensionMappings = (request.Profile.DimensionMappings ?? [])
                .Select(mapping => mapping with { ProviderId = providerId })
                .ToArray()
        };

        if (normalizedProfile.AccountMappings.Count == 0 && normalizedProfile.DimensionMappings.Count == 0)
        {
            normalizedProfile = normalizedProfile with { CertificationState = AccountingCertificationStateDto.Draft };
        }

        _mappingProfiles[MappingProfileKey(providerId, fundProfileId, request.LedgerBookId, profileId)] =
            new ScopedExternalGlMappingProfile(providerId, fundProfileId, request.LedgerBookId, normalizedProfile, actor, NormalizeEvidenceReferences(request.EvidenceLinks));

        return Task.FromResult(normalizedProfile);
    }

    public async Task<ExternalGlExportPackageDto> CreateExportPackageAsync(
        AccountingSystemExportPackageRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var actor = RequireText(request.Actor, "Actor");
        var providerId = NormalizeProviderId(request.ProviderId);
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var mappingProfile = ResolveMappingProfile(providerId, fundProfileId, request.LedgerBookId, request.MappingProfileId);
        var reconciliation = await TryReconcileLatestAsync(providerId, fundProfileId, request.LedgerBookId, ct).ConfigureAwait(false);
        var periodStart = request.PeriodStart ?? reconciliation?.PeriodStart ?? CurrentMonthStart();
        var periodEnd = request.PeriodEnd ?? reconciliation?.PeriodEnd ?? CurrentMonthEnd(periodStart);
        var validationIssues = BuildExportValidationIssues(providerId, mappingProfile, reconciliation, request.RequireBalancedReconciliation);
        var evidenceLinks = BuildExportEvidenceLinks(request, mappingProfile, reconciliation);
        var hasCritical = validationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var certificationState = hasCritical
            ? AccountingCertificationStateDto.Draft
            : AccountingCertificationStateDto.ReadyForReview;
        var certification = new ExternalGlExportCertificationDto(
            $"external-gl-export-cert-{SanitizeId(providerId)}-{SanitizeId(fundProfileId)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            certificationState,
            actor,
            DateTimeOffset.UtcNow,
            hasCritical
                ? "Export package is retained as a guarded review artifact and cannot be certified until validation issues are resolved."
                : "Export package passed automated mapping and reconciliation safeguards and is ready for human certification review.",
            evidenceLinks);

        return new ExternalGlExportPackageDto(
            $"external-gl-export-{SanitizeId(providerId)}-{SanitizeId(fundProfileId)}-{periodEnd:yyyyMMdd}-{Guid.NewGuid():N}",
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
            validationIssues);
    }

    public async Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        request ??= new AccountingSystemImportRequestDto();
        var provider = await ResolveProviderAsync(request.ProviderId, ct).ConfigureAwait(false);
        var detail = await provider.ImportAsync(request, ct).ConfigureAwait(false);

        if (request.PersistPreview)
        {
            _latestImports[ImportKey(detail.Summary.ProviderId, detail.Summary.FundProfileId, detail.Summary.LedgerBookId)] = detail;
        }

        return detail;
    }

    public async Task<AccountingSystemImportDetailDto> GetLatestImportAsync(
        string? providerId = null,
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedProviderId = await ResolveProviderIdAsync(providerId, ct).ConfigureAwait(false);
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        if (_latestImports.TryGetValue(ImportKey(normalizedProviderId, normalizedFundProfileId, ledgerBookId), out var detail))
        {
            return detail;
        }

        return await ImportAsync(
            new AccountingSystemImportRequestDto(
                normalizedProviderId,
                normalizedFundProfileId,
                ledgerBookId,
                PersistPreview: true),
            ct).ConfigureAwait(false);
    }

    public async Task<AccountingSystemReconciliationSummaryDto> ReconcileLatestAsync(
        string? providerId = null,
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var latest = await GetLatestImportAsync(providerId, fundProfileId, ledgerBookId, ct).ConfigureAwait(false);
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
            summaryEvidenceReferences)
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
        string? mappingProfileId)
    {
        var candidates = _mappingProfiles.Values
            .Where(record => string.Equals(record.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Where(record => string.Equals(record.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(record => record.LedgerBookId == ledgerBookId);

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
        CancellationToken ct)
    {
        if (!_providers.Any(provider => string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        try
        {
            return await ReconcileLatestAsync(providerId, fundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildExportValidationIssues(
        string providerId,
        ScopedExternalGlMappingProfile? mappingProfile,
        AccountingSystemReconciliationSummaryDto? reconciliation,
        bool requireBalancedReconciliation)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
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

        if (reconciliation is null)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "MissingExternalGlReconciliation",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Provider '{providerId}' has no available import/reconciliation evidence.",
                providerId,
                "Import chart, journal, and trial-balance evidence and reconcile it against Meridian ledger truth before export certification."));
        }
        else if (requireBalancedReconciliation && reconciliation.BreakCount > 0)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "UnresolvedExternalGlBreaks",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"{reconciliation.BreakCount} external GL reconciliation break(s) remain unresolved.",
                reconciliation.ReconciliationId,
                "Resolve or approve GL tie-out breaks with retained evidence before export certification."));
        }

        issues.Add(new AccountingConfigurationValidationIssueDto(
            "LiveExternalPostingDisabled",
            AccountingConfigurationValidationSeverityDto.Info,
            "Live external GL posting is disabled; this operation only creates a guarded export artifact.",
            providerId,
            "Review, approve, and reconcile the export artifact outside Meridian until a later live-posting adapter is explicitly approved."));

        return issues;
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
            metadata);
    }

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

    private static string ImportKey(string providerId, string fundProfileId, Guid? ledgerBookId)
        => $"{NormalizeProviderId(providerId)}|{NormalizeFundProfileId(fundProfileId)}|{ledgerBookId?.ToString("D") ?? "none"}";

    private static string MappingProfileKey(string providerId, string fundProfileId, Guid? ledgerBookId, string profileId)
        => $"{ImportKey(providerId, fundProfileId, ledgerBookId)}|{profileId.Trim().ToLowerInvariant()}";

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
        IReadOnlyList<string> EvidenceKinds)
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
                EvidenceKinds);
    }

    private sealed record ScopedExternalGlMappingProfile(
        string ProviderId,
        string FundProfileId,
        Guid? LedgerBookId,
        ExternalGlMappingProfileDto Profile,
        string Actor,
        IReadOnlyList<string> EvidenceLinks);

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
