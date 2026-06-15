using System.Collections.Concurrent;
using Meridian.Contracts.AccountingSystem;
using Meridian.Ledger;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingSystem;

public sealed class AccountingSystemIntegrationService
{
    private const string DefaultProviderId = "quickbooks-fixture";
    private const string QuickBooksOnlineProviderId = "quickbooks";
    private const string DefaultFundProfileId = "default-fund";

    private readonly IReadOnlyList<IAccountingSystemProvider> _providers;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly ConcurrentDictionary<string, AccountingSystemImportDetailDto> _latestImports = new(StringComparer.OrdinalIgnoreCase);

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

        if (!_providers.Any(static provider => string.Equals(provider.ProviderId, QuickBooksOnlineProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            rows.Add(new AccountingSystemProviderDto(
                QuickBooksOnlineProviderId,
                "QuickBooks Online",
                AccountingSystemProviderStateDto.Planned,
                RequiresCredentials: true,
                SupportsChartOfAccounts: true,
                SupportsJournalEntries: true,
                SupportsTrialBalance: true,
                SupportsPosting: false,
                "OAuth adapter not registered",
                "Live QuickBooks Online OAuth, company selection, and read-only GL import require the QuickBooks Online provider registration.",
                ["QuickBooksAccount", "QuickBooksJournalEntry", "QuickBooksTrialBalance"]));
        }

        return rows
            .OrderBy(static row => row.State == AccountingSystemProviderStateDto.Available ? 0 : 1)
            .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        var breakCount = rows.Count(static row => row.Status != AccountingSystemReconciliationStatusDto.Matched);

        return new AccountingSystemReconciliationSummaryDto(
            $"gl-recon-{latest.Summary.ImportId}",
            latest.Summary.ImportId,
            latest.Summary.ProviderId,
            latest.Summary.FundProfileId,
            latest.Summary.PeriodStart,
            latest.Summary.PeriodEnd,
            DateTimeOffset.UtcNow,
            rows.Count(static row => row.Status == AccountingSystemReconciliationStatusDto.Matched),
            rows.Count(static row => row.Status != AccountingSystemReconciliationStatusDto.Matched),
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
                breakCount,
                rows.Count)
        };
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
        int breakCount,
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
                ResolveTieOutPackageStatus(rowCount, breakCount, externalEvidenceReferences.Count, meridianEvidenceReferences.Count),
                summaryEvidenceReferences.Count,
                summaryEvidenceReferences,
                BuildTieOutRequiredActions(breakCount, externalEvidenceReferences.Count, meridianEvidenceReferences.Count))
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
        int breakCount,
        int externalEvidenceCount,
        int meridianEvidenceCount)
    {
        var actions = new List<string>(3);
        if (externalEvidenceCount == 0)
        {
            actions.Add("Import external accounting-system evidence.");
        }

        if (meridianEvidenceCount == 0)
        {
            actions.Add("Load Meridian ledger journal evidence.");
        }

        if (breakCount > 0)
        {
            actions.Add("Resolve GL reconciliation breaks before approving close evidence.");
        }

        return actions;
    }

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

    private static string NormalizeProviderId(string? providerId)
        => string.IsNullOrWhiteSpace(providerId)
            ? DefaultProviderId
            : providerId.Trim().ToLowerInvariant();

    private static string NormalizeFundProfileId(string? fundProfileId)
        => string.IsNullOrWhiteSpace(fundProfileId) ? DefaultFundProfileId : fundProfileId.Trim();

    private static string ImportKey(string providerId, string fundProfileId, Guid? ledgerBookId)
        => $"{NormalizeProviderId(providerId)}|{NormalizeFundProfileId(fundProfileId)}|{ledgerBookId?.ToString("D") ?? "none"}";

    private static string SanitizeId(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

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
}
