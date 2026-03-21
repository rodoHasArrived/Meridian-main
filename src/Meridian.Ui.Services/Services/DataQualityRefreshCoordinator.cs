using System;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Coordinates manual and recurring data-quality refresh work independently of any UI framework.
/// </summary>
public sealed class DataQualityRefreshCoordinator : IDisposable
{
    private readonly IRefreshScheduler _scheduler;
    private readonly Func<CancellationToken, Task> _refreshAsync;
    private readonly Action<Exception>? _onRefreshError;
    private bool _isStarted;

    public DataQualityRefreshCoordinator(
        IRefreshScheduler scheduler,
        Func<CancellationToken, Task> refreshAsync,
        Action<Exception>? onRefreshError = null)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
        _onRefreshError = onRefreshError;
    }

    public bool IsStarted => _isStarted;

    public async Task StartAsync(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        if (_isStarted)
        {
            return;
        }

        await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        _scheduler.Start(interval, RefreshCoreAsync, cancellationToken);
        _isStarted = true;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
        => RefreshCoreAsync(cancellationToken);

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _scheduler.Stop();
        _isStarted = false;
    }

    public void Dispose()
    {
        Stop();
        _scheduler.Dispose();
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _refreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal cancellation path.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _onRefreshError?.Invoke(ex);
        }
    }
}
