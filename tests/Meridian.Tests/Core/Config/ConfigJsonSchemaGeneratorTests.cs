using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

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
            .Contain(["IB", "Alpaca", "Polygon", "NYSE", "Synthetic"]);
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
    public void GenerateSchema_IncludesApiHostOptions()
    {
        var apiHostSchema = GetRootProperty("ApiHost");
        var apiHostBranch = apiHostSchema["anyOf"]!.AsArray()
            .Select(static node => node!.AsObject())
            .First(static node => node["$ref"] is not null);

        apiHostBranch["$ref"]?.GetValue<string>().Should().Be("#/$defs/ApiHostOptions");

        var apiHostDefinition = _generator.GenerateSchema()["$defs"]!["ApiHostOptions"]!.AsObject();
        var properties = apiHostDefinition["properties"]!.AsObject();
        properties.Select(static property => property.Key).Should().Contain(
            "AllowedOrigins",
            "AllowInsecureTransportForReverseProxy",
            "DeploymentMode",
            "ServeWorkstationAssets",
            "Urls");
    }

    [Fact]
    public void GenerateSchema_ApiHostDeploymentModeMatchesHostOptions()
    {
        var apiHostDefinition = _generator.GenerateSchema()["$defs"]!["ApiHostOptions"]!.AsObject();
        var modes = apiHostDefinition["properties"]!["DeploymentMode"]!["enum"]!.AsArray()
            .Select(static node => node!.GetValue<string>());

        modes.Should().Contain(["LocalWorkstation", "ProductionApi", "Worker", "Migration"]);
    }

    [Fact]
    public void GenerateSchema_IncludesSecurityMasterWorkbenchOptions()
    {
        var workbenchSchema = GetRootProperty("SecurityMasterWorkbench");
        var workbenchBranch = workbenchSchema["anyOf"]!.AsArray()
            .Select(static node => node!.AsObject())
            .First(static node => node["$ref"] is not null);

        workbenchBranch["$ref"]?.GetValue<string>().Should().Be("#/$defs/SecurityMasterWorkbenchOptions");

        var definition = _generator.GenerateSchema()["$defs"]!["SecurityMasterWorkbenchOptions"]!.AsObject();
        var properties = definition["properties"]!.AsObject();
        properties.Select(static property => property.Key).Should().Contain(
            "SourcePrecedence",
            "GoldenCopySource",
            "RequireIndependentReviewer",
            "MaxBulkResolveBatch");
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
