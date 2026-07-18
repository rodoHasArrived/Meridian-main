using FluentAssertions;
using Meridian.Application.Composition.Startup;
using Meridian.Contracts.Lifecycle;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Composition.Startup;

public sealed class RuntimeReadinessServiceTests
{
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task EvaluateAsync_AllRequiredChecksPass_ReturnsReady()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var service = new RuntimeReadinessService(
            lifecycle,
            [CreateCheck("config", LifecycleCheckRequirement.Required, LifecycleCheckStatus.Passing)]);

        var snapshot = await service.EvaluateAsync();

        snapshot.Readiness.Should().Be(RuntimeReadinessStatus.Ready);
        snapshot.State.Should().Be(RuntimeLifecycleState.Ready);
        snapshot.AcceptingWork.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_RequiredCheckFails_ReturnsNotReady()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var service = new RuntimeReadinessService(
            lifecycle,
            [CreateCheck("database", LifecycleCheckRequirement.Required, LifecycleCheckStatus.Failing)]);

        var snapshot = await service.EvaluateAsync();

        snapshot.Readiness.Should().Be(RuntimeReadinessStatus.NotReady);
        snapshot.State.Should().Be(RuntimeLifecycleState.EvaluatingReadiness);
        snapshot.AcceptingWork.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_DegradableCheckFails_ReturnsDegradedAndAcceptsWork()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var service = new RuntimeReadinessService(
            lifecycle,
            [
                CreateCheck("config", LifecycleCheckRequirement.Required, LifecycleCheckStatus.Passing),
                CreateCheck("provider", LifecycleCheckRequirement.Degradable, LifecycleCheckStatus.Failing)
            ]);

        var snapshot = await service.EvaluateAsync();

        snapshot.Readiness.Should().Be(RuntimeReadinessStatus.Degraded);
        snapshot.State.Should().Be(RuntimeLifecycleState.Degraded);
        snapshot.AcceptingWork.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_CheckThrows_RecordsSanitizedFailure()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var service = new RuntimeReadinessService(
            lifecycle,
            [
                new DelegateRuntimeReadinessCheck(
                    "database",
                    "Database",
                    LifecycleCheckRequirement.Required,
                    _ => throw new InvalidOperationException("secret connection string"))
            ]);

        var snapshot = await service.EvaluateAsync();

        snapshot.Checks.Should().ContainSingle().Which.Message.Should().Be("Database check failed: InvalidOperationException");
        snapshot.Checks[0].Message.Should().NotContain("secret connection string");
    }

    private static IRuntimeReadinessCheck CreateCheck(
        string id,
        LifecycleCheckRequirement requirement,
        LifecycleCheckStatus status)
        => new DelegateRuntimeReadinessCheck(
            id,
            id,
            requirement,
            _ => ValueTask.FromResult(new RuntimeReadinessCheckResult(status, status.ToString())));
}
