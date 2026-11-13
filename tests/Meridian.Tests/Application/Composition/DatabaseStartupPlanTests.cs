using FluentAssertions;
using Meridian.Application.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class DatabaseStartupPlanTests
{
    [Fact]
    public void AddDatabaseStartupPlan_RepeatedRegistrationRetainsOnePlanAndAcceptsLaterModules()
    {
        var services = new ServiceCollection();
        services.AddDatabaseStartupPlan();
        var originalStepCount = services.Count(descriptor => descriptor.ServiceType == typeof(DatabaseStartupStep));
        services.AddDatabaseStartupPlan();
        var additional = Record("additional", [MoneyMarketStartup.StartupStep.Id], []);
        services.AddDatabaseStartupStep(additional);
        using var provider = services.BuildServiceProvider();

        var plan = provider.GetRequiredService<DatabaseStartupPlan>();

        plan.Steps.Select(step => step.Id).Should().Equal(
            "ledger", "security-master", "direct-lending", "asset-operations",
            "fund-accounts", "fund-structure", "banking", "money-market", "additional");
        plan.Steps.Should().HaveCount(originalStepCount + 1);
        plan.Steps.Should().ContainSingle(step => ReferenceEquals(step, additional));
        plan.Steps.ToList().IndexOf(MoneyMarketStartup.StartupStep)
            .Should().BeLessThan(plan.Steps.ToList().IndexOf(additional));
        provider.GetRequiredService<DatabaseStartupPlan>().Should().BeSameAs(plan);
    }

    [Fact]
    public async Task EnsureReadyAsync_OrdersDependenciesBeforeDependentsAndAwaitsCompletion()
    {
        var events = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plan = new DatabaseStartupPlan(
        [
            Record("projection", ["schema"], events),
            new DatabaseStartupStep("schema", ["connection"], async (_, cancellationToken, _) =>
            {
                events.Add("schema-start");
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
                events.Add("schema-finish");
            }),
            Record("connection", [], events)
        ]);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var initialization = plan.EnsureReadyAsync(services, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        events.Should().Equal("connection", "schema-start");
        initialization.IsCompleted.Should().BeFalse();

        release.SetResult();
        await initialization;
        events.Should().Equal("connection", "schema-start", "schema-finish", "projection");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("cycle")]
    [InlineData("self")]
    public void Constructor_InvalidGraph_RejectsBeforeAnyInitializerRuns(string scenario)
    {
        var writes = new List<string>();
        DatabaseStartupStep[] invalid = scenario switch
        {
            "missing" => [Record("reader", ["absent"], writes)],
            "duplicate" => [Record("same", [], writes), Record("same", [], writes)],
            "cycle" => [Record("one", ["two"], writes), Record("two", ["one"], writes)],
            "self" => [Record("one", ["one"], writes)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        // A valid independent writer registered first must also remain untouched.
        Action construct = () => _ = new DatabaseStartupPlan([Record("writer", [], writes), .. invalid]);

        construct.Should().Throw<InvalidOperationException>();
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureReadyAsync_FailurePreservesOriginalExceptionAndDoesNotRunLaterStep()
    {
        var events = new List<string>();
        var failure = new InvalidOperationException("migration sentinel");
        var plan = new DatabaseStartupPlan(
        [
            Record("connection", [], events),
            new DatabaseStartupStep("schema", ["connection"], (_, _, _) => Task.FromException(failure)),
            Record("projection", ["schema"], events)
        ]);
        using var services = new ServiceCollection().BuildServiceProvider();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => plan.EnsureReadyAsync(services));

        thrown.Should().BeSameAs(failure);
        events.Should().Equal("connection");
    }

    [Fact]
    public async Task EnsureReadyAsync_CancellationBetweenStepsPreventsLaterWrites()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var plan = new DatabaseStartupPlan(
        [
            new DatabaseStartupStep("connection", [], (_, token, _) =>
            {
                token.Should().Be(cancellation.Token);
                events.Add("connection");
                cancellation.Cancel();
                return Task.CompletedTask;
            }),
            Record("schema", ["connection"], events)
        ]);
        using var services = new ServiceCollection().BuildServiceProvider();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plan.EnsureReadyAsync(services, cancellation.Token));

        events.Should().Equal("connection");
    }

    [Fact]
    public async Task EnsureReadyAsync_AlreadyCanceled_DoesNotBeginInitialization()
    {
        var events = new List<string>();
        var plan = new DatabaseStartupPlan([Record("schema", [], events)]);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plan.EnsureReadyAsync(services, cancellation.Token));

        events.Should().BeEmpty();
    }

    private static DatabaseStartupStep Record(string id, string[] dependencies, List<string> events)
        => new(id, dependencies, (_, cancellationToken, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(id);
            return Task.CompletedTask;
        });
}
