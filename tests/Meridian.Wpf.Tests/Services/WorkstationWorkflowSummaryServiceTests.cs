using System.IO;
using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.UI;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationWorkflowSummaryServiceTests
{
    [Fact]
    public async Task GetAsync_WithDegradedProviderMetrics_RoutesDataActionToProviderHealth()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-workflow-summary-" + Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        Directory.CreateDirectory(Path.Combine(dataRoot, "_status"));

        try
        {
            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(new Dictionary<string, string?> { ["DataRoot"] = dataRoot }));
            File.WriteAllText(
                Path.Combine(dataRoot, "_status", "providers.json"),
                JsonSerializer.Serialize(
                    new ProviderMetricsStatus(
                        Timestamp: DateTimeOffset.UtcNow,
                        Providers:
                        [
                            new ProviderMetrics(
                                ProviderId: "polygon",
                                ProviderType: "Polygon",
                                IsConnected: false,
                                TradesReceived: 0,
                                DepthUpdatesReceived: 0,
                                QuotesReceived: 0,
                                ConnectionAttempts: 1,
                                ConnectionFailures: 1,
                                MessagesDropped: 0,
                                ActiveSubscriptions: 0,
                                AverageLatencyMs: 0,
                                MinLatencyMs: 0,
                                MaxLatencyMs: 0,
                                DataQualityScore: 0,
                                ConnectionSuccessRate: 0,
                                Timestamp: DateTimeOffset.UtcNow)
                        ],
                        TotalProviders: 1,
                        HealthyProviders: 0),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            var readService = new StrategyRunReadService(
                new StrategyRunStore(),
                new PortfolioReadService(),
                new LedgerReadService());
            var summaryService = new WorkstationWorkflowSummaryService(
                readService,
                configStore: new Meridian.Application.UI.ConfigStore(configPath));

            var summary = await summaryService.GetAsync();
            var dataOperationsSummary = summary.Workspaces.Single(static workspace => workspace.WorkspaceId == "data");

            dataOperationsSummary.StatusLabel.Should().Be("Provider degradation detected");
            dataOperationsSummary.NextAction.Label.Should().Be("Open Provider Health");
            dataOperationsSummary.NextAction.TargetPageTag.Should().Be("ProviderHealth");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
