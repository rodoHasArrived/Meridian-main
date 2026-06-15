using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ProviderHealthService"/> — health score calculation,
/// history management, alerting, timer lifecycle, and data models.
/// </summary>
public sealed class ProviderHealthServiceTests : IDisposable
{
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
        var svc = ProviderHealthService.Instance;

        var history = svc.GetHealthHistory("non-existent-provider-" + Guid.NewGuid(), TimeSpan.FromHours(1));

        history.Should().NotBeNull();
        history.Should().BeEmpty();
    }

    // ── Timer Lifecycle ──────────────────────────────────────────────

    [Fact]
    public void StartMonitoring_ShouldNotThrow()
    {
        var svc = ProviderHealthService.Instance;

        var act = () => svc.StartMonitoring();

        act.Should().NotThrow();
        svc.StopMonitoring(); // Clean up
    }

    [Fact]
    public void StopMonitoring_ShouldNotThrow()
    {
        var svc = ProviderHealthService.Instance;

        var act = () => svc.StopMonitoring();

        act.Should().NotThrow();
    }

    [Fact]
    public void StartAndStopMonitoring_ShouldNotThrow()
    {
        var svc = ProviderHealthService.Instance;

        svc.StartMonitoring();
        svc.StopMonitoring();
        svc.StartMonitoring();
        svc.StopMonitoring();
    }

    // ── Data Model: ProviderHealthData ────────────────────────────────

    [Fact]
    public void ProviderHealthData_ShouldHaveDefaults()
    {
        var data = new ProviderHealthData();
        data.ProviderId.Should().BeEmpty();
        data.ProviderName.Should().BeEmpty();
        data.IsConnected.Should().BeFalse();
        data.OverallScore.Should().Be(0);
        data.Metrics.Should().NotBeNull();
        data.Breakdown.Should().NotBeNull();
    }

    // ── Data Model: HealthMetrics ────────────────────────────────────

    [Fact]
    public void HealthMetrics_ShouldHaveDefaults()
    {
        var metrics = new HealthMetrics();
        metrics.ConnectionStabilityScore.Should().Be(0);
        metrics.AverageLatencyMs.Should().Be(0);
        metrics.LatencyP99Ms.Should().Be(0);
        metrics.DataCompletenessPercent.Should().Be(0);
        metrics.ReconnectsLastHour.Should().Be(0);
        metrics.UptimePercent.Should().Be(0);
        metrics.MessagesPerSecond.Should().Be(0);
        metrics.ErrorsLastHour.Should().Be(0);
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
        component.Score.Should().Be(0);
        component.WeightedScore.Should().Be(0);
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
        point.OverallScore.Should().Be(0);
        point.LatencyMs.Should().Be(0);
        point.CompletenessPercent.Should().Be(0);
    }

    // ── Data Model: ProviderHealthInfo ───────────────────────────────

    [Fact]
    public void ProviderHealthInfo_ShouldHaveDefaults()
    {
        var info = new ProviderHealthInfo();
        info.ProviderId.Should().BeEmpty();
        info.ProviderName.Should().BeEmpty();
        info.IsConnected.Should().BeFalse();
        info.ConnectionStabilityScore.Should().Be(0);
        info.AverageLatencyMs.Should().Be(0);
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


    [Fact]
    public async Task GetAllProviderHealthAsync_SlowEndpoint_CoalescesHealthUpdatedPerRefresh()
    {
        var service = new ProviderHealthService(async ct =>
        {
            await Task.Delay(50, ct);
            return CreateHealthResponse("slow-feed");
        });
        var events = 0;
        service.HealthUpdated += (_, args) =>
        {
            events++;
            args.Providers.Should().ContainSingle(p => p.ProviderId == "slow-feed");
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var providers = await service.GetAllProviderHealthAsync(timeout.Token);

        providers.Should().ContainSingle(p => p.ProviderId == "slow-feed");
        events.Should().Be(1);
        service.Dispose();
    }

    [Fact]
    public async Task MonitoringRefresh_StopMonitoring_CancelsOutstandingHealthCheck()
    {
        using var entered = new ManualResetEventSlim(false);
        var service = new ProviderHealthService(async ct =>
        {
            entered.Set();
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            return CreateHealthResponse("cancelled-feed");
        });

        service.StartMonitoring();
        var refresh = service.TriggerMonitoringRefreshForTestsAsync();
        entered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        service.StopMonitoring();

        await refresh;
        service.Dispose();
    }

    [Fact]
    public async Task MonitoringRefresh_Dispose_CancelsOutstandingHealthCheck()
    {
        using var entered = new ManualResetEventSlim(false);
        var service = new ProviderHealthService(async ct =>
        {
            entered.Set();
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            return CreateHealthResponse("disposed-feed");
        });

        service.StartMonitoring();
        var refresh = service.TriggerMonitoringRefreshForTestsAsync();
        entered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        service.Dispose();

        await refresh;
    }

    [Fact]
    public async Task GetAllProviderHealthAsync_ConcurrentRefreshes_DoNotOverlapEndpointCalls()
    {
        var activeCalls = 0;
        var maxActiveCalls = 0;
        var service = new ProviderHealthService(async ct =>
        {
            var active = Interlocked.Increment(ref activeCalls);
            maxActiveCalls = Math.Max(maxActiveCalls, active);
            try
            {
                await Task.Delay(50, ct);
                return CreateHealthResponse("serial-feed");
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(
            service.GetAllProviderHealthAsync(timeout.Token),
            service.GetAllProviderHealthAsync(timeout.Token),
            service.GetAllProviderHealthAsync(timeout.Token));

        maxActiveCalls.Should().Be(1);
        service.Dispose();
    }

    private static ApiResponse<ProviderHealthResponse> CreateHealthResponse(string providerId)
    {
        return new ApiResponse<ProviderHealthResponse>
        {
            Success = true,
            StatusCode = 200,
            Data = new ProviderHealthResponse
            {
                Providers =
                [
                    new ProviderHealthInfo
                    {
                        ProviderId = providerId,
                        ProviderName = providerId,
                        IsConnected = true,
                        ConnectionStabilityScore = 99,
                        AverageLatencyMs = 25,
                        LatencyP99Ms = 40,
                        LatencyConsistencyScore = 95,
                        DataCompletenessPercent = 99,
                        ReconnectsLastHour = 0,
                        UptimePercent = 99,
                        MessagesPerSecond = 1000,
                        ErrorsLastHour = 0
                    }
                ]
            }
        };
    }

    public void Dispose()
    {
        // Ensure monitoring is stopped after tests
        ProviderHealthService.Instance.StopMonitoring();
    }
}
