using FluentAssertions;
using Meridian.Contracts.Operations;

namespace Meridian.Tests.Contracts;

public sealed class DataProvenanceTests
{
    [Theory]
    [InlineData(DataProvenance.Real, false)]
    [InlineData(DataProvenance.Simulated, true)]
    [InlineData(DataProvenance.Seeded, true)]
    [InlineData(DataProvenance.Sample, true)]
    public void IsNonReal_AndRequiresBadge_TrackNonRealProvenance(DataProvenance provenance, bool expected)
    {
        provenance.IsNonReal().Should().Be(expected);
        provenance.RequiresBadge().Should().Be(expected);
    }

    [Theory]
    [InlineData(DataProvenance.Real, null)]
    [InlineData(DataProvenance.Simulated, "SIMULATED")]
    [InlineData(DataProvenance.Seeded, "SEEDED")]
    [InlineData(DataProvenance.Sample, "SAMPLE")]
    public void Label_IsNullForRealAndUppercaseOtherwise(DataProvenance provenance, string? expected)
    {
        provenance.Label().Should().Be(expected);
    }

    [Theory]
    [InlineData(null, DataProvenance.Simulated)]
    [InlineData("", DataProvenance.Simulated)]
    [InlineData("real", DataProvenance.Real)]
    [InlineData("live", DataProvenance.Real)]
    [InlineData("seeded", DataProvenance.Seeded)]
    [InlineData("demo", DataProvenance.Seeded)]
    [InlineData("sample", DataProvenance.Sample)]
    [InlineData("simulated", DataProvenance.Simulated)]
    public void ParseTokenOrSimulated_MapsKnownTokens(string? token, DataProvenance expected)
    {
        DataProvenanceExtensions.ParseTokenOrSimulated(token).Should().Be(expected);
    }

    [Fact]
    public void ParseTokenOrSimulated_NeverUpgradesUnknownTokenToReal()
    {
        DataProvenanceExtensions.ParseTokenOrSimulated("mystery-source").Should().Be(DataProvenance.Simulated);
    }

    [Fact]
    public void Badge_TryCreate_ReturnsNullForRealData()
    {
        DataProvenanceBadge.TryCreate(DataProvenance.Real).Should().BeNull();
    }

    [Fact]
    public void Badge_TryCreate_ProducesPersistentNonDismissableBadge()
    {
        var badge = DataProvenanceBadge.TryCreate(DataProvenance.Seeded);

        badge.Should().NotBeNull();
        badge!.Provenance.Should().Be(DataProvenance.Seeded);
        badge.Label.Should().Be("SEEDED");
        badge.Dismissable.Should().BeFalse();
        badge.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Badge_TryCreate_HonorsCallerSuppliedDetail()
    {
        var badge = DataProvenanceBadge.TryCreate(DataProvenance.Simulated, "Random-walk simulator; no real fills.");

        badge!.Detail.Should().Be("Random-walk simulator; no real fills.");
    }
}
