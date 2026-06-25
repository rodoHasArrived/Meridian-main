using Meridian.Domain.Events;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Captures market events and lets tests wait for event-driven conditions without fixed sleeps.
/// </summary>
public sealed class SignalingMarketEventPublisher : IMarketEventPublisher
{
    private readonly object _sync = new();
    private readonly List<MarketEvent> _events = [];
    private TaskCompletionSource<object?> _nextPublished = CreateSignal();

    public IReadOnlyList<MarketEvent> PublishedEvents
    {
        get
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }
    }

    public bool TryPublish(in MarketEvent evt)
    {
        TaskCompletionSource<object?> signal;
        lock (_sync)
        {
            _events.Add(evt);
            signal = _nextPublished;
            _nextPublished = CreateSignal();
        }

        signal.TrySetResult(null);
        return true;
    }

    public async Task WaitUntilAsync(Func<IReadOnlyList<MarketEvent>, bool> predicate, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            Task waitTask;
            lock (_sync)
            {
                var snapshot = _events.ToArray();
                if (predicate(snapshot))
                    return;
                waitTask = _nextPublished.Task;
            }

            try
            {
                await waitTask.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for market data.", ex);
            }
        }
    }

    public async Task<bool> HasAdditionalEventWithinAsync(int currentCount, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (true)
        {
            Task waitTask;
            lock (_sync)
            {
                if (_events.Count > currentCount)
                    return true;
                waitTask = _nextPublished.Task;
            }

            try
            {
                await waitTask.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    private static TaskCompletionSource<object?> CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
