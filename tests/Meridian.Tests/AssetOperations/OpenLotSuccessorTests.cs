using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotSuccessorTests
{
    [Fact]
    public void Exchange_CarriesRemainingAcquisitionFactsAndCurrentBasisIndependently()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var successor = OpenLotSuccessorTestData.Successor(source, 1m);
        var instruction = OpenLotSuccessorTestData.Build(source, [successor]);

        var validate = () => OpenLotSuccessors.Validate(instruction);

        validate.Should().NotThrow();
        instruction.Projection.CanPreparePostingCandidate.Should().BeTrue();
        var carried = instruction.Successors.Single().Lot;
        carried.OpenTransactionCostBasis.Should().Be(594m);
        carried.OpenFunctionalCostBasis.Should().Be(653.4m);
        carried.Acquisition.TransactionCostBasis.Should().Be(600m);
        carried.Acquisition.FunctionalCostBasis.Should().Be(660m);
        carried.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
        carried.AcquiredDate.Should().Be(source.AcquiredDate);
        carried.Acquisition.HoldingPeriodStartDate.Should().Be(source.Acquisition.HoldingPeriodStartDate);
        carried.Acquisition.FaceValueTerms.Should().Be(source.Acquisition.FaceValueTerms);
        carried.Acquisition.Evidence.Should().Contain(source.Acquisition.Evidence);
        instruction.Projection.LotMutations!.Mutations.Single().SourceAfter.Should().BeNull();
    }

    [Fact]
    public void AdvanceRefunding_ReconcilesBothCurrenciesAndTagsOnlyTheRefundedSuccessor()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var refunded = OpenLotSuccessorTestData.Successor(source, 0.6m);
        var unrefunded = OpenLotSuccessorTestData.Successor(source, 0.4m);
        var instruction = OpenLotSuccessorTestData.Build(source, [refunded, unrefunded], advanceRefunding: true);

        var validate = () => OpenLotSuccessors.Validate(instruction);

        validate.Should().NotThrow();
        instruction.Successors.Sum(item => item.Lot.OpenTransactionCostBasis).Should().Be(source.OpenTransactionCostBasis);
        instruction.Successors.Sum(item => item.Lot.OpenFunctionalCostBasis).Should().Be(source.OpenFunctionalCostBasis);
        instruction.Successors.Sum(item => item.Lot.Acquisition.TransactionCostBasis).Should().Be(600m);
        instruction.Successors.Sum(item => item.Lot.Acquisition.FunctionalCostBasis).Should().Be(660m);
        var mutations = instruction.Projection.LotMutations!.Mutations;
        mutations.Single(item => item.TargetLotId == refunded.Lot.TaxLotRecordId).ReportingTags.Should().Equal("ScheduleD");
        mutations.Single(item => item.TargetLotId == unrefunded.Lot.TaxLotRecordId).ReportingTags.Should().BeEmpty();
        mutations.Sum(item => item.SourceBasisAmount).Should().Be(source.OpenTransactionCostBasis);
        mutations.Sum(item => item.SourceCarryingAmount).Should().Be(source.OpenFunctionalCostBasis);
    }

    [Fact]
    public void AdvanceRefunding_AssignsFinalCurrencyResidualsWithoutRecomputingAtSpotFx()
    {
        var predecessor = OpenLotSuccessorTestData.Predecessor();
        var source = predecessor with
        {
            OpenQuantity = 100m,
            OpenTransactionCostBasis = 100.03m,
            OpenFunctionalCostBasis = 110.03m,
            Acquisition = predecessor.Acquisition with { TransactionCostBasis = 100.01m, FunctionalCostBasis = 110.01m }
        };
        var first = OpenLotSuccessorTestData.Successor(source, 0.5m);
        var roundedSecond = OpenLotSuccessorTestData.Successor(source, 0.5m);
        var second = roundedSecond with
        {
            Lot = roundedSecond.Lot with
            {
                OpenTransactionCostBasis = 50.01m,
                OpenFunctionalCostBasis = 55.01m,
                Acquisition = roundedSecond.Lot.Acquisition with { TransactionCostBasis = 50m, FunctionalCostBasis = 55m }
            }
        };
        var instruction = OpenLotSuccessorTestData.Build(source, [first, second], advanceRefunding: true);

        var validate = () => OpenLotSuccessors.Validate(instruction);

        validate.Should().NotThrow();
        first.Lot.OpenTransactionCostBasis.Should().Be(50.02m);
        first.Lot.OpenFunctionalCostBasis.Should().Be(55.02m);
        instruction.Successors.Sum(item => item.Lot.OpenTransactionCostBasis).Should().Be(100.03m);
        instruction.Successors.Sum(item => item.Lot.OpenFunctionalCostBasis).Should().Be(110.03m);
        instruction.Successors.Sum(item => item.Lot.Acquisition.TransactionCostBasis).Should().Be(100.01m);
        instruction.Successors.Sum(item => item.Lot.Acquisition.FunctionalCostBasis).Should().Be(110.01m);
    }

    [Theory]
    [InlineData("CLF", "UYW")]
    [InlineData("UYW", "CLF")]
    public void AdvanceRefunding_PreservesFourDecimalAcquisitionAndCurrentBasesThroughProjection(
        string transactionCurrency, string functionalCurrency)
    {
        var predecessor = OpenLotSuccessorTestData.Predecessor();
        var source = predecessor with
        {
            OpenQuantity = 100m,
            OpenTransactionCostBasis = 100.0301m,
            OpenFunctionalCostBasis = 110.0301m,
            Acquisition = predecessor.Acquisition with
            {
                AcquisitionCurrency = transactionCurrency,
                FunctionalCurrency = functionalCurrency,
                TransactionCostBasis = 100.0101m,
                FunctionalCostBasis = 110.0111m
            }
        };
        var first = OpenLotSuccessorTestData.Successor(source, 0.5m);
        var roundedSecond = OpenLotSuccessorTestData.Successor(source, 0.5m);
        var second = roundedSecond with
        {
            Lot = roundedSecond.Lot with
            {
                OpenTransactionCostBasis = 50.0150m,
                OpenFunctionalCostBasis = 55.0150m,
                Acquisition = roundedSecond.Lot.Acquisition with
                {
                    TransactionCostBasis = 50.0050m,
                    FunctionalCostBasis = 55.0055m
                }
            }
        };
        var instruction = OpenLotSuccessorTestData.Build(source, [first, second], advanceRefunding: true);

        var validate = () => OpenLotSuccessors.Validate(instruction);

        validate.Should().NotThrow();
        instruction.Projection.EventAmount.Should().Be(110.0301m);
        instruction.Projection.LotMutations!.Mutations.Select(item => item.CarryingAmount)
            .Should().Equal(55.0151m, 55.0150m);
        first.Lot.OpenTransactionCostBasis.Should().Be(50.0151m);
        first.Lot.Acquisition.TransactionCostBasis.Should().Be(50.0051m);
        first.Lot.Acquisition.FunctionalCostBasis.Should().Be(55.0056m);
        instruction.Successors.Sum(item => item.Lot.OpenTransactionCostBasis).Should().Be(100.0301m);
        instruction.Successors.Sum(item => item.Lot.OpenFunctionalCostBasis).Should().Be(110.0301m);
        instruction.Successors.Sum(item => item.Lot.Acquisition.TransactionCostBasis).Should().Be(100.0101m);
        instruction.Successors.Sum(item => item.Lot.Acquisition.FunctionalCostBasis).Should().Be(110.0111m);
        instruction.Successors.Should().OnlyContain(item => item.Lot.Acquisition.AcquisitionFxRateToFunctional == 1.1m);
    }

    [Theory]
    [InlineData(CorporateActionSuccessorRoleDto.Refunded)]
    [InlineData(CorporateActionSuccessorRoleDto.Unrefunded)]
    [InlineData(CorporateActionSuccessorRoleDto.Escrow)]
    [InlineData(CorporateActionSuccessorRoleDto.Child)]
    public void Exchange_RejectsRolesOutsideTheApprovedSuccessorWorkflow(CorporateActionSuccessorRoleDto role)
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var instruction = OpenLotSuccessorTestData.Build(source,
            [OpenLotSuccessorTestData.Successor(source, 1m)], exchangeRole: role);

        var validate = () => OpenLotSuccessors.Validate(instruction);

        instruction.Projection.CanPreparePostingCandidate.Should().BeTrue();
        instruction.Projection.LotMutations!.Mutations.Single().ReportingTags.Should().BeEmpty();
        validate.Should().Throw<ArgumentException>().WithMessage("*role must match*");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(90)]
    public void AdvanceRefunding_RejectsZeroRoundedOrResidualFunctionalBasisBeforeDrafting(int firstAllocationPercent)
    {
        var source = OpenLotSuccessorTestData.Predecessor() with { OpenFunctionalCostBasis = 0.01m };
        var fraction = firstAllocationPercent / 100m;
        var first = OpenLotSuccessorTestData.Successor(source, fraction);
        var roundedSecond = OpenLotSuccessorTestData.Successor(source, 1m - fraction);
        var second = roundedSecond with
        {
            Lot = roundedSecond.Lot with
            {
                OpenFunctionalCostBasis = source.OpenFunctionalCostBasis - first.Lot.OpenFunctionalCostBasis
            }
        };
        var instruction = OpenLotSuccessorTestData.Build(source, [first, second], advanceRefunding: true);

        var validate = () => OpenLotSuccessors.Validate(instruction);
        var issues = AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.CorporateAction,
            new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CorporateAction, CorporateAction: instruction),
            instruction.Projection.EventAmount, OpenLotSuccessorTestData.EffectiveDate, []);

        instruction.Projection.CanPreparePostingCandidate.Should().BeTrue();
        instruction.Successors.Should().ContainSingle(item => item.Lot.OpenFunctionalCostBasis == 0m);
        validate.Should().Throw<ArgumentException>().WithMessage("*positive functional carrying-basis allocation*");
        issues.Should().Equal("Every successor requires a positive functional carrying-basis allocation before journal drafting.");
    }

    [Fact]
    public void Validate_RejectsPredecessorSnapshotOrVersionDifferentFromTheReviewedPlan()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var instruction = OpenLotSuccessorTestData.Build(source, [OpenLotSuccessorTestData.Successor(source, 1m)]);

        var changedVersion = () => OpenLotSuccessors.Validate(instruction with
        {
            ExpectedLot = source with { Version = source.Version + 1 }
        });
        var changedBasis = () => OpenLotSuccessors.Validate(instruction with
        {
            ExpectedLot = source with { OpenTransactionCostBasis = source.OpenTransactionCostBasis + 0.01m }
        });

        changedVersion.Should().Throw<ArgumentException>();
        changedBasis.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("fx")]
    [InlineData("holding-period")]
    [InlineData("transaction-basis")]
    [InlineData("functional-basis")]
    [InlineData("acquisition-basis")]
    [InlineData("security-identity")]
    [InlineData("lot-identity")]
    [InlineData("source-evidence")]
    public void Validate_RejectsSuccessorFactsOutsideTheApprovedProjection(string changedFact)
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var target = OpenLotSuccessorTestData.Successor(source, 1m);
        var instruction = OpenLotSuccessorTestData.Build(source, [target]);
        var changed = changedFact switch
        {
            "fx" => target.Lot with
            {
                Acquisition = target.Lot.Acquisition with { AcquisitionFxRateToFunctional = 1.2m, FunctionalCostBasis = 720m }
            },
            "holding-period" => target.Lot with
            {
                Acquisition = target.Lot.Acquisition with { HoldingPeriodStartDate = source.Acquisition.HoldingPeriodStartDate.AddDays(-1) }
            },
            "transaction-basis" => target.Lot with { OpenTransactionCostBasis = target.Lot.OpenTransactionCostBasis + 0.01m },
            "functional-basis" => target.Lot with { OpenFunctionalCostBasis = target.Lot.OpenFunctionalCostBasis + 0.01m },
            "acquisition-basis" => target.Lot with
            {
                Acquisition = target.Lot.Acquisition with { TransactionCostBasis = 500m, FunctionalCostBasis = 550m }
            },
            "security-identity" => target.Lot with { SecurityId = Guid.NewGuid() },
            "lot-identity" => target.Lot with { TaxLotRecordId = Guid.NewGuid() },
            "source-evidence" => target.Lot with
            {
                Acquisition = target.Lot.Acquisition with
                {
                    Evidence = target.Lot.Acquisition.Evidence.Where(item => !source.Acquisition.Evidence.Contains(item)).ToArray()
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(changedFact))
        };

        var validate = () => OpenLotSuccessors.Validate(instruction with { Successors = [target with { Lot = changed }] });

        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_RejectsScheduleDOnTheUnrefundedSuccessor()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var instruction = OpenLotSuccessorTestData.Build(source,
            [OpenLotSuccessorTestData.Successor(source, 0.6m), OpenLotSuccessorTestData.Successor(source, 0.4m)],
            advanceRefunding: true);
        var projection = instruction.Projection;
        var mutations = projection.LotMutations!.Mutations;
        var altered = projection with
        {
            LotMutations = projection.LotMutations with
            {
                Mutations = [mutations[0], mutations[1] with { ReportingTags = ["ScheduleD"] }]
            }
        };

        var validate = () => OpenLotSuccessors.Validate(instruction with { Projection = altered });

        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AdvanceRefunding_RejectsInflatedFaceEvenWhenTheAuthoritativeBasisPlanReconciles()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var refunded = OpenLotSuccessorTestData.Successor(source, 0.6m);
        refunded = refunded with
        {
            Lot = refunded.Lot with { OriginalQuantity = 37m, OpenQuantity = 37m }
        };
        var unrefunded = OpenLotSuccessorTestData.Successor(source, 0.4m);
        var instruction = OpenLotSuccessorTestData.Build(source, [refunded, unrefunded], advanceRefunding: true,
            allocationPercents: [0.6m, 0.4m]);

        instruction.Projection.CanPreparePostingCandidate.Should().BeTrue();
        instruction.Successors.Sum(item => item.Lot.OpenFunctionalCostBasis).Should().Be(source.OpenFunctionalCostBasis);
        var validate = () => OpenLotSuccessors.Validate(instruction);

        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AdvanceRefunding_RejectsRefundedRoleMovedFromSuccessorToPredecessor()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var instruction = OpenLotSuccessorTestData.Build(source,
            [OpenLotSuccessorTestData.Successor(source, 0.6m), OpenLotSuccessorTestData.Successor(source, 0.4m)],
            advanceRefunding: true);
        var projection = instruction.Projection;
        var malformed = projection with
        {
            Recipe = projection.Recipe.Select(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeOut
                ? operation with { SuccessorRole = CorporateActionSuccessorRoleDto.Refunded }
                : operation.SuccessorRole == CorporateActionSuccessorRoleDto.Refunded
                    ? operation with { SuccessorRole = CorporateActionSuccessorRoleDto.Successor }
                    : operation).ToArray(),
            LotMutations = projection.LotMutations! with
            {
                Mutations = projection.LotMutations.Mutations.Select(mutation => mutation with { ReportingTags = [] }).ToArray()
            }
        };

        var validate = () => OpenLotSuccessors.Validate(instruction with { Projection = malformed });

        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Fingerprint_BindsSuccessorVersionAndSecurityEvidence()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var target = OpenLotSuccessorTestData.Successor(source, 1m);
        var instruction = OpenLotSuccessorTestData.Build(source, [target]);
        var fingerprint = OpenLotSuccessors.Fingerprint(instruction);

        OpenLotSuccessors.Fingerprint(instruction with
        {
            Successors = [target with { ExpectedBookPositionVersion = target.ExpectedBookPositionVersion + 1 }]
        }).Should().NotBe(fingerprint);
        OpenLotSuccessors.Fingerprint(instruction with
        {
            Successors = [target with { ExpectedSecurityHash = Sha256Digest.ComputeUtf8("changed-target-security") }]
        }).Should().NotBe(fingerprint);
        OpenLotSuccessors.Fingerprint(instruction with
        {
            ExpectedSecurityHash = Sha256Digest.ComputeUtf8("changed-source-security")
        }).Should().NotBe(fingerprint);
    }

    [Fact]
    public void Mapper_RejectsExchangeWithoutCanonicalSuccessorInstruction()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var instruction = OpenLotSuccessorTestData.Build(source, [OpenLotSuccessorTestData.Successor(source, 1m)]);
        var request = OpenLotSuccessorTestData.MapRequest(instruction);

        var result = new CorporateActionAssetAccountingEventMapper().Map(request with { SuccessorInstruction = null });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(item => item.Code == "corporate-action.canonical-successors-required");
    }

    [Fact]
    public void Mapper_RejectsLossOfHistoricalSuccessorAcquisitionEvidence()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var instruction = OpenLotSuccessorTestData.Build(source, [OpenLotSuccessorTestData.Successor(source, 1m)]);
        var request = OpenLotSuccessorTestData.MapRequest(instruction);

        var result = new CorporateActionAssetAccountingEventMapper().Map(request with
        {
            RetainedEvidence = request.RetainedEvidence.Where(item => item.SubjectType != "OpenLotAcquisition").ToArray()
        });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(item => item.Code == "corporate-action.canonical-successors-invalid");
    }
}

internal static class OpenLotSuccessorTestData
{
    internal static readonly DateOnly EffectiveDate = new(2026, 8, 25);
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);

    internal static OpenLotSuccessorInstructionDto Build(
        OpenLotDto predecessor,
        IReadOnlyList<OpenLotSuccessorTargetDto> successors,
        bool advanceRefunding = false,
        long expectedPositionVersion = 1,
        long expectedSecurityVersion = 1,
        Guid? periodId = null,
        string? expectedSecurityHash = null,
        IReadOnlyList<decimal>? allocationPercents = null,
        string fundProfileId = "fund-alpha",
        long expectedPeriodVersion = 1,
        CorporateActionAccountingTypeDto? actionType = null,
        CorporateActionPolicyInputsDto? policyInputs = null,
        decimal? splitRatio = null,
        Guid? actionId = null,
        DateOnly? effectiveDate = null,
        bool identifierChanged = false,
        bool? canonicalLotTransferJournal = null,
        long sourceEventVersion = 1,
        CorporateActionSuccessorRoleDto exchangeRole = CorporateActionSuccessorRoleDto.Successor)
    {
        var sourceId = actionId ?? Guid.NewGuid();
        var actionDate = effectiveDate ?? EffectiveDate;
        var positionSnapshotId = Guid.NewGuid();
        var lotSnapshotId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var totalTargetQuantity = successors.Sum(item => item.Lot.OpenQuantity);
        var allocations = successors.Select((item, index) => new CorporateActionSuccessorAllocationDto(
            item.Lot.SecurityId,
            advanceRefunding
                ? index == 0 ? CorporateActionSuccessorRoleDto.Refunded : CorporateActionSuccessorRoleDto.Unrefunded
                : exchangeRole,
            item.Lot.OpenQuantity,
            advanceRefunding ? allocationPercents?[index] ?? item.Lot.OpenQuantity / totalTargetQuantity : null)).ToArray();
        var request = new CorporateActionAccountingProjectionRequest(
            sourceId, sourceEventVersion,
            actionType ?? (advanceRefunding ? CorporateActionAccountingTypeDto.AdvanceRefunding : CorporateActionAccountingTypeDto.RegS144AExchange),
            advanceRefunding ? AccountingBasisKindDto.Statutory : AccountingBasisKindDto.Gaap,
            predecessor.SecurityId, predecessor.BookPositionId, expectedPositionVersion, expectedPositionVersion,
            actionDate, EffectiveDate, ObservedAt,
            predecessor.Acquisition.FunctionalCurrency, "SecurityMaster", sourceId.ToString("D"), new string('a', 64),
            new CorporateActionEconomicsDto(PositionQuantity: predecessor.OpenQuantity, AffectedQuantity: predecessor.OpenQuantity,
                CarryingAmount: predecessor.OpenFunctionalCostBasis, Successors: allocations, SplitRatio: splitRatio,
                IdentifierChanged: identifierChanged),
            PolicyInputs: policyInputs,
            EvidenceManifest:
            [
                Dependency(CorporateActionProjectionEvidenceRoleDto.SourceEvent, sourceId, sourceEventVersion, "SecurityMasterCorporateAction", 'a'),
                Dependency(CorporateActionProjectionEvidenceRoleDto.PositionSnapshot, positionSnapshotId, expectedPositionVersion, "PositionSnapshot", 'b'),
                Dependency(CorporateActionProjectionEvidenceRoleDto.LotSnapshot, lotSnapshotId, predecessor.Version, "LotSnapshot", 'c'),
                Dependency(CorporateActionProjectionEvidenceRoleDto.PolicyDecision, policyId, 2, "CorporateActionPolicyDecision", 'd')
            ],
            CaseId: Guid.NewGuid(), CaseVersion: 3, PolicyDecisionVersion: 2, PositionSnapshotId: positionSnapshotId,
            AccountingScope: new CorporateActionAccountingProjectionScopeDto("tenant-alpha", "company-alpha", fundProfileId,
                predecessor.LedgerBookId, periodId ?? Guid.NewGuid(), expectedPeriodVersion, "US"),
            LotSnapshotId: lotSnapshotId, LotSnapshotVersion: predecessor.Version, PolicyDecisionId: policyId)
        {
            CanonicalLotTransferJournal = canonicalLotTransferJournal
                ?? (actionType is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit)
        };
        var service = new CorporateActionAccountingProjectionService();
        var intent = service.Project(request);
        intent.Status.Should().Be(CorporateActionProjectionStatusDto.Projected, "the reusable fixture must use a supported registered projection");
        var mutations = intent.LotMutations!.Mutations.Select((mutation, index) => mutation with
        {
            SourceLotId = predecessor.TaxLotRecordId,
            ExpectedSourceLotVersion = predecessor.Version,
            SourceBefore = new CorporateActionLotStateSnapshotDto(predecessor.OpenQuantity,
                predecessor.OpenFunctionalCostBasis, predecessor.OpenTransactionCostBasis),
            TargetLotId = successors[index].Lot.TaxLotRecordId,
            TargetOperation = CorporateActionLotTargetOperationDto.Create,
            TargetAfter = new CorporateActionLotStateSnapshotDto(successors[index].Lot.OpenQuantity,
                successors[index].Lot.OpenFunctionalCostBasis, successors[index].Lot.OpenTransactionCostBasis),
            BasisAmount = successors[index].Lot.OpenTransactionCostBasis,
            SourceBasisAmount = successors[index].Lot.OpenTransactionCostBasis
        }).ToArray();
        var projection = service.Project(request with { AuthoritativeLotMutations = mutations });
        projection.CanPreparePostingCandidate.Should().BeTrue("authoritative successor fixture must reconcile: {0}",
            string.Join("; ", projection.Blockers.Select(item => item.Message)));
        return new OpenLotSuccessorInstructionDto(projection, predecessor, expectedSecurityVersion, successors,
            expectedSecurityHash ?? Sha256Digest.ComputeUtf8("source-security"));
    }

    internal static OpenLotDto Predecessor()
    {
        var id = Guid.NewGuid();
        var acquired = new DateOnly(2025, 1, 1);
        return new OpenLotDto(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"predecessor-{id:D}", acquired,
            100m, 60m, 594m, 653.4m, 4,
            new OpenLotAcquisitionDto(LotQuantityBasis.Face, "EUR", "USD", 1.1m, 1000m, 1100m,
                new DateOnly(2024, 12, 15), new FaceValueAcquisitionTermsDto(100m, 1m, BondAmortizationMethod.StraightLine, null),
                [AcquisitionEvidence(id, acquired)]));
    }

    internal static OpenLotSuccessorTargetDto Successor(OpenLotDto predecessor, decimal fraction)
    {
        var id = Guid.NewGuid();
        var remainingFraction = predecessor.OpenQuantity / predecessor.OriginalQuantity;
        var acquisition = predecessor.Acquisition;
        var lot = new OpenLotDto(id, Guid.NewGuid(), Guid.NewGuid(), predecessor.LedgerBookId, $"successor-{id:D}",
            predecessor.AcquiredDate, predecessor.OpenQuantity * fraction, predecessor.OpenQuantity * fraction,
            OpenLotSuccessors.Allocate(predecessor.OpenTransactionCostBasis, fraction, acquisition.AcquisitionCurrency),
            OpenLotSuccessors.Allocate(predecessor.OpenFunctionalCostBasis, fraction, acquisition.FunctionalCurrency), 1,
            acquisition with
            {
                TransactionCostBasis = OpenLotSuccessors.Allocate(acquisition.TransactionCostBasis * remainingFraction, fraction, acquisition.AcquisitionCurrency),
                FunctionalCostBasis = OpenLotSuccessors.Allocate(acquisition.FunctionalCostBasis * remainingFraction, fraction, acquisition.FunctionalCurrency),
                Evidence = [.. acquisition.Evidence, AcquisitionEvidence(id, predecessor.AcquiredDate)]
            });
        return new OpenLotSuccessorTargetDto(lot, 1, 1, Sha256Digest.ComputeUtf8($"successor-security-{id:D}"));
    }

    internal static CorporateActionAssetAccountingEventMapRequest MapRequest(
        OpenLotSuccessorInstructionDto instruction, LedgerDimensionSetDto? dimensions = null)
    {
        var projection = instruction.Projection;
        var source = instruction.ExpectedLot;
        var scope = projection.AccountingScope!;
        var economicEvent = projection.EconomicEvent!;
        var lineage = projection.ProjectionLineage!;
        var eventScope = new AssetAccountingEventScopeDto(source.SecurityId, instruction.ExpectedSecurityVersion,
            source.BookPositionId, projection.LotMutations!.ExpectedPositionVersion, source.LedgerBookId,
            scope.PeriodId, projection.Treatment.AccountingBasis, scope.FundProfileId, scope.TenantId, scope.CompanyId,
            Dimensions: dimensions);
        var amount = projection.EventAmount;
        var reusesSourceScope = instruction.Successors.Any(target => target.Lot.SecurityId == source.SecurityId
            && target.Lot.BookPositionId == source.BookPositionId);
        dimensions ??= new LedgerDimensionSetDto(FundId: scope.FundProfileId, InstrumentId: source.SecurityId,
            BookId: source.LedgerBookId.ToString("D"), TaxLotId: reusesSourceScope ? source.LotId : null)
        { PositionId = source.BookPositionId };
        var debitLines = instruction.Successors.Select(target => new ProjectedAccountingEffectLineDto("Assets:Successor",
            target.Lot.OpenFunctionalCostBasis, 0m, source.Acquisition.FunctionalCurrency,
            Dimensions: dimensions with
            {
                InstrumentId = target.Lot.SecurityId,
                PositionId = target.Lot.BookPositionId,
                TaxLotId = reusesSourceScope ? target.Lot.LotId : dimensions.TaxLotId
            })).ToArray();
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion,
            economicEvent.EffectiveDate, amount, amount, source.Acquisition.FunctionalCurrency,
            [
                .. debitLines,
                new ProjectedAccountingEffectLineDto("Assets:Investment", 0m, amount, source.Acquisition.FunctionalCurrency,
                    Dimensions: dimensions with { TaxLotId = reusesSourceScope ? source.LotId : dimensions.TaxLotId })
            ]);
        var mapped = CorporateActionMappedAccountingEffectAttestor.Create(projection, eventScope, effect,
            new AccountingRulePackReferenceDto("pack-asset", "v7", "rule-valuation", "v2"),
            [
                new CorporateActionPostingComponentLineMappingDto(0, CorporateActionPostingComponentKindDto.CarryingValueRelief,
                    [new CorporateActionPostingComponentLineAllocationDto(debitLines.Length, amount)], "predecessor-relief"),
                new CorporateActionPostingComponentLineMappingDto(1, CorporateActionPostingComponentKindDto.PurchaseCost,
                    debitLines.Select((line, index) => new CorporateActionPostingComponentLineAllocationDto(index, line.Debit)).ToArray(), "successor-carryover")
            ]);
        var evidence = projection.EvidenceManifest.Select(item => new RetainedEvidenceIdentityDto(
            item.EvidenceId, item.EvidenceUri, item.ContentHashSha256, economicEvent.SourceDomain, item.EvidenceId,
            "Accepted", "controller", ObservedAt.AddHours(1), economicEvent.EffectiveDate, item.EvidenceVersion,
            ObservedAt.AddHours(1).AddMinutes(1), "evidence-vault", item.SubjectType, item.SubjectId))
            .Concat(source.Acquisition.Evidence)
            .Concat(instruction.Successors.SelectMany(item => item.Lot.Acquisition.Evidence))
            .DistinctBy(item => item.EvidenceId).ToList();
        evidence.Add(new RetainedEvidenceIdentityDto("successor-event", "evidence://successors/event",
            economicEvent.SourceContentHash!, economicEvent.SourceDomain, economicEvent.SourceEntityId!,
            "Accepted", "controller", ObservedAt.AddHours(1), economicEvent.EffectiveDate, economicEvent.EventVersion,
            ObservedAt.AddHours(1).AddMinutes(1), "evidence-vault", AssetAccountingEvidenceSubjects.Event,
            economicEvent.EventId.ToString("D")));
        return new CorporateActionAssetAccountingEventMapRequest(projection, eventScope, mapped, scope.ExpectedPeriodVersion,
            "projector", ObservedAt.AddHours(2), evidence, SuccessorInstruction: instruction);
    }

    private static CorporateActionProjectionEvidenceDependencyDto Dependency(
        CorporateActionProjectionEvidenceRoleDto role, Guid id, long version, string subjectType, char hash)
        => new(role, $"{role}", $"evidence://successors/{id:D}", new string(hash, 64), version, subjectType, id.ToString("D"));

    private static RetainedEvidenceIdentityDto AcquisitionEvidence(Guid lotId, DateOnly date)
        => new($"acquisition-{lotId:D}", $"evidence://acquisition/{lotId:D}", new string('e', 64), "Ledger",
            lotId.ToString("D"), "Accepted", "reviewer", ObservedAt, date, 1, ObservedAt, "retention-service",
            "OpenLotAcquisition", lotId.ToString("D"));
}
