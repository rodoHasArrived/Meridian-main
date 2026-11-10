using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Paces the Alpaca Trading API reference read that fans out into many requests per call: the
/// windowed corporate-action history.
/// </summary>
/// <remarks>
/// The Trading API allows 200 requests per minute per key, and that budget is shared with order
/// submission, cancellation and recovery on the same key. The corporate-action history is
/// therefore held to half of it across every provider instance in the process, so a bulk ingest
/// cannot by itself push order flow into rate-limit refusals. Short bursts (one security's
/// history) are not delayed; a sustained run is, and its callers queue in arrival order. The
/// limit is per process, not per key, and it does not cover other Trading API reads: asset
/// search has its own limiter and option contract discovery is a request or two per call.
/// </remarks>
public sealed class AlpacaReferenceRequestPacing
{
    private readonly RateLimiter? _limiter;

    private AlpacaReferenceRequestPacing(RateLimiter? limiter) => _limiter = limiter;

    /// <summary>Process-wide pacing used when nothing else is supplied.</summary>
    public static AlpacaReferenceRequestPacing Shared { get; } =
        new(new RateLimiter(AlpacaRateLimits.MaxRequestsPerWindow / 2, AlpacaRateLimits.Window));

    /// <summary>No pacing. For deterministic fixtures that never reach Alpaca.</summary>
    public static AlpacaReferenceRequestPacing Unpaced { get; } = new(null);

    /// <summary>Waits until the next reference request may be sent.</summary>
    public Task WaitAsync(CancellationToken ct = default) =>
        _limiter is null ? Task.CompletedTask : _limiter.WaitForSlotAsync(ct);
}
