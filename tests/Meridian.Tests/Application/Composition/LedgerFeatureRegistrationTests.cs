using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class LedgerFeatureRegistrationTests
{
    [Fact]
    public void Register_AddsCorporateActionAccountingPreparationBoundaryAsSingletons()
    {
        var services = new ServiceCollection();

        new LedgerFeatureRegistration().Register(services, CompositionOptions.Default);

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(CorporateActionAccountingProjectionService) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(ICorporateActionAccountingProjectionService) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(CorporateActionAssetAccountingEventMapper) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(ICorporateActionAssetAccountingEventMapper) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICorporateActionAccountingProjectionService>()
            .Should().BeSameAs(provider.GetRequiredService<CorporateActionAccountingProjectionService>());
        provider.GetRequiredService<ICorporateActionAssetAccountingEventMapper>()
            .Should().BeSameAs(provider.GetRequiredService<CorporateActionAssetAccountingEventMapper>());
    }

    [Fact]
    public void Register_PreservesHostProvidedCorporateActionAccountingBoundaries()
    {
        var customProjector = new StubCorporateActionAccountingProjectionService();
        var customMapper = new StubCorporateActionAssetAccountingEventMapper();
        var services = new ServiceCollection()
            .AddSingleton<ICorporateActionAccountingProjectionService>(customProjector)
            .AddSingleton<ICorporateActionAssetAccountingEventMapper>(customMapper);

        new LedgerFeatureRegistration().Register(services, CompositionOptions.Default);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICorporateActionAccountingProjectionService>()
            .Should().BeSameAs(customProjector);
        provider.GetRequiredService<ICorporateActionAssetAccountingEventMapper>()
            .Should().BeSameAs(customMapper);
    }

    private sealed class StubCorporateActionAccountingProjectionService : ICorporateActionAccountingProjectionService
    {
        public CorporateActionAccountingProjectionDto Project(CorporateActionAccountingProjectionRequest request) =>
            throw new NotSupportedException();
    }

    private sealed class StubCorporateActionAssetAccountingEventMapper : ICorporateActionAssetAccountingEventMapper
    {
        public CorporateActionAssetAccountingEventMapResult Map(CorporateActionAssetAccountingEventMapRequest request) =>
            throw new NotSupportedException();
    }
}
