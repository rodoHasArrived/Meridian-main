using FluentAssertions;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Failover;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="FailoverAwareMarketDataClient"/>.
/// Tests delegation to active provider, subscription tracking, and provider switching.
/// </summary>
public sealed class FailoverAwareMarketDataClientTests : IAsyncLifetime
{
    private readonly ConnectionHealthMonitor _healthMonitor;
    private readonly StreamingFailoverService _failoverService;
    private readonly FakeMarketDataClient _primaryClient;
    private readonly FakeMarketDataClient _backupClient;
    private readonly Dictionary<string, IMarketDataClient> _providers;
    private FailoverAwareMarketDataClient _sut = null!;

    private readonly FailoverRuleConfig _rule = new(
        Id: "test-rule",
        PrimaryProviderId: "primary",
        BackupProviderIds: new[] { "backup" },
        FailoverThreshold: 3,
        RecoveryThreshold: 2
    );

    public FailoverAwareMarketDataClientTests()
    {
        _healthMonitor = new ConnectionHealthMonitor();
        _failoverService = new StreamingFailoverService(_healthMonitor);
        _primaryClient = new FakeMarketDataClient("primary");
        _backupClient = new FakeMarketDataClient("backup");

        _providers = new Dictionary<string, IMarketDataClient>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = _primaryClient,
            ["backup"] = _backupClient
        };
    }

    public Task InitializeAsync()
    {
        _failoverService.RegisterProvider("primary");
        _failoverService.RegisterProvider("backup");

        _sut = new FailoverAwareMarketDataClient(_providers, _failoverService, "test-rule", "primary");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sut.DisposeAsync();
        _failoverService.Dispose();
        _healthMonitor.Dispose();
    }

    [Fact]
    public void Constructor_SetsActiveProvider()
    {
        _sut.ActiveProviderId.Should().Be("primary");
        _sut.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithInvalidInitialProvider_Throws()
    {
        var act = () => new FailoverAwareMarketDataClient(_providers, _failoverService, "test-rule", "nonexistent");
        act.Should().Throw<ArgumentException>().WithMessage("*nonexistent*");
    }

    [Fact]
    public async Task ConnectAsync_DelegatesToActiveClient()
    {
        await _sut.ConnectAsync();

        _primaryClient.ConnectCallCount.Should().Be(1);
        _backupClient.ConnectCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ConnectAsync_OnFailure_TriesBackup()
    {
        _primaryClient.ShouldFailConnect = true;

        await _sut.ConnectAsync();

        _primaryClient.ConnectCallCount.Should().Be(1);
        _backupClient.ConnectCallCount.Should().Be(1);
        _sut.ActiveProviderId.Should().Be("backup");
    }

    [Fact]
    public async Task ConnectAsync_WhenCancelled_DoesNotRecordFailureOrTryBackup()
    {
        _primaryClient.ShouldCancelConnect = true;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _backupClient.ConnectCallCount.Should().Be(0);
        _sut.ActiveProviderId.Should().Be("primary");
        _failoverService.GetProviderHealthSnapshots()
            .First(s => s.ProviderId == "primary")
            .ConsecutiveFailures
            .Should().Be(0);
    }

    [Fact]
    public async Task Constructor_NormalizesInitialProviderIdentifier()
    {
        await _sut.DisposeAsync();

        _sut = new FailoverAwareMarketDataClient(_providers, _failoverService, "test-rule", " PRIMARY ");

        _sut.ActiveProviderId.Should().Be("primary");
    }

    [Fact]
    public async Task Constructor_NormalizesIncomingProviderMapKeys()
    {
        await _sut.DisposeAsync();

        var providers = new Dictionary<string, IMarketDataClient>
        {
            [" PRIMARY "] = _primaryClient,
            [" BACKUP "] = _backupClient
        };

        _sut = new FailoverAwareMarketDataClient(providers, _failoverService, "test-rule", "primary");

        _sut.ActiveProviderId.Should().Be("primary");
        await _sut.ConnectAsync();
        _primaryClient.ConnectCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Constructor_DuplicateNormalizedProviderKeys_Throws()
    {
        var providers = new Dictionary<string, IMarketDataClient>
        {
            ["primary"] = _primaryClient,
            [" PRIMARY "] = _backupClient
        };

        var act = () => new FailoverAwareMarketDataClient(providers, _failoverService, "test-rule", "primary");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate provider*normalized key 'primary'*");
    }

    [Fact]
    public async Task ConnectAsync_AllProvidersFail_Throws()
    {
        _primaryClient.ShouldFailConnect = true;
        _backupClient.ShouldFailConnect = true;

        var act = () => _sut.ConnectAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*All streaming providers failed*");
    }

    [Fact]
    public async Task DisconnectAsync_DelegatesToActiveClient()
    {
        await _sut.ConnectAsync();
        await _sut.DisconnectAsync();

        _primaryClient.DisconnectCallCount.Should().Be(1);
    }

    [Fact]
    public void SubscribeMarketDepth_DelegatesToActiveClient()
    {
        var cfg = new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: 5);

        var id = _sut.SubscribeMarketDepth(cfg);

        id.Should().BeGreaterThan(0);
        _primaryClient.DepthSubscriptions.Should().ContainKey("SPY");
    }

    [Fact]
    public void SubscribeMarketDepth_DuplicateSymbol_ReturnsExistingSubscription()
    {
        var cfg = new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: 5);

        var firstId = _sut.SubscribeMarketDepth(cfg);
        var secondId = _sut.SubscribeMarketDepth(cfg);

        secondId.Should().Be(firstId);
        _primaryClient.DepthSubscribeCallCount.Should().Be(1,
            "the failover wrapper should not create duplicate upstream depth subscriptions for the same symbol");
    }

    [Fact]
    public void SubscribeTrades_DelegatesToActiveClient()
    {
        var cfg = new SymbolConfig("AAPL", SubscribeTrades: true);

        var id = _sut.SubscribeTrades(cfg);

        id.Should().BeGreaterThan(0);
        _primaryClient.TradeSubscriptions.Should().ContainKey("AAPL");
    }

    [Fact]
    public void SubscribeTrades_DuplicateSymbol_ReturnsExistingSubscription()
    {
        var cfg = new SymbolConfig("AAPL", SubscribeTrades: true);

        var firstId = _sut.SubscribeTrades(cfg);
        var secondId = _sut.SubscribeTrades(cfg);

        secondId.Should().Be(firstId);
        _primaryClient.TradeSubscribeCallCount.Should().Be(1,
            "the failover wrapper should not create duplicate upstream trade subscriptions for the same symbol");
    }

    [Fact]
    public void UnsubscribeMarketDepth_DelegatesToActiveClient()
    {
        var cfg = new SymbolConfig("SPY", SubscribeDepth: true);
        var id = _sut.SubscribeMarketDepth(cfg);

        _sut.UnsubscribeMarketDepth(id);

        _primaryClient.UnsubscribedDepthIds.Should().Contain(id);
    }

    [Fact]
    public void UnsubscribeMarketDepth_DuplicateSubscribers_OnlyUnsubscribesUpstreamAfterLastRelease()
    {
        var cfg = new SymbolConfig("SPY", SubscribeDepth: true);
        var firstId = _sut.SubscribeMarketDepth(cfg);
        var secondId = _sut.SubscribeMarketDepth(cfg);

        firstId.Should().Be(secondId);

        _sut.UnsubscribeMarketDepth(firstId);
        _primaryClient.UnsubscribedDepthIds.Should().BeEmpty("a duplicate subscriber is still attached to the shared upstream subscription");

        _sut.UnsubscribeMarketDepth(secondId);
        _primaryClient.UnsubscribedDepthIds.Should().ContainSingle().Which.Should().Be(firstId);
    }

    [Fact]
    public void UnsubscribeTrades_DelegatesToActiveClient()
    {
        var cfg = new SymbolConfig("AAPL", SubscribeTrades: true);
        var id = _sut.SubscribeTrades(cfg);

        _sut.UnsubscribeTrades(id);

        _primaryClient.UnsubscribedTradeIds.Should().Contain(id);
    }

    [Fact]
    public void UnsubscribeTrades_DuplicateSubscribers_OnlyUnsubscribesUpstreamAfterLastRelease()
    {
        var cfg = new SymbolConfig("AAPL", SubscribeTrades: true);
        var firstId = _sut.SubscribeTrades(cfg);
        var secondId = _sut.SubscribeTrades(cfg);

        firstId.Should().Be(secondId);

        _sut.UnsubscribeTrades(firstId);
        _primaryClient.UnsubscribedTradeIds.Should().BeEmpty("a duplicate subscriber is still attached to the shared upstream subscription");

        _sut.UnsubscribeTrades(secondId);
        _primaryClient.UnsubscribedTradeIds.Should().ContainSingle().Which.Should().Be(firstId);
    }

    [Fact]
    public void ProviderId_ReturnsCompositeId()
    {
        _sut.ProviderId.Should().Be("failover-test-rule");
    }

    [Fact]
    public void ProviderDisplayName_ShowsActiveProvider()
    {
        _sut.ProviderDisplayName.Should().Contain("primary");
    }

    /// <summary>
    /// Fake IMarketDataClient for testing failover behavior without real connections.
    /// </summary>
    private sealed class FakeMarketDataClient : IMarketDataClient
    {
        private readonly string _id;
        private int _nextSubId = 1;

        public bool ShouldFailConnect { get; set; }
        public bool ShouldCancelConnect { get; set; }
        public int ConnectCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public int DepthSubscribeCallCount { get; private set; }
        public int TradeSubscribeCallCount { get; private set; }
        public Dictionary<string, int> DepthSubscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> TradeSubscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<int> UnsubscribedDepthIds { get; } = new();
        public List<int> UnsubscribedTradeIds { get; } = new();

        public FakeMarketDataClient(string id)
        {
            _id = id;
        }

        public bool IsEnabled => true;
        public string ProviderId => _id;
        public string ProviderDisplayName => $"Fake {_id}";
        public string ProviderDescription => $"Fake provider {_id}";
        public int ProviderPriority => 50;
        public Meridian.Infrastructure.Adapters.Core.ProviderCapabilities ProviderCapabilities
            => Meridian.Infrastructure.Adapters.Core.ProviderCapabilities.Streaming();

        public Task ConnectAsync(CancellationToken ct = default)
        {
            ConnectCallCount++;
            if (ShouldCancelConnect)
                return Task.FromCanceled(ct.IsCancellationRequested ? ct : new CancellationToken(canceled: true));
            if (ShouldFailConnect)
                throw new InvalidOperationException($"Fake connect failure for {_id}");
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            DisconnectCallCount++;
            return Task.CompletedTask;
        }

        public int SubscribeMarketDepth(SymbolConfig cfg)
        {
            DepthSubscribeCallCount++;
            var id = _nextSubId++;
            DepthSubscriptions[cfg.Symbol] = id;
            return id;
        }

        public void UnsubscribeMarketDepth(int subscriptionId)
        {
            UnsubscribedDepthIds.Add(subscriptionId);
        }

        public int SubscribeTrades(SymbolConfig cfg)
        {
            TradeSubscribeCallCount++;
            var id = _nextSubId++;
            TradeSubscriptions[cfg.Symbol] = id;
            return id;
        }

        public void UnsubscribeTrades(int subscriptionId)
        {
            UnsubscribedTradeIds.Add(subscriptionId);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
