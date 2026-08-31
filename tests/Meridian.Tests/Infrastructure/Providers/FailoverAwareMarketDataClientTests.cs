using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Infrastructure.Resilience;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="FailoverAwareMarketDataClient"/>.
/// Tests delegation to active provider, subscription tracking, and provider switching.
/// </summary>
public sealed class FailoverAwareMarketDataClientTests : IAsyncLifetime
{
    // Deadline for synchronization waits (event handshakes, thread joins, monitor-block detection).
    // These waits complete in milliseconds when healthy, so a generous budget does not slow passing
    // runs — but the previous 2-second budget produced load-induced TimeoutExceptions on busy
    // parallel CI runners (#2682). A genuine hang still fails, just with a longer fuse.
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(30);

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
    public async Task ConnectAsync_OnFailure_CommitsBackupThroughCoordinator()
    {
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        _primaryClient.ShouldFailConnect = true;

        await _sut.ConnectAsync();

        _primaryClient.ConnectCallCount.Should().Be(1);
        _backupClient.ConnectCallCount.Should().Be(1);
        _sut.ActiveProviderId.Should().Be("backup");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("backup");
    }

    [Fact]
    public async Task ConnectAsync_WhenPrimaryCouldNotBeConstructed_StartsOnBackupWithoutCoordinatorDivergence()
    {
        await _sut.DisposeAsync();
        var availableBackup = new FakeMarketDataClient("backup");
        _sut = new FailoverAwareMarketDataClient(
            new Dictionary<string, IMarketDataClient>
            {
                ["backup"] = availableBackup
            },
            _failoverService,
            _rule.Id,
            "backup");
        _failoverService.Start(
            new DataSourcesConfig(
                EnableFailover: true,
                HealthCheckIntervalSeconds: 3600,
                FailoverRules: [_rule]),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [_rule.Id] = "backup"
            });

        await _sut.ConnectAsync();

        availableBackup.ConnectCallCount.Should().Be(1);
        _sut.ActiveProviderId.Should().Be("backup");
        _failoverService.GetActiveProviderId(_rule.Id).Should().Be("backup");
        _failoverService.GetRuleSnapshots().Single().IsInFailoverState.Should().BeTrue();
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
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        _primaryClient.ShouldFailConnect = true;
        _backupClient.ShouldFailConnect = true;

        var act = () => _sut.ConnectAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*All streaming providers failed*");
        _sut.ActiveProviderId.Should().Be("primary");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("primary");
    }

    [Fact]
    public async Task Scenario_StartupFeedFailure_CancelledRetryCannotCommitALaterBackup()
    {
        await _sut.DisposeAsync();

        var primary = new FakeMarketDataClient("primary") { ShouldFailConnect = true };
        var rejectedBackup = new FakeMarketDataClient("backup1") { ShouldFailConnect = true };
        var cancelledBackup = new FakeMarketDataClient("backup2") { BlockConnectUntilCancelled = true };
        var rule = new FailoverRuleConfig(
            Id: "connect-cancel-rule",
            PrimaryProviderId: "primary",
            BackupProviderIds: ["backup1", "backup2"],
            FailoverThreshold: 1,
            RecoveryThreshold: 2);
        _failoverService.RegisterProvider("backup1");
        _failoverService.RegisterProvider("backup2");
        _sut = new FailoverAwareMarketDataClient(
            new Dictionary<string, IMarketDataClient>
            {
                ["primary"] = primary,
                ["backup1"] = rejectedBackup,
                ["backup2"] = cancelledBackup
            },
            _failoverService,
            rule.Id,
            "primary");
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [rule]));
        using var cts = new CancellationTokenSource();

        var connectTask = _sut.ConnectAsync(cts.Token);
        await cancelledBackup.ConnectEntered.Task.WaitAsync(SyncTimeout);
        cts.Cancel();

        Func<Task> act = async () => await connectTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        await WaitUntilAsync(() =>
            cancelledBackup.ConnectCancellationObserved &&
            cancelledBackup.DisconnectCallCount == 1);
        rejectedBackup.ConnectCallCount.Should().Be(1);
        cancelledBackup.ConnectCallCount.Should().Be(1);
        cancelledBackup.DisconnectCallCount.Should().Be(1);
        _sut.ActiveProviderId.Should().Be("primary");
        _failoverService.GetActiveProviderId(rule.Id).Should().Be("primary");
        _failoverService.GetRuleSnapshots().Single(x => x.RuleId == rule.Id)
            .FailoverCount.Should().Be(0);
    }

    [Fact]
    public async Task DisconnectAsync_DelegatesToActiveClient()
    {
        await _sut.ConnectAsync();
        await _sut.DisconnectAsync();

        _primaryClient.DisconnectCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ConnectionDiagnostics_TrackCompositeConnectAndDisconnect()
    {
        var observed = new List<WebSocketConnectionDiagnostics>();
        _sut.ConnectionDiagnosticsChanged += observed.Add;

        await _sut.ConnectAsync();
        var connected = _sut.GetConnectionDiagnosticsSnapshot();
        await _sut.DisconnectAsync();
        var disconnected = _sut.GetConnectionDiagnosticsSnapshot();

        connected.ProviderName.Should().Be("Failover (primary)");
        connected.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Connected);
        connected.IsConnected.Should().BeTrue();
        connected.LastConnectedAt.Should().NotBeNull();
        disconnected.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Disconnected);
        disconnected.IsConnected.Should().BeFalse();
        disconnected.LastDisconnectedAt.Should().NotBeNull();
        observed.Should().Contain(snapshot =>
            snapshot.ProviderName == "Failover (primary)" &&
            snapshot.LifecycleState == ProviderConnectionLifecycleState.Connected &&
            snapshot.IsConnected);
        observed.Should().Contain(snapshot =>
            snapshot.ProviderName == "Failover (primary)" &&
            snapshot.LifecycleState == ProviderConnectionLifecycleState.Disconnected &&
            !snapshot.IsConnected);
    }

    [Fact]
    public async Task ConnectionDiagnostics_ForcedFailover_PublishesConnectedActiveProviderSwitch()
    {
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 60,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();

        var switched = new TaskCompletionSource<WebSocketConnectionDiagnostics>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _sut.ConnectionDiagnosticsChanged += snapshot =>
        {
            if (snapshot.ProviderName == "Failover (backup)" && snapshot.IsConnected)
                switched.TrySetResult(snapshot);
        };

        _failoverService.ForceFailover("test-rule", "backup").Should().BeTrue();

        var switchSnapshot = await switched.Task.WaitAsync(SyncTimeout);
        _sut.ActiveProviderId.Should().Be("backup");
        switchSnapshot.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Connected);
        switchSnapshot.IsReconnecting.Should().BeFalse();
        _backupClient.ConnectCallCount.Should().Be(1);
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

    [Fact]
    public async Task Scenario_ProviderFeedInterruption_SelectedBackupConnectFails_TriesNextHealthyProviderWithoutStateDivergence()
    {
        await _sut.DisposeAsync();

        var primary = new FakeMarketDataClient("primary");
        var rejectedBackup = new FakeMarketDataClient("backup1") { ShouldFailConnect = true };
        var healthyBackup = new FakeMarketDataClient("backup2");
        _failoverService.RegisterProvider("backup1");
        _failoverService.RegisterProvider("backup2");
        var rule = new FailoverRuleConfig(
            Id: "fallback-rule",
            PrimaryProviderId: "primary",
            BackupProviderIds: ["backup1", "backup2"],
            FailoverThreshold: 1,
            RecoveryThreshold: 2);
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [rule]));
        _sut = new FailoverAwareMarketDataClient(
            new Dictionary<string, IMarketDataClient>
            {
                ["primary"] = primary,
                ["backup1"] = rejectedBackup,
                ["backup2"] = healthyBackup
            },
            _failoverService,
            rule.Id,
            "primary");
        await _sut.ConnectAsync();

        _failoverService.RecordFailure("primary", "injected feed interruption");

        await WaitUntilAsync(
            () => _sut.ActiveProviderId == "backup2" &&
                  _failoverService.GetActiveProviderId(rule.Id) == "backup2");

        rejectedBackup.ConnectCallCount.Should().Be(1);
        healthyBackup.ConnectCallCount.Should().Be(1);
        primary.DisconnectCallCount.Should().Be(1);
        _sut.ActiveProviderId.Should().Be(_failoverService.GetActiveProviderId(rule.Id));
    }

    [Fact]
    public async Task Scenario_Failover_RequiredResubscribeFails_DoesNotClaimConnectedSwitch()
    {
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();
        _sut.SubscribeTrades(new SymbolConfig("AAPL", SubscribeTrades: true)).Should().BePositive();
        _backupClient.ShouldFailTradeSubscribe = true;

        var switched = await _failoverService.ForceFailoverAsync("test-rule", "backup");

        switched.Should().BeFalse();
        _sut.ActiveProviderId.Should().Be("primary");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("primary");
        _primaryClient.DisconnectCallCount.Should().Be(0);
        _backupClient.DisconnectCallCount.Should().Be(1);
        _sut.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_Failover_SubscriptionAddedDuringHandoff_IsAppliedExactlyOnceToNewProvider()
    {
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();
        _sut.SubscribeTrades(new SymbolConfig("AAPL", SubscribeTrades: true)).Should().BePositive();
        _backupClient.BlockTradeSubscribeSymbol = "AAPL";

        var transitionTask = Task.Run(
            async () => await _failoverService.ForceFailoverAsync("test-rule", "backup"));
        await _backupClient.TradeSubscribeBlocked.Task.WaitAsync(SyncTimeout);

        using var addStarted = new ManualResetEventSlim(initialState: false);
        using var addFinished = new ManualResetEventSlim(initialState: false);
        Exception? addException = null;
        var addedHandle = -1;
        var addThread = new Thread(() =>
        {
            addStarted.Set();
            try
            {
                addedHandle = _sut.SubscribeTrades(new SymbolConfig("MSFT", SubscribeTrades: true));
            }
            catch (Exception ex)
            {
                addException = ex;
            }
            finally
            {
                addFinished.Set();
            }
        })
        {
            IsBackground = true
        };
        addThread.Start();
        addStarted.Wait(SyncTimeout).Should().BeTrue();
        try
        {
            WaitUntilBlockedOnMonitor(addThread);
            addFinished.IsSet.Should().BeFalse(
                "subscription mutation must wait until the provider hand-off commits");
        }
        finally
        {
            _backupClient.ReleaseBlockedTradeSubscribe();
        }

        (await transitionTask).Should().BeTrue();
        addThread.Join(SyncTimeout).Should().BeTrue();
        addException.Should().BeNull();
        addedHandle.Should().BePositive();
        _backupClient.TradeSubscriptions.Keys.Should().BeEquivalentTo(["AAPL", "MSFT"]);
        _backupClient.TradeSubscribeCallsBySymbol["AAPL"].Should().Be(1);
        _backupClient.TradeSubscribeCallsBySymbol["MSFT"].Should().Be(1);
        _sut.ActiveProviderId.Should().Be("backup");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("backup");
    }

    [Fact]
    public async Task Scenario_Failover_SubscriptionRemovedDuringHandoff_IsNotResurrected()
    {
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();
        var subscriptionHandle = _sut.SubscribeTrades(
            new SymbolConfig("AAPL", SubscribeTrades: true));
        _backupClient.SubscribeTrades(
            new SymbolConfig("PREEXISTING", SubscribeTrades: true)).Should().BePositive();
        _backupClient.BlockTradeSubscribeSymbol = "AAPL";

        var transitionTask = Task.Run(
            async () => await _failoverService.ForceFailoverAsync("test-rule", "backup"));
        await _backupClient.TradeSubscribeBlocked.Task.WaitAsync(SyncTimeout);

        using var removeStarted = new ManualResetEventSlim(initialState: false);
        using var removeFinished = new ManualResetEventSlim(initialState: false);
        Exception? removeException = null;
        var removeThread = new Thread(() =>
        {
            removeStarted.Set();
            try
            {
                _sut.UnsubscribeTrades(subscriptionHandle);
            }
            catch (Exception ex)
            {
                removeException = ex;
            }
            finally
            {
                removeFinished.Set();
            }
        })
        {
            IsBackground = true
        };
        removeThread.Start();
        removeStarted.Wait(SyncTimeout).Should().BeTrue();
        try
        {
            WaitUntilBlockedOnMonitor(removeThread);
            removeFinished.IsSet.Should().BeFalse(
                "subscription removal must wait until replacement subscription IDs are committed");
        }
        finally
        {
            _backupClient.ReleaseBlockedTradeSubscribe();
        }

        (await transitionTask).Should().BeTrue();
        removeThread.Join(SyncTimeout).Should().BeTrue();
        removeException.Should().BeNull();
        _backupClient.TradeSubscriptions.Should().NotContainKey("AAPL");
        _backupClient.TradeSubscriptions.Should().ContainKey("PREEXISTING");
        _backupClient.UnsubscribedTradeIds.Should().ContainSingle()
            .Which.Should().NotBe(subscriptionHandle,
                "the wrapper handle must resolve to the replacement provider's upstream ID");
        _sut.GetConnectionDiagnosticsSnapshot().ActiveSubscriptions.Should().Be(0);
        _sut.ActiveProviderId.Should().Be("backup");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("backup");
    }

    [Fact]
    public async Task Scenario_ForcedFailover_DisclosesSwitchOnTapePerAffectedSymbol()
    {
        await _sut.DisposeAsync();
        var publisher = new TestMarketEventPublisher();
        _sut = new FailoverAwareMarketDataClient(
            _providers, _failoverService, "test-rule", "primary", integrityPublisher: publisher);
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();
        _sut.SubscribeTrades(new SymbolConfig("AAPL", SubscribeTrades: true)).Should().BePositive();
        _sut.SubscribeMarketDepth(new SymbolConfig("SPY", SubscribeDepth: true)).Should().BePositive();
        var switchStartedAfter = DateTimeOffset.UtcNow;

        (await _failoverService.ForceFailoverAsync("test-rule", "backup")).Should().BeTrue();

        // The markers publish just after the hand-off commits; the transition task may resolve first.
        await WaitUntilAsync(() => publisher.PublishedEvents.Count >= 2);
        var markers = publisher.PublishedEvents
            .Where(e => e.Type == MarketEventType.Integrity)
            .ToList();
        markers.Select(e => e.Symbol).Should().BeEquivalentTo(["AAPL", "SPY"]);
        markers.Should().OnlyContain(e => e.Source == "primary",
            "the coverage-uncertain window belongs to the provider that lost the feed");
        var integrity = markers[0].Payload.Should().BeOfType<IntegrityEvent>().Subject;
        integrity.ErrorCode.Should().Be(1010);
        integrity.Description.Should().Contain("'primary' -> 'backup'");
        integrity.Description.Should().Contain("coverage uncertain");
        integrity.Timestamp.Should().BeOnOrAfter(switchStartedAfter,
            "the marker closes the window at resubscribe-complete time");
    }

    [Fact]
    public async Task Scenario_ForcedFailover_NoSubscriptions_DisclosesSwitchWithSystemMarker()
    {
        await _sut.DisposeAsync();
        var publisher = new TestMarketEventPublisher();
        _sut = new FailoverAwareMarketDataClient(
            _providers, _failoverService, "test-rule", "primary", integrityPublisher: publisher);
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();

        (await _failoverService.ForceFailoverAsync("test-rule", "backup")).Should().BeTrue();

        await WaitUntilAsync(() => publisher.PublishedEvents.Count >= 1);
        var marker = publisher.PublishedEvents.Should().ContainSingle().Subject;
        marker.Type.Should().Be(MarketEventType.Integrity);
        marker.Symbol.Should().Be("SYSTEM");
        marker.Source.Should().Be("primary");
    }

    [Fact]
    public async Task Scenario_ForcedFailover_MarkerPublishFailure_DoesNotBreakSwitch()
    {
        await _sut.DisposeAsync();
        var publisher = new ThrowingMarketEventPublisher();
        _sut = new FailoverAwareMarketDataClient(
            _providers, _failoverService, "test-rule", "primary", integrityPublisher: publisher);
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();
        _sut.SubscribeTrades(new SymbolConfig("AAPL", SubscribeTrades: true)).Should().BePositive();

        (await _failoverService.ForceFailoverAsync("test-rule", "backup")).Should().BeTrue();

        _sut.ActiveProviderId.Should().Be("backup");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("backup");
        // The hand-off must continue past the failed disclosure and release the old provider.
        await WaitUntilAsync(() => _primaryClient.DisconnectCallCount == 1);
        publisher.Attempts.Should().BeGreaterThan(0, "the publish must actually have been attempted");
        _backupClient.TradeSubscriptions.Should().ContainKey("AAPL");
    }

    [Fact]
    public async Task Scenario_FeedShutdown_DuringProviderSwitch_CancelsSwitchBeforeDisposingProviders()
    {
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();
        _backupClient.BlockConnectUntilCancelled = true;

        var transition = _failoverService.ForceFailoverAsync("test-rule", "backup");
        await _backupClient.ConnectEntered.Task.WaitAsync(SyncTimeout);

        await _sut.DisposeAsync();

        (await transition).Should().BeFalse();
        _backupClient.ConnectCancellationObserved.Should().BeTrue();
        _primaryClient.DisposeCount.Should().Be(1);
        _backupClient.DisposeCount.Should().Be(1);
        _sut.ActiveProviderId.Should().Be("primary");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("primary");
    }

    [Fact]
    public async Task Scenario_CoordinatorShutdown_DuringProviderSwitch_RejectsWithoutStateDivergence()
    {
        _failoverService.Start(new DataSourcesConfig(
            EnableFailover: true,
            HealthCheckIntervalSeconds: 3600,
            FailoverRules: [_rule]));
        await _sut.ConnectAsync();
        _backupClient.BlockConnectUntilCancelled = true;

        var transition = _failoverService.ForceFailoverAsync("test-rule", "backup");
        await _backupClient.ConnectEntered.Task.WaitAsync(SyncTimeout);

        _failoverService.Dispose();

        (await transition.WaitAsync(SyncTimeout)).Should().BeFalse();
        await WaitUntilAsync(() => _backupClient.ConnectCancellationObserved);
        _sut.ActiveProviderId.Should().Be("primary");
        _failoverService.GetActiveProviderId("test-rule").Should().Be("primary");
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DisposesEachProviderExactlyOnce()
    {
        await _sut.DisposeAsync();
        await _sut.DisposeAsync();

        _primaryClient.DisposeCount.Should().Be(1);
        _backupClient.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_AliasedProviderInstance_DisposesSharedInstanceExactlyOnce()
    {
        await _sut.DisposeAsync();
        var sharedProvider = new FakeMarketDataClient("shared");
        _sut = new FailoverAwareMarketDataClient(
            new Dictionary<string, IMarketDataClient>
            {
                ["primary"] = sharedProvider,
                ["backup"] = sharedProvider
            },
            _failoverService,
            "test-rule",
            "primary");

        await _sut.DisposeAsync();

        sharedProvider.DisposeCount.Should().Be(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(SyncTimeout);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static void WaitUntilBlockedOnMonitor(Thread thread)
    {
        SpinWait.SpinUntil(
                () => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                SyncTimeout)
            .Should().BeTrue("the subscription mutation should reach the hand-off synchronization gate");
    }

    /// <summary>
    /// Fake IMarketDataClient for testing failover behavior without real connections.
    /// </summary>
    private sealed class FakeMarketDataClient : IMarketDataClient
    {
        private readonly string _id;
        private int _nextSubId = 1;
        private ProviderConnectionLifecycleState _lifecycleState = ProviderConnectionLifecycleState.Configured;
        private DateTimeOffset? _lastConnectedAt;
        private DateTimeOffset? _lastDisconnectedAt;

        public bool ShouldFailConnect { get; set; }
        public bool ShouldCancelConnect { get; set; }
        public bool BlockConnectUntilCancelled { get; set; }
        public bool ShouldFailDepthSubscribe { get; set; }
        public bool ShouldFailTradeSubscribe { get; set; }
        public bool ConnectCancellationObserved { get; private set; }
        public int ConnectCallCount { get; private set; }
        public int DisconnectCallCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int DepthSubscribeCallCount { get; private set; }
        public int TradeSubscribeCallCount { get; private set; }
        public Dictionary<string, int> DepthSubscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> TradeSubscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> TradeSubscribeCallsBySymbol { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<int> UnsubscribedDepthIds { get; } = new();
        public List<int> UnsubscribedTradeIds { get; } = new();
        public string? BlockTradeSubscribeSymbol { get; set; }
        public TaskCompletionSource TradeSubscribeBlocked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ManualResetEventSlim BlockedTradeSubscribeRelease { get; } = new(initialState: false);

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

        public event Action<WebSocketConnectionDiagnostics>? ConnectionDiagnosticsChanged;

        public WebSocketConnectionDiagnostics GetConnectionDiagnosticsSnapshot()
            => new(
                ProviderName: ProviderDisplayName,
                LifecycleState: _lifecycleState,
                WebSocketState: System.Net.WebSockets.WebSocketState.None,
                IsConnected: _lifecycleState == ProviderConnectionLifecycleState.Connected,
                IsReconnecting: false,
                ReconnectAttempts: 0,
                LastConnectedAt: _lastConnectedAt,
                LastDisconnectedAt: _lastDisconnectedAt,
                LastHeartbeatReceivedAt: null,
                LastMessageReceivedAt: null,
                LastReconnectAttemptAt: null,
                LastError: null,
                LastFailureKind: null,
                ConnectionAge: _lastConnectedAt.HasValue && _lifecycleState == ProviderConnectionLifecycleState.Connected
                    ? DateTimeOffset.UtcNow - _lastConnectedAt.Value
                    : null,
                IdleDuration: null,
                ActiveSubscriptions: DepthSubscriptions.Count + TradeSubscriptions.Count);

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            ConnectCallCount++;
            ConnectEntered.TrySetResult();
            if (BlockConnectUntilCancelled)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    ConnectCancellationObserved = true;
                    throw;
                }
            }
            if (ShouldCancelConnect)
                await Task.FromCanceled(ct.IsCancellationRequested ? ct : new CancellationToken(canceled: true));
            if (ShouldFailConnect)
                throw new InvalidOperationException($"Fake connect failure for {_id}");
            _lifecycleState = ProviderConnectionLifecycleState.Connected;
            _lastConnectedAt = DateTimeOffset.UtcNow;
            ConnectionDiagnosticsChanged?.Invoke(GetConnectionDiagnosticsSnapshot());
        }

        public TaskCompletionSource ConnectEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            DisconnectCallCount++;
            _lifecycleState = ProviderConnectionLifecycleState.Disconnected;
            _lastDisconnectedAt = DateTimeOffset.UtcNow;
            ConnectionDiagnosticsChanged?.Invoke(GetConnectionDiagnosticsSnapshot());
            return Task.CompletedTask;
        }

        public int SubscribeMarketDepth(SymbolConfig cfg)
        {
            DepthSubscribeCallCount++;
            if (ShouldFailDepthSubscribe)
                throw new InvalidOperationException($"Fake depth subscription failure for {_id}");
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
            TradeSubscribeCallsBySymbol[cfg.Symbol] =
                TradeSubscribeCallsBySymbol.GetValueOrDefault(cfg.Symbol) + 1;
            if (string.Equals(cfg.Symbol, BlockTradeSubscribeSymbol, StringComparison.OrdinalIgnoreCase))
            {
                TradeSubscribeBlocked.TrySetResult();
                if (!BlockedTradeSubscribeRelease.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException($"Timed out waiting to release trade subscription for {cfg.Symbol}.");
            }
            if (ShouldFailTradeSubscribe)
                throw new InvalidOperationException($"Fake trade subscription failure for {_id}");
            var id = _nextSubId++;
            TradeSubscriptions[cfg.Symbol] = id;
            return id;
        }

        public void UnsubscribeTrades(int subscriptionId)
        {
            UnsubscribedTradeIds.Add(subscriptionId);
            var symbol = TradeSubscriptions.FirstOrDefault(entry => entry.Value == subscriptionId).Key;
            if (symbol is not null)
                TradeSubscriptions.Remove(symbol);
        }

        public void ReleaseBlockedTradeSubscribe()
            => BlockedTradeSubscribeRelease.Set();

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (DisposeCount == 1)
                BlockedTradeSubscribeRelease.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
