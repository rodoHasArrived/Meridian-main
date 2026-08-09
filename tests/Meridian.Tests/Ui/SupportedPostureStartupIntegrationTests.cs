using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup;
using Meridian.Contracts.Lifecycle;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

/// <summary>
/// PRD-000 / ADR-019 startup evidence for the real host composition: the supported
/// local-workstation posture composes, starts, and serves with the final-graph guard running
/// first, while the experimental ProductionApi posture fails closed with a diagnostic naming
/// the prohibited bindings.
/// </summary>
[Collection("Sequential")]
public sealed class SupportedPostureStartupIntegrationTests
{
    [Fact]
    public async Task UiServer_LocalWorkstationPosture_StartsServesAndFormatsEmptyApiFailures()
    {
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);

        try
        {
            await using var server = new UiServer(configPath, port: 0);
            using var startTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var app = GetServerApp(server);
            app.MapGet(
                "/api/problem-details/empty-unauthorized",
                () => Results.Unauthorized());
            app.MapGet(
                "/api/problem-details/empty-service-unavailable",
                () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
            app.MapGet(
                "/problem-details/empty-unauthorized",
                () => Results.Unauthorized());

            await server.StartAsync(startTimeout.Token);

            app.Services.GetServices<IHostedService>().First()
                .Should().BeOfType<ProductionRegistrationGuardService>(
                    "the ADR-019 final-graph guard must be the first hosted service to start");

            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var health = await client.GetAsync("/healthz", startTimeout.Token);
            health.IsSuccessStatusCode.Should().BeTrue("a started supported-posture host must serve liveness");
            var readiness = await app.Services
                .GetRequiredService<IRuntimeReadinessService>()
                .EvaluateAsync(startTimeout.Token);
            var reportingDurability = readiness.Checks
                .Single(check => check.Id == "reporting-durability");
            reportingDurability.Requirement.Should().Be(LifecycleCheckRequirement.Degradable);
            reportingDurability.Status.Should().Be(LifecycleCheckStatus.Degraded);
            reportingDurability.Message.Should().Contain("unavailable durable components");

            using var unauthorized = await client.GetAsync(
                "/api/problem-details/empty-unauthorized",
                startTimeout.Token);
            await AssertProblemDetailsAsync(
                unauthorized,
                HttpStatusCode.Unauthorized,
                "https://meridian.io/errors/unauthorized",
                "Authentication Required");

            using var serviceUnavailable = await client.GetAsync(
                "/api/problem-details/empty-service-unavailable",
                startTimeout.Token);
            await AssertProblemDetailsAsync(
                serviceUnavailable,
                HttpStatusCode.ServiceUnavailable,
                "https://meridian.io/errors/service-unavailable",
                "Service Unavailable");

            using var browserRoute = await client.GetAsync(
                "/problem-details/empty-unauthorized",
                startTimeout.Token);
            browserRoute.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            browserRoute.Content.Headers.ContentType?.MediaType.Should().NotBe(
                "application/problem+json",
                "the status-code fallback is intentionally limited to /api");

            await server.StopAsync(startTimeout.Token);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    /// <summary>
    /// PRD-019 probe/scrape evidence against the real host pipeline: with a governed account
    /// configured (authenticated posture), the compose healthcheck's unauthenticated
    /// <c>GET /health</c> and a Prometheus scrape's unauthenticated <c>GET /metrics</c> are
    /// served by <see cref="Meridian.Ui.Shared.Endpoints.MonitoringEndpointExemptions"/>,
    /// while the rest of the workstation still enforces session authentication.
    /// </summary>
    [Fact]
    public async Task UiServer_AuthenticatedPosture_ServesProbesAndScrapeWithoutASession()
    {
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);
        SeedGovernedUserAccount(root);

        try
        {
            await using var server = new UiServer(configPath, port: 0);
            using var startTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await server.StartAsync(startTimeout.Token);

            var app = GetServerApp(server);
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };

            // Controls: the seeded account puts the host in an authenticated posture, so a
            // served probe below proves the monitoring exemption rather than a disabled gate.
            using var apiControl = await client.GetAsync("/api/status", startTimeout.Token);
            apiControl.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                "an unauthenticated API request must be rejected while auth is configured");
            using var browserControl = await client.GetAsync("/", startTimeout.Token);
            browserControl.StatusCode.Should().Be(
                HttpStatusCode.Redirect,
                "an unauthenticated browser request must be sent to the login page while auth is configured");
            browserControl.Headers.Location!.ToString().Should().StartWith("/login");

            // The compose healthcheck path: unauthenticated GET /health sees health truth
            // (200 healthy / 503 unhealthy) instead of a login redirect masking either.
            using var health = await client.GetAsync("/health", startTimeout.Token);
            health.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
            health.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

            // The Prometheus scrape path: unauthenticated GET /metrics returns exposition
            // text containing registry series, not login page HTML.
            using var metrics = await client.GetAsync("/metrics", startTimeout.Token);
            metrics.StatusCode.Should().Be(HttpStatusCode.OK);
            metrics.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
            var exposition = await metrics.Content.ReadAsStringAsync(startTimeout.Token);
            exposition.Should().Contain("mdc_published");

            using var liveness = await client.GetAsync("/healthz", startTimeout.Token);
            liveness.IsSuccessStatusCode.Should().BeTrue();

            await server.StopAsync(startTimeout.Token);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task UiServer_StartAsync_CompletesReportingMigrationBeforeHostedServiceConstruction()
    {
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);
        var migration = new RecordingReportingMigrationStartup();
        var hostedServiceObservedReady = false;

        try
        {
            await using var server = new UiServer(
                configPath,
                port: 0,
                lifecycle: new RecordingLifecycleCoordinator(),
                ownsLifecycle: true,
                apiHostOptions: null,
                configureServices: services =>
                {
                    services.AddSingleton<IReportingMigrationStartup>(migration);
                    services.AddSingleton<IHostedService>(_ =>
                    {
                        hostedServiceObservedReady = migration.IsReady;
                        return new StopAndDisposeProbeHostedService();
                    });
                });
            using var startTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            await server.StartAsync(startTimeout.Token);

            migration.CallCount.Should().Be(1);
            hostedServiceObservedReady.Should().BeTrue(
                "reporting schema readiness must precede construction of schedule and distribution hosted services");
            await server.StopAsync(startTimeout.Token);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task UiServer_DisposeAsync_WaitsForInProgressStartAndSharesOneCleanup()
    {
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);
        var lifecycle = new RecordingLifecycleCoordinator();
        var server = new UiServer(
            configPath,
            port: 0,
            lifecycle: lifecycle,
            ownsLifecycle: true,
            apiHostOptions: null);
        var readinessGate = GetServerSemaphore(server, "_databaseReadinessGate");
        var lifecycleOperationGate = GetServerSemaphore(server, "_lifecycleOperationGate");
        var readinessGateHeld = false;
        var disposalCompleted = false;

        try
        {
            using var startCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await readinessGate.WaitAsync(startCancellation.Token);
            readinessGateHeld = true;

            var startTask = server.StartAsync(startCancellation.Token);
            lifecycleOperationGate.CurrentCount.Should().Be(
                0,
                "StartAsync must own the lifecycle operation gate before waiting for database readiness");

            var firstDispose = server.DisposeAsync().AsTask();
            var secondDispose = server.DisposeAsync().AsTask();

            secondDispose.Should().BeSameAs(
                firstDispose,
                "all concurrent disposal callers must await the same cleanup operation");
            firstDispose.IsCompleted.Should().BeFalse(
                "disposal must wait until the in-progress StartAsync releases host resources");

            startCancellation.Cancel();
            Func<Task> awaitStart = async () => await startTask;
            await awaitStart.Should().ThrowAsync<OperationCanceledException>();

            await firstDispose.WaitAsync(TimeSpan.FromSeconds(30));
            disposalCompleted = true;
            readinessGateHeld = false;

            lifecycle.DisposeCount.Should().Be(
                1,
                "the shared disposal operation owns the lifecycle coordinator exactly once");
        }
        finally
        {
            if (!disposalCompleted)
            {
                if (readinessGateHeld)
                    readinessGate.Release();

                await server.DisposeAsync();
            }

            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task UiServer_DisposeAsync_AfterSuccessfulStart_StopsHostedServicesExactlyOnce()
    {
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);
        var lifecycle = new RecordingLifecycleCoordinator();
        var probe = new StopAndDisposeProbeHostedService();
        var server = new UiServer(
            configPath,
            port: 0,
            lifecycle: lifecycle,
            ownsLifecycle: true,
            apiHostOptions: null,
            configureServices: services =>
            {
                services.AddSingleton(probe);
                services.AddSingleton<IHostedService>(
                    serviceProvider =>
                        serviceProvider.GetRequiredService<StopAndDisposeProbeHostedService>());
            });
        var disposed = false;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await server.StartAsync(timeout.Token);

            await server.DisposeAsync().AsTask().WaitAsync(timeout.Token);
            disposed = true;

            probe.StartCount.Should().Be(1);
            probe.StopCount.Should().Be(
                1,
                "disposing a started UiServer must enter the Generic Host stop lifecycle");
            probe.DisposeCount.Should().Be(1);
            lifecycle.DisposeCount.Should().Be(1);
        }
        finally
        {
            if (!disposed)
                await server.DisposeAsync();

            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task UiServer_StartAsync_ReadinessFailureStopsAppPreservesFailureAndRejectsRetry()
    {
        using var environment = UiServerDevelopmentEnvironmentScope.Enable();
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);
        var lifecycle = new RecordingLifecycleCoordinator();
        var readinessFailure = new InvalidOperationException("injected readiness failure");
        var server = new UiServer(
            configPath,
            port: 0,
            lifecycle: lifecycle,
            ownsLifecycle: true,
            apiHostOptions: null,
            readinessEvaluator: _ => Task.FromException(readinessFailure));

        try
        {
            using var startTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var app = GetServerApp(server);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.StartAsync(startTimeout.Token));

            thrown.Should().BeSameAs(
                readinessFailure,
                "cleanup must not replace the original readiness failure");
            app.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopped.IsCancellationRequested.Should().BeTrue(
                    "a listener started before readiness failed must be stopped before StartAsync returns");

            var retry = await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.StartAsync(startTimeout.Token));
            retry.Message.Should().Contain(
                "cannot retry startup",
                "a partially started Generic Host cannot be safely restarted");
        }
        finally
        {
            await server.DisposeAsync();
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

    private static SemaphoreSlim GetServerSemaphore(UiServer server, string fieldName)
    {
        var field = typeof(UiServer).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var gate = field!.GetValue(server) as SemaphoreSlim;
        gate.Should().NotBeNull();
        return gate!;
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedType,
        string expectedTitle)
    {
        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;
        problem.GetProperty("type").GetString().Should().Be(expectedType);
        problem.GetProperty("title").GetString().Should().Be(expectedTitle);
        problem.GetProperty("status").GetInt32().Should().Be((int)expectedStatus);
        problem.GetProperty("instance").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Seeds the governed account store the host resolves from its data root. Stored accounts
    /// take priority over the MDC_* environment variables, so this keeps the authenticated
    /// posture immune to auth env-var mutation by concurrently running test collections.
    /// </summary>
    private static void SeedGovernedUserAccount(string root)
    {
        var governanceDir = Path.Combine(root, "data", "governance");
        Directory.CreateDirectory(governanceDir);
        File.WriteAllText(
            Path.Combine(governanceDir, "user-accounts.json"),
            """
            {
              "accounts": [
                {
                  "username": "posture-admin",
                  "passwordHash": "pbkdf2-sha256$210000$oOQU8zfLm/Pzwrl8VZlatQ==$ePPcBmch9qAIfhbablmoBT/tKPGb/TKmFBHlFWKV1uU=",
                  "role": "Admin"
                }
              ]
            }
            """);
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

    private sealed class RecordingLifecycleCoordinator : IApplicationLifecycleCoordinator
    {
        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

        public bool IsShutdownRequested => false;

        public string? ShutdownReason => null;

        public CancellationToken ShutdownToken => CancellationToken.None;

        public string? LocalShutdownToken => null;

        public int DisposeCount { get; private set; }

        public Task RequestShutdownAsync(
            string reason,
            string? detail = null,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public void Dispose() => DisposeCount++;
    }

    private sealed class StopAndDisposeProbeHostedService : IHostedService, IAsyncDisposable
    {
        private int _startCount;
        private int _stopCount;
        private int _disposeCount;

        public int StartCount => Volatile.Read(ref _startCount);
        public int StopCount => Volatile.Read(ref _stopCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCount);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCount);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingReportingMigrationStartup : IReportingMigrationStartup
    {
        private int _callCount;
        private int _ready;

        public int CallCount => Volatile.Read(ref _callCount);
        public bool IsReady => Volatile.Read(ref _ready) == 1;

        public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            Volatile.Write(ref _ready, 1);
            return Task.CompletedTask;
        }
    }
}
