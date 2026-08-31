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

    [Fact]
    public void TheProductionRegistrationGuard_IsNotAPreflightGuard()
    {
        // Ninth Codex review round. Marking it in the eighth was a mistake: in a production
        // composition it resolves every factory-registered singleton to prove the graph is
        // constructible, so running it in the preflight would build the application graph with no
        // window on screen and a blocked constructor would leave the operator nothing at all --
        // exactly the failure the preflight was introduced to remove. A desktop reaches that branch
        // whenever MERIDIAN_MODE, DOTNET_ENVIRONMENT or the other posture variables say production,
        // so this is a real configuration rather than a hypothetical one.
        //
        // It still runs, first in the chain, as an ordinary hosted service behind the shell.
        var services = new ServiceCollection();
        services.AddProductionRegistrationGuard();

        services.Should().NotContain(
            descriptor => descriptor.ServiceType == typeof(IStartupRefusalGuard),
            "eager factory validation must not run in front of the shell");
        services.Should().Contain(
            descriptor => descriptor.ServiceType == typeof(IHostedService),
            "it still validates the final graph during host startup");
    }

    [Fact]
    public async Task AGuardThatCannotAnswer_IsARefusalRatherThanAnOrdinaryFailure()
    {
        // Sixteenth Codex review round. A guard that throws for some other reason -- the tenancy
        // guard failing to read the account store, say -- has not said the composition is safe; it
        // has said it cannot tell. Surfacing that as an ordinary exception meant every caller
        // applied its ordinary tolerance: the WPF shell reported a recoverable startup error and
        // showed the window, and the hosted-service retry behind it tolerates non-refusals too, so
        // a persistent read failure left the unpartitioned fund structure serving indefinitely.
        var services = new ServiceCollection();
        services.AddSingleton<IStartupRefusalGuard>(new UnanswerableGuard());
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider);

        var thrown = await preflight.Should().ThrowAsync<StartupRefusedException>();
        HostStartupEscalation.IsRefusal(thrown.Which).Should().BeTrue(
            "the shell decides whether to show a window by this predicate");
        thrown.Which.InnerException.Should().BeOfType<IOException>(
            "the operator needs the underlying fault to fix it");
    }

    [Fact]
    public async Task AGuardThatCannotAnswer_StopsTheGuardsAfterIt()
    {
        // An unresolved refusal question is not something to keep going past. The composition is
        // already not going to be served, and running further guards would only decide questions
        // that no longer have a host to apply to.
        var later = new RecordingRefusalGuard();

        var services = new ServiceCollection();
        services.AddSingleton<IStartupRefusalGuard>(new UnanswerableGuard());
        services.AddSingleton<IStartupRefusalGuard>(later);
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider);

        await preflight.Should().ThrowAsync<StartupRefusedException>();
        later.Started.Should().Be(0);
    }

    [Fact]
    public async Task ACancelledPreflight_IsNotReportedAsARefusal()
    {
        // Cancellation means the startup this was part of is being torn down, which is not the
        // same claim as "this composition must not serve". Reporting it as a refusal would put a
        // governance dialog in front of an operator who just closed the app.
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IStartupRefusalGuard>(new RecordingRefusalGuard());
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider, cancellation.Token);

        var thrown = await preflight.Should().ThrowAsync<OperationCanceledException>();
        HostStartupEscalation.IsRefusal(thrown.Which).Should().BeFalse();
    }

    private sealed class UnanswerableGuard : IStartupRefusalGuard
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => throw new IOException("The account store could not be read.");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
