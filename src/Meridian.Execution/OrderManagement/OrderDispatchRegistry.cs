using System.Collections.Concurrent;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns submission acknowledgments awaited by cancellation sweeps. Registration starts after
/// validation, and settlement follows publication of the gateway outcome into the order book.
/// </summary>
internal sealed class OrderDispatchRegistry(TimeSpan settleTimeout, ILogger logger)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _inFlight = new(StringComparer.Ordinal);

    internal Lease CreateLease() => new(this);

    internal bool Contains(string orderId) => _inFlight.ContainsKey(orderId);

    internal bool IsUnsettled(string orderId) =>
        _inFlight.TryGetValue(orderId, out var pending) && !pending.Task.IsCompleted;

    /// <summary>
    /// Awaits only dispatches present when this call starts. Later submissions cannot extend
    /// the deadline or starve a sweep. An admitted halt is independent of caller cancellation.
    /// </summary>
    internal async Task<IReadOnlyList<string>> WaitForCurrentAsync()
    {
        var inFlight = _inFlight.ToArray();
        if (inFlight.Length == 0)
            return [];

        logger.LogInformation(
            "Cancel-all is waiting up to {SettleTimeout} for {InFlightCount} in-flight submission(s) to be acknowledged before sweeping the book",
            settleTimeout,
            inFlight.Length);

        try
        {
            await Task.WhenAll(inFlight.Select(static entry => entry.Value.Task))
                .WaitAsync(settleTimeout)
                .ConfigureAwait(false);
            return [];
        }
        catch (TimeoutException)
        {
            var unsettled = inFlight
                .Where(static entry => !entry.Value.Task.IsCompleted)
                .Select(static entry => entry.Key)
                .ToList();
            logger.LogWarning(
                "Cancel-all stopped waiting after {SettleTimeout}: {UnsettledCount} submission(s) still await gateway acknowledgement and will be reported as working unless their cancellation is confirmed",
                settleTimeout,
                unsettled.Count);
            return unsettled;
        }
    }

    /// <summary>
    /// Tracks one submission attempt. A losing duplicate has no ownership of the winning
    /// dispatch; repeated settlement cannot remove a later reuse of the same client order id.
    /// </summary>
    internal sealed class Lease(OrderDispatchRegistry owner) : IDisposable
    {
        private string? _orderId;
        private TaskCompletionSource? _settled;

        internal void Begin(string orderId)
        {
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            while (true)
            {
                if (owner._inFlight.TryAdd(orderId, settled))
                {
                    _orderId = orderId;
                    _settled = settled;
                    return;
                }

                if (!owner._inFlight.TryGetValue(orderId, out var existing))
                    continue;

                if (!existing.Task.IsCompleted)
                {
                    // TryRegisterOrder will refuse this duplicate. Its disposal must not hide
                    // the winner's unacknowledged broker submission from a cancellation sweep.
                    return;
                }

                owner._inFlight.TryRemove(new KeyValuePair<string, TaskCompletionSource>(orderId, existing));
            }
        }

        /// <summary>Settles this attempt only, once its outcome is visible to the order book.</summary>
        internal void Settle()
        {
            if (_orderId is null || _settled is null)
                return;

            owner._inFlight.TryRemove(new KeyValuePair<string, TaskCompletionSource>(_orderId, _settled));
            _settled.TrySetResult();
        }

        public void Dispose() => Settle();
    }
}
