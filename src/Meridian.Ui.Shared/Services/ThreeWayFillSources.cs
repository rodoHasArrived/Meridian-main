using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Projects fill-level operands for the three-way comparison: broker fills from the retained
/// activity history, and Meridian fills from posted trade-fill and commission journals.
/// </summary>
internal static class ThreeWayFillSources
{
    private const string TradeFillActivityType = "trade-fill";
    private const string CommissionActivityType = "trade-commission";

    /// <summary>
    /// Broker fills executed in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>]. Each fill keeps the
    /// provider activity id and broker order id; the client order id is added when an order row maps it.
    /// </summary>
    public static IReadOnlyList<ThreeWayFillDto> FromBroker(
        IEnumerable<FundAccountBrokerageFillDto> fills,
        IEnumerable<FundAccountBrokerageOrderDto> orders,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string evidenceReference)
    {
        var clientOrderIds = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.ClientOrderId))
            .GroupBy(order => order.OrderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ClientOrderId!, StringComparer.Ordinal);
        return fills
            .Where(fill => fill.FilledAt >= fromUtc && fill.FilledAt <= toUtc)
            .OrderBy(fill => fill.FilledAt).ThenBy(fill => fill.FillId, StringComparer.Ordinal)
            .Select(fill => new ThreeWayFillDto(
                "broker:" + fill.FillId, fill.Symbol, NormalizeSide(fill.Side), Math.Abs(fill.Quantity), fill.Price,
                fill.Commission is { } commission ? Math.Abs(commission) : null, fill.FilledAt.ToUniversalTime(), evidenceReference)
            {
                BrokerActivityId = fill.FillId,
                BrokerOrderId = fill.OrderId,
                ClientOrderId = fill.OrderId is { } orderId && clientOrderIds.TryGetValue(orderId, out var client) ? client : null
            })
            .ToArray();
    }

    /// <summary>
    /// Meridian fills posted for <paramref name="financialAccountId"/> in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>].
    /// Returns <c>Complete = false</c> when a trade-fill journal lacks the identity or quantity evidence
    /// needed to compare it, so an unreadable posting is reported instead of silently skipped.
    /// </summary>
    public static (IReadOnlyList<ThreeWayFillDto> Fills, bool Complete) FromLedger(
        IEnumerable<LedgerJournalEntryRecord> journals,
        string financialAccountId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string evidenceReference)
    {
        var accountJournals = journals
            .Select(record => record.Entry)
            .Where(entry => entry.Timestamp >= fromUtc && entry.Timestamp <= toUtc)
            .Where(entry => BelongsTo(entry, financialAccountId))
            .ToArray();
        var commissions = accountJournals
            .Where(entry => entry.Metadata.ActivityType == CommissionActivityType && entry.Metadata.FillId is not null)
            .GroupBy(entry => entry.Metadata.FillId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var complete = true;
        var fills = new List<ThreeWayFillDto>();
        foreach (var entry in accountJournals.Where(entry => entry.Metadata.ActivityType == TradeFillActivityType))
        {
            var tags = entry.Metadata.Tags;
            var cash = entry.Lines.Where(line => IsCash(line.Account)).Sum(line => line.Debit - line.Credit);
            if (entry.Metadata.FillId is not { } fillId || string.IsNullOrWhiteSpace(entry.Metadata.Symbol)
                || tags is null || !tags.TryGetValue("quantity", out var quantityText)
                || !decimal.TryParse(quantityText, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var quantity)
                || quantity == 0m || cash == 0m)
            {
                complete = false;
                continue;
            }

            quantity = Math.Abs(quantity);
            var journalIds = new List<string> { entry.JournalEntryId.ToString("D") };
            decimal? commission = null;
            if (commissions.TryGetValue(fillId, out var commissionJournals))
            {
                commission = commissionJournals.Sum(journal => journal.Lines
                    .Where(line => line.Account.AccountType == LedgerAccountType.Expense).Sum(line => line.Debit - line.Credit));
                journalIds.AddRange(commissionJournals.Select(journal => journal.JournalEntryId.ToString("D")));
            }

            fills.Add(new ThreeWayFillDto(
                "ledger:" + fillId.ToString("D"), entry.Metadata.Symbol!,
                // Cash leaves the account on a buy and arrives on a sell; commission is a separate journal.
                cash < 0m ? "Buy" : "Sell", quantity, Math.Abs(cash) / quantity, commission,
                entry.Timestamp.ToUniversalTime(), evidenceReference)
            {
                BrokerActivityId = tags.TryGetValue("brokerActivityId", out var activityId) && !string.IsNullOrWhiteSpace(activityId)
                    ? activityId : null,
                ClientOrderId = tags.TryGetValue("source.orderId", out var orderId) && !string.IsNullOrWhiteSpace(orderId)
                    ? orderId : null,
                JournalEntryIds = journalIds
            });
        }

        return (fills.OrderBy(fill => fill.ExecutedAtUtc).ThenBy(fill => fill.FillKey, StringComparer.Ordinal).ToArray(), complete);
    }

    internal static string NormalizeSide(string side)
        => side.Trim().ToUpperInvariant() switch
        {
            "BUY" or "B" => "Buy",
            "SELL" or "S" or "SELL_SHORT" or "SELLSHORT" => "Sell",
            var other => other
        };

    private static bool BelongsTo(JournalEntry entry, string financialAccountId)
        => string.Equals(entry.Metadata.FinancialAccountId, financialAccountId, StringComparison.OrdinalIgnoreCase)
            || entry.Lines.Any(line => string.Equals(line.Account.FinancialAccountId ?? line.Dimensions?.AccountId,
                financialAccountId, StringComparison.OrdinalIgnoreCase));

    private static bool IsCash(LedgerAccount account)
        => account.AccountType == LedgerAccountType.Asset && account.Name.StartsWith("Cash", StringComparison.OrdinalIgnoreCase);
}
