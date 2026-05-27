using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Wpf.Shell.Refresh;

public sealed class ShellRefreshCoordinator : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _refreshCts;
    private int _revision;

    public void RequestRefresh(Func<CancellationToken, Task> refreshAsync)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);

        CancellationTokenSource cts;
        int revision;
        lock (_gate)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = new CancellationTokenSource();
            cts = _refreshCts;
            revision = ++_revision;
        }

        _ = RunRefreshAsync(refreshAsync, cts, revision);
    }

    public async Task RefreshNowAsync(Func<CancellationToken, Task> refreshAsync, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int revision;
        lock (_gate)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = linkedCts;
            revision = ++_revision;
        }

        await RunRefreshAsync(refreshAsync, linkedCts, revision).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
        }
    }

    private async Task RunRefreshAsync(
        Func<CancellationToken, Task> refreshAsync,
        CancellationTokenSource cts,
        int revision)
    {
        try
        {
            await refreshAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (revision == _revision && ReferenceEquals(_refreshCts, cts))
                {
                    _refreshCts = null;
                }
            }
        }
    }
}
