using FluentAssertions;
using Meridian.Application.DataQuality;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Application.DataQuality;

public sealed class CompositeDataQualityReadServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_WithoutMeasuredEvidence_ReportsUnavailableScoreInsteadOfZero()
    {
        await using var streaming = new DataQualityMonitoringService();
        var sut = new CompositeDataQualityReadService(streaming);

        var dashboard = await sut.GetDashboardAsync();

        dashboard.CompositeScore.Should().BeNull();
        dashboard.Status.Should().Be("Unavailable");
        dashboard.IsPartial.Should().BeTrue();
        dashboard.Symbols.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboardAsync_ProjectsAllAvailableSignalsAndMarksMissingSourcesPartial()
    {
        await using var streaming = CreateSeededStreamingService();
        var sut = new CompositeDataQualityReadService(streaming);

        var dashboard = await sut.GetDashboardAsync();

        dashboard.Symbols.Should().ContainSingle();
        var symbol = dashboard.Symbols.Single();
        symbol.Symbol.Should().Be("AAPL");
        symbol.IsPartial.Should().BeTrue();
        symbol.CoverageWeight.Should().Be(0.35);
        symbol.Status.Should().NotBe("Green", "partial evidence must never be presented as fully healthy");
        symbol.Components.Should().ContainSingle(component =>
            component.Kind == "StreamingFreshness" && component.Score.HasValue);
        symbol.Components.Should().ContainSingle(component =>
            component.Kind == "StoredCompleteness" && component.Availability == "Unavailable");
        symbol.Components.Should().ContainSingle(component =>
            component.Kind == "AdapterGapIntegrity" && component.Availability == "Unavailable");
        dashboard.IsPartial.Should().BeTrue();
        dashboard.Status.Should().NotBe("Green");
    }

    [Fact]
    public async Task GetDashboardAsync_UsesStableOpaqueGapIdsAndResolvesOnlyMatchingSymbols()
    {
        await using var streaming = CreateSeededStreamingService();
        var sut = new CompositeDataQualityReadService(streaming);

        var first = await sut.GetDashboardAsync();
        var second = await sut.GetDashboardAsync();

        var firstGap = first.OpenGaps.Should().ContainSingle().Subject;
        var secondGap = second.OpenGaps.Should().ContainSingle().Subject;
        secondGap.GapId.Should().Be(firstGap.GapId);
        firstGap.CanBackfill.Should().BeTrue();
        firstGap.Provider.Should().Be("polygon");
        firstGap.GapId.Should().MatchRegex("^[0-9a-f]{24}$");

        sut.TryResolveGap("aapl", secondGap.GapId, out var target).Should().BeTrue();
        target.GapId.Should().Be(secondGap.GapId);
        target.DashboardVersion.Should().Be(second.Version);
        target.Gap.Symbol.Should().Be("AAPL");
        target.Gap.Provider.Should().Be("polygon");
        target.Provider.Should().Be("polygon");
        sut.TryResolveGap("MSFT", secondGap.GapId, out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetDashboardAsync_GapWithoutProviderProvenance_DisablesRemediation()
    {
        await using var streaming = new DataQualityMonitoringService(new DataQualityMonitoringConfig
        {
            GapAnalyzerConfig = new GapAnalyzerConfig
            {
                GapThresholdSeconds = 60,
                ExpectedEventsPerHour = 1000
            }
        });
        var now = DateTimeOffset.UtcNow;
        streaming.ProcessTrade("AAPL", now.AddMinutes(-3), 150m, 100m, 1);
        streaming.ProcessTrade("AAPL", now.AddMinutes(-1), 151m, 100m, 2);
        var sut = new CompositeDataQualityReadService(streaming);

        var dashboard = await sut.GetDashboardAsync();

        var gap = dashboard.OpenGaps.Should().ContainSingle().Subject;
        gap.Provider.Should().BeNull();
        gap.CanBackfill.Should().BeFalse();
        gap.DisabledReason.Should().Contain("originating provider");
    }

    private static DataQualityMonitoringService CreateSeededStreamingService()
    {
        var service = new DataQualityMonitoringService(new DataQualityMonitoringConfig
        {
            GapAnalyzerConfig = new GapAnalyzerConfig
            {
                GapThresholdSeconds = 60,
                ExpectedEventsPerHour = 1000
            }
        });
        var now = DateTimeOffset.UtcNow;

        service.ProcessTrade("AAPL", now.AddMinutes(-3), 150m, 100m, 1, "polygon", 8);
        service.ProcessTrade("AAPL", now.AddMinutes(-1), 151m, 100m, 2, "polygon", 12);

        return service;
    }
}
