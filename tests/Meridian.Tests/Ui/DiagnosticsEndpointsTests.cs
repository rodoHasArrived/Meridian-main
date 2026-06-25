using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Contracts.Coordination;
using Meridian.Core.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Performance;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Meridian.Platform.Diagnostics;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using UiConfigStore = Meridian.Ui.Shared.Services.ConfigStore;
using Meridian.Contracts.Monitoring;

namespace Meridian.Tests.Ui;

public sealed class DiagnosticsEndpointsTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-diagnostics-endpoints-{Guid.NewGuid():N}");

    [Fact]
    public async Task DiagnosticsMetrics_IncludesPipelineAndMarketDataSnapshots()
    {
        var metrics = new TestEventMetrics();

        await using var pipeline = new EventPipeline(
            new NoOpStorageSink(),
            capacity: 8,
            fullMode: BoundedChannelFullMode.DropWrite,
            enablePeriodicFlush: false,
            metrics: metrics);
        pipeline.TryPublish(CreateTradeEvent("SPY")).Should().BeTrue();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IEventMetrics>(metrics);
            services.AddSingleton(pipeline);
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsMetrics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("eventPipeline").GetProperty("queueName").GetString().Should().Be("market-data");
        root.GetProperty("eventPipeline").GetProperty("queueCapacity").GetInt32().Should().Be(8);
        root.GetProperty("eventPipeline").GetProperty("published").GetInt64().Should().Be(1);
        root.GetProperty("eventPipeline").GetProperty("recovered").GetInt64().Should().Be(0);
        root.GetProperty("eventPipeline").GetProperty("rejected").GetInt64().Should().Be(0);
        root.GetProperty("eventPipeline").GetProperty("deduplicated").GetInt64().Should().Be(0);
        root.GetProperty("eventPipeline").GetProperty("isWalEnabled").GetBoolean().Should().BeFalse();
        root.GetProperty("eventPipeline").GetProperty("isValidationEnabled").GetBoolean().Should().BeFalse();
        root.GetProperty("eventPipeline").GetProperty("isDeduplicationEnabled").GetBoolean().Should().BeFalse();
        var pipelineHealth = root.GetProperty("eventPipelineHealth");
        pipelineHealth.GetProperty("operationName").GetString().Should().Be("pipeline.health.summary");
        pipelineHealth.GetProperty("queueName").GetString().Should().Be("market-data");
        pipelineHealth.GetProperty("status").GetString().Should().BeOneOf("Healthy", "Warning");
        pipelineHealth.GetProperty("recoveryAction").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("marketDataMetrics").GetProperty("published").GetInt64().Should().Be(1);
        root.GetProperty("marketDataMetrics").GetProperty("trades").GetInt64().Should().Be(1);
        var runtime = root.GetProperty("runtime");
        runtime.GetProperty("operationName").GetString().Should().Be("diagnostics.metrics.snapshot");
        runtime.GetProperty("componentName").GetString().Should().Be("DiagnosticsEndpoints");
        runtime.GetProperty("processId").GetInt32().Should().Be(Environment.ProcessId);
        runtime.GetProperty("threadCount").GetInt32().Should().BeGreaterThan(0);
        runtime.GetProperty("workingSetBytes").GetInt64().Should().BeGreaterThan(0);
        runtime.GetProperty("managedHeapBytes").GetInt64().Should().BeGreaterThan(0);
        runtime.GetProperty("processorCount").GetInt32().Should().BeGreaterThan(0);
        var payload = root.GetRawText().ToLowerInvariant();
        payload.Should().NotContain("secret");
        payload.Should().NotContain("accountnumber");
    }

    [Fact]
    public async Task DiagnosticsMetrics_WithoutMetricsServices_ReturnsNullMetricBlocks()
    {
        await using var app = await CreateAppAsync(_ => { });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsMetrics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("eventPipeline").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("eventPipelineHealth").GetProperty("available").GetBoolean().Should().BeFalse();
        root.GetProperty("eventPipelineHealth").GetProperty("failureReason").GetString().Should().Be("pipeline.statistics.unavailable");
        root.GetProperty("marketDataMetrics").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("runtime").GetProperty("operationName").GetString().Should().Be("diagnostics.metrics.snapshot");
        root.GetProperty("processMemoryBytes").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DiagnosticsMetrics_IncludesHotPathSnapshot_WhenDualPathPipelineIsRegistered()
    {
        var metrics = new TestEventMetrics();

        await using var pipeline = new EventPipeline(
            new NoOpStorageSink(),
            capacity: 16,
            fullMode: BoundedChannelFullMode.Wait,
            enablePeriodicFlush: false,
            metrics: metrics);
        await using var dualPath = new DualPathEventPipeline(
            pipeline,
            new SymbolTable(),
            ringBufferCapacity: 8,
            batchDrainSize: 4);

        dualPath.TryPublish(CreateTradeEvent("SPY")).Should().BeTrue();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IEventMetrics>(metrics);
            services.AddSingleton(pipeline);
            services.AddSingleton(dualPath);
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsMetrics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("eventPipelineHotPath").GetProperty("hotTradePublished").GetInt64().Should().Be(1);
        root.GetProperty("eventPipelineHotPath").GetProperty("hotTradeFallbacks").GetInt64().Should().Be(0);
        root.GetProperty("eventPipelineHotPath").GetProperty("hotQuotePublished").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task DiagnosticsMetrics_IncludesJsonlStorageSnapshot_WhenJsonlSinkIsRegistered()
    {
        var rootPath = Path.Combine(_tempRoot, "jsonl-storage");
        Directory.CreateDirectory(rootPath);

        await using var sink = new JsonlStorageSink(
            new StorageOptions { RootPath = rootPath },
            new DiagnosticsJsonlStoragePolicy(rootPath),
            JsonlBatchOptions.Default);

        await sink.AppendAsync(CreateTradeEvent("SPY"));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(sink);
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsMetrics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var storage = document.RootElement.GetProperty("jsonlStorage");

        storage.GetProperty("batchingEnabled").GetBoolean().Should().BeTrue();
        storage.GetProperty("batchSize").GetInt32().Should().Be(JsonlBatchOptions.Default.BatchSize);
        storage.GetProperty("eventsBuffered").GetInt64().Should().Be(1);
        storage.GetProperty("eventsWritten").GetInt64().Should().Be(0);
        storage.GetProperty("batchesWritten").GetInt64().Should().Be(0);
        storage.GetProperty("bufferCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task DiagnosticsMetrics_RedactsTrackedErrorDetails()
    {
        var errorTracker = new ErrorTracker("data");
        errorTracker.RecordError(new TrackedError
        {
            Id = "err-sensitive",
            Timestamp = DateTimeOffset.UtcNow,
            Level = "ERROR",
            Message = "Provider failed secretKey=message-secret Authorization: Bearer liveBearerToken123",
            ExceptionType = typeof(InvalidOperationException).FullName,
            StackTrace = "at Provider.Connect(token=stack-token)",
            Context = "provider.connect?api_key=query-secret&externalAccountId=ACCT-123456",
            InnerException = "accountNumber=ACCT-654321 token=inner-token"
        });

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(errorTracker);
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsMetrics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("err-sensitive");
        payload.Should().Contain("[REDACTED]");
        payload.Should().NotContain("message-secret");
        payload.Should().NotContain("liveBearerToken123");
        payload.Should().NotContain("stack-token");
        payload.Should().NotContain("query-secret");
        payload.Should().NotContain("inner-token");
        payload.Should().NotContain("ACCT-123456");
        payload.Should().NotContain("ACCT-654321");
    }

    [Fact]
    public async Task DiagnosticsMetrics_IncludesSanitizedShutdownSequenceStatus()
    {
        var shutdownDiagnostics = new ShutdownDiagnosticsService();
        shutdownDiagnostics.RecordStarted(
            "shutdown-correlation",
            ShutdownReason.Error,
            DateTimeOffset.UtcNow.AddSeconds(-2),
            flushableCount: 1,
            disposableCount: 1,
            callbackCount: 1);
        shutdownDiagnostics.RecordCompleted(new ShutdownResult(
            Success: true,
            Reason: ShutdownReason.Error,
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-2),
            CompletedAt: DateTimeOffset.UtcNow,
            DurationMs: 1250,
            EventsFlushed: 0,
            FlushTimeoutOccurred: false,
            ComponentsDisposed: 1,
            Warnings:
            [
                "Flush error for JsonlWriter: token=shutdown-secret accountNumber=ACCT-987654"
            ],
            CorrelationId: "shutdown-correlation"));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(shutdownDiagnostics);
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsMetrics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("runtime.shutdown.sequence");
        payload.Should().Contain("shutdown-correlation");
        payload.Should().Contain("[REDACTED]");
        payload.Should().NotContain("shutdown-secret");
        payload.Should().NotContain("ACCT-987654");

        using var document = JsonDocument.Parse(payload);
        var shutdown = document.RootElement.GetProperty("shutdown");
        shutdown.GetProperty("available").GetBoolean().Should().BeTrue();
        shutdown.GetProperty("status").GetString().Should().Be("Completed");
        shutdown.GetProperty("incompleteFlushCount").GetInt32().Should().Be(1);
        shutdown.GetProperty("warningCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task DiagnosticsMetrics_IncludesSafeProviderConnectionDiagnostics()
    {
        var registry = new ProviderRegistry();
        registry.Register(new DiagnosticProvider(
            "alpaca-secret-provider",
            "Alpaca accountNumber=ACCT-123456",
            new WebSocketConnectionDiagnostics(
                ProviderName: "Alpaca token=provider-secret",
                LifecycleState: ProviderConnectionLifecycleState.Reconnecting,
                WebSocketState: WebSocketState.Aborted,
                IsConnected: false,
                IsReconnecting: true,
                ReconnectAttempts: 2,
                LastConnectedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                LastDisconnectedAt: DateTimeOffset.UtcNow.AddSeconds(-30),
                LastHeartbeatReceivedAt: DateTimeOffset.UtcNow.AddSeconds(-20),
                LastMessageReceivedAt: DateTimeOffset.UtcNow.AddSeconds(-25),
                LastReconnectAttemptAt: DateTimeOffset.UtcNow.AddSeconds(-10),
                LastError: "raw provider error with token=raw-error-secret accountNumber=ACCT-654321",
                LastFailureKind: ProviderFailureKind.TransientNetworkFailure,
                ConnectionAge: null,
                IdleDuration: TimeSpan.FromSeconds(25))));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(registry);
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsMetrics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("providerConnections");
        payload.Should().Contain("[REDACTED]");
        payload.Should().Contain("TransientNetworkFailure");
        payload.Should().NotContain("provider-secret");
        payload.Should().NotContain("raw-error-secret");
        payload.Should().NotContain("ACCT-123456");
        payload.Should().NotContain("ACCT-654321");

        using var document = JsonDocument.Parse(payload);
        var providerConnections = document.RootElement.GetProperty("providerConnections");
        providerConnections.GetProperty("available").GetBoolean().Should().BeTrue();
        providerConnections.GetProperty("count").GetInt32().Should().Be(1);
        var provider = providerConnections.GetProperty("providers").EnumerateArray().Single();
        provider.GetProperty("lifecycleState").GetString().Should().Be("Reconnecting");
        provider.GetProperty("webSocketState").GetString().Should().Be("Aborted");
        provider.GetProperty("isReconnecting").GetBoolean().Should().BeTrue();
        provider.GetProperty("reconnectAttempts").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task DiagnosticsConfigEndpoints_RedactSensitivePaths()
    {
        Directory.CreateDirectory(_tempRoot);
        var sensitiveDataRoot = Path.Combine(_tempRoot, "accountNumber=ACCT-123456-data");
        var sensitiveCoordinationRoot = Path.Combine(_tempRoot, "externalAccountId=ACCT-987654-leases");
        var configPath = Path.Combine(_tempRoot, "appsettings.json");
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(
                new AppConfig(
                    DataRoot: sensitiveDataRoot,
                    Symbols: [new SymbolConfig("SPY")],
                    Coordination: new CoordinationConfig(
                        Enabled: true,
                        RootPath: sensitiveCoordinationRoot)),
                AppConfigJsonOptions.Write));

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new UiConfigStore(configPath));
        });

        var endpoints = new[]
        {
            (Method: HttpMethod.Get, Path: UiApiRoutes.DiagnosticsConfig),
            (Method: HttpMethod.Get, Path: UiApiRoutes.DiagnosticsQuickCheck),
            (Method: HttpMethod.Get, Path: UiApiRoutes.DiagnosticsShowConfig),
            (Method: HttpMethod.Post, Path: UiApiRoutes.DiagnosticsValidateConfig)
        };

        foreach (var (method, path) in endpoints)
        {
            using var request = new HttpRequestMessage(method, path);
            var response = await app.GetTestClient().SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadAsStringAsync();
            payload.Should().Contain("[REDACTED]");
            payload.Should().NotContain("ACCT-123456");
            payload.Should().NotContain("ACCT-987654");
        }
    }

    [Fact]
    public async Task DiagnosticsStorage_RedactsSensitiveStorageRoot()
    {
        var sensitiveRoot = Path.Combine(_tempRoot, "custodianAccountId=ACCT-445566-storage");

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new StorageOptions { RootPath = sensitiveRoot });
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsStorage);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("[REDACTED]");
        payload.Should().NotContain("ACCT-445566");
    }

    [Fact]
    public async Task DiagnosticsCoordination_RedactsSensitivePathsAndIdentifiers()
    {
        var sensitiveRoot = Path.Combine(_tempRoot, "accountNumber=ACCT-111111-leases");
        var sensitiveLeaseFile = Path.Combine(sensitiveRoot, "corrupted", "custodianAccountId=ACCT-222222.lease.json");

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ILeaseManager>(new StubLeaseManager(new CoordinationSnapshot
            {
                Enabled = true,
                Mode = "SharedStorage",
                InstanceId = "ops-user",
                RootPath = sensitiveRoot,
                HeldLeaseCount = 1,
                OrphanedLeaseCount = 1,
                CorruptedLeaseCount = 1,
                HeldLeases =
                [
                    new LeaseRecord(
                        "symbol:accountNumber=ACCT-333333:AAPL",
                        "instance-accountId=ACCT-444444",
                        4,
                        DateTimeOffset.UtcNow.AddMinutes(-5),
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        DateTimeOffset.UtcNow)
                ],
                OrphanedResources = ["orphaned:externalAccountId=ACCT-555555"],
                CorruptedLeaseFiles = [sensitiveLeaseFile]
            }));
        });

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.DiagnosticsCoordination);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("[REDACTED]");
        payload.Should().NotContain("ACCT-111111");
        payload.Should().NotContain("ACCT-222222");
        payload.Should().NotContain("ACCT-333333");
        payload.Should().NotContain("ACCT-444444");
        payload.Should().NotContain("ACCT-555555");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static MarketEvent CreateTradeEvent(string symbol)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100m,
            Size: 10,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            Venue: "TEST");

        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade);
    }

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection> configureServices)
    {
        var applicationBuilder = WebApplication.CreateBuilder();
        applicationBuilder.WebHost.UseTestServer();
        applicationBuilder.Services.AddRouting();
        applicationBuilder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter("diagnostics-test"));
        });
        configureServices(applicationBuilder.Services);

        var app = applicationBuilder.Build();
        app.UseRateLimiter();
        app.MapDiagnosticsEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private sealed class TestEventMetrics : IEventMetrics
    {
        private long _published;
        private long _dropped;
        private long _trades;

        public long Published => Interlocked.Read(ref _published);
        public long Dropped => Interlocked.Read(ref _dropped);
        public long Integrity => 0;
        public long Trades => Interlocked.Read(ref _trades);
        public long DepthUpdates => 0;
        public long Quotes => 0;
        public long HistoricalBars => 0;
        public double EventsPerSecond => Published;
        public double DropRate => Published == 0 ? 0 : (double)Dropped / Published * 100;

        public void IncPublished() => Interlocked.Increment(ref _published);
        public void IncDropped() => Interlocked.Increment(ref _dropped);
        public void IncIntegrity() { }
        public void IncTrades() => Interlocked.Increment(ref _trades);
        public void IncDepthUpdates() { }
        public void IncQuotes() { }
        public void IncHistoricalBars() { }
        public void RecordLatency(long startTimestamp) { }
        public void Reset()
        {
            Interlocked.Exchange(ref _published, 0);
            Interlocked.Exchange(ref _dropped, 0);
            Interlocked.Exchange(ref _trades, 0);
        }

        public MetricsSnapshot GetSnapshot() => new(
            Published: Published,
            Dropped: Dropped,
            Integrity: Integrity,
            Trades: Trades,
            DepthUpdates: DepthUpdates,
            Quotes: Quotes,
            HistoricalBars: HistoricalBars,
            EventsPerSecond: EventsPerSecond,
            TradesPerSecond: Trades,
            DepthUpdatesPerSecond: 0,
            HistoricalBarsPerSecond: 0,
            DropRate: DropRate,
            AverageLatencyUs: 0,
            MinLatencyUs: 0,
            MaxLatencyUs: 0,
            LatencySampleCount: 0,
            Gc0Collections: 0,
            Gc1Collections: 0,
            Gc2Collections: 0,
            Gc0Delta: 0,
            Gc1Delta: 0,
            Gc2Delta: 0,
            MemoryUsageMb: 0,
            HeapSizeMb: 0,
            Timestamp: DateTimeOffset.UtcNow);
    }

    private sealed class NoOpStorageSink : IStorageSink
    {
        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default) => ValueTask.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DiagnosticProvider : IProviderMetadata, IProviderConnectionDiagnosticsSource
    {
        private readonly WebSocketConnectionDiagnostics _diagnostics;

        public DiagnosticProvider(
            string providerId,
            string displayName,
            WebSocketConnectionDiagnostics diagnostics)
        {
            ProviderId = providerId;
            ProviderDisplayName = displayName;
            _diagnostics = diagnostics;
        }

        public string ProviderId { get; }
        public string ProviderDisplayName { get; }
        public string ProviderDescription => "Diagnostics endpoint test provider.";
        public int ProviderPriority => 10;
        public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming();

        public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged
        {
            add { }
            remove { }
        }

        public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot() => _diagnostics;
    }

    private sealed class DiagnosticsJsonlStoragePolicy : Meridian.Storage.Interfaces.IStoragePolicy
    {
        private readonly string _rootPath;

        public DiagnosticsJsonlStoragePolicy(string rootPath)
        {
            _rootPath = rootPath;
        }

        public string GetPath(MarketEvent evt) => Path.Combine(_rootPath, $"{evt.Symbol}.jsonl");
    }

    private sealed class StubLeaseManager : ILeaseManager
    {
        private readonly CoordinationSnapshot _snapshot;

        public StubLeaseManager(CoordinationSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public bool Enabled => _snapshot.Enabled;
        public string InstanceId => _snapshot.InstanceId;
        public Task<LeaseAcquireResult> TryAcquireAsync(string resourceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RenewAsync(string resourceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ReleaseAsync(string resourceId, CancellationToken ct = default) => throw new NotSupportedException();
        public bool HoldsLease(string resourceId) => false;
        public Task<CoordinationSnapshot> GetSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
    }
}
