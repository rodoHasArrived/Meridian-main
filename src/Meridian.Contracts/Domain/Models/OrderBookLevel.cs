using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// One price level in an order book side. Level is 0-based (0 = best).
/// </summary>
/// <remarks>
/// <para>
/// <b>Design Notes:</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Price type:</b> Uses decimal for financial precision. This ensures accurate representation
/// of prices without floating-point rounding errors, which is critical for financial calculations.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Cross-side validation:</b> Bid/ask price validation (bid &lt; ask) is performed at the
/// order book level rather than at the individual level, since single levels don't have
/// visibility into the opposite side.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed record OrderBookLevel
{
    /// <summary>
    /// Gets the side of the order book (bid or ask).
    /// </summary>
    public OrderBookSide Side { get; init; }

    /// <summary>
    /// Gets the depth level in the order book (0 = best price).
    /// Max 65,535 levels — far exceeds any real order book depth.
    /// </summary>
    public ushort Level { get; init; }

    /// <summary>
    /// Gets the price at this level.
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// Gets the total size available at this price level.
    /// </summary>
    public decimal Size { get; init; }

    /// <summary>
    /// Gets the market maker identifier (if available).
    /// </summary>
    public string? MarketMaker { get; init; }

    /// <summary>
    /// Validates order book level data at construction time to prevent corrupt datasets.
    /// </summary>
    public OrderBookLevel(
        OrderBookSide Side,
        ushort Level,
        decimal Price,
        decimal Size,
        string? MarketMaker = null)
    {
        if (Price <= 0)
            throw new ArgumentOutOfRangeException(nameof(Price), Price, "Price must be greater than 0");

        if (Size < 0)
            throw new ArgumentOutOfRangeException(nameof(Size), Size, "Size must be greater than or equal to 0");

        this.Side = Side;
        this.Level = Level;
        this.Price = Price;
        this.Size = Size;
        this.MarketMaker = MarketMaker;
    }
}
