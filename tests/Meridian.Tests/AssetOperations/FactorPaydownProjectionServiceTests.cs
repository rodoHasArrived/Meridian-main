using System.Globalization;
using FluentAssertions;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class FactorPaydownProjectionServiceTests
{
    private readonly FactorPaydownProjectionService _sut = new();

    [Fact]
    public void Project_ShouldCalculateGoldenMbsPrincipalAndTypedLineage()
    {
        var request = CreateRequest();

        var result = _sut.Project(request);

        result.Status.Should().Be(FactorPaydownProjectionStatus.Projected);
        result.ProducesPostingCandidate.Should().BeTrue();
        result.PrincipalPaydown.Should().Be(1_750m);
        result.EconomicEvent.Should().NotBeNull();
        result.EconomicEvent!.SecurityId.Should().Be(request.SecurityId);
        result.EconomicEvent.BookPositionId.Should().Be(request.PositionId);
        result.EconomicEvent.SourceContentHash.Should().Be("sha256:factor-row");
        result.EconomicEvent.EvidenceLinks.Should().Equal("evidence://factor/2026-04");
        result.EconomicState.Should().BeEquivalentTo(new
        {
            request.PositionId,
            Currency = "USD",
            Version = 8L,
            OriginalFaceAmount = 100_000m,
            CurrentFaceAmount = 96_250m,
            PriorFactor = 0.9800m,
            CurrentFactor = 0.9625m
        });
        result.Lineage.Should().BeEquivalentTo(new
        {
            ModelKey = FactorPaydownProjectionService.ModelKey,
            ModelVersion = FactorPaydownProjectionService.ModelVersion,
            EngineVersion = FactorPaydownProjectionService.EngineVersion,
            BookPositionId = (Guid?)request.PositionId
        });
    }

    [Fact]
    public void Project_ShouldProduceStableIdentityAcrossGenerationTimeAndCulture()
    {
        var request = CreateRequest();
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var first = _sut.Project(request with { GeneratedAtUtc = DateTimeOffset.Parse("2026-04-30T12:00:00Z") });
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
            var second = _sut.Project(request with { GeneratedAtUtc = DateTimeOffset.Parse("2027-01-01T12:00:00Z") });

            second.EconomicEvent!.EventId.Should().Be(first.EconomicEvent!.EventId);
            second.Lineage!.ProjectionRunId.Should().Be(first.Lineage!.ProjectionRunId);
            second.Lineage.ProjectionEventId.Should().Be(first.Lineage.ProjectionEventId);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Project_ShouldReturnNoChangeWithoutPostingCandidateForEqualFactors()
    {
        var result = _sut.Project(CreateRequest() with { CurrentFactor = 0.9800m });

        result.Status.Should().Be(FactorPaydownProjectionStatus.NoChange);
        result.ProducesPostingCandidate.Should().BeFalse();
        result.PrincipalPaydown.Should().BeNull();
        result.EconomicEvent.Should().BeNull();
    }

    [Theory]
    [InlineData("missing-evidence")]
    [InlineData("non-positive-face")]
    [InlineData("prior-factor-out-of-range")]
    [InlineData("current-factor-out-of-range")]
    [InlineData("factor-increase")]
    [InlineData("stale-version")]
    [InlineData("invalid-currency")]
    [InlineData("missing-source-hash")]
    public void Project_ShouldFailClosedForInvalidEconomicsOrProvenance(string scenario)
    {
        var request = scenario switch
        {
            "missing-evidence" => CreateRequest() with { EvidenceLinks = [] },
            "non-positive-face" => CreateRequest() with { HeldFace = 0m },
            "prior-factor-out-of-range" => CreateRequest() with { PriorFactor = 1.01m },
            "current-factor-out-of-range" => CreateRequest() with { CurrentFactor = -0.01m },
            "factor-increase" => CreateRequest() with { CurrentFactor = 0.99m },
            "stale-version" => CreateRequest() with { ExpectedPositionVersion = 6 },
            "invalid-currency" => CreateRequest() with { Currency = "US" },
            "missing-source-hash" => CreateRequest() with { SourceContentHash = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = _sut.Project(request);

        result.Status.Should().Be(FactorPaydownProjectionStatus.Rejected);
        result.ProducesPostingCandidate.Should().BeFalse();
        result.Issues.Should().NotBeEmpty();
    }

    [Fact]
    public void Project_ShouldRejectPositivePrincipalBelowCurrencyMinorUnit()
    {
        var result = _sut.Project(CreateRequest() with
        {
            HeldFace = 1m,
            PriorFactor = 1m,
            CurrentFactor = 0.999m
        });

        result.Status.Should().Be(FactorPaydownProjectionStatus.Rejected);
        result.Issues.Should().ContainSingle(issue => issue.Code == "factor-paydown.unroundable-currency-result");
    }

    [Fact]
    public void Project_ShouldRejectVersionOverflow()
    {
        var result = _sut.Project(CreateRequest() with
        {
            PositionVersion = long.MaxValue,
            ExpectedPositionVersion = long.MaxValue
        });

        result.Status.Should().Be(FactorPaydownProjectionStatus.Rejected);
        result.Issues.Should().ContainSingle(issue => issue.Code == "factor-paydown.amount-overflow");
    }

    private static FactorPaydownProjectionRequest CreateRequest()
        => new(
            Guid.Parse("99999999-1111-2222-3333-444444444444"),
            Guid.Parse("88888888-1111-2222-3333-444444444444"),
            7,
            7,
            100_000m,
            0.9800m,
            0.9625m,
            "USD",
            new DateOnly(2026, 4, 30),
            DateTimeOffset.Parse("2026-04-30T08:00:00Z"),
            "SecurityMaster",
            "factor-row-2026-04",
            "sha256:factor-row",
            ["evidence://factor/2026-04"]);
}
