using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// End-to-end coverage of the append-only Asset Accounting Event Spine projection store against
/// the real in-memory implementation, which routes every write through the same
/// <see cref="AssetAccountingEventProjectionRules"/> as the Postgres store.
/// </summary>
public sealed class InMemoryAssetAccountingEventProjectionStoreTests
{
    private static readonly Guid EventId = Guid.Parse("a3000000-eeee-4000-8000-000000000010");
    private static readonly Guid PeriodId = Guid.Parse("a3000000-eeee-4000-8000-000000000011");
    private static readonly DateOnly EffectiveDate = new(2026, 6, 30);
    private static readonly DateTimeOffset OccurredAt = DateTimeOffset.Parse("2026-06-30T18:00:00Z");

    [Fact]
    public async Task AppendAsync_ExpectedThenProjected_RetainsSeparateDurableVersions()
    {
        var store = await SeedStoreAsync();
        var (expected, projected) = BuildSpineVersions();

        var expectedAppend = await store.AppendAsync(expected, 0, 4);
        var projectedAppend = await store.AppendAsync(projected, 1, 4);

        expectedAppend.WasReplay.Should().BeFalse();
        projectedAppend.WasReplay.Should().BeFalse();
        var retainedExpected = await store.GetAsync(EventId, 1, 1);
        retainedExpected!.Projection.Stages.Should().ContainSingle()
            .Which.Stage.Should().Be(AssetAccountingLifecycleStageDto.Expected);
        retainedExpected.Projection.ProjectedEffect.Should().BeNull();
        retainedExpected.CanonicalFingerprint.Should().Be(expectedAppend.CanonicalFingerprint);
        var latest = await store.GetLatestAsync(EventId, 1);
        latest!.Projection.SpineVersion.Should().Be(2);
        latest.Projection.Stages.Select(static stage => stage.Stage).Should().Equal(
            AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected);
        latest.Projection.ProjectedEffect.Should().NotBeNull();
        latest.CanonicalFingerprint.Should().Be(projectedAppend.CanonicalFingerprint);
    }

    [Fact]
    public async Task AppendAsync_IdenticalProjectedPayload_ReturnsRetainedRecordAsReplay()
    {
        var store = await SeedStoreAsync();
        var (expected, projected) = BuildSpineVersions();
        await store.AppendAsync(expected, 0, 4);
        var first = await store.AppendAsync(projected, 1, 4);

        var replay = await store.AppendAsync(projected, 1, 4);

        replay.WasReplay.Should().BeTrue();
        replay.CanonicalFingerprint.Should().Be(first.CanonicalFingerprint);
        (await store.GetLatestAsync(EventId, 1))!.Projection.SpineVersion.Should().Be(2);
    }

    [Fact]
    public async Task AppendAsync_SameVersionDifferentPayload_FailsClosedOnCanonicalFingerprint()
    {
        var store = await SeedStoreAsync();
        var (expected, projected) = BuildSpineVersions();
        await store.AppendAsync(expected, 0, 4);
        await store.AppendAsync(projected, 1, 4);
        var mutated = projected with
        {
            Stages =
            [
                projected.Stages[0],
                projected.Stages[1] with { Notes = "rewritten attestation" }
            ]
        };

        var act = () => store.AppendAsync(mutated, 1, 4);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different canonical fingerprint*");
    }

    [Fact]
    public async Task AppendAsync_StaleSpineVersionAssertions_FailClosed()
    {
        var store = await SeedStoreAsync();
        var (expected, projected) = BuildSpineVersions();

        var casMiss = () => store.AppendAsync(projected, 1, 4);
        await casMiss.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current version is '0'*");

        await store.AppendAsync(expected, 0, 4);
        var nonConsecutive = () => store.AppendAsync(projected, 0, 4);
        await nonConsecutive.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*next consecutive spine version*");
    }

    [Fact]
    public async Task AppendAsync_StaleOrUnknownBookPositionAssertion_FailsClosed()
    {
        var store = await SeedStoreAsync();
        var (expected, _) = BuildSpineVersions();
        var staleScope = expected with
        {
            Scope = expected.Scope with { ExpectedBookPositionVersion = 3 }
        };

        var staleVersion = () => store.AppendAsync(staleScope, 0, 3);
        await staleVersion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*stale or mismatched book-position assertion*");

        var unknownPositionId = Guid.NewGuid();
        var unknownPosition = expected with
        {
            Scope = expected.Scope with { BookPositionId = unknownPositionId },
            EconomicEvent = expected.EconomicEvent with { BookPositionId = unknownPositionId },
            ProjectionLineage = expected.ProjectionLineage with
            {
                BookPositionId = unknownPositionId,
                TriggerEvent = expected.EconomicEvent with { BookPositionId = unknownPositionId }
            }
        };
        unknownPosition = unknownPosition with
        {
            EconomicEvent = unknownPosition.ProjectionLineage.TriggerEvent
        };

        var missing = () => store.AppendAsync(unknownPosition, 0, 4);
        await missing.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*was not found*");
    }

    [Fact]
    public async Task AppendAsync_LifecycleAppendCannotRewriteOrSkipCanonicalStages()
    {
        var store = await SeedStoreAsync();
        var (expected, projected) = BuildSpineVersions();
        await store.AppendAsync(expected, 0, 4);
        await store.AppendAsync(projected, 1, 4);

        var rewrittenAmount = projected with { SpineVersion = 3, EventAmount = 999m };
        var rewrite = () => store.AppendAsync(rewrittenAmount, 2, 4);
        await rewrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot rewrite the immutable source projection*");

        var noNewStage = projected with { SpineVersion = 3 };
        var stageless = () => store.AppendAsync(noNewStage, 2, 4);
        await stageless.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exactly one next canonical stage*");
    }

    [Fact]
    public async Task AppendAsync_PostedImpactWithoutDurableJournalAuthority_FailsClosed()
    {
        var store = await SeedStoreAsync();
        var (_, projected) = BuildSpineVersions();
        var posted = projected with
        {
            SpineVersion = 3,
            PostedJournalImpact = new PostedJournalImpactDto(
                Guid.NewGuid(), projected.Scope.LedgerBookId, projected.Scope.PeriodId,
                AccountingBasisKindDto.Gaap, OccurredAt.AddHours(1), JournalPostingStatusDto.Posted,
                "USD", 100m, 100m,
                [
                    new PostedJournalImpactLineDto(Guid.NewGuid(), "Assets:Investment", 100m, 0m, "USD"),
                    new PostedJournalImpactLineDto(Guid.NewGuid(), "Income:Unrealized", 0m, 100m, "USD")
                ])
        };

        var act = () => store.AppendAsync(posted, 2, 4);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires a configured durable journal authority*");
    }

    private static async Task<InMemoryAssetOperationsProjectionStore> SeedStoreAsync()
    {
        var store = new InMemoryAssetOperationsProjectionStore();
        await store.UpsertAsync(
            InstrumentPositionProjectionFixture.Create(),
            InstrumentPositionProjectionFixture.Approval);
        return store;
    }

    private static (AssetAccountingEventSpineDto Expected, AssetAccountingEventSpineDto Projected) BuildSpineVersions()
    {
        var securityId = InstrumentPositionProjectionFixture.SecurityId;
        var positionId = InstrumentPositionProjectionFixture.PositionId;
        var bookId = InstrumentPositionProjectionFixture.LedgerBookId;
        var sourceHash = new string('c', 64);
        var evidence = new RetainedEvidenceIdentityDto(
            "valuation-source", "evidence://asset/valuation-source", sourceHash,
            "Custodian", "valuation-source-entity", RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "controller", OccurredAt.AddSeconds(10), EffectiveDate, 1, OccurredAt.AddSeconds(20),
            "retention-service", AssetAccountingEvidenceSubjects.Event, EventId.ToString("D"));
        var economicEvent = new EconomicEventReferenceDto(
            EventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Valuation), 1,
            EffectiveDate, OccurredAt, "AssetOperations", "valuation-source-entity",
            SourceContentHash: sourceHash)
        {
            SecurityId = securityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            Guid.Parse("a3000000-eeee-4000-8000-000000000012"), null, "valuation", "v1", "engine-v1",
            "base", EffectiveDate, OccurredAt.AddMinutes(2), "AssetOperations",
            "valuation-source-entity", economicEvent)
        {
            BookPositionId = positionId
        };
        var projectedAt = OccurredAt.AddMinutes(5);
        var expected = new AssetAccountingEventSpineDto(
            EventId, AssetAccountingEventKindDto.Valuation, 1, 1, EffectiveDate, 100m, "USD",
            new AssetAccountingEventScopeDto(
                securityId, 2, positionId, 4, bookId, PeriodId, AccountingBasisKindDto.Gaap, "fund-alpha"),
            economicEvent, lineage, [evidence],
            [
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Expected, projectedAt,
                    "projector", [evidence], $"economic-event://{EventId:D}/1")
            ]);
        var projected = expected with
        {
            SpineVersion = 2,
            Stages =
            [
                .. expected.Stages,
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Projected, projectedAt,
                    "projector", [evidence], $"projection-run://{lineage.ProjectionRunId:D}")
            ],
            ProjectedEffect = new ProjectedAccountingEffectDto(
                lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion, lineage.ProjectionAsOfDate,
                100m, 100m, "USD",
                [
                    new ProjectedAccountingEffectLineDto("Assets:Investment", 100m, 0m, "USD"),
                    new ProjectedAccountingEffectLineDto("Income:Unrealized", 0m, 100m, "USD")
                ])
        };
        return (expected, projected);
    }
}
