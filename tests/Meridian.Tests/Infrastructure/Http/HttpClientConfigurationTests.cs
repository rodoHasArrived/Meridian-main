using FluentAssertions;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Infrastructure.Http;

public sealed class HttpClientConfigurationTests
{
    [Fact]
    public void AddMarketDataHttpClients_RegistersRobinhoodSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClients();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.RobinhoodSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.robinhood.com/"));
    }

    [Fact]
    public void AddMarketDataHttpClientsTracked_RegistersRobinhoodSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClientsTracked((_, _, _) => { });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.RobinhoodSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.robinhood.com/"));
    }
}
