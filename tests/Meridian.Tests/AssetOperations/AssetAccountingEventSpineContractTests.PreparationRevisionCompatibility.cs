using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.AssetOperations;

public sealed partial class AssetAccountingEventSpineContractTests
{
    [Fact]
    public void PreparationRevision_PreservesTheOriginalTwentyFiveArgumentConstructorAndDeconstructor()
    {
        // These are the exact CLR signatures shipped before preparation recovery. Deriving them
        // from the current constructor would miss a positional addition that breaks compiled callers.
        Type[] originalParameterTypes =
        [
            typeof(Guid),
            typeof(AssetAccountingEventKindDto),
            typeof(long),
            typeof(long),
            typeof(DateOnly),
            typeof(decimal),
            typeof(string),
            typeof(AssetAccountingEventScopeDto),
            typeof(EconomicEventReferenceDto),
            typeof(ProjectionLineageDto),
            typeof(IReadOnlyList<RetainedEvidenceIdentityDto>),
            typeof(IReadOnlyList<AssetAccountingStageEvidenceDto>),
            typeof(ProjectedAccountingEffectDto),
            typeof(PostedJournalImpactDto),
            typeof(Guid?),
            typeof(AssetAccountingCorrectionReferenceDto),
            typeof(PostingRuleJournalCandidateRequestDto),
            typeof(PostingRuleJournalCandidateResultDto),
            typeof(string),
            typeof(string),
            typeof(AssetLotMutationInstructionDto),
            typeof(string),
            typeof(OpenLotSuccessorInstructionDto),
            typeof(OpenLotCapitalReturnInstructionDto),
            typeof(OpenLotFactorPaydownInstructionDto)
        ];
        var legacy = BuildProjected();
        object?[] originalArguments =
        [
            legacy.EventId,
            legacy.EventKind,
            legacy.EventVersion,
            legacy.SpineVersion,
            legacy.EffectiveDate,
            legacy.EventAmount,
            legacy.Currency,
            legacy.Scope,
            legacy.EconomicEvent,
            legacy.ProjectionLineage,
            legacy.RetainedEvidence,
            legacy.Stages,
            legacy.ProjectedEffect,
            legacy.PostedJournalImpact,
            legacy.TaxLotMutationBatchId,
            legacy.Correction,
            legacy.DraftedCandidate,
            legacy.DraftedCandidateResult,
            legacy.DraftedCandidateFingerprint,
            legacy.DraftedCandidateResultFingerprint,
            legacy.DraftedLotMutation,
            legacy.DraftedLotMutationFingerprint,
            legacy.CorporateAction,
            legacy.CapitalReturn,
            legacy.FactorPaydown
        ];

        var constructor = typeof(AssetAccountingEventSpineDto).GetConstructor(originalParameterTypes);
        constructor.Should().NotBeNull("compiled producers bind the original twenty-five-argument constructor");
        var restored = (AssetAccountingEventSpineDto)constructor!.Invoke(originalArguments);
        restored.Should().BeEquivalentTo(legacy, options => options.WithStrictOrdering());
        restored.PreparationRevision.Should().BeNull();

        var deconstructor = typeof(AssetAccountingEventSpineDto).GetMethod("Deconstruct",
            originalParameterTypes.Select(static type => type.MakeByRefType()).ToArray());
        deconstructor.Should().NotBeNull("compiled deconstruction binds the original by-reference parameter list");
        var outputs = new object?[25];
        deconstructor!.Invoke(restored, outputs);
        outputs.Should().Equal(originalArguments);
    }

    [Fact]
    public void PreparationRevision_LegacySnapshotKeepsThePreRecoveryCanonicalBytes()
    {
        // Captured from the unchanged BuildProjected fixture using the pre-recovery, 25-argument
        // Contracts assembly. Main 62a6147c and pre-recovery 8bf093eb have identical DTO/fixture sources.
        // This pins old bytes rather than comparing two serializations produced by the new codec.
        const string originalFingerprint = "fcfacbfb7ac49bd54789c49b4529d9a992ed8d3fd24a4ab22d606a9ced0eddf6";
        var legacy = BuildProjected();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        bytes.Should().HaveCount(4883);
        Sha256Digest.Compute(bytes).Should().Be(originalFingerprint);
        AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(legacy).Should().Be(originalFingerprint);
    }

    [Fact]
    public void PreparationRevision_NonPositionalLinkSurvivesRecordCopyAndJsonRoundTrip()
    {
        var (_, revised) = BuildFactorPreparationRevisionPair();
        var copy = revised with { Stages = revised.Stages.ToArray() };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.SerializeToElement(copy, options);
        var restored = json.Deserialize<AssetAccountingEventSpineDto>(options)!;

        copy.PreparationRevision.Should().BeSameAs(revised.PreparationRevision);
        json.GetProperty("preparationRevision").GetProperty("previousCanonicalFingerprint").GetString()
            .Should().Be(revised.PreparationRevision!.PreviousCanonicalFingerprint);
        restored.Should().BeEquivalentTo(revised, configuration => configuration.WithStrictOrdering());
        AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(restored).Should()
            .Be(AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(revised));
    }
}
