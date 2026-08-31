using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class CorporateActionAccountingProjectionServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PositionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SuccessorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondSuccessorId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CaseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PositionSnapshotId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid LedgerBookId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid PeriodId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid SourceCorporateActionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LotSnapshotId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PolicyDecisionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SourceLotId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TargetLotId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid SecondSourceLotId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly CorporateActionAccountingProjectionService _sut = new();

    [Fact]
    public void Project_CA01_ShouldCarryBookValueAndHoldingPeriodWithoutGainLoss()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RegS144AExchange,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 98m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Successor,
                        100m)
                ])));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.LotMutations!.Mutations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = CorporateActionLotMutationKindDto.CarryOver,
            TargetSecurityId = (Guid?)SuccessorId,
            CarryingAmount = (decimal?)98m,
            HoldingPeriodTreatment = CorporateActionHoldingPeriodTreatmentDto.CarryOver
        });
        result.PostingSet!.Components.Should().NotContain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain ||
            component.Kind == CorporateActionPostingComponentKindDto.RealizedLoss);
    }

    [Fact]
    public void Project_CA03_CA04_ShouldAllocateAdvanceRefundingAndRejectIncompleteAllocation()
    {
        var validRequest = CreateRequest(
            CorporateActionAccountingTypeDto.AdvanceRefunding,
            AccountingBasisKindDto.Statutory,
            new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 100m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Refunded,
                        60m,
                        0.60m),
                    new CorporateActionSuccessorAllocationDto(
                        SecondSuccessorId,
                        CorporateActionSuccessorRoleDto.Unrefunded,
                        40m,
                        0.40m)
                ]));

        var valid = _sut.Project(validRequest);
        var invalid = _sut.Project(validRequest with
        {
            Economics = validRequest.Economics with
            {
                Successors =
                [
                    validRequest.Economics.Successors[0],
                    validRequest.Economics.Successors[1] with { BookValueAllocationPercent = 0.39m }
                ]
            }
        });

        valid.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        valid.LotMutations!.Mutations.Select(mutation => mutation.CarryingAmount)
            .Should().Equal(60m, 40m);
        valid.LotMutations.Mutations.Single(mutation =>
                mutation.TargetSecurityId == SuccessorId)
            .ReportingTags.Should().ContainSingle("ScheduleD");
        valid.LotMutations.Mutations.Single(mutation =>
                mutation.TargetSecurityId == SecondSuccessorId)
            .ReportingTags.Should().BeEmpty();
        invalid.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.successor-allocation-total-invalid");
    }

    [Fact]
    public void Project_CA05_ShouldBlockBankruptcyWithoutCaseSpecificMethod()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.BankruptcyDistribution,
            economics: new CorporateActionEconomicsDto(AffectedQuantity: 10m, CarryingAmount: 25m)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Blocked);
        result.Blockers.Should().Contain(blocker => blocker.Code == "corporate-action.bankruptcy-method-required");
        result.EconomicEvent.Should().BeNull();
    }

    [Fact]
    public void Project_CA06_ShouldApplyApprovedBankruptcyTransferOutAtZero()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.BankruptcyDistribution,
            economics: new CorporateActionEconomicsDto(AffectedQuantity: 10m, CarryingAmount: 25m),
            policy: new CorporateActionPolicyInputsDto(
                BankruptcyMethod: CorporateActionBankruptcyMethodDto.TransferOutAtZero)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.Recipe.Should().ContainSingle(operation =>
            operation.Kind == CorporateActionEconomicOperationKindDto.TransferOut &&
            operation.Amount == 0m);
        result.LotMutations!.Mutations.Should().ContainSingle(mutation =>
            mutation.Kind == CorporateActionLotMutationKindDto.TransferOut &&
            mutation.CarryingAmount == 25m);
        result.PostingSet!.Components.Should().ContainSingle(component =>
            component.Kind == CorporateActionPostingComponentKindDto.CarryingValueRelief &&
            component.Amount == 25m);
    }

    [Fact]
    public void Project_CA07_CA08_ShouldApplyBasisSpecificCallTreatment()
    {
        var economics = new CorporateActionEconomicsDto(
            AffectedQuantity: 100m,
            CarryingAmount: 98m,
            ParAmount: 100m,
            GrossCashConsideration: 103m,
            AccruedIncome: 2m,
            IsMakeWhole: true);

        var statutory = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            AccountingBasisKindDto.Statutory,
            economics));
        var gaap = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            AccountingBasisKindDto.Gaap,
            economics));

        statutory.PostingSet!.Components.Single(component =>
            component.Kind == CorporateActionPostingComponentKindDto.PrepaymentPenaltyIncome)
            .Amount.Should().Be(1m);
        gaap.PostingSet!.Components.Single(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain)
            .Amount.Should().Be(3m);
    }

    [Fact]
    public void Project_ShouldBindAuthoritativeLotIdentityVersionAndBeforeSnapshotForFullDisposal()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            economics: CallEconomics());
        var authoritativeMutation = FullDisposalMutation(basisAmount: 96m, expectedSourceLotVersion: 11);

        var result = _sut.Project(request with
        {
            AuthoritativeLotMutations = [authoritativeMutation]
        });

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.CanPreparePostingCandidate.Should().BeTrue();
        result.LotMutations!.HasAuthoritativeLotResolution.Should().BeTrue();
        result.LotMutations.Mutations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            authoritativeMutation);
        result.LotMutations.Mutations.Single().TargetLotId.Should().BeNull(
            "a full disposal legitimately has no surviving target lot");
        result.LotMutations.Mutations.Single().SourceAfter.Should().BeNull();
    }

    [Fact]
    public void Project_ShouldKeepUnresolvedLotIntentOutOfPostingPreparation()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            economics: CallEconomics()));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.PostingSet!.RequiresJournalCandidate.Should().BeTrue();
        result.LotMutations!.RequiresAuthoritativeLotResolution.Should().BeTrue();
        result.LotMutations.HasAuthoritativeLotResolution.Should().BeFalse();
        result.CanPreparePostingCandidate.Should().BeFalse();
    }

    [Fact]
    public void Project_ShouldFailClosedForIncompleteAuthoritativeLotSnapshot()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            economics: CallEconomics());
        var incomplete = FullDisposalMutation(96m, 11) with { SourceBefore = null };

        var result = _sut.Project(request with { AuthoritativeLotMutations = [incomplete] });

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Blocked);
        result.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.lot-mutation-before-snapshot-required");
        result.CanPreparePostingCandidate.Should().BeFalse();
    }

    [Fact]
    public void Project_ShouldBindLotVersionAndBasisSnapshotIntoProjectionIdentity()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            economics: CallEconomics());
        var baseline = _sut.Project(request with
        {
            AuthoritativeLotMutations = [FullDisposalMutation(96m, 11)]
        });
        var changed = _sut.Project(request with
        {
            AuthoritativeLotMutations = [FullDisposalMutation(95m, 12)]
        });

        baseline.ProjectionInputHash.Should().NotBe(changed.ProjectionInputHash);
        baseline.PostingIntentHash.Should().NotBe(changed.PostingIntentHash);
        baseline.ProjectionLineage!.ProjectionRunId.Should().NotBe(changed.ProjectionLineage!.ProjectionRunId);
    }

    [Fact]
    public void LotPlanValidator_ShouldAllowMissingSidesOnlyForCreateOrFullDisposal()
    {
        var acquisition = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Acquire,
            SuccessorId,
            Quantity: 5m,
            CarryingAmount: 100m,
            HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.NewLot,
            TargetLotId: TargetLotId,
            TargetOperation: CorporateActionLotTargetOperationDto.Create,
            TargetAfter: new CorporateActionLotStateSnapshotDto(5m, 100m, 101m),
            BasisAmount: 101m);
        var disposal = FullDisposalMutation(96m, 11);
        var incompleteCarryOver = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.CarryOver,
            SecurityId,
            SuccessorId,
            100m,
            98m,
            HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            SourceQuantity: 100m,
            SourceCarryingAmount: 98m,
            SourceBasisAmount: 96m,
            BasisAmount: 96m);

        CorporateActionLotMutationPlanValidator.Validate([acquisition, disposal]).Should().BeEmpty();
        CorporateActionLotMutationPlanValidator.Validate([incompleteCarryOver]).Should().Contain(blocker =>
            blocker.Code == "corporate-action.lot-mutation-target-required");
    }

    [Fact]
    public void LotPlanValidator_ShouldRequireVersionGuardForDistinctExistingTargetLot()
    {
        var allocationWithoutTargetVersion = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Allocate,
            SecurityId,
            SuccessorId,
            100m,
            98m,
            1m,
            CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            TargetLotId: TargetLotId,
            TargetOperation: CorporateActionLotTargetOperationDto.Update,
            TargetBefore: new CorporateActionLotStateSnapshotDto(20m, 10m, 10m),
            TargetAfter: new CorporateActionLotStateSnapshotDto(120m, 108m, 106m),
            BasisAmount: 96m,
            SourceQuantity: 100m,
            SourceCarryingAmount: 98m,
            SourceBasisAmount: 96m);

        CorporateActionLotMutationPlanValidator.Validate([allocationWithoutTargetVersion]).Should()
            .Contain(blocker => blocker.Code == "corporate-action.lot-mutation-target-version-required");
        CorporateActionLotMutationPlanValidator.Validate(
                [allocationWithoutTargetVersion with { ExpectedTargetLotVersion = 7 }])
            .Should().BeEmpty();
    }

    [Fact]
    public void LotPlanValidator_ShouldRejectCreateVersionAndUpdateWithoutVersion()
    {
        var create = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Acquire,
            SuccessorId,
            Quantity: 5m,
            CarryingAmount: 100m,
            TargetLotId: TargetLotId,
            TargetOperation: CorporateActionLotTargetOperationDto.Create,
            ExpectedTargetLotVersion: 7,
            TargetAfter: new CorporateActionLotStateSnapshotDto(5m, 100m, 101m),
            BasisAmount: 101m);
        var update = create with
        {
            TargetOperation = CorporateActionLotTargetOperationDto.Update,
            ExpectedTargetLotVersion = null,
            TargetBefore = new CorporateActionLotStateSnapshotDto(2m, 25m, 26m),
            TargetAfter = new CorporateActionLotStateSnapshotDto(7m, 125m, 127m)
        };

        CorporateActionLotMutationPlanValidator.Validate([create]).Should().Contain(blocker =>
            blocker.Code == "corporate-action.lot-mutation-target-version-not-permitted");
        CorporateActionLotMutationPlanValidator.Validate([update]).Should().Contain(blocker =>
            blocker.Code == "corporate-action.lot-mutation-target-version-required");
    }

    [Fact]
    public void LotPlanValidator_ShouldNotTreatMissingDisposedQuantityAsFullDisposal()
    {
        var disposalWithoutQuantity = FullDisposalMutation(96m, 11) with { Quantity = null };

        CorporateActionLotMutationPlanValidator.Validate([disposalWithoutQuantity]).Should().Contain(blocker =>
            blocker.Code == "corporate-action.lot-mutation-partial-disposal-after-snapshot-required");
    }

    [Fact]
    public void Project_ShouldAcceptExplicitCreateAndUpdateTargetsForBookValueExchange()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.RegS144AExchange,
            economics: BookValueExchangeEconomics());
        var createMutation = BookValueExchangeMutation(CorporateActionLotTargetOperationDto.Create);
        var updateMutation = BookValueExchangeMutation(
            CorporateActionLotTargetOperationDto.Update,
            expectedTargetLotVersion: 7);

        var create = _sut.Project(request with { AuthoritativeLotMutations = [createMutation] });
        var update = _sut.Project(request with { AuthoritativeLotMutations = [updateMutation] });

        create.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        create.CanPreparePostingCandidate.Should().BeTrue();
        create.LotMutations!.Mutations.Single().TargetBefore.Should().BeNull();
        create.LotMutations.Mutations.Single().ExpectedTargetLotVersion.Should().BeNull();
        update.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        update.CanPreparePostingCandidate.Should().BeTrue();
        update.LotMutations!.Mutations.Single().TargetBefore.Should().NotBeNull();
        update.LotMutations.Mutations.Single().ExpectedTargetLotVersion.Should().Be(7);
        update.ProjectionInputHash.Should().NotBe(create.ProjectionInputHash);
        update.PostingIntentHash.Should().NotBe(create.PostingIntentHash);
    }

    [Fact]
    public void Project_ShouldReconcilePartialDisposalSourceBeforeAndAfter()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.CallRedemption,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 40m,
                CarryingAmount: 39m,
                ParAmount: 40m,
                GrossCashConsideration: 42m));
        var mutation = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Dispose,
            SecurityId,
            Quantity: 40m,
            CarryingAmount: 39m,
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            SourceAfter: new CorporateActionLotStateSnapshotDto(60m, 59m, 58m),
            BasisAmount: 38m);

        var result = _sut.Project(request with { AuthoritativeLotMutations = [mutation] });

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.CanPreparePostingCandidate.Should().BeTrue();
        result.LotMutations!.Mutations.Single().SourceAfter.Should().Be(
            new CorporateActionLotStateSnapshotDto(60m, 59m, 58m));
    }

    [Fact]
    public void LotPlanValidator_ShouldReconcileOneSourceAcrossManyCreatedTargets()
    {
        var sourceBefore = new CorporateActionLotStateSnapshotDto(100m, 100m, 90m);
        var first = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Allocate,
            SecurityId,
            SuccessorId,
            60m,
            60m,
            0.60m,
            CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            Description: "Carry source-lot lineage to successor",
            ReportingTags: ["ScheduleD"],
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: sourceBefore,
            TargetLotId: TargetLotId,
            TargetOperation: CorporateActionLotTargetOperationDto.Create,
            TargetAfter: new CorporateActionLotStateSnapshotDto(60m, 60m, 54m),
            BasisAmount: 54m,
            SourceQuantity: 60m,
            SourceCarryingAmount: 60m,
            SourceBasisAmount: 54m);
        var second = first with
        {
            TargetSecurityId = SecondSuccessorId,
            Quantity = 40m,
            CarryingAmount = 40m,
            AllocationPercent = 0.40m,
            ReportingTags = [],
            TargetLotId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            TargetAfter = new CorporateActionLotStateSnapshotDto(40m, 40m, 36m),
            BasisAmount = 36m,
            SourceQuantity = 40m,
            SourceCarryingAmount = 40m,
            SourceBasisAmount = 36m
        };

        CorporateActionLotMutationPlanValidator.Validate([first, second]).Should().BeEmpty();
        CorporateActionLotMutationPlanValidator.Validate(
                [first, second with { SourceQuantity = 39m }])
            .Should().Contain(blocker =>
                blocker.Code == "corporate-action.lot-mutation-source-transition-invalid");
        CorporateActionLotMutationPlanValidator.Validate(
                [first, second with { SourceBasisAmount = 35m }])
            .Should().Contain(blocker =>
                blocker.Code == "corporate-action.lot-mutation-source-transition-invalid");

        var projection = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.AdvanceRefunding,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 100m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Refunded,
                        60m,
                        0.60m),
                    new CorporateActionSuccessorAllocationDto(
                        SecondSuccessorId,
                        CorporateActionSuccessorRoleDto.Unrefunded,
                        40m,
                        0.40m)
                ])) with
        {
            AuthoritativeLotMutations = [first, second]
        });
        projection.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        projection.CanPreparePostingCandidate.Should().BeTrue();
    }

    [Fact]
    public void LotPlanValidator_ShouldReconcileManySourcesIntoOneUpdatedTarget()
    {
        var targetBefore = new CorporateActionLotStateSnapshotDto(10m, 8m, 7m);
        var targetAfter = new CorporateActionLotStateSnapshotDto(110m, 103m, 97m);
        var first = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Allocate,
            SecurityId,
            SuccessorId,
            40m,
            38m,
            HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: new CorporateActionLotStateSnapshotDto(40m, 38m, 35m),
            TargetLotId: TargetLotId,
            TargetOperation: CorporateActionLotTargetOperationDto.Update,
            ExpectedTargetLotVersion: 7,
            TargetBefore: targetBefore,
            TargetAfter: targetAfter,
            BasisAmount: 35m,
            SourceQuantity: 40m,
            SourceCarryingAmount: 38m,
            SourceBasisAmount: 35m);
        var second = first with
        {
            Quantity = 60m,
            CarryingAmount = 57m,
            SourceLotId = SecondSourceLotId,
            ExpectedSourceLotVersion = 4,
            SourceBefore = new CorporateActionLotStateSnapshotDto(60m, 57m, 55m),
            BasisAmount = 55m,
            SourceQuantity = 60m,
            SourceCarryingAmount = 57m,
            SourceBasisAmount = 55m
        };

        CorporateActionLotMutationPlanValidator.Validate([first, second]).Should().BeEmpty();
        CorporateActionLotMutationPlanValidator.Validate(
                [first, second with { TargetAfter = targetAfter with { BasisAmount = 96m } }])
            .Should().Contain(blocker =>
                blocker.Code == "corporate-action.lot-mutation-target-transition-conflict");
        CorporateActionLotMutationPlanValidator.Validate(
                [first, second with { BasisAmount = 54m }])
            .Should().Contain(blocker =>
                blocker.Code == "corporate-action.lot-mutation-target-delta-invalid");
    }

    [Fact]
    public void LotPlanValidator_ShouldReconcileInPlaceQuantityAndIndependentBasisReduction()
    {
        var quantityChange = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.ChangeQuantity,
            SecurityId,
            Quantity: 200m,
            CarryingAmount: 98m,
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            SourceAfter: new CorporateActionLotStateSnapshotDto(200m, 98m, 96m),
            BasisAmount: 0m);
        var carryingReduction = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.ReduceCarryingValue,
            SecurityId,
            CarryingAmount: 15m,
            SourceLotId: SecondSourceLotId,
            ExpectedSourceLotVersion: 4,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            SourceAfter: new CorporateActionLotStateSnapshotDto(100m, 83m, 86m),
            BasisAmount: 10m);

        CorporateActionLotMutationPlanValidator.Validate([quantityChange]).Should().BeEmpty();
        CorporateActionLotMutationPlanValidator.Validate([carryingReduction]).Should().BeEmpty(
            "carrying-value and basis reductions are independent measures");
        CorporateActionLotMutationPlanValidator.Validate(
                [quantityChange with { BasisAmount = 1m }])
            .Should().Contain(blocker =>
                blocker.Code == "corporate-action.lot-mutation-quantity-change-invalid");
        CorporateActionLotMutationPlanValidator.Validate(
                [carryingReduction with
                {
                    SourceAfter = new CorporateActionLotStateSnapshotDto(100m, 83m, 85m)
                }])
            .Should().Contain(blocker =>
                blocker.Code == "corporate-action.lot-mutation-carrying-reduction-invalid");
    }

    [Fact]
    public void Project_ShouldPairQuantityAndIndependentBasisReductionTemplates()
    {
        var quantityChange = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.ChangeQuantity,
            SecurityId,
            Quantity: 200m,
            CarryingAmount: 98m,
            HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.Unchanged,
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            SourceAfter: new CorporateActionLotStateSnapshotDto(200m, 98m, 96m),
            BasisAmount: 0m);
        var carryingReduction = new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.ReduceCarryingValue,
            SecurityId,
            CarryingAmount: 15m,
            HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.Unchanged,
            SourceLotId: SecondSourceLotId,
            ExpectedSourceLotVersion: 4,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            SourceAfter: new CorporateActionLotStateSnapshotDto(100m, 83m, 86m),
            BasisAmount: 10m);

        var split = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.StockSplit,
            economics: new CorporateActionEconomicsDto(
                PositionQuantity: 100m,
                CarryingAmount: 98m,
                SplitRatio: 2m)) with
        {
            AuthoritativeLotMutations = [quantityChange]
        });
        var returnOfCapital = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ReturnOfCapital,
            economics: new CorporateActionEconomicsDto(
                PositionQuantity: 100m,
                CarryingAmount: 98m,
                GrossCashConsideration: 15m)) with
        {
            AuthoritativeLotMutations = [carryingReduction]
        });

        split.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        split.LotMutations!.HasAuthoritativeLotResolution.Should().BeTrue();
        returnOfCapital.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        returnOfCapital.CanPreparePostingCandidate.Should().BeTrue();
    }

    [Fact]
    public void Project_ShouldFingerprintEverySourceAndTargetTransitionAuthority()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.RegS144AExchange,
            economics: BookValueExchangeEconomics());
        var baselineMutation = BookValueExchangeMutation(
            CorporateActionLotTargetOperationDto.Update,
            expectedTargetLotVersion: 7);
        var targetVersionChanged = baselineMutation with { ExpectedTargetLotVersion = 8 };
        var targetSnapshotsChanged = baselineMutation with
        {
            TargetBefore = new CorporateActionLotStateSnapshotDto(21m, 11m, 11m),
            TargetAfter = new CorporateActionLotStateSnapshotDto(121m, 109m, 107m)
        };
        var sourceSnapshotChanged = baselineMutation with
        {
            SourceBefore = new CorporateActionLotStateSnapshotDto(100m, 98m, 95m),
            SourceBasisAmount = 95m
        };
        var sourceAfterChanged = baselineMutation with
        {
            SourceAfter = new CorporateActionLotStateSnapshotDto(0m, 0m, 0m)
        };
        var targetBasisDeltaChanged = baselineMutation with
        {
            TargetAfter = new CorporateActionLotStateSnapshotDto(120m, 108m, 107m),
            BasisAmount = 97m
        };

        var results = new[]
        {
            baselineMutation,
            targetVersionChanged,
            targetSnapshotsChanged,
            sourceSnapshotChanged,
            sourceAfterChanged,
            targetBasisDeltaChanged
        }.Select(mutation => _sut.Project(request with { AuthoritativeLotMutations = [mutation] })).ToArray();

        results.Should().OnlyContain(result => result.Status == CorporateActionProjectionStatusDto.Projected);
        results.Select(result => result.ProjectionInputHash).Should().OnlyHaveUniqueItems();
        results.Select(result => result.PostingIntentHash).Should().OnlyHaveUniqueItems();
        results.Select(result => result.ProjectionLineage!.ProjectionRunId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Project_CA10_CA14_CA15_ShouldKeepIncomeAndPurchaseEconomicsDistinct()
    {
        var consent = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ConsentSolicitation,
            economics: new CorporateActionEconomicsDto(GrossCashConsideration: 3m),
            policy: new CorporateActionPolicyInputsDto(ConsentTermsChanged: false)));
        var dividend = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.CashDividend,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 100m, CashRatePerUnit: 1m)));
        var reinvestment = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.DividendReinvestment,
            economics: new CorporateActionEconomicsDto(
                PositionQuantity: 100m,
                CashRatePerUnit: 1m,
                PurchasePricePerUnit: 20m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SecurityId,
                        CorporateActionSuccessorRoleDto.Successor,
                        5m)
                ])));

        consent.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.ConsentIncome && component.Amount == 3m);
        dividend.EventAmount.Should().Be(100m);
        reinvestment.Recipe.Select(operation => operation.Kind).Should().Equal(
            CorporateActionEconomicOperationKindDto.DividendIncome,
            CorporateActionEconomicOperationKindDto.Purchase);
        reinvestment.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.DividendIncome && component.Amount == 100m);
        reinvestment.PostingSet.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.PurchaseCost && component.Amount == 100m);
    }

    [Fact]
    public void Project_CA09_ShouldKeepUnpaidConsentSolicitationReferenceOnly()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ConsentSolicitation,
            economics: new CorporateActionEconomicsDto(),
            policy: new CorporateActionPolicyInputsDto(ConsentTermsChanged: false)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.EventAmount.Should().Be(0m);
        result.Recipe.Should().ContainSingle(operation =>
            operation.Kind == CorporateActionEconomicOperationKindDto.ReferenceDataChange);
        result.PostingSet!.RequiresJournalCandidate.Should().BeFalse();
        result.PostingSet.Components.Should().BeEmpty();
    }

    [Fact]
    public void Project_CA11_ShouldBlockConsentWithChangedTermsForModificationWorkflow()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ConsentSolicitation,
            economics: new CorporateActionEconomicsDto(GrossCashConsideration: 3m),
            policy: new CorporateActionPolicyInputsDto(ConsentTermsChanged: true)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Blocked);
        result.Blockers.Select(blocker => blocker.Code).Should().Contain(
        [
            "corporate-action.consent-modification-assessment-required",
            "corporate-action.consent-modification-accounting-required"
        ]);
    }

    [Fact]
    public void Project_CA11_CA12_ShouldProjectNonStatConversionAndBlockUnapprovedStatTreatment()
    {
        var gaap = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.DebtToEquityConversion,
            AccountingBasisKindDto.Gaap,
            BookValueExchangeEconomics()));
        var statutory = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.DebtToEquityConversion,
            AccountingBasisKindDto.Statutory,
            BookValueExchangeEconomics()));

        gaap.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        gaap.Recipe.Select(operation => operation.Kind).Should().Equal(
            CorporateActionEconomicOperationKindDto.ExchangeOut,
            CorporateActionEconomicOperationKindDto.ExchangeIn);
        gaap.LotMutations!.Mutations.Single().CarryingAmount.Should().Be(98m);
        statutory.Status.Should().Be(CorporateActionProjectionStatusDto.Blocked);
        statutory.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.stat-conversion-treatment-required");
    }

    [Fact]
    public void Project_CA13_ShouldReconcileApprovedConversionCashToRecognitionAndSuccessorBasis()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.DebtToEquityConversion,
            economics: BookValueExchangeEconomics() with { GrossCashConsideration = 10m },
            policy: new CorporateActionPolicyInputsDto(
                CashRecognition: CorporateActionCashRecognitionDto.Gain,
                ApprovedCashRecognitionAmount: 5m,
                ApprovedSuccessorBasis: 93m)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.LotMutations!.Mutations.Should().ContainSingle(mutation => mutation.CarryingAmount == 93m);
        result.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain && component.Amount == 5m);
        result.PostingSet.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.PurchaseCost && component.Amount == 93m);
    }

    [Fact]
    public void Project_CA13_CA16_CA18_CA23_ShouldFailClosedForMissingPolicyDecisions()
    {
        var stockDividend = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.StockDividend,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 100m, SplitRatio: 1.10m)));
        var conversionWithCash = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.DebtToEquityConversion,
            economics: BookValueExchangeEconomics() with { GrossCashConsideration = 10m }));
        var exchangeOffer = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ExchangeOffer,
            economics: BookValueExchangeEconomics()));
        var mixedMerger = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.MergerMixed,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 100m,
                GrossCashConsideration: 10m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Acquirer,
                        10m,
                        1m,
                        95m)
                ])));

        stockDividend.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.stock-dividend-basis-treatment-required");
        conversionWithCash.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.conversion-cash-recognition-required");
        exchangeOffer.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.exchange-offer-method-required");
        mixedMerger.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.merger-recognition-required");
    }

    [Fact]
    public void Project_CA17_ShouldProjectApprovedScripSubscriptionLifecycleWithoutJournalIntent()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ScripDividend,
            economics: new CorporateActionEconomicsDto(
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Scrip,
                        100m)
                ]),
            policy: new CorporateActionPolicyInputsDto(
                ScripDividendTreatmentApproved: true,
                ScripFinalDistributionCaseId: Guid.Parse("99999999-9999-9999-9999-999999999999"))));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.Recipe.Should().ContainSingle(operation =>
            operation.Kind == CorporateActionEconomicOperationKindDto.TransferIn &&
            operation.LinkedCaseId == Guid.Parse("99999999-9999-9999-9999-999999999999"));
        result.LotMutations!.Mutations.Should().ContainSingle(mutation =>
            mutation.Kind == CorporateActionLotMutationKindDto.TransferIn &&
            mutation.LinkedCaseId == Guid.Parse("99999999-9999-9999-9999-999999999999"));
        result.PostingSet!.RequiresJournalCandidate.Should().BeFalse();
    }

    [Fact]
    public void Project_CA19_ShouldApplyApprovedExchangeOfferSaleAndPurchaseElection()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ExchangeOffer,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 100m,
                GrossCashConsideration: 110m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Successor,
                        10m,
                        FairValue: 110m)
                ]),
            policy: new CorporateActionPolicyInputsDto(
                ExchangeOfferMethod: CorporateActionExchangeOfferMethodDto.SaleAndPurchase,
                ApprovedTaxClassification: CorporateActionTaxClassificationDto.PreliminaryTaxable,
                ExchangeOfferIsMaterial: true)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.Recipe.Select(operation => operation.Kind).Should().Equal(
            CorporateActionEconomicOperationKindDto.CorporateActionSale,
            CorporateActionEconomicOperationKindDto.Purchase);
        result.LotMutations!.Mutations.Select(mutation => mutation.Kind).Should().Equal(
            CorporateActionLotMutationKindDto.Dispose,
            CorporateActionLotMutationKindDto.Acquire);
        result.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain && component.Amount == 10m);
        result.PostingSet.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.PurchaseCost && component.Amount == 110m);
    }

    [Fact]
    public void Project_CA19_DirectExchangeCash_ShouldUseApprovedRecognitionAndReconciledBasis()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ExchangeOffer,
            economics: BookValueExchangeEconomics() with { GrossCashConsideration = 10m },
            policy: new CorporateActionPolicyInputsDto(
                ExchangeOfferMethod: CorporateActionExchangeOfferMethodDto.DirectExchange,
                CashRecognition: CorporateActionCashRecognitionDto.Gain,
                ApprovedCashRecognitionAmount: 5m,
                ApprovedSuccessorBasis: 93m,
                ApprovedTaxClassification: CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                ExchangeOfferIsMaterial: false)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.LotMutations!.Mutations.Should().ContainSingle(mutation => mutation.CarryingAmount == 93m);
        result.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain && component.Amount == 5m);
    }

    [Fact]
    public void Project_CA20_CA22_CA26_ShouldCalculateDispositionGainLoss()
    {
        var cashInLieu = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.FractionalCashInLieu,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 0.4m,
                CarryingAmount: 3.5m,
                GrossCashConsideration: 4m)));
        var cashMerger = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.MergerCash,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 80m,
                GrossCashConsideration: 100m)));
        var put = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.PutRedemption,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 98m,
                GrossCashConsideration: 100m)));

        cashInLieu.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain && component.Amount == 0.5m);
        cashMerger.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain && component.Amount == 20m);
        put.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain && component.Amount == 2m);
    }

    [Fact]
    public void Project_CA21_CA24_ShouldRequireMergerHoldingPolicyButPreserveIdentityChangeLineage()
    {
        var merger = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.MergerStock,
            economics: BookValueExchangeEconomics()));
        var approvedMerger = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.MergerStock,
            economics: BookValueExchangeEconomics(),
            policy: new CorporateActionPolicyInputsDto(CarryHoldingPeriod: true)));
        var nameChange = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.NameIdentifierChange,
            economics: BookValueExchangeEconomics()));

        merger.Blockers.Should().Contain(blocker => blocker.Code == "corporate-action.merger-holding-period-required");
        approvedMerger.LotMutations!.Mutations.Single().HoldingPeriodTreatment.Should()
            .Be(CorporateActionHoldingPeriodTreatmentDto.CarryOver);
        nameChange.LotMutations!.Mutations.Single().HoldingPeriodTreatment.Should()
            .Be(CorporateActionHoldingPeriodTreatmentDto.CarryOver);
    }

    [Fact]
    public void Project_CA23_ShouldDeriveMixedMergerSuccessorBasisFromApprovedRecognitionModel()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.MergerMixed,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 100m,
                GrossCashConsideration: 10m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Acquirer,
                        10m,
                        1m,
                        95m)
                ]),
            policy: new CorporateActionPolicyInputsDto(
                MergerRecognition: CorporateActionMergerRecognitionDto.GainLimitedToCashNoLoss,
                CarryHoldingPeriod: true)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.LotMutations!.Mutations.Should().ContainSingle(mutation => mutation.CarryingAmount == 95m);
        result.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.RealizedGain && component.Amount == 5m);
        result.PostingSet.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.PurchaseCost && component.Amount == 95m);
    }

    [Fact]
    public void Project_CA25_CA27_CA28_ShouldProjectPikAndCapReturnOfCapitalAtBasis()
    {
        var pik = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.PaymentInKind,
            economics: new CorporateActionEconomicsDto(
                ParAmount: 1_000m,
                Rate: 0.05m,
                PurchasePricePerUnit: 1m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SecurityId,
                        CorporateActionSuccessorRoleDto.Successor,
                        50m)
                ])));
        var returnOfCapital = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ReturnOfCapital,
            economics: new CorporateActionEconomicsDto(
                CarryingAmount: 100m,
                GrossCashConsideration: 15m)));
        var excess = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ReturnOfCapital,
            economics: new CorporateActionEconomicsDto(
                CarryingAmount: 100m,
                GrossCashConsideration: 120m)));

        pik.EventAmount.Should().Be(50m);
        pik.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.PurchaseCost && component.Amount == 50m);
        returnOfCapital.LotMutations!.Mutations.Single().CarryingAmount.Should().Be(15m);
        excess.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.return-of-capital-exceeds-basis");
    }

    [Fact]
    public void Project_CA29_CA31_CA33_CA34_CA37_CA38_ShouldHandleUnitOnlyActionsAndFractions()
    {
        var reverseSplit = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ReverseStockSplit,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 1_000m, SplitRatio: 0.1m)));
        var rightsBlocked = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RightsDistribution,
            economics: RightsDistributionEconomics()));
        var rightsApproved = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RightsDistribution,
            economics: RightsDistributionEconomics(),
            policy: new CorporateActionPolicyInputsDto(RightsZeroValueApproved: true)));
        var valuedRights = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RightsDistribution,
            economics: RightsDistributionEconomics() with
            {
                Successors =
                [
                    RightsDistributionEconomics().Successors.Single() with { FairValue = 12m }
                ]
            },
            policy: new CorporateActionPolicyInputsDto(RightsZeroValueApproved: true)));
        var exercise = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RightsExercise,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 0m,
                SubscriptionPricePerUnit: 10m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Underlying,
                        100m)
                ])));
        var expiration = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RightsExpiration,
            economics: new CorporateActionEconomicsDto(AffectedQuantity: 100m, CarryingAmount: 0m)));
        var exerciseWithBasis = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RightsExercise,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 4m,
                SubscriptionPricePerUnit: 10m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Underlying,
                        100m)
                ])));
        var split = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.StockSplit,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 100m, SplitRatio: 2m)));
        var fractionalSplit = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.StockSplit,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 101m, SplitRatio: 1.5m)));
        var linkedFractionalSplit = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.StockSplit,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 101m, SplitRatio: 1.5m),
            policy: new CorporateActionPolicyInputsDto(
                FractionalCashInLieuCaseId: Guid.Parse("99999999-9999-9999-9999-999999999999"))));

        reverseSplit.LotMutations!.Mutations.Single().Quantity.Should().Be(100m);
        rightsBlocked.Status.Should().Be(CorporateActionProjectionStatusDto.Blocked);
        rightsApproved.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        rightsApproved.PostingSet!.RequiresJournalCandidate.Should().BeFalse();
        valuedRights.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.rights-observable-value-review-required");
        exercise.EventAmount.Should().Be(1_000m);
        exercise.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.Cash && component.Amount == 1_000m);
        exerciseWithBasis.LotMutations!.Mutations.Should().Contain(mutation =>
            mutation.Kind == CorporateActionLotMutationKindDto.Acquire && mutation.CarryingAmount == 1_004m);
        expiration.Recipe.Should().ContainSingle(operation =>
            operation.Kind == CorporateActionEconomicOperationKindDto.TransferOut);
        split.LotMutations!.Mutations.Single().Quantity.Should().Be(200m);
        fractionalSplit.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.split-fractional-residual");
        linkedFractionalSplit.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        linkedFractionalSplit.LotMutations!.Mutations.Should().ContainSingle(mutation =>
            mutation.LinkedCaseId == Guid.Parse("99999999-9999-9999-9999-999999999999"));
    }

    [Fact]
    public void Project_CA30_ShouldUseCarryoverExchangeForReverseSplitIdentifierChange()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.ReverseStockSplit,
            economics: new CorporateActionEconomicsDto(
                PositionQuantity: 1_000m,
                CarryingAmount: 975m,
                SplitRatio: 0.1m,
                IdentifierChanged: true,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Successor,
                        100m)
                ])));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.Recipe.Select(operation => operation.Kind).Should().Equal(
            CorporateActionEconomicOperationKindDto.ExchangeOut,
            CorporateActionEconomicOperationKindDto.ExchangeIn);
        result.LotMutations!.Mutations.Should().ContainSingle(mutation =>
            mutation.Kind == CorporateActionLotMutationKindDto.CarryOver &&
            mutation.TargetSecurityId == SuccessorId &&
            mutation.CarryingAmount == 975m &&
            mutation.HoldingPeriodTreatment == CorporateActionHoldingPeriodTreatmentDto.CarryOver);
    }

    [Fact]
    public void Project_CA32_ShouldApplyExplicitZeroValueApprovalForRightsDistribution()
    {
        var result = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.RightsDistribution,
            economics: RightsDistributionEconomics(),
            policy: new CorporateActionPolicyInputsDto(RightsZeroValueApproved: true)));

        result.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        result.EventAmount.Should().Be(0m);
        result.Recipe.Should().ContainSingle(operation =>
            operation.Kind == CorporateActionEconomicOperationKindDto.TransferIn &&
            operation.Amount == 0m);
        result.LotMutations!.Mutations.Should().ContainSingle(mutation =>
            mutation.Kind == CorporateActionLotMutationKindDto.TransferIn &&
            mutation.CarryingAmount == 0m);
        result.PostingSet!.RequiresJournalCandidate.Should().BeFalse();
    }

    [Fact]
    public void Project_CA35_CA36_ShouldSeparateTaxableAndNonTaxableSpinOffRecipes()
    {
        var nonTaxable = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.SpinOff,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 100m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SecurityId,
                        CorporateActionSuccessorRoleDto.Successor,
                        100m,
                        0.80m),
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Child,
                        20m,
                        0.20m)
                ]),
            policy: new CorporateActionPolicyInputsDto(
                SpinOffTaxTreatment: CorporateActionSpinOffTaxTreatmentDto.NonTaxableBasisAllocation)));
        var taxable = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.SpinOff,
            economics: new CorporateActionEconomicsDto(
                GrossCashConsideration: 20m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Child,
                        20m,
                        FairValue: 20m)
                ]),
            policy: new CorporateActionPolicyInputsDto(
                SpinOffTaxTreatment: CorporateActionSpinOffTaxTreatmentDto.TaxableDividendAndPurchase)));
        var missingRetainedParent = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.SpinOff,
            economics: new CorporateActionEconomicsDto(
                AffectedQuantity: 100m,
                CarryingAmount: 100m,
                Successors:
                [
                    new CorporateActionSuccessorAllocationDto(
                        SuccessorId,
                        CorporateActionSuccessorRoleDto.Child,
                        20m,
                        1m)
                ]),
            policy: new CorporateActionPolicyInputsDto(
                SpinOffTaxTreatment: CorporateActionSpinOffTaxTreatmentDto.NonTaxableBasisAllocation)));

        nonTaxable.LotMutations!.Mutations.Select(mutation => mutation.CarryingAmount)
            .Should().Equal(80m, 20m);
        taxable.Recipe.Select(operation => operation.Kind).Should().Equal(
            CorporateActionEconomicOperationKindDto.DividendIncome,
            CorporateActionEconomicOperationKindDto.Purchase);
        missingRetainedParent.Blockers.Should().Contain(blocker =>
            blocker.Code == "corporate-action.spin-off-retained-parent-required");
    }

    [Fact]
    public void Project_CA39_CA40_CA41_CA42_CA43_ShouldKeepTenderBasisDifferencesAndAllocation()
    {
        var economics = new CorporateActionEconomicsDto(
            AffectedQuantity: 100m,
            CarryingAmount: 95m,
            ParAmount: 100m,
            GrossCashConsideration: 105m);
        var gaap = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.TenderOffer,
            AccountingBasisKindDto.Gaap,
            economics));
        var statutoryAbovePar = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.TenderOffer,
            AccountingBasisKindDto.Statutory,
            economics));
        var statutoryBelowPar = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.TenderOffer,
            AccountingBasisKindDto.Statutory,
            economics with { GrossCashConsideration = 95m }));
        var statutoryCustom = _sut.Project(CreateRequest(
            CorporateActionAccountingTypeDto.TenderOffer,
            AccountingBasisKindDto.Statutory,
            economics with { GrossCashConsideration = 95m },
            new CorporateActionPolicyInputsDto(StatutoryTenderIncomeAllocationPercent: 0.10m)));

        gaap.Recipe.Should().ContainSingle(operation =>
            operation.Kind == CorporateActionEconomicOperationKindDto.CorporateActionSale);
        statutoryAbovePar.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.InvestmentIncome && component.Amount == 5m);
        statutoryBelowPar.PostingSet!.Components.Should().NotContain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.InvestmentIncome);
        statutoryCustom.PostingSet!.Components.Should().Contain(component =>
            component.Kind == CorporateActionPostingComponentKindDto.InvestmentIncome && component.Amount == 9.50m);
        gaap.EconomicEvent!.EventId.Should().NotBe(statutoryAbovePar.EconomicEvent!.EventId);
    }

    [Fact]
    public void Project_ShouldProduceStableIdentityAcrossGenerationTimeAndCulture()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.CashDividend,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 100m, CashRatePerUnit: 1m));
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var first = _sut.Project(request with { GeneratedAtUtc = DateTimeOffset.Parse("2026-08-25T12:00:00Z") });
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
            var second = _sut.Project(request with { GeneratedAtUtc = DateTimeOffset.Parse("2027-01-01T12:00:00Z") });

            second.EconomicEvent!.EventId.Should().Be(first.EconomicEvent!.EventId);
            second.ProjectionLineage!.ProjectionRunId.Should().Be(first.ProjectionLineage!.ProjectionRunId);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Project_ShouldChangeProjectionIdentityWhenAnyGovernedDependencyChanges()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.CashDividend,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 100m, CashRatePerUnit: 1m));
        var baseline = _sut.Project(request);
        var positionChanged = _sut.Project(request with
        {
            PositionVersion = 8,
            ExpectedPositionVersion = 8,
            PositionSnapshotId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            EvidenceManifest = request.EvidenceManifest
                .Select(item => item.Role == CorporateActionProjectionEvidenceRoleDto.PositionSnapshot
                    ? item with
                    {
                        SubjectId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd").ToString("D"),
                        EvidenceVersion = 8
                    }
                    : item)
                .ToArray()
        });
        var policyChanged = _sut.Project(request with
        {
            PolicyInputs = request.PolicyInputs with
            {
                ApprovedTaxClassification = CorporateActionTaxClassificationDto.PreliminaryTaxable
            }
        });
        var economicsChanged = _sut.Project(request with
        {
            Economics = request.Economics with { CashRatePerUnit = 1.01m }
        });
        var evidenceChanged = _sut.Project(request with
        {
            EvidenceManifest = request.EvidenceManifest
                .Select(item => item.Role == CorporateActionProjectionEvidenceRoleDto.LotSnapshot
                    ? item with { ContentHashSha256 = new string('e', 64) }
                    : item)
                .ToArray()
        });

        var runIds = new[]
        {
            baseline.ProjectionLineage!.ProjectionRunId,
            positionChanged.ProjectionLineage!.ProjectionRunId,
            policyChanged.ProjectionLineage!.ProjectionRunId,
            economicsChanged.ProjectionLineage!.ProjectionRunId,
            evidenceChanged.ProjectionLineage!.ProjectionRunId
        };
        runIds.Should().OnlyHaveUniqueItems();
        baseline.EconomicEvent!.EventId.Should().Be(positionChanged.EconomicEvent!.EventId);
        baseline.ProjectionInputHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Project_ShouldRejectProfileDateStaleVersionAndMissingEvidence()
    {
        var request = CreateRequest(
            CorporateActionAccountingTypeDto.CashDividend,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 100m, CashRatePerUnit: 1m));
        var result = _sut.Project(request with
        {
            RuleProfileAsOfDate = new DateOnly(2026, 8, 24),
            ExpectedPositionVersion = 6,
            EvidenceManifest = []
        });

        result.Blockers.Select(blocker => blocker.Code).Should().Contain(
            "corporate-action.rule-profile-not-effective",
            "corporate-action.position-version-stale",
            "corporate-action.evidence-required");
    }

    private static CorporateActionAccountingProjectionRequest CreateRequest(
        CorporateActionAccountingTypeDto actionType,
        AccountingBasisKindDto accountingBasis = AccountingBasisKindDto.Gaap,
        CorporateActionEconomicsDto? economics = null,
        CorporateActionPolicyInputsDto? policy = null)
        => new(
            SourceCorporateActionId,
            1,
            actionType,
            accountingBasis,
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
            economics ?? new CorporateActionEconomicsDto(),
            policy,
            CreateEvidenceManifest(),
            CaseId: CaseId,
            CaseVersion: 3,
            ElectionVersion: null,
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
                SourceCorporateActionId.ToString("D")),
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

    private static CorporateActionEconomicsDto BookValueExchangeEconomics()
        => new(
            AffectedQuantity: 100m,
            CarryingAmount: 98m,
            Successors:
            [
                new CorporateActionSuccessorAllocationDto(
                    SuccessorId,
                    CorporateActionSuccessorRoleDto.Successor,
                    100m)
            ]);

    private static CorporateActionEconomicsDto CallEconomics()
        => new(
            AffectedQuantity: 100m,
            CarryingAmount: 98m,
            ParAmount: 100m,
            GrossCashConsideration: 103m,
            AccruedIncome: 2m,
            IsMakeWhole: true);

    private static CorporateActionLotMutationDto BookValueExchangeMutation(
        CorporateActionLotTargetOperationDto targetOperation,
        long? expectedTargetLotVersion = null)
        => new(
            CorporateActionLotMutationKindDto.CarryOver,
            SecurityId,
            SuccessorId,
            100m,
            98m,
            HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            Description: "Carry source-lot lineage to successor",
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: 11,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            TargetLotId: TargetLotId,
            TargetOperation: targetOperation,
            ExpectedTargetLotVersion: expectedTargetLotVersion,
            TargetBefore: targetOperation == CorporateActionLotTargetOperationDto.Update
                ? new CorporateActionLotStateSnapshotDto(20m, 10m, 10m)
                : null,
            TargetAfter: targetOperation == CorporateActionLotTargetOperationDto.Update
                ? new CorporateActionLotStateSnapshotDto(120m, 108m, 106m)
                : new CorporateActionLotStateSnapshotDto(100m, 98m, 96m),
            BasisAmount: 96m,
            SourceQuantity: 100m,
            SourceCarryingAmount: 98m,
            SourceBasisAmount: 96m);

    private static CorporateActionLotMutationDto FullDisposalMutation(
        decimal basisAmount,
        long expectedSourceLotVersion)
        => new(
            CorporateActionLotMutationKindDto.Dispose,
            SecurityId,
            Quantity: 100m,
            CarryingAmount: 98m,
            SourceLotId: SourceLotId,
            ExpectedSourceLotVersion: expectedSourceLotVersion,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, basisAmount),
            BasisAmount: basisAmount);

    private static CorporateActionEconomicsDto RightsDistributionEconomics()
        => new(
            Successors:
            [
                new CorporateActionSuccessorAllocationDto(
                    SuccessorId,
                    CorporateActionSuccessorRoleDto.Right,
                    100m)
            ]);
}
