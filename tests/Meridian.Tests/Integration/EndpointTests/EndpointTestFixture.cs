using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Models;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Shared test fixture that sets up an in-memory ASP.NET Core test server
/// with all UI endpoints mapped. Uses TestServer for zero-network-overhead
/// request/response testing.
/// </summary>
public sealed class EndpointTestFixture : IAsyncLifetime
{
    private Microsoft.AspNetCore.Builder.WebApplication? _app;
    private string? _tempConfigDir;
    private string? _originalAuthMode;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Creates an <see cref="HttpClient"/> backed by the in-memory TestServer that does NOT
    /// automatically follow redirects. Use this to inspect 3xx responses directly.
    /// The caller is responsible for disposing the returned client.
    /// </summary>
    public HttpClient CreateNoRedirectClient()
    {
        var testServer = _app!.GetTestServer();
        return new HttpClient(testServer.CreateHandler()) { BaseAddress = new Uri("http://localhost/") };
    }

    public async Task InitializeAsync()
    {
        _originalAuthMode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "optional");

        _tempConfigDir = Path.Combine(Path.GetTempPath(), $"mdc-endpoint-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempConfigDir);

        var configPath = Path.Combine(_tempConfigDir, "appsettings.json");
        File.WriteAllText(configPath, GetMinimalConfig());

        // Create the status endpoint handlers with test data
        var statusHandlers = CreateTestStatusHandlers();

        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Test";

        // Register the Ui.Shared ConfigStore wrapper (endpoints resolve this type).
        // The core ConfigStore (Application.UI.ConfigStore) is registered separately by AddMarketDataServices.
        builder.Services.AddSingleton(new Meridian.Ui.Shared.Services.ConfigStore(configPath));

        _app = builder.BuildUiHost(statusHandlers, configPath);

        // Map the dashboard HTML endpoint (not included in BuildUiHost with status handlers)
        _app.MapDashboard();

        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app != null)
            await _app.DisposeAsync();
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", _originalAuthMode);
        if (_tempConfigDir != null && Directory.Exists(_tempConfigDir))
        {
            try
            { Directory.Delete(_tempConfigDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static StatusEndpointHandlers CreateTestStatusHandlers()
    {
        Func<MetricsSnapshot> metricsProvider = () => new MetricsSnapshot(
            Published: 500, Dropped: 2, Integrity: 1, Trades: 400,
            DepthUpdates: 80, Quotes: 20, HistoricalBars: 100,
            EventsPerSecond: 500.0, TradesPerSecond: 400.0,
            DepthUpdatesPerSecond: 80.0, HistoricalBarsPerSecond: 100.0,
            DropRate: 0.004, AverageLatencyUs: 50.0, MinLatencyUs: 5.0,
            MaxLatencyUs: 200.0, LatencySampleCount: 500,
            Gc0Collections: 0, Gc1Collections: 0, Gc2Collections: 0,
            Gc0Delta: 0, Gc1Delta: 0, Gc2Delta: 0,
            MemoryUsageMb: 80.0, HeapSizeMb: 40.0,
            Timestamp: DateTimeOffset.UtcNow);

        Func<PipelineStatistics> pipelineProvider = () => new PipelineStatistics(
            PublishedCount: 500, DroppedCount: 2, ConsumedCount: 498,
            CurrentQueueSize: 5, PeakQueueSize: 100, QueueCapacity: 100000,
            QueueUtilization: 0.00005, AverageProcessingTimeUs: 25.0,
            TimeSinceLastFlush: TimeSpan.FromSeconds(1),
            Timestamp: DateTimeOffset.UtcNow);

        Func<IReadOnlyList<DepthIntegrityEvent>> integrityProvider =
            () => Array.Empty<DepthIntegrityEvent>();

        return new StatusEndpointHandlers(metricsProvider, pipelineProvider, integrityProvider);
    }

    private static string GetMinimalConfig()
    {
        var config = new
        {
            DataRoot = "data",
            Compress = false,
            DataSource = "IB",
            Symbols = new[]
            {
                new
                {
                    Symbol = "SPY",
                    SubscribeTrades = true,
                    SubscribeDepth = true,
                    DepthLevels = 10,
                    SecurityType = "STK",
                    Exchange = "SMART",
                    Currency = "USD"
                },
                new
                {
                    Symbol = "AAPL",
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
            DataSources = new
            {
                Sources = new[]
                {
                    new
                    {
                        Id = "test-alpaca",
                        Name = "Test Alpaca",
                        Provider = "Alpaca",
                        Enabled = true,
                        Type = "RealTime",
                        Priority = 10,
                        Description = "Test Alpaca provider"
                    }
                },
                DefaultRealTimeSourceId = "test-alpaca",
                EnableFailover = true,
                FailoverTimeoutSeconds = 30,
                HealthCheckIntervalSeconds = 10,
                AutoRecover = true,
                FailoverRules = new[]
                {
                    new
                    {
                        Id = "test-rule-1",
                        PrimaryProviderId = "test-alpaca",
                        BackupProviderIds = new[] { "test-backup" },
                        FailoverThreshold = 3,
                        RecoveryThreshold = 5,
                        DataQualityThreshold = 70,
                        MaxLatencyMs = 100
                    }
                }
            },
            Backfill = new
            {
                Enabled = false,
                Provider = "stooq",
                Symbols = new[] { "SPY" }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }
}
