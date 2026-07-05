namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Resolves the ticker a security actually traded under on a given date, using
/// security-master identifier validity intervals. Lets a historical backfill that spans a
/// ticker rename (e.g. FB→META) query the provider with the era-correct symbol per
/// date-range chunk while landing all data under the canonical requested symbol.
/// </summary>
public interface IHistoricalSymbolTimelineResolver
{
    /// <summary>
    /// Returns the ticker under which the security identified by <paramref name="symbol"/>
    /// traded on <paramref name="date"/>. Returns <paramref name="symbol"/> unchanged when
    /// the security master has no mapping for that date.
    /// </summary>
    Task<string> ResolveTickerForDateAsync(string symbol, DateOnly date, CancellationToken ct = default);
}
