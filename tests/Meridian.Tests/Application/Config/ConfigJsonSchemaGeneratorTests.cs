using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Config;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ConfigJsonSchemaGeneratorTests
{
    private readonly ConfigJsonSchemaGenerator _generator = new();

    [Fact]
    public void GenerateSchema_IncludesJsonSchemaMetadata()
    {
        var schema = _generator.GenerateSchema();

        schema["$schema"]?.GetValue<string>().Should().Be("https://json-schema.org/draft/2020-12/schema");
        schema["title"]?.GetValue<string>().Should().Be("Meridian appsettings schema");
        schema["properties"].Should().NotBeNull();
    }

    [Fact]
    public void GenerateSchema_UsesStringEnumValuesForDataSource()
    {
        var dataSourceSchema = GetRootProperty("DataSource");

        dataSourceSchema["type"]?.GetValue<string>().Should().Be("string");
        dataSourceSchema["enum"]!.AsArray().Select(static n => n!.GetValue<string>())
            .Should()
            .Contain(["IB", "Alpaca", "Polygon", "StockSharp", "NYSE", "Synthetic"]);
    }

    [Fact]
    public void GenerateSchema_AllowsNullForOptionalComplexSections()
    {
        var backfillSchema = GetRootProperty("Backfill");

        backfillSchema["anyOf"].Should().NotBeNull();
        var supportsNull = backfillSchema["anyOf"]!.AsArray()
            .Select(static node => node!.AsObject())
            .Any(static node => node["type"]?.GetValue<string>() == "null");
        supportsNull.Should().BeTrue();
    }

    [Fact]
    public void GenerateSchema_DescribesArrayItemDefinitions()
    {
        var symbolsSchema = GetRootProperty("Symbols");
        var arrayBranch = symbolsSchema["anyOf"]!.AsArray()
            .Select(static node => node!.AsObject())
            .First(static node => node["type"]?.GetValue<string>() == "array");

        arrayBranch["items"]!["$ref"]?.GetValue<string>().Should().Be("#/$defs/SymbolConfig");
    }

    [Fact]
    public void WriteSchema_WritesSchemaToDisk()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"appsettings-schema-{Guid.NewGuid():N}.json");

        try
        {
            _generator.WriteSchema(tempPath);

            File.Exists(tempPath).Should().BeTrue();

            var json = File.ReadAllText(tempPath);
            json.Should().Contain("\"$schema\"");
            json.Should().Contain("\"DataSource\"");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private JsonObject GetRootProperty(string propertyName)
    {
        var schema = _generator.GenerateSchema();
        var properties = schema["properties"]!.AsObject();
        return properties[propertyName]!.AsObject();
    }
}
