using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Contracts.Ledger;

public sealed class LedgerDimensionTagsTests
{
    [Fact]
    public void HasAnyDimension_IsFalseForAnEmptySet() =>
        LedgerDimensionTags.HasAnyDimension(new LedgerDimensionSetDto()).Should().BeFalse();

    // The drift this consolidation removes: the endpoint copy asked `is not null` while the storage
    // copy asked `!IsNullOrWhiteSpace`, so a whitespace-only value — easy to produce from a CSV
    // import, a trimmed-to-empty form field, or a padded fixed-width feed — was "dimensioned" at one
    // layer and "not dimensioned" at the other.
    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData("\t")]
    [InlineData("   \r\n ")]
    public void HasAnyDimension_TreatsBlankValuesAsAbsent(string blank)
    {
        var dimensions = new LedgerDimensionSetDto(FundId: blank, BookId: blank);

        LedgerDimensionTags.HasAnyDimension(dimensions).Should().BeFalse();
    }

    [Fact]
    public void HasAnyDimension_IsTrueForAPopulatedValue() =>
        LedgerDimensionTags.HasAnyDimension(new LedgerDimensionSetDto(FundId: "fund-1"))
            .Should()
            .BeTrue();

    [Fact]
    public void HasAnyDimension_TrimsBeforeDeciding() =>
        LedgerDimensionTags.HasAnyDimension(new LedgerDimensionSetDto(FundId: "  fund-1  "))
            .Should()
            .BeTrue();

    [Fact]
    public void HasAnyDimension_TreatsABlankExternalGlEntryAsAbsent()
    {
        // A pair with a blank key or value is not a dimension. ExtractExternalGlDimensions never
        // emits one, so this only arises from a caller-supplied dictionary -- where the close path
        // answered "not dimensioned" and every other caller answered "dimensioned" (#2672). The
        // stricter reading now lives here so the two cannot disagree.
        LedgerDimensionTags.HasAnyDimension(new LedgerDimensionSetDto(
            ExternalGlDimensions: new Dictionary<string, string> { ["  "] = "100" }))
            .Should()
            .BeFalse();

        LedgerDimensionTags.HasAnyDimension(new LedgerDimensionSetDto(
            ExternalGlDimensions: new Dictionary<string, string> { ["costCentre"] = "  " }))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void HasAnyDimension_CountsAnExternalGlEntryWithBothSidesPopulated() =>
        LedgerDimensionTags.HasAnyDimension(new LedgerDimensionSetDto(
            ExternalGlDimensions: new Dictionary<string, string> { ["costCentre"] = "100" }))
            .Should()
            .BeTrue();

    [Fact]
    public void HasAnyDimension_CountsAPopulatedEntryAlongsideABlankOne() =>
        LedgerDimensionTags.HasAnyDimension(new LedgerDimensionSetDto(
            ExternalGlDimensions: new Dictionary<string, string>
            {
                ["  "] = "100",
                ["costCentre"] = "200"
            }))
            .Should()
            .BeTrue();

    // Guards against a copy that silently covers only part of the dimension set: every field on
    // LedgerDimensionSetDto must, on its own, be enough to make the set "dimensioned". One of the
    // predicate copies this consolidation replaced checked only 11 of the 19 fields.
    [Fact]
    public void HasAnyDimension_CoversEveryDimensionField()
    {
        (string Field, LedgerDimensionSetDto Dimensions)[] singleFieldSets =
        [
            (nameof(LedgerDimensionSetDto.FundId), new LedgerDimensionSetDto(FundId: "x")),
            (nameof(LedgerDimensionSetDto.EntityId), new LedgerDimensionSetDto(EntityId: "x")),
            (nameof(LedgerDimensionSetDto.SleeveId), new LedgerDimensionSetDto(SleeveId: "x")),
            (nameof(LedgerDimensionSetDto.StrategyId), new LedgerDimensionSetDto(StrategyId: "x")),
            (nameof(LedgerDimensionSetDto.InvestorId), new LedgerDimensionSetDto(InvestorId: "x")),
            (nameof(LedgerDimensionSetDto.CapitalAccountId), new LedgerDimensionSetDto(CapitalAccountId: "x")),
            (nameof(LedgerDimensionSetDto.InstrumentId), new LedgerDimensionSetDto(InstrumentId: Guid.NewGuid())),
            (nameof(LedgerDimensionSetDto.TaxLotId), new LedgerDimensionSetDto(TaxLotId: "x")),
            (nameof(LedgerDimensionSetDto.CostCenterId), new LedgerDimensionSetDto(CostCenterId: "x")),
            (nameof(LedgerDimensionSetDto.CounterpartyId), new LedgerDimensionSetDto(CounterpartyId: "x")),
            (nameof(LedgerDimensionSetDto.OrganizationId), new LedgerDimensionSetDto(OrganizationId: "x")),
            (nameof(LedgerDimensionSetDto.PortfolioId), new LedgerDimensionSetDto(PortfolioId: "x")),
            (nameof(LedgerDimensionSetDto.BookId), new LedgerDimensionSetDto(BookId: "x")),
            (nameof(LedgerDimensionSetDto.AccountId), new LedgerDimensionSetDto(AccountId: "x")),
            (nameof(LedgerDimensionSetDto.CustomerId), new LedgerDimensionSetDto(CustomerId: "x")),
            (nameof(LedgerDimensionSetDto.VendorId), new LedgerDimensionSetDto(VendorId: "x")),
            (nameof(LedgerDimensionSetDto.ProjectId), new LedgerDimensionSetDto(ProjectId: "x")),
            (nameof(LedgerDimensionSetDto.ExternalGlDimensions), new LedgerDimensionSetDto(
                ExternalGlDimensions: new Dictionary<string, string> { ["costCentre"] = "100" })),
            (nameof(LedgerDimensionSetDto.PositionId), new LedgerDimensionSetDto { PositionId = Guid.NewGuid() })
        ];

        var uncovered = singleFieldSets
            .Where(candidate => !LedgerDimensionTags.HasAnyDimension(candidate.Dimensions))
            .Select(candidate => candidate.Field)
            .ToArray();

        uncovered.Should().BeEmpty("every dimension field alone should make the set dimensioned");
    }

    [Fact]
    public void HasAnyDimension_RejectsNull()
    {
        var act = () => LedgerDimensionTags.HasAnyDimension(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("externalGl.costCentre")]
    [InlineData("externalGl:costCentre")]
    [InlineData("gl.costCentre")]
    [InlineData("gl:costCentre")]
    [InlineData("GL.costCentre")]
    public void ExtractExternalGlDimensions_RecognisesEveryPrefix(string key)
    {
        var result = LedgerDimensionTags.ExtractExternalGlDimensions(
            new Dictionary<string, string> { [key] = "100" });

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new KeyValuePair<string, string>("costCentre", "100"));
    }

    [Fact]
    public void ExtractExternalGlDimensions_IgnoresUnprefixedAndBlankTags()
    {
        var result = LedgerDimensionTags.ExtractExternalGlDimensions(new Dictionary<string, string>
        {
            ["fundId"] = "fund-1",
            ["gl.costCentre"] = "  ",
            ["gl.region"] = " emea ",
            ["gl."] = "orphan"
        });

        result.Should().ContainSingle().And.ContainKey("region");
        result["region"].Should().Be("emea");
    }

    [Fact]
    public void ExtractExternalGlDimensions_StripsTheSuppliedScopePrefix()
    {
        var result = LedgerDimensionTags.ExtractExternalGlDimensions(
            new Dictionary<string, string>
            {
                ["line.gl.costCentre"] = "100",
                ["other.gl.region"] = "emea"
            },
            prefix: "line.");

        result.Should().ContainSingle().And.ContainKey("costCentre");
    }

    [Fact]
    public void ExtractExternalGlDimensions_IsCaseInsensitiveOnKeys()
    {
        var result = LedgerDimensionTags.ExtractExternalGlDimensions(
            new Dictionary<string, string> { ["gl.costCentre"] = "100" });

        result.ContainsKey("COSTCENTRE").Should().BeTrue();
    }

    [Fact]
    public void ExtractExternalGlDimensions_ReturnsEmptyForNullOrEmptyTags()
    {
        LedgerDimensionTags.ExtractExternalGlDimensions(null).Should().BeEmpty();
        LedgerDimensionTags.ExtractExternalGlDimensions(new Dictionary<string, string>())
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FirstTag_ReturnsTheFirstMatchingKeyNormalized()
    {
        var tags = new Dictionary<string, string> { ["fundProfileId"] = "  fund-1  " };

        LedgerDimensionTags.FirstTag(tags, "fundId", "fundProfileId").Should().Be("fund-1");
    }

    [Fact]
    public void FirstTag_PrefersTheEarlierKey()
    {
        var tags = new Dictionary<string, string>
        {
            ["fundId"] = "first",
            ["fundProfileId"] = "second"
        };

        LedgerDimensionTags.FirstTag(tags, "fundId", "fundProfileId").Should().Be("first");
    }

    [Fact]
    public void FirstTag_ReturnsNullWhenAbsentOrBlank()
    {
        LedgerDimensionTags.FirstTag(null, "fundId").Should().BeNull();
        LedgerDimensionTags.FirstTag(new Dictionary<string, string>(), "fundId").Should().BeNull();
        LedgerDimensionTags.FirstTag(new Dictionary<string, string> { ["fundId"] = "  " }, "fundId")
            .Should()
            .BeNull();
    }
}
