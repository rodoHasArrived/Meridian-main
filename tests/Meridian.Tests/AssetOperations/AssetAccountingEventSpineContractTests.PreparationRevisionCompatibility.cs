using System.IO.Compression;
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
    public void PreparationRevision_PopulatedSnapshotKeepsThePositionalRecoveryCanonicalBytes()
    {
        // Actual pre-e2dde28b populated bytes, captured by the scratch reflection oracle at
        // /workspace/scratch/meridian-spine-positional-baseline-20261028/revised-positional.json.
        // Old Contracts assembly SHA-256: 670787aec7d91575ad8db738320d80d19dba0db80493fd400c90da383e901074.
        // Keep the captured identities: the original fixture generates fresh GUIDs on each call.
        const string originalFingerprint = "7a9a78475015f9d20b1f2432c7f6a09bec33cd45ccc2bd529f9d9e9f756a84f8";
        const string originalPayloadGzipBase64 = """
            H4sIAAAAAAACA+1cbXPbNhL+Kx5+PSMG30l9U2Snl2ty9dlOZ25u8gEEljKvFMGSoGM34/9+C5CUKFlyFLfNdVp4PB4RL0tg99ln
            dwnKnx24g0q9Fc7MESyIRZT7xAM/JXkceoS5cUbcNBKhF2VJnFLntJ/wfVHpKQvZ1LJhCuZcFbIae3+EptWXM/fUaeuignWDjyPy
            HHD0HZzjPJThUS8iNCL+Wvh8JbtKObOQ4s8rSk8d3jUNVPwBh3+4PsdxLZc1Tv7stIB9hXrodwAoy89SwjMvJEGQRyTxaUx4FOQi
            jDw/yUJ9k/saVwDiepg7XW4m5U+Xsi30dozMhKVCZKlH8iRMSEBDIGmeeAQERJCnIYjEm8h8PZk/lVuCWEKje4eVxjTwXECpMZAg
            DTKScl+QMGZCeEDzOEpQag1NIYWZ4bM08zjgjiJOSZB4AWFZGBAhQgEJGiow1mGca+UV1fI1a4sWJ37HWI0deVeJy0bmRQlGnr4m
            rKxvGXYqqNiAgv7juofLVc2qXr3D53WfKFZQ6R222hJa4FPJaM7ROrvT2xLQ3Lqr6soSr5VG0vJh01JUd9Aq2WxaOKsLxcp5v8vp
            SJzdrdZQPg4Iit2/kxMpXLZqgTJgekd9I2hq1qjJymSzZFXxCxth0reiL6hcloXcNGVfaXG2uzPeoQZW0xWhi4ipTupG/hext2mA
            e1xwxcrvyvOphR71Ar8W2Y84C7is5KrgF9o5talfxhk3D9plnXnbgpqvYfrqCA55ljIkN+wg5uqD4ludN24yoxR//2b+asjJruFw
            LlesQMnOSADvWYsaW/dfbDCbM474I438RLRcGhmXwNuVO6bnrGt3mnphC4nwqdTfWXuL8sLASxMvox7z4zQOMp+ymOcudynzuB/5
            LvUpx/t4jEHM4jAMU5rE+Cdg1Bes12UhkAvhXVH9hGb9z7phdnbWL/cMlzsZiO2M/9wVvenP8oD7NOVAGN6PBCyKkVIyn0AGInOx
            0Yup8/H0Rcz6UvZsQKFFQFwMS8ZtfV6vf2oIA43J3j40BXYe0gDf6P76lnlh9NtYoDfs9QOCZnUYRVeQQ9NvZi+MGlw1fLpWTHWa
            ptEjoMYAsu7BWPJgWLNSjSxLI3nsOQbrX4izvcamXjYa4YBwdyp8HGuWOAojd6wrtXXaLtOMtN/hLwYTDoOOp5HH0x1MTFBNjkL1
            M7j5ag/Zjy74lT9P0fXOJA77UXXcOo8E2nBxGGYuccMbmh4FMz32q2A2CvfpQZjhhc4m0NgtNHcFhydI+6GGCgP6fGPLHZwdpbHH
            j4/roIoikGeBLU2yuWm86np+i7IMmSPnJAfhEz8NM8I4y0iQ5Z7vJiL2Qq3ozcSLTeiMkpj7eUIYpD6hInRJRt2QxH5OE98N/IAG
            OHUlBZTfg1bAKmvJwCQ1exDyUzX2r/XruK8wYzZ513Kadzvb88hmQeTONfk0pn2Ya+JIzBtha8nz9od8H4csoQIdt79d5FVNsURn
            sHmIzUNsHmLzEJuH2Dzkz56HoCev2vWye0I3bb8dkbddjfKRCcWQ1QxPEb4Rv1uutlxtudpy9Z+mZkRKMvv7VRXjC8u+L1dtSipW
            nkNWqFafsMRhf8JimhdYvkzb9xy8lKja1lDt5El1767tbKFj0ikm6Sh96/jGyHVm+wQKaHlT1GoS3+y5wl/iXGEvhN4a06x6uh+A
            NEHQYWBaHFkc6VkX90joLcyugJXFLyBO3sm23WBp5DtLSRZKT6CE1UjRvmYlw/QDp6GaoZeEMf0fmErhjd6iYXVoHyoxo9X3nTLa
            ec0Uv52qE3HFt1DUsBxFLVglCmFi8/72K2h1Unyg9w0mxGilpqjU8wIOD5ws+nDPnul9GnI5ZCH6Caw9GraPZO0jWVvm2zLflvl/
            kTK/tAfC9kDYZh82+7DZh80+bPZhsw97IGwPhC1XW662XG25eour7bfU7LfU7NHNt/qW2oDtS4PGrSp9x5OGTCmP44jziOn4HQQ0
            4yGiN09yjOg88mmS+KEfpl5CQ+ZCyLlHsySMXLwj5EkS++aZSCGbNyYuOzP6Kg1PnQo+TRqoGVPxombl+tjkme+1llL1b1eMa0bL
            a9j2GLgCLPHF8ST0bWvll5FH2UMbjS5LghfENfhC8m1A7AsHsimWBeLmXx0zXuvMUqNQ7EHm3t9607CqZeYxzgK9ZyCdNAq8/m0C
            PeZNV5kBrNw75G4CqEmc18b5ebjnyGVof3C2Ri127ZxvbrbbNZn25v4KFXAjN0szt1df2E3+/E5uZSmQeXs/wYjeqH1qxmwTfmRl
            Bze6uDAPO1kzyHK1XjVMQGyDn62QWwbCeQ/qVmrL/lPOJ83TaP/vAsqBDx43cf5p3m5zNJuj/T452iRu9I6otgG9Ju8pa0/8zzjE
            PNcl2yx1/ZDuOOD+fiTxwjgzHd4bBPQt4x1XqMh2oJVaW0d27fXWf1DwTtcdC1bJquCs3DqpdzggI7uMsgxDW8RSLwyyJEgijydp
            hDVoyjnHkijlNEriLHN9N4PMA4GpW54lUR/X9IoGcwyRWzaTjn3lXfqkvGOtOWq4gmHaiX7FoDkZX4yolifqFk5QPkZ8ECdDVnGy
            YnWtH8BjsWaLelvU26LeBow/SsDQ9R9bDm9Am4/DK4e81xtTClp1HD+OY5+QbDNa0qxsfMWqZzgE5DEucaaz6EoqvdLtp7eWQC2B
            WgK1BPp/eyq64c3L8fsyvw9xTl5paboKgXjkuzmWNi1tWtq0tPmHos2Pj/8DleuxP6tRAAA=
            """;
        using var compressed = new MemoryStream(Convert.FromBase64String(originalPayloadGzipBase64));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var payload = new MemoryStream();
        gzip.CopyTo(payload);
        var originalBytes = payload.ToArray();
        originalBytes.Should().HaveCount(20907);
        Sha256Digest.Compute(originalBytes).Should().Be(originalFingerprint);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var revised = JsonSerializer.Deserialize<AssetAccountingEventSpineDto>(originalBytes, options)!;
        revised.PreparationRevision.Should().NotBeNull();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(revised, options);

        bytes.Should().Equal(originalBytes, "existing populated recovery snapshots must reopen with their exact canonical bytes");
        Sha256Digest.Compute(bytes).Should().Be(originalFingerprint);
        AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(revised).Should().Be(originalFingerprint);
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
