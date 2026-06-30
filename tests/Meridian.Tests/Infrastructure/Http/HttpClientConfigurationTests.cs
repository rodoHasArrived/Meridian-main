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
    public void AddMarketDataHttpClients_RegistersAlphaVantageSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClients();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.AlphaVantageSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://www.alphavantage.co/"));
    }

    [Fact]
    public void AddMarketDataHttpClients_RegistersTwelveDataSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClients();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.TwelveDataSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.twelvedata.com/"));
    }

    [Fact]
    public void AddMarketDataHttpClients_RegistersTiingoSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClients();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.TiingoSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.tiingo.com/"));
    }

    [Fact]
    public void AddMarketDataHttpClients_RegistersFredSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClients();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.FredSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.stlouisfed.org/fred/"));
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

    [Fact]
    public void AddMarketDataHttpClientsTracked_RegistersAlphaVantageSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClientsTracked((_, _, _) => { });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.AlphaVantageSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://www.alphavantage.co/"));
    }

    [Fact]
    public void AddMarketDataHttpClientsTracked_RegistersTwelveDataSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClientsTracked((_, _, _) => { });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.TwelveDataSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.twelvedata.com/"));
    }

    [Fact]
    public void AddMarketDataHttpClientsTracked_RegistersTiingoSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClientsTracked((_, _, _) => { });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.TiingoSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.tiingo.com/"));
    }

    [Fact]
    public void AddMarketDataHttpClientsTracked_RegistersFredSymbolSearchClient()
    {
        var services = new ServiceCollection();
        services.AddMarketDataHttpClientsTracked((_, _, _) => { });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(HttpClientNames.FredSymbolSearch);

        client.BaseAddress.Should().Be(new Uri("https://api.stlouisfed.org/fred/"));
    }
}
