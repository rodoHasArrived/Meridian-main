using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
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

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new DiagnosticsFeatureRegistration().Register(services, options);

        await using var provider = services.BuildServiceProvider();
        var bundleService = provider.GetRequiredService<DiagnosticBundleService>();

        var result = await bundleService.GenerateAsync(new DiagnosticBundleOptions(
            IncludeRuntimeSummary: true,
            IncludeSystemInfo: false,
            IncludeConfiguration: false,
            IncludeMetrics: true,
            IncludeLogs: false,
            IncludeStorageInfo: false,
            IncludeEnvironmentVariables: false));

        result.Success.Should().BeTrue(result.Message);
        using var archive = ZipFile.OpenRead(result.ZipPath!);
        using var metricsDocument = await JsonDocument.ParseAsync(archive.GetEntry("metrics.json")!.Open());
        using var summaryDocument = await JsonDocument.ParseAsync(archive.GetEntry("runtime-summary.json")!.Open());

        metricsDocument.RootElement.GetProperty("published").GetInt64().Should().Be(1);
        summaryDocument.RootElement.GetProperty("metrics").GetProperty("available").GetBoolean().Should().BeTrue();
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
}
