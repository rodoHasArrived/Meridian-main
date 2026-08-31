using FluentAssertions;
using Meridian;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

// Mutates process-global environment variables (ASPNETCORE_ENVIRONMENT, MDC_AUTH_MODE) that
// ProductionServiceRegistrationPolicy.IsProductionEnvironment() reads during any concurrent
// service composition, so this class must not run in parallel with other collections (#2680).
[Collection("Sequential")]
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
                .Where(ex =>
                    ex.Message.Contains("non-production DI registrations", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("production-safe startup requires persistence-backed governance domain services", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void ApiHostOptions_FromConfiguration_UsesConfiguredRemoteBindingsAndOrigins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiHost:DeploymentMode"] = "ProductionApi",
                ["ApiHost:Urls:0"] = " https://api.meridian.example.com/ ",
                ["ApiHost:Urls:1"] = "https://api.meridian.example.com",
                ["ApiHost:AllowedOrigins:0"] = "https://workstation.meridian.example.com/",
                ["ApiHost:ServeWorkstationAssets"] = "false"
            })
            .Build();

        var options = ApiHostOptions.FromConfiguration(configuration, port: 8080);

        options.DeploymentMode.Should().Be(MeridianApiDeploymentMode.ProductionApi);
        options.Urls.Should().Equal("https://api.meridian.example.com");
        options.AllowedOrigins.Should().Equal("https://workstation.meridian.example.com");
        options.ServeWorkstationAssets.Should().BeFalse();
    }

    [Fact]
    public void ApiHostOptions_FromConfiguration_DefaultsToLocalWorkstationBinding()
    {
        var options = ApiHostOptions.FromConfiguration(new ConfigurationBuilder().Build(), port: 8181);

        options.DeploymentMode.Should().Be(MeridianApiDeploymentMode.LocalWorkstation);
        options.Urls.Should().Equal("http://localhost:8181");
        options.AllowedOrigins.Should().BeEmpty();
        options.ServeWorkstationAssets.Should().BeTrue();
    }

    [Fact]
    public void ValidateAuthenticationTransportSecurity_WhenProductionApiUsesHttp_RejectsStartup()
    {
        using var authMode = new EnvironmentVariableScope("MDC_AUTH_MODE", "required");
        var options = new ApiHostOptions
        {
            DeploymentMode = MeridianApiDeploymentMode.ProductionApi,
            Urls = ["http://0.0.0.0:8080"]
        };

        Action act = () => UiServer.ValidateAuthenticationTransportSecurity(new TestHostEnvironment("Production"), options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no HTTPS binding is configured*");
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void ValidateAuthenticationTransportSecurity_WhenLocalWorkstationUsesLoopbackHttp_AllowsStartup(
        string url)
    {
        using var authMode = new EnvironmentVariableScope("MDC_AUTH_MODE", "required");
        var options = new ApiHostOptions
        {
            DeploymentMode = MeridianApiDeploymentMode.LocalWorkstation,
            Urls = [url]
        };

        Action act = () => UiServer.ValidateAuthenticationTransportSecurity(new TestHostEnvironment("Production"), options);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080")]
    [InlineData("http://192.168.1.10:8080")]
    public void ValidateAuthenticationTransportSecurity_WhenLocalWorkstationUsesNonLoopbackHttp_RejectsStartup(
        string url)
    {
        using var authMode = new EnvironmentVariableScope("MDC_AUTH_MODE", "required");
        var options = new ApiHostOptions
        {
            DeploymentMode = MeridianApiDeploymentMode.LocalWorkstation,
            Urls = [url]
        };

        Action act = () => UiServer.ValidateAuthenticationTransportSecurity(new TestHostEnvironment("Production"), options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no HTTPS binding is configured*");
    }

    [Fact]
    public void ValidateAuthenticationTransportSecurity_WhenReverseProxyInsecureTransportIsExplicit_AllowsStartup()
    {
        using var authMode = new EnvironmentVariableScope("MDC_AUTH_MODE", "required");
        var options = new ApiHostOptions
        {
            DeploymentMode = MeridianApiDeploymentMode.ProductionApi,
            Urls = ["http://0.0.0.0:8080"],
            AllowInsecureTransportForReverseProxy = true
        };

        Action act = () => UiServer.ValidateAuthenticationTransportSecurity(new TestHostEnvironment("Production"), options);

        act.Should().NotThrow();
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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Meridian.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
