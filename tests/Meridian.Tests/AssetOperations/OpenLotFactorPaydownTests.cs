using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// Pure rules of the reviewed factor paydown: pro-rata basis relief, the realized difference, the
/// per-lot largest-remainder allocation tied to the factor decline, the full paydown that closes a lot, and the
/// refusals that keep a client from shifting principal or basis between lots.
/// </summary>
public sealed class OpenLotFactorPaydownTests
{
    [Fact]
    public void Project_PremiumLot_0_95To0_90_Relieves50750AndRecognises750Loss()
    {
        // Original face 1,000,000 of par booked at factor 0.95 at 101.5: booked face 950,000, cost
        // 964,250. Paying 0.95 -> 0.90 repays 50,000 of principal, relieves 964,250 x 0.90/0.95 =
        // 50,750 of basis (the 1.5% premium on the 50,000 repaid is a 750 realized loss).
        var lot = FactorPaydownTestData.PremiumLot();
        var instruction = FactorPaydownTestData.Build([lot], 0.95m, 0.90m);

        instruction.PrincipalPaydown.Should().Be(50_000m);
        var allocation = instruction.Lots.Should().ContainSingle().Subject;
        allocation.ExpectedCurrentFactor.Should().Be(0.95m);
        allocation.Principal.Should().Be(50_000m);
        allocation.TransactionBasisAfter.Should().Be(913_500m);
        allocation.FunctionalBasisAfter.Should().Be(913_500m);
        OpenLotFactorPaydown.BasisRelieved(instruction).Should().Be((50_750m, 50_750m));
        OpenLotFactorPaydown.RealizedResult(instruction).Should().Be(-750m, "a premium lot realizes a loss on the principal repaid");
        var components = OpenLotFactorPaydown.Components(instruction);
        components[OpenLotFactorPaydown.PrincipalComponent].Should().Be(50_000m);
        components[OpenLotFactorPaydown.BasisRelievedComponent].Should().Be(50_750m);
        components[OpenLotFactorPaydown.RealizedGainComponent].Should().Be(0m);
        components[OpenLotFactorPaydown.RealizedLossComponent].Should().Be(750m);

        var after = OpenLotFactorPaydown.Project(instruction).Should().ContainSingle().Subject;
        after.OpenQuantity.Should().Be(lot.OpenQuantity, "quantity is booked face and is factor-invariant");
        after.OpenTransactionCostBasis.Should().Be(913_500m);
        after.OpenFunctionalCostBasis.Should().Be(913_500m);
        after.CurrentFactor.Should().Be(0.90m);
        after.Version.Should().Be(lot.Version + 1);
        after.Acquisition.Should().BeSameAs(lot.Acquisition, "a paydown never restates acquisition facts");
        OpenLotFactorPaydown.Project(JsonSerializer.Deserialize<OpenLotFactorPaydownInstructionDto>(
            JsonSerializer.Serialize(instruction))!).Should().BeEquivalentTo([after]);
    }

    [Fact]
    public void Project_FullPaydownToZero_ClosesLotRelievesAllBasisAndRecognises13500Loss()
    {
        var lot = FactorPaydownTestData.PremiumLot();
        var step1 = OpenLotFactorPaydown.Project(FactorPaydownTestData.Build([lot], 0.95m, 0.90m)).Single();
        var instruction = FactorPaydownTestData.Build([step1], 0.90m, 0m);

        instruction.PrincipalPaydown.Should().Be(900_000m, "par face 1,000,000 x 0.90");
        OpenLotFactorPaydown.BasisRelieved(instruction).Functional.Should().Be(913_500m);
        OpenLotFactorPaydown.RealizedResult(instruction).Should().Be(-13_500m);
        var closed = OpenLotFactorPaydown.Project(instruction).Single();
        closed.OpenQuantity.Should().Be(0m, "a full paydown closes the lot");
        closed.OpenTransactionCostBasis.Should().Be(0m);
        closed.OpenFunctionalCostBasis.Should().Be(0m);
        closed.CurrentFactor.Should().Be(0m);
        closed.Version.Should().Be(lot.Version + 2);

        // Tie-out across both steps: cash 950,000 is the booked face, basis relieved 964,250 is the
        // acquisition basis, cumulative loss 14,250 is the premium paid.
        (50_000m + instruction.PrincipalPaydown).Should().Be(950_000m);
        (50_750m + 913_500m).Should().Be(lot.OpenFunctionalCostBasis);
        (-750m + OpenLotFactorPaydown.RealizedResult(instruction)).Should().Be(-14_250m);
    }

    [Fact]
    public void Project_DiscountLot_RecognisesGain()
    {
        var lot = FactorPaydownTestData.PremiumLot(pricePercentOfPar: 98m);
        lot.OpenFunctionalCostBasis.Should().Be(931_000m);
        var instruction = FactorPaydownTestData.Build([lot], 0.95m, 0.90m);

        OpenLotFactorPaydown.BasisRelieved(instruction).Functional.Should().Be(49_000m);
        OpenLotFactorPaydown.RealizedResult(instruction).Should().Be(1_000m, "a discount lot realizes a gain on the principal repaid");
        var components = OpenLotFactorPaydown.Components(instruction);
        components[OpenLotFactorPaydown.RealizedGainComponent].Should().Be(1_000m);
        components[OpenLotFactorPaydown.RealizedLossComponent].Should().Be(0m);
        OpenLotFactorPaydown.Project(instruction).Single().OpenFunctionalCostBasis.Should().Be(882_000m);
    }

    [Fact]
    public void Project_ParLot_RealizesNothing()
    {
        var lot = FactorPaydownTestData.PremiumLot(pricePercentOfPar: 100m);
        var instruction = FactorPaydownTestData.Build([lot], 0.95m, 0.90m);
        OpenLotFactorPaydown.RealizedResult(instruction).Should().Be(0m);
        OpenLotFactorPaydown.Components(instruction).Values.Where(static value => value == 0m).Should().HaveCount(2);
    }

    [Fact]
    public void Allocate_MultiLot_AssignsCurrencyResidualToLastLot()
    {
        // Three par lots of 1,000 par face each paying 0.95 -> 0.916667 repay 33.333 apiece; the
        // position principal is rounded once (99.999 -> 100.00). Each lot takes 33.33 and the one
        // cent left over goes to the largest remainder, a three-way tie broken toward the later lot,
        // so the lots always sum to the position principal exactly.
        var lots = FactorPaydownTestData.PoolLots([950m, 950m, 950m]);
        var instruction = FactorPaydownTestData.Build(lots, 0.95m, 0.916667m);

        instruction.PrincipalPaydown.Should().Be(100.00m);
        instruction.Lots.Select(static lot => lot.Principal).Should().Equal(33.33m, 33.33m, 33.34m);
        instruction.Lots.Select(static lot => lot.Ordinal).Should().Equal(0, 1, 2);
        instruction.Lots.Sum(static lot => lot.Principal).Should().Be(instruction.PrincipalPaydown);
        instruction.Lots.Should().OnlyContain(lot => lot.FunctionalBasisAfter == 916.67m);
        // Basis is relieved per lot (950 - 916.67 = 33.33, three times = 99.99) while principal is
        // rounded once at the position (100.00), so across lots the currency rounding is recognised
        // in realized P&L even though every lot is at par: a one-cent realized gain.
        OpenLotFactorPaydown.BasisRelieved(instruction).Functional.Should().Be(99.99m);
        OpenLotFactorPaydown.RealizedResult(instruction).Should().Be(0.01m);
        OpenLotFactorPaydown.Components(instruction)[OpenLotFactorPaydown.RealizedGainComponent].Should().Be(0.01m);
        OpenLotFactorPaydown.Project(instruction).Should().HaveCount(3)
            .And.OnlyContain(lot => lot.CurrentFactor == 0.916667m && lot.OpenQuantity == 950m);
    }

    [Fact]
    public void Allocate_TinyMultiLotPaydown_LargestRemainderNeverAllocatesANegativeLot()
    {
        // Four fully open USD lots of 1,000 par face each paying 1 -> 0.999994 repay 0.006 apiece; the
        // position principal is round(0.024) = 0.02. Rounding each lot away from zero and giving the
        // last lot the residual would allocate 0.01, 0.01, 0.01 and -0.01. Largest remainder truncates
        // every lot to 0.00 and gives the two cents to the largest remainders, a four-way tie broken
        // toward the later lots.
        var lots = FactorPaydownTestData.PoolLots([1_000m, 1_000m, 1_000m, 1_000m], bookedFactor: 1m);
        var instruction = FactorPaydownTestData.Build(lots, 1m, 0.999994m);

        instruction.PrincipalPaydown.Should().Be(0.02m);
        instruction.Lots.Select(static lot => lot.Principal).Should().Equal(0.00m, 0.00m, 0.01m, 0.01m);
        instruction.Lots.Should().OnlyContain(lot => lot.Principal >= 0m && lot.Principal <= 0.01m,
            "no lot goes below zero or above its 0.006 share rounded up");
        instruction.Lots.Sum(static lot => lot.Principal).Should().Be(0.02m);
        OpenLotFactorPaydown.Project(instruction).Should().HaveCount(4)
            .And.OnlyContain(lot => lot.CurrentFactor == 0.999994m);
    }

    [Theory]
    [InlineData("0.916667")]
    [InlineData("0.9")]
    [InlineData("0.123457")]
    [InlineData("0.000001")]
    [InlineData("0")]
    public void Allocate_NeverGivesALotLessThanItsTruncatedShareOrMoreThanItsShareRoundedUp(string newFactor)
    {
        // Lots of uneven par face and open share; one is half sold. Whatever the factor, every lot's
        // principal sits between its raw share truncated and rounded up to the cent, and the lots sum
        // to the position principal.
        var lots = FactorPaydownTestData.PoolLots([950m, 1_900.95m, 7_125m, 33.25m]);
        lots[1] = lots[1] with
        {
            OpenQuantity = lots[1].OriginalQuantity / 2m,
            OpenTransactionCostBasis = lots[1].OpenTransactionCostBasis / 2m,
            OpenFunctionalCostBasis = lots[1].OpenFunctionalCostBasis / 2m
        };
        var factor = decimal.Parse(newFactor, System.Globalization.CultureInfo.InvariantCulture);
        var instruction = FactorPaydownTestData.Build(lots, 0.95m, factor);

        foreach (var allocation in instruction.Lots)
        {
            var lot = allocation.ExpectedLot;
            var raw = lot.OriginalQuantity / 0.95m * (lot.OpenQuantity / lot.OriginalQuantity) * (0.95m - factor);
            allocation.Principal.Should().BeGreaterThanOrEqualTo(decimal.Round(raw, 2, MidpointRounding.ToZero));
            allocation.Principal.Should().BeLessThanOrEqualTo(decimal.Round(raw, 2, MidpointRounding.ToPositiveInfinity));
        }
        instruction.Lots.Sum(static lot => lot.Principal).Should().Be(instruction.PrincipalPaydown);
        OpenLotFactorPaydown.Project(instruction).Should().HaveCount(4);
    }

    [Fact]
    public void Allocate_RefusesAPositionPrincipalNotTiedToTheFactorDecline()
    {
        // The factors determine the principal exactly: 1,000,000 of par face x 0.05. A principal off
        // by any amount would otherwise be absorbed by the lots and posted as realized gain or loss
        // against cash the pool never paid.
        var lot = FactorPaydownTestData.PremiumLot();
        OpenLotFactorPaydown.Allocate([lot], 0.95m, 0.90m, 50_000m, "USD").Single().Principal.Should().Be(50_000m);
        foreach (var principal in new[] { 50_001m, 49_999.99m, 50_000.01m })
        {
            var allocate = () => OpenLotFactorPaydown.Allocate([lot], 0.95m, 0.90m, principal, "USD");
            allocate.Should().Throw<ArgumentException>().WithMessage("*held par face x (prior - new)*");
        }
    }

    [Fact]
    public void Amortization_RefusesALotThatHoldsAPaydownFactor()
    {
        // A lot booked at factor 1 and since paid down is not amortized: its premium or discount is
        // realized through the paydowns and the reduced basis at disposal.
        var instruction = Meridian.Tests.Storage.AtomicTaxLotJournalStoreTests.AmortPureInstruction(
            110m, 10m, BondAmortizationMethod.StraightLine, null);
        OpenLotAmortization.Project(instruction);

        var paidDown = instruction with { ExpectedLot = instruction.ExpectedLot with { CurrentFactor = 0.9m } };
        var project = () => OpenLotAmortization.Project(paidDown);

        project.Should().Throw<ArgumentException>().WithMessage("*paydown factor*");
    }

    [Theory]
    [InlineData("factor-increase")]
    [InlineData("factor-equal")]
    [InlineData("unit-lot")]
    [InlineData("stale-lot-factor")]
    [InlineData("cross-currency")]
    [InlineData("tampered-principal")]
    [InlineData("tampered-basis")]
    [InlineData("shifted-principal")]
    [InlineData("shifted-position-principal")]
    [InlineData("wrong-event-type")]
    [InlineData("wrong-event-id")]
    [InlineData("wrong-model-key")]
    [InlineData("ordinal-gap")]
    [InlineData("duplicate-lot")]
    [InlineData("foreign-scope-lot")]
    [InlineData("acquired-after-paydown")]
    [InlineData("closed-lot")]
    public void Project_RefusesFactorIncreaseEqualFactorUnitLotStaleLotFactorAndCrossCurrency(string change)
    {
        var scope = FactorPaydownTestData.PremiumLot();
        var second = FactorPaydownTestData.PremiumLot(scope.SecurityId, scope.BookPositionId, scope.LedgerBookId,
            lotId: "pool-lot-2", acquired: scope.AcquiredDate.AddMonths(1));
        var instruction = FactorPaydownTestData.Build([scope, second], 0.95m, 0.90m);
        var lots = instruction.Lots.ToArray();
        OpenLotFactorPaydownLotDto Lot(int index, Func<OpenLotDto, OpenLotDto> change) => lots[index] with { ExpectedLot = change(lots[index].ExpectedLot) };
        instruction = change switch
        {
            "factor-increase" => instruction with { NewFactor = 0.96m },
            "factor-equal" => instruction with { NewFactor = 0.95m },
            "unit-lot" => instruction with
            {
                Lots = [lots[0], Lot(1, lot => lot with { Acquisition = lot.Acquisition with { QuantityBasis = LotQuantityBasis.Units, FaceValueTerms = null } })]
            },
            "stale-lot-factor" => instruction with { Lots = [lots[0], Lot(1, lot => lot with { CurrentFactor = 0.90m })] },
            "cross-currency" => instruction with
            {
                Lots = [lots[0], Lot(1, lot => lot with
                {
                    OpenTransactionCostBasis = lot.OpenTransactionCostBasis / 1.1m,
                    Acquisition = lot.Acquisition with
                    {
                        AcquisitionCurrency = "EUR", AcquisitionFxRateToFunctional = 1.1m,
                        TransactionCostBasis = lot.Acquisition.TransactionCostBasis / 1.1m
                    }
                })]
            },
            "tampered-principal" => instruction with { PrincipalPaydown = instruction.PrincipalPaydown + 1m },
            "tampered-basis" => instruction with { Lots = [lots[0] with { FunctionalBasisAfter = lots[0].FunctionalBasisAfter + 1m }, lots[1]] },
            "shifted-principal" => instruction with { Lots = [lots[0] with { Principal = lots[0].Principal + 1m }, lots[1] with { Principal = lots[1].Principal - 1m }] },
            // The position principal and the last lot moved together: every per-lot comparison and
            // the sum agree, so only the tie to the factor decline refuses it.
            "shifted-position-principal" => instruction with
            {
                PrincipalPaydown = instruction.PrincipalPaydown + 1m,
                Lots = [lots[0], lots[1] with { Principal = lots[1].Principal + 1m }]
            },
            "wrong-event-type" => instruction with { EconomicEvent = instruction.EconomicEvent with { EventType = FactorPaydownProjectionService.EventType } },
            "wrong-event-id" => instruction with { EconomicEvent = instruction.EconomicEvent with { EventId = Guid.NewGuid() } },
            "wrong-model-key" => instruction with { Lineage = instruction.Lineage with { ModelKey = "client-selected-model" } },
            "ordinal-gap" => instruction with { Lots = [lots[0], lots[1] with { Ordinal = 2 }] },
            "duplicate-lot" => instruction with { Lots = [lots[0], lots[0] with { Ordinal = 1 }] },
            "foreign-scope-lot" => instruction with { Lots = [lots[0], Lot(1, lot => lot with { BookPositionId = Guid.NewGuid() })] },
            "acquired-after-paydown" => instruction with { Lots = [lots[0], Lot(1, lot => lot with { AcquiredDate = instruction.EconomicEvent.EffectiveDate.AddDays(1) })] },
            _ => instruction with { Lots = [lots[0], Lot(1, lot => lot with { OpenQuantity = 0m, OpenTransactionCostBasis = 0m, OpenFunctionalCostBasis = 0m })] }
        };

        var project = () => OpenLotFactorPaydown.Project(instruction);
        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Fingerprint_IsStableAndOmitsNulls()
    {
        var instruction = FactorPaydownTestData.Build([FactorPaydownTestData.PremiumLot()], 0.95m, 0.90m);
        var fingerprint = OpenLotFactorPaydown.Fingerprint(instruction);
        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
        OpenLotFactorPaydown.Fingerprint(JsonSerializer.Deserialize<OpenLotFactorPaydownInstructionDto>(
            JsonSerializer.Serialize(instruction))!).Should().Be(fingerprint, "the fingerprint survives a JSON round trip");
        OpenLotFactorPaydown.Fingerprint(instruction with { NewFactor = 0.85m }).Should().NotBe(fingerprint);

        // Absent factor-paydown members never appear in retained JSON, so every historical instruction,
        // basis adjustment, canonical lot and posting command keeps its digest.
        JsonSerializer.Serialize(new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.None)).Should().NotContain("FactorPaydown");
        JsonSerializer.Serialize(new OpenLotBasisAdjustmentDto(Guid.NewGuid(), "Amortization", 1m, 10m, 10m)).Should().NotContain("FactorPaydown");
        JsonSerializer.Serialize(OpenLotSuccessorTestData.Predecessor()).Should().NotContain("CurrentFactor");
        JsonSerializer.Serialize(new AccountingPostingCommandDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 1, 1), DateTimeOffset.UtcNow, "key", AccountingPostingIntentDto.Originating, Guid.NewGuid())).Should().NotContain("FactorPaydown");
    }

    [Fact]
    public void OpenLotDto_CurrentFactorOmittedWhenNull_KeepsCapitalReturnFingerprintAndMakesStaleLotsDetectable()
    {
        var lot = OpenLotSuccessorTestData.Predecessor();
        var capitalReturn = CapitalReturnTestData.Build(lot);
        OpenLotCapitalReturn.Fingerprint(capitalReturn).Should().Be(OpenLotCapitalReturn.Fingerprint(
            capitalReturn with { ExpectedLot = lot with { CurrentFactor = null } }));
        OpenLotAmortization.SameLot(lot, lot with { CurrentFactor = null }).Should().BeTrue();
        OpenLotAmortization.SameLot(lot, lot with { CurrentFactor = 0.9m }).Should().BeFalse("a paid-down lot no longer matches a stale expected lot");
        var apply = () => OpenLotValidation.Validate(lot with { CurrentFactor = 1m });
        apply.Should().Throw<ArgumentException>("a current factor is strictly below the booked factor");
        OpenLotValidation.Validate(lot with { CurrentFactor = 0m });
    }

    [Fact]
    public void Validator_RequiresTheIntentAmountDateAndEveryLotsAcquisitionEvidence()
    {
        var instruction = FactorPaydownTestData.Build([FactorPaydownTestData.PremiumLot()], 0.95m, 0.90m);
        var evidence = FactorPaydownTestData.RetainedEvidence(instruction);
        var date = instruction.EconomicEvent.EffectiveDate;
        IReadOnlyList<string> Validate(AssetLotMutationInstructionDto mutation, decimal amount, DateOnly effective, IReadOnlyList<RetainedEvidenceIdentityDto> retained)
            => AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.CorporateAction, mutation, amount, effective, retained);
        var mutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.FactorPaydown, FactorPaydown: instruction);

        Validate(mutation, 50_000m, date, evidence).Should().BeEmpty();
        Validate(mutation with { Intent = AssetLotMutationIntentDto.CapitalReturn }, 50_000m, date, evidence).Should().NotBeEmpty();
        Validate(mutation, 50_750m, date, evidence).Should().ContainSingle(issue => issue.Contains("principal amount"));
        Validate(mutation, 50_000m, date.AddDays(1), evidence).Should().ContainSingle(issue => issue.Contains("event date"));
        Validate(mutation, 50_000m, date, evidence.Where(item => item.SubjectType != "OpenLotAcquisition").ToArray())
            .Should().ContainSingle(issue => issue.Contains("acquisition evidence"));
        Validate(mutation with { ReliefMethod = "Fifo" }, 50_000m, date, evidence).Should().NotBeEmpty();
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.Disposal, mutation, 50_000m, date, evidence).Should().NotBeEmpty();
    }

    [Fact]
    public void EvidenceDate_AdmitsOnlyThePaidDownLotsOwnAcquisitionEvidenceBeforeTheEvent()
    {
        var instruction = FactorPaydownTestData.Build([FactorPaydownTestData.PremiumLot()], 0.95m, 0.90m);
        var lot = instruction.Lots[0].ExpectedLot;
        var acquisition = lot.Acquisition.Evidence.Single();
        var date = instruction.EconomicEvent.EffectiveDate;
        (acquisition.EffectiveDate < date).Should().BeTrue();
        bool Admitted(RetainedEvidenceIdentityDto item) => AssetAccountingEvidenceSubjects.MatchesEventEvidenceDate(
            AssetAccountingEventKindDto.CorporateAction, date, lot.SecurityId, instruction.Scope.ExpectedSecurityVersion,
            item, null, requireInstruction: true, factorPaydown: instruction);
        Admitted(acquisition).Should().BeTrue();
        Admitted(acquisition with { ContentHashSha256 = new string('f', 64) }).Should().BeFalse();
        Admitted(acquisition with { EffectiveDate = date.AddDays(1) }).Should().BeFalse();
        Admitted(acquisition with { SubjectId = Guid.NewGuid().ToString("D") }).Should().BeFalse();
        Admitted(acquisition with { EffectiveDate = date }).Should().BeTrue("event-dated evidence always matches");
    }

    [Fact]
    public void ProjectionIdentity_IsTheProjectorsIdentity()
    {
        // The Contracts identity rule and the Instruments projector must agree, or the instruction
        // would refuse the very event the projector emits.
        var securityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var hash = Sha256Digest.ComputeUtf8("factor-row");
        var projection = new FactorPaydownProjectionService().Project(new FactorPaydownProjectionRequest(securityId, positionId,
            3, 3, 1_000_000m, 0.95m, 0.90m, "USD", new DateOnly(2026, 6, 30), DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
            "SecurityMaster", "factor-row", hash.ToUpperInvariant(), ["evidence://factor"]));
        projection.ProducesPostingCandidate.Should().BeTrue();
        projection.EconomicEvent!.EventId.Should().Be(FactorPaydownProjectionIdentity.EventId(securityId, positionId,
            new DateOnly(2026, 6, 30), 0.95m, 0.90m, hash));
        projection.EconomicEvent.EventType.Should().Be(FactorPaydownProjectionIdentity.EventType);
        projection.Lineage!.ModelKey.Should().Be(FactorPaydownProjectionIdentity.ModelKey);
        projection.Lineage.ProjectionRunId.Should().Be(FactorPaydownProjectionIdentity.DeterministicGuid($"{projection.EconomicEvent.EventId:N}|projection-run"));
    }
}

internal static class FactorPaydownTestData
{
    internal static readonly DateOnly EffectiveDate = new(2026, 6, 30);
    internal static readonly DateOnly AcquiredDate = new(2026, 1, 15);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 30, 18, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The worked lot: original face 1,000,000 of par booked at factor 0.95 (booked face 950,000) at
    /// 101.5, cost basis 964,250, USD throughout. Canonical open-lot units are face, so the open-lot
    /// quantities are the booked face.
    /// </summary>
    internal static OpenLotDto PremiumLot(Guid? securityId = null, Guid? positionId = null, Guid? bookId = null,
        decimal pricePercentOfPar = 101.5m, decimal originalFace = 950_000m, decimal bookedFactor = 0.95m,
        decimal? openFace = null, string lotId = "pool-lot-1", DateOnly? acquired = null, long version = 1)
    {
        var id = Guid.NewGuid();
        var acquiredDate = acquired ?? AcquiredDate;
        var basis = originalFace * pricePercentOfPar / 100m;
        var open = openFace ?? originalFace;
        return new OpenLotDto(id, securityId ?? Guid.NewGuid(), positionId ?? Guid.NewGuid(), bookId ?? Guid.NewGuid(), lotId,
            acquiredDate, originalFace, open, basis * open / originalFace, basis * open / originalFace, version,
            new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, basis, basis, acquiredDate,
                new FaceValueAcquisitionTermsDto(100m, bookedFactor, BondAmortizationMethod.NoAmortization, null),
                [AcquisitionEvidence(id, acquiredDate)]));
    }

    /// <summary>
    /// Par lots of one pool, one per booked face given, acquired a day apart so the array order is
    /// the store's acquired-date order.
    /// </summary>
    internal static OpenLotDto[] PoolLots(IReadOnlyList<decimal> bookedFaces, decimal bookedFactor = 0.95m)
    {
        var scope = PremiumLot(pricePercentOfPar: 100m, originalFace: bookedFaces[0], bookedFactor: bookedFactor);
        return bookedFaces.Select((face, index) => PremiumLot(scope.SecurityId, scope.BookPositionId, scope.LedgerBookId,
            pricePercentOfPar: 100m, originalFace: face, bookedFactor: bookedFactor, lotId: $"pool-lot-{index}",
            acquired: scope.AcquiredDate.AddDays(index))).ToArray();
    }

    /// <summary>The factor row's content hash: the given one, or a deterministic stand-in for the security and date.</summary>
    internal static string SourceHash(OpenLotDto first, DateOnly effective, string? sourceHash = null)
        => sourceHash ?? Sha256Digest.ComputeUtf8($"factor-row|{first.SecurityId:N}|{effective:yyyy-MM-dd}");

    /// <summary>
    /// The real projector run the instruction is built from: held face is the lots' par face (booked
    /// face restated to a factor of one, scaled by the open share), so the position's principal is
    /// par face times the factor decline, exactly as the candidate service recomputes it.
    /// </summary>
    internal static FactorPaydownProjectionResult Projection(IReadOnlyList<OpenLotDto> lots, decimal priorFactor, decimal newFactor,
        long positionVersion = 1, DateOnly? date = null, string? sourceHash = null)
    {
        var first = lots[0];
        var effective = date ?? EffectiveDate;
        var heldFace = lots.Sum(lot => lot.OriginalQuantity / lot.Acquisition.FaceValueTerms!.BookedFactor * (lot.OpenQuantity / lot.OriginalQuantity));
        return new FactorPaydownProjectionService().Project(new FactorPaydownProjectionRequest(first.SecurityId, first.BookPositionId,
            positionVersion, positionVersion, heldFace, priorFactor, newFactor, first.Acquisition.FunctionalCurrency, effective, ObservedAt,
            "SecurityMaster", $"factor-row-{effective:yyyyMM}", SourceHash(first, effective, sourceHash), ["evidence://factor/row"]));
    }

    /// <summary>
    /// The lot of record that projects back to <paramref name="lot"/>: quantity is face per 100, unit
    /// cost is the acquisition basis per unit, and the par terms and current factor are carried as
    /// the store carries them.
    /// </summary>
    internal static LedgerTaxLotRecord LotRecord(OpenLotDto lot, LedgerAccount account, DateTimeOffset at, Guid? mutationBatchId = null)
    {
        var terms = lot.Acquisition.FaceValueTerms!;
        var quantity = lot.OriginalQuantity / LedgerTaxLotFaceValueTerms.LedgerLotParBasis;
        var batch = mutationBatchId ?? Guid.NewGuid();
        return new LedgerTaxLotRecord(lot.TaxLotRecordId, lot.LedgerBookId, account, lot.LotId, lot.AcquiredDate, quantity,
            lot.OpenQuantity / LedgerTaxLotFaceValueTerms.LedgerLotParBasis, lot.Acquisition.TransactionCostBasis / quantity,
            lot.Acquisition.AcquisitionCurrency, at, at, Version: lot.Version, OriginatingMutationBatchId: batch, LastMutationBatchId: batch,
            SecurityId: lot.SecurityId, BookPositionId: lot.BookPositionId, OriginalFace: lot.OriginalQuantity,
            BookedFactor: terms.BookedFactor, ParBasis: terms.ParBasis, Acquisition: lot.Acquisition, CurrentFactor: lot.CurrentFactor);
    }

    internal static OpenLotFactorPaydownInstructionDto Build(IReadOnlyList<OpenLotDto> lots, decimal priorFactor, decimal newFactor,
        long securityVersion = 1, long positionVersion = 1, Guid? periodId = null, long periodVersion = 1,
        string fund = "fund-alpha", string? securityHash = null, DateOnly? date = null, string? sourceHash = null,
        AccountingBasisKindDto accountingBasis = AccountingBasisKindDto.Gaap, string? tenantId = "tenant-alpha",
        string? companyId = "company-alpha", LedgerDimensionSetDto? dimensions = null)
    {
        var first = lots[0];
        var effective = date ?? EffectiveDate;
        var hash = SourceHash(first, effective, sourceHash);
        var currency = first.Acquisition.FunctionalCurrency;
        var projection = Projection(lots, priorFactor, newFactor, positionVersion, effective, hash);
        projection.ProducesPostingCandidate.Should().BeTrue(string.Join("; ", projection.Issues.Select(static issue => issue.Message)));
        var allocated = OpenLotFactorPaydown.Allocate(lots, priorFactor, newFactor, projection.PrincipalPaydown!.Value, currency);
        var acquisitionEvidence = lots.SelectMany(static lot => lot.Acquisition.Evidence).ToArray();
        var economicEvent = projection.EconomicEvent! with
        {
            EventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction)
        };
        var factorEvidence = new RetainedEvidenceIdentityDto("factor-event", "evidence://factor/row", hash, "SecurityMaster",
            economicEvent.SourceEntityId!, "Accepted", "controller", ObservedAt, effective, 1, ObservedAt.AddMinutes(1),
            "evidence-vault", AssetAccountingEvidenceSubjects.Event, economicEvent.EventId.ToString("D"));
        RetainedEvidenceIdentityDto[] evidence = [factorEvidence, .. acquisitionEvidence];
        economicEvent = economicEvent with
        {
            EvidenceLinks = evidence.Select(static item => item.EvidenceUri).Distinct().ToArray(),
            RetainedEvidence = evidence
        };
        var lineage = projection.Lineage! with
        {
            TriggerEvent = economicEvent,
            EvidenceLinks = economicEvent.EvidenceLinks,
            RetainedEvidence = evidence
        };
        var scope = new AssetAccountingEventScopeDto(first.SecurityId, securityVersion, first.BookPositionId, positionVersion,
            first.LedgerBookId, periodId ?? Guid.NewGuid(), accountingBasis, fund, tenantId, companyId, dimensions);
        return new OpenLotFactorPaydownInstructionDto(economicEvent, lineage, scope, periodVersion,
            securityHash ?? Sha256Digest.ComputeUtf8("source-security"), priorFactor, newFactor,
            projection.PrincipalPaydown.Value, currency, allocated);
    }

    /// <summary>The factor event evidence and every paid-down lot's acquisition evidence, as the spine retains them.</summary>
    internal static IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence(OpenLotFactorPaydownInstructionDto instruction)
        => instruction.EconomicEvent.RetainedEvidence;

    /// <summary>
    /// The spine projection request a preparation service assembles: the canonical corporate-action
    /// event, its lineage, and the three-line projected effect (cash, carrying relief, realized
    /// result) with a zero component omitted.
    /// </summary>
    internal static ProjectAssetAccountingEventRequestDto ProjectRequest(OpenLotFactorPaydownInstructionDto instruction,
        LedgerDimensionSetDto dimensions, string actor = "projector", string cashPath = "Assets:Cash",
        string carryingPath = "Assets:Investment", string gainPath = "Income:Realized Gain", string lossPath = "Expense:Realized Loss")
    {
        var components = OpenLotFactorPaydown.Components(instruction);
        var currency = instruction.Currency;
        var lines = new List<ProjectedAccountingEffectLineDto>
        {
            new(cashPath, components[OpenLotFactorPaydown.PrincipalComponent], 0m, currency, Dimensions: dimensions)
        };
        if (components[OpenLotFactorPaydown.BasisRelievedComponent] > 0m)
            lines.Add(new(carryingPath, 0m, components[OpenLotFactorPaydown.BasisRelievedComponent], currency, Dimensions: dimensions));
        if (components[OpenLotFactorPaydown.RealizedGainComponent] > 0m)
            lines.Add(new(gainPath, 0m, components[OpenLotFactorPaydown.RealizedGainComponent], currency, Dimensions: dimensions));
        if (components[OpenLotFactorPaydown.RealizedLossComponent] > 0m)
            lines.Add(new(lossPath, components[OpenLotFactorPaydown.RealizedLossComponent], 0m, currency, Dimensions: dimensions));
        var lineage = instruction.Lineage;
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion,
            instruction.EconomicEvent.EffectiveDate, lines.Sum(static line => line.Debit), lines.Sum(static line => line.Credit), currency, lines);
        return new ProjectAssetAccountingEventRequestDto(AssetAccountingEventKindDto.CorporateAction, instruction.Scope,
            instruction.EconomicEvent, lineage, effect, instruction.PrincipalPaydown, currency, instruction.ExpectedPeriodVersion,
            actor, lineage.GeneratedAtUtc.AddHours(1), RetainedEvidence(instruction), FactorPaydown: instruction);
    }

    internal static RetainedEvidenceIdentityDto AcquisitionEvidence(Guid lotId, DateOnly date)
        => new($"acquisition-{lotId:D}", $"evidence://acquisition/{lotId:D}", new string('e', 64), "Ledger",
            lotId.ToString("D"), "Accepted", "reviewer", new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero), date, 1,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 30)), TimeSpan.Zero), "retention-service", "OpenLotAcquisition", lotId.ToString("D"));
}
