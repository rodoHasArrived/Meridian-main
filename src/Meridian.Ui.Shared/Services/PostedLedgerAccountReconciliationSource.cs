using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compares broker evidence with posted cash and canonical open lots. Carrying-value marks are
/// excluded from cost basis only after validating their retained evidence. Incomplete acquisition
/// coverage or a moved read population is unavailable,
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
                if ((brokerRows ?? []).Any(p => p.AssetClass.ToLowerInvariant() is not (AlpacaAssetClassCodes.UsEquity or "equity" or "stock" or "stk" or "etf")))
                    throw new InvalidOperationException("Provider quantity and average-cost conventions require explicit instrument multipliers; this comparison supports unit-priced equities.");
                if (futureActivity)
                    throw new InvalidOperationException("Later journal activity prevents using current open lots at the earlier broker cutoff; capture a fresh broker snapshot.");
                var accounts = lines.Where(l => l.Account.Symbol == symbol && l.Account.Name is "Securities" or "Short Securities Payable")
                    .Select(l => l.Account)
                    .Concat(new[] { LedgerAccounts.Securities(symbol, account), LedgerAccounts.ShortSecuritiesPayable(symbol, account) })
                    .Distinct().ToArray();
                var lots = new List<LedgerTaxLotRecord>();
                foreach (var lotAccount in accounts)
                {
                    var retained = await store.ListOpenTaxLotsAsync(book.LedgerBookId, lotAccount, ct);
                    if (retained.Any(l => l.LedgerBookId != book.LedgerBookId || l.Account != lotAccount))
                        throw new InvalidOperationException("Canonical lot evidence does not match the selected book and exact account identity.");
                    lots.AddRange(retained);
                }
                if (lots.Select(l => l.TaxLotRecordId).Distinct().Count() != lots.Count)
                    throw new InvalidOperationException("Canonical lot identities are duplicated across the selected position population.");
                if (lots.Any(l => l.CreatedAt > provider.SyncedAt || l.UpdatedAt > provider.SyncedAt || l.AcquiredDate > DateOnly.FromDateTime(provider.SyncedAt.UtcDateTime)))
                    throw new InvalidOperationException("Current lot evidence is later than the broker observation.");
                var canonical = lots.Select(l => l.ToOpenLot()).ToArray();
                if (canonical.Any(l => l.Acquisition.QuantityBasis != LotQuantityBasis.Units))
                    throw new InvalidOperationException("The broker quantity convention is not certified against this face-denominated lot population.");
                if (canonical.Any(l => !string.Equals(l.Acquisition.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)) ||
                    (brokerRows ?? []).Any(p => !string.Equals(p.Currency ?? provider.Balance?.Currency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Position cost comparison requires the same currency or retained FX conversion evidence.");
                EnsureAcquisitionCoverage(book, symbol, eligible, accounts, lots, brokerRows ?? []);
                quantity = canonical.Sum(l => l.Acquisition.Direction == OpenLotDirection.Short ? -l.OpenQuantity : l.OpenQuantity);
                cost = canonical.Sum(l => l.OpenFunctionalCostBasis);
                reason += " Quantity and cost use canonical acquisitions covering every retained position origin; validated valuation marks do not change acquisition basis.";
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

    private static void EnsureAcquisitionCoverage(LedgerBookDto book, string symbol,
        IReadOnlyList<LedgerJournalEntryRecord> records, IReadOnlyCollection<LedgerAccount> accounts,
        IReadOnlyList<LedgerTaxLotRecord> lots, IReadOnlyList<FundAccountBrokeragePositionDto> brokerRows)
    {
        // A nonempty open-lot subset is not proof of the account's total position. Do not invent
        // opening lots, or infer historical disposal/restatement allocations from a net balance.
        if (lots.Any(l => l.OpenQuantity != l.OriginalQuantity || l.BasisAdjustment is not null
            || l.Acquisition?.CorporateActionLineage is not null || l.OriginatingMutationBatchId != l.LastMutationBatchId))
            throw new InvalidOperationException("Historical lot mutations require complete retained population coverage before quantity or cost can be compared.");
        if (lots.Select(l => l.SecurityId).Distinct().Count() > 1
            || brokerRows.Any(p => p.Security is { } security && lots.Any(l => l.SecurityId != security.SecurityId)))
            throw new InvalidOperationException("The broker symbol and canonical lots do not identify one exact instrument.");

        foreach (var lot in lots)
        {
            var origins = records.Where(r => r.Entry.JournalEntryId == lot.SourceJournalEntryId).ToArray();
            if (origins.Length != 1)
                throw new InvalidOperationException("A canonical lot lacks one retained acquisition origin in the selected book and cutoff.");
            var origin = origins[0].Entry;
            var originLines = origin.Lines.Where(l => l.Account == lot.Account).ToArray();
            if (origin.Metadata.SecurityId != lot.SecurityId || originLines.Length == 0
                || originLines.Any(l => l.Dimensions?.InstrumentId != lot.SecurityId || l.Dimensions?.PositionId != lot.BookPositionId)
                || originLines.Any(l => l.Dimensions?.AccountId is { } financialAccountId
                    && !string.Equals(financialAccountId, lot.Account.FinancialAccountId, StringComparison.OrdinalIgnoreCase))
                || ValuationMarkEvidenceGuard.IsValuation(origin.Metadata.IdempotencyKey) || origin.Metadata.ActivityType == "fair-value-mark"
                || (origin.Metadata.EffectiveDate ?? DateOnly.FromDateTime(origin.Timestamp.UtcDateTime)) != lot.AcquiredDate)
                throw new InvalidOperationException("Canonical acquisition origin does not match the retained instrument, position, account and acquisition date.");
            if ((lot.Account.Name == "Short Securities Payable") != (lot.Acquisition!.Direction == OpenLotDirection.Short))
                throw new InvalidOperationException("Canonical acquisition direction does not match its securities account.");
        }

        foreach (var record in records)
        {
            var entry = record.Entry;
            foreach (var component in entry.Lines.Where(l => accounts.Contains(l.Account)).GroupBy(l => l.Account))
            {
                if (IsValidatedValuationOnly(book, symbol, entry, component.Key, lots))
                    continue;
                var acquisitions = lots.Where(l => l.SourceJournalEntryId == entry.JournalEntryId && l.Account == component.Key).ToArray();
                var shortPosition = component.Key.Name == "Short Securities Payable";
                if (acquisitions.Length == 0 || !entry.IsBalanced
                    || component.Any(l => shortPosition ? l.Debit != 0m : l.Credit != 0m)
                    || component.Any(l => l.Currency is { } currency && !string.Equals(currency.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                    || component.Sum(l => shortPosition ? l.Credit : l.Debit) != acquisitions.Sum(l => l.Acquisition!.FunctionalCostBasis))
                    throw new InvalidOperationException("The posted position lacks complete canonical acquisition coverage for every retained origin and account component; an opening balance or other unsupported history remains unavailable.");
            }
        }
    }

    private static bool IsValidatedValuationOnly(LedgerBookDto book, string symbol, JournalEntry entry,
        LedgerAccount account, IReadOnlyList<LedgerTaxLotRecord> lots)
    {
        var gainAccount = LedgerAccounts.UnrealizedGainFor(account.FinancialAccountId ?? symbol);
        var lossAccount = LedgerAccounts.UnrealizedLossFor(account.FinancialAccountId ?? symbol);
        if ((!ValuationMarkEvidenceGuard.IsValuation(entry.Metadata.IdempotencyKey) && entry.Metadata.ActivityType != "fair-value-mark")
            || !entry.IsBalanced || entry.Metadata.SecurityId is not { } securityId
            || account != LedgerAccounts.Securities(symbol, account.FinancialAccountId)
            || !lots.Any(l => l.Account == account && l.SecurityId == securityId)
            || !string.Equals(entry.Metadata.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            || entry.Lines.Any(l => l.Account != account && l.Account != gainAccount && l.Account != lossAccount)
            || entry.Lines.All(l => l.Account == account)
            || entry.Lines.Any(l => l.Currency is null || !l.Currency.IsFunctionalCurrency
                || !string.Equals(l.Currency.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)))
            return false;
        var entityIds = entry.Lines.Select(l => l.Dimensions?.EntityId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var positionLines = entry.Lines.Where(l => l.Account == account).ToArray();
        if (entityIds.Length != 1 || entry.Lines.Any(l => l.Dimensions is null
                || l.Dimensions.FundId != book.FundProfileId || l.Dimensions.InstrumentId != securityId
                || l.Dimensions.AccountId != account.FinancialAccountId)
            || entry.Lines.Any(l => l.Account == gainAccount && l.Debit != 0m || l.Account == lossAccount && l.Credit != 0m)
            || positionLines.Sum(l => l.Debit) != entry.Lines.Where(l => l.Account == gainAccount).Sum(l => l.Credit)
            || positionLines.Sum(l => l.Credit) != entry.Lines.Where(l => l.Account == lossAccount).Sum(l => l.Debit))
            return false;
        return ValuationMarkEvidenceGuard.Validate(
            entry.Metadata.Tags?.GetValueOrDefault(ValuationMarkEvidenceGuard.EvidenceTag), book.FundProfileId,
            entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(entry.Timestamp.UtcDateTime), securityId,
            account.FinancialAccountId, symbol, entry.Metadata.Tags?.GetValueOrDefault(ValuationMarkEvidenceGuard.DigestTag), entityIds[0]) is null;
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
