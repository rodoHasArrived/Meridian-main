using System.IO.Compression;
using System.Net.WebSockets;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Contracts.Monitoring;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;
using Meridian.Platform.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class DiagnosticsFeatureRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-diagnostics-registration-{Guid.NewGuid():N}");

    [Fact]
    public async Task Register_WiresEventMetricsIntoDiagnosticBundleService()
    {
        Directory.CreateDirectory(_root);
        var dataRoot = Path.Combine(_root, "data");
        var configPath = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(new AppConfig(DataRoot: dataRoot), AppConfigJsonOptions.Write));

        var metrics = new TestEventMetrics();
        metrics.IncPublished();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventMetrics>(metrics);
        var registry = new ProviderRegistry();
        registry.Register(new DiagnosticProvider(new WebSocketConnectionDiagnostics(
            ProviderName: "Alpaca",
            LifecycleState: ProviderConnectionLifecycleState.Connected,
            WebSocketState: WebSocketState.Open,
            IsConnected: true,
            IsReconnecting: false,
            ReconnectAttempts: 1,
            LastConnectedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            LastDisconnectedAt: null,
            LastHeartbeatReceivedAt: DateTimeOffset.UtcNow.AddSeconds(-5),
            LastMessageReceivedAt: DateTimeOffset.UtcNow.AddSeconds(-4),
            LastReconnectAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            LastError: "raw detail omitted from bundle",
            LastFailureKind: null,
            ConnectionAge: TimeSpan.FromMinutes(1),
            IdleDuration: TimeSpan.FromSeconds(4))));
        services.AddSingleton(registry);

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new DiagnosticsFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var bundleService = provider.GetRequiredService<DiagnosticBundleService>();
        var errorTracker = provider.GetRequiredService<ErrorTracker>();
        var shutdownDiagnostics = provider.GetRequiredService<ShutdownDiagnosticsService>();
        errorTracker.RecordError(new TrackedError
        {
            Id = "err-registration",
            Timestamp = DateTimeOffset.UtcNow,
            Level = "ERROR",
            Message = "Provider failed token=registration-secret",
            Context = "diagnostic-bundle?accountNumber=ACCT-123456"
        });
        shutdownDiagnostics.RecordStarted(
            "registration-shutdown-correlation",
            ShutdownReason.UserRequested,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            flushableCount: 1,
            disposableCount: 0,
            callbackCount: 0);
        shutdownDiagnostics.RecordCompleted(new ShutdownResult(
            Success: true,
            Reason: ShutdownReason.UserRequested,
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            DurationMs: 250,
            ComponentsDisposed: 0,
            CorrelationId: "registration-shutdown-correlation"));

        var result = await bundleService.GenerateAsync(new DiagnosticBundleOptions(
            IncludeRuntimeSummary: true,
            IncludeSystemInfo: false,
            IncludeConfiguration: false,
            IncludeMetrics: true,
            IncludeTrackedErrors: true,
            IncludeLogs: false,
            IncludeStorageInfo: false,
            IncludeEnvironmentVariables: false));

        result.Success.Should().BeTrue(result.Message);
        using var archive = ZipFile.OpenRead(result.ZipPath!);
        using var metricsDocument = await JsonDocument.ParseAsync(archive.GetEntry("metrics.json")!.Open());
        using var errorsDocument = await JsonDocument.ParseAsync(archive.GetEntry("recent-errors.json")!.Open());
        using var summaryDocument = await JsonDocument.ParseAsync(archive.GetEntry("runtime-summary.json")!.Open());

        metricsDocument.RootElement.GetProperty("published").GetInt64().Should().Be(1);
        errorsDocument.RootElement.GetProperty("errors").EnumerateArray()
            .Should()
            .Contain(error => error.GetProperty("id").GetString() == "err-registration");
        errorsDocument.RootElement.GetRawText().Should().NotContain("registration-secret");
        errorsDocument.RootElement.GetRawText().Should().NotContain("ACCT-123456");
        summaryDocument.RootElement.GetProperty("metrics").GetProperty("available").GetBoolean().Should().BeTrue();
        summaryDocument.RootElement.GetProperty("errors").GetProperty("available").GetBoolean().Should().BeTrue();
        summaryDocument.RootElement.GetProperty("shutdown").GetProperty("available").GetBoolean().Should().BeTrue();
        summaryDocument.RootElement.GetProperty("shutdown").GetProperty("correlationId").GetString().Should().Be("registration-shutdown-correlation");
        summaryDocument.RootElement.GetProperty("providerConnections").GetProperty("available").GetBoolean().Should().BeTrue();
        summaryDocument.RootElement.GetProperty("providerConnections").GetProperty("count").GetInt32().Should().Be(1);
        summaryDocument.RootElement.GetProperty("providerConnections").GetProperty("providers").EnumerateArray().Single()
            .GetProperty("lifecycleState").GetString().Should().Be("Connected");
    }

    [Fact]
    public async Task Register_DiagnosticBundleRedactsProviderSecrets()
    {
        Directory.CreateDirectory(_root);
        var dataRoot = Path.Combine(_root, "data");
        var configPath = Path.Combine(_root, "appsettings.json");
        const string keyId = "PKLIVEABCDEFGHIJKLMNOP";
        const string secretKey = "super-secret-provider-token";
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(
                new AppConfig(
                    DataRoot: dataRoot,
                    DataSource: DataSourceKind.Alpaca,
                    Alpaca: new AlpacaOptions(
                        KeyId: keyId,
                        SecretKey: secretKey,
                        Feed: "iex",
                        UseSandbox: true)),
                AppConfigJsonOptions.Write));

        var services = new ServiceCollection();
        services.AddLogging();

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new DiagnosticsFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var bundleService = provider.GetRequiredService<DiagnosticBundleService>();

        var result = await bundleService.GenerateAsync(new DiagnosticBundleOptions(
            IncludeRuntimeSummary: false,
            IncludeSystemInfo: false,
            IncludeConfiguration: true,
            IncludeMetrics: false,
            IncludeLogs: false,
            IncludeStorageInfo: false,
            IncludeEnvironmentVariables: false));

        result.Success.Should().BeTrue(result.Message);
        using var archive = ZipFile.OpenRead(result.ZipPath!);
        using var reader = new StreamReader(archive.GetEntry("config-sanitized.json")!.Open());
        var sanitizedConfig = await reader.ReadToEndAsync();

        sanitizedConfig.Should().Contain("[REDACTED]");
        sanitizedConfig.Should().NotContain(keyId);
        sanitizedConfig.Should().NotContain(secretKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestEventMetrics : IEventMetrics
    {
        private long _published;

        public long Published => Interlocked.Read(ref _published);
        public long Dropped => 0;
        public long Integrity => 0;
        public long Trades => 0;
        public long DepthUpdates => 0;
        public long Quotes => 0;
        public long HistoricalBars => 0;
        public double EventsPerSecond => Published;
        public double DropRate => 0;

        public void IncPublished() => Interlocked.Increment(ref _published);
        public void IncDropped() { }
        public void IncIntegrity() { }
        public void IncTrades() { }
        public void IncDepthUpdates() { }
        public void IncQuotes() { }
        public void IncHistoricalBars() { }
        public void RecordLatency(long startTimestamp) { }
        public void Reset() => Interlocked.Exchange(ref _published, 0);

        public MetricsSnapshot GetSnapshot() => new(
            Published: Published,
            Dropped: Dropped,
            Integrity: Integrity,
            Trades: Trades,
            DepthUpdates: DepthUpdates,
            Quotes: Quotes,
            HistoricalBars: HistoricalBars,
            EventsPerSecond: EventsPerSecond,
            TradesPerSecond: 0,
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

    private sealed class DiagnosticProvider : IProviderMetadata, IProviderConnectionDiagnosticsSource
    {
        private readonly WebSocketConnectionDiagnostics _diagnostics;

        public DiagnosticProvider(WebSocketConnectionDiagnostics diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public string ProviderId => "alpaca";
        public string ProviderDisplayName => "Alpaca";
        public string ProviderDescription => "Diagnostics registration test provider.";
        public int ProviderPriority => 10;
        public ProviderCapabilities ProviderCapabilities { get; } = ProviderCapabilities.Streaming();

        public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged
        {
            add { }
            remove { }
        }

        public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot() => _diagnostics;
    }
}
