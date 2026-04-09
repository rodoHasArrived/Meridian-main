namespace Meridian.Execution.Margin;

/// <summary>
/// Captures the current margin health for one account at a given point in time.
/// Returned by <see cref="Services.PaperTradingPortfolio.CheckMarginStatus"/>.
/// </summary>
/// <param name="AccountId">The account that was evaluated.</param>
/// <param name="PortfolioRequirement">
///     Aggregate margin requirement across all open positions in the account.
/// </param>
/// <param name="PositionRequirements">
///     Per-symbol margin requirements.
///     Empty when the account has no open positions or no margin model is active.
/// </param>
/// <param name="IsMarginCall">
///     <see langword="true"/> when the portfolio maintenance margin exceeds the account equity,
///     meaning the broker would issue a margin call.
/// </param>
/// <param name="MarginDeficiency">
///     The shortfall amount when <paramref name="IsMarginCall"/> is <see langword="true"/>;
///     otherwise 0.  This is the cash amount the account holder must deposit to restore the
///     account to compliance.
/// </param>
public sealed record MarginCallStatus(
    string AccountId,
    MarginRequirement PortfolioRequirement,
    IReadOnlyList<MarginRequirement> PositionRequirements,
    bool IsMarginCall,
    decimal MarginDeficiency)
{
    /// <summary>
    /// Convenience factory that represents a cash account or one with no open positions
    /// (no margin call possible, zero requirements).
    /// </summary>
    public static MarginCallStatus NoMarginRequired(string accountId) =>
        new(
            accountId,
            new MarginRequirement(null, 0m, 0m, 0m, 0m),
            [],
            false,
            0m);
}
