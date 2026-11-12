using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compares broker evidence with posted cash and canonical open lots. Carrying-value marks are
/// excluded from cost basis. A missing acquisition or a moved read population is unavailable,
/// never a fabricated zero or a matched custodian-to-provider comparison.
/// </summary>
public sealed class PostedLedgerAccountReconciliationSource(ILedgerJournalStore store)
{
    public async Task<IReadOnlyList<ProviderShadowBookComparisonLineDto>> CompareAsync(
        LedgerBookDto book, Guid accountId, FundAccountBrokerageSyncActivityDto provider,
        decimal tolerance, CancellationToken ct = default)
    {
        if (book.FundStructureNodeId != accountId || provider.FundAccountId != accountId)
            throw new InvalidOperationException("Posted-ledger reconciliation account scope is mismatched.");
        tolerance = Math.Abs(tolerance);
        var account = accountId.ToString("D");
        var query = new LedgerJournalEntryQuery(LedgerBookId: book.LedgerBookId);
        var records = await store.QueryAsync(query, ct);
        bool Owned(LedgerEntry line) => string.Equals(line.Account.FinancialAccountId, account, StringComparison.OrdinalIgnoreCase);
        // A dedicated financial-account book may retain legacy unscoped account identities.
        bool InScope(LedgerEntry line) => Owned(line) || line.Account.FinancialAccountId is null;
        var eligible = records.Where(r => r.Entry.Timestamp <= provider.SyncedAt).ToArray();
        var lines = eligible.SelectMany(r => r.Entry.Lines).Where(InScope).ToArray();
        var cash = lines.Where(line => line.Account.Name == "Cash").Sum(line => line.Debit - line.Credit);
        var sequence = records.Count == 0 ? 0 : records.Max(r => r.GlobalSequence);
        var evidence = $"Posted book {book.LedgerBookId:D}, journal sequence {sequence}, broker cutoff {provider.SyncedAt:O}.";
        var currencyMatches = string.Equals(book.BaseCurrency, provider.Balance?.Currency, StringComparison.OrdinalIgnoreCase);
        var result = new List<ProviderShadowBookComparisonLineDto>
        {
            Compare("posted-ledger-cash", "Posted ledger cash", currencyMatches ? cash : null,
                provider.Balance?.Cash, tolerance, evidence + " Cash uses journal debit less credit in the book currency.")
        };
        var futureActivity = records.Any(r => r.Entry.Timestamp > provider.SyncedAt && r.Entry.Lines.Any(InScope));
        var providerBySymbol = provider.Positions.GroupBy(p => p.Symbol.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var symbols = providerBySymbol.Keys.Concat(lines.Where(l => l.Account.Name is "Securities" or "Short Securities Payable")
            .Select(l => l.Account.Symbol).OfType<string>()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            providerBySymbol.TryGetValue(symbol, out var brokerRows);
            decimal? quantity = null;
            decimal? cost = null;
            var reason = evidence;
            try
            {
                if ((brokerRows ?? []).Any(p => p.AssetClass.ToLowerInvariant() is not ("us_equity" or "equity" or "stock" or "stk" or "etf")))
                    throw new InvalidOperationException("Provider quantity and average-cost conventions require explicit instrument multipliers; this comparison supports unit-priced equities.");
                if (futureActivity)
                    throw new InvalidOperationException("Later journal activity prevents using current open lots at the earlier broker cutoff; capture a fresh broker snapshot.");
                var accounts = lines.Where(l => l.Account.Symbol == symbol && l.Account.Name is "Securities" or "Short Securities Payable")
                    .Select(l => l.Account)
                    .Concat(new[] { LedgerAccounts.Securities(symbol, account), LedgerAccounts.ShortSecuritiesPayable(symbol, account) })
                    .Distinct().ToArray();
                var lots = new List<LedgerTaxLotRecord>();
                foreach (var lotAccount in accounts)
                    lots.AddRange(await store.ListOpenTaxLotsAsync(book.LedgerBookId, lotAccount, ct));
                if (lots.Any(l => l.UpdatedAt > provider.SyncedAt || l.AcquiredDate > DateOnly.FromDateTime(provider.SyncedAt.UtcDateTime)))
                    throw new InvalidOperationException("Current lot evidence is later than the broker observation.");
                if (lots.Count == 0 && lines.Where(l => l.Account.Symbol == symbol && l.Account.Name is "Securities" or "Short Securities Payable")
                        .Sum(l => l.Debit - l.Credit) != 0m)
                    throw new InvalidOperationException("The posted position lacks canonical acquisition and open-lot evidence.");
                var canonical = lots.DistinctBy(l => l.TaxLotRecordId).Select(l => l.ToOpenLot()).ToArray();
                if (canonical.Any(l => l.Acquisition.QuantityBasis != LotQuantityBasis.Units))
                    throw new InvalidOperationException("The broker quantity convention is not certified against this face-denominated lot population.");
                if (canonical.Any(l => !string.Equals(l.Acquisition.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)) ||
                    (brokerRows ?? []).Any(p => !string.Equals(p.Currency ?? provider.Balance?.Currency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Position cost comparison requires the same currency or retained FX conversion evidence.");
                quantity = canonical.Sum(l => l.Acquisition.Direction == OpenLotDirection.Short ? -l.OpenQuantity : l.OpenQuantity);
                cost = canonical.Sum(l => l.OpenFunctionalCostBasis);
                reason += " Quantity and cost use canonical open lots, including governed basis adjustments.";
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or LedgerValidationException)
            {
                reason += " " + ex.Message;
            }
            result.Add(Compare($"posted-ledger-position-quantity:{symbol}", $"Posted {symbol} quantity", quantity,
                brokerRows?.Sum(p => p.Quantity) ?? 0m, tolerance, reason));
            result.Add(Compare($"posted-ledger-position-cost-basis:{symbol}", $"Posted {symbol} cost basis", cost,
                brokerRows?.Sum(p => Math.Abs(p.Quantity) * p.AverageEntryPrice) ?? 0m, tolerance, reason));
        }
        var after = await store.QueryAsync(query, ct);
        if (records.Count != after.Count || sequence != (after.Count == 0 ? 0 : after.Max(r => r.GlobalSequence)))
            throw new InvalidOperationException("The posted journal changed while reconciling; retry with a stable ledger population.");
        return result;
    }

    private static ProviderShadowBookComparisonLineDto Compare(string dimension, string label, decimal? ledger,
        decimal? broker, decimal tolerance, string reason)
    {
        var variance = ledger.HasValue && broker.HasValue ? broker.Value - ledger.Value : (decimal?)null;
        return new(dimension, label, "posted-ledger", "provider-sync", ledger, broker, variance,
            variance is null ? ProviderLedgerReconciliationCheckStatusDto.Blocked
                : Math.Abs(variance.Value) <= tolerance ? ProviderLedgerReconciliationCheckStatusDto.Matched
                : ProviderLedgerReconciliationCheckStatusDto.Break, reason);
    }
}
