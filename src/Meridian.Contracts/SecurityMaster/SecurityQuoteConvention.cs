namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// How a raw unit quote scales into money for a class's lots: the declared catalog fact behind
/// the pricing conversion, instead of an inference from the lot unit at each call site.
/// <para>
/// The vocabulary is deliberately minimal pending the domain-owner ruling recorded in
/// remediation slice 8: <see cref="PercentOfPar"/> for face-value paper quoted as a percentage of
/// par (a 99.5 quote on 1,000,000 face is 995,000), <see cref="PerUnit"/> for everything
/// quoted in money per economic unit, and <see cref="Balance"/> for cash-like positions that are
/// not quoted at all (Deposit and Repo, decided 2026-11-25). A class that quotes differently
/// (yield-quoted bills, per-point contracts) extends this vocabulary through that decision, not
/// through magnitude or class-name inference.
/// </para>
/// </summary>
public enum SecurityQuoteConvention
{
    /// <summary>The quote is money per economic unit (share, contract-adjusted unit, currency unit).</summary>
    PerUnit,

    /// <summary>The quote is a percentage of par; monetary value divides the quote by 100.</summary>
    PercentOfPar,

    /// <summary>
    /// The position is not quoted: it is valued at its outstanding balance (its economic units, in
    /// the position currency) plus accrued interest, which is carried by the accrual path rather
    /// than by a price. A supplied quote does not scale the value.
    /// </summary>
    Balance,
}
