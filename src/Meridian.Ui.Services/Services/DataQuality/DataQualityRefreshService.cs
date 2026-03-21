using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Periodic refresh loop shared by data-quality consumers.
/// </summary>
public sealed class DataQualityRefreshService : IDataQualityRefreshService
{
    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;

    public void Start(TimeSpan interval, Func<CancellationToken, Task> onRefresh)
    {
        ArgumentNullException.ThrowIfNull(onRefresh);

        Stop();
        _refreshCts = new CancellationTokenSource();
        _refreshTask = RunLoopAsync(interval, onRefresh, _refreshCts.Token);
    }

    public void Stop()
    {
        if (_refreshCts == null)
        {
            return;
        }

        try
        {
            _refreshCts.Cancel();
        }
        finally
        {
            _refreshCts.Dispose();
            _refreshCts = null;
            _refreshTask = null;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private static async Task RunLoopAsync(TimeSpan interval, Func<CancellationToken, Task> onRefresh, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await onRefresh(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }
}
