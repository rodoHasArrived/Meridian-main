using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotCorporateActionTests
{
    private static readonly DateOnly AcquiredDate = new(2025, 1, 15);
    private static readonly DateOnly EffectiveDate = new(2026, 3, 1);

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 160)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 40)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock, 125)]
    public void StockTransformation_CarriesOnlyRemainingAcquisitionBasisAndPreservesCurrentBasis(
        CorporateActionAccountingTypeDto actionType, int quantity)
    {
        var instruction = Instruction(actionType);

        var projection = OpenLotCorporateAction.Project(instruction).Should().ContainSingle().Subject;

        projection.Successor.Quantity.Should().Be(quantity);
        projection.AcquisitionTransactionCostBasis.Should().Be(800m,
            "20 of the original 100 units have already been relieved");
        projection.AcquisitionFunctionalCostBasis.Should().Be(1000m);
        projection.OpenTransactionCostBasis.Should().Be(720m,
            "a prior basis adjustment must not be undone by the corporate action");
        projection.OpenFunctionalCostBasis.Should().Be(900m);
        projection.OpenFunctionalCostBasis.Should().Be(projection.OpenTransactionCostBasis * 1.25m);
        projection.AcquisitionFunctionalCostBasis.Should().Be(projection.AcquisitionTransactionCostBasis * 1.25m);
        projection.Successor.AcquisitionEvidence.EffectiveDate.Should().Be(AcquiredDate);
        instruction.ExpectedLot.Acquisition.HoldingPeriodStartDate.Should().Be(new DateOnly(2024, 12, 1));
        instruction.Mutations.Single().HoldingPeriodTreatment.Should().Be(CorporateActionHoldingPeriodTreatmentDto.CarryOver);
    }

    [Fact]
    public void AdvanceRefunding_ConservesFaceAndDistinctBasesAcrossRefundedAndUnrefundedSuccessors()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding);

        var projection = OpenLotCorporateAction.Project(instruction);

        projection.Select(p => p.Successor.Quantity).Should().Equal(20000m, 60000m);
        projection.Select(p => p.AcquisitionTransactionCostBasis).Should().Equal(22000m, 66000m);
        projection.Select(p => p.AcquisitionFunctionalCostBasis).Should().Equal(27500m, 82500m);
        projection.Select(p => p.OpenTransactionCostBasis).Should().Equal(21000m, 63000m);
        projection.Select(p => p.OpenFunctionalCostBasis).Should().Equal(26250m, 78750m);
        projection.Sum(p => p.Successor.Quantity).Should().Be(instruction.ExpectedLot.OpenQuantity);
        projection.Sum(p => p.OpenTransactionCostBasis).Should().Be(instruction.ExpectedLot.OpenTransactionCostBasis);
        projection.Sum(p => p.OpenFunctionalCostBasis).Should().Be(instruction.ExpectedLot.OpenFunctionalCostBasis);
        projection[0].Successor.Role.Should().Be(CorporateActionSuccessorRoleDto.Refunded);
        projection[0].Successor.ReportingTags.Should().Equal("ScheduleD");
        projection[1].Successor.Role.Should().Be(CorporateActionSuccessorRoleDto.Unrefunded);
        projection[1].Successor.ReportingTags.Should().BeEmpty();
    }

    [Fact]
    public void AdvanceRefunding_RoundedAllocationsRetainResidualWithoutLosingEitherCurrency()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding, 33.333333333333m);
        instruction = Replan(instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            {
                OpenTransactionCostBasis = 720m,
                OpenFunctionalCostBasis = 900m
            }
        });

        var projection = OpenLotCorporateAction.Project(instruction);

        projection.Select(p => p.OpenTransactionCostBasis).Should().Equal(240m, 480m);
        projection.Select(p => p.OpenFunctionalCostBasis).Should().Equal(300m, 600m);
        projection.Sum(p => p.AcquisitionTransactionCostBasis).Should().Be(88000m);
        projection.Sum(p => p.AcquisitionFunctionalCostBasis).Should().Be(110000m);
        foreach (var part in projection)
            Math.Abs(part.OpenFunctionalCostBasis - part.OpenTransactionCostBasis * 1.25m)
                .Should().BeLessThanOrEqualTo(0.000000000001m);
    }

    [Theory]
    [InlineData("source-identity")]
    [InlineData("source-security")]
    [InlineData("source-version")]
    [InlineData("source-before")]
    [InlineData("source-after")]
    [InlineData("source-quantity")]
    [InlineData("source-carrying")]
    [InlineData("source-basis")]
    [InlineData("target-identity")]
    [InlineData("target-security")]
    [InlineData("target-operation")]
    [InlineData("target-before")]
    [InlineData("target-version")]
    [InlineData("target-after")]
    [InlineData("quantity")]
    [InlineData("carrying")]
    [InlineData("basis")]
    [InlineData("allocation")]
    [InlineData("holding-period")]
    [InlineData("linked-case")]
    [InlineData("tags")]
    public void ReviewedMutation_RejectsChangedSourceTargetEconomicsOrHoldingPeriod(string field)
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        var original = instruction.Mutations.Single();
        var changed = field switch
        {
            "source-identity" => original with { SourceLotId = Guid.NewGuid() },
            "source-security" => original with { SecurityId = Guid.NewGuid() },
            "source-version" => original with { ExpectedSourceLotVersion = original.ExpectedSourceLotVersion + 1 },
            "source-before" => original with { SourceBefore = original.SourceBefore! with { BasisAmount = 721m } },
            "source-after" => original with { SourceAfter = new(1m, 0m, 0m) },
            "source-quantity" => original with { SourceQuantity = 79m },
            "source-carrying" => original with { SourceCarryingAmount = 899m },
            "source-basis" => original with { SourceBasisAmount = 719m },
            "target-identity" => original with { TargetLotId = Guid.NewGuid() },
            "target-security" => original with { TargetSecurityId = Guid.NewGuid() },
            "target-operation" => original with { TargetOperation = CorporateActionLotTargetOperationDto.Update },
            "target-before" => original with { TargetBefore = new(0m, 0m, 0m) },
            "target-version" => original with { ExpectedTargetLotVersion = 1 },
            "target-after" => original with { TargetAfter = original.TargetAfter! with { Quantity = 121m } },
            "quantity" => original with { Quantity = 121m },
            "carrying" => original with { CarryingAmount = 901m },
            "basis" => original with { BasisAmount = 721m },
            "allocation" => original with { AllocationPercent = 99m },
            "holding-period" => original with { HoldingPeriodTreatment = CorporateActionHoldingPeriodTreatmentDto.NewLot },
            "linked-case" => original with { LinkedCaseId = Guid.NewGuid() },
            "tags" => original with { ReportingTags = ["ScheduleD"] },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var project = () => OpenLotCorporateAction.Project(instruction with { Mutations = [changed] });

        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AdvanceRefunding_RejectsSwappedSourceAllocationsEvenWhenAggregateReliefBalances()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding);
        var first = instruction.Mutations[0];
        var second = instruction.Mutations[1];
        instruction = instruction with
        {
            Mutations =
            [
                first with { SourceQuantity = second.SourceQuantity, SourceCarryingAmount = second.SourceCarryingAmount, SourceBasisAmount = second.SourceBasisAmount },
                second with { SourceQuantity = first.SourceQuantity, SourceCarryingAmount = first.SourceCarryingAmount, SourceBasisAmount = first.SourceBasisAmount }
            ]
        };
        CorporateActionLotMutationPlanValidator.Validate(instruction.Mutations).Should().BeEmpty();

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecurityEvidence_RejectsChangedProjectionBytesForPredecessorAndSuccessor(bool successor)
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = successor
            ? instruction with { Successors = [instruction.Successors[0] with { Security = instruction.Successors[0].Security with { DisplayName = "Unreviewed change" } }] }
            : instruction with { Security = instruction.Security with { DisplayName = "Unreviewed change" } };

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*Security Master evidence*");
    }

    [Theory]
    [InlineData("security-subject")]
    [InlineData("security-version")]
    [InlineData("security-future-date")]
    [InlineData("security-review")]
    [InlineData("acquisition-subject")]
    [InlineData("acquisition-date")]
    [InlineData("acquisition-review")]
    public void SuccessorEvidence_RequiresReviewedExactLotAndSecurityBindings(string field)
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        var successor = instruction.Successors.Single();
        successor = field switch
        {
            "security-subject" => successor with { SecurityEvidence = successor.SecurityEvidence with { SubjectId = Guid.NewGuid().ToString("D") } },
            "security-version" => successor with { SecurityEvidence = successor.SecurityEvidence with { EvidenceVersion = successor.Security.Version + 1 } },
            "security-future-date" => successor with { SecurityEvidence = successor.SecurityEvidence with { EffectiveDate = EffectiveDate.AddDays(1) } },
            "security-review" => successor with { SecurityEvidence = successor.SecurityEvidence with { ReviewStatus = "Pending" } },
            "acquisition-subject" => successor with { AcquisitionEvidence = successor.AcquisitionEvidence with { SubjectId = instruction.ExpectedLot.TaxLotRecordId.ToString("D") } },
            "acquisition-date" => successor with { AcquisitionEvidence = successor.AcquisitionEvidence with { EffectiveDate = EffectiveDate } },
            "acquisition-review" => successor with { AcquisitionEvidence = successor.AcquisitionEvidence with { ReviewStatus = "Pending" } },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var project = () => OpenLotCorporateAction.Project(instruction with { Successors = [successor] });

        project.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("currency")]
    [InlineData("not-yet-effective")]
    [InlineData("expired")]
    public void SuccessorSecurity_RequiresActiveEffectiveSameCurrencyEvenWithMatchingHash(string field)
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        var successor = instruction.Successors.Single();
        var security = field switch
        {
            "inactive" => successor.Security with { Status = SecurityStatusDto.Inactive },
            "currency" => successor.Security with { Currency = "GBP" },
            "not-yet-effective" => successor.Security with { EffectiveFrom = At(EffectiveDate.AddDays(1)) },
            "expired" => successor.Security with { EffectiveTo = At(EffectiveDate.AddDays(-1)) },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        successor = successor with { Security = security, SecurityEvidence = SecurityEvidence(security) };

        var project = () => OpenLotCorporateAction.Project(instruction with { Successors = [successor] });

        project.Should().Throw<ArgumentException>().WithMessage("*Security Master evidence*");
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 80)]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 40)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 80)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 160)]
    public void Split_RejectsUnchangedOrOppositeDirectionQuantity(CorporateActionAccountingTypeDto actionType, int quantity)
    {
        var instruction = Instruction(actionType);
        instruction = Replan(instruction with { Successors = [instruction.Successors[0] with { Quantity = quantity }] });

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Split_RejectsFractionalCashInLieuWithoutInventingTreatment()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.ReverseStockSplit);
        instruction = Replan(instruction with { Successors = [instruction.Successors[0] with { Quantity = 40.5m }] });

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*cash-in-lieu*");
    }

    [Fact]
    public void Split_WithReviewedSecurityReclassificationBindsTheExactSuccessorSecurity()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.StockSplit);
        var security = Security(Guid.NewGuid(), false);
        instruction = Replan(instruction with
        {
            Successors = [instruction.Successors[0] with { Security = security, SecurityEvidence = SecurityEvidence(security) }]
        });

        var projection = OpenLotCorporateAction.Project(instruction).Single();

        projection.Successor.Security.SecurityId.Should().Be(security.SecurityId);
        projection.OpenFunctionalCostBasis.Should().Be(instruction.ExpectedLot.OpenFunctionalCostBasis);
    }

    [Fact]
    public void StockSplit_RejectsRefundedRole()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.StockSplit);
        instruction = instruction with { Successors = [instruction.Successors[0] with { Role = CorporateActionSuccessorRoleDto.Refunded }] };

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("missing-refunded-tag")]
    [InlineData("unrefunded-tag")]
    [InlineData("duplicate-role")]
    [InlineData("lost-face")]
    [InlineData("basis-allocation")]
    public void AdvanceRefunding_RejectsLostFaceInvalidAllocationOrScheduleDTracking(string field)
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding);
        var first = instruction.Successors[0];
        var second = instruction.Successors[1];
        var successors = field switch
        {
            "missing-refunded-tag" => new[] { first with { ReportingTags = [] }, second },
            "unrefunded-tag" => new[] { first, second with { ReportingTags = ["ScheduleD"] } },
            "duplicate-role" => new[] { first, second with { Role = CorporateActionSuccessorRoleDto.Refunded } },
            "lost-face" => new[] { first with { Quantity = first.Quantity - 1m }, second },
            "basis-allocation" => new[] { first with { BasisAllocationPercent = 20m }, second },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var project = () => OpenLotCorporateAction.Project(instruction with { Successors = successors });

        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Merger_AcceptsExactlyRepresentableTwelveDecimalQuantity()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = Replan(instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            {
                OriginalQuantity = 80m,
                Acquisition = instruction.ExpectedLot.Acquisition with
                { TransactionCostBasis = 4.938271560492m, FunctionalCostBasis = 6.172839450615m }
            },
            Successors = [instruction.Successors[0] with { Quantity = 1.234567890123m }]
        });

        OpenLotCorporateAction.Project(instruction).Single().Successor.Quantity.Should().Be(1.234567890123m);
    }

    [Fact]
    public void SingleSuccessor_NullAllocationMeansTheEntireReviewedBasis()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = instruction with { Mutations = [instruction.Mutations[0] with { AllocationPercent = null }] };

        var projection = OpenLotCorporateAction.Project(instruction).Single();

        projection.OpenTransactionCostBasis.Should().Be(720m);
        projection.OpenFunctionalCostBasis.Should().Be(900m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Projection_RefusesQuantityOrBasisThatStorageWouldSilentlyRound(bool basis)
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = Replan(basis
            ? instruction with { ExpectedLot = instruction.ExpectedLot with { OpenTransactionCostBasis = 720.0000000000001m, OpenFunctionalCostBasis = 900.000000000000125m } }
            : instruction with { Successors = [instruction.Successors[0] with { Quantity = 1.2345678901234m }] });

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Projection_RejectsCurrentBasisThatNoLongerReconcilesToRetainedFx()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = Replan(instruction with { ExpectedLot = instruction.ExpectedLot with { OpenFunctionalCostBasis = 901m } });

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*acquisition FX*");
    }

    [Fact]
    public void Projection_RejectsEffectiveDateBeforeOriginalAcquisition()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock)
            with
        { EffectiveDate = AcquiredDate.AddDays(-1) };

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*effective date*");
    }

    [Fact]
    public void Evidence_RetainsBothAcquisitionDatesAndSecurityVersionsWithoutDuplicatingSameSecurity()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.StockSplit);

        var evidence = OpenLotCorporateAction.Evidence(instruction);

        evidence.Should().HaveCount(3);
        evidence.Should().Contain(instruction.ExpectedLot.Acquisition.Evidence[0]);
        evidence.Should().Contain(instruction.SecurityEvidence);
        evidence.Should().Contain(instruction.Successors[0].AcquisitionEvidence);
        evidence.Where(e => e.SubjectType == "OpenLotAcquisition").Should().OnlyContain(e => e.EffectiveDate == AcquiredDate);
    }

    [Fact]
    public void Batch_TransfersEveryPredecessorWithoutPoolingAcquisitionFactsOrEqualAmountSuccessors()
    {
        var instruction = AddPredecessor(Instruction(CorporateActionAccountingTypeDto.MergerStock));

        var groups = OpenLotCorporateAction.Groups(instruction);
        var projection = OpenLotCorporateAction.Project(instruction);

        groups.Should().HaveCount(2);
        projection.Select(target => target.PredecessorTaxLotRecordId)
            .Should().Equal(groups.Select(group => group.ExpectedLot.TaxLotRecordId));
        projection.Select(target => target.OpenFunctionalCostBasis).Should().Equal(900m, 900m);
        projection.Select(target => target.AcquisitionFunctionalCostBasis).Should().Equal(1000m, 1000m);
        projection.Select(target => target.Successor.TaxLotRecordId).Distinct().Should().HaveCount(2);
        projection[1].Successor.AcquisitionEvidence.EffectiveDate.Should().Be(AcquiredDate.AddMonths(1));
        groups[1].ExpectedLot.Acquisition.HoldingPeriodStartDate.Should().Be(new DateOnly(2024, 11, 1));
        OpenLotCorporateAction.Evidence(instruction).Should().Contain(groups[1].ExpectedLot.Acquisition.Evidence[0]);
        var lotInstruction = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CorporateAction,
            AssetAccountId: "Investments", CorporateAction: instruction);
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.CorporateAction, lotInstruction,
            1800m, EffectiveDate, OpenLotCorporateAction.Evidence(instruction)).Should().BeEmpty();
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.CorporateAction, lotInstruction,
            900m, EffectiveDate, OpenLotCorporateAction.Evidence(instruction)).Should().NotBeEmpty();
    }

    [Fact]
    public void AdvanceRefundingBatch_CreatesDistinctRefundedAndUnrefundedLotsForEachAcquisition()
    {
        var instruction = AddPredecessor(Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding));

        var projection = OpenLotCorporateAction.Project(instruction);

        projection.Should().HaveCount(4);
        projection.Select(target => target.Successor.TaxLotRecordId).Distinct().Should().HaveCount(4);
        projection.Select(target => target.Successor.BookPositionId).Distinct().Should().HaveCount(2);
        projection.Sum(target => target.Successor.Quantity).Should().Be(160000m);
        projection.Sum(target => target.AcquisitionFunctionalCostBasis).Should().Be(220000m);
        projection.Sum(target => target.OpenFunctionalCostBasis).Should().Be(210000m);
        foreach (var group in OpenLotCorporateAction.Groups(instruction))
        {
            var targets = projection.Where(target => target.PredecessorTaxLotRecordId == group.ExpectedLot.TaxLotRecordId).ToArray();
            targets.Select(target => target.Successor.Role).Should().Equal(CorporateActionSuccessorRoleDto.Refunded, CorporateActionSuccessorRoleDto.Unrefunded);
            targets[0].Successor.ReportingTags.Should().Equal("ScheduleD");
            targets[1].Successor.ReportingTags.Should().BeEmpty();
            targets.Should().OnlyContain(target => target.Successor.AcquisitionEvidence.EffectiveDate == group.ExpectedLot.AcquiredDate);
        }
    }

    [Theory]
    [InlineData("book")]
    [InlineData("security")]
    [InlineData("position")]
    [InlineData("predecessor")]
    [InlineData("successor")]
    [InlineData("successor-lot-id")]
    public void Batch_RejectsScopeDriftAndReusedLotIdentities(string changed)
    {
        var instruction = AddPredecessor(Instruction(CorporateActionAccountingTypeDto.MergerStock));
        var additional = instruction.AdditionalPredecessors![0];
        var lot = additional.ExpectedLot;
        additional = changed switch
        {
            "book" => additional with { ExpectedLot = lot with { LedgerBookId = Guid.NewGuid() } },
            "security" => additional with { ExpectedLot = lot with { SecurityId = Guid.NewGuid() } },
            "position" => additional with { ExpectedLot = lot with { BookPositionId = Guid.NewGuid() } },
            "predecessor" => additional with { ExpectedLot = instruction.ExpectedLot },
            "successor" => additional with { Successors = instruction.Successors },
            "successor-lot-id" => additional with { Successors = [additional.Successors[0] with { LotId = instruction.Successors[0].LotId }] },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };

        var project = () => OpenLotCorporateAction.Project(instruction with { AdditionalPredecessors = [additional] });

        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SinglePredecessor_PreservesLegacyJsonAndFingerprintWhenAdditionalPredecessorsAreAbsent()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);

        JsonSerializer.SerializeToElement(instruction).TryGetProperty("AdditionalPredecessors", out _).Should().BeFalse();
        var roundTrip = JsonSerializer.Deserialize<OpenLotCorporateActionInstructionDto>(JsonSerializer.Serialize(instruction))!;

        roundTrip.AdditionalPredecessors.Should().BeNull();
        OpenLotCorporateAction.Fingerprint(roundTrip).Should().Be(OpenLotCorporateAction.Fingerprint(instruction));
        OpenLotCorporateAction.Groups(roundTrip).Should().ContainSingle();
    }

    [Fact]
    public void AdvanceRefunding_NormalizesCurrentJournalAllocationsToTenDecimalsWithAnExactResidual()
    {
        var instruction = SameCurrency(Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding, 33.333333333333m));

        var projected = OpenLotCorporateAction.Project(instruction);

        projected.Select(target => target.OpenFunctionalCostBasis).Should().Equal(34999.9999999996m, 70000.0000000004m);
        projected.Sum(target => target.OpenFunctionalCostBasis).Should().Be(105000m);
        projected.Select(target => target.OpenTransactionCostBasis)
            .Should().Equal(projected.Select(target => target.OpenFunctionalCostBasis));
        projected.Should().OnlyContain(target => decimal.Round(target.OpenFunctionalCostBasis, 10) == target.OpenFunctionalCostBasis);

        var oldPrecisionPlan = Replan(instruction, journalDecimals: 12);
        var projectUnnormalized = () => OpenLotCorporateAction.Project(oldPrecisionPlan);
        projectUnnormalized.Should().Throw<ArgumentException>().WithMessage("*reviewed corporate-action plan*");
    }

    [Theory]
    [InlineData("transaction")]
    [InlineData("functional")]
    [InlineData("fx")]
    public void Projection_RejectsJournalOverprecisionBeforeReview(string field)
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        var lot = instruction.ExpectedLot;
        lot = field switch
        {
            "transaction" => lot with { OpenTransactionCostBasis = 720.00000000001m },
            "functional" => lot with { OpenFunctionalCostBasis = 900.00000000001m },
            "fx" => lot with { Acquisition = lot.Acquisition with { AcquisitionFxRateToFunctional = 1.25000000001m } },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var project = () => OpenLotCorporateAction.Project(instruction with { ExpectedLot = lot });

        project.Should().Throw<ArgumentException>().WithMessage("*ten journal decimals*");
    }

    [Fact]
    public void Projection_RejectsOriginalAcquisitionFxMismatchEvenWhenCurrentBasisReconciles()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            { Acquisition = instruction.ExpectedLot.Acquisition with { FunctionalCostBasis = 1250.001m } }
        };
        OpenLotValidation.Validate(instruction.ExpectedLot);

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*Original and current predecessor bases*");
    }

    [Fact]
    public void Projection_RejectsOriginalAllocationFxDriftEvenWhenCurrentJournalAllocationsReconcile()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.AdvanceRefunding, 33.333333333333m);
        instruction = Replan(instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            {
                OriginalQuantity = 80000m,
                OpenTransactionCostBasis = 0.3m,
                OpenFunctionalCostBasis = 300m,
                Acquisition = instruction.ExpectedLot.Acquisition with
                { TransactionCostBasis = 1m, FunctionalCostBasis = 1000m, AcquisitionFxRateToFunctional = 1000m }
            }
        });
        instruction.Mutations[0].SourceBasisAmount.Should().Be(0.1m);
        instruction.Mutations[0].SourceCarryingAmount.Should().Be(100m);

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*conserve retained acquisition FX*");
    }

    [Fact]
    public void Projection_RejectsNonRepresentableSuccessorUnitCostBeforeApproval()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = Replan(instruction with { Successors = [instruction.Successors[0] with { Quantity = 120m }] });

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*unit cost*twelve decimals*");
    }

    [Theory]
    [InlineData("fx")]
    [InlineData("proportional-acquisition")]
    public void Projection_ReturnsArgumentExceptionForArithmeticOverflow(string field)
    {
        var instruction = SameCurrency(Instruction(CorporateActionAccountingTypeDto.MergerStock));
        var acquisition = instruction.ExpectedLot.Acquisition with
        { TransactionCostBasis = decimal.MaxValue, FunctionalCostBasis = decimal.MaxValue };
        if (field == "fx")
            acquisition = acquisition with { AcquisitionCurrency = "EUR", AcquisitionFxRateToFunctional = 2m };
        instruction = instruction with { ExpectedLot = instruction.ExpectedLot with { Acquisition = acquisition } };

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithInnerException<OverflowException>();
    }

    [Fact]
    public void Batch_ConservesLargeTotalsWhenEachIndividualReceiptFitsItsDurableRange()
    {
        var instruction = SameCurrency(Instruction(CorporateActionAccountingTypeDto.MergerStock));
        instruction = AddPredecessor(Replan(instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            {
                OpenTransactionCostBasis = 90000000000000000000000000m,
                OpenFunctionalCostBasis = 90000000000000000000000000m
            }
        }));

        var projected = OpenLotCorporateAction.Project(instruction);

        projected.Sum(target => target.OpenFunctionalCostBasis).Should().Be(180000000000000000000000000m);
        projected.Sum(target => target.OpenTransactionCostBasis).Should().Be(180000000000000000000000000m);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("unit-cost")]
    [InlineData("receipt-cost-basis")]
    public void Projection_RejectsAmountsOutsideTheSqlNumericRangeBeforeReview(string field)
    {
        const decimal outsideLotRange = 100000000000000000000000000m;
        var instruction = SameCurrency(Instruction(CorporateActionAccountingTypeDto.MergerStock));
        instruction = instruction with
        {
            ExpectedLot = instruction.ExpectedLot with
            {
                OriginalQuantity = 2m,
                OpenQuantity = 2m,
                Acquisition = instruction.ExpectedLot.Acquisition with
                { TransactionCostBasis = outsideLotRange, FunctionalCostBasis = outsideLotRange }
            },
            Successors = [instruction.Successors[0] with { Quantity = field == "quantity" ? outsideLotRange : 1m }]
        };
        if (field == "receipt-cost-basis")
            instruction = instruction with { ExpectedLot = instruction.ExpectedLot with { OpenTransactionCostBasis = outsideLotRange, OpenFunctionalCostBasis = outsideLotRange } };
        instruction = Replan(instruction);

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Projection_RejectsPredecessorVersionThatCannotAdvance()
    {
        var instruction = Instruction(CorporateActionAccountingTypeDto.MergerStock);
        instruction = Replan(instruction with { ExpectedLot = instruction.ExpectedLot with { Version = long.MaxValue } });

        var project = () => OpenLotCorporateAction.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*versioned positive open lot*");
    }

    internal static OpenLotCorporateActionInstructionDto Instruction(CorporateActionAccountingTypeDto actionType,
        decimal refundedPercent = 25m)
    {
        var face = actionType == CorporateActionAccountingTypeDto.AdvanceRefunding;
        var security = Security(Guid.NewGuid(), face);
        var lotId = Guid.NewGuid();
        var lot = new OpenLotDto(lotId, security.SecurityId, Guid.NewGuid(), Guid.NewGuid(), "original-lot", AcquiredDate,
            face ? 100000m : 100m, face ? 80000m : 80m, face ? 84000m : 720m, face ? 105000m : 900m, 4,
            new(face ? LotQuantityBasis.Face : LotQuantityBasis.Units, "EUR", "USD", 1.25m,
                face ? 110000m : 1000m, face ? 137500m : 1250m, new DateOnly(2024, 12, 1),
                face ? new(100m, 1m, BondAmortizationMethod.StraightLine, null) : null,
                [Evidence("OpenLotAcquisition", lotId, AcquiredDate)]));
        var split = actionType is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit;
        var successors = face
            ? new[]
            {
                Successor(Security(Guid.NewGuid(), true), 80000m * refundedPercent / 100m, refundedPercent, CorporateActionSuccessorRoleDto.Refunded),
                Successor(Security(Guid.NewGuid(), true), 80000m * (100m - refundedPercent) / 100m, 100m - refundedPercent, CorporateActionSuccessorRoleDto.Unrefunded)
            }
            : new[]
            {
                Successor(split ? security : Security(Guid.NewGuid(), false),
                    actionType == CorporateActionAccountingTypeDto.StockSplit ? 160m :
                    actionType == CorporateActionAccountingTypeDto.ReverseStockSplit ? 40m : 125m,
                    100m, CorporateActionSuccessorRoleDto.Successor)
            };
        return Replan(new(Guid.NewGuid(), actionType, EffectiveDate, lot, security, SecurityEvidence(security), 3, successors, [], "Investments"));
    }

    private static OpenLotCorporateActionInstructionDto Replan(OpenLotCorporateActionInstructionDto instruction, int journalDecimals = 10)
    {
        var mutations = new List<CorporateActionLotMutationDto>();
        foreach (var group in OpenLotCorporateAction.Groups(instruction))
        {
            var lot = group.ExpectedLot;
            decimal transactionAllocated = 0m, functionalAllocated = 0m, quantityAllocated = 0m;
            for (var index = 0; index < group.Successors.Count; index++)
            {
                var successor = group.Successors[index];
                var last = index == group.Successors.Count - 1;
                var fraction = successor.BasisAllocationPercent / 100m;
                var transaction = last ? lot.OpenTransactionCostBasis - transactionAllocated : decimal.Round(lot.OpenTransactionCostBasis * fraction, journalDecimals, MidpointRounding.ToEven);
                var functional = last ? lot.OpenFunctionalCostBasis - functionalAllocated : decimal.Round(lot.OpenFunctionalCostBasis * fraction, journalDecimals, MidpointRounding.ToEven);
                var sourceQuantity = last ? lot.OpenQuantity - quantityAllocated : lot.OpenQuantity * fraction;
                mutations.Add(new(CorporateActionLotMutationKindDto.CarryOver, lot.SecurityId, successor.Security.SecurityId,
                    successor.Quantity, functional, fraction, CorporateActionHoldingPeriodTreatmentDto.CarryOver,
                    ReportingTags: successor.ReportingTags, SourceLotId: lot.TaxLotRecordId, ExpectedSourceLotVersion: lot.Version,
                    SourceBefore: new(lot.OpenQuantity, lot.OpenFunctionalCostBasis, lot.OpenTransactionCostBasis), SourceAfter: new(0m, 0m, 0m),
                    TargetLotId: successor.TaxLotRecordId, TargetOperation: CorporateActionLotTargetOperationDto.Create,
                    TargetAfter: new(successor.Quantity, functional, transaction), BasisAmount: transaction,
                    SourceQuantity: sourceQuantity, SourceCarryingAmount: functional, SourceBasisAmount: transaction));
                transactionAllocated += transaction;
                functionalAllocated += functional;
                quantityAllocated += sourceQuantity;
            }
        }
        return instruction with { Mutations = mutations };
    }

    internal static OpenLotCorporateActionInstructionDto AddPredecessor(OpenLotCorporateActionInstructionDto instruction)
    {
        var lotId = Guid.NewGuid();
        var acquired = AcquiredDate.AddMonths(1);
        var lot = instruction.ExpectedLot with
        {
            TaxLotRecordId = lotId,
            LotId = "additional-predecessor",
            AcquiredDate = acquired,
            Acquisition = instruction.ExpectedLot.Acquisition with
            {
                HoldingPeriodStartDate = new DateOnly(2024, 11, 1),
                Evidence = [Evidence("OpenLotAcquisition", lotId, acquired)]
            }
        };
        var successors = instruction.Successors.Select(successor =>
        {
            var id = Guid.NewGuid();
            return successor with
            {
                TaxLotRecordId = id,
                LotId = "additional-" + id,
                AcquisitionEvidence = Evidence("OpenLotAcquisition", id, acquired)
            };
        }).ToArray();
        return Replan(instruction with { AdditionalPredecessors = [new(lot, successors, instruction.SourceAssetAccountId)] });
    }

    private static OpenLotCorporateActionInstructionDto SameCurrency(OpenLotCorporateActionInstructionDto instruction)
    {
        var security = instruction.Security with { Currency = "USD" };
        return Replan(instruction with
        {
            Security = security,
            SecurityEvidence = SecurityEvidence(security),
            ExpectedLot = instruction.ExpectedLot with
            {
                OpenTransactionCostBasis = instruction.ExpectedLot.OpenFunctionalCostBasis,
                Acquisition = instruction.ExpectedLot.Acquisition with
                {
                    AcquisitionCurrency = "USD",
                    AcquisitionFxRateToFunctional = 1m,
                    TransactionCostBasis = instruction.ExpectedLot.Acquisition.FunctionalCostBasis
                }
            },
            Successors = instruction.Successors.Select(successor =>
            {
                var targetSecurity = successor.Security with { Currency = "USD" };
                return successor with { Security = targetSecurity, SecurityEvidence = SecurityEvidence(targetSecurity) };
            }).ToArray()
        });
    }

    private static OpenLotCorporateActionSuccessorDto Successor(SecurityProjectionRecord security, decimal quantity,
        decimal allocation, CorporateActionSuccessorRoleDto role)
    {
        var id = Guid.NewGuid();
        return new(id, "successor-" + id, "Investments", security, SecurityEvidence(security), Guid.NewGuid(), 2,
            quantity, allocation, role, role == CorporateActionSuccessorRoleDto.Refunded ? ["ScheduleD"] : [],
            Evidence("OpenLotAcquisition", id, AcquiredDate));
    }

    private static SecurityProjectionRecord Security(Guid id, bool face)
    {
        var empty = JsonSerializer.SerializeToElement(new { });
        return new(id, face ? "Bond" : "Equity", SecurityStatusDto.Active, "Reviewed security", "EUR", "ISIN",
            "TEST" + id.ToString("N"), empty, empty, empty, 2, At(new DateOnly(2025, 1, 1)), null, [], []);
    }

    private static RetainedEvidenceIdentityDto SecurityEvidence(SecurityProjectionRecord security)
        => Evidence("SecurityMasterProjection", security.SecurityId, AcquiredDate) with
        {
            EvidenceVersion = security.Version,
            ContentHashSha256 = OpenLotAmortization.SecurityHash(security)
        };

    private static RetainedEvidenceIdentityDto Evidence(string subject, Guid id, DateOnly effective)
        => new(subject + ":" + id, "evidence://reviewed/" + id, new string('a', 64), "custodian", "retained-source",
            "Accepted", "reviewer", At(effective), effective, 1, At(effective), "custodian", subject, id.ToString("D"));

    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
