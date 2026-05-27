using FluentAssertions;

namespace Meridian.Tests.Ui;

public sealed class ProductionStartupPolicySmokeTests
{
    [Fact]
    public void UiServer_WhenProductionEnvironmentAndInMemoryBindingsExist_FailsStartup()
    {
        using var environment = new EnvironmentVariableScope("ASPNETCORE_ENVIRONMENT", "Production");
        var configPath = Path.Combine(Path.GetTempPath(), $"meridian-prod-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, "{}");

        try
        {
            Action act = () => _ = new Meridian.UiServer(configPath, port: 0);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*non-production DI registrations*");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}
