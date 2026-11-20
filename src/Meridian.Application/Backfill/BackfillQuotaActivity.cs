using Meridian.Contracts.Backfill;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Application.Backfill;

internal sealed class BackfillQuotaActivity(
    BackfillConfig? config, RateLimiterRegistry limiters, Action<DateTimeOffset>? onThrottled)
{
    private const int MaxReportedEvents = 50;
    private readonly object _sync = new();
    private readonly HashSet<string> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _providersThatReturnedBars = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BackfillThrottleEvent> _events = [];
    private long _requests;
    private int _eventsObserved;
    private bool _partialRequestCount;

    public HistoricalBackfillRequestScope Observe(bool retryRateLimited) =>
        new(GetHeadroomPercent, BeforeUnmeteredFetchAsync, RecordRequest, RecordBarsPublished, RecordThrottle,
            retryRateLimited, RecordUnmeteredFetchStarted);

    public BackfillQuotaReceipt BuildReceipt(BackfillQuotaEstimate estimate, TimeSpan elapsed)
    {
        lock (_sync)
        {
            var actualRequests = _partialRequestCount ? (long?)null : _requests;
            return new BackfillQuotaReceipt(estimate, _providers.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                actualRequests, elapsed.TotalSeconds, actualRequests - estimate.ExpectedRequests,
                elapsed.TotalSeconds - estimate.EstimatedDurationSeconds, _eventsObserved, _events.ToArray(),
                _providersThatReturnedBars.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private int GetHeadroomPercent(IHistoricalDataProvider provider) => BackfillQuotaPlanner.ReadLimit(provider) is null
        ? 0 : BackfillQuotaPlanner.GetPolicy(config, provider.Name).ReserveHeadroomPercent;

    private async Task BeforeUnmeteredFetchAsync(IHistoricalDataProvider provider, CancellationToken ct)
    {
        if (BackfillQuotaPlanner.ReadLimit(provider) is { } limit)
        {
            var policy = BackfillQuotaPlanner.GetPolicy(config, provider.Name);
            var capacity = BackfillQuotaPlanner.ApplyHeadroom(limit.Maximum, policy.ReserveHeadroomPercent);
            if (limit.Sample is { StateAvailable: true, ResetAt: { } reset } sample &&
                (sample.IsRateLimited || sample.RequestsInWindow >= capacity) && reset > DateTimeOffset.UtcNow)
            {
                var wait = reset - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    RecordThrottle(provider.Name, "observed-budget", wait);
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }
            }

            // Non-HTTP adapters may implement their own pacing. Apply the shared budget to
            // fetch admissions, and disclose that their internal request count is unavailable.
            var limiter = limiters.GetOrCreate($"{provider.Name}|{capacity}|{limit.Window.Ticks}", capacity, limit.Window);
            await limiter.WaitForUnitsAsync(1, 0, wait => RecordThrottle(provider.Name, "quota-admission", wait), ct)
                .ConfigureAwait(false);
        }
    }

    private void RecordUnmeteredFetchStarted(string provider)
    {
        lock (_sync)
        {
            _partialRequestCount = true;
            _providers.Add(provider);
        }
    }

    private void RecordRequest(string provider)
    {
        lock (_sync)
        {
            _providers.Add(provider);
            _requests++;
        }
    }

    private void RecordBarsPublished(string provider)
    {
        if (provider.Equals("composite", StringComparison.OrdinalIgnoreCase))
            return;
        lock (_sync)
            _providersThatReturnedBars.Add(provider);
    }

    private void RecordThrottle(string provider, string reason, TimeSpan? delay)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _eventsObserved++;
            if (_events.Count < MaxReportedEvents)
                _events.Add(new BackfillThrottleEvent(provider, reason, now, delay?.TotalSeconds));
        }

        if (delay is { } wait && wait > TimeSpan.Zero)
            onThrottled?.Invoke(now + wait);
    }
}
