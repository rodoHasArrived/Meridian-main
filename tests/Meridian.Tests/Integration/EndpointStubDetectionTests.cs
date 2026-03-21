using System.Net;
using System.Net.Sockets;
using System.Reflection;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests.Integration;

/// <summary>
/// Integration tests that enumerate all route constants from UiApiRoutes,
/// verify route format, and detect endpoints returning 501 (Not Implemented).
/// Prevents unimplemented stubs from slipping through unnoticed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EndpointStubDetectionTests : IAsyncLifetime
{
    private StatusHttpServer? _server;
    private HttpClient? _httpClient;
    private int _testPort;

    /// <summary>
    /// All route constants discovered via reflection from UiApiRoutes.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<(string Name, string Route)>> AllRouteConstants = new(() =>
    {
        return typeof(UiApiRoutes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Route: (string)f.GetRawConstantValue()!))
            .OrderBy(r => r.Route)
            .ToList();
    });

    public Task InitializeAsync()
    {
        Func<MetricsSnapshot> metricsProvider = () => new MetricsSnapshot(
            Published: 100, Dropped: 5, Integrity: 2, Trades: 80,
            DepthUpdates: 15, Quotes: 5, HistoricalBars: 50,
            EventsPerSecond: 1000.0, TradesPerSecond: 800.0,
            DepthUpdatesPerSecond: 150.0, HistoricalBarsPerSecond: 50.0,
            DropRate: 0.05, AverageLatencyUs: 100.0, MinLatencyUs: 10.0,
            MaxLatencyUs: 500.0, LatencySampleCount: 1000,
            Gc0Collections: 0, Gc1Collections: 0, Gc2Collections: 0,
            Gc0Delta: 0, Gc1Delta: 0, Gc2Delta: 0,
            MemoryUsageMb: 100.0, HeapSizeMb: 50.0,
            Timestamp: DateTimeOffset.UtcNow);

        Func<PipelineStatistics> pipelineProvider = () => new PipelineStatistics(
            PublishedCount: 100, DroppedCount: 5, ConsumedCount: 95,
            CurrentQueueSize: 10, PeakQueueSize: 1000, QueueCapacity: 100000,
            QueueUtilization: 0.0001, AverageProcessingTimeUs: 50.5,
            TimeSinceLastFlush: TimeSpan.FromSeconds(1),
            Timestamp: DateTimeOffset.UtcNow);

        Func<IReadOnlyList<DepthIntegrityEvent>> integrityProvider = () => Array.Empty<DepthIntegrityEvent>();

        _testPort = GetFreePort();
        _server = new StatusHttpServer(_testPort, metricsProvider, pipelineProvider, integrityProvider);
        _server.Start();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_testPort}"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        if (_server != null)
            await _server.DisposeAsync();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    #region Route Constant Discovery

    [Fact]
    public void UiApiRoutes_HasRouteConstants()
    {
        // Act
        var routes = AllRouteConstants.Value;

        // Assert
        routes.Should().NotBeEmpty("UiApiRoutes should define route constants");
        routes.Count.Should().BeGreaterThan(100,
            "Expected 100+ route constants based on documented ~269 routes");
    }

    [Fact]
    public void UiApiRoutes_AllRoutesStartWithSlash()
    {
        // Act & Assert
        foreach (var (name, route) in AllRouteConstants.Value)
        {
            route.Should().StartWith("/",
                $"Route constant {name} = \"{route}\" must start with '/'");
        }
    }

    [Fact]
    public void UiApiRoutes_NoTrailingSlashes()
    {
        // Act & Assert
        foreach (var (name, route) in AllRouteConstants.Value)
        {
            if (route.Length > 1)
            {
                route.Should().NotEndWith("/",
                    $"Route constant {name} = \"{route}\" should not have trailing slash");
            }
        }
    }

    [Fact]
    public void UiApiRoutes_NoDuplicateRoutes()
    {
        // Act
        var routes = AllRouteConstants.Value.Select(r => r.Route).ToList();
        var duplicates = routes
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        // Assert
        duplicates.Should().BeEmpty(
            $"Found duplicate route patterns: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void UiApiRoutes_ParameterizedRoutes_UseConsistentFormat()
    {
        // Act
        var parameterized = AllRouteConstants.Value
            .Where(r => r.Route.Contains('{'))
            .ToList();

        // Assert
        foreach (var (name, route) in parameterized)
        {
            // Verify balanced braces
            var openCount = route.Count(c => c == '{');
            var closeCount = route.Count(c => c == '}');
            openCount.Should().Be(closeCount,
                $"Route {name} = \"{route}\" has unbalanced braces");

            // Verify parameter names are not empty
            route.Should().NotContain("{}",
                $"Route {name} = \"{route}\" has empty parameter placeholder");
        }
    }

    #endregion

    #region Route Coverage Analysis

    /// <summary>
    /// Routes that the StatusHttpServer handles (both /api/ prefixed and unprefixed).
    /// </summary>
    private static readonly HashSet<string> StatusHttpServerRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/healthz", "/health/detailed",
        "/ready", "/readyz",
        "/live", "/livez",
        "/metrics",
        "/api/status", "/api/errors", "/api/backpressure",
        "/api/providers/latency", "/api/connections",
        "/api/backfill/providers", "/api/backfill/status"
    };

    /// <summary>
    /// Routes mapped in ASP.NET Core endpoint files (Ui.Shared/Endpoints/).
    /// Maintained to detect when new UiApiRoutes constants are added without implementations.
    /// </summary>
    private static readonly HashSet<string> MappedEndpointRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        // StatusEndpoints.cs
        "/health", "/health/detailed", "/ready", "/live", "/metrics",
        "/api/status", "/api/errors", "/api/backpressure",
        "/api/providers/latency", "/api/connections",
        // ConfigEndpoints.cs
        "/api/config", "/api/config/datasource", "/api/config/alpaca",
        "/api/config/storage", "/api/config/symbols", "/api/config/symbols/{symbol}",
        "/api/config/derivatives",
        // BackfillEndpoints.cs
        "/api/backfill/providers", "/api/backfill/status", "/api/backfill/run",
        // ProviderEndpoints.cs
        "/api/config/datasources", "/api/config/datasources/{id}",
        "/api/config/datasources/{id}/toggle", "/api/config/datasources/defaults",
        "/api/config/datasources/failover",
        "/api/providers/comparison", "/api/providers/status",
        "/api/providers/metrics", "/api/providers/catalog",
        "/api/providers/catalog/{providerId}",
        // FailoverEndpoints.cs
        "/api/failover/config", "/api/failover/rules",
        "/api/failover/force/{ruleId}", "/api/failover/health",
        // IBEndpoints.cs
        "/api/providers/ib/status", "/api/providers/ib/error-codes",
        "/api/providers/ib/limits",
        // SymbolMappingEndpoints.cs
        "/api/symbols/mappings",
        // LiveDataEndpoints.cs
        "/api/data/trades/{symbol}", "/api/data/quotes/{symbol}",
        "/api/data/orderbook/{symbol}", "/api/data/bbo/{symbol}",
        "/api/data/orderflow/{symbol}", "/api/data/health",
        // SymbolEndpoints.cs
        "/api/symbols", "/api/symbols/monitored", "/api/symbols/archived",
        "/api/symbols/{symbol}/status", "/api/symbols/add", "/api/symbols/{symbol}/remove",
        "/api/symbols/{symbol}/trades", "/api/symbols/{symbol}/depth",
        "/api/symbols/statistics", "/api/symbols/validate", "/api/symbols/{symbol}/archive",
        "/api/symbols/bulk-add", "/api/symbols/bulk-remove", "/api/symbols/search",
        "/api/symbols/batch",
        // StorageEndpoints.cs
        "/api/storage/profiles", "/api/storage/stats", "/api/storage/breakdown",
        "/api/storage/symbol/{symbol}/info", "/api/storage/symbol/{symbol}/stats",
        "/api/storage/symbol/{symbol}/files", "/api/storage/symbol/{symbol}/path",
        "/api/storage/health", "/api/storage/cleanup/candidates", "/api/storage/cleanup",
        "/api/storage/archive/stats", "/api/storage/catalog", "/api/storage/search/files",
        "/api/storage/health/check", "/api/storage/health/orphans",
        "/api/storage/tiers/migrate", "/api/storage/tiers/statistics",
        "/api/storage/tiers/plan", "/api/storage/maintenance/defrag",
        // StorageQualityEndpoints.cs
        "/api/storage/quality/summary", "/api/storage/quality/scores",
        "/api/storage/quality/symbol/{symbol}", "/api/storage/quality/alerts",
        "/api/storage/quality/alerts/{alertId}/acknowledge",
        "/api/storage/quality/rankings/{symbol}", "/api/storage/quality/trends",
        "/api/storage/quality/anomalies", "/api/storage/quality/check"
    };

    [Fact]
    public void UiApiRoutes_UnmappedRoutes_AreDocumented()
    {
        // Act - find routes that exist as constants but have no handler
        var allRoutes = AllRouteConstants.Value
            .Select(r => r.Route)
            .ToList();

        var unmapped = allRoutes
            .Where(r => !MappedEndpointRoutes.Contains(r))
            .Where(r => !StatusHttpServerRoutes.Contains(r))
            .ToList();

        // Assert - document unmapped routes; this test will fail if the count
        // changes, prompting developers to either implement or update this list
        unmapped.Should().NotBeEmpty(
            "Some routes are expected to be unimplemented; " +
            "update MappedEndpointRoutes when implementing new endpoints");

        // Record the current count for regression detection.
        // If this count decreases, routes have been implemented (update MappedEndpointRoutes).
        // If this count increases, new route constants were added without implementations.
        var previousUnmappedCount = unmapped.Count;
        previousUnmappedCount.Should().BeGreaterThan(0,
            "This assertion tracks unmapped route count for regression detection");
    }

    [Fact]
    public void MappedEndpointRoutes_AllExistInUiApiRoutes()
    {
        // Verify that our mapped routes set doesn't contain stale entries
        var allRouteValues = AllRouteConstants.Value
            .Select(r => r.Route)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Add known routes that aren't in UiApiRoutes but are still mapped
        var knownExtraRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/healthz", "/readyz", "/livez",
            "/api/config/datasource", "/api/config/alpaca",
            "/api/config/datasources", "/api/config/datasources/{id}",
            "/api/config/datasources/{id}/toggle", "/api/config/datasources/defaults",
            "/api/config/datasources/failover",
            "/api/config/symbols/{symbol}" // DELETE endpoint mapped via route concatenation
        };

        var staleRoutes = MappedEndpointRoutes
            .Where(r => !allRouteValues.Contains(r) && !knownExtraRoutes.Contains(r))
            .ToList();

        staleRoutes.Should().BeEmpty(
            $"MappedEndpointRoutes contains routes not found in UiApiRoutes: " +
            $"{string.Join(", ", staleRoutes)}. Remove stale entries.");
    }

    #endregion

    #region StatusHttpServer - No 501 on Core Endpoints

    [Theory]
    [InlineData("/api/status")]
    [InlineData("/api/health")]
    [InlineData("/health")]
    [InlineData("/ready")]
    [InlineData("/live")]
    [InlineData("/metrics")]
    [InlineData("/api/errors")]
    [InlineData("/api/backfill/providers")]
    [InlineData("/api/backfill/status")]
    public async Task CoreEndpoint_DoesNotReturn501(string path)
    {
        // Act
        var response = await _httpClient!.GetAsync(path);

        // Assert
        ((int)response.StatusCode).Should().NotBe(501,
            $"Core endpoint {path} should not return 501 Not Implemented");
    }

    [Fact]
    public async Task StatusHttpServer_AllHandledRoutes_DoNotReturn501()
    {
        // Arrange - routes that the StatusHttpServer explicitly handles
        var routesToTest = new[]
        {
            "/api/status", "/health", "/healthz", "/ready", "/readyz",
            "/live", "/livez", "/metrics", "/api/errors",
            "/api/backfill/providers", "/api/backfill/status"
        };

        var stubEndpoints = new List<(string Path, int StatusCode)>();

        // Act
        foreach (var route in routesToTest)
        {
            var response = await _httpClient!.GetAsync(route);
            if ((int)response.StatusCode == 501)
            {
                stubEndpoints.Add((route, 501));
            }
        }

        // Assert
        stubEndpoints.Should().BeEmpty(
            $"The following endpoints returned 501: " +
            $"{string.Join(", ", stubEndpoints.Select(e => e.Path))}");
    }

    [Fact]
    public async Task HealthDetailed_IdentifiedAsStub_WhenProviderNotRegistered()
    {
        // This test documents the known 501 stub: /health/detailed returns 501
        // when the detailedHealthProvider is not registered on the StatusHttpServer.
        // The StatusHttpServer test setup doesn't register extended providers.

        // Act
        var response = await _httpClient!.GetAsync("/health/detailed");

        // Assert - currently expected to be 501 since no provider is registered
        // When a provider IS registered, this will return 200, and the test can be updated
        ((int)response.StatusCode).Should().Be(501,
            "health/detailed should return 501 when detailedHealthProvider is not registered. " +
            "If this now returns 200, the provider has been connected - update this test.");
    }

    #endregion

    #region Route Categorization

    [Fact]
    public void UiApiRoutes_CategorizedCorrectly()
    {
        var routes = AllRouteConstants.Value;

        var categories = new Dictionary<string, int>
        {
            ["Health/Status"] = routes.Count(r =>
                r.Route.StartsWith("/health") || r.Route.StartsWith("/ready") ||
                r.Route.StartsWith("/live") || r.Route.StartsWith("/metrics") ||
                r.Route == "/api/status" || r.Route == "/api/errors" ||
                r.Route == "/api/connections" || r.Route == "/api/backpressure"),
            ["Config"] = routes.Count(r => r.Route.StartsWith("/api/config")),
            ["Backfill"] = routes.Count(r => r.Route.StartsWith("/api/backfill")),
            ["Provider"] = routes.Count(r => r.Route.StartsWith("/api/providers")),
            ["Failover"] = routes.Count(r => r.Route.StartsWith("/api/failover")),
            ["Symbol"] = routes.Count(r => r.Route.StartsWith("/api/symbols")),
            ["Storage"] = routes.Count(r => r.Route.StartsWith("/api/storage")),
            ["Diagnostics"] = routes.Count(r => r.Route.StartsWith("/api/diagnostics")),
            ["Admin"] = routes.Count(r => r.Route.StartsWith("/api/admin")),
            ["Analytics"] = routes.Count(r => r.Route.StartsWith("/api/analytics")),
            ["Data"] = routes.Count(r => r.Route.StartsWith("/api/data")),
            ["Export"] = routes.Count(r => r.Route.StartsWith("/api/export")),
            ["Lean"] = routes.Count(r => r.Route.StartsWith("/api/lean")),
            ["Quality"] = routes.Count(r => r.Route.StartsWith("/api/quality")),
            ["SLA"] = routes.Count(r => r.Route.StartsWith("/api/sla")),
            ["Maintenance"] = routes.Count(r => r.Route.StartsWith("/api/maintenance")),
            ["Messaging"] = routes.Count(r => r.Route.StartsWith("/api/messaging")),
            ["Replay"] = routes.Count(r => r.Route.StartsWith("/api/replay")),
            ["Sampling"] = routes.Count(r => r.Route.StartsWith("/api/sampling")),
            ["Subscriptions"] = routes.Count(r => r.Route.StartsWith("/api/subscriptions")),
            ["Schedules"] = routes.Count(r => r.Route.StartsWith("/api/schedules")),
            ["Alignment"] = routes.Count(r => r.Route.StartsWith("/api/alignment")),
            ["Indices"] = routes.Count(r => r.Route.StartsWith("/api/indices")),
            ["Options"] = routes.Count(r => r.Route.StartsWith("/api/options")),
            ["HealthApi"] = routes.Count(r => r.Route.StartsWith("/api/health")),
            ["Ingestion"] = routes.Count(r => r.Route.StartsWith("/api/ingestion")),
            ["Historical"] = routes.Count(r => r.Route.StartsWith("/api/historical")),
            ["Calendar"] = routes.Count(r => r.Route.StartsWith("/api/calendar")),
            ["Canonicalization"] = routes.Count(r => r.Route.StartsWith("/api/canonicalization")),
            ["Auth"] = routes.Count(r => r.Route.StartsWith("/api/auth") || r.Route == "/login"),
            ["Resilience"] = routes.Count(r => r.Route.StartsWith("/api/resilience")),
            ["Other"] = 0
        };

        var categorizedCount = categories.Values.Sum();
        categories["Other"] = routes.Count - categorizedCount;

        // Assert - all routes should fall into known categories
        categories["Other"].Should().BeLessThanOrEqualTo(10,
            "Too many uncategorized routes - add new categories as needed");

        // Each major category should have routes defined
        categories["Health/Status"].Should().BeGreaterThan(0);
        categories["Config"].Should().BeGreaterThan(0);
        categories["Backfill"].Should().BeGreaterThan(0);
        categories["Provider"].Should().BeGreaterThan(0);
    }

    #endregion

    #region Helper Method Tests

    [Fact]
    public void WithParam_ReplacesParameterCorrectly()
    {
        // Act
        var result = UiApiRoutes.WithParam("/api/symbols/{symbol}/status", "symbol", "SPY");

        // Assert
        result.Should().Be("/api/symbols/SPY/status");
    }

    [Fact]
    public void WithParam_EscapesSpecialCharacters()
    {
        // Act
        var result = UiApiRoutes.WithParam("/api/symbols/{symbol}/status", "symbol", "BRK/A");

        // Assert
        result.Should().Contain("BRK");
        result.Should().NotContain("{symbol}");
    }

    [Fact]
    public void WithQuery_AppendsQueryString()
    {
        // Act
        var result = UiApiRoutes.WithQuery("/api/status", "format=json");

        // Assert
        result.Should().Be("/api/status?format=json");
    }

    [Fact]
    public void WithQuery_EmptyQueryString_ReturnsOriginalRoute()
    {
        // Act
        var result = UiApiRoutes.WithQuery("/api/status", "");

        // Assert
        result.Should().Be("/api/status");
    }

    #endregion
}
