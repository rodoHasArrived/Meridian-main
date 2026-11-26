using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Strategies.Services;

/// <summary>Projects posted native effects and already-applied asset receipts, including zero-cash splits.</summary>
public static class StrategyPositionAccountingProjector
{
    public static IReadOnlyList<StrategyPositionAccountingEvidence> Project(IReadOnlyList<JournalEntry> journals,
        IReadOnlyList<AssetEventCashFlow> receipts, IChartOfAccounts? chart = null)
    {
        chart ??= StandardChartOfAccounts.Instance;
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var journal in journals)
            ledger.Post(journal);
        var result = new List<StrategyPositionAccountingEvidence>();
        foreach (var summary in ledger.SummarizeAccounts())
        {
            if (!chart.TryGetRegistered(summary.Account, out var role)
                || role is not (LedgerAccountRole.Securities or LedgerAccountRole.ShortSecuritiesPayable))
                continue;
            var account = summary.Account;
            var isShort = role == LedgerAccountRole.ShortSecuritiesPayable;
            var sources = journals.Where(journal => journal.Lines.Any(line => line.Account == account)).ToArray();
            var effects = sources.Where(journal => !IsValuation(journal)).ToArray();
            var events = receipts.Where(flow => string.Equals(flow.AccountId, account.FinancialAccountId, StringComparison.OrdinalIgnoreCase)
                && (flow.UnitsImpacted < 0m) == isShort
                && (string.Equals(flow.Symbol, account.Symbol, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(flow.RelatedSymbol, account.Symbol, StringComparison.OrdinalIgnoreCase))).ToArray();
            var quantity = 0m;
            string? failure = null;
            var units = new HashSet<SecurityLotUnitKind>();
            var multipliers = new HashSet<decimal>();
            foreach (var journal in effects)
            {
                if (SecurityTradePositionEvidence.TryRead(journal, out var effect, out var problem, chart) && effect is not null)
                {
                    quantity += isShort ? -effect.ShortQuantityChange : effect.LongQuantityChange;
                    units.Add(effect.QuantityUnit);
                    multipliers.Add(effect.Multiplier);
                }
                else if (journal.Metadata.ActivityType?.StartsWith("asset_event_", StringComparison.Ordinal) != true
                    || !events.Any(flow => flow.Timestamp == journal.Timestamp && flow.Symbol == journal.Metadata.Symbol
                        && string.Equals(flow.AccountId, journal.Metadata.FinancialAccountId, StringComparison.OrdinalIgnoreCase)))
                    failure ??= problem ?? "The journal has no supported position effect.";
            }
            foreach (var flow in events)
            {
                if (flow.PositionEvidenceVersion != 1 || flow.QuantityUnit is null || flow.LotMultiplier is null || flow.FunctionalCurrency is null)
                {
                    failure ??= "A processed asset receipt lacks versioned native-unit or currency evidence.";
                    continue;
                }
                units.Add(flow.QuantityUnit.Value);
                multipliers.Add(flow.LotMultiplier.Value);
                var target = string.IsNullOrWhiteSpace(flow.RelatedSymbol) ? flow.Symbol : flow.RelatedSymbol;
                var successor = flow.UnitsImpacted * flow.PositionFactor - flow.FractionalUnits;
                if (string.Equals(flow.Symbol, account.Symbol, StringComparison.OrdinalIgnoreCase))
                    quantity -= flow.UnitsImpacted;
                if (string.Equals(target, account.Symbol, StringComparison.OrdinalIgnoreCase))
                    quantity += successor;
            }
            var currencies = sources.SelectMany(journal => journal.Lines.Where(line => line.Account == account))
                .Select(static line => line.Currency?.FunctionalCurrency).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var currency = currencies.Length == 1 ? currencies[0] : null;
            if (units.Count != 1 || multipliers.Count != 1 || multipliers.SingleOrDefault() <= 0m
                || currency is null || events.Any(flow => !string.Equals(flow.FunctionalCurrency, currency, StringComparison.OrdinalIgnoreCase)))
                failure ??= "Native units, multipliers or functional currency are missing or disagree.";
            if (isShort ? quantity > 0m : quantity < 0m)
                failure ??= "The retained position effects have an inconsistent direction.";
            var ids = sources.SelectMany(journal => journal.Lines.Where(line => line.Account == account)
                .Select(line => line.Dimensions?.InstrumentId
                    ?? (string.Equals(line.Account.Symbol, journal.Metadata.Symbol, StringComparison.OrdinalIgnoreCase) ? journal.Metadata.SecurityId : null))).Distinct().ToArray();
            if (ids.Where(static id => id.HasValue).Distinct().Count() > 1)
                failure ??= "Position journals disagree on canonical security identity.";
            var marked = sources.Any(IsValuation);
            result.Add(new(account.Symbol!, account.FinancialAccountId, ids.Length == 1 ? ids[0] : null, isShort,
                failure is null ? quantity : null, marked ? null : summary.Balance,
                units.Count == 1 ? units.Single().ToString() : null, multipliers.Count == 1 ? multipliers.Single() : null,
                currency, failure ?? (marked ? "Valuation journals require canonical historical lot-basis evidence." : null),
                sources.Select(static journal => $"journal:{journal.JournalEntryId:D}")
                    .Concat(events.Select(flow => $"processed-asset:{flow.AccountId}:{flow.Symbol}:{flow.Timestamp:O}:{flow.EventType}")).ToArray()));
        }
        return result;
    }

    public static bool IsValuation(JournalEntry entry) => entry.Metadata.ActivityType == "fair-value-mark"
        || entry.Metadata.Tags?.Keys.Any(static key => key.StartsWith("valuation.", StringComparison.OrdinalIgnoreCase)) == true;
}
