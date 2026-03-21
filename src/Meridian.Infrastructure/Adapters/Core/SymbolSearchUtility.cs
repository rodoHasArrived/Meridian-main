namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Shared utility methods for symbol search providers.
/// </summary>
public static class SymbolSearchUtility
{
    /// <summary>
    /// Calculates a relevance score for a search result based on how well it matches the query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="symbol">The ticker symbol.</param>
    /// <param name="name">Optional company name.</param>
    /// <param name="position">Position in search results (0-based).</param>
    /// <param name="positionPenaltyMax">Maximum position penalty (default 20).</param>
    /// <param name="positionPenaltyFactor">Multiplier for position penalty (default 2).</param>
    /// <returns>Score from 0-100 where higher is better match.</returns>
    public static int CalculateMatchScore(
        string query,
        string symbol,
        string? name,
        int position,
        int positionPenaltyMax = 20,
        int positionPenaltyFactor = 2)
    {
        var score = 50;
        var queryUpper = query.ToUpperInvariant();
        var symbolUpper = symbol.ToUpperInvariant();

        // Exact symbol match
        if (symbolUpper == queryUpper)
            score = 100;
        // Symbol starts with query
        else if (symbolUpper.StartsWith(queryUpper))
            score = 80 + (10 - Math.Min(10, symbolUpper.Length - queryUpper.Length));
        // Symbol contains query
        else if (symbolUpper.Contains(queryUpper))
            score = 60;
        // Name contains query
        else if (name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            score = 40;

        // Position penalty (prefer earlier results)
        score -= Math.Min(positionPenaltyMax, position * positionPenaltyFactor);

        return Math.Max(0, score);
    }

    /// <summary>
    /// Normalizes a symbol to uppercase.
    /// </summary>
    public static string NormalizeSymbol(string symbol) => symbol.ToUpperInvariant();
}
