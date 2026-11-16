using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;

namespace Meridian.Execution.PaperMatching;

/// <summary>
/// The single freshness rule for paper-matching observations: how old a quote or trade print
/// may be and still price, trigger, or fill a paper order.
/// <para>
/// Disabled by default (<see cref="MaxAge"/> is <see langword="null"/>), which preserves the
/// documented <c>paper-match/1</c> behaviour of matching against the feed cache as it is.
/// When <see cref="PaperTradingGatewayOptions.MaxObservationAgeSeconds"/> is configured, both
/// paper gateways and the pre-trade guard that predicts them capture through one instance of
/// this policy, so the engine and the guard cannot disagree about which observations count.
/// </para>
/// <para>
/// The window applies to tick data. A quote or print outside it (older, or timestamped further
/// in the future than the window) is dropped. When a symbol has tick data and none of it is
/// current, the feed has stalled: the whole observation is withheld and marked
/// <see cref="PaperMarketObservation.IsStale"/>, including any cached bar, so a stalled tick
/// feed cannot fall through to an older bar close. A symbol that has only ever produced bars
/// is a bar-driven session with no wall-clock tick cadence and is left untouched.
/// </para>
/// </summary>
public sealed class PaperObservationFreshnessPolicy
{
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>The policy that applies no freshness window.</summary>
    public static PaperObservationFreshnessPolicy Disabled { get; } = new(maxAge: null);

    /// <summary>Creates a policy with an explicit window; <see langword="null"/> disables it.</summary>
    public PaperObservationFreshnessPolicy(TimeSpan? maxAge, Func<DateTimeOffset>? clock = null)
    {
        if (maxAge is { } window)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero, nameof(maxAge));
        }

        MaxAge = maxAge;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>Maximum tick observation age, or <see langword="null"/> when disabled.</summary>
    public TimeSpan? MaxAge { get; }

    /// <summary>Builds the policy the configured gateway options describe.</summary>
    public static PaperObservationFreshnessPolicy From(
        PaperTradingGatewayOptions? options, Func<DateTimeOffset>? clock = null)
    {
        if (options?.MaxObservationAgeSeconds is not { } seconds)
        {
            return clock is null ? Disabled : new PaperObservationFreshnessPolicy(null, clock);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds, nameof(options));
        return new PaperObservationFreshnessPolicy(TimeSpan.FromSeconds(seconds), clock);
    }

    /// <summary>
    /// Captures the matcher's observation for <paramref name="symbol"/> under this policy.
    /// </summary>
    public PaperMarketObservation Capture(ILiveFeedAdapter? feed, string symbol)
    {
        if (MaxAge is not { } window || feed is null || string.IsNullOrWhiteSpace(symbol))
        {
            return PaperMarketObservation.Capture(feed, symbol);
        }

        // One coherent snapshot, filtered, then projected — never a second read of the feed.
        var snapshot = feed.GetSnapshot(symbol);
        var asOf = _clock();
        var quote = snapshot.Quote is { } q && IsCurrent(q.Timestamp, asOf, window) ? q : null;
        var trade = snapshot.Trade is { } t && IsCurrent(t.Timestamp, asOf, window) ? t : null;

        var hadTicks = snapshot.Quote is not null || snapshot.Trade is not null;
        if (hadTicks && quote is null && trade is null)
        {
            return PaperMarketObservation.Stale;
        }

        return PaperMarketObservation.FromSnapshot(snapshot with { Quote = quote, Trade = trade });
    }

    /// <summary>
    /// Reject reason for a market order whose only observations fell outside the window.
    /// </summary>
    public string BuildStaleObservationRejectReason(string symbol)
        => $"The latest market data for '{symbol}' is older than the configured " +
           $"{MaxAge?.TotalSeconds ?? 0:0.###}s freshness window " +
           $"('{PaperTradingGatewayOptions.SectionKey}:{nameof(PaperTradingGatewayOptions.MaxObservationAgeSeconds)}'), " +
           "so the order was not filled at a price the market may have left. " +
           "Check the live feed subscription for this symbol or retry when quotes resume.";

    // A timestamp far in the future is as untrustworthy as a stale one, so a bad clock cannot
    // hold an observation live indefinitely (the same rule the pre-trade marks apply).
    private static bool IsCurrent(DateTimeOffset timestamp, DateTimeOffset asOf, TimeSpan window)
        => timestamp >= asOf - window && timestamp <= asOf + window;
}
