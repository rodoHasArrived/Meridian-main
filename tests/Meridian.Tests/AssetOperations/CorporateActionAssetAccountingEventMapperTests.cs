using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class CorporateActionAssetAccountingEventMapperTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PositionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LedgerBookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CaseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PositionSnapshotId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid LotSnapshotId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid PolicyDecisionId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly DateTimeOffset MappedAtUtc = DateTimeOffset.Parse("2026-08-25T10:00:00Z");

    private readonly CorporateActionAccountingProjectionService _projector = new();
    private readonly CorporateActionAssetAccountingEventMapper _sut = new();

    [Fact]
    public void Map_ShouldProduceCorporateActionEventSpineHandoffWithoutDurablePostingIdentity()
    {
        var projection = ProjectCashDividend();
        var result = _sut.Map(CreateMapRequest(projection));

        result.IsMapped.Should().BeTrue();
        result.Blockers.Should().BeEmpty();
        result.Projection!.Event.EventKind.Should().Be(AssetAccountingEventKindDto.CorporateAction);
        result.Projection.Event.EconomicEvent.Should().BeSameAs(projection.EconomicEvent);
        result.Projection.Event.ProjectionLineage.Should().BeSameAs(projection.ProjectionLineage);
        result.Projection.Event.ProjectedEffect.Lines.Should().HaveCount(2);
        result.Projection.Event.RetainedEvidence.Should().HaveCount(5);
        result.Projection.Treatment.RuleProfile.RulePackId.Should().Be("clearwater-corporate-actions");
        result.Projection.Treatment.RuleProfile.RulePackVersion.Should().Be("v1");
        result.Projection.LotMutations.Should().BeSameAs(projection.LotMutations);
        result.Projection.PostingSet.Should().BeSameAs(projection.PostingSet);
        result.Projection.AppliedAccountingRulePack.RulePackId.Should().Be("meridian-corporate-action-gaap");
        result.Projection.PostingIdempotencyKey.Should().StartWith("corporate-action-posting/v1:");
    }

    [Fact]
    public void Map_ShouldFailClosedForBlockedOrReferenceOnlyProjection()
    {
        var blocked = _projector.Project(CreateProjectionRequest(
            CorporateActionAccountingTypeDto.BankruptcyDistribution,
            new CorporateActionEconomicsDto(AffectedQuantity: 10m, CarryingAmount: 25m)));
        var referenceOnly = _projector.Project(CreateProjectionRequest(
            CorporateActionAccountingTypeDto.ConsentSolicitation,
            new CorporateActionEconomicsDto(),
            new CorporateActionPolicyInputsDto(ConsentTermsChanged: false)));

        var blockedResult = _sut.Map(CreateMapRequest(blocked));
        var referenceOnlyResult = _sut.Map(CreateMapRequest(referenceOnly));

        blockedResult.IsMapped.Should().BeFalse();
        blockedResult.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.event-map-projection-blocked");
        referenceOnly.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        referenceOnly.PostingSet!.RequiresJournalCandidate.Should().BeFalse();
        referenceOnlyResult.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.event-map-no-journal-intent");
    }

    [Fact]
    public void Map_ShouldRejectScopeMismatchAndMissingEvidence()
    {
        var projection = ProjectCashDividend();
        var request = CreateMapRequest(projection);
        var result = _sut.Map(request with
        {
            Scope = request.Scope with { ExpectedBookPositionVersion = 8 },
            RetainedEvidence = []
        });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Select(blocker => blocker.Code).Should().Contain(
        [
            "corporate-action.event-map-scope-mismatch",
            "corporate-action.event-map-evidence-required"
        ]);
    }

    [Fact]
    public void Map_ShouldFailClosedWhenJournalIntentStillHasUnresolvedLotMutations()
    {
        var projection = _projector.Project(CreateProjectionRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 98m,
                ParAmount: 100m,
                GrossCashConsideration: 103m,
                AccruedIncome: 2m,
                IsMakeWhole: true)));

        var result = _sut.Map(CreateMapRequest(projection));

        projection.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        projection.PostingSet!.RequiresJournalCandidate.Should().BeTrue();
        projection.CanPreparePostingCandidate.Should().BeFalse();
        result.IsMapped.Should().BeFalse();
        result.Blockers.Select(blocker => blocker.Code).Should().Contain(
        [
            "corporate-action.lot-mutation-source-required",
            "corporate-action.lot-mutation-source-version-required",
            "corporate-action.lot-mutation-before-snapshot-required",
            "corporate-action.lot-mutation-basis-amount-required"
        ]);
    }

    [Fact]
    public void Map_ShouldRejectEvidenceThatDoesNotBindExactEventSourceDateAndHash()
    {
        var projection = ProjectCashDividend();
        var request = CreateMapRequest(projection);
        var eventEvidence = request.RetainedEvidence.Single(item =>
            item.SubjectType == AssetAccountingEvidenceSubjects.Event);
        var mismatchedEvidence = eventEvidence with
        {
            SourceReference = "different-source-entity"
        };
        var evidence = request.RetainedEvidence
            .Select(item => item.EvidenceId == eventEvidence.EvidenceId ? mismatchedEvidence : item)
            .ToArray();

        var result = _sut.Map(request with { RetainedEvidence = evidence });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.event-map-evidence-binding-mismatch");
    }

    [Fact]
    public void Map_ShouldRejectIncompleteEvidenceIdentity()
    {
        var projection = ProjectCashDividend();
        var request = CreateMapRequest(projection);
        var sourceEvidence = request.RetainedEvidence[0];
        var incompleteEvidence = sourceEvidence with
        {
            ReviewStatus = "Pending",
            ReviewedBy = string.Empty
        };
        var evidence = request.RetainedEvidence
            .Select(item => item.EvidenceId == sourceEvidence.EvidenceId ? incompleteEvidence : item)
            .ToArray();

        var result = _sut.Map(request with { RetainedEvidence = evidence });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.event-map-evidence-incomplete");
    }

    [Fact]
    public void Map_ShouldRejectUnbalancedOrLineageMismatchedEffect()
    {
        var projection = ProjectCashDividend();
        var request = CreateMapRequest(projection);
        var invalidEffect = request.MappedEffect.Effect with
        {
            ProjectionRunId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TotalCredits = 99m
        };

        var result = _sut.Map(request with
        {
            MappedEffect = request.MappedEffect with { Effect = invalidEffect }
        });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Select(blocker => blocker.Code).Should().Contain(
        [
            "corporate-action.event-map-effect-lineage-mismatch",
            "corporate-action.event-map-effect-unbalanced"
        ]);
    }

    [Fact]
    public void Map_ShouldRejectArbitraryBalancedEffectThatDoesNotReconcileToPostingIntent()
    {
        var projection = ProjectCashDividend();
        var request = CreateMapRequest(projection);
        var unrelatedBalancedEffect = request.MappedEffect.Effect with
        {
            TotalDebits = 50m,
            TotalCredits = 50m,
            Lines =
            [
                new ProjectedAccountingEffectLineDto("Cash", 50m, 0m, "USD"),
                new ProjectedAccountingEffectLineDto("DividendIncome", 0m, 50m, "USD")
            ]
        };

        var result = _sut.Map(request with
        {
            MappedEffect = request.MappedEffect with { Effect = unrelatedBalancedEffect }
        });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.event-map-mapping-attestation-invalid");
    }

    [Fact]
    public void Map_ShouldRejectComponentManifestWhoseAllocationsDoNotReconcileAmounts()
    {
        var projection = ProjectCashDividend();
        var request = CreateMapRequest(projection);
        var mappings = new CorporateActionPostingComponentLineMappingDto[]
        {
            new(
                0,
                CorporateActionPostingComponentKindDto.Cash,
                [new CorporateActionPostingComponentLineAllocationDto(0, 99m)],
                "cash-receipt"),
            new(
                1,
                CorporateActionPostingComponentKindDto.DividendIncome,
                [new CorporateActionPostingComponentLineAllocationDto(1, 100m)],
                "dividend-income")
        };
        var tampered = new CorporateActionMappedAccountingEffectDto(
            request.MappedEffect.Effect,
            request.MappedEffect.AccountingRulePack,
            projection.PostingIntentHash!,
            CorporateActionMappedAccountingEffectAttestor.ComputeMappingHash(
                projection.PostingIntentHash!,
                request.Scope,
                request.MappedEffect.Effect,
                request.MappedEffect.AccountingRulePack,
                mappings),
            mappings);

        var result = _sut.Map(request with { MappedEffect = tampered });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.event-map-component-reconciliation-incomplete");
    }

    [Fact]
    public void Map_ShouldRejectManifestThatOverallocatesOneLineAndUnderallocatesAnother()
    {
        var projection = ProjectCashDividend();
        var request = CreateMapRequest(projection);
        var mappings = new CorporateActionPostingComponentLineMappingDto[]
        {
            new(
                0,
                CorporateActionPostingComponentKindDto.Cash,
                [new CorporateActionPostingComponentLineAllocationDto(0, 100m)],
                "cash-receipt"),
            new(
                1,
                CorporateActionPostingComponentKindDto.DividendIncome,
                [
                    new CorporateActionPostingComponentLineAllocationDto(0, 50m),
                    new CorporateActionPostingComponentLineAllocationDto(1, 50m)
                ],
                "dividend-income")
        };
        var tampered = new CorporateActionMappedAccountingEffectDto(
            request.MappedEffect.Effect,
            request.MappedEffect.AccountingRulePack,
            projection.PostingIntentHash!,
            CorporateActionMappedAccountingEffectAttestor.ComputeMappingHash(
                projection.PostingIntentHash!,
                request.Scope,
                request.MappedEffect.Effect,
                request.MappedEffect.AccountingRulePack,
                mappings),
            mappings);

        var result = _sut.Map(request with { MappedEffect = tampered });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.event-map-component-reconciliation-incomplete");
    }

    private CorporateActionAccountingProjectionDto ProjectCashDividend()
        => _projector.Project(CreateProjectionRequest(
            CorporateActionAccountingTypeDto.CashDividend,
            new CorporateActionEconomicsDto(PositionQuantity: 100m, CashRatePerUnit: 1m)));

    private static CorporateActionAccountingProjectionRequest CreateProjectionRequest(
        CorporateActionAccountingTypeDto actionType,
        CorporateActionEconomicsDto economics,
        CorporateActionPolicyInputsDto? policy = null)
        => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            actionType,
            AccountingBasisKindDto.Gaap,
            SecurityId,
            PositionId,
            7,
            7,
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 8, 25),
            DateTimeOffset.Parse("2026-08-25T08:00:00Z"),
            "USD",
            "SecurityMaster",
            "corporate-action-source-1",
            new string('a', 64),
            economics,
            policy,
            EvidenceManifest: CreateEvidenceManifest(),
            CaseId: CaseId,
            CaseVersion: 3,
            PolicyDecisionVersion: 2,
            PositionSnapshotId: PositionSnapshotId,
            AccountingScope: new CorporateActionAccountingProjectionScopeDto(
                "tenant-alpha",
                "company-alpha",
                "fund-alpha",
                LedgerBookId,
                PeriodId,
                3,
                "US"),
            LotSnapshotId: LotSnapshotId,
            LotSnapshotVersion: 7,
            PolicyDecisionId: PolicyDecisionId);

    private static CorporateActionAssetAccountingEventMapRequest CreateMapRequest(
        CorporateActionAccountingProjectionDto projection)
    {
        var lineage = projection.ProjectionLineage;
        var economicEvent = projection.EconomicEvent;
        var effect = new ProjectedAccountingEffectDto(
            lineage?.ProjectionRunId ?? Guid.Parse("66666666-6666-6666-6666-666666666666"),
            lineage?.ModelKey ?? CorporateActionAccountingProjectionService.ModelKey,
            lineage?.ModelVersion ?? CorporateActionAccountingProjectionService.ModelVersion,
            lineage?.ProjectionAsOfDate ?? new DateOnly(2026, 8, 25),
            100m,
            100m,
            "USD",
            [
                new ProjectedAccountingEffectLineDto("Cash", 100m, 0m, "USD"),
                new ProjectedAccountingEffectLineDto("DividendIncome", 0m, 100m, "USD")
            ]);
        var scope = new AssetAccountingEventScopeDto(
            SecurityId,
            2,
            PositionId,
            7,
            LedgerBookId,
            PeriodId,
            AccountingBasisKindDto.Gaap,
            "fund-alpha",
            "tenant-alpha",
            "company-alpha");
        IReadOnlyList<RetainedEvidenceIdentityDto> evidence = economicEvent is null
            ? []
            : CreateRetainedEvidence(projection, economicEvent);

        var accountingRulePack = new AccountingRulePackReferenceDto(
            "meridian-corporate-action-gaap",
            "v3",
            "cash-dividend",
            "v2");
        var mappings = new CorporateActionPostingComponentLineMappingDto[]
        {
            new(
                0,
                CorporateActionPostingComponentKindDto.Cash,
                [new CorporateActionPostingComponentLineAllocationDto(0, 100m)],
                "cash-receipt"),
            new(
                1,
                CorporateActionPostingComponentKindDto.DividendIncome,
                [new CorporateActionPostingComponentLineAllocationDto(1, 100m)],
                "dividend-income")
        };
        var mappedEffect = projection.CanPreparePostingCandidate
            ? CorporateActionMappedAccountingEffectAttestor.Create(
                projection,
                scope,
                effect,
                accountingRulePack,
                mappings)
            : new CorporateActionMappedAccountingEffectDto(
                effect,
                accountingRulePack,
                projection.PostingIntentHash ?? string.Empty,
                string.Empty,
                []);

        return new CorporateActionAssetAccountingEventMapRequest(
            projection,
            scope,
            mappedEffect,
            3,
            "controller",
            MappedAtUtc,
            evidence,
            "Mapped after promoted rule-pack account resolution.");
    }

    private static IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto> CreateEvidenceManifest()
        =>
        [
            new(
                CorporateActionProjectionEvidenceRoleDto.SourceEvent,
                "source-event-1",
                "evidence://corporate-action/source-1",
                new string('a', 64),
                1,
                "SecurityMasterCorporateAction",
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa").ToString("D")),
            new(
                CorporateActionProjectionEvidenceRoleDto.PositionSnapshot,
                "position-snapshot-1",
                "evidence://corporate-action/position-1",
                new string('b', 64),
                7,
                "PositionSnapshot",
                PositionSnapshotId.ToString("D")),
            new(
                CorporateActionProjectionEvidenceRoleDto.LotSnapshot,
                "lot-snapshot-1",
                "evidence://corporate-action/lots-1",
                new string('c', 64),
                7,
                "LotSnapshot",
                LotSnapshotId.ToString("D")),
            new(
                CorporateActionProjectionEvidenceRoleDto.PolicyDecision,
                "policy-decision-1",
                "evidence://corporate-action/policy-1",
                new string('d', 64),
                2,
                "CorporateActionPolicyDecision",
                PolicyDecisionId.ToString("D"))
        ];

    private static IReadOnlyList<RetainedEvidenceIdentityDto> CreateRetainedEvidence(
        CorporateActionAccountingProjectionDto projection,
        EconomicEventReferenceDto economicEvent)
    {
        var reviewedAt = DateTimeOffset.Parse("2026-08-25T09:00:00Z");
        var retainedAt = DateTimeOffset.Parse("2026-08-25T09:01:00Z");
        var dependencies = projection.EvidenceManifest
            .Select(item => new RetainedEvidenceIdentityDto(
                item.EvidenceId,
                item.EvidenceUri,
                item.ContentHashSha256,
                economicEvent.SourceDomain,
                item.EvidenceId,
                RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
                "controller",
                reviewedAt,
                economicEvent.EffectiveDate,
                item.EvidenceVersion,
                retainedAt,
                "evidence-vault",
                item.SubjectType,
                item.SubjectId))
            .ToList();
        dependencies.Add(new RetainedEvidenceIdentityDto(
            "mapped-event-evidence-1",
            "evidence://corporate-action/mapped-event-1",
            economicEvent.SourceContentHash!,
            economicEvent.SourceDomain,
            economicEvent.SourceEntityId!,
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "controller",
            reviewedAt,
            economicEvent.EffectiveDate,
            1,
            retainedAt,
            "evidence-vault",
            AssetAccountingEvidenceSubjects.Event,
            economicEvent.EventId.ToString("D")));
        return dependencies;
    }
}
