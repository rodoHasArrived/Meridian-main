namespace Meridian.Execution.Sdk;

/// <summary>
/// Extension helpers for working with <see cref="IPosition"/> and converting between
/// concrete position types from different pillars.
/// </summary>
public static class PositionExtensions
{
    /// <summary>
    /// Projects a sequence of positions into an <see cref="IPosition"/> enumerable.
    /// Useful when calling code that only requires the cross-pillar interface surface.
    /// </summary>
    /// <typeparam name="T">Concrete position type implementing <see cref="IPosition"/>.</typeparam>
    /// <param name="positions">Source positions.</param>
    /// <returns>The same sequence typed as <see cref="IPosition"/>.</returns>
    public static IEnumerable<IPosition> AsIPositions<T>(this IEnumerable<T> positions)
        where T : IPosition
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Cast<IPosition>();
    }

    /// <summary>
    /// Projects a dictionary keyed by symbol into a dictionary of <see cref="IPosition"/> values.
    /// </summary>
    /// <typeparam name="T">Concrete position type implementing <see cref="IPosition"/>.</typeparam>
    /// <param name="positions">Source dictionary keyed by ticker symbol.</param>
    /// <returns>
    /// A new dictionary with the same keys and values typed as <see cref="IPosition"/>.
    /// </returns>
    public static IReadOnlyDictionary<string, IPosition> ToIPositionDictionary<T>(
        this IReadOnlyDictionary<string, T> positions)
        where T : IPosition
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.ToDictionary(
            static kvp => kvp.Key,
            static kvp => (IPosition)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts an <see cref="IPosition"/> to an anonymous-style summary for display or logging
    /// without depending on a concrete type.
    /// </summary>
    /// <param name="position">The position to summarise.</param>
    /// <returns>
    /// A value tuple containing the key display fields from the position.
    /// </returns>
    public static (string Symbol, long Quantity, decimal AverageCostBasis, decimal UnrealizedPnl, decimal RealizedPnl)
        ToDisplayTuple(this IPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return (position.Symbol, position.Quantity, position.AverageCostBasis,
                position.UnrealizedPnl, position.RealizedPnl);
    }
}
