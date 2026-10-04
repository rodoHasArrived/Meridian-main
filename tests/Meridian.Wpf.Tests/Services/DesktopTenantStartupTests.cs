using Meridian.Application.Composition;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Tenancy;
using Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Wpf.Tests.Services;

public sealed class DesktopTenantStartupTests
{
    [Fact]
    public async Task Activation_WaitsForPreparationAndInspectionBeforeResolvingWorkspaceOrShowingShell()
    {
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspected = new TaskCompletionSource<TenantCutoverReadiness>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prerequisites = Substitute.For<ITenantCutoverStartupPrerequisites>();
        prerequisites.PrepareAsync(Arg.Any<CancellationToken>()).Returns(prepared.Task);
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        check.InspectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            inspectionStarted.TrySetResult();
            return inspected.Task;
        });
        var probe = new ActivationProbe();
        using var provider = CreateServices(check, probe, prerequisites).BuildServiceProvider();

        var activation = ActivateAsync(provider);
        activation.IsCompleted.Should().BeFalse();
        probe.AssertUnopened();
        await check.DidNotReceive().InspectAsync(Arg.Any<CancellationToken>());

        prepared.SetResult();
        await inspectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        activation.IsCompleted.Should().BeFalse();
        probe.AssertUnopened();

        inspected.SetResult(new TenantCutoverReadiness([]));
        await activation.WaitAsync(TimeSpan.FromSeconds(5));
        probe.Workspaces.Should().Be(1);
        probe.Shells.Should().Be(1);
        probe.Shows.Should().Be(1);
        probe.BackgroundStarts.Should().Be(0, "readiness must not start unrelated workers before the shell");

        var hosted = provider.GetServices<IHostedService>().ToArray();
        hosted.OfType<TenantCutoverGuardService>().Should().BeEmpty();
        hosted.OfType<DesktopTenantStartup>().Should().ContainSingle().Which.Should()
            .BeSameAs(provider.GetRequiredService<DesktopTenantStartup>());
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
        probe.BackgroundStarts.Should().Be(1);
        await prerequisites.Received(1).PrepareAsync(Arg.Any<CancellationToken>());
        await check.Received(1).InspectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusedInspection_NeverResolvesWorkspaceOrShowsShellIncludingHostedRetry()
    {
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        check.InspectAsync(Arg.Any<CancellationToken>()).Returns(new TenantCutoverReadiness(
            [new TenantCutoverFinding("ledger", "ledger_books", "missing tenant", 7)]));
        var probe = new ActivationProbe();
        using var provider = CreateServices(check, probe, Prepared()).BuildServiceProvider();

        Func<Task> activate = () => ActivateAsync(provider);
        await activate.Should().ThrowAsync<StartupRefusedException>().WithMessage("*ledger/ledger_books: 7*");
        Func<Task> hostedStart = () => provider.GetRequiredService<DesktopTenantStartup>().StartAsync(CancellationToken.None);
        await hostedStart.Should().ThrowAsync<StartupRefusedException>();
        probe.AssertUnopened();
        await check.Received(1).InspectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShutdownDuringInspection_PreventsActivationEvenIfInspectionCompletesLater()
    {
        // Deliberately ignore cancellation in the inspector, as a slow database driver might.
        var inspected = new TaskCompletionSource<TenantCutoverReadiness>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        check.InspectAsync(Arg.Any<CancellationToken>()).Returns(inspected.Task);
        var probe = new ActivationProbe();
        using var provider = CreateServices(check, probe, Prepared()).BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();

        var activation = ActivateAsync(provider, cancellation.Token);
        await check.Received(1).InspectAsync(Arg.Any<CancellationToken>());
        cancellation.Cancel();
        Func<Task> finish = () => activation.WaitAsync(TimeSpan.FromSeconds(5));
        await finish.Should().ThrowAsync<OperationCanceledException>();
        inspected.SetResult(new TenantCutoverReadiness([]));

        Func<Task> retry = () => ActivateAsync(provider);
        await retry.Should().ThrowAsync<OperationCanceledException>();
        probe.AssertUnopened();
        await check.Received(1).InspectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedMigrationOrImport_RefusesBeforeInspectionWithoutExposingSensitiveError()
    {
        var prerequisites = Substitute.For<ITenantCutoverStartupPrerequisites>();
        prerequisites.PrepareAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException(new InvalidOperationException("Password=secret; retained sensitive data")));
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        var probe = new ActivationProbe();
        using var provider = CreateServices(check, probe, prerequisites).BuildServiceProvider();

        Func<Task> activate = () => ActivateAsync(provider);
        var error = await activate.Should().ThrowAsync<StartupRefusedException>()
            .WithMessage("*workspace has not been opened*fund-structure-tenant-backfill.md*");
        error.Which.ToString().Should().NotContain("secret").And.NotContain("sensitive data");
        probe.AssertUnopened();
        await check.DidNotReceive().InspectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DesktopWithoutDurableStores_UsesDefaultPreparationAndCanActivateAfterInspection()
    {
        var check = Substitute.For<ITenantCutoverReadinessCheck>();
        check.InspectAsync(Arg.Any<CancellationToken>()).Returns(new TenantCutoverReadiness([]));
        var probe = new ActivationProbe();
        using var provider = CreateServices(check, probe).BuildServiceProvider();

        provider.GetRequiredService<ITenantCutoverStartupPrerequisites>()
            .Should().BeOfType<TenantCutoverStartupPrerequisites>();
        await ActivateAsync(provider);

        probe.Workspaces.Should().Be(1);
        probe.Shows.Should().Be(1);
        await check.Received(1).InspectAsync(Arg.Any<CancellationToken>());
    }

    private static ITenantCutoverStartupPrerequisites Prepared()
    {
        var prerequisites = Substitute.For<ITenantCutoverStartupPrerequisites>();
        prerequisites.PrepareAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return prerequisites;
    }

    private static ServiceCollection CreateServices(ITenantCutoverReadinessCheck check, ActivationProbe probe,
        ITenantCutoverStartupPrerequisites? prerequisites = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TenantScopeEnforcementOptions.FailClosed);
        services.AddSingleton(check);
        if (prerequisites is not null)
            services.AddSingleton(prerequisites);
        // Repeated composition must not reintroduce the original hosted guard or a second gate.
        services.AddDesktopTenantScopeServices();
        services.AddDesktopTenantScopeServices();
        services.AddSingleton<IHostedService>(new BackgroundProbe(probe));
        services.AddSingleton(_ =>
        {
            probe.Workspaces++;
            return new WorkspaceProbe();
        });
        services.AddSingleton(_ =>
        {
            probe.Shells++;
            return new ShellProbe(probe);
        });
        return services;
    }

    private static Task ActivateAsync(ServiceProvider provider, CancellationToken cancellationToken = default)
        => provider.GetRequiredService<DesktopTenantStartup>().ActivateAsync(() =>
        {
            provider.GetRequiredService<WorkspaceProbe>();
            provider.GetRequiredService<ShellProbe>().Show();
        }, cancellationToken);

    private sealed class ActivationProbe
    {
        public int Workspaces;
        public int Shells;
        public int Shows;
        public int BackgroundStarts;

        public void AssertUnopened()
        {
            Workspaces.Should().Be(0);
            Shells.Should().Be(0);
            Shows.Should().Be(0);
            BackgroundStarts.Should().Be(0);
        }
    }

    private sealed class WorkspaceProbe { }
    private sealed class ShellProbe(ActivationProbe probe)
    {
        public void Show() => probe.Shows++;
    }

    private sealed class BackgroundProbe(ActivationProbe probe) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            probe.BackgroundStarts++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
