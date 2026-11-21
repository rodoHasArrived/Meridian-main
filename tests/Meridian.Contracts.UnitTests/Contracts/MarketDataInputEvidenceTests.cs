using FluentAssertions;
using Meridian.Contracts.Operations;

namespace Meridian.Tests.Contracts;

public sealed class MarketDataInputEvidenceTests
{
    [Fact]
    public void MissingEvidence_AddsNoLegacyTagsAndRemainsUnassessed()
    {
        MarketDataInputEvidenceTags.Create(null).Should().BeEmpty();
        MarketDataInputEvidenceTags.Read(null).Should().BeEmpty();
        MarketDataInputEvidenceTags.BuildWarnings([null]).Should().ContainSingle()
            .Which.Kind.Should().Be("Unknown");
    }

    [Fact]
    public void InvalidOptionalQuality_IsRetainedAsUnknownWithoutPreventingFillStorage()
    {
        var inputs = new MarketDataInputEvidence[]
        {
            new(ProviderId: "alpaca", QualityScore: double.NaN, QualityThreshold: double.PositiveInfinity,
                AssessmentFrom: new(2026, 11, 20), AssessmentTo: new(2026, 11, 1)), null!
        };
        var frozen = MarketDataInputEvidenceTags.Freeze(inputs)!;
        var tags = MarketDataInputEvidenceTags.Create(frozen);
        var reloaded = MarketDataInputEvidenceTags.Read(tags);

        reloaded.Should().HaveCount(2);
        reloaded[0].QualityScore.Should().BeNull();
        reloaded[0].EvidenceIssue.Should().Contain("Invalid quality");
        MarketDataInputEvidenceTags.BuildWarnings([reloaded]).Should().ContainSingle().Which.Kind.Should().Be("Unknown");
    }

    [Fact]
    public void RecordedThresholdAndAssessmentWindow_SurviveRoundTripAndDriveWarnings()
    {
        var input = new MarketDataInputEvidence(ProviderId: "alpaca", Repaired: false, GapFilled: false,
            QualityScore: .85, QualityThreshold: .9, QualityAssessedAt: DateTimeOffset.UtcNow,
            AssessmentFrom: new(2026, 11, 1), AssessmentTo: new(2026, 11, 20));
        var retained = MarketDataInputEvidenceTags.Read(MarketDataInputEvidenceTags.Create([input]));
        var warnings = MarketDataInputEvidenceTags.BuildWarnings([retained], minimumQualityScore: .1);

        warnings.Should().ContainSingle().Which.Kind.Should().Be("LowQuality");
        warnings[0].Evidence.Should().Equal(input);
    }

    [Fact]
    public void RepairedAndGapFilledWarnings_CountEachLogicalFillOnceAndNeverInventPnl()
    {
        var input = new MarketDataInputEvidence(ProviderId: "stooq", Repaired: true, GapFilled: true,
            RepairReceiptId: "retained-repair", QualityScore: .95, QualityThreshold: .8,
            QualityAssessedAt: DateTimeOffset.UtcNow, AssessmentFrom: new(2026, 11, 1), AssessmentTo: new(2026, 11, 20));
        var warnings = MarketDataInputEvidenceTags.BuildWarnings([[input, input], [input]]);

        warnings.Select(warning => warning.Kind).Should().BeEquivalentTo(["Repaired", "GapFilled"]);
        warnings.Should().OnlyContain(warning => warning.AffectedFillCount == 2);
    }

    [Fact]
    public void CorruptAndUnsupportedMetadata_NeverBecomeCleanCoverage()
    {
        foreach (var tags in new[]
        {
            new Dictionary<string, string> { [MarketDataInputEvidenceTags.VersionTag] = "9", [MarketDataInputEvidenceTags.InputsTag] = "[]" },
            new Dictionary<string, string> { [MarketDataInputEvidenceTags.VersionTag] = "1", [MarketDataInputEvidenceTags.InputsTag] = "corrupt" }
        })
            MarketDataInputEvidenceTags.BuildWarnings([MarketDataInputEvidenceTags.Read(tags)])
                .Should().ContainSingle().Which.Kind.Should().Be("Unknown");
    }
}
