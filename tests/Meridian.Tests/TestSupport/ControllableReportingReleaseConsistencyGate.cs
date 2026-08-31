using System.Collections.Concurrent;
using Meridian.Reporting;

namespace Meridian.Tests.TestSupport;

internal sealed class ControllableReportingReleaseConsistencyGate :
    IReportingReleaseConsistencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _periodGates =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _attemptSignals = [];
    private int _attemptCount;

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string accountingPeriodId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountingPeriodId);
        cancellationToken.ThrowIfCancellationRequested();

        var attempt = Interlocked.Increment(ref _attemptCount);
        SignalFor(attempt).TrySetResult(true);
        var periodGate = _periodGates.GetOrAdd(
            accountingPeriodId,
            static _ => new SemaphoreSlim(1, 1));
        await periodGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(periodGate);
    }

    public Task WaitForAttemptAsync(
        int attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        return SignalFor(attempt).Task.WaitAsync(cancellationToken);
    }

    private TaskCompletionSource<bool> SignalFor(int attempt) =>
        _attemptSignals.GetOrAdd(
            attempt,
            static _ => new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously));

    private sealed class Lease(SemaphoreSlim periodGate) : IAsyncDisposable
    {
        private SemaphoreSlim? _periodGate = periodGate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _periodGate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
