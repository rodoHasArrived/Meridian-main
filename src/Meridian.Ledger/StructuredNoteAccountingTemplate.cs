using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ledger;

/// <summary>
/// Structured notes recognize changes from retained carrying value in profit or loss. The daily
/// valuation route supplies dated marks and approval evidence; this template never accrues coupons.
/// </summary>
public static class StructuredNoteAccountingTemplate
{
    public const string TemplateId = "structured-note-fvtpl-v1";

    public static bool AppliesTo(string? assetClass)
        => SecurityAssetClassCatalog.TryGet(assetClass, out var descriptor)
            && descriptor.ValuationAccountingTreatment == SecurityValuationAccountingTreatment.FairValueThroughProfitOrLoss;

    internal static decimal ValueAtQuote(DailyPortfolioPriceMark mark, decimal price)
    {
        if (mark.SecurityId is null)
            throw new InvalidOperationException("Structured-note valuation requires a canonical Security Master identity.");

        return LotQuantity.For(mark.InstrumentType, mark.Quantity)
            .ValueAtPrice(price, LotQuantity.QuoteConventionFor(mark.InstrumentType));
    }

    /// <summary>
    /// The reusable gain or loss template. Its amount is the absolute change in monetary carrying
    /// value, already converted from the class's raw quote convention by the valuation projector.
    /// Generated instances remain subject to the existing governed valuation draft lifecycle.
    /// </summary>
    public static JournalTemplate Create(string symbol, string? financialAccountId, bool gain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var carrying = LedgerAccounts.Securities(symbol, financialAccountId);
        var scope = financialAccountId ?? symbol;
        return new JournalTemplate(
            $"{TemplateId}-{(gain ? "gain" : "loss")}",
            "Structured note fair value through profit or loss",
            "Recognize the change from retained carrying value using a dated valuation mark.",
            gain
                ? [new(carrying, JournalTemplateSide.Debit, "markAdjustment"),
                    new(LedgerAccounts.UnrealizedGainFor(scope), JournalTemplateSide.Credit, "markAdjustment")]
                : [new(LedgerAccounts.UnrealizedLossFor(scope), JournalTemplateSide.Debit, "markAdjustment"),
                    new(carrying, JournalTemplateSide.Credit, "markAdjustment")]);
    }
}
