using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed partial class InMemoryAssetAccountingEventProjectionStoreTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AppendAsync_FactorPreparationRecoveryAppendsCurrentAssertionsWithoutRewritingHistory(bool projectedFirst, bool advancePosition)
    {
        var positionProjection = InstrumentPositionProjectionFixture.Create();
        var store = new InMemoryAssetOperationsProjectionStore();
        await store.UpsertAsync(positionProjection, InstrumentPositionProjectionFixture.Approval);
        var (expected, projected) = BuildFactorPreparationVersions();
        var originalExpected = await store.AppendAsync(expected, 0, 4);
        if (projectedFirst)
            await store.AppendAsync(projected, 1, 4);
        var previous = (await store.GetLatestAsync(expected.EventId, expected.EventVersion))!;
        if (advancePosition)
        {
            await store.UpsertAsync(InstrumentPositionProjectionFixture.Advance(positionProjection), InstrumentPositionProjectionFixture.Approval);
        }
        var positionVersion = advancePosition ? 5 : 4;
        var revised = ReviseFactorPreparation(previous.Projection, positionVersion);
        AssetAccountingEventSpineValidator.Validate(revised).Should().BeEmpty();

        var appended = await store.AppendAsync(revised, previous.Projection.SpineVersion, positionVersion);
        var replay = await store.AppendAsync(revised, previous.Projection.SpineVersion, positionVersion);

        appended.WasReplay.Should().BeFalse();
        replay.WasReplay.Should().BeTrue();
        replay.CanonicalFingerprint.Should().Be(appended.CanonicalFingerprint);
        var latest = (await store.GetLatestAsync(expected.EventId, 1))!;
        latest.Projection.Should().BeEquivalentTo(revised);
        latest.Projection.Stages.Should().HaveCount(2);
        latest.Projection.ProjectedEffect!.Lines[0].AccountId.Should().Be("Assets:CorrectedCash");
        latest.Projection.Scope.ExpectedSecurityVersion.Should().Be(3);
        latest.Projection.Scope.ExpectedBookPositionVersion.Should().Be(positionVersion);
        latest.Projection.FactorPaydown!.ExpectedPeriodVersion.Should().Be(2);
        latest.Projection.FactorPaydown.Lots.Should().OnlyContain(lot => lot.ExpectedLot.Version == 2);
        latest.Projection.PreparationRevision!.PreviousCanonicalFingerprint.Should().Be(previous.CanonicalFingerprint);
        (await store.GetAsync(previous.Projection.EventId, 1, previous.Projection.SpineVersion)).Should().BeEquivalentTo(previous);
        (await store.GetAsync(expected.EventId, 1, 1))!.CanonicalFingerprint.Should().Be(originalExpected.CanonicalFingerprint);
        (await store.GetLatestAsync(expected.EventId, 2)).Should().BeNull("recovery never creates another economic-event version");
        var rival = revised with { PreparationRevision = revised.PreparationRevision with { Reason = "A different attempted recovery" } };
        var conflicting = () => store.AppendAsync(rival, previous.Projection.SpineVersion, positionVersion);
        await conflicting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different canonical fingerprint*");
        (await store.GetLatestAsync(expected.EventId, 1))!.CanonicalFingerprint.Should().Be(appended.CanonicalFingerprint);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("prior-version")]
    [InlineData("actor")]
    [InlineData("actor-case")]
    [InlineData("source")]
    [InlineData("fund")]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("period")]
    [InlineData("basis")]
    [InlineData("evidence")]
    [InlineData("factors")]
    [InlineData("lineage")]
    [InlineData("future-time")]
    public async Task AppendAsync_FactorPreparationRecoveryRefusesChangedAuthorityAndPreservesTheRetainedSnapshot(string mutation)
    {
        var store = await SeedStoreAsync();
        var (expected, projected) = BuildFactorPreparationVersions();
        await store.AppendAsync(expected, 0, 4);
        await store.AppendAsync(projected, 1, 4);
        var previous = (await store.GetLatestAsync(expected.EventId, 1))!;
        var revised = ReviseFactorPreparation(previous.Projection, 4);
        var revision = revised.PreparationRevision!;
        switch (mutation)
        {
            case "hash":
                revised = revised with { PreparationRevision = revision with { PreviousCanonicalFingerprint = Sha256Digest.ComputeUtf8("another snapshot") } };
                break;
            case "prior-version":
                revised = revised with { PreparationRevision = revision with { PreviousSpineVersion = revision.PreviousSpineVersion - 1 } };
                break;
            case "actor":
            case "actor-case":
                var actor = mutation == "actor" ? "another-projector" : revision.PreparedBy.ToUpperInvariant();
                revised = revised with
                {
                    PreparationRevision = revision with { PreparedBy = actor },
                    Stages = revised.Stages.Select(stage => stage with { AttestedBy = actor }).ToArray()
                };
                break;
            case "source":
                var economicEvent = revised.EconomicEvent with { SourceDomain = "ChangedSourceDomain" };
                var sourceLineage = revised.ProjectionLineage with { TriggerEvent = economicEvent };
                revised = revised with
                {
                    EconomicEvent = economicEvent,
                    ProjectionLineage = sourceLineage,
                    FactorPaydown = revised.FactorPaydown! with { EconomicEvent = economicEvent, Lineage = sourceLineage }
                };
                break;
            case "fund":
                revised = WithFactorScope(revised, revised.Scope with { FundProfileId = "foreign-fund" });
                break;
            case "tenant":
                revised = WithFactorScope(revised, revised.Scope with { TenantId = "foreign-tenant" });
                break;
            case "company":
                revised = WithFactorScope(revised, revised.Scope with { CompanyId = "foreign-company" });
                break;
            case "period":
                revised = WithFactorScope(revised, revised.Scope with { PeriodId = Guid.NewGuid() });
                break;
            case "basis":
                revised = WithFactorScope(revised, revised.Scope with { AccountingBasis = AccountingBasisKindDto.Tax });
                break;
            case "evidence":
                var extra = revised.RetainedEvidence[0] with { EvidenceId = "another-factor-artifact", EvidenceUri = "evidence://factor/replaced" };
                revised = revised with { RetainedEvidence = [.. revised.RetainedEvidence, extra] };
                break;
            case "factors":
                revised = revised with { FactorPaydown = revised.FactorPaydown! with { NewFactor = 0.89m } };
                break;
            case "lineage":
                var changedLineage = revised.ProjectionLineage with { ModelVersion = "changed-model-version" };
                revised = revised with
                {
                    ProjectionLineage = changedLineage,
                    ProjectedEffect = revised.ProjectedEffect! with { ModelVersion = changedLineage.ModelVersion },
                    FactorPaydown = revised.FactorPaydown! with { Lineage = changedLineage }
                };
                break;
            default:
                var future = DateTimeOffset.UtcNow.AddHours(1);
                revised = revised with
                {
                    PreparationRevision = revision with { PreparedAtUtc = future },
                    Stages = revised.Stages.Select(stage => stage with { AttestedAtUtc = future }).ToArray()
                };
                break;
        }

        var append = () => store.AppendAsync(revised, previous.Projection.SpineVersion, 4);

        await append.Should().ThrowAsync<InvalidOperationException>();
        (await store.GetLatestAsync(expected.EventId, 1)).Should().BeEquivalentTo(previous);
        (await store.GetAsync(expected.EventId, 1, 3)).Should().BeNull();
    }

    [Theory]
    [InlineData(AssetAccountingLifecycleStageDto.Drafted)]
    [InlineData(AssetAccountingLifecycleStageDto.Approved)]
    [InlineData(AssetAccountingLifecycleStageDto.Posted)]
    public void ValidateTransition_FactorPreparationRecoveryCannotReplaceAnAdvancedLifecycle(AssetAccountingLifecycleStageDto finalStage)
    {
        var (_, projected) = BuildFactorPreparationVersions();
        var previous = projected with
        {
            Stages = [.. projected.Stages, projected.Stages[^1] with { Stage = finalStage }]
        };
        var revised = ReviseFactorPreparation(previous, 4);

        var recover = () => AssetAccountingPreparationRevisionValidator.ValidateTransition(previous, revised);

        recover.Should().Throw<InvalidOperationException>().WithMessage("*Expected or Projected factor preparation*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateStreamContinuity_OrdinaryLifecycleCannotDropOrRewriteItsPreparationLink(bool dropLink)
    {
        var (_, projected) = BuildFactorPreparationVersions();
        var revised = ReviseFactorPreparation(projected, 4);
        var drafted = revised with
        {
            SpineVersion = revised.SpineVersion + 1,
            Stages = [.. revised.Stages, revised.Stages[^1] with { Stage = AssetAccountingLifecycleStageDto.Drafted }],
            PreparationRevision = dropLink ? null : revised.PreparationRevision! with { Reason = "Rewritten after preparation" }
        };

        var append = () => AssetAccountingEventProjectionRules.ValidateStreamContinuity(revised, drafted);

        append.Should().Throw<InvalidOperationException>();
    }

    private static (AssetAccountingEventSpineDto Expected, AssetAccountingEventSpineDto Projected) BuildFactorPreparationVersions()
    {
        var lot = FactorPaydownTestData.PremiumLot(InstrumentPositionProjectionFixture.SecurityId,
            InstrumentPositionProjectionFixture.PositionId, InstrumentPositionProjectionFixture.LedgerBookId);
        var dimensions = new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "company-alpha",
            InstrumentId: lot.SecurityId, BookId: lot.LedgerBookId.ToString("D"))
        { PositionId = lot.BookPositionId };
        var instruction = FactorPaydownTestData.Build([lot], 0.95m, 0.90m, securityVersion: 2, positionVersion: 4,
            periodId: PeriodId, dimensions: dimensions);
        var request = FactorPaydownTestData.ProjectRequest(instruction, dimensions);
        var expected = new AssetAccountingEventSpineDto(request.EconomicEvent.EventId, request.EventKind,
            request.EconomicEvent.EventVersion, 1, request.EconomicEvent.EffectiveDate, request.EventAmount, request.Currency,
            request.Scope, request.EconomicEvent, request.ProjectionLineage, request.RetainedEvidence,
            [new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Expected, request.ProjectedAtUtc,
                request.Actor, request.RetainedEvidence, $"economic-event://{request.EconomicEvent.EventId:D}/1")],
            FactorPaydown: instruction);
        return (expected, expected with
        {
            SpineVersion = 2,
            ProjectedEffect = request.ProjectedEffect,
            Stages = [.. expected.Stages, new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Projected,
                request.ProjectedAtUtc, request.Actor, request.RetainedEvidence, $"projection-run://{request.ProjectionLineage.ProjectionRunId:D}")]
        });
    }

    private static AssetAccountingEventSpineDto ReviseFactorPreparation(AssetAccountingEventSpineDto previous, long positionVersion)
    {
        var source = previous.FactorPaydown!;
        var request = FactorPaydownTestData.ProjectRequest(source, previous.Scope.Dimensions!, cashPath: "Assets:CorrectedCash");
        var at = previous.Stages[^1].AttestedAtUtc.AddMinutes(1);
        var scope = previous.Scope with { ExpectedSecurityVersion = 3, ExpectedBookPositionVersion = positionVersion };
        return previous with
        {
            SpineVersion = previous.SpineVersion + 1,
            Scope = scope,
            FactorPaydown = source with
            {
                Scope = scope,
                ExpectedPeriodVersion = 2,
                ExpectedSecurityHash = Sha256Digest.ComputeUtf8("current-security"),
                Lots = source.Lots.Select(lot => lot with { ExpectedLot = lot.ExpectedLot with { Version = 2 } }).ToArray()
            },
            ProjectedEffect = request.ProjectedEffect,
            Stages = [new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Expected, at,
                    previous.Stages[0].AttestedBy, previous.RetainedEvidence, previous.Stages[0].ReferenceId),
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Projected, at,
                    previous.Stages[0].AttestedBy, previous.RetainedEvidence, $"projection-run://{previous.ProjectionLineage.ProjectionRunId:D}")],
            PreparationRevision = new AssetAccountingPreparationRevisionDto(previous.SpineVersion,
                AssetAccountingPreparationRevisionValidator.ProjectionFingerprint(previous), previous.Stages[0].AttestedBy, at,
                "Correct the proposed cash mapping after refreshing source version assertions.")
        };
    }

    private static AssetAccountingEventSpineDto WithFactorScope(AssetAccountingEventSpineDto projection, AssetAccountingEventScopeDto scope)
        => projection with { Scope = scope, FactorPaydown = projection.FactorPaydown! with { Scope = scope } };
}
