using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

public sealed class AppConfigJsonOptionsTests
{
    [Fact]
    public void Read_AllowsCommentsTrailingCommasAndDataSourceKindStrings()
    {
        const string json = """
            {
              // Hand-edited config files may keep short operator comments.
              "dataRoot": "data/live",
              "dataSource": "alpaca",
            }
            """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);

        config.Should().NotBeNull();
        config!.DataRoot.Should().Be("data/live");
        config.DataSource.Should().Be(DataSourceKind.Alpaca);
    }

    [Fact]
    public void Write_UsesPrettyPrintedCamelCaseConfigJson()
    {
        var config = new AppConfig(
            DataRoot: "data/live",
            DataSource: DataSourceKind.Polygon);

        var json = JsonSerializer.Serialize(config, AppConfigJsonOptions.Write);

        json.Should().Contain("  \"dataRoot\": \"data/live\"");
        json.Should().Contain("  \"dataSource\": \"Polygon\"");
    }
}
