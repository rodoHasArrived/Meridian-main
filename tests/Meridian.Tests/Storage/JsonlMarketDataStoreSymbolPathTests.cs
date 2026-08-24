using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Store;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage;
using Meridian.Storage.Policies;
using Meridian.Storage.Sinks;
using Meridian.Storage.Store;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class JsonlMarketDataStoreSymbolPathTests : TempDirectoryTestBase
{
    [Theory]
    [InlineData("BTC/USD")]
    [InlineData("BTC:USD")]
    [InlineData("BTC USD")]
    [InlineData("BTC_USD")]
    [InlineData("btc/usd")]
    public async Task WriteThenQueryAsync_PathSensitiveSymbol_RoundTrips(string symbol)
    {
        var options = CreateOptions();
        var policy = new JsonlStoragePolicy(options);
        var marketEvent = CreateTrade(symbol, sequence: 1);

        await WriteAsync(options, policy, marketEvent);

        var storedEvents = await QueryAsync(symbol);

        storedEvents.Should().ContainSingle();
        storedEvents[0].EffectiveSymbol.Should().Be(symbol);
        policy.TryParsePath(policy.GetPath(marketEvent))!.Symbol.Should().Be(symbol.ToUpperInvariant());
    }

    [Theory]
    [InlineData("BTC/USD", "BTC_USD")]
    [InlineData("BTC:USD", "BTC_USD")]
    [InlineData("BTC USD", "BTC_USD")]
    public async Task WriteThenQueryAsync_LegacyCollisionPair_UsesDistinctPathsAndRawFiltering(
        string leftSymbol,
        string rightSymbol)
    {
        var options = CreateOptions();
        var policy = new JsonlStoragePolicy(options);
        var leftEvent = CreateTrade(leftSymbol, sequence: 1);
        var rightEvent = CreateTrade(rightSymbol, sequence: 2);

        policy.GetPath(leftEvent).Should().NotBe(policy.GetPath(rightEvent));
        await WriteAsync(options, policy, leftEvent, rightEvent);

        Directory.GetFiles(TestDataRoot, "*.jsonl", SearchOption.AllDirectories)
            .Should().HaveCount(2);

        var leftResults = await QueryAsync(leftSymbol);
        var rightResults = await QueryAsync(rightSymbol);

        leftResults.Should().ContainSingle();
        leftResults[0].EffectiveSymbol.Should().Be(leftSymbol);
        rightResults.Should().ContainSingle();
        rightResults[0].EffectiveSymbol.Should().Be(rightSymbol);
    }

    public static IEnumerable<object[]> PathMetadataRoundTripCases()
    {
        foreach (var namingConvention in Enum.GetValues<FileNamingConvention>())
        {
            foreach (var datePartition in Enum.GetValues<DatePartition>())
            {
                yield return new object[] { namingConvention, datePartition, false };
                yield return new object[] { namingConvention, datePartition, true };
            }
        }
    }

    [Theory]
    [MemberData(nameof(PathMetadataRoundTripCases))]
    public void GetPathThenTryParsePath_AllConventionPartitionProviderCombinations_RoundTripMetadata(
        FileNamingConvention namingConvention,
        DatePartition datePartition,
        bool includeProvider)
    {
        var options = new StorageOptions
        {
            RootPath = TestDataRoot,
            NamingConvention = namingConvention,
            DatePartition = datePartition,
            IncludeProvider = includeProvider,
            FilePrefix = "bar_1min"
        };
        var policy = new JsonlStoragePolicy(options);
        var marketEvent = CreateTrade("btc/usd", sequence: 1, source: "TEST_SOURCE");

        var metadata = policy.TryParsePath(policy.GetPath(marketEvent));

        metadata.Should().NotBeNull();
        metadata!.Symbol.Should().Be("BTC/USD");
        metadata.EventType.Should().Be(nameof(MarketEventType.Trade));

        var pathIncludesProvider = namingConvention switch
        {
            FileNamingConvention.BySource or
                FileNamingConvention.Hierarchical or
                FileNamingConvention.Canonical => true,
            FileNamingConvention.Flat => includeProvider,
            _ => false
        };
        metadata.Source.Should().Be(pathIncludesProvider ? "TEST_SOURCE" : "Unknown");

        var expectedDate = namingConvention == FileNamingConvention.Canonical
            ? new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero)
            : datePartition switch
            {
                DatePartition.None => (DateTimeOffset?)null,
                DatePartition.Monthly => new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                DatePartition.Daily => new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero),
                DatePartition.Hourly => new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
                _ => throw new ArgumentOutOfRangeException(nameof(datePartition))
            };
        metadata.Date.Should().Be(expectedDate);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData(@"C:\escape")]
    [InlineData("safe/name")]
    [InlineData("safe.name")]
    [InlineData("safe name")]
    public void Constructor_UnsafeFilePrefix_Throws(string filePrefix)
    {
        var options = new StorageOptions
        {
            RootPath = TestDataRoot,
            FilePrefix = filePrefix
        };

        var act = () => new JsonlStoragePolicy(options);

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(StorageOptions.FilePrefix));
    }

    [Fact]
    public void GetPath_SafePrefixAndPathSensitiveIdentities_RemainsWithinConfiguredRoot()
    {
        var options = new StorageOptions
        {
            RootPath = TestDataRoot,
            NamingConvention = FileNamingConvention.Hierarchical,
            DatePartition = DatePartition.None,
            FilePrefix = "bar_1min"
        };
        var policy = new JsonlStoragePolicy(options);
        var marketEvent = CreateTrade("../btc/usd", sequence: 1, source: "../TEST_SOURCE");

        var path = policy.GetPath(marketEvent);
        var relativePath = Path.GetRelativePath(Path.GetFullPath(TestDataRoot), Path.GetFullPath(path));

        Path.IsPathRooted(relativePath).Should().BeFalse();
        relativePath.Should().NotBe("..");
        relativePath.Should().NotStartWith($"..{Path.DirectorySeparatorChar}");
        Path.GetFileName(path).Should().Be("bar_1min_data.jsonl");
    }

    private StorageOptions CreateOptions()
    {
        return new StorageOptions
        {
            RootPath = TestDataRoot,
            NamingConvention = FileNamingConvention.BySymbol,
            DatePartition = DatePartition.None
        };
    }

    private static async Task WriteAsync(
        StorageOptions options,
        JsonlStoragePolicy policy,
        params MarketEvent[] events)
    {
        await using var sink = new JsonlStorageSink(options, policy, JsonlBatchOptions.NoBatching);
        foreach (var marketEvent in events)
        {
            await sink.AppendAsync(marketEvent);
        }

        await sink.FlushAsync();
    }

    private async Task<IReadOnlyList<MarketEvent>> QueryAsync(string symbol)
    {
        var result = new List<MarketEvent>();
        var store = new JsonlMarketDataStore(TestDataRoot);
        await foreach (var marketEvent in store.QueryAsync(new MarketDataQuery(Symbol: new SymbolId(symbol))))
        {
            result.Add(marketEvent);
        }

        return result;
    }

    private static MarketEvent CreateTrade(string symbol, int sequence, string source = "TEST")
    {
        var timestamp = new DateTimeOffset(2026, 8, 5, 12, 0, sequence, TimeSpan.Zero);
        var trade = new Trade(
            timestamp,
            symbol,
            100m + sequence,
            100,
            AggressorSide.Buy,
            sequence);

        return MarketEvent.Trade(timestamp, symbol, trade, source, sequence);
    }
}
