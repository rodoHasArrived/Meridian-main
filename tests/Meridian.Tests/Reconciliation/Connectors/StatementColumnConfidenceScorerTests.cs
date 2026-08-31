using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class StatementColumnConfidenceScorerTests
{
    private static StatementMappingProfileDocument CanonicalProfile =>
        StatementBuiltInProfiles.All.Single(profile =>
            profile.ProfileId == StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);

    [Fact]
    public void MapColumns_ExactAliasFuzzyAndUnmapped_ScoreAsExpected()
    {
        var mappings = StatementColumnConfidenceScorer.MapColumns(
            ["account", "Ticker", "trade dt", "totally unrelated"],
            CanonicalProfile);

        mappings[0].Confidence.Should().Be(StatementMappingConfidence.Exact);
        mappings[0].CanonicalField.Should().Be(StatementCanonicalField.Account);
        mappings[0].Score.Should().Be(1.0m);

        mappings[1].Confidence.Should().Be(StatementMappingConfidence.Alias);
        mappings[1].CanonicalField.Should().Be(StatementCanonicalField.SecurityIdentifier);
        mappings[1].Score.Should().Be(0.9m);

        mappings[2].Confidence.Should().Be(StatementMappingConfidence.Fuzzy);
        mappings[2].CanonicalField.Should().Be(StatementCanonicalField.TradeDate);
        mappings[2].Score.Should().Be(0.6m);

        mappings[3].Confidence.Should().Be(StatementMappingConfidence.Unmapped);
        mappings[3].CanonicalField.Should().BeNull();
        mappings[3].Score.Should().Be(0m);
    }

    [Fact]
    public void MapColumns_DuplicateCandidates_StrongestColumnClaimsTheField()
    {
        var mappings = StatementColumnConfidenceScorer.MapColumns(
            ["account", "account number"],
            CanonicalProfile);

        mappings[0].CanonicalField.Should().Be(StatementCanonicalField.Account);
        mappings[1].CanonicalField.Should().BeNull();
        mappings[1].Confidence.Should().Be(StatementMappingConfidence.Unmapped);
        mappings[1].Rationale.Should().Contain("already mapped");
    }

    [Fact]
    public void MapColumns_ExplicitRemappingWinsOverImplicitCanonicalAliases()
    {
        var swappedProfile = CanonicalProfile with
        {
            Fields = CanonicalProfile.Fields.Select(field => field.CanonicalField switch
            {
                "Quantity" => field with { SourceColumn = "price" },
                "Price" => field with { SourceColumn = "quantity" },
                _ => field
            }).ToArray()
        };

        var mappings = StatementColumnConfidenceScorer.MapColumns(["quantity", "price"], swappedProfile);

        mappings[0].CanonicalField.Should().Be(StatementCanonicalField.Price);
        mappings[0].Confidence.Should().Be(StatementMappingConfidence.Exact);
        mappings[1].CanonicalField.Should().Be(StatementCanonicalField.Quantity);
        mappings[1].Confidence.Should().Be(StatementMappingConfidence.Exact);
    }

    [Fact]
    public void MapColumns_NormalizedExplicitRemappingWinsOverImplicitCanonicalAliases()
    {
        var swappedProfile = CanonicalProfile with
        {
            Fields = CanonicalProfile.Fields.Select(field => field.CanonicalField switch
            {
                "Quantity" => field with { SourceColumn = "unit_price" },
                "Price" => field with { SourceColumn = "quan_tity" },
                _ => field
            }).ToArray()
        };

        var mappings = StatementColumnConfidenceScorer.MapColumns(["quan-tity", "unit-price"], swappedProfile);

        mappings[0].CanonicalField.Should().Be(StatementCanonicalField.Price);
        mappings[0].Confidence.Should().Be(StatementMappingConfidence.Fuzzy);
        mappings[1].CanonicalField.Should().Be(StatementCanonicalField.Quantity);
        mappings[1].Confidence.Should().Be(StatementMappingConfidence.Fuzzy);
    }

    [Theory]
    [InlineData("unrte", StatementCanonicalField.Quantity)]
    [InlineData("unita", StatementCanonicalField.Price)]
    public void MapColumns_EditDistanceRanksClosenessBeforeExplicitSourceTieBreaker(
        string remappedPriceSource,
        StatementCanonicalField expectedField)
    {
        var remappedProfile = CanonicalProfile with
        {
            Fields = CanonicalProfile.Fields.Select(field =>
                field.CanonicalField == "Price"
                    ? field with { SourceColumn = remappedPriceSource }
                    : field).ToArray()
        };

        var mapping = StatementColumnConfidenceScorer.MapColumns(["unitz"], remappedProfile).Single();

        mapping.CanonicalField.Should().Be(expectedField);
        mapping.Confidence.Should().Be(StatementMappingConfidence.Fuzzy);
    }

    [Fact]
    public void ScoreProfile_CanonicalHeaderRanksCanonicalProfileHighest()
    {
        string[] canonicalHeader =
            ["account", "symbol", "quantity", "price", "cashAmount", "activityType", "tradeDate"];
        var sampleBroker = StatementBuiltInProfiles.All.Single(profile =>
            profile.ProfileId == StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId);

        var canonicalScore = StatementColumnConfidenceScorer.ScoreProfile(canonicalHeader, CanonicalProfile);
        var sampleBrokerScore = StatementColumnConfidenceScorer.ScoreProfile(canonicalHeader, sampleBroker);

        canonicalScore.Should().Be(1.0m);
        canonicalScore.Should().BeGreaterThan(sampleBrokerScore);
    }
}
