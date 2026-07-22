using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class AdaptivePartitionPlacementPlannerTests
{
    private readonly AdaptivePartitionPlacementPlanner _sut = new();

    [Fact]
    public void Scenario_HighVolumeMultiSourceStream_UsesHourlySourceAwareHotPlacement()
    {
        var recommendation = _sut.Recommend(new AdaptivePartitionPlacementRequest(
            TotalEvents: 1_200_000,
            Coverage: TimeSpan.FromHours(6),
            SymbolCount: 50,
            SourceCount: 3,
            EventTypeCount: 2));

        recommendation.ReasonCode.Should().Be("HighVolumeHotIngest");
        recommendation.RecommendedProfile.Should().Be(StorageProfile.LowLatency);
        recommendation.EventsPerHour.Should().Be(200_000);
        recommendation.Strategy.Should().Be(new PartitionStrategy(
            PartitionDimension.Symbol,
            PartitionDimension.EventType,
            PartitionDimension.Source,
            DateGranularity: DatePartition.Hourly));
        recommendation.Notes.Should().Contain(note => note.Contains("events/hour", StringComparison.Ordinal));
    }

    [Fact]
    public void Scenario_LongWindowLowVolumeBackfill_UsesMonthlyArchivalPlacement()
    {
        var recommendation = _sut.Recommend(new AdaptivePartitionPlacementRequest(
            TotalEvents: 300_000,
            Coverage: TimeSpan.FromDays(180),
            SymbolCount: 25,
            SourceCount: 2));

        recommendation.ReasonCode.Should().Be("LowVolumeArchivalWindow");
        recommendation.RecommendedProfile.Should().Be(StorageProfile.Archival);
        recommendation.EventsPerDay.Should().BeApproximately(1666.666d, 0.001d);
        recommendation.Strategy.Should().Be(new PartitionStrategy(
            PartitionDimension.Date,
            PartitionDimension.Source,
            PartitionDimension.Symbol,
            DateGranularity: DatePartition.Monthly));
    }

    [Fact]
    public void Scenario_MultiSourceDailyBackfill_KeepsProviderDimensionForReconciliation()
    {
        var recommendation = _sut.Recommend(new AdaptivePartitionPlacementRequest(
            TotalEvents: 500_000,
            Coverage: TimeSpan.FromDays(10),
            SymbolCount: 100,
            SourceCount: 4));

        recommendation.ReasonCode.Should().Be("MultiSourceDailyEvidence");
        recommendation.RecommendedProfile.Should().Be(StorageProfile.Research);
        recommendation.Strategy.Should().Be(new PartitionStrategy(
            PartitionDimension.Date,
            PartitionDimension.Source,
            PartitionDimension.Symbol,
            DateGranularity: DatePartition.Daily));
        recommendation.Notes.Should().Contain(note => note.Contains("Multiple providers", StringComparison.Ordinal));
    }

    [Fact]
    public void Scenario_InvalidEvidenceShape_FailsClosedBeforeRecommendation()
    {
        var act = () => _sut.Recommend(new AdaptivePartitionPlacementRequest(
            TotalEvents: -1,
            Coverage: TimeSpan.FromDays(1),
            SymbolCount: 1,
            SourceCount: 1));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("request.TotalEvents");
    }
}
