using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ConfigEnvironmentOverrideTests
{
    [Fact]
    public void ApplyOverrides_MapsIbSocketAndClientPortalSeparately()
    {
        using var hostVar = new EnvironmentVariableScope("MDC_IB_HOST", "10.0.0.5");
        using var portVar = new EnvironmentVariableScope("MDC_IB_PORT", "7497");
        using var clientIdVar = new EnvironmentVariableScope("MDC_IB_CLIENT_ID", "17");
        using var paperVar = new EnvironmentVariableScope("MDC_IB_PAPER", "true");
        using var portalEnabledVar = new EnvironmentVariableScope("MDC_IB_CLIENT_PORTAL_ENABLED", "true");
        using var portalUrlVar = new EnvironmentVariableScope("MDC_IB_CLIENT_PORTAL_BASE_URL", "https://localhost:5000");
        using var portalCertVar = new EnvironmentVariableScope("MDC_IB_CLIENT_PORTAL_ALLOW_SELF_SIGNED", "true");

        var sut = new ConfigEnvironmentOverride();
        var result = sut.ApplyOverrides(new AppConfig());

        result.IB.Should().NotBeNull();
        result.IB!.Host.Should().Be("10.0.0.5");
        result.IB.Port.Should().Be(7497);
        result.IB.ClientId.Should().Be(17);
        result.IB.UsePaperTrading.Should().BeTrue();

        result.IBClientPortal.Should().NotBeNull();
        result.IBClientPortal!.Enabled.Should().BeTrue();
        result.IBClientPortal.BaseUrl.Should().Be("https://localhost:5000");
        result.IBClientPortal.AllowSelfSignedCertificates.Should().BeTrue();
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
