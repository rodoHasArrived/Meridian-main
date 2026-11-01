using FluentAssertions;
using Meridian.Application.Services;
using Serilog;
using Xunit;
using static Meridian.Application.Services.AutoConfigurationService;

namespace Meridian.Tests.Application.Services;

public sealed class ConfigurationServiceProviderDetectionTests
{
    [Fact]
    public async Task FormatProviderDetection_WithConfiguredAndMissingProviders_ListsStatusAndMissingCredentials()
    {
        await using var service = new ConfigurationService(new LoggerConfiguration().CreateLogger());
        var providers = new[]
        {
            new DetectedProvider("alpaca", "Alpaca", true, [], true, 1, ["RealTime", "Historical"]),
            new DetectedProvider("polygon", "Polygon", false, ["POLYGON_API_KEY"], false, 2, ["Historical"])
        };

        var text = service.FormatProviderDetection(providers);

        text.Should().Contain("Detected Data Providers:");
        text.Should().Contain("[OK] Alpaca");
        text.Should().Contain("[--] Polygon");
        text.Should().Contain("Capabilities: RealTime, Historical");
        text.Should().Contain("Missing: POLYGON_API_KEY");
        text.Should().Contain("1/2 providers configured");
        text.Should().NotContain("--wizard");
    }

    [Fact]
    public async Task FormatProviderDetection_WithNoConfiguredProviders_IncludesSetupHint()
    {
        await using var service = new ConfigurationService(new LoggerConfiguration().CreateLogger());
        var providers = new[]
        {
            new DetectedProvider("alpaca", "Alpaca", false, ["ALPACA_KEY_ID"], false, 1, ["RealTime"])
        };

        var text = service.FormatProviderDetection(providers);

        text.Should().Contain("0/1 providers configured");
        text.Should().Contain("export ALPACA_KEY_ID=your-key-id");
        text.Should().Contain("Meridian --wizard");
    }
}
