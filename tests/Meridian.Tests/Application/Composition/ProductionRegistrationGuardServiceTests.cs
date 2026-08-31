using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Operations;
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

    private static IHost BuildHost(Action<IServiceCollection> configure)
        => new Microsoft.Extensions.Hosting.HostBuilder()
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
