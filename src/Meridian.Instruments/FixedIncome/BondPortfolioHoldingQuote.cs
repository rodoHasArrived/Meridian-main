using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Accepted statement invoices establish a dated valuation mark, never executable liquidity.</summary>
internal static class BondPortfolioHoldingQuote
{
    internal static (BondWorkflowRequest Request, BondCalculationInput Authority, decimal Dirty) Bind(
        BondWorkflowRequest request, AccountBondHoldingsSnapshotDto snapshot, CustodianPositionLineDto[] rows)
    {
        if (rows.Select(row => row.Currency).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new ArgumentException("Accepted holding quotation has competing native currencies.");
        var original = rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.OriginalFace) : row.Bond!.OriginalFace);
        var current = rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace);
        if (current == 0m || Math.Sign(current) != Math.Sign(original))
            throw new ArgumentException("Accepted holding quotation has no consistent signed current-face basis.");
        var invoices = rows.Select(row =>
        {
            var invoice = BondHoldingNormalization.Calculate(row.Bond!, row.Currency);
            var direction = row.IsShort && row.Bond!.OriginalFace > 0m ? -1m : 1m;
            return (Clean: direction * invoice.Clean, Dirty: direction * invoice.Dirty);
        }).ToArray();
        // The invoice normalizer applies inflation once. Converting its nominal cash to a
        // quotation on unindexed current face prevents a second index-ratio multiplication.
        var nominalQuote = invoices.Sum(row => row.Clean) / current * 100m;
        if (nominalQuote < 0m)
            throw new ArgumentException("Accepted holding quotation does not establish a nonnegative nominal clean price.");
        var selected = request with { CleanPricePer100 = nominalQuote, QuoteSettlementDate = snapshot.AsOfDate,
            PriceBasis = BondWorkflowPriceBasis.NominalClean,
            ConfirmationEvidenceReference = $"accepted-holdings:{snapshot.BatchId:D}:{snapshot.SourceContentHash}" };
        var authority = new BondCalculationInput("AcceptedHoldingInvoice", snapshot.BatchId.ToString("D"),
            ContentSha256: snapshot.SourceContentHash,
            AsOf: new DateTimeOffset(snapshot.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        return (selected, authority, Math.Sign(original) * invoices.Sum(row => row.Dirty));
    }
}
