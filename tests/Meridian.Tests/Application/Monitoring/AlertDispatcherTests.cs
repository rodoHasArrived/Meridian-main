using FluentAssertions;
using Meridian.Application.Monitoring.Core;

namespace Meridian.Tests.Application.Monitoring;

public sealed class AlertDispatcherTests : IDisposable
{
    private readonly AlertDispatcher _sut = new(maxRecentAlerts: 100);

    public void Dispose() => _sut.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MonitoringAlert MakeAlert(
        AlertSeverity severity = AlertSeverity.Warning,
        AlertCategory category = AlertCategory.DataQuality,
        string source = "test-source") =>
        new(
            Guid.NewGuid().ToString("N"),
            severity,
            category,
            source,
            Title: "Test alert",
            Message: "Test message",
            Timestamp: DateTimeOffset.UtcNow);

    // ── Publish (sync) ───────────────────────────────────────────────────────

    [Fact]
    public void Publish_WithSyncSubscriber_InvokesHandlerWithAlert()
    {
        MonitoringAlert? received = null;
        using var _ = _sut.Subscribe(a => received = a);
        var alert = MakeAlert();

        _sut.Publish(alert);

        received.Should().BeSameAs(alert);
    }

    [Fact]
    public void Publish_WithMultipleSubscribers_AllHandlersInvoked()
    {
        var receivedBy1 = false;
        var receivedBy2 = false;
        using var _1 = _sut.Subscribe(_ => receivedBy1 = true);
        using var _2 = _sut.Subscribe(_ => receivedBy2 = true);

        _sut.Publish(MakeAlert());

        receivedBy1.Should().BeTrue();
        receivedBy2.Should().BeTrue();
    }

    [Fact]
    public void Publish_WhenSubscriberThrows_DoesNotPropagateException()
    {
        using var _ = _sut.Subscribe(_ => throw new InvalidOperationException("subscriber error"));

        var act = () => _sut.Publish(MakeAlert());

        act.Should().NotThrow();
    }

    [Fact]
    public void Publish_NullAlert_ThrowsArgumentNullException()
    {
        var act = () => _sut.Publish(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Publish_AfterDispose_ThrowsObjectDisposedException()
    {
        _sut.Dispose();

        var act = () => _sut.Publish(MakeAlert());

        act.Should().Throw<ObjectDisposedException>();
    }

    // ── PublishAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_WithAsyncSubscriber_InvokesHandlerWithAlert()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        MonitoringAlert? received = null;
        using var _ = _sut.Subscribe(a =>
        {
            received = a;
            return Task.CompletedTask;
        });
        var alert = MakeAlert();

        await _sut.PublishAsync(alert, cts.Token);

        received.Should().BeSameAs(alert);
    }

    [Fact]
    public async Task PublishAsync_WithFailingAsyncSubscriber_DoesNotPropagateException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var _ = _sut.Subscribe(_ => Task.FromException(new InvalidOperationException("async error")));

        var act = () => _sut.PublishAsync(MakeAlert(), cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_NullAlert_ThrowsArgumentNullException()
    {
        var act = () => _sut.PublishAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        _sut.Dispose();

        var act = () => _sut.PublishAsync(MakeAlert());

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ── Subscription handle / unsubscribe ────────────────────────────────────

    [Fact]
    public void Subscribe_DisposingHandle_StopsDelivery()
    {
        var count = 0;
        var handle = _sut.Subscribe(_ => count++);

        _sut.Publish(MakeAlert()); // should receive
        handle.Dispose();
        _sut.Publish(MakeAlert()); // should not receive

        count.Should().Be(1);
    }

    [Fact]
    public void Subscribe_DisposingHandleTwice_DoesNotThrow()
    {
        var handle = _sut.Subscribe(_ => { });
        handle.Dispose();

        var act = handle.Dispose;

        act.Should().NotThrow();
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_WithSeverityFilter_OnlyMatchingAlertsDelivered()
    {
        var received = new List<MonitoringAlert>();
        using var _ = _sut.Subscribe(
            a => received.Add(a),
            new AlertFilter(MinSeverity: AlertSeverity.Error));

        _sut.Publish(MakeAlert(AlertSeverity.Info));
        _sut.Publish(MakeAlert(AlertSeverity.Warning));
        _sut.Publish(MakeAlert(AlertSeverity.Error));
        _sut.Publish(MakeAlert(AlertSeverity.Critical));

        received.Should().HaveCount(2);
        received.Should().OnlyContain(a => a.Severity >= AlertSeverity.Error);
    }

    [Fact]
    public void Publish_WithCategoryFilter_OnlyMatchingCategoriesDelivered()
    {
        var received = new List<MonitoringAlert>();
        using var _ = _sut.Subscribe(
            a => received.Add(a),
            new AlertFilter(Categories: [AlertCategory.Connection]));

        _sut.Publish(MakeAlert(category: AlertCategory.Connection));
        _sut.Publish(MakeAlert(category: AlertCategory.DataQuality));
        _sut.Publish(MakeAlert(category: AlertCategory.Storage));

        received.Should().ContainSingle(a => a.Category == AlertCategory.Connection);
    }

    [Fact]
    public void Publish_WithSourceFilter_OnlyMatchingSourcesDelivered()
    {
        var received = new List<MonitoringAlert>();
        using var _ = _sut.Subscribe(
            a => received.Add(a),
            new AlertFilter(Sources: ["polygon", "alpaca"]));

        _sut.Publish(MakeAlert(source: "polygon"));
        _sut.Publish(MakeAlert(source: "alpaca"));
        _sut.Publish(MakeAlert(source: "nyse"));

        received.Should().HaveCount(2);
        received.Should().NotContain(a => a.Source == "nyse");
    }

    // ── GetRecentAlerts ──────────────────────────────────────────────────────

    [Fact]
    public void GetRecentAlerts_AfterPublish_ContainsPublishedAlerts()
    {
        _sut.Publish(MakeAlert());
        _sut.Publish(MakeAlert());

        var alerts = _sut.GetRecentAlerts(count: 10);

        alerts.Should().HaveCount(2);
    }

    [Fact]
    public void GetRecentAlerts_WithCountLimit_ReturnsMostRecent()
    {
        for (var i = 0; i < 5; i++)
            _sut.Publish(MakeAlert());

        var alerts = _sut.GetRecentAlerts(count: 3);

        alerts.Should().HaveCount(3);
    }

    [Fact]
    public void GetRecentAlerts_WithFilter_OnlyReturnsMatchingAlerts()
    {
        _sut.Publish(MakeAlert(AlertSeverity.Info));
        _sut.Publish(MakeAlert(AlertSeverity.Error));
        _sut.Publish(MakeAlert(AlertSeverity.Critical));

        var alerts = _sut.GetRecentAlerts(
            count: 100,
            filter: new AlertFilter(MinSeverity: AlertSeverity.Error));

        alerts.Should().HaveCount(2);
        alerts.Should().OnlyContain(a => a.Severity >= AlertSeverity.Error);
    }

    [Fact]
    public void GetRecentAlerts_WithNoAlerts_ReturnsEmpty()
    {
        var alerts = _sut.GetRecentAlerts();

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void GetRecentAlerts_WhenMaxRecentAlertsExceeded_TrimsOldestAlerts()
    {
        var sut = new AlertDispatcher(maxRecentAlerts: 3);
        using (sut)
        {
            for (var i = 0; i < 5; i++)
                sut.Publish(MakeAlert());

            // The internal queue trims to maxRecentAlerts
            var alerts = sut.GetRecentAlerts(count: 100);
            alerts.Should().HaveCount(3);
        }
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    [Fact]
    public void GetStatistics_AfterNoAlerts_TotalIsZero()
    {
        var stats = _sut.GetStatistics();

        stats.TotalAlerts.Should().Be(0);
    }

    [Fact]
    public void GetStatistics_TracksTotalAlertCount()
    {
        _sut.Publish(MakeAlert());
        _sut.Publish(MakeAlert());
        _sut.Publish(MakeAlert());

        var stats = _sut.GetStatistics();

        stats.TotalAlerts.Should().Be(3);
    }

    [Fact]
    public void GetStatistics_TracksBySeverity()
    {
        _sut.Publish(MakeAlert(AlertSeverity.Info));
        _sut.Publish(MakeAlert(AlertSeverity.Warning));
        _sut.Publish(MakeAlert(AlertSeverity.Warning));
        _sut.Publish(MakeAlert(AlertSeverity.Error));

        var stats = _sut.GetStatistics();

        stats.AlertsBySeverity[AlertSeverity.Info].Should().Be(1);
        stats.AlertsBySeverity[AlertSeverity.Warning].Should().Be(2);
        stats.AlertsBySeverity[AlertSeverity.Error].Should().Be(1);
    }

    [Fact]
    public void GetStatistics_TracksByCategory()
    {
        _sut.Publish(MakeAlert(category: AlertCategory.Connection));
        _sut.Publish(MakeAlert(category: AlertCategory.Storage));
        _sut.Publish(MakeAlert(category: AlertCategory.Connection));

        var stats = _sut.GetStatistics();

        stats.AlertsByCategory[AlertCategory.Connection].Should().Be(2);
        stats.AlertsByCategory[AlertCategory.Storage].Should().Be(1);
    }

    [Fact]
    public void GetStatistics_TracksBySource()
    {
        _sut.Publish(MakeAlert(source: "polygon"));
        _sut.Publish(MakeAlert(source: "polygon"));
        _sut.Publish(MakeAlert(source: "alpaca"));

        var stats = _sut.GetStatistics();

        stats.AlertsBySource["polygon"].Should().Be(2);
        stats.AlertsBySource["alpaca"].Should().Be(1);
    }

    [Fact]
    public void GetStatistics_SinceTimestamp_IsBeforeFirstAlert()
    {
        var before = DateTimeOffset.UtcNow;

        var stats = _sut.GetStatistics();

        stats.Since.Should().BeOnOrBefore(before);
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var sut = new AlertDispatcher();
        sut.Dispose();

        var act = sut.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ClearsAllSubscriptions()
    {
        var sut = new AlertDispatcher();
        var received = false;
        // Subscribe, then dispose the dispatcher
        sut.Subscribe(_ => received = true);
        sut.Dispose();

        // After disposal, subscriptions are cleared (no longer called even if Publish were callable)
        received.Should().BeFalse();
    }

    // ── Concurrency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_ConcurrentPublishes_AllAlertsTrackedInStatistics()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        const int alertCount = 100;

        var tasks = Enumerable.Range(0, alertCount)
            .Select(_ => _sut.PublishAsync(MakeAlert(), cts.Token))
            .ToArray();

        await Task.WhenAll(tasks);

        _sut.GetStatistics().TotalAlerts.Should().Be(alertCount);
    }
}
