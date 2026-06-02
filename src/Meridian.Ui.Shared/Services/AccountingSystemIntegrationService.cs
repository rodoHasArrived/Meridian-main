using System.Collections.Concurrent;
using Meridian.Contracts.AccountingSystem;
using Meridian.Infrastructure.Adapters.QuickBooks;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed class AccountingSystemIntegrationService
{
    private const string DefaultProviderId = QuickBooksFixtureAccountingProvider.Id;
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

    public IReadOnlyList<AccountingSystemProviderDto> ListProviders()
    {
        var rows = _providers
            .Select(provider => ToProviderDto(provider))
            .Append(new AccountingSystemProviderDto(
                "quickbooks",
                "QuickBooks Online",
                AccountingSystemProviderStateDto.Planned,
                RequiresCredentials: true,
                SupportsChartOfAccounts: true,
                SupportsJournalEntries: true,
                SupportsTrialBalance: true,
                SupportsPosting: false,
                "OAuth adapter planned",
                "Live QuickBooks Online OAuth, company selection, refresh-token management, and posting/export are intentionally outside this contract-first slice.",
                ["QuickBooksAccount", "QuickBooksJournalEntry", "QuickBooksTrialBalance"]))
            .OrderBy(static row => row.State == AccountingSystemProviderStateDto.Available ? 0 : 1)
            .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return rows;
    }

    public async Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        request ??= new AccountingSystemImportRequestDto();
        var provider = ResolveProvider(request.ProviderId);
        var detail = await provider.ImportAsync(request, ct).ConfigureAwait(false);

        if (request.PersistPreview)
        {
            _latestImports[ImportKey(detail.Summary.ProviderId, detail.Summary.FundProfileId, detail.Summary.LedgerBookId)] = detail;
        }

        return detail;
    }

    public Task<AccountingSystemImportDetailDto> GetLatestImportAsync(
        string? providerId = null,
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedProviderId = NormalizeProviderId(providerId);
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        if (_latestImports.TryGetValue(ImportKey(normalizedProviderId, normalizedFundProfileId, ledgerBookId), out var detail))
        {
            return Task.FromResult(detail);
        }

        return ImportAsync(
            new AccountingSystemImportRequestDto(
                normalizedProviderId,
                normalizedFundProfileId,
                ledgerBookId,
                PersistPreview: true),
            ct);
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
            var meridianDebit = meridian.Debit;
            var meridianCredit = meridian.Credit;
            var variance = (externalDebit - externalCredit) - (meridianDebit - meridianCredit);
            var status = ResolveStatus(external, meridian.HasValue, variance);

            rows.Add(new AccountingSystemReconciliationRowDto(
                $"gl-recon-{SanitizeId(accountCode)}",
                accountCode,
                external?.AccountName ?? meridian.AccountName ?? accountCode,
                external?.Currency ?? meridian.Currency ?? "USD",
                status,
                externalDebit,
                externalCredit,
                meridianDebit,
                meridianCredit,
                variance,
                BuildDetail(status, variance),
                external?.EvidenceRef));
        }

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
            PostingDisabledReason: "External GL posting/export is disabled until the provider-neutral evidence and reconciliation path is proven.",
            rows,
            latest.Summary.EvidenceReferences);
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
            foreach (var line in entries.SelectMany(static entry => entry.Entry.Lines))
            {
                var accountCode = line.Account.Name;
                if (!totals.TryGetValue(accountCode, out var total))
                {
                    total = new MeridianAccountTotal(accountCode, "USD", 0m, 0m, HasValue: true);
                }

                totals[accountCode] = total with
                {
                    Debit = total.Debit + line.Debit,
                    Credit = total.Credit + line.Credit
                };
            }
        }

        return totals;
    }

    private IAccountingSystemProvider ResolveProvider(string? providerId)
    {
        var normalized = NormalizeProviderId(providerId);
        return _providers.FirstOrDefault(provider => string.Equals(provider.ProviderId, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Accounting system provider '{normalized}' is not available.");
    }

    private static AccountingSystemProviderDto ToProviderDto(IAccountingSystemProvider provider)
        => new(
            provider.ProviderId,
            provider.DisplayName,
            AccountingSystemProviderStateDto.Available,
            RequiresCredentials: false,
            provider.Capabilities.SupportsChartOfAccounts,
            provider.Capabilities.SupportsJournalEntries,
            provider.Capabilities.SupportsTrialBalance,
            provider.Capabilities.SupportsPosting,
            "Ready for fixture import",
            "Read-only external GL import and reconciliation are available for contract-first validation.",
            provider.Capabilities.EvidenceKinds);

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
            AccountingSystemReconciliationStatusDto.Matched => "External GL and Meridian ledger totals match within tolerance.",
            AccountingSystemReconciliationStatusDto.MissingExternal => "Meridian ledger has activity that is absent from the external GL import.",
            AccountingSystemReconciliationStatusDto.MissingMeridian => "External GL has activity that is absent from Meridian ledger evidence.",
            AccountingSystemReconciliationStatusDto.Variance => $"External GL and Meridian ledger net totals differ by {variance:0.00}.",
            _ => "Review required before close evidence can rely on this row."
        };

    private static string NormalizeProviderId(string? providerId)
        => string.IsNullOrWhiteSpace(providerId) || string.Equals(providerId, "quickbooks", StringComparison.OrdinalIgnoreCase)
            ? DefaultProviderId
            : providerId.Trim().ToLowerInvariant();

    private static string NormalizeFundProfileId(string? fundProfileId)
        => string.IsNullOrWhiteSpace(fundProfileId) ? DefaultFundProfileId : fundProfileId.Trim();

    private static string ImportKey(string providerId, string fundProfileId, Guid? ledgerBookId)
        => $"{NormalizeProviderId(providerId)}|{NormalizeFundProfileId(fundProfileId)}|{ledgerBookId?.ToString("D") ?? "none"}";

    private static string SanitizeId(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private readonly record struct MeridianAccountTotal(
        string AccountName,
        string Currency,
        decimal Debit,
        decimal Credit,
        bool HasValue);
}
