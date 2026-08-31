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

    [Fact]
    public void ValidateIbClientPortalCertificate_WithValidCertificate_AcceptsAnyHost()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://gw.example.com/v1/api/portfolio/accounts");

        var accepted = HttpClientConfiguration.ValidateIbClientPortalCertificate(
            request, certificate: null, chain: null, System.Net.Security.SslPolicyErrors.None);

        accepted.Should().BeTrue("a certificate that passes standard validation is always acceptable");
    }

    [Theory]
    [InlineData("https://localhost:5000/v1/api/portfolio/accounts")]
    [InlineData("https://127.0.0.1:5000/v1/api/portfolio/accounts")]
    [InlineData("https://[::1]:5000/v1/api/portfolio/accounts")]
    public void ValidateIbClientPortalCertificate_WithCertificateErrors_AcceptsLoopbackHostsOnly(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var accepted = HttpClientConfiguration.ValidateIbClientPortalCertificate(
            request, certificate: null, chain: null,
            System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors);

        accepted.Should().BeTrue("IB Gateway's self-signed certificate is tolerated on loopback hosts");
    }

    [Theory]
    [InlineData("https://gw.example.com/v1/api/portfolio/accounts")]
    [InlineData("https://192.168.1.20:5000/v1/api/portfolio/accounts")]
    public void ValidateIbClientPortalCertificate_WithCertificateErrors_RejectsRemoteHosts(string url)
    {
        // The previous DangerousAcceptAnyServerCertificateValidator accepted any certificate
        // from any host, leaving the brokerage-credential path with no MITM protection.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var accepted = HttpClientConfiguration.ValidateIbClientPortalCertificate(
            request, certificate: null, chain: null,
            System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors);

        accepted.Should().BeFalse("certificate errors from non-loopback hosts must fail TLS validation");
    }
}
