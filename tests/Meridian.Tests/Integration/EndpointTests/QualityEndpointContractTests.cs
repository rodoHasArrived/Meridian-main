using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.DataQuality;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Contracts.Api;
using Meridian.Contracts.Api.Quality;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Locks the quality endpoint payloads that the desktop dashboard currently consumes.
/// </summary>
public sealed class QualityEndpointContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task QualityDashboard_ReturnsStableDashboardContract()
    {
        var _r = await CreateHostAsync();
        await using var host = _r.App;
        using var client = host.GetTestClient();
        var observedAt = new DateTimeOffset(2026, 03, 20, 14, 05, 00, TimeSpan.Zero);
        _r.QualityService.SequenceTracker.RecordError(CreateSequenceGapError(observedAt, actualSequence: 10));
        _r.QualityService.SequenceTracker.RecordError(CreateSequenceGapError(observedAt.AddSeconds(1), actualSequence: 20));

        var response = await client.GetAsync(UiApiRoutes.QualityDashboard);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<QualityDashboardResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.RealTimeMetrics.SymbolHealth.Should().NotBeEmpty();
        payload.RecentGaps.Should().NotBeEmpty();
        payload.RecentAnomalies.Should().NotBeEmpty();
        payload.AnomalyStats.UnacknowledgedCount.Should().BeGreaterThan(0);
        payload.SequenceStats.TotalErrors.Should().Be(1);
        payload.SequenceStats.RetainedTotalErrors.Should().Be(1);
        payload.SequenceStats.LifetimeTotalErrors.Should().Be(2);
        payload.SequenceStats.RetainedErrorRate.Should().Be(payload.SequenceStats.ErrorRate);
        payload.SequenceStats.LifetimeErrorRate.Should().BeGreaterThan(payload.SequenceStats.RetainedErrorRate);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("realTimeMetrics", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("recentGaps", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("recentAnomalies", out _).Should().BeTrue();
        var sequenceStats = json.RootElement.GetProperty("sequenceStats");
        sequenceStats.GetProperty("totalErrors").GetInt64().Should().Be(1);
        sequenceStats.GetProperty("retainedTotalErrors").GetInt64().Should().Be(1);
        sequenceStats.GetProperty("lifetimeTotalErrors").GetInt64().Should().Be(2);
        sequenceStats.TryGetProperty("retainedErrorRate", out _).Should().BeTrue();
        sequenceStats.TryGetProperty("lifetimeErrorRate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task QualityDashboard_AndContextualRemediation_ExposeAdditiveCompositeContract()
    {
        var composite = StubCompositeQualityService.Create();
        var remediation = new StubGapRemediationService();
        var _r = await CreateHostAsync(composite, remediation);
        await using var host = _r.App;
        using var client = host.GetTestClient();

        var dashboardResponse = await client.GetAsync(UiApiRoutes.QualityDashboard);
        var dashboard = await dashboardResponse.Content.ReadFromJsonAsync<QualityDashboardResponse>(JsonOptions);

        dashboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        dashboard.Should().NotBeNull();
        dashboard!.RealTimeMetrics.SymbolHealth.Should().NotBeEmpty(
            "the legacy dashboard projection remains intact");
        dashboard.Composite.Should().NotBeNull();
        dashboard.Composite!.Version.Should().Be("quality-v1");
        dashboard.Composite.OpenGaps.Should().ContainSingle();

        var gap = dashboard.Composite.OpenGaps.Single();
        var route = UiApiRoutes.WithParam(UiApiRoutes.QualityGapsBySymbol, "symbol", gap.Symbol);
        var response = await client.PostAsJsonAsync(
            route,
            new QualityGapRemediationRequest(gap.GapId, dashboard.Composite.Version));
        var payload = await response.Content.ReadFromJsonAsync<QualityGapRemediationResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.Should().NotBeNull();
        payload!.GapId.Should().Be(gap.GapId);
        payload.Status.Should().Be(nameof(AutoRemediationOutcome.Completed));
        payload.Provider.Should().Be("polygon");
        remediation.RequestedGap.Should().NotBeNull();
        remediation.RequestedGap!.GapStart.Should().Be(gap.From);
        remediation.RequestedGap.GapEnd.Should().Be(gap.To);
        remediation.RequestedProvider.Should().Be("polygon");

        var stale = await client.PostAsJsonAsync(
            route,
            new QualityGapRemediationRequest(gap.GapId, "stale-version"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        remediation.CallCount.Should().Be(1, "stale snapshots must not enqueue a backfill");
    }

    [Fact]
    public async Task QualityGaps_ReturnsStableGapContract()
    {
        var _r = await CreateHostAsync();
        await using var host = _r.App;
        using var client = host.GetTestClient();

        var response = await client.GetAsync($"{UiApiRoutes.QualityGaps}?count=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<List<QualityGapResponse>>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Should().NotBeEmpty();
        payload.Should().AllSatisfy(g => g.Symbol.Should().Be("AAPL"));
        payload.Max(g => g.Duration).Should().BeGreaterThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ContextualRemediation_WithoutProviderProvenance_FailsClosed()
    {
        var composite = StubCompositeQualityService.Create(provider: null);
        var remediation = new StubGapRemediationService();
        var _r = await CreateHostAsync(composite, remediation);
        await using var host = _r.App;
        using var client = host.GetTestClient();
        var gap = composite.Dashboard.OpenGaps.Single();
        var route = UiApiRoutes.WithParam(UiApiRoutes.QualityGapsBySymbol, "symbol", gap.Symbol);

        var response = await client.PostAsJsonAsync(
            route,
            new QualityGapRemediationRequest(gap.GapId, composite.Dashboard.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        remediation.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task QualityAnomalies_ReturnsStableAnomalyContract()
    {
        var _r = await CreateHostAsync();
        await using var host = _r.App;
        using var client = host.GetTestClient();

        var response = await client.GetAsync($"{UiApiRoutes.QualityAnomalies}?count=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<List<QualityAnomalyResponse>>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Should().ContainSingle();
        payload[0].Symbol.Should().Be("AAPL");
        payload[0].IsAcknowledged.Should().BeFalse();
    }

    [Fact]
    public async Task QualityLatencyStatistics_ReturnsStableLatencyContract()
    {
        var _r = await CreateHostAsync();
        await using var host = _r.App;
        using var client = host.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.QualityLatencyStatistics);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<QualityLatencyStatisticsResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.SymbolsTracked.Should().BeGreaterThan(0);
        payload.TotalSamples.Should().BeGreaterThan(0);
        payload.DistributionsBySymbol.Keys.Should().Contain(k => k.StartsWith("AAPL:"));
    }

    [Fact]
    public async Task QualityComparison_ReturnsStableComparisonContract()
    {
        var _r = await CreateHostAsync();
        await using var host = _r.App;
        using var client = host.GetTestClient();

        var route = UiApiRoutes.WithParam(UiApiRoutes.QualityComparison, "symbol", "AAPL");
        route = $"{route}?date=2026-03-20";
        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<QualityComparisonResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Symbol.Should().Be("AAPL");
        payload.Providers.Should().HaveCount(2);
        payload.RecommendedProvider.Should().Be("Provider2");
    }

    [Theory]
    [InlineData("/api/quality/completeness?date=not-a-date", "date")]
    [InlineData("/api/quality/reports/weekly?weekStart=03%2F20%2F2026", "weekStart")]
    public async Task QualityDateParameters_WhenMalformed_ReturnValidationProblem(
        string route,
        string field)
    {
        var hostContext = await CreateHostAsync();
        await using var host = hostContext.App;
        using var client = host.GetTestClient();

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Validation);
        problem.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
        problem.RootElement.GetRawText().Should().NotContain("FormatException");
    }

    [Fact]
    public async Task QualityDashboard_WhenDependencyFails_ReturnsSafeLoggedProblem()
    {
        var hostContext = await CreateHostAsync(new ThrowingCompositeQualityService());
        await using var host = hostContext.App;
        using var client = host.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.QualityDashboard);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Internal);
        problem.RootElement.GetProperty("detail").GetString().Should().Contain("could not be completed");
        problem.RootElement.GetRawText().Should().NotContain("provider-secret-123");
    }

    [Fact]
    public async Task QualityDashboard_WhenRequestIsAborted_PropagatesCancellationToDependency()
    {
        var composite = new BlockingCompositeQualityService();
        var hostContext = await CreateHostAsync(composite);
        await using var host = hostContext.App;
        using var client = host.GetTestClient();
        using var cts = new CancellationTokenSource();

        var request = client.GetAsync(UiApiRoutes.QualityDashboard, cts.Token);
        await composite.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await composite.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task QualityAnomalyAcknowledgement_ReturnsStableAcknowledgementContract()
    {
        var _r = await CreateHostAsync();
        await using var host = _r.App;
        var qualityService = _r.QualityService;
        var anomalyId = _r.AnomalyId;
        using var client = host.GetTestClient();

        var route = UiApiRoutes.WithParam(UiApiRoutes.QualityAnomaliesAcknowledge, "anomalyId", anomalyId);
        var response = await client.PostAsync(route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<QualityAnomalyAcknowledgementResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Acknowledged.Should().BeTrue();
        qualityService.AnomalyDetector.GetRecentAnomalies(10).Single().IsAcknowledged.Should().BeTrue();
    }

    private sealed record HostContext(WebApplication App, DataQualityMonitoringService QualityService, string AnomalyId);

    private static Task<HostContext> CreateHostAsync(
        ICompositeDataQualityReadService? compositeService = null,
        IDataQualityGapRemediationService? remediationService = null)
    {
        var qualityService = CreateSeededQualityService();
        var anomalyId = qualityService.AnomalyDetector.GetRecentAnomalies(10).Single().Id;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(qualityService);
        if (compositeService is not null)
            builder.Services.AddSingleton(compositeService);
        if (remediationService is not null)
            builder.Services.AddSingleton(remediationService);

        var app = builder.Build();
        app.MapDataQualityEndpoints(app.Services.GetRequiredService<DataQualityMonitoringService>());
        app.StartAsync().GetAwaiter().GetResult();
        return Task.FromResult(new HostContext(app, qualityService, anomalyId));
    }

    private static DataQualityMonitoringService CreateSeededQualityService()
    {
        var service = new DataQualityMonitoringService(new DataQualityMonitoringConfig
        {
            GapAnalyzerConfig = new GapAnalyzerConfig
            {
                GapThresholdSeconds = 60,
                ExpectedEventsPerHour = 1000
            },
            SequenceErrorConfig = new SequenceErrorConfig { MaxErrorsPerSymbol = 1 }
        });

        var baseTime = new DateTimeOffset(2026, 03, 20, 14, 00, 00, TimeSpan.Zero);

        service.ProcessTrade("AAPL", baseTime, 150.00m, 100m, sequence: 1, provider: "Provider1", latencyMs: 8);
        service.ProcessTrade("AAPL", baseTime.AddMinutes(2), 151.00m, 100m, sequence: 2, provider: "Provider1", latencyMs: 12);

        service.ProcessTrade("AAPL", baseTime, 150.00m, 100m, sequence: 1, provider: "Provider2", latencyMs: 18);
        service.ProcessTrade("AAPL", baseTime.AddMinutes(1), 150.50m, 100m, sequence: 2, provider: "Provider2", latencyMs: 24);

        service.ProcessQuote("AAPL", baseTime.AddMinutes(3), 151.00m, 150.00m, 10m, 10m, provider: "Provider1", latencyMs: 30);

        return service;
    }

    private static SequenceError CreateSequenceGapError(DateTimeOffset timestamp, long actualSequence) =>
        new(
            Timestamp: timestamp,
            Symbol: "AAPL",
            EventType: "Trade",
            ErrorType: SequenceErrorType.Gap,
            ExpectedSequence: actualSequence - 1,
            ActualSequence: actualSequence,
            GapSize: 1,
            StreamId: null,
            Provider: "Provider1");

    private sealed class StubCompositeQualityService : ICompositeDataQualityReadService
    {
        private readonly CompositeDataQualityGap _target;

        private StubCompositeQualityService(
            QualityCompositeDashboardResponse dashboard,
            CompositeDataQualityGap target)
        {
            Dashboard = dashboard;
            _target = target;
        }

        public QualityCompositeDashboardResponse Dashboard { get; }

        public Task<QualityCompositeDashboardResponse> GetDashboardAsync(CancellationToken ct = default) =>
            Task.FromResult(Dashboard);

        public bool TryResolveGap(string symbol, string gapId, out CompositeDataQualityGap target)
        {
            if (string.Equals(symbol, _target.Gap.Symbol, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(gapId, _target.GapId, StringComparison.Ordinal))
            {
                target = _target;
                return true;
            }

            target = null!;
            return false;
        }

        public static StubCompositeQualityService Create(string? provider = "polygon")
        {
            var from = new DateTimeOffset(2026, 07, 10, 13, 35, 00, TimeSpan.Zero);
            var to = from.AddMinutes(7);
            var gap = new DataGap(
                Symbol: "AAPL",
                EventType: "Trade",
                GapStart: from,
                GapEnd: to,
                Duration: to - from,
                MissedSequenceStart: 10,
                MissedSequenceEnd: 20,
                EstimatedMissedEvents: 11,
                Severity: GapSeverity.Significant,
                PossibleCause: "provider interruption",
                Provider: provider);
            var gapResponse = new QualityCompositeGapResponse(
                GapId: "111111111111111111111111",
                Symbol: gap.Symbol,
                Provider: provider,
                EventType: gap.EventType,
                From: gap.GapStart,
                To: gap.GapEnd,
                EstimatedMissingEvents: gap.EstimatedMissedEvents,
                Severity: gap.Severity.ToString(),
                Status: "Open",
                CanBackfill: true,
                DisabledReason: null);
            var component = new QualityComponentResponse(
                Kind: "StreamingFreshness",
                Label: "Streaming freshness",
                Weight: 0.35,
                Score: 92,
                Availability: "Measured",
                ObservedAt: to,
                IssueCount: 0,
                Detail: "Current streaming evidence.");
            var symbol = new QualityCompositeSymbolResponse(
                Symbol: gap.Symbol,
                CompositeScore: 92,
                Status: "Amber",
                IsPartial: true,
                CoverageWeight: 0.35,
                ExpectedEvents: 100,
                ObservedEvents: 89,
                AnomalyCount: 0,
                Components: [component],
                OpenGaps: [gapResponse],
                ProviderFreshness: [],
                Issues: []);
            var dashboard = new QualityCompositeDashboardResponse(
                Version: "quality-v1",
                ObservedAt: to,
                CompositeScore: 92,
                Status: "Amber",
                IsPartial: true,
                CoverageWeight: 0.35,
                Components: [component],
                Symbols: [symbol],
                OpenGaps: [gapResponse],
                AnomalyCount: 0);

            return new StubCompositeQualityService(
                dashboard,
                new CompositeDataQualityGap(gapResponse.GapId, dashboard.Version, gap, gapResponse.Provider));
        }
    }

    private sealed class ThrowingCompositeQualityService : ICompositeDataQualityReadService
    {
        public Task<QualityCompositeDashboardResponse> GetDashboardAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("provider-secret-123");

        public bool TryResolveGap(string symbol, string gapId, out CompositeDataQualityGap target)
        {
            target = null!;
            return false;
        }
    }

    private sealed class BlockingCompositeQualityService : ICompositeDataQualityReadService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<QualityCompositeDashboardResponse> GetDashboardAsync(
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("Unreachable.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }

        public bool TryResolveGap(string symbol, string gapId, out CompositeDataQualityGap target)
        {
            target = null!;
            return false;
        }
    }

    private sealed class StubGapRemediationService : IDataQualityGapRemediationService
    {
        public int CallCount { get; private set; }
        public DataGap? RequestedGap { get; private set; }
        public string? RequestedProvider { get; private set; }

        public Task<AutoGapRemediationRequestResult> RequestDataQualityGapAsync(
            DataGap gap,
            string? provider = null,
            CancellationToken ct = default)
        {
            CallCount++;
            RequestedGap = gap;
            RequestedProvider = provider;
            return Task.FromResult(new AutoGapRemediationRequestResult(
                AutoRemediationOutcome.Completed,
                provider ?? "stooq",
                DateOnly.FromDateTime(gap.GapStart.UtcDateTime),
                DateOnly.FromDateTime(gap.GapEnd.UtcDateTime),
                "AAPL|polygon|2026-07-10|2026-07-10"));
        }
    }
}
