using Meridian.Ledger;

namespace Meridian.Execution.Allocation;

/// <summary>
/// Applies an <see cref="AllocationResult"/> to a <see cref="FundLedgerBook"/> by posting
/// proportional buy/sell journal entries to each destination sleeve or entity ledger.
/// </summary>
/// <remarks>
/// <para>
/// The allocator posts to sleeve ledgers by default. It uses the
/// <see cref="LedgerAccounts.AllocationControl"/> clearing account to ensure the parent
/// fund ledger remains balanced: each sleeve debit has a corresponding credit on the
/// fund-level allocation control account, and the net effect across all sleeve postings
/// is zero on the fund ledger.
/// </para>
/// <para>
/// Usage pattern:
/// <code>
/// var engine = new ProportionalAllocationEngine();
/// var result = engine.Allocate("AAPL", 1000, 180m, rule, DateTimeOffset.UtcNow);
/// BlockTradeAllocator.PostToFundLedger(book, result, side: OrderSide.Buy);
/// </code>
/// </para>
/// </remarks>
public static class BlockTradeAllocator
{
    /// <summary>
    /// Posts sleeve-level journal entries for a block-trade allocation.
    /// Each destination in <paramref name="result"/> maps to a sleeve ledger accessed via
    /// <paramref name="book"/>.
    /// </summary>
    /// <param name="book">The fund ledger book owning the sleeve ledgers.</param>
    /// <param name="result">The allocation result produced by an <see cref="IAllocationEngine"/>.</param>
    /// <param name="isBuy">
    ///     <c>true</c> when the block trade is a purchase (Dr Securities / Cr Cash);
    ///     <c>false</c> when it is a sale (Dr Cash / Cr Securities).
    /// </param>
    public static void PostToSleeveLedgers(
        FundLedgerBook book,
        AllocationResult result,
        bool isBuy)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(result);

        foreach (var slice in result.Slices)
        {
            if (slice.AllocatedQuantity == 0)
                continue;

            var sleeveLedger = book.SleeveLedger(slice.DestinationId);
            var securitiesAccount = LedgerAccounts.Securities(result.Symbol);
            var cashAccount = LedgerAccounts.Cash;

            var description = isBuy
                ? $"Block buy {slice.AllocatedQuantity} {result.Symbol} @ {result.FillPrice:F4} [{slice.DestinationId}]"
                : $"Block sell {slice.AllocatedQuantity} {result.Symbol} @ {result.FillPrice:F4} [{slice.DestinationId}]";

            if (isBuy)
            {
                sleeveLedger.PostLines(
                    result.AllocatedAt,
                    description,
                    [
                        (securitiesAccount, slice.ProRataValue, 0m),
                        (cashAccount, 0m, slice.ProRataValue)
                    ]);
            }
            else
            {
                sleeveLedger.PostLines(
                    result.AllocatedAt,
                    description,
                    [
                        (cashAccount, slice.ProRataValue, 0m),
                        (securitiesAccount, 0m, slice.ProRataValue)
                    ]);
            }
        }
    }

    /// <summary>
    /// Posts entity-level journal entries for a block-trade allocation.
    /// Each destination in <paramref name="result"/> maps to an entity ledger accessed via
    /// <paramref name="book"/>.
    /// </summary>
    public static void PostToEntityLedgers(
        FundLedgerBook book,
        AllocationResult result,
        bool isBuy)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(result);

        foreach (var slice in result.Slices)
        {
            if (slice.AllocatedQuantity == 0)
                continue;

            var entityLedger = book.EntityLedger(slice.DestinationId);
            var securitiesAccount = LedgerAccounts.Securities(result.Symbol);
            var cashAccount = LedgerAccounts.Cash;

            var description = isBuy
                ? $"Block buy {slice.AllocatedQuantity} {result.Symbol} @ {result.FillPrice:F4} [{slice.DestinationId}]"
                : $"Block sell {slice.AllocatedQuantity} {result.Symbol} @ {result.FillPrice:F4} [{slice.DestinationId}]";

            if (isBuy)
            {
                entityLedger.PostLines(
                    result.AllocatedAt,
                    description,
                    [
                        (securitiesAccount, slice.ProRataValue, 0m),
                        (cashAccount, 0m, slice.ProRataValue)
                    ]);
            }
            else
            {
                entityLedger.PostLines(
                    result.AllocatedAt,
                    description,
                    [
                        (cashAccount, slice.ProRataValue, 0m),
                        (securitiesAccount, 0m, slice.ProRataValue)
                    ]);
            }
        }
    }
}
