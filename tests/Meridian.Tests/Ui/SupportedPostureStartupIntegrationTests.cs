using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

/// <summary>
/// PRD-000 / ADR-019 startup evidence for the real host composition: the supported
/// local-workstation posture composes, starts, and serves with the final-graph guard running
/// first, while the experimental ProductionApi posture fails closed with a diagnostic naming
/// the prohibited bindings.
/// </summary>
public sealed class SupportedPostureStartupIntegrationTests
{
    [Fact]
    public async Task UiServer_LocalWorkstationPosture_StartsServesAndRunsGuardFirst()
    {
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);

        try
        {
            await using var server = new UiServer(configPath, port: 0);
            using var startTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            await server.StartAsync(startTimeout.Token);

            var app = GetServerApp(server);
            app.Services.GetServices<IHostedService>().First()
                .Should().BeOfType<ProductionRegistrationGuardService>(
                    "the ADR-019 final-graph guard must be the first hosted service to start");

            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var health = await client.GetAsync("/healthz", startTimeout.Token);
            health.IsSuccessStatusCode.Should().BeTrue("a started supported-posture host must serve liveness");

            await server.StopAsync(startTimeout.Token);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public void UiServer_ProductionApiPosture_FailsClosedNamingProhibitedBindings()
    {
        // The development environment scope proves the declared posture alone forces the
        // rejection; no production environment variable is involved.
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);

        try
        {
            var options = new ApiHostOptions
            {
                DeploymentMode = MeridianApiDeploymentMode.ProductionApi,
                Urls = ["https://127.0.0.1:0"],
                ServeWorkstationAssets = false
            };

            Action act = () => _ = new UiServer(configPath, port: 0, apiHostOptions: options);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*rejected non-production DI registrations*")
                .WithMessage("*InMemory*");
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    private static WebApplication GetServerApp(UiServer server)
    {
        var field = typeof(UiServer).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var app = field!.GetValue(server) as WebApplication;
        app.Should().NotBeNull();
        return app!;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "posture-startup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CleanupTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            try
            { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static string WriteMinimalConfig(string root)
    {
        var config = new
        {
            DataRoot = Path.Combine(root, "data"),
            Compress = false,
            DataSource = "Synthetic",
            ApiHost = new
            {
                // Kestrel only supports dynamic ports on explicit loopback addresses, and the
                // workstation bundle assets are not present in the test bin directory.
                Urls = new[] { "http://127.0.0.1:0" },
                ServeWorkstationAssets = false
            },
            Symbols = new[]
            {
                new
                {
                    Symbol = "SPY",
                    SubscribeTrades = true,
                    SubscribeDepth = false,
                    DepthLevels = 10,
                    SecurityType = "STK",
                    Exchange = "SMART",
                    Currency = "USD"
                }
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                IncludeProvider = false
            },
            Backfill = new
            {
                Enabled = false,
                Provider = "stooq",
                Symbols = new[] { "SPY" }
            }
        };

        var configPath = Path.Combine(root, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return configPath;
    }

    private sealed class UiServerDevelopmentEnvironmentScope : IDisposable
    {
        private readonly string? _originalUseInMemoryGovernance;
        private readonly string? _originalAspNetCoreEnvironment;
        private readonly string? _originalDotnetEnvironment;

        private UiServerDevelopmentEnvironmentScope()
        {
            _originalUseInMemoryGovernance = Environment.GetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE");
            _originalAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            _originalDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
        }

        public static UiServerDevelopmentEnvironmentScope Enable() => new();

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", _originalUseInMemoryGovernance);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspNetCoreEnvironment);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnvironment);
        }
    }
}
