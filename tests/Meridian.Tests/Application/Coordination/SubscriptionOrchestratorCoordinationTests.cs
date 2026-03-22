using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.Subscriptions;
using Meridian.Contracts.Configuration;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Application.Coordination;

public sealed class SubscriptionOrchestratorCoordinationTests
{
    [Fact]
    public async Task ApplyAsync_AllowsOnlyOneInstanceToOwnSharedSymbolSubscriptions()
    {
        var tempDir = CreateTempDir();
        try
        {
            var coordination = CreateConfig(tempDir);
            var publisher = new TestMarketEventPublisher();
            var config = new AppConfig(
                DataRoot: tempDir,
                Symbols:
                [
                    new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 5)
                ],
                Coordination: coordination);

            await using var leaseManager1 = new LeaseManager(
                coordination with { InstanceId = "instance-a" },
                new SharedStorageCoordinationStore(coordination with { InstanceId = "instance-a" }, tempDir));
            await using var leaseManager2 = new LeaseManager(
                coordination with { InstanceId = "instance-b" },
                new SharedStorageCoordinationStore(coordination with { InstanceId = "instance-b" }, tempDir));

            var owner1 = new SubscriptionOwnershipService(leaseManager1);
            var owner2 = new SubscriptionOwnershipService(leaseManager2);

            var client1 = new TrackingMarketDataClient();
            var client2 = new TrackingMarketDataClient();

            var orchestrator1 = new SubscriptionOrchestrator(
                new MarketDepthCollector(publisher),
                new TradeDataCollector(publisher),
                client1,
                "polygon",
                owner1);
            var orchestrator2 = new SubscriptionOrchestrator(
                new MarketDepthCollector(publisher),
                new TradeDataCollector(publisher),
                client2,
                "polygon",
                owner2);

            await Task.WhenAll(
                orchestrator1.ApplyAsync(config),
                orchestrator2.ApplyAsync(config));

            (client1.DepthSubscriptions.Count + client2.DepthSubscriptions.Count).Should().Be(1);
            (client1.TradeSubscriptions.Count + client2.TradeSubscriptions.Count).Should().Be(1);

            var activeClient = client1.TradeSubscriptions.Count == 1 ? client1 : client2;
            var passiveClient = ReferenceEquals(activeClient, client1) ? client2 : client1;
            var activeOrchestrator = ReferenceEquals(activeClient, client1) ? orchestrator1 : orchestrator2;
            var passiveOrchestrator = ReferenceEquals(activeClient, client1) ? orchestrator2 : orchestrator1;

            activeOrchestrator.ActiveSubscriptionCount.Should().Be(2);
            passiveOrchestrator.ActiveSubscriptionCount.Should().Be(0);

            await activeOrchestrator.ApplyAsync(config with { Symbols = [] });
            await passiveOrchestrator.ApplyAsync(config);

            passiveClient.TradeSubscriptions.Should().ContainSingle().Which.Should().Be("SPY");
            passiveClient.DepthSubscriptions.Should().ContainSingle().Which.Should().Be("SPY");
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    private static CoordinationConfig CreateConfig(string dataRoot)
        => new(
            Enabled: true,
            Mode: CoordinationMode.SharedStorage,
            LeaseTtlSeconds: 30,
            RenewIntervalSeconds: 10,
            TakeoverDelaySeconds: 5,
            RootPath: Path.Combine(dataRoot, "_coordination"));

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian_subcoord_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDir(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    private sealed class TrackingMarketDataClient : IMarketDataClient
    {
        private int _nextId = 1000;
        private readonly Dictionary<int, string> _tradeSubscriptionIds = new();
        private readonly Dictionary<int, string> _depthSubscriptionIds = new();

        public bool IsEnabled => true;
        public bool IsConnected { get; private set; }
        public string ProviderId => "tracking";
        public string ProviderDisplayName => "Tracking Client";
        public string ProviderDescription => "Test tracking client";
        public int ProviderPriority => 0;
        public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.Streaming(trades: true, depth: true);

        public IReadOnlyList<string> TradeSubscriptions => _tradeSubscriptionIds.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        public IReadOnlyList<string> DepthSubscriptions => _depthSubscriptionIds.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        public Task ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public int SubscribeMarketDepth(SymbolConfig cfg)
        {
            var id = Interlocked.Increment(ref _nextId);
            _depthSubscriptionIds[id] = cfg.Symbol;
            return id;
        }

        public void UnsubscribeMarketDepth(int subscriptionId)
        {
            _depthSubscriptionIds.Remove(subscriptionId);
        }

        public int SubscribeTrades(SymbolConfig cfg)
        {
            var id = Interlocked.Increment(ref _nextId);
            _tradeSubscriptionIds[id] = cfg.Symbol;
            return id;
        }

        public void UnsubscribeTrades(int subscriptionId)
        {
            _tradeSubscriptionIds.Remove(subscriptionId);
        }

        public ValueTask DisposeAsync()
        {
            _tradeSubscriptionIds.Clear();
            _depthSubscriptionIds.Clear();
            return ValueTask.CompletedTask;
        }
    }
}
