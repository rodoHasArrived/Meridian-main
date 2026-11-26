using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Integrity;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage.Backfill;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class BackfillBatchPriceBasisTests
{
    private static readonly DateOnly Session = new(2026, 11, 20);

    [Theory]
    [InlineData(null, "daily|2026-11-20|100|101|99|100|1000|")]
    [InlineData(false, "daily|2026-11-20|100|101|99|100|1000|False")]
    [InlineData(true, "daily|2026-11-20|100|101|99|100|1000|True")]
    public void HashRow_UndeclaredBasis_PreservesLegacyReceiptIdentity(bool? adjusted, string legacyRow)
    {
        var bar = CreateBar(HistoricalPriceBasis.Unknown, adjusted);

        BackfillBatchRevision.HashRow(Event(bar)).Should().Be(Sha256Digest.ComputeUtf8(legacyRow));
    }

    [Fact]
    public void HashRow_EqualPricesWithDifferentBases_CannotShareReceiptEvidence()
    {
        var hashes = Enum.GetValues<HistoricalPriceBasis>()
            .Select(basis => BackfillBatchRevision.HashRow(Event(CreateBar(basis))))
            .ToArray();

        hashes.Should().OnlyHaveUniqueItems();
        BackfillBatchRevision.HashRow(Event(CreateBar(HistoricalPriceBasis.Raw)))
            .Should().NotBe(BackfillBatchRevision.HashRow(Event(CreateBar(HistoricalPriceBasis.Unknown, false))),
                "a legacy false flag is not the same evidence as an explicit raw-price declaration");
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown)]
    [InlineData(HistoricalPriceBasis.Raw)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted)]
    public void HashRow_SourceGeneratedRoundTrip_RetainsBasisBoundEvidence(HistoricalPriceBasis basis)
    {
        var original = CreateBar(basis);
        var json = JsonSerializer.Serialize(original, MarketDataJsonContext.Default.HistoricalBar);
        var restored = JsonSerializer.Deserialize(json, MarketDataJsonContext.Default.HistoricalBar)!;

        restored.PriceBasis.Should().Be(basis);
        BackfillBatchRevision.HashRow(Event(restored)).Should().Be(BackfillBatchRevision.HashRow(Event(original)));
    }

    [Fact]
    public void HashRow_LegacyJsonWithoutBasis_RetainsOriginalDigestAndUnknownBasis()
    {
        const string json = """
            {"symbol":"SPY","sessionDate":"2026-11-20","open":100,"high":101,"low":99,
             "close":100,"volume":1000,"source":"fixture","sequenceNumber":1,"isAdjusted":false}
            """;
        var restored = JsonSerializer.Deserialize(json, MarketDataJsonContext.Default.HistoricalBar)!;

        restored.PriceBasis.Should().Be(HistoricalPriceBasis.Unknown);
        BackfillBatchRevision.HashRow(Event(restored)).Should()
            .Be(Sha256Digest.ComputeUtf8("daily|2026-11-20|100|101|99|100|1000|False"));
    }

    private static HistoricalBar CreateBar(HistoricalPriceBasis basis, bool? adjusted = null) =>
        new("SPY", Session, 100m, 101m, 99m, 100m, 1_000, "fixture", 1,
            IsAdjusted: adjusted, PriceBasis: basis);

    private static MarketEvent Event(HistoricalBar bar) =>
        MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, bar.Source, bar.SequenceNumber);
}
