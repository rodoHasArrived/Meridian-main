namespace Meridian.Core.Resilience;

/// <summary>
/// Canonical exponential-backoff delay calculation shared by reconnect loops, retry policies, and
/// requeue paths across the platform. Consolidates the previously hand-rolled
/// <c>Math.Pow(2, attempt)</c> variants so every site grows, caps, and jitters delays the same way.
/// </summary>
public static class Backoff
{
    /// <summary>
    /// Computes the delay before the given retry attempt using exponential growth with an optional
    /// cap and symmetric jitter.
    /// </summary>
    /// <param name="attempt">1-based attempt number; values below 1 are treated as 1.</param>
    /// <param name="baseDelay">Delay before the first retry attempt.</param>
    /// <param name="maxDelay">
    /// Optional cap applied to the un-jittered delay. Jitter is applied after capping, so the
    /// result can exceed the cap by at most <paramref name="jitterFraction"/>.
    /// </param>
    /// <param name="jitterFraction">
    /// Symmetric jitter as a fraction of the capped delay (for example 0.2 for ±20%). Zero disables
    /// jitter. Values are clamped to [0, 1].
    /// </param>
    /// <param name="multiplier">Growth factor per attempt; must be at least 1 (default 2).</param>
    /// <param name="random">Randomness source for jitter; defaults to <see cref="Random.Shared"/>.</param>
    public static TimeSpan ExponentialDelay(
        int attempt,
        TimeSpan baseDelay,
        TimeSpan? maxDelay = null,
        double jitterFraction = 0.0,
        double multiplier = 2.0,
        Random? random = null)
    {
        var exponent = Math.Max(attempt, 1) - 1;
        multiplier = Math.Max(1.0, multiplier);

        // Guard against overflow for uncapped callers on high attempt counts (~24.8 days).
        var delayMs = Math.Min(baseDelay.TotalMilliseconds * Math.Pow(multiplier, exponent), int.MaxValue);
        if (maxDelay is { } cap)
        {
            delayMs = Math.Min(delayMs, cap.TotalMilliseconds);
        }

        jitterFraction = Math.Clamp(jitterFraction, 0.0, 1.0);
        if (jitterFraction > 0)
        {
            var source = random ?? Random.Shared;
            delayMs += delayMs * jitterFraction * (source.NextDouble() * 2 - 1);
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
    }
}
