using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Storage;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Composition;

public sealed class MaintenanceFeatureRegistrationTests
{
    [Fact]
    public void Register_PublishesArchiveSchedulerAsTheHostedSingleton()
    {
        var services = new ServiceCollection();

        new MaintenanceFeatureRegistration().Register(services, CompositionOptions.Default);

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(ScheduledArchiveMaintenanceService)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        var hostedDescriptor = services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.Lifetime == ServiceLifetime.Singleton
            && descriptor.ImplementationFactory != null).Which;

        var root = Path.Combine(Path.GetTempPath(), "meridian-maintenance-registration", Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new ArchiveMaintenanceScheduleManager(
                NullLogger<ArchiveMaintenanceScheduleManager>.Instance,
                root);
            using var scheduler = new ScheduledArchiveMaintenanceService(
                NullLogger<ScheduledArchiveMaintenanceService>.Instance,
                manager,
                Mock.Of<IFileMaintenanceService>(),
                Mock.Of<ITierMigrationService>(),
                new StorageOptions { RootPath = root });
            using var provider = new ServiceCollection()
                .AddSingleton(scheduler)
                .BuildServiceProvider();

            hostedDescriptor.ImplementationFactory!(provider).Should().BeSameAs(scheduler);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
