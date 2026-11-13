using ProviderCapabilityDescriptor = Meridian.ProviderSdk.ProviderCapabilityDescriptor;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderFamilyCatalogServiceTests
{
    [Fact]
    public async Task CatalogHistoricalMetadata_DoesNotMaskTheRegisteredProvider()
    {
        using var registry = new ProviderRegistry();
        var historical = new Mock<IHistoricalDataProvider>();
        historical.SetupGet(provider => provider.ProviderId).Returns("stooq");
        historical.SetupGet(provider => provider.ProviderDisplayName).Returns("Synthetic Stooq fixture");
        historical.SetupGet(provider => provider.ProviderDescription).Returns("Deterministic contract test");
        historical.SetupGet(provider => provider.ProviderCapabilities).Returns(ProviderCapabilities.None);
        registry.Register(historical.Object);
        var catalog = new ProviderFamilyCatalogService(registry, [], []);

        var family = catalog.GetFamily("stooq");

        family.Should().NotBeNull();
        (await family!.ResolveCapabilityAsync<IHistoricalDataProvider>(ProviderCapabilityKind.HistoricalBars))
            .Should().BeSameAs(historical.Object);
    }

    [Fact]
    public async Task CatalogStreamingMetadata_DoesNotMaskThePreparedAsynchronousFactory()
    {
        var prepared = false;
        var factoryCalls = 0;
        using var registry = new ProviderRegistry
        {
            CredentialPreparer = _ =>
            {
                prepared = true;
                return ValueTask.FromResult(PreparedProviderCredentials.None);
            }
        };
        var client = Mock.Of<IMarketDataClient>();
        registry.RegisterStreamingFactory("alpaca", () =>
        {
            prepared.Should().BeTrue("asynchronous resolution must prepare credentials before calling the synchronous factory");
            factoryCalls++;
            return client;
        });
        var catalog = new ProviderFamilyCatalogService(registry, [], []);

        var family = catalog.GetFamily("alpaca");

        family.Should().NotBeNull();
        (await family!.ResolveCapabilityAsync<IMarketDataClient>(ProviderCapabilityKind.RealtimeMarketData))
            .Should().BeSameAs(client);
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task MetadataOnlyCapability_RemainsUnresolved()
    {
        using var registry = new ProviderRegistry();
        var catalog = new ProviderFamilyCatalogService(registry, [], []);

        var family = catalog.GetFamily("alpaca");

        family.Should().NotBeNull();
        family!.SupportsCapability(ProviderCapabilityKind.RealtimeMarketData).Should().BeTrue();
        (await family.ResolveCapabilityAsync(ProviderCapabilityKind.RealtimeMarketData)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaceholderUpgrade_PreservesTheFirstConcreteRegistration(bool asynchronous)
    {
        var family = new MutableProviderFamilyAdapter("fixture", "Fixture", "Test family");
        var descriptor = new ProviderCapabilityDescriptor(ProviderCapabilityKind.HistoricalBars, "bars");
        var expected = new object();
        var ignored = new object();
        family.RegisterCapability(descriptor, resolver: null);
        family.RegisterCapability(descriptor, resolver: asynchronous ? null : () => expected,
            asyncResolver: asynchronous ? _ => ValueTask.FromResult<object?>(expected) : null);
        family.RegisterCapability(descriptor, resolver: () => ignored,
            asyncResolver: _ => ValueTask.FromResult<object?>(ignored));
        family.RegisterCapability(descriptor, resolver: null);

        (await family.ResolveCapabilityAsync(descriptor.Kind)).Should().BeSameAs(expected);
        family.CapabilityDescriptors.Should().ContainSingle(candidate => candidate.Kind == descriptor.Kind);
    }
}
