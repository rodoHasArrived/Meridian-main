using FluentAssertions;
using Meridian.Ui.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.UiServices;

[CollectionDefinition("ApiClientService composition serial", DisableParallelization = true)]
public sealed class ApiClientServiceCompositionCollection
{
}

[Collection("ApiClientService composition serial")]
public sealed class HttpClientConfigurationTests
{
    [Fact]
    public void AddDesktopApiClient_RegistersExternallyOwnedSingletonThatSurvivesProviderDisposal()
    {
        var services = new ServiceCollection();

        services.AddDesktopApiClient();

        var descriptor = services.Should()
            .ContainSingle(candidate => candidate.ServiceType == typeof(ApiClientService))
            .Subject;
        descriptor.ImplementationInstance.Should().BeSameAs(ApiClientService.Instance);
        descriptor.ImplementationFactory.Should().BeNull();
        descriptor.ImplementationType.Should().BeNull();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);

        // Assert the non-owning descriptor before exercising disposal. If registration ever
        // regresses to a factory, this test must fail without poisoning the process singleton.
        using (var firstProvider = services.BuildServiceProvider())
        {
            firstProvider.GetRequiredService<ApiClientService>()
                .Should().BeSameAs(ApiClientService.Instance);
        }

        using (var secondProvider = services.BuildServiceProvider())
        {
            secondProvider.GetRequiredService<ApiClientService>()
                .Should().BeSameAs(ApiClientService.Instance);
        }

        ApiClientService.Instance.GetBackfillClient().Should().NotBeNull();
    }
}
