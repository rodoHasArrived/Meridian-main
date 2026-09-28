using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.AccountingSystem;

public sealed partial class AccountingSystemIntegrationService
{
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
            var chartAccount = latest.ChartAccounts.FirstOrDefault(a => string.Equals(a.AccountCode, accountCode, StringComparison.OrdinalIgnoreCase));
            var externalCurrency = external?.Currency ?? chartAccount?.Currency;
            var currencyMismatch = meridian is not null && externalCurrency is not null &&
                !string.Equals(externalCurrency, meridian.Currency, StringComparison.OrdinalIgnoreCase);
            var status = currencyMismatch ? AccountingSystemReconciliationStatusDto.Variance
                : latest.Summary.TrialBalanceBasis is not null && variance == 0m && chartAccount is not null
                    ? AccountingSystemReconciliationStatusDto.Matched : ResolveStatus(external, meridian is not null, variance);
            var rowExternalEvidenceReferences = NormalizeEvidenceReferences([external?.EvidenceRef]);
            var meridianEvidenceReferences = meridian is null ? [] : NormalizeEvidenceReferences(meridian.EvidenceReferences);
            var rowEvidenceReferences = NormalizeEvidenceReferences(rowExternalEvidenceReferences.Concat(meridianEvidenceReferences));

            rows.Add(new AccountingSystemReconciliationRowDto(
                $"gl-recon-{SanitizeId(accountCode)}",
                accountCode,
                external?.AccountName ?? meridian?.AccountName ?? accountCode,
                meridian?.Currency ?? externalCurrency ?? "USD",
                status,
                externalDebit,
                externalCredit,
                meridianDebit,
                meridianCredit,
                variance,
                currencyMismatch
                    ? $"External GL currency '{externalCurrency}' differs from Meridian ledger book currency '{meridian!.Currency}'; amounts cannot be reconciled without governed conversion."
                    : BuildDetail(status, variance),
                external?.EvidenceRef)
            {
                PeriodDebit = latest.Summary.TrialBalanceBasis is null ? null : meridian?.PeriodDebit ?? 0m,
                PeriodCredit = latest.Summary.TrialBalanceBasis is null ? null : meridian?.PeriodCredit ?? 0m,
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

    private async Task<Dictionary<string, MeridianAccountTotal>> LoadMeridianTotalsAsync(
        AccountingSystemImportSummaryDto summary,
        CancellationToken ct)
    {
        var totals = new Dictionary<string, MeridianAccountTotal>(StringComparer.OrdinalIgnoreCase);
        if (_ledgerJournalStore is null)
            return totals;
        var periods = await _ledgerJournalStore.ListPeriodsAsync(
            ledgerBookId: summary.LedgerBookId, fundProfileId: summary.FundProfileId, ct: ct).ConfigureAwait(false);
        var basis = summary.TrialBalanceBasis;
        var incomeCodes = basis?.IncomeStatementAccountCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var book = summary.LedgerBookId is { } bookId
            ? await _ledgerJournalStore.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false) : null;
        if (basis is not null && (book is null || book.AccountingBasis is not (AccountingBasisKindDto.Primary or AccountingBasisKindDto.Gaap)))
            throw new InvalidOperationException("Live accrual GL reconciliation requires an accrual-compatible Primary or Gaap ledger book; Cash, Tax, Statutory and unresolved book bases are unsupported.");
        var currency = book?.BaseCurrency ?? "USD";
        MeridianAccountTotal Total(string code)
        {
            if (!totals.TryGetValue(code, out var total))
                totals[code] = total = new MeridianAccountTotal(code, currency);
            return total;
        }
        foreach (var period in periods.Where(period => period.StartDate <= summary.PeriodEnd &&
                     (basis is not null || period.EndDate >= summary.PeriodStart)))
        {
            ct.ThrowIfCancellationRequested();
            var entries = await _ledgerJournalStore.GetByPeriodAsync(period.PeriodId, ct).ConfigureAwait(false);
            foreach (var record in entries)
            {
                var date = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
                if (date > summary.PeriodEnd || (basis is null && date < summary.PeriodStart))
                    continue;
                foreach (var line in record.Entry.Lines)
                {
                    var accountCode = line.Account.Name;
                    var evidence = BuildMeridianEvidenceReferences(record, line).ToArray();
                    if (date >= summary.PeriodStart)
                    {
                        var activity = Total(accountCode);
                        activity.PeriodDebit += line.Debit;
                        activity.PeriodCredit += line.Credit;
                        activity.EvidenceReferences.UnionWith(evidence);
                    }
                    // Report roll-forward is a comparison projection, never an export journal.
                    var balanceCode = basis is not null && date < basis.IncomeStatementPeriodStart && incomeCodes!.Contains(accountCode)
                        ? basis.RetainedEarningsAccountCode! : accountCode;
                    var total = Total(balanceCode);
                    total.Debit += line.Debit;
                    total.Credit += line.Credit;
                    total.EvidenceReferences.UnionWith(evidence);
                }
            }
        }
        return totals;
    }
}
