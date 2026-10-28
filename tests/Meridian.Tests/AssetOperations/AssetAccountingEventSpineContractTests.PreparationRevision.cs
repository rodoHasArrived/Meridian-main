using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.AssetOperations;

public sealed partial class AssetAccountingEventSpineContractTests
{
    [Fact]
    public void PreparationRevision_LegacyJsonOmitsNullLinkAndRetainsTheSameFingerprintAfterReading()
    {
        var legacy = BuildProjected();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.SerializeToElement(legacy, options);
        var restored = json.Deserialize<AssetAccountingEventSpineDto>(options)!;

        json.TryGetProperty("preparationRevision", out _).Should().BeFalse();
        restored.PreparationRevision.Should().BeNull();
        AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(restored).Should()
            .Be(AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(legacy));
    }

    [Fact]
    public void PreparationRevision_ValidFactorSnapshotAllowsAnAppendVersionBeyondItsTwoStagePrefix()
    {
        var (previous, revised) = BuildFactorPreparationRevisionPair();

        var validateTransition = () => AssetAccountingPreparationRevisionValidator.ValidateTransition(previous, revised);

        validateTransition.Should().NotThrow();
        AssetAccountingEventSpineValidator.Validate(revised).Should().BeEmpty();
        revised.SpineVersion.Should().Be(3);
        revised.Stages.Select(stage => stage.Stage).Should().Equal(
            AssetAccountingLifecycleStageDto.Expected, AssetAccountingLifecycleStageDto.Projected);
        revised.EventId.Should().Be(previous.EventId);
        revised.EventVersion.Should().Be(previous.EventVersion);
    }

    [Fact]
    public void PreparationRevision_JsonRetainsTheExactLinkAndChangesTheImmutableSnapshotFingerprint()
    {
        var (_, revised) = BuildFactorPreparationRevisionPair();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.SerializeToElement(revised, options);
        var restored = json.Deserialize<AssetAccountingEventSpineDto>(options)!;

        json.GetProperty("preparationRevision").GetProperty("previousSpineVersion").GetInt64().Should().Be(2);
        restored.PreparationRevision.Should().BeEquivalentTo(revised.PreparationRevision);
        AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(restored).Should()
            .Be(AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(revised));
        AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(revised with { PreparationRevision = null }).Should()
            .NotBe(AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(revised),
                "the immutable fingerprint must bind the reviewed predecessor and recovery reason");
    }

    [Theory]
    [InlineData("zero-prior-version")]
    [InlineData("future-prior-version")]
    [InlineData("missing-hash")]
    [InlineData("missing-actor")]
    [InlineData("missing-reason")]
    [InlineData("default-time")]
    [InlineData("non-utc-time")]
    [InlineData("different-attestation-time")]
    [InlineData("missing-factor")]
    public void PreparationRevision_IncompleteMetadataOrUnmatchedAttestationsFailTheSpineContract(string mutation)
    {
        var (_, revised) = BuildFactorPreparationRevisionPair();
        var revision = revised.PreparationRevision!;
        revised = mutation switch
        {
            "zero-prior-version" => revised with { PreparationRevision = revision with { PreviousSpineVersion = 0 } },
            "future-prior-version" => revised with { PreparationRevision = revision with { PreviousSpineVersion = revised.SpineVersion } },
            "missing-hash" => revised with { PreparationRevision = revision with { PreviousCanonicalFingerprint = " " } },
            "missing-actor" => revised with { PreparationRevision = revision with { PreparedBy = " " } },
            "missing-reason" => revised with { PreparationRevision = revision with { Reason = " " } },
            "default-time" => revised with { PreparationRevision = revision with { PreparedAtUtc = default } },
            "non-utc-time" => revised with { PreparationRevision = revision with { PreparedAtUtc = revision.PreparedAtUtc.ToOffset(TimeSpan.FromHours(1)) } },
            "different-attestation-time" => revised with { PreparationRevision = revision with { PreparedAtUtc = revision.PreparedAtUtc.AddMinutes(1) } },
            _ => revised with { FactorPaydown = null }
        };

        AssetAccountingEventSpineValidator.Validate(revised).Should()
            .Contain(issue => issue.Contains("preparation revision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreparationRevision_CanonicalButDifferentPredecessorHashCannotAuthorizeRecovery()
    {
        var (previous, revised) = BuildFactorPreparationRevisionPair();
        revised = revised with
        {
            PreparationRevision = revised.PreparationRevision! with { PreviousCanonicalFingerprint = new string('f', 64) }
        };

        var validate = () => AssetAccountingPreparationRevisionValidator.ValidateTransition(previous, revised);

        validate.Should().Throw<InvalidOperationException>().WithMessage("*immediately preceding immutable snapshot*");
    }

    private static (AssetAccountingEventSpineDto Previous, AssetAccountingEventSpineDto Revised) BuildFactorPreparationRevisionPair()
    {
        var lot = FactorPaydownTestData.PremiumLot();
        var dimensions = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "company-alpha",
            InstrumentId: lot.SecurityId, BookId: lot.LedgerBookId.ToString("D"))
        { PositionId = lot.BookPositionId };
        var instruction = FactorPaydownTestData.Build([lot], 0.95m, 0.90m, dimensions: dimensions);
        var request = FactorPaydownTestData.ProjectRequest(instruction, dimensions);
        var previous = new AssetAccountingEventSpineDto(instruction.EconomicEvent.EventId, request.EventKind,
            instruction.EconomicEvent.EventVersion, 2, request.EconomicEvent.EffectiveDate, request.EventAmount,
            request.Currency, request.Scope, request.EconomicEvent, request.ProjectionLineage, request.RetainedEvidence,
            [new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Expected, request.ProjectedAtUtc,
                request.Actor, request.RetainedEvidence, $"economic-event://{request.EconomicEvent.EventId:D}/{request.EconomicEvent.EventVersion}"),
             new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Projected, request.ProjectedAtUtc,
                request.Actor, request.RetainedEvidence, $"projection-run://{request.ProjectionLineage.ProjectionRunId:D}")],
            request.ProjectedEffect, FactorPaydown: instruction);
        var at = request.ProjectedAtUtc.AddMinutes(1);
        var revised = previous with
        {
            SpineVersion = previous.SpineVersion + 1,
            Stages = previous.Stages.Select(stage => stage with { AttestedAtUtc = at }).ToArray(),
            PreparationRevision = new AssetAccountingPreparationRevisionDto(previous.SpineVersion,
                AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(previous), request.Actor, at,
                "Reprepare after correcting the proposed account mapping.")
        };
        return (previous, revised);
    }
}
