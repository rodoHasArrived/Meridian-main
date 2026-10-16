using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotAmortizationContinuationTests
{
    [Theory]
    [InlineData(110, BondAmortizationMethod.StraightLine)]
    [InlineData(90, BondAmortizationMethod.StraightLine)]
    [InlineData(120, BondAmortizationMethod.ConstantYield)]
    public void Exchange_ContinuesTheOriginalScheduleWithBothBasesAndAcquisitionFx(decimal price, BondAmortizationMethod method)
    {
        var instruction = Inputs(price, method);
        var prior = instruction.Continuation!.PredecessorAmortization;
        var expected = OpenLotAmortization.Project(prior with
        {
            AsOfDate = instruction.AsOfDate,
            ExpectedLot = prior.ExpectedLot with
            {
                OpenTransactionCostBasis = instruction.ExpectedLot.OpenTransactionCostBasis,
                OpenFunctionalCostBasis = instruction.ExpectedLot.OpenFunctionalCostBasis
            }
        });

        OpenLotAmortization.Project(instruction).Should().Be(expected);
        instruction.ExpectedLot.OriginalQuantity.Should().Be(60m);
        prior.ExpectedLot.OriginalQuantity.Should().Be(100m);
        instruction.ExpectedLot.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
        OpenLotAmortization.Project(instruction with { AsOfDate = new DateOnly(2027, 1, 1) })
            .TransactionCostBasis.Should().Be(60m);
    }

    [Fact]
    public void Exchange_LaterPartialReliefUsesTheRemainingOriginalSchedule()
    {
        var instruction = Inputs();
        var lot = instruction.ExpectedLot;
        instruction = instruction with
        {
            ExpectedLot = lot with
            {
                Version = lot.Version + 1,
                OpenQuantity = 30m,
                OpenTransactionCostBasis = lot.OpenTransactionCostBasis / 2m,
                OpenFunctionalCostBasis = lot.OpenFunctionalCostBasis / 2m
            }
        };

        var projected = OpenLotAmortization.Project(instruction);

        // Twenty of twenty-four months have elapsed: (110 - 10 * 20/24) * 30/100.
        projected.TransactionCostBasis.Should().Be(30.5m);
        projected.FunctionalCostBasis.Should().Be(33.55m);
        projected.TransactionMovement.Should().Be(-1m);
    }

    [Theory]
    [InlineData("missing-continuation")]
    [InlineData("unsupported-version")]
    [InlineData("missing-birth")]
    [InlineData("legacy-calculation")]
    [InlineData("changed-terms")]
    [InlineData("changed-reference")]
    [InlineData("changed-fx")]
    [InlineData("changed-origin")]
    [InlineData("before-exchange")]
    [InlineData("stale-predecessor")]
    [InlineData("nested-successor")]
    public void UnprovedContinuationCannotAuthorizeAnAmortization(string alteration)
    {
        var instruction = Inputs();
        var continuation = instruction.Continuation!;
        instruction = alteration switch
        {
            "missing-continuation" => instruction with { Continuation = null },
            "unsupported-version" => instruction with { Continuation = continuation with { ModelVersion = "unknown" } },
            "missing-birth" => instruction with { Continuation = continuation with { SuccessorMutationBatchId = Guid.Empty } },
            "legacy-calculation" => instruction with { CalculationVersion = null },
            "changed-terms" => instruction with { Security = instruction.Security with { CommonTerms = JsonSerializer.SerializeToElement(new { maturityDate = "2028-01-01" }) } },
            "changed-reference" => instruction with { Security = instruction.Security with { Version = 2 } },
            "changed-fx" => instruction with { ExpectedLot = instruction.ExpectedLot with { Acquisition = instruction.ExpectedLot.Acquisition with { AcquisitionFxRateToFunctional = 1.2m } } },
            "changed-origin" => instruction with { ExpectedLot = instruction.ExpectedLot with { Acquisition = instruction.ExpectedLot.Acquisition with { CorporateActionLineage = instruction.ExpectedLot.Acquisition.CorporateActionLineage! with { CorporateActionId = Guid.NewGuid() } } } },
            "before-exchange" => instruction with { AsOfDate = new DateOnly(2026, 8, 24) },
            "stale-predecessor" => instruction with { Continuation = continuation with { PredecessorAmortization = continuation.PredecessorAmortization with { ExpectedLot = continuation.PredecessorAmortization.ExpectedLot with { Version = 99 } } } },
            "nested-successor" => instruction with { Continuation = continuation with { PredecessorAmortization = continuation.PredecessorAmortization with { Continuation = continuation } } },
            _ => throw new ArgumentOutOfRangeException(nameof(alteration))
        };

        var project = () => OpenLotAmortization.Project(instruction);

        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OrdinaryAcquisitionCannotAssertExchangeAuthority()
    {
        var instruction = Inputs();
        var ordinary = instruction.Continuation!.PredecessorAmortization with { Continuation = instruction.Continuation };
        var project = () => OpenLotAmortization.Project(ordinary);
        project.Should().Throw<ArgumentException>().WithMessage("*ordinary acquisition*");
    }

    [Fact]
    public void ChangedMaturityCannotReuseThePredecessorScheduleEvenWithMatchingReferenceHashes()
    {
        var instruction = Inputs();
        var terms = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(instruction.Security.CommonTerms)!;
        terms["maturityDate"] = JsonSerializer.SerializeToElement("2028-01-01");
        var security = instruction.Security with { CommonTerms = JsonSerializer.SerializeToElement(terms) };
        var hash = OpenLotAmortization.SecurityHash(security);
        var continuation = instruction.Continuation!;
        var target = continuation.Exchange.Successors[0] with { ExpectedSecurityHash = hash };
        instruction = instruction with
        {
            Security = security,
            SecurityEvidence = instruction.SecurityEvidence with { ContentHashSha256 = hash },
            Continuation = continuation with { Exchange = continuation.Exchange with { Successors = [target] } }
        };

        var project = () => OpenLotAmortization.Project(instruction);

        project.Should().Throw<ArgumentException>().WithMessage("*unchanged*economic terms*");
    }

    [Fact]
    public void OptionalContinuationPreservesHistoricalJsonAndRetainsTheReviewedNewPayload()
    {
        var instruction = Inputs();
        var ordinary = instruction.Continuation!.PredecessorAmortization;
        JsonSerializer.Serialize(ordinary).Should().NotContain("Continuation");
        var json = JsonSerializer.Serialize(instruction);
        var reloaded = JsonSerializer.Deserialize<OpenLotAmortizationInstructionDto>(json)!;
        OpenLotAmortization.SameInstruction(instruction, reloaded).Should().BeTrue();
        OpenLotAmortization.Project(reloaded).Should().Be(OpenLotAmortization.Project(instruction));
    }

    internal static OpenLotAmortizationInstructionDto Inputs(decimal price = 110m,
        BondAmortizationMethod method = BondAmortizationMethod.StraightLine)
    {
        var prior = AtomicTaxLotJournalStoreTests.AmortPureInstruction(price, 10m, method,
            method == BondAmortizationMethod.ConstantYield ? 0m : null);
        prior = prior with
        {
            ExpectedLot = prior.ExpectedLot with
            { OpenQuantity = 60m, OpenTransactionCostBasis = price * 0.6m, OpenFunctionalCostBasis = price * 0.66m }
        };
        var amortized = OpenLotAmortization.Project(prior);
        var parent = prior.ExpectedLot with
        {
            Version = prior.ExpectedLot.Version + 1,
            OpenTransactionCostBasis = amortized.TransactionCostBasis,
            OpenFunctionalCostBasis = amortized.FunctionalCostBasis
        };
        var target = OpenLotSuccessorTestData.Successor(parent, 1m);
        target = target with
        {
            Lot = target.Lot with
            { OpenTransactionCostBasis = parent.OpenTransactionCostBasis, OpenFunctionalCostBasis = parent.OpenFunctionalCostBasis }
        };
        var security = prior.Security with { SecurityId = target.Lot.SecurityId };
        target = target with { ExpectedSecurityHash = OpenLotAmortization.SecurityHash(security) };
        var exchange = OpenLotSuccessorTestData.Build(parent, [target], expectedSecurityHash: OpenLotAmortization.SecurityHash(prior.Security));
        var lot = OpenLotSuccessors.WithLineage(exchange, target.Lot);
        var evidence = prior.SecurityEvidence with
        {
            SubjectId = security.SecurityId.ToString("D"),
            ContentHashSha256 = OpenLotAmortization.SecurityHash(security)
        };
        return new(lot, security, evidence, 1, new DateOnly(2026, 9, 1))
        { Continuation = new(OpenLotAmortizationContinuation.ModelVersion, Guid.NewGuid(), exchange, prior) };
    }
}
