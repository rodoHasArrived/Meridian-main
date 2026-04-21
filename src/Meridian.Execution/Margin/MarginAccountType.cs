namespace Meridian.Execution.Margin;

/// <summary>
/// Classifies the margin regime of a brokerage account.
/// </summary>
public enum MarginAccountType
{
    /// <summary>
    /// Cash account — no borrowing allowed. Positions must be fully funded by available cash.
    /// </summary>
    Cash,

    /// <summary>
    /// Regulation T (Reg T) margin account. FINRA/SEC rules: 50 % initial margin for long
    /// positions; 25 % maintenance margin. Short positions require 150 % of the short value
    /// as initial collateral. Buying power is up to 2× the available cash equity.
    /// </summary>
    RegT,

    /// <summary>
    /// Portfolio margin account. Risk-based margining using stress-test scenarios rather than
    /// fixed percentage requirements. Typically requires a minimum equity threshold.
    /// </summary>
    PortfolioMargin,
}
