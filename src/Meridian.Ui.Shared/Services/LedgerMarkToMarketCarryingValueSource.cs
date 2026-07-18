using System.Globalization;
using Meridian.Application.Accounting;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Hydrates the requested ledger book once and returns the durable securities-account carrying
/// value for every requested security/account key. Missing accounts are explicit null results;
/// present zero-balance accounts remain zero.
/// </summary>
public sealed class LedgerMarkToMarketCarryingValueSource(ILedgerJournalStore store)
    : IMarkToMarketCarryingValueSource
{
    public async Task<IReadOnlyDictionary<MarkToMarketCarryingValueKey, MarkToMarketCarryingValue>> GetCarryingValuesAsync(
        MarkToMarketCarryingValueRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.LedgerBookId.HasValue || request.LedgerBookId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Daily valuation carrying-value hydration requires a ledger book id.");
        }

        var asOfUtc = request.AsOf.ToUniversalTime();
        var ledger = await store
            .HydrateLedgerAsOfAsync(request.LedgerBookId.Value, asOfUtc, ct: ct)
            .ConfigureAwait(false);
        var trialBalance = ledger.TrialBalanceAsOf(asOfUtc);
        var results = new Dictionary<MarkToMarketCarryingValueKey, MarkToMarketCarryingValue>();
        foreach (var position in request.Positions)
        {
            var key = MarkToMarketCarryingValueKey.FromPosition(position);
            var account = LedgerAccounts.Securities(key.Symbol, key.FinancialAccountId);
            var accountExists = trialBalance.TryGetValue(account, out var balance);
            var evidence = $"ledger://books/{request.LedgerBookId.Value:D}/accounts/{Uri.EscapeDataString(account.ToString())}/as-of/{Uri.EscapeDataString(asOfUtc.ToString("O", CultureInfo.InvariantCulture))}";
            results.Add(key, new MarkToMarketCarryingValue(
                accountExists ? balance : null,
                "durable-ledger-trial-balance",
                asOfUtc,
                evidence));
        }

        return results;
    }
}
