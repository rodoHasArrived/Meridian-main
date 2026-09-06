using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Operations;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Application.Composition;

public sealed class ProductionRegistrationGuardServiceTests
{
    [Fact]
    public async Task StartAsync_ProductionPosture_RejectsBindingRegisteredAfterCompositionValidation()
    {
        // The exact PRD-000 bypass: the in-memory binding lands after the composition root
        // (and its inline Validate call) has already run.
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore, InMemoryTestStore>();
        });

        Func<Task> act = () => host.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*rejected non-production DI registrations*InMemoryTestStore*");
    }

    [Fact]
    public async Task Preflight_ProductionPosture_RefusesAStaticallyProhibitedBindingBeforeAnyShell()
    {
        // Seventeenth Codex review round. Keeping the whole guard behind the shell meant a
        // prohibited production graph stayed interactive -- MainWindow shown, its loaded workflow
        // started -- until hosted-service startup got round to refusing it. The descriptor scan
        // that decides this resolves nothing, so it belongs in front of the window.
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        services.AddProductionRegistrationGuard();
        services.AddSingleton<ITestStore, InMemoryTestStore>();
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider);

        (await preflight.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*rejected non-production DI registrations*InMemoryTestStore*");
    }

    [Fact]
    public async Task Preflight_DoesNotResolveFactorySingletons()
    {
        // The other half of the same split, and the reason the ninth round kept this guard out of
        // the preflight in the first place: a factory that cannot construct must NOT stop the shell
        // appearing. If the preflight ever started resolving singletons, this composition would
        // refuse here instead of behind the window, and an operator would be left with nothing.
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        services.AddProductionRegistrationGuard();
        services.AddSingleton<ITestStore>(_ => throw new InvalidOperationException("cannot construct"));
        await using var provider = services.BuildServiceProvider();

        var preflight = async () => await StartupRefusalPreflight.RunAsync(provider);

        await preflight.Should().NotThrowAsync(
            "eager factory validation belongs behind the shell, where the ninth round put it");

        // ...and it is still caught there.
        var behindTheShell = async () => await provider
            .GetRequiredService<ProductionRegistrationGuardService>()
            .StartAsync(CancellationToken.None);
        await behindTheShell.Should().ThrowAsync<StartupRefusedException>();
    }

    [Fact]
    public async Task StartAsync_UnconstructibleSingleton_RaisesARefusalAHostCannotDegradePast()
    {
        // Codex review finding on PR #2871. The three refusal sites in
        // ProductionServiceRegistrationPolicy took StartupRefusedException, but this one -- raised
        // when final-graph validation cannot construct a registered singleton -- stayed a bare
        // InvalidOperationException. HostStartupEscalation.IsRefusal therefore did not match it, so
        // the WPF shell's tolerant catch went on swallowing exactly the bypass that change closes.
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore>(_ => throw new InvalidOperationException("cannot construct"));
        });

        Func<Task> act = () => host.StartAsync();

        var refusal = await act.Should().ThrowAsync<StartupRefusedException>();
        refusal.Which.Message.Should().Contain("could not construct singleton service");
        HostStartupEscalation.IsRefusal(refusal.Which).Should().BeTrue(
            "a host that escalates refusals must be able to tell this apart from a worker failing");
    }

    [Fact]
    public async Task StartAsync_ProductionPosture_RejectsFactoryHiddenNonProductionImplementation()
    {
        // Factory descriptors expose no implementation type; the guard must resolve the
        // singleton and reject by the actual runtime type.
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore>(_ => new InMemoryTestStore());
        });

        Func<Task> act = () => host.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*rejected non-production DI registrations*InMemoryTestStore*");
    }

    [Fact]
    public async Task StartAsync_ProductionPosture_RejectsFactoryHiddenMarkerImplementationRegardlessOfName()
    {
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore>(_ => new ProcessLocalTestStore());
        });

        Func<Task> act = () => host.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*rejected non-production DI registrations*ProcessLocalTestStore*");
    }

    [Fact]
    public async Task StartAsync_ProductionPosture_WithOnlyProductionSafeBindings_Starts()
    {
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore, DurableTestStore>();
            services.AddSingleton<ISecondaryTestStore>(_ => new DurableSecondaryTestStore());
        });

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_LocalWorkstationPosture_UnlabeledInMemoryDurableBindings_RefusesStartupNamingBindings()
    {
        // W9-TRUTH-001: the supported local posture asserts durable money-path stores at startup.
        // Fabricated in-memory durables without the pinned non-real provenance label are refused.
        using var quietEnvironment = new ProductionEnvironmentQuietScope();
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore, InMemoryTestStore>();
            services.AddSingleton<ISecondaryTestStore>(_ => new InMemorySecondaryTestStore());
        });

        Func<Task> act = () => host.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Supported local-workstation posture requires durable money-path stores*InMemoryTestStore*");
    }

    [Fact]
    public async Task StartAsync_LocalWorkstationPosture_LabeledComposition_AcceptsInMemoryDurableBindings()
    {
        // The pinned non-real label is the sanctioned way to run fabricated local stores: the
        // persistent simulation label rides every surface instead of the durability assertion.
        using var quietEnvironment = new ProductionEnvironmentQuietScope();
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
            services.ForceDataProvenanceLabel(DataProvenance.Seeded);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore, InMemoryTestStore>();
        });

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_LocalWorkstationPosture_DoesNotRejectNonDurableDevelopmentBindings()
    {
        using var quietEnvironment = new ProductionEnvironmentQuietScope();
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestGadget, InMemoryTestGadget>();
        });

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_ProductionPosture_UnconstructibleSingleton_FailsStartupWithDiagnostic()
    {
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<ITestStore>(_ => throw new InvalidDataException("broken factory"));
        });

        Func<Task> act = () => host.StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*could not construct singleton service*");
    }

    [Fact]
    public void AddProductionRegistrationGuard_InsertsGuardAsFirstHostedServiceAndIsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostedService, RecordingHostedService>();

        services.AddProductionRegistrationGuard();
        services.AddProductionRegistrationGuard();

        services[0].ServiceType.Should().Be(typeof(IHostedService));
        services.Count(descriptor => descriptor.ServiceType == typeof(ProductionRegistrationGuardService))
            .Should().Be(1);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>().First()
            .Should().BeOfType<ProductionRegistrationGuardService>();
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton, false, false)]
    [InlineData(ServiceLifetime.Scoped, false, false)]
    [InlineData(ServiceLifetime.Transient, false, false)]
    [InlineData(ServiceLifetime.Singleton, true, false)]
    [InlineData(ServiceLifetime.Scoped, true, false)]
    [InlineData(ServiceLifetime.Transient, true, false)]
    [InlineData(ServiceLifetime.Singleton, false, true)]
    [InlineData(ServiceLifetime.Scoped, false, true)]
    [InlineData(ServiceLifetime.Transient, false, true)]
    [InlineData(ServiceLifetime.Singleton, true, true)]
    [InlineData(ServiceLifetime.Scoped, true, true)]
    [InlineData(ServiceLifetime.Transient, true, true)]
    public async Task StartAsync_FactoryHiddenStore_RefusesBeforeWorkersStart(
        ServiceLifetime lifetime, bool keyed, bool local)
    {
        using var quiet = new ProductionEnvironmentQuietScope();
        var worker = new StartProbe();
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(local
                ? MeridianDeploymentPosture.LocalWorkstation : MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddSingleton<IHostedService>(worker);
            // Register after the guard, with a second safe binding under the same key. Validation
            // must inspect the whole enumerable, not only the last service selected by GetService.
            AddStoreFactory(services, lifetime, keyed, () => new InMemoryTestStore());
            AddStoreFactory(services, lifetime, keyed, () => new DurableTestStore());
        });

        var start = () => host.StartAsync();
        (await start.Should().ThrowAsync<StartupRefusedException>())
            .Which.Message.Should().Contain(nameof(InMemoryTestStore));
        worker.StartCount.Should().Be(0);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton, false)]
    [InlineData(ServiceLifetime.Scoped, false)]
    [InlineData(ServiceLifetime.Transient, false)]
    [InlineData(ServiceLifetime.Singleton, true)]
    [InlineData(ServiceLifetime.Scoped, true)]
    [InlineData(ServiceLifetime.Transient, true)]
    public async Task StartAsync_DurableFactory_AllLifetimesAndExplicitKeysStart(
        ServiceLifetime lifetime, bool keyed)
    {
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            AddStoreFactory(services, lifetime, keyed, () => new DurableTestStore());
        });
        await host.StartAsync();
        await host.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_WildcardFactory_RefusesUnverifiableKeySpace(bool local)
    {
        using var quiet = new ProductionEnvironmentQuietScope();
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(local
                ? MeridianDeploymentPosture.LocalWorkstation : MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            services.AddKeyedSingleton<ITestStore>(KeyedService.AnyKey,
                (_, key) => Equals(key, "unapproved-account") ? new InMemoryTestStore() : new DurableTestStore());
        });
        var start = () => host.StartAsync();
        (await start.Should().ThrowAsync<StartupRefusedException>())
            .Which.Message.Should().Contain("wildcard keyed factory");
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped, false)]
    [InlineData(ServiceLifetime.Transient, true)]
    public async Task StartAsync_ValidationResources_AreAsynchronouslyDisposedOnSuccessAndRefusal(
        ServiceLifetime lifetime, bool keyed)
    {
        foreach (var refuse in new[] { false, true })
        {
            var stores = new List<DurableAsyncTestStore>();
            var services = new ServiceCollection();
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            AddStoreFactory(services, lifetime, keyed, () =>
            {
                var store = new DurableAsyncTestStore();
                stores.Add(store);
                return store;
            });
            if (refuse)
                services.AddSingleton<ISecondaryTestStore>(_ => throw new InvalidDataException("cannot construct"));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var start = () => provider.GetRequiredService<ProductionRegistrationGuardService>().StartAsync(CancellationToken.None);
            if (refuse)
                await start.Should().ThrowAsync<StartupRefusedException>();
            else
                await start();
            stores.Should().ContainSingle();
            stores[0].DisposeCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task StartAsync_Cancellation_DisposesValidationScopeAndDoesNotBecomeARefusal()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new DurableAsyncTestStore();
        var services = new ServiceCollection();
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        services.AddProductionRegistrationGuard();
        services.AddScoped<ITestStore>(_ =>
        {
            cancellation.Cancel();
            return store;
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var start = () => provider.GetRequiredService<ProductionRegistrationGuardService>().StartAsync(cancellation.Token);
        await start.Should().ThrowAsync<OperationCanceledException>();
        store.DisposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_NullFactoryResult_RefusesMissingAuthority(bool keyed)
    {
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
            services.AddProductionRegistrationGuard();
            AddStoreFactory(services, ServiceLifetime.Scoped, keyed, () => null!);
        });
        var start = () => host.StartAsync();
        (await start.Should().ThrowAsync<StartupRefusedException>()).Which.Message.Should().Contain("returned null");
    }

    [Fact]
    public async Task StartAsync_LabeledLocalFactory_RemainsExplicitlySimulated()
    {
        using var quiet = new ProductionEnvironmentQuietScope();
        using var host = BuildHost(services =>
        {
            services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.LocalWorkstation);
            services.ForceDataProvenanceLabel(DataProvenance.Seeded);
            services.AddProductionRegistrationGuard();
            AddStoreFactory(services, ServiceLifetime.Scoped, true, () => new InMemoryTestStore());
        });
        await host.StartAsync();
        await host.StopAsync();
    }

    private static void AddStoreFactory(IServiceCollection services, ServiceLifetime lifetime, bool keyed, Func<ITestStore> factory)
        => services.Add(keyed
            ? new ServiceDescriptor(typeof(ITestStore), "company-a", (_, _) => factory(), lifetime)
            : new ServiceDescriptor(typeof(ITestStore), _ => factory(), lifetime));

    private sealed class DurableAsyncTestStore : ITestStore, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StartProbe : IHostedService
    {
        public int StartCount { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IHost BuildHost(Action<IServiceCollection> configure)
        => new Microsoft.Extensions.Hosting.HostBuilder()
            .UseDefaultServiceProvider(options => options.ValidateScopes = true)
            .ConfigureServices(services => configure(services))
            .Build();

    private interface ITestStore;

    private interface ISecondaryTestStore;

    private interface ITestGadget;

    private sealed class InMemoryTestGadget : ITestGadget;

    private sealed class InMemoryTestStore : ITestStore;

    private sealed class DurableTestStore : ITestStore;

    private sealed class InMemorySecondaryTestStore : ISecondaryTestStore;

    private sealed class DurableSecondaryTestStore : ISecondaryTestStore;

    private sealed class ProcessLocalTestStore : ITestStore, INonProductionOnlyService;

    private sealed class RecordingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
