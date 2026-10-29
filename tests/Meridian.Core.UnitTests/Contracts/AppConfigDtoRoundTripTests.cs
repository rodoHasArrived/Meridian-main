using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Xunit;

namespace Meridian.Tests.Contracts;

public sealed class AppConfigDtoRoundTripTests
{
    [Fact]
    public void UnmodeledTopLevelSections_SurviveDeserializeSerializeRoundTrip()
    {
        const string json = """
        {
          "dataRoot": "data",
          "dataSource": "IB",
          "ApiHost": { "DeploymentMode": "LocalWorkstation" },
          "PaperTrading": { "Gateway": { "ScaffoldMarketFillPrice": 2.5 } },
          "Status": { "SsePublishIntervalMs": 500 },
          "Connectivity": { "Probes": { "TcpConnectTimeoutMs": 3000 } }
        }
        """;

        var dto = JsonSerializer.Deserialize<AppConfigDto>(json);

        dto.Should().NotBeNull();
        dto!.AdditionalSections.Should().NotBeNull();
        dto.AdditionalSections.Should().ContainKeys("ApiHost", "PaperTrading", "Status", "Connectivity");

        var rewritten = JsonSerializer.Serialize(dto);

        using var doc = JsonDocument.Parse(rewritten);
        var root = doc.RootElement;
        root.GetProperty("PaperTrading").GetProperty("Gateway").GetProperty("ScaffoldMarketFillPrice").GetDecimal()
            .Should().Be(2.5m);
        root.GetProperty("Status").GetProperty("SsePublishIntervalMs").GetInt32().Should().Be(500);
        root.GetProperty("Connectivity").GetProperty("Probes").GetProperty("TcpConnectTimeoutMs").GetInt32()
            .Should().Be(3000);
        root.GetProperty("ApiHost").GetProperty("DeploymentMode").GetString().Should().Be("LocalWorkstation");
    }

    [Fact]
    public void CoreAppConfig_UnmodeledTopLevelSections_SurviveDeserializeSerializeRoundTrip()
    {
        const string json = """
        {
          "DataRoot": "data",
          "PaperTrading": { "Gateway": { "ScaffoldMarketFillPrice": 2.5 } },
          "Status": { "SsePublishIntervalMs": 500 },
          "Connectivity": { "Probes": { "TcpConnectTimeoutMs": 3000 } }
        }
        """;

        var config = JsonSerializer.Deserialize<Meridian.Core.Config.AppConfig>(json);

        config.Should().NotBeNull();
        config!.AdditionalSections.Should().NotBeNull();
        config.AdditionalSections.Should().ContainKeys("PaperTrading", "Status", "Connectivity");

        var rewritten = JsonSerializer.Serialize(config);

        using var doc = JsonDocument.Parse(rewritten);
        var root = doc.RootElement;
        root.GetProperty("PaperTrading").GetProperty("Gateway").GetProperty("ScaffoldMarketFillPrice").GetDecimal()
            .Should().Be(2.5m);
        root.GetProperty("Status").GetProperty("SsePublishIntervalMs").GetInt32().Should().Be(500);
        root.GetProperty("Connectivity").GetProperty("Probes").GetProperty("TcpConnectTimeoutMs").GetInt32()
            .Should().Be(3000);
    }

    [Fact]
    public void ConfigSchema_DoesNotAdvertiseExtensionDataCatchAll()
    {
        var schema = new Meridian.Core.Config.ConfigJsonSchemaGenerator().GenerateSchemaJson();
        schema.Should().NotContain("AdditionalSections");
    }
}
