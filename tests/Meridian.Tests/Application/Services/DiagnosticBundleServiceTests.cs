using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class DiagnosticBundleServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-diag-tests-{Guid.NewGuid():N}");
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task GenerateAsync_RedactsSecretsAndAccountNumbersAcrossBundle()
    {
        var dataRoot = Path.Combine(_tempRoot, "accountNumber=ACCT-654321-data");
        var logsRoot = Path.Combine(dataRoot, "_logs");
        Directory.CreateDirectory(logsRoot);
        Directory.CreateDirectory(Path.Combine(dataRoot, "statements"));

        await File.WriteAllTextAsync(
            Path.Combine(logsRoot, "meridian-20260520.log"),
            """
            2026-05-20T12:00:00Z Provider auth failed secretKey=log-secret-value token=log-token-value accountNumber=ACCT-778899
            2026-05-20T12:00:01Z Authorization: Bearer liveBearerToken12345
            2026-05-20T12:00:02Z callback=https://example.test/callback?api_key=query-secret&symbol=AAPL
            """);
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "statements", "accountNumber=ACCT-123456.csv"), "fixture");

        SetEnvironmentVariable("ALPACA_SECRET_KEY", "env-secret-value");
        SetEnvironmentVariable("MDC_SESSION_TOKEN", "env-session-token");

        var service = new DiagnosticBundleService(
            dataRoot,
            metricsProvider: CreateMetricsSnapshot,
            configProvider: CreateConfigWithSecrets);

        var result = await service.GenerateAsync(new DiagnosticBundleOptions(
            IncludeSystemInfo: false,
            IncludeConfiguration: true,
            IncludeMetrics: true,
            IncludeLogs: true,
            IncludeStorageInfo: true,
            IncludeEnvironmentVariables: true,
            LogDays: 7));

        result.Success.Should().BeTrue(result.Message);
        result.ZipPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(result.ZipPath).Should().BeTrue();

        var bundleText = ReadAllBundleText(result.ZipPath!);
        bundleText.Should().NotContain("config-key-id");
        bundleText.Should().NotContain("config-secret-key");
        bundleText.Should().NotContain("log-secret-value");
        bundleText.Should().NotContain("log-token-value");
        bundleText.Should().NotContain("liveBearerToken12345");
        bundleText.Should().NotContain("query-secret");
        bundleText.Should().NotContain("env-secret-value");
        bundleText.Should().NotContain("env-session-token");
        bundleText.Should().NotContain("config-polygon-secret");
        bundleText.Should().NotContain("top-level-polygon-secret");
        bundleText.Should().NotContain("source-polygon-secret");
        bundleText.Should().NotContain("source-alpaca-key");
        bundleText.Should().NotContain("source-alpaca-secret");
        bundleText.Should().NotContain("vault://prod/alpaca-secret");
        bundleText.Should().NotContain("brokerage-account-9999");
        bundleText.Should().NotContain("ACCT-778899");
        bundleText.Should().NotContain("ACCT-123456");
        bundleText.Should().NotContain("ACCT-654321");
        bundleText.Should().Contain("[REDACTED]");

        using var archive = ZipFile.OpenRead(result.ZipPath!);
        var manifestEntry = archive.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull();
        using var manifestStream = manifestEntry!.Open();
        using var manifestDocument = await JsonDocument.ParseAsync(manifestStream);

        manifestDocument.RootElement.GetProperty("bundleId").GetString().Should().Be(result.BundleId);
        manifestDocument.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        manifestDocument.RootElement.GetProperty("durationMs").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        manifestDocument.RootElement.GetProperty("filesCollected").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(["runtime-summary.json", "config-sanitized.json", "metrics.json", "environment.txt", "storage-info.txt"]);

        var runtimeSummaryEntry = archive.GetEntry("runtime-summary.json");
        runtimeSummaryEntry.Should().NotBeNull();
        using var runtimeSummaryStream = runtimeSummaryEntry!.Open();
        using var runtimeSummaryDocument = await JsonDocument.ParseAsync(runtimeSummaryStream);

        runtimeSummaryDocument.RootElement.GetProperty("operationName").GetString().Should().Be("diagnostic-bundle.generate");
        runtimeSummaryDocument.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        runtimeSummaryDocument.RootElement.GetProperty("diagnostics").GetProperty("logDirectoryExists").GetBoolean().Should().BeTrue();
        runtimeSummaryDocument.RootElement.GetProperty("metrics").GetProperty("available").GetBoolean().Should().BeTrue();
    }

    public void Dispose()
    {
        foreach (var (key, value) in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        _originalEnvironment.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }

    private static AppConfig CreateConfigWithSecrets() => new(
        DataRoot: "data",
        DataSource: DataSourceKind.Alpaca,
        Alpaca: new AlpacaOptions(
            KeyId: "config-key-id",
            SecretKey: "config-secret-key",
            Feed: "iex",
            UseSandbox: true),
        Polygon: new PolygonOptions(ApiKey: "top-level-polygon-secret"),
        Backfill: new BackfillConfig(
            Enabled: true,
            Provider: "polygon",
            Providers: new BackfillProvidersConfig(
                Polygon: new PolygonConfig(ApiKey: "config-polygon-secret"))),
        DataSources: new DataSourcesConfig(
            Sources:
            [
                new DataSourceConfig(
                    Id: "source-polygon",
                    Name: "Polygon primary",
                    Provider: DataSourceKind.Polygon,
                    Polygon: new PolygonOptions(ApiKey: "source-polygon-secret")),
                new DataSourceConfig(
                    Id: "source-alpaca",
                    Name: "Alpaca fallback",
                    Provider: DataSourceKind.Alpaca,
                    Alpaca: new AlpacaOptions(
                        KeyId: "source-alpaca-key",
                        SecretKey: "source-alpaca-secret"))
            ],
            DefaultRealTimeSourceId: "source-alpaca",
            DefaultHistoricalSourceId: "source-polygon"),
        ProviderConnections: new ProviderConnectionsConfig(
            Connections:
            [
                new ProviderConnectionConfig(
                    ConnectionId: "alpaca-prod",
                    ProviderFamilyId: "alpaca",
                    DisplayName: "Alpaca production",
                    ConnectionType: ProviderConnectionType.Brokerage,
                    ConnectionMode: ProviderConnectionMode.ReadOnly,
                    CredentialReference: "vault://prod/alpaca-secret",
                    ExternalAccountId: "brokerage-account-9999",
                    Scope: new ProviderConnectionScope(Workspace: "Trading"))
            ]));

    private static MetricsSnapshot CreateMetricsSnapshot() => new(
        Published: 10,
        Dropped: 1,
        Integrity: 0,
        Trades: 4,
        DepthUpdates: 2,
        Quotes: 4,
        HistoricalBars: 0,
        EventsPerSecond: 10,
        TradesPerSecond: 4,
        DepthUpdatesPerSecond: 2,
        HistoricalBarsPerSecond: 0,
        DropRate: 9.09,
        AverageLatencyUs: 20,
        MinLatencyUs: 5,
        MaxLatencyUs: 100,
        LatencySampleCount: 10,
        Gc0Collections: 1,
        Gc1Collections: 0,
        Gc2Collections: 0,
        Gc0Delta: 1,
        Gc1Delta: 0,
        Gc2Delta: 0,
        MemoryUsageMb: 64,
        HeapSizeMb: 32,
        Timestamp: DateTimeOffset.UtcNow);

    private static string ReadAllBundleText(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var builder = new StringBuilder();

        foreach (var entry in archive.Entries.Where(static entry => !string.IsNullOrEmpty(entry.Name)))
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            builder.AppendLine(reader.ReadToEnd());
        }

        return builder.ToString();
    }
}
