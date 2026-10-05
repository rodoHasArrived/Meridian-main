using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
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
