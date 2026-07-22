using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.AssetOperations;

public sealed class AssetAccountingEventSpineContractTests
{
    [Fact]
    public void Validate_ExactProjectedSpine_Passes()
        => AssetAccountingEventSpineValidator.Validate(BuildProjected()).Should().BeEmpty();

    [Fact]
    public void Validate_EventEvidenceHashMismatch_FailsClosed()
    {
        var spine = BuildProjected();
        spine = spine with
        {
            EconomicEvent = spine.EconomicEvent with { SourceContentHash = new string('b', 64) }
        };

        AssetAccountingEventSpineValidator.Validate(spine)
            .Should().Contain(issue => issue.Contains("source hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TriggerEventMaterialMismatch_FailsClosed()
    {
        var spine = BuildProjected();
        spine = spine with
        {
            ProjectionLineage = spine.ProjectionLineage with
            {
                TriggerEvent = spine.EconomicEvent with { SourceEntityId = "different-source" }
            }
        };

        AssetAccountingEventSpineValidator.Validate(spine)
            .Should().Contain(issue => issue.Contains("exactly match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DuplicateEvidenceId_FailsClosed()
    {
        var spine = BuildProjected();
        spine = spine with { RetainedEvidence = [spine.RetainedEvidence[0], spine.RetainedEvidence[0]] };

        AssetAccountingEventSpineValidator.Validate(spine)
            .Should().Contain(issue => issue.Contains("duplicate evidence ids", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingStageReference_FailsClosed()
    {
        var spine = BuildProjected();
        spine = spine with
        {
            Stages = [spine.Stages[0], spine.Stages[1] with { ReferenceId = " " }]
        };

        AssetAccountingEventSpineValidator.Validate(spine)
            .Should().Contain(issue => issue.Contains("reference id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DoubleSidedProjectedLine_FailsClosed()
    {
        var spine = BuildProjected();
        spine = spine with
        {
            ProjectedEffect = spine.ProjectedEffect! with
            {
                Lines =
                [
                    new ProjectedAccountingEffectLineDto("Assets:Investment", 100m, 100m, "USD"),
                    new ProjectedAccountingEffectLineDto("Income:Unrealized", 0m, 100m, "USD")
                ]
            }
        };

        AssetAccountingEventSpineValidator.Validate(spine)
            .Should().Contain(issue => issue.Contains("exactly one positive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PostedJournalImpact_UnknownStatusOrNonUtcTime_FailsClosed()
    {
        var impact = new PostedJournalImpactDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AccountingBasisKindDto.Gaap,
            DateTimeOffset.Parse("2026-07-01T12:00:00-07:00"), JournalPostingStatusDto.Unknown,
            "USD", 10m, 10m,
            [
                new PostedJournalImpactLineDto(Guid.NewGuid(), "Cash", 10m, 0m, "USD"),
                new PostedJournalImpactLineDto(Guid.NewGuid(), "Income", 0m, 10m, "USD")
            ]);

        PostedJournalImpactValidator.Validate(impact).Should().Contain(issue => issue.Contains("Posted status"));
        PostedJournalImpactValidator.Validate(impact).Should().Contain(issue => issue.Contains("UTC posting timestamp"));
    }

    [Fact]
    public void AcquisitionLot_EconomicsMustEqualEventAmountAndDate()
    {
        var evidence = BuildProjected().RetainedEvidence;
        var instruction = new AssetLotMutationInstructionDto(
            AssetLotMutationIntentDto.Acquire,
            new AssetAcquisitionLotDto(Guid.NewGuid(), "lot-1", new DateOnly(2026, 6, 29), 2m, 49m, "Assets:Investment"));

        AssetLotMutationInstructionValidator.Validate(
                AssetAccountingEventKindDto.Acquisition,
                instruction,
                100m,
                new DateOnly(2026, 6, 30),
                evidence)
            .Should().HaveCountGreaterThanOrEqualTo(2);
    }

    private static AssetAccountingEventSpineDto BuildProjected()
    {
        var eventId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var securityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var positionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var bookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var periodId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var effectiveDate = new DateOnly(2026, 6, 30);
        var occurredAt = DateTimeOffset.Parse("2026-06-30T18:00:00Z");
        var sourceHash = new string('a', 64);
        var evidence = new RetainedEvidenceIdentityDto(
            "source-1", "evidence://asset/source-1", sourceHash, "Custodian", "position-valuation-1",
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus, "controller",
            occurredAt.AddMinutes(1), effectiveDate, 1, occurredAt.AddMinutes(2), "retention-service",
            AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"));
        var economicEvent = new EconomicEventReferenceDto(
            eventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Valuation), 1,
            effectiveDate, occurredAt, "AssetOperations", "position-valuation-1", SourceContentHash: sourceHash)
        {
            SecurityId = securityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            Guid.Parse("55555555-5555-5555-5555-555555555555"), null, "valuation", "v1", "engine-v1", "base",
            effectiveDate, occurredAt.AddMinutes(3), "AssetOperations", "position-valuation-1", economicEvent)
        {
            BookPositionId = positionId
        };
        var projectedAt = occurredAt.AddMinutes(4);
        var projected = new ProjectedAccountingEffectDto(
            lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion, lineage.ProjectionAsOfDate,
            100m, 100m, "USD",
            [
                new ProjectedAccountingEffectLineDto("Assets:Investment", 100m, 0m, "USD"),
                new ProjectedAccountingEffectLineDto("Income:Unrealized", 0m, 100m, "USD")
            ]);
        return new AssetAccountingEventSpineDto(
            eventId, AssetAccountingEventKindDto.Valuation, 1, 1, effectiveDate, 100m, "USD",
            new AssetAccountingEventScopeDto(securityId, 2, positionId, 3, bookId, periodId,
                AccountingBasisKindDto.Gaap, "fund-alpha"),
            economicEvent, lineage, [evidence],
            [
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Expected, projectedAt,
                    "projector", [evidence], $"economic-event://{eventId:D}/1"),
                new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.Projected, projectedAt,
                    "projector", [evidence], $"projection-run://{lineage.ProjectionRunId:D}")
            ],
            projected);
    }
}
