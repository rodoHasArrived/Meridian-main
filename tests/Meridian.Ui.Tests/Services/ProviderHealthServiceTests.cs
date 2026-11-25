using FluentAssertions;
using System.Net;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ProviderHealthService"/> — health score calculation,
/// history management, alerting, timer lifecycle, and data models.
/// </summary>
public sealed class ProviderHealthServiceTests : IDisposable
{
    private readonly ProviderHealthService _service = new();
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = ProviderHealthService.Instance;
        var b = ProviderHealthService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── Health History ───────────────────────────────────────────────

    [Fact]
    public void GetHealthHistory_NonExistentProvider_ShouldReturnEmptyList()
    {
        var svc = _service;

        var history = svc.GetHealthHistory("non-existent-provider-" + Guid.NewGuid(), TimeSpan.FromHours(1));

        history.Should().NotBeNull();
        history.Should().BeEmpty();
    }

    // ── Timer Lifecycle ──────────────────────────────────────────────

    [Fact]
    public void StartMonitoring_ShouldNotThrow()
    {
        var svc = _service;

        var act = () => svc.StartMonitoring();

        act.Should().NotThrow();
        svc.StopMonitoring(); // Clean up
    }

    [Fact]
    public void StopMonitoring_ShouldNotThrow()
    {
        var svc = _service;

        var act = () => svc.StopMonitoring();

        act.Should().NotThrow();
    }

    [Fact]
    public void StartAndStopMonitoring_ShouldNotThrow()
    {
        var svc = _service;

        var act = () =>
        {
            svc.StartMonitoring();
            svc.StopMonitoring();
            svc.StartMonitoring();
            svc.StopMonitoring();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task TimerRefresh_SkipsOverlappingTickAndReleasesGuardAfterCompletion()
    {
        using var handler = new GatedHealthHandler();
        using var api = new ApiClientService(new TestClientFactory(handler));
        using var service = new ProviderHealthService(api);
        var first = service.RefreshFromTimerAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.RefreshFromTimerAsync();
        handler.Requests.Should().Be(1, "a later timer tick must not overlap the first request");
        handler.Release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await service.RefreshFromTimerAsync();

        handler.Requests.Should().Be(2, "the guard must be released after the first request finishes");
    }

    private sealed class TestClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task CompareProvidersAsync_UnmeasuredMetricsStayUnavailableInRankingsAndHistory()
    {
        using var handler = new GatedHealthHandler("""
            {
              "providers": [
                {
                  "providerId": "unmeasured",
                  "providerName": "Unmeasured",
                  "isConnected": true,
                  "averageLatencyMs": null,
                  "latencyConsistencyScore": null,
                  "dataCompletenessPercent": null,
                  "reconnectsLastHour": null
                },
                {
                  "providerId": "observed",
                  "providerName": "Observed",
                  "isConnected": true,
                  "averageLatencyMs": 42.5
                }
              ]
            }
            """);
        handler.Release.TrySetResult();
        using var api = new ApiClientService(new TestClientFactory(handler));
        using var service = new ProviderHealthService(api);
        var alerts = new List<HealthAlertEventArgs>();
        service.HealthAlert += (_, args) => alerts.Add(args);

        var comparison = await service.CompareProvidersAsync();

        comparison.Providers.Should().HaveCount(2).And.OnlyContain(provider => provider.OverallScore == null);
        comparison.BestLatency.Should().Be("observed");
        comparison.BestOverall.Should().BeNull();
        comparison.BestCompleteness.Should().BeNull();
        comparison.BestStability.Should().BeNull();
        var history = service.GetHealthHistory("unmeasured", TimeSpan.FromMinutes(1)).Should().ContainSingle().Subject;
        history.LatencyMs.Should().BeNull();
        history.OverallScore.Should().BeNull();
        alerts.Should().BeEmpty();
    }

    private sealed class GatedHealthHandler(string responseBody = "{\"providers\":[]}") : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    // ── Data Model: ProviderHealthData ────────────────────────────────

    [Fact]
    public void ProviderHealthData_ShouldHaveDefaults()
    {
        var data = new ProviderHealthData();
        data.ProviderId.Should().BeEmpty();
        data.ProviderName.Should().BeEmpty();
        data.IsConnected.Should().BeFalse();
        data.OverallScore.Should().BeNull();
        data.Metrics.Should().NotBeNull();
        data.Breakdown.Should().NotBeNull();
    }

    // ── Data Model: HealthMetrics ────────────────────────────────────

    [Fact]
    public void HealthMetrics_ShouldHaveDefaults()
    {
        var metrics = new HealthMetrics();
        metrics.ConnectionStabilityScore.Should().BeNull();
        metrics.AverageLatencyMs.Should().BeNull();
        metrics.LatencyP99Ms.Should().BeNull();
        metrics.DataCompletenessPercent.Should().BeNull();
        metrics.ReconnectsLastHour.Should().BeNull();
        metrics.UptimePercent.Should().BeNull();
        metrics.MessagesPerSecond.Should().BeNull();
        metrics.ErrorsLastHour.Should().BeNull();
    }

    // ── Data Model: HealthScoreBreakdown ─────────────────────────────

    [Fact]
    public void HealthScoreBreakdown_ShouldHaveDefaults()
    {
        var breakdown = new HealthScoreBreakdown();
        breakdown.ConnectionStability.Should().NotBeNull();
        breakdown.LatencyConsistency.Should().NotBeNull();
        breakdown.DataCompleteness.Should().NotBeNull();
        breakdown.ReconnectionFrequency.Should().NotBeNull();
    }

    [Fact]
    public void ScoreComponent_ShouldHaveDefaults()
    {
        var component = new ScoreComponent();
        component.Weight.Should().Be(0);
        component.Score.Should().BeNull();
        component.WeightedScore.Should().BeNull();
    }

    // ── Data Model: FailoverThresholds ───────────────────────────────

    [Fact]
    public void FailoverThresholds_ShouldHaveDefaults()
    {
        var thresholds = new FailoverThresholds();
        thresholds.MinHealthScore.Should().Be(70);
        thresholds.MaxLatencyMs.Should().Be(500);
        thresholds.MaxReconnectsPerHour.Should().Be(5);
        thresholds.MinDataCompletenessPercent.Should().Be(95);
        thresholds.AutoFailoverEnabled.Should().BeTrue();
    }

    // ── Data Model: ProviderHealthComparison ─────────────────────────

    [Fact]
    public void ProviderHealthComparison_ShouldHaveDefaults()
    {
        var comparison = new ProviderHealthComparison();
        comparison.Providers.Should().NotBeNull().And.BeEmpty();
        comparison.BestOverall.Should().BeNull();
        comparison.BestLatency.Should().BeNull();
        comparison.BestCompleteness.Should().BeNull();
        comparison.BestStability.Should().BeNull();
    }

    // ── Data Model: HealthHistoryPoint ───────────────────────────────

    [Fact]
    public void HealthHistoryPoint_ShouldHaveDefaults()
    {
        var point = new HealthHistoryPoint();
        point.OverallScore.Should().BeNull();
        point.LatencyMs.Should().BeNull();
        point.CompletenessPercent.Should().BeNull();
    }

    // ── Data Model: ProviderHealthInfo ───────────────────────────────

    [Fact]
    public void ProviderHealthInfo_ShouldHaveDefaults()
    {
        var info = new ProviderHealthInfo();
        info.ProviderId.Should().BeEmpty();
        info.ProviderName.Should().BeEmpty();
        info.IsConnected.Should().BeFalse();
        info.ConnectionStabilityScore.Should().BeNull();
        info.AverageLatencyMs.Should().BeNull();
    }

    // ── Event Args ───────────────────────────────────────────────────

    [Fact]
    public void HealthUpdateEventArgs_ShouldHaveDefaults()
    {
        var args = new HealthUpdateEventArgs();
        args.Providers.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void HealthAlertEventArgs_ShouldHaveDefaults()
    {
        var args = new HealthAlertEventArgs();
        args.ProviderId.Should().BeEmpty();
        args.ProviderName.Should().BeEmpty();
        args.Message.Should().BeEmpty();
        args.CurrentValue.Should().Be(0);
        args.Threshold.Should().Be(0);
    }

    // ── HealthAlertType Enum ─────────────────────────────────────────

    [Theory]
    [InlineData(HealthAlertType.LowOverallScore)]
    [InlineData(HealthAlertType.HighLatency)]
    [InlineData(HealthAlertType.LowCompleteness)]
    [InlineData(HealthAlertType.FrequentReconnects)]
    [InlineData(HealthAlertType.ConnectionLost)]
    public void HealthAlertType_AllValues_ShouldBeDefined(HealthAlertType type)
    {
        Enum.IsDefined(typeof(HealthAlertType), type).Should().BeTrue();
    }

    public void Dispose()
    {
        // Ensure monitoring is stopped after tests
        _service.Dispose();
    }
}
