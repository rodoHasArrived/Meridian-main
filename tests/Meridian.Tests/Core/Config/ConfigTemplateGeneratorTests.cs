using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

/// <summary>
/// The generated templates are written to disk verbatim — nothing expands shell placeholders
/// before deserialization — so a "${VAR:-default}" literal in a field the validators enforce
/// produces a file that fails to load rather than one that falls back to the default.
/// </summary>
[Collection("Sequential")]
public sealed class ConfigTemplateGeneratorTests
{
    [Fact]
    public void GenerateDocker_ProducesConfigThatLoadsAndValidates()
    {
        var template = new ConfigTemplateGenerator().GenerateDocker();

        // AppConfigJsonOptions.Read is the same options instance ConfigStore and the startup
        // helpers use, so this exercises the real load path rather than a lenient one.
        var config = JsonSerializer.Deserialize<AppConfig>(template.Json, AppConfigJsonOptions.Read);

        config.Should().NotBeNull();

        // FieldValidationStage is the repo's own seam over AppConfigValidator, so this asserts
        // the same field rules the running host applies.
        var findings = new FieldValidationStage().Validate(config!);

        findings.Where(f => f.IsError).Should().BeEmpty(
            "the Docker template must load as written; errors: {0}",
            string.Join("; ", findings.Select(f => $"{f.Property}: {f.Message}")));
    }

    [Fact]
    public void GenerateDocker_EmitsParseableLiteralsForValidatedFields()
    {
        var template = new ConfigTemplateGenerator().GenerateDocker();

        var config = JsonSerializer.Deserialize<AppConfig>(template.Json, AppConfigJsonOptions.Read);

        // DataSourceKindConverter fails closed on an unknown string and SymbolConfigValidator
        // matches ^[A-Z0-9\-\.\/]+$, so neither field can carry a placeholder.
        config!.DataSource.Should().Be(DataSourceKind.Synthetic);
        config.Symbols.Should().NotBeNull();
        config.Symbols!.Should().ContainSingle().Which.Symbol.Should().Be("SPY");
    }

    [Fact]
    public void GenerateDocker_AdvertisedVariablesActuallyOverrideTheGeneratedConfig()
    {
        // A variable the template advertises but the override layer does not apply is
        // capability that silently does nothing — MDC_SYMBOLS was exactly that, which is why
        // its placeholder could not be "repaired" by setting the variable it named.
        var template = new ConfigTemplateGenerator().GenerateDocker();
        var config = JsonSerializer.Deserialize<AppConfig>(template.Json, AppConfigJsonOptions.Read);

        template.EnvironmentVariables.Should().NotBeNull();
        template.EnvironmentVariables!.Should().ContainKey("MDC_DATASOURCE");
        template.EnvironmentVariables.Should().ContainKey("MDC_SYMBOLS");

        var original = new Dictionary<string, string?>
        {
            ["MDC_DATASOURCE"] = Environment.GetEnvironmentVariable("MDC_DATASOURCE"),
            ["MDC_SYMBOLS"] = Environment.GetEnvironmentVariable("MDC_SYMBOLS")
        };

        try
        {
            Environment.SetEnvironmentVariable("MDC_DATASOURCE", "Polygon");
            Environment.SetEnvironmentVariable("MDC_SYMBOLS", "AAPL,MSFT");

            var overridden = new ConfigEnvironmentOverride().ApplyOverrides(config!);

            overridden.DataSource.Should().Be(DataSourceKind.Polygon);
            overridden.Symbols!.Select(s => s.Symbol).Should().Equal("AAPL", "MSFT");
        }
        finally
        {
            foreach (var (name, value) in original)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
