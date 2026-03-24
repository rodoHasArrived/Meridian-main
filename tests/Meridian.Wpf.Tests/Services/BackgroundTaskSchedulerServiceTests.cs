using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="BackgroundTaskSchedulerService"/> — task scheduling,
/// lifecycle management, cancellation, and task loop execution.
/// </summary>
public sealed class BackgroundTaskSchedulerServiceTests
{
    private static BackgroundTaskSchedulerService Svc => BackgroundTaskSchedulerService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = BackgroundTaskSchedulerService.Instance;
        var b = BackgroundTaskSchedulerService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── StartAsync / StopAsync ───────────────────────────────────────

    [Fact]
    public async Task StartAsync_ShouldSetIsRunningTrue()
    {
        var svc = Svc;
        await svc.StartAsync();

        svc.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_CalledTwice_ShouldBeIdempotent()
    {
        var svc = Svc;
        await svc.StartAsync();
        await svc.StartAsync(); // no exception

        svc.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_ShouldSetIsRunningFalse()
    {
        var svc = Svc;
        await svc.StartAsync();

        await svc.StopAsync();

        svc.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_ShouldBeIdempotent()
    {
        var svc = Svc;
        // Ensure stopped
        await svc.StopAsync();

        // Call stop again — should not throw
        Func<Task> act = () => svc.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_ShouldClearAllScheduledTasks()
    {
        var svc = Svc;
        await svc.StartAsync();

        svc.ScheduleTask("stop-clear-task", _ => Task.CompletedTask, TimeSpan.FromHours(1));
        svc.TaskCount.Should().BeGreaterThan(0);

        await svc.StopAsync();

        svc.TaskCount.Should().Be(0);
    }

    // ── ScheduleTask ─────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleTask_ShouldAddToScheduledTasks()
    {
        var svc = Svc;
        await svc.StartAsync();

        var taskId = "sched-" + Guid.NewGuid();
        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "Test task",
            Action = _ => Task.CompletedTask,
            Interval = TimeSpan.FromHours(1)
        });

        svc.ScheduledTaskIds.Should().Contain(taskId);

        // Cleanup
        svc.CancelTask(taskId);
    }

    [Fact]
    public async Task ScheduleTask_NullTask_ShouldThrow()
    {
        var svc = Svc;
        await svc.StartAsync();

        var act = () => svc.ScheduleTask((ScheduledTask)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ScheduleTask_NullAction_ShouldThrow()
    {
        var svc = Svc;
        await svc.StartAsync();

        var act = () => svc.ScheduleTask(new ScheduledTask
        {
            Name = "No action",
            Action = null
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ScheduleTask_DisabledTask_ShouldNotBeAdded()
    {
        var svc = Svc;
        await svc.StartAsync();

        var taskId = "disabled-" + Guid.NewGuid();
        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "Disabled task",
            Action = _ => Task.CompletedTask,
            Interval = TimeSpan.FromHours(1),
            IsEnabled = false
        });

        svc.ScheduledTaskIds.Should().NotContain(taskId);
    }

    [Fact]
    public async Task ScheduleTask_ReplacesExistingWithSameId()
    {
        var svc = Svc;
        await svc.StartAsync();

        var taskId = "replace-" + Guid.NewGuid();
        int firstCalled = 0, secondCalled = 0;

        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "First",
            Action = _ => { firstCalled++; return Task.CompletedTask; },
            Interval = TimeSpan.FromHours(1)
        });

        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "Second",
            Action = _ => { secondCalled++; return Task.CompletedTask; },
            Interval = TimeSpan.FromHours(1)
        });

        // Only one task with this ID should exist
        svc.ScheduledTaskIds.Count(id => id == taskId).Should().Be(1);

        // Cleanup
        svc.CancelTask(taskId);
    }

    [Fact]
    public async Task ScheduleTask_SimpleOverload_ShouldWork()
    {
        var svc = Svc;
        await svc.StartAsync();

        var countBefore = svc.TaskCount;

        svc.ScheduleTask("simple-" + Guid.NewGuid(), _ => Task.CompletedTask, TimeSpan.FromHours(1));

        svc.TaskCount.Should().BeGreaterThan(countBefore);

        // Cleanup
        await svc.StopAsync();
    }

    [Fact]
    public async Task ScheduleTask_WhenNotRunning_ShouldNotAddTask()
    {
        var svc = Svc;
        await svc.StopAsync(); // Explicitly ensure scheduler is stopped

        var taskId = "not-running-" + Guid.NewGuid();
        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "Should not schedule",
            Action = _ => Task.CompletedTask,
            Interval = TimeSpan.FromHours(1)
        });

        svc.ScheduledTaskIds.Should().NotContain(taskId);
    }

    // ── CancelTask ───────────────────────────────────────────────────

    [Fact]
    public async Task CancelTask_ShouldRemoveTask()
    {
        var svc = Svc;
        await svc.StartAsync();

        var taskId = "cancel-" + Guid.NewGuid();
        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "Cancel me",
            Action = _ => Task.CompletedTask,
            Interval = TimeSpan.FromHours(1)
        });

        svc.CancelTask(taskId);

        svc.ScheduledTaskIds.Should().NotContain(taskId);
    }

    [Fact]
    public void CancelTask_NonExistent_ShouldNotThrow()
    {
        var svc = Svc;
        var act = () => svc.CancelTask("non-existent-" + Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ── Task execution ───────────────────────────────────────────────

    [Fact]
    public async Task ScheduledTask_ShouldExecuteAction()
    {
        var svc = Svc;
        await svc.StopAsync();
        await svc.StartAsync();

        int executionCount = 0;
        var taskId = "exec-" + Guid.NewGuid();

        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "Quick task",
            Action = _ => { executionCount++; return Task.CompletedTask; },
            Interval = TimeSpan.FromMilliseconds(20)
        });

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (executionCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        executionCount.Should().BeGreaterThan(0);

        svc.CancelTask(taskId);
    }

    [Fact]
    public async Task ScheduledTask_ExceptionInAction_ShouldNotStopScheduler()
    {
        var svc = Svc;
        await svc.StopAsync();
        await svc.StartAsync();

        int callCount = 0;
        var taskId = "error-task-" + Guid.NewGuid();

        svc.ScheduleTask(new ScheduledTask
        {
            Id = taskId,
            Name = "Failing task",
            Action = _ =>
            {
                callCount++;
                throw new InvalidOperationException("boom");
            },
            Interval = TimeSpan.FromMilliseconds(50)
        });

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (callCount <= 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        // Should have been called multiple times despite exceptions
        callCount.Should().BeGreaterThan(1);

        svc.CancelTask(taskId);
    }

    // ── ScheduledTask model ──────────────────────────────────────────

    [Fact]
    public void ScheduledTask_ShouldHaveDefaults()
    {
        var task = new ScheduledTask();
        task.Id.Should().NotBeNullOrEmpty();
        task.Name.Should().BeEmpty();
        task.Action.Should().BeNull();
        task.Interval.Should().Be(TimeSpan.FromMinutes(1));
        task.IsEnabled.Should().BeTrue();
    }

    // ── Properties ───────────────────────────────────────────────────

    [Fact]
    public async Task TaskCount_ShouldReflectScheduledTasks()
    {
        var svc = Svc;
        await svc.StartAsync();

        var id1 = "count1-" + Guid.NewGuid();
        var id2 = "count2-" + Guid.NewGuid();
        var before = svc.TaskCount;

        svc.ScheduleTask(new ScheduledTask { Id = id1, Name = "A", Action = _ => Task.CompletedTask, Interval = TimeSpan.FromHours(1) });
        svc.ScheduleTask(new ScheduledTask { Id = id2, Name = "B", Action = _ => Task.CompletedTask, Interval = TimeSpan.FromHours(1) });

        svc.TaskCount.Should().Be(before + 2);

        // Cleanup
        svc.CancelTask(id1);
        svc.CancelTask(id2);
    }
}
