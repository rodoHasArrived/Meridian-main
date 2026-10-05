using FluentAssertions;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Moq;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderRegistryCapabilityFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Streaming_registration_paths_share_alias_lookup_replacement_and_runtime_enablement(bool streamingFirst)
    {
        using var registry = new ProviderRegistry();
        var first = Mock.Of<IMarketDataClient>();
        var replacement = Mock.Of<IMarketDataClient>();
        var firstCalls = 0;
        var replacementCalls = 0;

        Register(streamingFirst, " INTERACTIVE-BROKERS ", () =>
        {
            firstCalls++;
            return first;
        });

        registry.GetCapability<IMarketDataClient>("ibkr").Should().BeSameAs(first);
        registry.CreateStreamingClient(" IB ").Should().BeSameAs(first);
        registry.SupportedStreamingSources.Should().Equal("ibkr");
        firstCalls.Should().Be(2);

        Register(!streamingFirst, "ib", () =>
        {
            replacementCalls++;
            return replacement;
        });

        registry.GetCapability<IMarketDataClient>("interactive-brokers").Should().BeSameAs(replacement);
        registry.CreateStreamingClient("ibkr").Should().BeSameAs(replacement);
        registry.SupportedStreamingSources.Should().Equal("ibkr");
        firstCalls.Should().Be(2, "replacing either registration path must replace the same factory");
        replacementCalls.Should().Be(2);

        registry.Disable(" INTERACTIVE_BROKERS ");

        registry.GetCapability<IMarketDataClient>("ibkr").Should().BeNull();
        var createDisabled = () => registry.CreateStreamingClient("ib");
        createDisabled.Should().Throw<InvalidOperationException>();
        registry.SupportedStreamingSources.Should().BeEmpty();
        replacementCalls.Should().Be(2, "disabled capabilities must not invoke their factories");

        registry.Enable("interactivebrokers");

        registry.GetCapability<IMarketDataClient>("ib").Should().BeSameAs(replacement);
        registry.CreateStreamingClient("ibkr").Should().BeSameAs(replacement);
        registry.SupportedStreamingSources.Should().Equal("ibkr");
        replacementCalls.Should().Be(4);

        void Register(bool useStreamingRegistration, string providerId, Func<IMarketDataClient> factory)
        {
            if (useStreamingRegistration)
                registry.RegisterStreamingFactory(providerId, factory);
            else
                registry.RegisterCapabilityFactory(providerId, typeof(IMarketDataClient), factory);
        }
    }
}
