using System.Runtime.ExceptionServices;
using Meridian.Execution.Services;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns OMS admission and background-work lifetime. Named callbacks run report intake and
/// query working reductions; command state and report processing remain with their owners.
/// </summary>
internal sealed class OrderManagementLifecycleCoordinator(
    Func<CancellationToken, Task> runReportPump,
    AccountingHandoffCoordinator accountingHandoff,
    ExecutionReportHub executionReports,
    ExecutionFailureState failures,
    ExecutionOperatorControlService? operatorControls,
    Func<string, Guid?, decimal> workingReductionQuantity) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _reportPumpCts = new();
    private Task _reportPumpTask = Task.CompletedTask;
    private Task _handoffRecoveryTask = Task.CompletedTask;
    private Task? _shutdownTask;
    private TaskCompletionSource? _operationsDrained;
    private int _activeOperations;
    private bool _started;
    private bool _shutdownStarted;

    internal Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_shutdownStarted, typeof(OrderManagementSystem));
            cancellationToken.ThrowIfCancellationRequested();
            if (_started)
                return Task.CompletedTask;

            // Install the live query before any command can enter. The startup token never
            // becomes the service lifetime token, and neither worker runs under this lock.
            if (operatorControls is not null)
                operatorControls.WorkingReductionQuantityProbe = workingReductionQuantity;

            _reportPumpTask = Task.Run(() => runReportPump(_reportPumpCts.Token));
            _handoffRecoveryTask = accountingHandoff.CanReplay
                ? Task.Run(() => accountingHandoff.ReplayPendingAsync(_reportPumpCts.Token))
                : Task.CompletedTask;
            _started = true;
        }
        return Task.CompletedTask;
    }

    internal Task StopAsync(CancellationToken cancellationToken = default)
        => DisposeAsync().AsTask().WaitAsync(cancellationToken);

    internal OperationLease EnterOperation()
    {
        lock (_sync)
        {
            if (_shutdownStarted)
                throw new ObjectDisposedException(nameof(OrderManagementSystem));
            if (!_started)
                throw new InvalidOperationException("The OMS must be started before accepting operations. Call StartAsync first.");
            if (failures.Failure is { } reportPumpFailure)
                throw new InvalidOperationException(
                    "The authoritative broker-report pump failed and the OMS is closed to new operations.",
                    reportPumpFailure);

            checked
            {
                _activeOperations++;
            }
            return new OperationLease(this);
        }
    }

    internal ExecutionReportHub.Subscription SubscribeExecutionReports(
        int capacity,
        string? subscriberName,
        OrderManagementSystem.UndeliverableExecutionReportHandler? undeliverableHandler)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                "Execution-report subscription capacity must be positive.");

        lock (_sync)
        {
            // Subscription admission is intentionally independent of startup and the failure
            // latch. Only shutdown closes registration, atomically with operation admission.
            ObjectDisposedException.ThrowIf(_shutdownStarted, typeof(OrderManagementSystem));
            return executionReports.Subscribe(capacity, subscriberName, undeliverableHandler);
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource startGate;
        Task shutdownTask;
        lock (_sync)
        {
            _shutdownStarted = true;
            executionReports.BeginClose();
            if (_shutdownTask is not null)
                return new ValueTask(_shutdownTask);

            var operationsDrained = _activeOperations == 0
                ? Task.CompletedTask
                : (_operationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdownTask = DisposeCoreAsync(startGate.Task, operationsDrained);
            _shutdownTask = shutdownTask;
        }

        // Cancellation callbacks and all asynchronous shutdown work begin outside admission's
        // lock. Every caller observes the same task even if its own StopAsync wait is cancelled.
        startGate.SetResult();
        return new ValueTask(shutdownTask);
    }

    private async Task DisposeCoreAsync(Task startGate, Task operationsDrained)
    {
        await startGate.ConfigureAwait(false);
        // A broker operation may still return an accepted fill. Do not cancel report intake
        // until all admitted operations have completed their authoritative side effects.
        await operationsDrained.ConfigureAwait(false);

        Exception? shutdownFailure = null;
        try
        {
            await _reportPumpCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            shutdownFailure = ex;
        }

        try
        {
            await Task.WhenAll(_reportPumpTask, _handoffRecoveryTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_reportPumpCts.IsCancellationRequested)
        {
            // Expected when shutdown interrupts intake or a retained-handoff load.
        }
        catch (Exception ex)
        {
            shutdownFailure = shutdownFailure is null ? ex : new AggregateException(shutdownFailure, ex);
        }

        // Producers have finished. Accepted reports can now drain before abandoned buffered
        // reports pass through the hub's bounded recovery/failure handling.
        try
        {
            await executionReports.CompleteSubscribersAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            shutdownFailure = shutdownFailure is null ? ex : new AggregateException(shutdownFailure, ex);
        }
        finally
        {
            executionReports.CompleteObserver();
            _reportPumpCts.Dispose();
            if (operatorControls?.WorkingReductionQuantityProbe == workingReductionQuantity)
                operatorControls.WorkingReductionQuantityProbe = null;
        }

        // A subscriber may fail outside the pump stack, including while shutdown is draining.
        // Keep the existing shutdown-error precedence when another cleanup step already failed.
        if (shutdownFailure is null && failures.Failure is { } terminalReportFailure)
            shutdownFailure = terminalReportFailure;
        if (shutdownFailure is not null)
            ExceptionDispatchInfo.Capture(shutdownFailure).Throw();
    }

    private void ExitOperation()
    {
        lock (_sync)
        {
            _activeOperations--;
            if (_activeOperations == 0)
                _operationsDrained?.TrySetResult();
        }
    }

    internal sealed class OperationLease(OrderManagementLifecycleCoordinator owner) : IDisposable
    {
        private OrderManagementLifecycleCoordinator? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitOperation();
    }
}
