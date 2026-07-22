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
