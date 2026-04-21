using Meridian.Execution.Models;

namespace Meridian.Execution.Margin;

/// <summary>
/// Calculates margin requirements for individual positions and for the portfolio as a whole.
/// Implementations encode brokerage-specific or regulatory margin rules.
/// </summary>
public interface IMarginModel
{
    /// <summary>Human-readable name of this margin model (e.g., "Reg T", "Portfolio Margin").</summary>
    string ModelName { get; }

    /// <summary>
    /// Calculates the margin requirement for a single position described by
    /// <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The position to evaluate.</param>
    /// <param name="lastPrice">Current market price of the instrument.</param>
    /// <param name="portfolioEquity">
    ///     Current total portfolio equity (used for excess-liquidity calculation).
    /// </param>
    /// <returns>A <see cref="MarginRequirement"/> for the position.</returns>
    MarginRequirement CalculateForPosition(
        ExecutionPosition position,
        decimal lastPrice,
        decimal portfolioEquity);

    /// <summary>
    /// Calculates the aggregate portfolio-level margin requirement across all open positions.
    /// </summary>
    /// <param name="positions">All open positions in the portfolio.</param>
    /// <param name="prices">Current market prices keyed by symbol.</param>
    /// <param name="cash">Available cash in the portfolio.</param>
    /// <returns>A portfolio-level <see cref="MarginRequirement"/> (Symbol = null).</returns>
    MarginRequirement CalculatePortfolioRequirement(
        IReadOnlyDictionary<string, ExecutionPosition> positions,
        IReadOnlyDictionary<string, decimal> prices,
        decimal cash);
}
