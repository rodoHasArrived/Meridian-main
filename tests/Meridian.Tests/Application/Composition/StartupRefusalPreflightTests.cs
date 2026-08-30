using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// Eighth Codex review round on PR #2871. Hoisting the whole host ahead of the WPF shell decided
/// refusals early but made the window wait on every ordinary hosted service, so a slow data root
/// could hold the shell back indefinitely. The preflight runs the guards on their own instead;
/// these cover the behaviour the shell depends on, off Windows, where the shell itself cannot run.
/// </summary>
public sealed class StartupRefusalPreflightTests
{
    [Fact]
    public async Task RunAsync_RunsRefusalGuardsAndLeavesOrdinaryHostedServicesAlone()
    {
        var guard = new RecordingRefusalGuard();
        var ordinary = new RecordingHostedService();

        var services = new ServiceCollection();
        services.AddSingleton<IStartupRefusalGuard>(guard);
        services.AddSingleton<IHostedService>(ordinary);
        await using var provider = services.BuildServiceProvider();

        await StartupRefusalPreflight.RunAsync(provider);

        guard.Started.Should().Be(1, "the preflight exists to decide refusals");
        ordinary.Started.Should().Be(
            0,
            "a slow ordinary service must not be able to hold the shell back before it is shown");
    }

    [Fact]
    public async Task RunAsync_LetsARefusalPropagateSoTheCallerCanRefuseToShowAShell()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStartupRefusalGuard>(new RefusingGuard());
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider);

        var thrown = await preflight.Should().ThrowAsync<StartupRefusedException>();
        HostStartupEscalation.IsRefusal(thrown.Which).Should().BeTrue(
            "the shell decides what to do by this predicate");
    }

    [Fact]
    public async Task RunAsync_RunsEveryGuardRatherThanTheFirstOne()
    {
        // The marker exists so a guard added later is covered without the shell being edited.
        var first = new RecordingRefusalGuard();
        var second = new RecordingRefusalGuard();

        var services = new ServiceCollection();
        services.AddSingleton<IStartupRefusalGuard>(first);
        services.AddSingleton<IStartupRefusalGuard>(second);
        await using var provider = services.BuildServiceProvider();

        await StartupRefusalPreflight.RunAsync(provider);

        first.Started.Should().Be(1);
        second.Started.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_OnACompositionWithNoGuards_DoesNothing()
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider);

        await preflight.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AGuardIsSafeToRunTwice_BecauseTheHostStartsItAgainBehindTheShell()
    {
        // The contract IStartupRefusalGuard states, pinned: pre-running a guard must not change
        // what starting it later does. A guard that acted on the composition instead of asking a
        // question about it would break the shell's startup rather than the preflight's.
        var guard = new RecordingRefusalGuard();

        var services = new ServiceCollection();
        services.AddSingleton(guard);
        services.AddSingleton<IStartupRefusalGuard>(sp => sp.GetRequiredService<RecordingRefusalGuard>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RecordingRefusalGuard>());
        await using var provider = services.BuildServiceProvider();

        await StartupRefusalPreflight.RunAsync(provider);
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        guard.Started.Should().Be(2, "the same singleton serves both roles");
    }

    private sealed class RecordingRefusalGuard : IStartupRefusalGuard
    {
        public int Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHostedService : IHostedService
    {
        public int Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RefusingGuard : IStartupRefusalGuard
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => throw new StartupRefusedException("This composition must not serve anything.");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
