using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Plaid;
using Meridian.DataIntegration.Monitoring;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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

    [Fact]
    public async Task MapWorkstationEndpoints_ReadinessRepositoryUnavailable_PreservesDataAndSharedProviderCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-readiness-failure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_status"));
        try
        {
            var configPath = Path.Combine(root, "appsettings.json");
            await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");
            var metrics = new ProviderMetricsStatus(DateTimeOffset.UtcNow,
                [CrossScreenProviderMetrics("yahoo", isConnected: true, quality: 0.96, successRate: 1)],
                TotalProviders: 1, HealthyProviders: 1);
            await File.WriteAllTextAsync(Path.Combine(root, "_status", "providers.json"),
                JsonSerializer.Serialize(metrics, ServerJsonOptions));

            var plaid = Substitute.For<IPlaidConnectionRepository>();
            plaid.ListItemsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
                Task.FromException<IReadOnlyList<PlaidItemDto>>(new IOException("Synthetic storage failure.")));
            await using var app = await CreateAppAsync(services =>
            {
                RegisterConfigStores(services, configPath);
                services.AddSingleton(plaid);
                services.AddSingleton<ProviderReadinessService>();
            }, currentUserPermissions: UserPermission.ViewDiagnostics | UserPermission.ViewHistoricalData);
            using var client = app.GetTestClient();

            foreach (var route in new[] { UiApiRoutes.WorkstationData, UiApiRoutes.WorkstationDataOperations })
            {
                using var data = await ReadJsonAsync(client, route);
                var provider = data.RootElement.GetProperty("providers").EnumerateArray().Should().ContainSingle().Subject;
                provider.GetProperty("providerId").GetString().Should().Be("yahoo");
                provider.GetProperty("status").GetString().Should().Be("Healthy");
                provider.GetProperty("latency").GetString().Should().Be("40 ms average");
                provider.GetProperty("diagnostics").EnumerateArray().Should().Contain(diagnostic =>
                    diagnostic.GetProperty("id").GetString() == "provider-readiness" &&
                    diagnostic.GetProperty("statusLabel").GetString() == "Unavailable");
            }

            using var counts = await ReadJsonAsync(client, UiApiRoutes.WorkstationCrossScreenCounts);
            counts.RootElement.GetProperty("providers").GetProperty("healthy").GetInt32().Should().Be(1);
            await plaid.Received(3).ListItemsAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            // Recent: a provider is not listed Healthy on evidence older than three days.
            Timestamp: DateTimeOffset.UtcNow.AddMinutes(-1));
}
