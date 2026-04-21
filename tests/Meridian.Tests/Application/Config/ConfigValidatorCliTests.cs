using FluentAssertions;
using Meridian.Application.Config;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ConfigValidatorCliTests
{
    [Fact]
    public void Validate_WithAlpacaCredentialsFromEnvironment_ReturnsValid()
    {
        const string configJson = """
            {
              "dataRoot": "data",
              "dataSource": "Alpaca",
              "alpaca": null,
              "symbols": [
                {
                  "symbol": "SPY",
                  "subscribeTrades": true
                }
              ]
            }
            """;

        using var keyScope = new EnvironmentVariableScope("ALPACA_KEY_ID", "AKXXXXXXXXXXXXXXXX");
        using var secretScope = new EnvironmentVariableScope("ALPACA_SECRET_KEY", "secretxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

        var sut = new ConfigValidatorCli();

        var result = sut.ValidateJson(configJson);

        result.IsValid.Should().BeTrue();
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
