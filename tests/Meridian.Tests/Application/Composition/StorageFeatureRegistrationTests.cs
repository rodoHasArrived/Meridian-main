using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.DirectLending;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Store;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.DirectLending;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class StorageFeatureRegistrationTests : IDisposable
{
    private readonly string? _originalSecurityMasterConnectionString;
    private readonly string? _originalSecurityMasterSchema;
    private readonly string? _originalDirectLendingConnectionString;
    private readonly string? _originalDirectLendingSchema;

    public StorageFeatureRegistrationTests()
    {
        _originalSecurityMasterConnectionString = Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable);
        _originalSecurityMasterSchema = Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable);
        _originalDirectLendingConnectionString = Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable);
        _originalDirectLendingSchema = Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable);
    }

    [Fact]
    public void Register_SkipsPostgresBackedServices_WhenConnectionStringsAreMissing()
    {
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().NotContain(sd => sd.ServiceType == typeof(SecurityMasterOptions));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IValidateOptions<SecurityMasterOptions>));
        services.Should().NotContain(sd => sd.ServiceType == typeof(ISecurityMasterStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ISecurityMasterIngestStatusService));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IPolygonCorporateActionFetcher));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IDirectLendingStateStore));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IDirectLendingService));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(SecurityMasterProjectionWarmupService));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DirectLendingOutboxDispatcher));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DailyAccrualWorker));
    }

    [Fact]
    public void Register_AddsPostgresBackedServices_WhenConnectionStringsAreConfigured()
    {
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, "Host=sm-db;Port=5432;Database=security;Username=postgres;Password=secret");
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, "Host=dl-db;Port=5432;Database=loans;Username=postgres;Password=secret");
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(SecurityMasterOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IValidateOptions<SecurityMasterOptions>));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ISecurityMasterStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ISecurityMasterIngestStatusService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IPolygonCorporateActionFetcher));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(DirectLendingOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDirectLendingStateStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDirectLendingService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(SecurityMasterProjectionWarmupService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DirectLendingOutboxDispatcher));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DailyAccrualWorker));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, _originalSecurityMasterConnectionString);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, _originalSecurityMasterSchema);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, _originalDirectLendingConnectionString);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, _originalDirectLendingSchema);
    }
}
