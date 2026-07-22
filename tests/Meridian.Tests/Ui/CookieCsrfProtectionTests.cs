using System.Net;
using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class CookieCsrfProtectionTests
{
    [Fact]
    public void ShouldUseSecureCookies_WhenProductionHttpRequestIsLoopback_ReturnsTrue()
    {
        var context = CreateContext(IPAddress.Loopback, IPAddress.Loopback, isHttps: false);

        CookieCsrfProtection.ShouldUseSecureCookies(context).Should().BeTrue();
    }


    [Fact]
    public void ShouldUseSecureCookies_WhenDevelopmentHttpRequestIsLoopback_ReturnsFalse()
    {
        var context = CreateContext(IPAddress.Loopback, IPAddress.Loopback, isHttps: false, Environments.Development);

        CookieCsrfProtection.ShouldUseSecureCookies(context).Should().BeFalse();
    }

    [Fact]
    public void ShouldUseSecureCookies_WhenProductionHttpRequestIsNotLoopback_ReturnsTrue()
    {
        var context = CreateContext(IPAddress.Parse("192.0.2.10"), IPAddress.Parse("192.0.2.20"), isHttps: false);

        CookieCsrfProtection.ShouldUseSecureCookies(context).Should().BeTrue();
    }

    [Fact]
    public void ShouldUseSecureCookies_WhenLoopbackRequestUsesHttps_ReturnsTrue()
    {
        var context = CreateContext(IPAddress.IPv6Loopback, IPAddress.IPv6Loopback, isHttps: true);

        CookieCsrfProtection.ShouldUseSecureCookies(context).Should().BeTrue();
    }

    private static DefaultHttpContext CreateContext(IPAddress remoteAddress, IPAddress localAddress, bool isHttps, string environmentName = Environments.Production)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Connection.LocalIpAddress = localAddress;
        context.Request.Scheme = isHttps ? "https" : "http";
        return context;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Meridian.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
