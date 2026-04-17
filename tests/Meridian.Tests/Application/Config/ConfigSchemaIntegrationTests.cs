using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ConfigSchemaIntegrationTests
{
    [Fact]
    public void CheckedInSchema_MatchesCurrentGeneratorOutput()
    {
        var repoRoot = ResolveRepoRoot();
        var schemaPath = Path.Combine(repoRoot, "config", "appsettings.schema.json");

        File.Exists(schemaPath).Should().BeTrue("the checked-in config schema should be present for IDE validation");

        var generator = new ConfigJsonSchemaGenerator();
        var generated = NormalizeJson(generator.GenerateSchemaJson());
        var checkedIn = NormalizeJson(File.ReadAllText(schemaPath));

        generated.Should().Be(checkedIn);
    }

    [Fact]
    public void SampleConfig_ReferencesCheckedInSchema()
    {
        var repoRoot = ResolveRepoRoot();
        var samplePath = Path.Combine(repoRoot, "config", "appsettings.sample.json");

        File.Exists(samplePath).Should().BeTrue();

        var sampleJson = File.ReadAllText(samplePath);
        sampleJson.Should().Contain("\"$schema\": \"./appsettings.schema.json\"");
    }

    private static string ResolveRepoRoot()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "Meridian.sln")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName ?? AppContext.BaseDirectory;
    }

    private static string NormalizeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(SortElement(doc.RootElement));
    }

    private static object? SortElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .Where(property => !string.Equals(property.Name, "default", StringComparison.Ordinal))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToDictionary(
                    property => property.Name,
                    property => SortElement(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(SortElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
}
