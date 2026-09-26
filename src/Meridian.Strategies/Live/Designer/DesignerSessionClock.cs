namespace Meridian.Strategies.Live.Designer;

/// <summary>
/// Maps a live market-event timestamp onto the trading session it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The UTC calendar date is the wrong answer for a US equity feed. An extended-hours quote at
/// 19:30 Eastern is already the next day in UTC, so a UTC-dated clock would close the session
/// mid-flight, treat the earlier part of that same session as complete, and merge the late quote
/// into the following regular session. <c>MOMENTUM_63D</c> and <c>VOLATILITY_20D</c> would then be
/// computed from closes that never were, and a document could enter or exit on them.
/// </para>
/// <para>
/// Converting to Eastern first is sufficient rather than approximate: the US extended-hours
/// window runs 04:00-20:00 Eastern and so never crosses midnight in that zone, which is the only
/// property the session date depends on. No holiday calendar is needed — a market holiday simply
/// produces no events, and a session with no observations never opens.
/// </para>
/// <para>
/// This is correct for exactly what <see cref="DesignerStrategyPlan"/> admits: Equity and ETF
/// instruments on the US session. A venue on another calendar would need a per-instrument session
/// map, which is why the boundary is named here rather than assumed away. The zone is resolved
/// locally rather than by taking a dependency on <c>Meridian.Platform</c>'s trading calendar,
/// which would cross a layer boundary for one time-zone lookup.
/// </para>
/// </remarks>
internal static class DesignerSessionClock
{
    private static readonly TimeZoneInfo ExchangeZone = ResolveExchangeZone();

    /// <summary>
    /// The trading date <paramref name="timestamp"/> falls in, in exchange-local terms.
    /// </summary>
    public static DateOnly SessionDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, ExchangeZone).DateTime);

    private static TimeZoneInfo ResolveExchangeZone()
    {
        // The IANA id is available on Linux and, through ICU, on current Windows builds; the
        // Windows registry id is the fallback for hosts without it.
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
