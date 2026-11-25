using System.Text.Json;
using Meridian.Contracts.Domain.Models;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class StoredBarPriceBasisTests
{
    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown)]
    [InlineData(HistoricalPriceBasis.Raw)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted)]
    public async Task ReadingStoredBarPreservesDeclaredPriceBasis(HistoricalPriceBasis basis)
    {
        var original = new HistoricalBar("AAPL", new DateOnly(2026, 1, 2), 49m, 51m, 48m, 50m, 2_000,
            Source: "fixture", PriceBasis: basis);
        var restored = await ReadAsync(JsonSerializer.Serialize(original));

        var bar = Assert.Single(restored);
        Assert.Equal(basis, bar.PriceBasis);
        Assert.Equal(original.IsAdjusted, bar.IsAdjusted);
        Assert.Equal(original.Close, bar.Close);
    }

    [Fact]
    public async Task LegacyRawFlagDoesNotEstablishRawBasis()
    {
        var original = new HistoricalBar("AAPL", new DateOnly(2026, 1, 2), 49m, 51m, 48m, 50m, 2_000,
            IsAdjusted: false);
        var restored = Assert.Single(await ReadAsync(JsonSerializer.Serialize(original)));

        Assert.Equal(HistoricalPriceBasis.Unknown, restored.PriceBasis);
        Assert.False(restored.IsAdjusted);
    }

    [Fact]
    public async Task ContradictoryStoredBasisIsNotAdmitted()
    {
        var original = new HistoricalBar("AAPL", new DateOnly(2026, 1, 2), 49m, 51m, 48m, 50m, 2_000,
            PriceBasis: HistoricalPriceBasis.SplitAdjusted);
        var corrupt = JsonSerializer.Serialize(original).Replace("\"IsAdjusted\":true", "\"IsAdjusted\":false", StringComparison.Ordinal);

        Assert.Empty(await ReadAsync(corrupt));
    }

    [Fact]
    public async Task CompoundBasisNameCannotCreateRawEligibility()
    {
        var original = new HistoricalBar("AAPL", new DateOnly(2026, 1, 2), 49m, 51m, 48m, 50m, 2_000,
            PriceBasis: HistoricalPriceBasis.Raw);
        var corrupt = JsonSerializer.Serialize(original).Replace("\"PriceBasis\":\"Raw\"",
            "\"PriceBasis\":\"Unknown, Raw\"", StringComparison.Ordinal);

        Assert.Empty(await ReadAsync(corrupt));
    }

    [Fact]
    public async Task CaseVariantDuplicateBasisIsNotAdmitted()
    {
        var original = new HistoricalBar("AAPL", new DateOnly(2026, 1, 2), 49m, 51m, 48m, 50m, 2_000,
            PriceBasis: HistoricalPriceBasis.Raw);
        var corrupt = JsonSerializer.Serialize(original).Replace("\"PriceBasis\":\"Raw\"",
            "\"PriceBasis\":\"Raw\",\"priceBasis\":\"SplitAdjusted\"", StringComparison.Ordinal);

        Assert.Empty(await ReadAsync(corrupt));
    }

    private static async Task<List<HistoricalBar>> ReadAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-price-basis-{Guid.NewGuid():N}.jsonl");
        try
        {
            await File.WriteAllTextAsync(path, json + Environment.NewLine);
            return await BackfillService.ParseBarsFromFileAsync(path, "AAPL", new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 3), CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
