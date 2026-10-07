using Microsoft.Extensions.Hosting;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Stops and drains this host's replay file readers before owned files are released.</summary>
internal sealed class ReplayScanLifetime(IHostApplicationLifetime lifetime) : IHostedService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _pending = [];
    private readonly CancellationTokenSource _stopping =
        CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
    private bool _stopped;
    private Task? _disposeTask;

    public bool TryStart(Func<CancellationToken, Task> scan)
    {
        lock (_gate)
        {
            if (_stopped || _stopping.IsCancellationRequested)
                return false;

            var task = scan(_stopping.Token);
            _pending.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_gate)
                    _pending.Remove(completed);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return true;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] pending;
        lock (_gate)
        {
            _stopped = true;
            pending = _pending.ToArray();
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        // The singleton is also exposed as IHostedService; DI can dispose both aliases.
        lock (_gate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _stopping.Dispose();
    }
}
