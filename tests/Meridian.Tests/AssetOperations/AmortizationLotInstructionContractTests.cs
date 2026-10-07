using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class AmortizationLotInstructionContractTests
{
    [Fact]
    public void CompleteReviewedAmortizationInstruction_Passes()
    {
        var inputs = Inputs();
        Validate(inputs).Should().BeEmpty();
    }

    [Fact]
    public void ReviewedInstruction_RequiresExactDateAmountAndRetainedEvidence()
    {
        var inputs = Inputs();
        var instruction = Instruction(inputs);
        var amount = Math.Abs(OpenLotAmortization.Project(inputs).FunctionalMovement);
        var evidence = inputs.ExpectedLot.Acquisition.Evidence.Append(inputs.SecurityEvidence).ToArray();
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.DepreciationAmortization,
            instruction, amount + 1m, inputs.AsOfDate, evidence).Should().Contain(issue => issue.Contains("event amount"));
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.DepreciationAmortization,
            instruction, amount, inputs.AsOfDate.AddDays(1), evidence).Should().Contain(issue => issue.Contains("effective date"));
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.DepreciationAmortization,
            instruction, amount, inputs.AsOfDate, inputs.ExpectedLot.Acquisition.Evidence)
            .Should().Contain(issue => issue.Contains("complete Security Master"));
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.DepreciationAmortization,
            instruction, amount, inputs.AsOfDate, [inputs.SecurityEvidence])
            .Should().Contain(issue => issue.Contains("acquisition evidence"));
    }

    [Fact]
    public void NullReferenceInputs_ReturnValidationIssues()
    {
        var inputs = Inputs();
        foreach (var missing in new[]
                 {
                     inputs with { ExpectedLot = null! }, inputs with { Security = null! },
                     inputs with { SecurityEvidence = null! }
                 })
            Validate(missing).Should().NotBeEmpty();
    }

    [Fact]
    public void MissingAcquisitionTerms_ReturnValidationIssues()
    {
        var inputs = Inputs();
        var lot = inputs.ExpectedLot;
        Validate(inputs with { ExpectedLot = lot with { Acquisition = lot.Acquisition with { FaceValueTerms = null } } })
            .Should().Contain(issue => issue.Contains("Face lots require"));
    }

    [Fact]
    public void ExtremeYield_ReturnsAnExplicitRefusalInsteadOfArithmeticFailure()
    {
        var inputs = Inputs();
        var acquisition = inputs.ExpectedLot.Acquisition;
        inputs = inputs with
        {
            ExpectedLot = inputs.ExpectedLot with
            {
                Acquisition = acquisition with
                {
                    FaceValueTerms = acquisition.FaceValueTerms! with
                    { AmortizationMethod = BondAmortizationMethod.ConstantYield, EffectiveYield = decimal.MaxValue }
                }
            }
        };
        Validate(inputs).Should().Contain(issue => issue.Contains("decimal calculation bounds"));
    }

    [Theory]
    [InlineData("couponType", "\"Floating\"")]
    [InlineData("couponType", "\"Step\"")]
    [InlineData("couponType", "\"InflationLinked\"")]
    [InlineData("isCallable", "true")]
    [InlineData("isCallable", "\"unknown\"")]
    [InlineData("currentFactor", "\"unknown\"")]
    [InlineData("floatingIndex", "\"SOFR\"")]
    [InlineData("callDate", "\"2026-07-01\"")]
    [InlineData("preRefundDate", "\"2026-07-01\"")]
    [InlineData("mandatoryPutDate", "\"2026-07-01\"")]
    [InlineData("principalSchedule", "[{\"paymentDate\":\"invalid\",\"amount\":0}]")]
    [InlineData("stepSchedule", "[{\"effectiveDate\":\"invalid\",\"rate\":-1}]")]
    [InlineData("factorScheduleEntries", "[{\"factor\":-1}]")]
    [InlineData("factorScheduleEntries", "{}")]
    [InlineData("stepSchedule", "\"unresolved\"")]
    [InlineData("legs", "[{}]")]
    public void UnsupportedOrMalformedReferenceTerms_BlockPosting(string name, string json)
    {
        var inputs = WithTerms(Inputs(), terms => terms[name] = JsonDocument.Parse(json).RootElement.Clone());
        Validate(inputs).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("couponType")]
    [InlineData("couponRate")]
    [InlineData("isCallable")]
    public void MissingBondCouponAuthority_BlockPosting(string name)
        => Validate(WithTerms(Inputs(), terms => terms.Remove(name))).Should().NotBeEmpty();

    [Fact]
    public void ForeignAssetClass_BlockPosting()
    {
        var inputs = Inputs();
        var security = inputs.Security with { AssetClass = "Equity" };
        inputs = inputs with
        {
            Security = security,
            SecurityEvidence = inputs.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(security) }
        };
        Validate(inputs).Should().Contain(issue => issue.Contains("supports only"));
    }

    [Fact]
    public void EmptyCodecSchedules_DoNotInventUnsupportedCashFlows()
    {
        var inputs = WithTerms(Inputs(), terms =>
        {
            terms["principalSchedule"] = JsonSerializer.SerializeToElement(Array.Empty<object>());
            terms["stepSchedule"] = JsonSerializer.SerializeToElement(Array.Empty<object>());
        });
        Validate(inputs).Should().BeEmpty();
    }

    [Fact]
    public void MalformedGovernedProfile_DoesNotFallBackToCommonTerms()
    {
        var inputs = Inputs();
        var security = inputs.Security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(new { profileFields = new[] { "invalid" } }) };
        inputs = inputs with
        {
            Security = security,
            SecurityEvidence = inputs.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(security) }
        };
        Validate(inputs).Should().Contain(issue => issue.Contains("profile-field authority"));
    }

    [Fact]
    public void SecurityEvidenceHash_SurvivesJsonObjectPropertyReorderingAndBindsValues()
    {
        var inputs = Inputs();
        var terms = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputs.Security.CommonTerms)!;
        var reordered = inputs.Security with
        {
            CommonTerms = JsonSerializer.SerializeToElement(terms.Reverse().ToDictionary(item => item.Key, item => item.Value))
        };
        OpenLotAmortization.SecurityHash(reordered).Should().Be(inputs.SecurityEvidence.ContentHashSha256);
        Validate(inputs with { Security = reordered }).Should().BeEmpty();
        reordered = reordered with { Version = reordered.Version + 1 };
        OpenLotAmortization.SecurityHash(reordered).Should().NotBe(inputs.SecurityEvidence.ContentHashSha256);
        Validate(inputs with { Security = reordered }).Should().NotBeEmpty();
    }

    [Fact]
    public void LegacyInstructionFingerprint_RemainsByteCompatibleWhenAmortizationIsAbsent()
    {
        // Independent SHA-256 fixture for the pre-amortization Web JSON payload. The
        // explicitly declared DisposalSelections property follows the generated record properties.
        AssetLotMutationInstructionValidator.Fingerprint(new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.None))
            .Should().Be("a81fa5e2e9962c93185204340b8bff6f70a9caf15a2c528b41868722436cb1d9");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Reversal_RestoresRetainedPriorBasisWithTheExactInverseMovement(bool premium, bool previouslyAdjusted)
    {
        var original = AtomicTaxLotJournalStoreTests.AmortPureInstruction(premium ? 110m : 90m, 10m,
            BondAmortizationMethod.StraightLine, null);
        if (previouslyAdjusted)
        {
            var basis = premium ? 107m : 93m;
            original = original with
            {
                ExpectedLot = original.ExpectedLot with
                { OpenTransactionCostBasis = basis, OpenFunctionalCostBasis = basis * 1.1m }
            };
        }
        var forward = OpenLotAmortization.Project(original);
        var reversal = ReversalInputs(original);

        var inverse = OpenLotAmortization.Project(reversal);

        inverse.TransactionCostBasis.Should().Be(original.ExpectedLot.OpenTransactionCostBasis);
        inverse.FunctionalCostBasis.Should().Be(original.ExpectedLot.OpenFunctionalCostBasis);
        inverse.TransactionMovement.Should().Be(-forward.TransactionMovement);
        inverse.FunctionalMovement.Should().Be(-forward.FunctionalMovement);
        ValidateInstruction(CorrectionInstruction(reversal)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("acquisition")]
    [InlineData("position")]
    [InlineData("version")]
    [InlineData("batch")]
    [InlineData("journal")]
    public void Reversal_RejectsChangedImmutableFactsAndInvalidWitnesses(string defect)
    {
        var inputs = ReversalInputs(Inputs());
        var reversal = inputs.Reversal!;
        var before = reversal.RestoresLot;
        reversal = defect switch
        {
            "quantity" => reversal with { RestoresLot = before with { OpenQuantity = before.OpenQuantity - 1m } },
            "acquisition" => reversal with
            {
                RestoresLot = before with
                { Acquisition = before.Acquisition with { HoldingPeriodStartDate = before.AcquiredDate.AddDays(-1) } }
            },
            "position" => reversal with { RestoresLot = before with { BookPositionId = Guid.NewGuid() } },
            "version" => reversal with { RestoresLot = before with { Version = inputs.ExpectedLot.Version } },
            "batch" => reversal with { MutationBatchId = Guid.Empty },
            "journal" => reversal with { JournalEntryId = Guid.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        var project = () => OpenLotAmortization.Project(inputs with { Reversal = reversal });

        project.Should().Throw<ArgumentException>().WithMessage("*original lot identity, quantity and acquisition facts*");
    }

    [Theory]
    [InlineData("missing-all")]
    [InlineData("missing-batch")]
    [InlineData("missing-journal")]
    [InlineData("missing-approval")]
    [InlineData("wrong-batch")]
    [InlineData("wrong-journal")]
    [InlineData("empty-batch")]
    [InlineData("empty-journal")]
    [InlineData("unapproved")]
    [InlineData("no-approver")]
    [InlineData("no-reason")]
    [InlineData("non-utc")]
    public void Reversal_RejectsIncompleteOrDifferentCorrectionLineageBeforeApproval(string defect)
    {
        var instruction = CorrectionInstruction(ReversalInputs(Inputs()));
        var approval = instruction.CorrectionApproval!;
        instruction = defect switch
        {
            "missing-all" => instruction with { CorrectsMutationBatchId = null, CorrectsJournalEntryId = null, CorrectionApproval = null },
            "missing-batch" => instruction with { CorrectsMutationBatchId = null },
            "missing-journal" => instruction with { CorrectsJournalEntryId = null },
            "missing-approval" => instruction with { CorrectionApproval = null },
            "wrong-batch" => instruction with { CorrectsMutationBatchId = Guid.NewGuid() },
            "wrong-journal" => instruction with { CorrectsJournalEntryId = Guid.NewGuid() },
            "empty-batch" => instruction with { CorrectsMutationBatchId = Guid.Empty },
            "empty-journal" => instruction with { CorrectsJournalEntryId = Guid.Empty },
            "unapproved" => instruction with { CorrectionApproval = approval with { Status = LedgerAdjustmentApprovalStatusDto.Pending } },
            "no-approver" => instruction with { CorrectionApproval = approval with { ApprovedBy = "" } },
            "no-reason" => instruction with { CorrectionApproval = approval with { ReasonCode = "" } },
            "non-utc" => instruction with { CorrectionApproval = approval with { ApprovedAt = approval.ApprovedAt.ToOffset(TimeSpan.FromHours(1)) } },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };

        ValidateInstruction(instruction).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Amortization_RejectsDisposalSalePriceBeforeApproval(bool reversal)
    {
        var instruction = reversal ? CorrectionInstruction(ReversalInputs(Inputs())) : Instruction(Inputs());

        ValidateInstruction(instruction with { DisposalSalePrice = 0m }).Should()
            .Contain(issue => issue.Contains("complete Amortize instruction"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAmortization_NullReversalPreservesThePriorSerializedShape(bool legacy)
    {
        var inputs = Inputs() with { CalculationVersion = legacy ? null : OpenLotAmortization.ModelVersion };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var priorShape = new
        {
            inputs.ExpectedLot,
            inputs.Security,
            inputs.SecurityEvidence,
            inputs.ExpectedBookPositionVersion,
            inputs.AsOfDate,
            inputs.CalculationVersion
        };

        JsonSerializer.Serialize(inputs, options).Should().Be(JsonSerializer.Serialize(priorShape, options));
    }

    private static OpenLotAmortizationInstructionDto ReversalInputs(OpenLotAmortizationInstructionDto original)
    {
        var projected = OpenLotAmortization.Project(original);
        return original with
        {
            ExpectedLot = original.ExpectedLot with
            {
                Version = original.ExpectedLot.Version + 1,
                OpenTransactionCostBasis = projected.TransactionCostBasis,
                OpenFunctionalCostBasis = projected.FunctionalCostBasis
            },
            Reversal = new(Guid.NewGuid(), Guid.NewGuid(), original.ExpectedLot)
        };
    }

    private static AssetLotMutationInstructionDto CorrectionInstruction(OpenLotAmortizationInstructionDto inputs)
        => Instruction(inputs) with
        {
            CorrectsMutationBatchId = inputs.Reversal!.MutationBatchId,
            CorrectsJournalEntryId = inputs.Reversal.JournalEntryId,
            CorrectionApproval = new("amortization-correction", LedgerAdjustmentApprovalStatusDto.Approved,
                "independent-controller", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "correct-amortization")
        };

    private static IReadOnlyList<string> ValidateInstruction(AssetLotMutationInstructionDto instruction)
    {
        var inputs = instruction.Amortization!;
        return AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.DepreciationAmortization,
            instruction, Math.Abs(OpenLotAmortization.Project(inputs).FunctionalMovement), inputs.AsOfDate,
            inputs.ExpectedLot.Acquisition.Evidence.Append(inputs.SecurityEvidence).ToArray());
    }

    private static OpenLotAmortizationInstructionDto Inputs()
        => AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m, BondAmortizationMethod.StraightLine, null);

    private static AssetLotMutationInstructionDto Instruction(OpenLotAmortizationInstructionDto inputs)
        => new(AssetLotMutationIntentDto.Amortize, AssetAccountId: "Assets:Investment", Amortization: inputs);

    private static IReadOnlyList<string> Validate(OpenLotAmortizationInstructionDto inputs)
    {
        var valid = Inputs();
        return AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.DepreciationAmortization,
            Instruction(inputs), Math.Abs(OpenLotAmortization.Project(valid).FunctionalMovement), valid.AsOfDate,
            (inputs.ExpectedLot?.Acquisition?.Evidence ?? valid.ExpectedLot.Acquisition.Evidence)
                .Append(inputs.SecurityEvidence ?? valid.SecurityEvidence).ToArray());
    }

    private static OpenLotAmortizationInstructionDto WithTerms(OpenLotAmortizationInstructionDto inputs,
        Action<Dictionary<string, JsonElement>> change)
    {
        var terms = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputs.Security.CommonTerms)!;
        change(terms);
        var security = inputs.Security with { CommonTerms = JsonSerializer.SerializeToElement(terms) };
        return inputs with
        {
            Security = security,
            SecurityEvidence = inputs.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(security) }
        };
    }
}
