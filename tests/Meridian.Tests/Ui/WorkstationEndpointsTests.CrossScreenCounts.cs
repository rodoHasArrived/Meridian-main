using System.Text.Json;
using FluentAssertions;
using Meridian.DataIntegration.Monitoring;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_CrossScreenCounts_CountsProvidersLikeTheDataWorkspaceAndGatesEachSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "cross-screen-counts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_status"));
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        // The metrics store's own tally says both providers are healthy; the rows the Data workspace
        // lists say one is degraded. Every count must follow the rows the operator can see.
        var metrics = new ProviderMetricsStatus(
            Timestamp: new DateTimeOffset(2026, 4, 24, 17, 0, 0, TimeSpan.Zero),
            Providers:
            [
                CrossScreenProviderMetrics("yahoo", isConnected: true, quality: 0.96, successRate: 1),
                CrossScreenProviderMetrics("alpaca", isConnected: false, quality: 0.62, successRate: 0)
            ],
            TotalProviders: 2,
            HealthyProviders: 2);
        await File.WriteAllTextAsync(Path.Combine(root, "_status", "providers.json"), JsonSerializer.Serialize(metrics, ServerJsonOptions));

        await using var app = await CreateAppAsync(services => RegisterConfigStores(services, configPath),
            currentUserPermissions: UserPermission.ManageCredentials | UserPermission.ViewHistoricalData);
        var client = app.GetTestClient();

        using var counts = await ReadJsonAsync(client, "/api/workstation/cross-screen-counts");
        var providers = counts.RootElement.GetProperty("providers");
        providers.GetProperty("healthy").GetInt32().Should().Be(1);
        providers.GetProperty("total").GetInt32().Should().Be(2);
        providers.GetProperty("definition").GetString().Should().Contain("Healthy");

        // A Data-only caller is not handed the trading book's order count or the break queue's.
        counts.RootElement.GetProperty("openOrders").ValueKind.Should().Be(JsonValueKind.Null);
        counts.RootElement.GetProperty("openBreaks").ValueKind.Should().Be(JsonValueKind.Null);

        // The Data workspace's own headline card agrees with the counts endpoint and with its list.
        using var data = await ReadJsonAsync(client, "/api/workstation/data");
        var listedHealthy = data.RootElement.GetProperty("providers").EnumerateArray()
            .Count(static provider => provider.GetProperty("status").GetString() == "Healthy");
        var card = data.RootElement.GetProperty("metrics").EnumerateArray()
            .First(static metric => metric.GetProperty("id").GetString() == "providers-healthy");
        card.GetProperty("value").GetString().Should().Be("1");
        listedHealthy.Should().Be(1);
    }

    private static ProviderMetrics CrossScreenProviderMetrics(string providerId, bool isConnected, double quality, double successRate) =>
        new(
            ProviderId: providerId,
            ProviderType: "Streaming equities",
            IsConnected: isConnected,
            TradesReceived: 10,
            DepthUpdatesReceived: 0,
            QuotesReceived: 10,
            ConnectionAttempts: 1,
            ConnectionFailures: isConnected ? 0 : 1,
            MessagesDropped: 0,
            ActiveSubscriptions: 1,
            AverageLatencyMs: isConnected ? 40 : 500,
            MinLatencyMs: 20,
            MaxLatencyMs: isConnected ? 80 : 900,
            DataQualityScore: quality,
            ConnectionSuccessRate: successRate,
            Timestamp: new DateTimeOffset(2026, 4, 24, 16, 59, 0, TimeSpan.Zero));
}
