using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The API host's diagnostics endpoints are wired. Before this, <c>/health/detailed</c> answered
/// 501 "Detailed health check not configured" and <c>/api/errors</c> answered "Error buffer not
/// configured", and the only runtime disk-space probe (<c>SystemHealthChecker</c>) was never
/// constructed outside tests, so a filling data root had no signal until writes failed.
/// </summary>
[Trait("Category", "Unit")]
[Collection("Sequential")]
public sealed class UiServerDiagnosticsWiringTests : IDisposable
{
    private readonly TestArtifactDirectory _artifacts = TestArtifactDirectory.Create(nameof(UiServerDiagnosticsWiringTests));

    public void Dispose() => _artifacts.Dispose();

    [Fact]
    public async Task TheHost_ServesDetailedHealthWithADiskCheck_RecentErrors_AndDiskSpaceMetrics()
    {
        var dataRoot = Path.Combine(_artifacts.RootPath, "data");
        Directory.CreateDirectory(dataRoot);
        var configPath = WriteMinimalConfig(dataRoot);
        using var environment = DevelopmentEnvironmentScope.Enable();

        await using var server = new UiServer(configPath, port: 0);
        var handlers = GetServerApp(server).Services.GetRequiredService<StatusEndpointHandlers>();

        // The checker samples on its own timer (first sample immediately), so allow it a moment.
        DetailedHealthReport? report = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var (current, error) = await handlers.GetDetailedHealthAsync();
            error.Should().BeNull("the detailed health check is registered in the host");
            report = current;
            if (report!.Checks.Any(static check => check.Name.StartsWith("disk_", StringComparison.Ordinal)))
                break;
            await Task.Delay(100);
        }

        report!.Checks.Should().Contain(check => check.Name.StartsWith("disk_", StringComparison.Ordinal),
            "the data-root volume is monitored for free space");
        report.Checks.Should().Contain(check => check.Name == "pipeline_queue");

        handlers.GetErrors().Message.Should().NotBe("Error buffer not configured");

        var exposition = await handlers.GetPrometheusMetricsAsync();
        exposition.Should().Contain($"mdc_disk_free_bytes{{path=\"{dataRoot}\"}}",
            "free space on the data-root volume is exported for the disk alerts");
    }

    private string WriteMinimalConfig(string dataRoot)
    {
        var configPath = Path.Combine(_artifacts.RootPath, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            DataRoot = dataRoot,
            Compress = false,
            DataSource = "Synthetic",
            ApiHost = new { Urls = new[] { "http://127.0.0.1:0" }, ServeWorkstationAssets = false },
            Storage = new { NamingConvention = "BySymbol", DatePartition = "Daily", IncludeProvider = false },
            Backfill = new { Enabled = false, Provider = "stooq", Symbols = new[] { "SPY" } }
        }));
        return configPath;
    }

    private static WebApplication GetServerApp(UiServer server)
    {
        var field = typeof(UiServer).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(server).Should().BeOfType<WebApplication>().Subject;
    }

    // The environment the other UiServer composition tests run under: in-memory governance, Development.
    private sealed class DevelopmentEnvironmentScope : IDisposable
    {
        private static readonly string[] Names = ["MERIDIAN_USE_INMEMORY_GOVERNANCE", "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT"];
        private readonly string?[] _original = Names.Select(Environment.GetEnvironmentVariable).ToArray();

        private DevelopmentEnvironmentScope()
        {
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
        }

        public static DevelopmentEnvironmentScope Enable() => new();

        public void Dispose()
        {
            for (var i = 0; i < Names.Length; i++)
            {
                Environment.SetEnvironmentVariable(Names[i], _original[i]);
            }
        }
    }
}
