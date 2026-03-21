using System;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Cross-platform recurring refresh scheduler backed by <see cref="PeriodicTimer"/>.
/// </summary>
public sealed class PeriodicRefreshScheduler : IRefreshScheduler
{
    private readonly object _sync = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public void Start(TimeSpan interval, Func<CancellationToken, Task> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Refresh interval must be positive.");
        }

        lock (_sync)
        {
            StopLoop_NoLock();

            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = RunLoopAsync(interval, callback, _loopCts.Token);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopLoop_NoLock();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static async Task RunLoopAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await callback(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private void StopLoop_NoLock()
    {
        if (_loopCts is null)
        {
            return;
        }

        _loopCts.Cancel();
        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
    }
}
