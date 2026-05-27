using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Wpf.Services;

/// <summary>
/// Represents a scheduled background task.
/// </summary>
public sealed class ScheduledTask
{
    /// <summary>
    /// Gets or sets the unique identifier for the task.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the task name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the action to execute.
    /// </summary>
    public Func<CancellationToken, Task>? Action { get; set; }

    /// <summary>
    /// Gets or sets the interval between executions.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets whether the task is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Service for scheduling and managing background tasks.
/// Implements singleton pattern for application-wide task scheduling.
/// </summary>
public sealed class BackgroundTaskSchedulerService
{
    private static readonly Lazy<BackgroundTaskSchedulerService> _instance =
        new(() => new BackgroundTaskSchedulerService());

    private readonly ConcurrentDictionary<string, (ScheduledTask Task, CancellationTokenSource Cts, Task Loop)> _scheduledTasks = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    /// <summary>
    /// Gets the singleton instance of the BackgroundTaskSchedulerService.
    /// </summary>
    public static BackgroundTaskSchedulerService Instance => _instance.Value;

    /// <summary>
    /// Gets whether the scheduler is running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the number of scheduled tasks.
    /// </summary>
    public int TaskCount => _scheduledTasks.Count;

    /// <summary>
    /// Gets a snapshot of all scheduled task IDs.
    /// </summary>
    public IReadOnlyList<string> ScheduledTaskIds => _scheduledTasks.Keys.ToList();

    private BackgroundTaskSchedulerService()
    {
    }

    /// <summary>
    /// Starts the background task scheduler.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public Task StartAsync()
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the background task scheduler and cancels all running tasks.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public Task StopAsync()
        => StopAsync(CancellationToken.None);

    /// <summary>
    /// Stops the background task scheduler and waits for running task loops to observe cancellation.
    /// </summary>
    public async Task StopAsync(CancellationToken ct)
    {
        if (!_isRunning)
        {
            return;
        }

        var entries = _scheduledTasks.ToArray();
        foreach (var (_, (_, taskCts, _)) in entries)
        {
            taskCts.Cancel();
        }

        var loops = entries.Select(entry => entry.Value.Loop).ToArray();
        if (loops.Length > 0)
        {
            try
            {
                await Task.WhenAll(loops).WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LoggingService.Instance.LogWarning(
                    "Background task scheduler stop timed out while waiting for task loops");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError(
                    "Background task scheduler observed a task failure during shutdown",
                    ex);
            }
        }

        foreach (var (_, (_, taskCts, _)) in entries)
            taskCts.Dispose();

        _scheduledTasks.Clear();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isRunning = false;
    }

    /// <summary>
    /// Schedules a task for periodic execution. If a task with the same ID already exists, it is replaced.
    /// </summary>
    /// <param name="task">The task to schedule.</param>
    public void ScheduleTask(ScheduledTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.Action is null)
            throw new ArgumentException("Task action must not be null.", nameof(task));

        // Cancel existing task with same ID if present
        CancelTask(task.Id);

        if (!_isRunning || !task.IsEnabled)
            return;

        var taskCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
        var loop = RunTaskLoopAsync(task, taskCts.Token);
        _scheduledTasks[task.Id] = (task, taskCts, loop);

        _ = loop.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    LoggingService.Instance.LogError(
                        $"Background task '{task.Name}' failed",
                        completed.Exception?.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Schedules a task for execution with the specified parameters.
    /// </summary>
    /// <param name="name">The task name.</param>
    /// <param name="action">The action to execute.</param>
    /// <param name="interval">The interval between executions.</param>
    public void ScheduleTask(string name, Func<CancellationToken, Task> action, TimeSpan interval)
    {
        ScheduleTask(new ScheduledTask
        {
            Name = name,
            Action = action,
            Interval = interval
        });
    }

    /// <summary>
    /// Cancels a scheduled task.
    /// </summary>
    /// <param name="taskId">The task identifier to cancel.</param>
    public void CancelTask(string taskId)
    {
        if (_scheduledTasks.TryRemove(taskId, out var entry))
        {
            entry.Cts.Cancel();
            _ = entry.Loop.ContinueWith(
                _ => entry.Cts.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task RunTaskLoopAsync(ScheduledTask task, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(task.Interval, ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested || !task.IsEnabled)
                    break;

                try
                {
                    await task.Action!(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    LoggingService.Instance.LogError(
                        $"Scheduled background task '{task.Name}' failed",
                        ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
    }
}
