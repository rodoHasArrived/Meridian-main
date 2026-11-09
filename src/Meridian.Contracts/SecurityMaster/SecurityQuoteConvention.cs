namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// How a raw unit quote scales into money for a class's lots: the declared catalog fact behind
/// the pricing conversion, instead of an inference from the lot unit at each call site.
/// <para>
/// The vocabulary is deliberately minimal pending the domain-owner ruling recorded in
/// remediation slice 8: <see cref="PercentOfPar"/> for face-value paper quoted as a percentage of
/// par (a 99.5 quote on 1,000,000 face is 995,000), and <see cref="PerUnit"/> for everything
/// quoted in money per economic unit. A class that quotes differently (yield-quoted bills,
/// per-point contracts) extends this vocabulary through that decision, not through magnitude or
/// class-name inference.
/// </para>
/// </summary>
public enum SecurityQuoteConvention
{
    /// <summary>The quote is money per economic unit (share, contract-adjusted unit, currency unit).</summary>
    PerUnit,

    /// <summary>The quote is a percentage of par; monetary value divides the quote by 100.</summary>
    PercentOfPar,
}
