namespace Meridian.Ledger;

/// <summary>One lot relieved by a retained disposal, as recorded at the time it was booked.</summary>
/// <param name="HoldingPeriodStart">
/// The lot's effective holding-period start — its acquisition date unless an earlier wash sale
/// carried a start onto it.
/// </param>
public sealed record LedgerTaxLotDisposalHistoryLot(
    string LotId,
    DateOnly AcquiredDate,
    DateOnly HoldingPeriodStart,
    decimal Quantity,
    decimal UnitCost,
    decimal CostBasis);

/// <summary>
/// A disposal as retained in durable tax-lot history, with enough of the original economics to
/// rebuild the relief projection that produced it.
/// </summary>
/// <param name="RecognizedGainOrLoss">
/// The gain or loss actually booked to the ledger by this disposal (gain credits less loss debits).
/// It excludes any wash-sale deferral, which is why <paramref name="WashSaleBasisIncreases"/> is
/// needed to recover the full economic result.
/// </param>
/// <param name="WashSaleBasisIncreases">Deferrals retained against this disposal, empty when none applied.</param>
/// <param name="MatchedReplacementQuantity">Replacement shares that drove the deferral, or zero when none applied.</param>
public sealed record LedgerTaxLotDisposalHistory(
    Guid MutationBatchId,
    Guid JournalEntryId,
    LedgerAccount Account,
    DateOnly SaleDate,
    LedgerTaxLotReliefMethod ReliefMethod,
    IReadOnlyList<LedgerTaxLotDisposalHistoryLot> Lots,
    decimal RecognizedGainOrLoss,
    IReadOnlyList<WashSaleBasisIncrease> WashSaleBasisIncreases,
    decimal MatchedReplacementQuantity)
{
    /// <summary>Total wash-sale loss deferred out of this disposal.</summary>
    public decimal DisallowedWashSaleLoss
        => WashSaleBasisIncreases.Sum(static increase => increase.Amount);
}

/// <summary>
/// Rebuilds <see cref="LedgerTaxLotReliefProjection"/>s from retained disposal history so a governed
/// report pack can carry real realized-gain rows.
/// <para>
/// The pack's tax-lot artifact accepts projections but production never supplied any, so it shipped
/// as a header with no rows — including a <c>DisallowedWashSaleLoss</c> column that could never
/// report anything. This projector closes that gap from durable history rather than requiring the
/// original in-memory projection to have been kept.
/// </para>
/// </summary>
public static class LedgerTaxLotReliefHistoryProjector
{
    /// <summary>
    /// Rebuilds one disposal's projection, or returns <c>null</c> when the retained economics cannot
    /// produce a well-formed one. Returning null rather than throwing is deliberate: a report pack
    /// must not fail to generate because a single historical row is unreconstructable — the caller
    /// reports the omission instead.
    /// </summary>
    public static LedgerTaxLotReliefProjection? Project(LedgerTaxLotDisposalHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Lots.Count == 0)
            return null;

        var quantitySold = history.Lots.Sum(static lot => lot.Quantity);
        if (quantitySold <= 0m)
            return null;

        // Proceeds are recovered rather than stored: recognized = proceeds - basis - deferred, so
        // proceeds = basis + recognized + deferred. Deriving them from the booked gain/loss keeps the
        // rebuilt rows tied to the journal instead of to a separately-recorded price that could drift.
        var retainedCostBasis = history.Lots.Sum(static lot => lot.CostBasis);
        var disallowed = history.DisallowedWashSaleLoss;
        var proceeds = retainedCostBasis + history.RecognizedGainOrLoss - disallowed;
        if (proceeds < 0m)
            return null;

        var salePrice = proceeds / quantitySold;

        LedgerTaxLotReliefProjection projection;
        try
        {
            projection = LedgerTaxLotReliefProjector.Project(BuildInput(history, quantitySold, salePrice));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Retained history that no longer satisfies the relief engine's invariants (a zero-quantity
            // lot, a duplicate SpecificId selection) is skipped rather than aborting the whole pack.
            return null;
        }

        if (history.WashSaleBasisIncreases.Count == 0)
            return projection;

        // The retained outcome is attached rather than recomputed: the pack must report the deferral
        // that was actually booked, not one re-derived from today's replacement history.
        var economicLoss = projection.RealizedGainOrLoss < 0m ? -projection.RealizedGainOrLoss : 0m;
        return projection with
        {
            WashSale = new WashSaleOutcome(
                disallowed,
                Math.Max(0m, economicLoss - disallowed),
                history.MatchedReplacementQuantity,
                history.WashSaleBasisIncreases),
        };
    }

    private static LedgerTaxLotReliefInput BuildInput(
        LedgerTaxLotDisposalHistory history,
        decimal quantitySold,
        decimal salePrice)
        => new(
            history.Account,
            history.SaleDate,
            quantitySold,
            RoundUnitPrice(salePrice),
            history.ReliefMethod,
            history.Lots
                .Select(static lot => new LedgerTaxLot(
                    lot.LotId,
                    lot.AcquiredDate,
                    lot.Quantity,
                    lot.UnitCost,
                    securityId: null,
                    // Guard the invariant rather than trusting history: a start later than
                    // acquisition would throw, and it can only mean the row predates the carry.
                    holdingPeriodStartDate: lot.HoldingPeriodStart <= lot.AcquiredDate
                        ? lot.HoldingPeriodStart
                        : null))
                .ToArray(),
            financialAccountId: history.Account.FinancialAccountId,
            // Every retained lot is fully consumed by this disposal, so lot-discrete methods all
            // select the same set; SpecificId additionally needs them named in relief order.
            specificLotIds: history.ReliefMethod == LedgerTaxLotReliefMethod.SpecificId
                ? history.Lots.Select(static lot => lot.LotId).ToArray()
                : null);

    /// <summary>
    /// Rounds a recovered per-unit price to a precision that survives re-multiplication by the sold
    /// quantity. Currency rounding would lose too much on a low-priced, high-quantity disposal.
    /// </summary>
    private static decimal RoundUnitPrice(decimal salePrice)
        => decimal.Round(salePrice, 10, MidpointRounding.AwayFromZero);
}
