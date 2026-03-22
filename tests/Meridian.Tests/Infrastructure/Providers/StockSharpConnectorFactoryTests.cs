using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.StockSharp;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class StockSharpConnectorFactoryTests
{
    [Fact]
    public void SupportedConnectorTypes_IncludeAllDocumentedNamedConnectors()
    {
        StockSharpConnectorFactory.SupportedConnectorTypes.Should().Contain(new[]
        {
            "Rithmic",
            "IQFeed",
            "CQG",
            "InteractiveBrokers",
            "Binance",
            "Coinbase",
            "Kraken",
            "Custom"
        });
    }

    [Theory]
    [InlineData("Rithmic")]
    [InlineData("IQFeed")]
    [InlineData("CQG")]
    [InlineData("InteractiveBrokers")]
    [InlineData("Binance")]
    [InlineData("Coinbase")]
    [InlineData("Kraken")]
    [InlineData("Custom")]
    public void IsSupported_ReturnsTrueForDocumentedConnectorTypes(string connectorType)
    {
        StockSharpConnectorFactory.IsSupported(connectorType).Should().BeTrue();
    }

    [Fact]
    public void UnsupportedConnectorMessage_IncludesRecoveryGuidance()
    {
        var message = StockSharpConnectorFactory.BuildUnsupportedConnectorMessage(
            new StockSharpConfig(Enabled: true, ConnectorType: "CustomVendor"));

        message.Should().Contain("CustomVendor");
        message.Should().Contain("AdapterType");
        message.Should().Contain("AdapterAssembly");
        message.Should().Contain("docs/providers/stocksharp-connectors.md");
        message.Should().Contain("Rithmic");
        message.Should().Contain("Kraken");
    }

    [Fact]
    public void AdapterLoadFailureMessage_IncludesAssemblyQualificationGuidance()
    {
        var message = StockSharpConnectorFactory.BuildAdapterLoadFailureMessage("Vendor.Adapter");

        message.Should().Contain("Vendor.Adapter");
        message.Should().Contain("AdapterAssembly");
        message.Should().Contain("docs/providers/stocksharp-connectors.md");
    }
}
