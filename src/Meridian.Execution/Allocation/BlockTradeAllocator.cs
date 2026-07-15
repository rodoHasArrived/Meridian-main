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
        PostToDestinationLedgers(book, result, isBuy, static (ledgerBook, destinationId) =>
            ledgerBook.SleeveLedger(destinationId));
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
        PostToDestinationLedgers(book, result, isBuy, static (ledgerBook, destinationId) =>
            ledgerBook.EntityLedger(destinationId));
    }

    private static void PostToDestinationLedgers(
        FundLedgerBook book,
        AllocationResult result,
        bool isBuy,
        Func<FundLedgerBook, string, Meridian.Ledger.Ledger> resolveLedger)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(resolveLedger);

        foreach (var slice in result.Slices)
        {
            if (slice.AllocatedQuantity == 0)
                continue;

            resolveLedger(book, slice.DestinationId).PostLines(
                result.AllocatedAt,
                BuildDescription(result, slice, isBuy),
                BuildPostingLines(result, slice, isBuy));
        }
    }

    private static string BuildDescription(AllocationResult result, AllocationSlice slice, bool isBuy) =>
        isBuy
            ? $"Block buy {slice.AllocatedQuantity} {result.Symbol} @ {result.FillPrice:F4} [{slice.DestinationId}]"
            : $"Block sell {slice.AllocatedQuantity} {result.Symbol} @ {result.FillPrice:F4} [{slice.DestinationId}]";

    private static (LedgerAccount Account, decimal Debit, decimal Credit)[] BuildPostingLines(
        AllocationResult result,
        AllocationSlice slice,
        bool isBuy)
    {
        var securitiesAccount = LedgerAccounts.Securities(result.Symbol);
        var cashAccount = LedgerAccounts.Cash;

        return isBuy
            ? [
                (securitiesAccount, slice.ProRataValue, 0m),
                (cashAccount, 0m, slice.ProRataValue)
            ]
            : [
                (cashAccount, slice.ProRataValue, 0m),
                (securitiesAccount, 0m, slice.ProRataValue)
            ];
    }
}
