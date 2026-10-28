namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    // Guarded by the same lock as shutdown admission, so Start cannot create an
    // unobserved pump after Stop has captured the tasks it must drain.
    private bool _started;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_disposeSync)
        {
            ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_started)
                return Task.CompletedTask;

            // Close-only admission needs the running OMS's committed reduction book.
            if (_operatorControls is not null)
                _operatorControls.WorkingReductionQuantityProbe = ResolveWorkingReductionQuantity;

            _reportPumpTask = Task.Run(() => PumpGatewayExecutionReportsAsync(_reportPumpCts.Token));
            _handoffRecoveryTask = _accountingHandoff.CanReplay
                ? Task.Run(() => _accountingHandoff.ReplayPendingAsync(_reportPumpCts.Token))
                : Task.CompletedTask;
            _started = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
        => DisposeAsync().AsTask().WaitAsync(cancellationToken);
}
