using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Export;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>The journal remains exportable but the requested lot evidence was not retained.</summary>
public sealed class LedgerExportEvidenceUnavailableException(string message) : InvalidOperationException(message);

/// <summary>Builds lot schedules from retained simulated portfolio evidence. No current market
/// quote is substituted for a missing historical snapshot.</summary>
public static class BacktestLedgerExportMapper
{
    public static LedgerExportRequest Build(BacktestResult result, DateTimeOffset periodStart,
        DateTimeOffset asOf, string outputDirectory, ExportFormat format, string? financialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var snapshot = result.Snapshots.LastOrDefault(snapshot => snapshot.Timestamp == asOf)
            ?? throw new LedgerExportEvidenceUnavailableException("Lot export is unavailable: no retained portfolio snapshot matches the requested as-of instant.");
        var open = new List<LedgerOpenLotExportRow>();
        var realized = new List<LedgerRealizedLotExportRow>();
        foreach (var account in snapshot.Accounts.Values)
        {
            if (financialAccountId is not null && !string.Equals(account.AccountId,
                    financialAccountId.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (account.OpenLots is null || account.ClosedLots is null)
                throw new LedgerExportEvidenceUnavailableException("Lot export is unavailable: retained account evidence does not include complete open and closed lots.");
            if (account.OpenLots.GroupBy(lot => lot.LotId).Any(group => group.Count() > 1) ||
                account.ClosedLots.GroupBy(lot => (lot.LotId, lot.CloseFillId)).Any(group => group.Count() > 1))
                throw new InvalidOperationException("Lot export is unavailable: retained account evidence contains duplicate lot or disposal identities.");
            foreach (var position in account.Positions.Values.Where(position => position.ExactQuantity != 0m))
            {
                var quantity = account.OpenLots.Where(lot => string.Equals(lot.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase))
                    .Sum(lot => lot.IsShort ? -lot.Quantity : lot.Quantity);
                if (quantity != position.ExactQuantity)
                    throw new InvalidOperationException("Lot export is unavailable: retained lots do not cover the full position quantity.");
            }
            foreach (var group in account.OpenLots.GroupBy(lot => lot.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                var lots = group.ToArray();
                if (!account.Positions.TryGetValue(group.Key, out var position) ||
                    lots.Select(lot => lot.IsShort).Distinct().Count() != 1 ||
                    lots.Select(lot => (lot.UnitKind, lot.Multiplier)).Distinct().Count() != 1)
                    throw new InvalidOperationException("Lot export is unavailable: open lots have no unambiguous retained position mark.");
                var isShort = lots[0].IsShort;
                var totalBasis = lots.Sum(lot => lot.CostBasis());
                var carrying = isShort ? LedgerAccounts.ShortSecuritiesPayable(group.Key, account.AccountId) :
                    LedgerAccounts.Securities(group.Key, account.AccountId);
                if (Math.Abs(result.Ledger.GetBalanceAsOf(carrying, asOf) - totalBasis) > 0.00000001m)
                    throw new InvalidOperationException("Lot export is unavailable: retained lot basis does not reconcile to the ledger.");
                var marketValue = isShort ? totalBasis - position.UnrealizedPnl : totalBasis + position.UnrealizedPnl;
                var exposure = lots.Sum(lot => lot.NotionalValue(1m));
                if (marketValue < 0m || exposure <= 0m)
                    throw new InvalidOperationException("Lot export is unavailable: retained position valuation is inconsistent.");
                var allocated = 0m;
                for (var index = 0; index < lots.Length; index++)
                {
                    var lot = lots[index];
                    // Position unrealized P&L retains the actual aggregate mark. Allocate that mark
                    // at a common instrument price, carrying the decimal residual on the final lot.
                    var value = index == lots.Length - 1 ? marketValue - allocated :
                        marketValue * lot.NotionalValue(1m) / exposure;
                    allocated += value;
                    if (lot.BasisComponents.Count == 0)
                    {
                        open.Add(new(lot.LotId.ToString(), lot.Symbol, account.AccountId,
                            DateOnly.FromDateTime(lot.OpenedAt.UtcDateTime), lot.Quantity, lot.CostBasis(), value, asOf, isShort));
                    }
                    else
                    {
                        if (lot.BasisComponents.Sum(component => component.SuccessorQuantity) != lot.Quantity)
                            throw new InvalidOperationException("Lot export is unavailable: composite source quantities do not reconcile.");
                        var componentAllocated = 0m;
                        for (var componentIndex = 0; componentIndex < lot.BasisComponents.Count; componentIndex++)
                        {
                            var component = lot.BasisComponents[componentIndex];
                            var componentValue = componentIndex == lot.BasisComponents.Count - 1 ? value - componentAllocated :
                                value * component.SuccessorQuantity / lot.Quantity;
                            componentAllocated += componentValue;
                            open.Add(new($"{lot.LotId}/{component.SourceLotId}/{componentIndex}", lot.Symbol, account.AccountId,
                                DateOnly.FromDateTime(component.OpenedAt.UtcDateTime), component.SuccessorQuantity,
                                component.AllocatedBasis, componentValue, asOf, isShort));
                        }
                    }
                }
            }
            foreach (var lot in account.ClosedLots.Where(lot => lot.ClosedAt > periodStart && lot.ClosedAt <= asOf))
            {
                var opening = lot.CostBasis ?? new LotQuantity(lot.Quantity, lot.UnitKind, lot.Multiplier).ValueAtPrice(lot.EntryPrice);
                var closing = new LotQuantity(lot.Quantity, lot.UnitKind, lot.Multiplier).ValueAtPrice(lot.ClosePrice);
                var acquired = DateOnly.FromDateTime(lot.OpenedAt.UtcDateTime);
                var sold = DateOnly.FromDateTime(lot.ClosedAt.UtcDateTime);
                // Report long-sale character by calendar anniversary. Short-sale tax character
                // requires additional authority and remains explicitly unclassified.
                var character = lot.IsShort ? "UnclassifiedShortSale" : sold > acquired.AddYears(1) ? "LongTerm" : "ShortTerm";
                var fees = lot.EntryCommission + lot.ExitCommission;
                realized.Add(new(lot.LotId.ToString(), lot.Symbol, account.AccountId, acquired, sold, lot.Quantity,
                    lot.IsShort ? opening : closing, lot.IsShort ? closing : opening, character,
                    -fees, fees == 0m ? null : "TradingFees", lot.ClosedAt, lot.IsShort, $"{lot.LotId}/{lot.CloseFillId}"));
            }
        }
        return new(result.Ledger, periodStart, asOf, outputDirectory, format, financialAccountId, open, realized);
    }
}
