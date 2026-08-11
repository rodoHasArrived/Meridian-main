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

        // Pinning the advertised set means adding a sixth variable fails here until it is proven
        // to apply below, rather than silently joining the list unwired.
        var probes = new Dictionary<string, string>
        {
            ["MDC_DATASOURCE"] = "Polygon",
            ["MDC_SYMBOLS"] = "AAPL,MSFT",
            ["MDC_ALPACA_KEY_ID"] = "probe-key-id",
            ["MDC_ALPACA_SECRET_KEY"] = "probe-secret-key",
            ["MDC_ALPACA_FEED"] = "sip"
        };

        template.EnvironmentVariables!.Keys
            .Where(key => key.StartsWith("MDC_", StringComparison.Ordinal))
            .Should().BeEquivalentTo(probes.Keys);

        var original = probes.Keys.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable);

        try
        {
            foreach (var (name, value) in probes)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            var overridden = new ConfigEnvironmentOverride().ApplyOverrides(config!);

            overridden.DataSource.Should().Be(DataSourceKind.Polygon);
            overridden.Symbols!.Select(s => s.Symbol).Should().Equal("AAPL", "MSFT");
            overridden.Alpaca.Should().NotBeNull();
            overridden.Alpaca!.KeyId.Should().Be("probe-key-id");
            overridden.Alpaca.SecretKey.Should().Be("probe-secret-key");
            overridden.Alpaca.Feed.Should().Be("sip");
        }
        finally
        {
            foreach (var (name, value) in original)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    [Fact]
    public void GenerateDocker_SwitchedToAlpaca_StillValidatesOnceCredentialsAreSupplied()
    {
        // The Alpaca block is not exercised by the default Synthetic template because
        // AlpacaOptionsValidator runs only When(DataSource == Alpaca). That is exactly how the
        // unparseable "${ALPACA_FEED:-iex}" survived: it became reachable only after an operator
        // switched providers, which is the one moment the template is supposed to help.
        var template = new ConfigTemplateGenerator().GenerateDocker();
        var config = JsonSerializer.Deserialize<AppConfig>(template.Json, AppConfigJsonOptions.Read);

        var switched = config! with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = config.Alpaca! with { KeyId = "a-real-key-id", SecretKey = "a-real-secret-key" }
        };

        var findings = new FieldValidationStage().Validate(switched);

        findings.Where(f => f.IsError).Should().BeEmpty(
            "switching the generated template to Alpaca and supplying credentials must leave a "
            + "loadable config; errors: {0}",
            string.Join("; ", findings.Select(f => $"{f.Property}: {f.Message}")));
    }
}
