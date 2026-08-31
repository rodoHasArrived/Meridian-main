using System.Security.Cryptography;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class AssetAccountingLifecycleSeparationTests
{
    [Fact]
    public void ExpectedAndProjected_AreConsecutiveDurableSpineVersions()
    {
        var expected = CreateExpectedSpine();
        var projected = CreateProjectedSpine(expected);

        var appendExpected = () => AssetAccountingEventProjectionRules.ValidateAppend(
            expected,
            expectedSpineVersion: 0,
            expectedBookPositionVersion: expected.Scope.ExpectedBookPositionVersion);
        var appendProjected = () => AssetAccountingEventProjectionRules.ValidateAppend(
            projected,
            expectedSpineVersion: 1,
            expectedBookPositionVersion: projected.Scope.ExpectedBookPositionVersion);
        var continueStream = () => AssetAccountingEventProjectionRules.ValidateStreamContinuity(
            expected,
            projected);

        appendExpected.Should().NotThrow();
        appendProjected.Should().NotThrow();
        continueStream.Should().NotThrow();
        expected.Stages.Should().ContainSingle()
            .Which.Stage.Should().Be(AssetAccountingLifecycleStageDto.Expected);
        expected.ProjectedEffect.Should().BeNull();
        projected.SpineVersion.Should().Be(2);
        projected.Stages.Select(static stage => stage.Stage).Should().Equal(
            AssetAccountingLifecycleStageDto.Expected,
            AssetAccountingLifecycleStageDto.Projected);
        projected.ProjectedEffect.Should().NotBeNull();
    }

    [Fact]
    public void ApprovedAndPosted_MustAppendAsSeparateDurableSpineVersions()
    {
        var projected = CreateProjectedSpine(CreateExpectedSpine());
        var evidence = projected.RetainedEvidence;
        var drafted = projected with
        {
            SpineVersion = 3,
            Stages =
            [
                .. projected.Stages,
                new AssetAccountingStageEvidenceDto(
                    AssetAccountingLifecycleStageDto.Drafted,
                    projected.Stages[^1].AttestedAtUtc.AddMinutes(1),
                    "accountant",
                    evidence,
                    "rules-studio://asset/draft")
            ]
        };
        var approvedStage = new AssetAccountingStageEvidenceDto(
            AssetAccountingLifecycleStageDto.Approved,
            drafted.Stages[^1].AttestedAtUtc.AddMinutes(1),
            "controller",
            evidence,
            "approval-1");
        var postedStage = new AssetAccountingStageEvidenceDto(
            AssetAccountingLifecycleStageDto.Posted,
            approvedStage.AttestedAtUtc.AddMinutes(1),
            "controller",
            evidence,
            projected.EventId.ToString("D"));
        var approved = drafted with
        {
            SpineVersion = 4,
            Stages = [.. drafted.Stages, approvedStage]
        };
        var posted = approved with
        {
            SpineVersion = 5,
            Stages = [.. approved.Stages, postedStage],
            PostedJournalImpact = new PostedJournalImpactDto(
                Guid.NewGuid(),
                approved.Scope.LedgerBookId,
                approved.Scope.PeriodId,
                approved.Scope.AccountingBasis,
                postedStage.AttestedAtUtc,
                JournalPostingStatusDto.Posted,
                approved.Currency,
                100m,
                100m)
        };
        var combined = drafted with
        {
            SpineVersion = 4,
            Stages = [.. drafted.Stages, approvedStage, postedStage],
            PostedJournalImpact = posted.PostedJournalImpact
        };

        var approve = () => AssetAccountingEventProjectionRules.ValidateStreamContinuity(drafted, approved);
        var post = () => AssetAccountingEventProjectionRules.ValidateStreamContinuity(approved, posted);
        var combine = () => AssetAccountingEventProjectionRules.ValidateStreamContinuity(drafted, combined);

        approve.Should().NotThrow();
        post.Should().NotThrow();
        combine.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one next canonical stage*");
    }

    [Fact]
    public void CorrectionReference_PreservesOriginalJournalPeriodAndBasisOutsideCorrectingScope()
    {
        var originalBookId = Guid.NewGuid();
        var originalPeriodId = Guid.NewGuid();
        var correction = new AssetAccountingCorrectionReferenceDto(
            Guid.NewGuid(),
            EventVersion: 7,
            PostedJournalEntryId: Guid.NewGuid(),
            originalBookId,
            originalPeriodId,
            AccountingBasisKindDto.Tax);
        var correcting = CreateExpectedSpine() with { Correction = correction };

        var issues = AssetAccountingEventSpineValidator.Validate(correcting);

        issues.Should().BeEmpty();
        correcting.Scope.LedgerBookId.Should().NotBe(originalBookId);
        correcting.Scope.PeriodId.Should().NotBe(originalPeriodId);
        correcting.Scope.AccountingBasis.Should().Be(AccountingBasisKindDto.Gaap);
        correcting.Correction.Should().Be(correction);
    }

    private static AssetAccountingEventSpineDto CreateExpectedSpine()
    {
        var eventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var securityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var positionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var bookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var periodId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var effectiveDate = new DateOnly(2026, 7, 1);
        var occurredAtUtc = DateTimeOffset.Parse("2026-07-01T12:00:00Z");
        var projectedAtUtc = occurredAtUtc.AddMinutes(5);
        var hash = Convert.ToHexString(SHA256.HashData("asset-event"u8.ToArray())).ToLowerInvariant();
        var evidence = new RetainedEvidenceIdentityDto(
            "asset-event",
            "evidence://asset/event",
            hash,
            "Custodian",
            "source-event-1",
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "reviewer",
            occurredAtUtc.AddMinutes(1),
            effectiveDate,
            1,
            occurredAtUtc.AddMinutes(2),
            "retention-service",
            AssetAccountingEvidenceSubjects.Event,
            eventId.ToString("D"));
        var economicEvent = new EconomicEventReferenceDto(
            eventId,
            AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Valuation),
            1,
            effectiveDate,
            occurredAtUtc,
            "AssetOperations",
            "source-event-1",
            SourceContentHash: hash)
        {
            SecurityId = securityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            null,
            "valuation",
            "v1",
            "engine-v1",
            "base",
            effectiveDate,
            occurredAtUtc.AddMinutes(3),
            "AssetOperations",
            "source-event-1",
            economicEvent)
        {
            BookPositionId = positionId
        };
        return new AssetAccountingEventSpineDto(
            eventId,
            AssetAccountingEventKindDto.Valuation,
            EventVersion: 1,
            SpineVersion: 1,
            effectiveDate,
            EventAmount: 100m,
            Currency: "USD",
            new AssetAccountingEventScopeDto(
                securityId,
                ExpectedSecurityVersion: 2,
                positionId,
                ExpectedBookPositionVersion: 3,
                bookId,
                periodId,
                AccountingBasisKindDto.Gaap,
                "fund-alpha"),
            economicEvent,
            lineage,
            [evidence],
            [
                new AssetAccountingStageEvidenceDto(
                    AssetAccountingLifecycleStageDto.Expected,
                    projectedAtUtc,
                    "projector",
                    [evidence],
                    $"economic-event://{eventId:D}/1")
            ]);
    }

    private static AssetAccountingEventSpineDto CreateProjectedSpine(
        AssetAccountingEventSpineDto expected)
    {
        var projectedAtUtc = expected.Stages[^1].AttestedAtUtc;
        return expected with
        {
            SpineVersion = 2,
            Stages =
            [
                .. expected.Stages,
                new AssetAccountingStageEvidenceDto(
                    AssetAccountingLifecycleStageDto.Projected,
                    projectedAtUtc,
                    "projector",
                    expected.RetainedEvidence,
                    $"projection-run://{expected.ProjectionLineage.ProjectionRunId:D}")
            ],
            ProjectedEffect = new ProjectedAccountingEffectDto(
                expected.ProjectionLineage.ProjectionRunId,
                expected.ProjectionLineage.ModelKey,
                expected.ProjectionLineage.ModelVersion,
                expected.ProjectionLineage.ProjectionAsOfDate,
                TotalDebits: 100m,
                TotalCredits: 100m,
                expected.Currency,
                [
                    new ProjectedAccountingEffectLineDto("Assets:Investment", 100m, 0m, expected.Currency),
                    new ProjectedAccountingEffectLineDto("Income:Unrealized", 0m, 100m, expected.Currency)
                ])
        };
    }
}
