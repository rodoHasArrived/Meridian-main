using FluentAssertions;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class BackendServiceManagerTests
{
    [Fact]
    public void BuildProcessArguments_StartsDesktopModeOnConfiguredPort()
    {
        var args = BackendServiceManager.BuildProcessArguments(
            @"C:\config\appsettings.json",
            "http://localhost:9105");

        args.Should().Equal(
            "--mode",
            "desktop",
            "--config",
            @"C:\config\appsettings.json",
            "--http-port",
            "9105");
    }

    [Fact]
    public void ResolveHttpPort_FallsBackToDefaultPortForInvalidServiceUrl()
    {
        var port = BackendServiceManager.ResolveHttpPort("not-a-valid-url");

        port.Should().Be(8080);
    }
}
