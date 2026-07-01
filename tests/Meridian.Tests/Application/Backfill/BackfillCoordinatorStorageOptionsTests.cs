using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage;
using Xunit;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Scenario coverage for adaptive backfill placement so high-volume and multi-source runs produce storage paths operators can inspect by provider and cadence.
/// </summary>
public sealed class BackfillCoordinatorStorageOptionsTests
{
    [Fact]
    public void Scenario_IntradayBackfill_UsesHourlySymbolPlacementAndKeepsGranularityPrefix()
    {
        var baseOptions = StorageProfilePresets.CreateFromProfile(
            StorageProfilePresets.DefaultProfile,
            rootPath: "data",
            compress: true);

        var options = BackfillCoordinator.CreateStorageOptionsForRequest(
            baseOptions,
            new BackfillRequest(
                Provider: "yahoo",
                Symbols: ["SPY"],
                From: new DateOnly(2026, 6, 1),
                To: new DateOnly(2026, 6, 1),
                Granularity: DataGranularity.Minute1));

        options.NamingConvention.Should().Be(FileNamingConvention.BySymbol);
        options.DatePartition.Should().Be(DatePartition.Hourly);
        options.FilePrefix.Should().Be("bar_1min");
        options.PartitionStrategy.Should().Be(new PartitionStrategy(
            PartitionDimension.Symbol,
            PartitionDimension.Date,
            null,
            DateGranularity: DatePartition.Hourly));
        options.Compress.Should().BeTrue();
        options.CompressionCodec.Should().Be(baseOptions.CompressionCodec);
    }

    [Fact]
    public void Scenario_CompositeDailyBackfill_KeepsProviderDimensionForCrossSourceEvidence()
    {
        var baseOptions = new StorageOptions
        {
            RootPath = "data",
            NamingConvention = FileNamingConvention.BySymbol,
            DatePartition = DatePartition.Daily,
            GenerateManifests = true,
            ActiveSinks = ["jsonl", "parquet"]
        };

        var options = BackfillCoordinator.CreateStorageOptionsForRequest(
            baseOptions,
            new BackfillRequest(
                Provider: "composite",
                Symbols: ["aapl", " AAPL ", "MSFT"],
                From: new DateOnly(2026, 1, 1),
                To: new DateOnly(2026, 1, 10),
                Granularity: DataGranularity.Daily));

        options.NamingConvention.Should().Be(FileNamingConvention.BySource);
        options.DatePartition.Should().Be(DatePartition.Daily);
        options.FilePrefix.Should().BeNull();
        options.PartitionStrategy.Should().Be(new PartitionStrategy(
            PartitionDimension.Date,
            PartitionDimension.Source,
            PartitionDimension.Symbol,
            DateGranularity: DatePartition.Daily));
        options.GenerateManifests.Should().BeTrue();
        options.ActiveSinks.Should().Equal("jsonl", "parquet");
    }

    [Fact]
    public void Scenario_LongWindowBackfill_UsesMonthlyArchivalPlacementWithoutDroppingRetention()
    {
        var baseOptions = new StorageOptions
        {
            RootPath = "data",
            NamingConvention = FileNamingConvention.BySymbol,
            DatePartition = DatePartition.Daily,
            RetentionDays = 365,
            MaxTotalBytes = 1024 * 1024
        };

        var options = BackfillCoordinator.CreateStorageOptionsForRequest(
            baseOptions,
            new BackfillRequest(
                Provider: "stooq",
                Symbols: ["AAPL", "MSFT"],
                From: new DateOnly(2020, 1, 1),
                To: new DateOnly(2026, 1, 1),
                Granularity: DataGranularity.Daily));

        options.NamingConvention.Should().Be(FileNamingConvention.ByDate);
        options.DatePartition.Should().Be(DatePartition.Monthly);
        options.PartitionStrategy.Should().Be(new PartitionStrategy(
            PartitionDimension.Date,
            PartitionDimension.Symbol,
            null,
            DateGranularity: DatePartition.Monthly));
        options.RetentionDays.Should().Be(365);
        options.MaxTotalBytes.Should().Be(1024 * 1024);
    }
}
