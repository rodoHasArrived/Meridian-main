using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The exposure and credit support arithmetic on the worked fixture: an ISDA 2002 master, a CSA
/// with a 250,000 counterparty threshold, 100,000 minimum transfer, 1,000,000 independent amount
/// posted by the fund and 10,000 rounding, a 10,000,000 bought swap and a 4,000,000 sold one, and
/// 596,245 of haircut-adjusted collateral posted by the fund.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OtcExposureRulesTests
{
    private static readonly DateOnly Trade = new(2026, 3, 2);
    private static readonly DateOnly AsOf = new(2026, 3, 31);
    private static readonly Guid SwapId = Guid.NewGuid();
    private static readonly Guid MasterId = Guid.NewGuid();
    private static readonly Guid CsaId = Guid.NewGuid();
    private static readonly Guid NettingSetId = Guid.NewGuid();
    private static readonly Guid BuyId = Guid.NewGuid();
    private static readonly Guid SellId = Guid.NewGuid();

    [Theory]
    [InlineData("A net", 1.25, 400_000, OtcMasterAgreementType.Isda2002, 75_000, null, "Delivery:Fund:330000")]
    [InlineData("B return", 1.25, 1_600_000, OtcMasterAgreementType.Isda2002, 75_000, null, "Return:Counterparty:870000")]
    [InlineData("C below MTA", 1.25, 750_000, OtcMasterAgreementType.Isda2002, 75_000, null, null)]
    [InlineData("D gross", 1.25, 400_000, OtcMasterAgreementType.Other, 75_000, null, "Delivery:Fund:460000")]
    [InlineData("E signed exposure", 14.00, 400_000, OtcMasterAgreementType.Isda2002, 840_000, null, "Return:Counterparty:430000")]
    [InlineData("F two movements", 25.00, 400_000, OtcMasterAgreementType.Isda2002, 1_500_000, "Delivery:Counterparty:250000", "Return:Counterparty:590000")]
    public void WorkedCases_ProduceTheExpectedMovements(string label, double pricePercent, decimal cashPosted, OtcMasterAgreementType masterType, decimal expectedNet, string? fundSideMovement, string? counterpartySideMovement)
    {
        var view = View(masterType);
        var price = (decimal)pricePercent;
        var valuations = new[] { Valued(view.Contracts[0], price), Valued(view.Contracts[1], price) };

        var snapshot = OtcExposureRules.Compute(view, valuations, Balances(cashPosted), AsOf, "casey", DateTimeOffset.UtcNow);

        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Complete, label);
        snapshot.NettingBasis.Should().Be(masterType == OtcMasterAgreementType.Other ? OtcNettingBasis.Gross : OtcNettingBasis.CloseOutNet, label);
        snapshot.NetExposure.Should().Be(expectedNet, label);
        snapshot.GrossPositive.Should().Be(view.Contracts[0].Notional * price / 100m, label);
        snapshot.GrossNegative.Should().Be(view.Contracts[1].Notional * price / 100m, label);
        snapshot.Currency.Should().Be("USD");
        snapshot.RuleVersion.Should().Be(OtcExposureRules.RuleVersion);
        snapshot.Blockers.Should().BeEmpty(label);
        snapshot.CollateralHeld.Should().HaveCount(2, label);
        snapshot.CounterpartyAsSecuredParty.Held.Should().Be(cashPosted + 196_245m, label);
        snapshot.FundAsSecuredParty.Held.Should().Be(0m, label);

        Describe(snapshot.FundAsSecuredParty.Movement).Should().Be(fundSideMovement, $"{label}: fund as Secured Party");
        Describe(snapshot.CounterpartyAsSecuredParty.Movement).Should().Be(counterpartySideMovement, $"{label}: counterparty as Secured Party");
        snapshot.Movements.Select(Describe).Should().Equal(new[] { fundSideMovement, counterpartySideMovement }.Where(m => m is not null), label);
        snapshot.Movements.Should().OnlyContain(m => m.Currency == "USD");
    }

    [Fact]
    public void CaseA_ReportsTheCreditSupportArithmeticOnBothSides()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        var snapshot = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m), Valued(view.Contracts[1], 1.25m)], Balances(400_000m), AsOf, "casey", DateTimeOffset.UtcNow);

        var fund = snapshot.FundAsSecuredParty;
        fund.SecuredParty.Should().Be(OtcMovementParty.Fund);
        fund.Exposure.Should().Be(75_000m);
        fund.IndependentAmountPledgor.Should().Be(0m, "the counterparty posts no independent amount");
        fund.IndependentAmountSecuredParty.Should().Be(1_000_000m, "the independent amount is read as the fund's");
        fund.ThresholdPledgor.Should().Be(250_000m);
        fund.CreditSupportAmount.Should().Be(0m);
        fund.Movement.Should().BeNull();

        var counterparty = snapshot.CounterpartyAsSecuredParty;
        counterparty.SecuredParty.Should().Be(OtcMovementParty.Counterparty);
        counterparty.Exposure.Should().Be(-75_000m, "close-out net exposure is signed");
        counterparty.IndependentAmountPledgor.Should().Be(1_000_000m);
        counterparty.ThresholdPledgor.Should().Be(0m);
        counterparty.CreditSupportAmount.Should().Be(925_000m);
        counterparty.Held.Should().Be(596_245m);
        counterparty.DeliveryRaw.Should().Be(328_755m);
        counterparty.Movement!.Amount.Should().Be(330_000m, "the delivery rounds up after the MTA test on the raw amount");

        snapshot.CollateralHeld.Should().ContainSingle(line => line.Kind == OtcCollateralKind.Security)
            .Which.HaircutAdjusted.Should().Be(196_245m);
        snapshot.Provenance.Should().Be(DataProvenance.Real);
        snapshot.MasterAgreementVersion.Should().Be(view.MasterAgreement.Version);
        snapshot.CollateralAgreementVersion.Should().Be(view.CollateralAgreement!.Version);
        snapshot.Version.Should().Be(0, "the store assigns the version");
        snapshot.PayloadSha256.Should().BeEmpty("the store assigns the digest");
    }

    [Fact]
    public void CaseG_AnUnvaluedContract_LeavesTheSnapshotIncomplete_WithExposuresButNoMovement()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        var snapshot = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m), Unvalued(view.Contracts[1])], Balances(400_000m), AsOf, "casey", DateTimeOffset.UtcNow);

        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        snapshot.NetExposure.Should().Be(125_000m, "exposure is reported for what was valued");
        snapshot.GrossPositive.Should().Be(125_000m);
        snapshot.GrossNegative.Should().Be(0m);
        snapshot.Movements.Should().BeEmpty();
        snapshot.FundAsSecuredParty.Movement.Should().BeNull();
        snapshot.CounterpartyAsSecuredParty.Movement.Should().BeNull();
        snapshot.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.MissingInput && b.Reason.Contains(SellId.ToString("D")));
        snapshot.Contracts.Should().HaveCount(2);
        snapshot.Contracts[1].Status.Should().Be(OtcValuationStatus.Unvalued);
        snapshot.Contracts[1].MarkToMarket.Should().BeNull();
    }

    [Fact]
    public void CaseH_AReturnThatRoundsToZero_IsNoMovement()
    {
        OtcExposureRules.Movement(-100_000m, 100_000m, 100_000m, 150_000m, OtcMovementParty.Fund, OtcMovementParty.Counterparty, "USD").Should().BeNull();
        OtcExposureRules.RoundDown(100_000m, 150_000m).Should().Be(0m);

        // The same return with a smaller rounding amount is a movement by the Secured Party.
        var movement = OtcExposureRules.Movement(-100_000m, 100_000m, 100_000m, 10_000m, OtcMovementParty.Fund, OtcMovementParty.Counterparty, "USD");
        movement.Should().BeEquivalentTo(new OtcCollateralMovementDto(OtcMovementKind.Return, OtcMovementParty.Counterparty, 100_000m, "USD"));
    }

    [Fact]
    public void MinimumTransfer_IsTestedOnTheUnroundedAmount_AndADeliveryIsMovedByThePledgor()
    {
        OtcExposureRules.Movement(99_999m, -99_999m, 100_000m, 10_000m, OtcMovementParty.Fund, OtcMovementParty.Counterparty, "USD").Should().BeNull("99,999 would round up to 100,000 but the MTA test comes first");
        OtcExposureRules.Movement(100_000m, -100_000m, 100_000m, 10_000m, OtcMovementParty.Fund, OtcMovementParty.Counterparty, "USD")
            .Should().BeEquivalentTo(new OtcCollateralMovementDto(OtcMovementKind.Delivery, OtcMovementParty.Fund, 100_000m, "USD"));
        OtcExposureRules.Movement(0m, 0m, 0m, null, OtcMovementParty.Fund, OtcMovementParty.Counterparty, "USD").Should().BeNull("nothing to move");
    }

    [Fact]
    public void Pins_Haircut_Rounding_And_CreditSupport()
    {
        OtcExposureRules.HaircutAdjusted(200_250m, 2m).Should().Be(196_245m);
        OtcExposureRules.HaircutAdjusted(400_000m, 0m).Should().Be(400_000m);
        OtcExposureRules.RoundUp(328_755m, 10_000m).Should().Be(330_000m);
        OtcExposureRules.RoundDown(871_245m, 10_000m).Should().Be(870_000m);
        OtcExposureRules.RoundUp(328_755m, null).Should().Be(328_755m);
        OtcExposureRules.RoundDown(871_245m, null).Should().Be(871_245m);
        OtcExposureRules.RoundUp(330_000m, 10_000m).Should().Be(330_000m, "an exact multiple is unchanged");
        OtcExposureRules.CreditSupportAmount(-75_000m, 1_000_000m, 0m, 0m).Should().Be(925_000m);
        OtcExposureRules.CreditSupportAmount(75_000m, 0m, 1_000_000m, 250_000m).Should().Be(0m, "floored at zero");
    }

    [Theory]
    [InlineData(OtcMasterAgreementType.Isda1992, true)]
    [InlineData(OtcMasterAgreementType.Isda2002, true)]
    [InlineData(OtcMasterAgreementType.Gmra, true)]
    [InlineData(OtcMasterAgreementType.Gmsla, true)]
    [InlineData(OtcMasterAgreementType.Other, false)]
    public void OnlyTheNamedMasterForms_PermitCloseOutNetting(OtcMasterAgreementType type, bool nets)
        => OtcExposureRules.PermitsCloseOutNetting(type).Should().Be(nets);

    [Fact]
    public void ContractSelection_ExcludesClosedAndUntradedContracts_AndIncludesPendingOnes()
    {
        var pending = Contract(Guid.NewGuid(), OtcContractDirection.Buy, 1m, OtcContractStatus.Pending, Trade, Trade.AddYears(5));
        var terminated = pending with { ContractId = Guid.NewGuid(), Status = OtcContractStatus.Terminated };
        var novated = pending with { ContractId = Guid.NewGuid(), Status = OtcContractStatus.Novated };
        var matured = pending with { ContractId = Guid.NewGuid(), Status = OtcContractStatus.Matured };
        var future = pending with { ContractId = Guid.NewGuid(), TradeDate = AsOf.AddDays(1), EffectiveDate = AsOf.AddDays(1) };

        OtcExposureRules.SelectContracts([pending, terminated, novated, matured, future], AsOf)
            .Select(c => c.ContractId).Should().Equal(pending.ContractId);
    }

    [Fact]
    public void AnOpenContractPastItsTermination_IsABlocker()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        var stale = view.Contracts[1] with { TerminationDate = AsOf.AddDays(-1) };
        view = view with { Contracts = [view.Contracts[0], stale] };

        var snapshot = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m), Valued(stale, 1.25m)], Balances(400_000m), AsOf, "casey", DateTimeOffset.UtcNow);

        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        snapshot.NetExposure.Should().Be(75_000m, "the contract is still valued; the operator decides whether to close it");
        snapshot.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.PastTermination && b.Reason.Contains(stale.ContractId.ToString("D")));
        snapshot.Movements.Should().BeEmpty();
    }

    [Fact]
    public void WithoutACollateralAgreement_ExposuresAreReported_AndNoCreditSupportIsComputed()
    {
        var view = View(OtcMasterAgreementType.Isda2002) with { CollateralAgreement = null };
        view = view with { NettingSet = view.NettingSet with { CollateralAgreementId = null } };

        var snapshot = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m), Valued(view.Contracts[1], 1.25m)], Balances(400_000m), AsOf, "casey", DateTimeOffset.UtcNow);

        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        snapshot.Currency.Should().Be("USD", "the first contract's currency");
        snapshot.CollateralAgreementId.Should().BeNull();
        snapshot.CollateralAgreementVersion.Should().BeNull();
        snapshot.NetExposure.Should().Be(75_000m);
        snapshot.CollateralHeld.Should().BeEmpty("balances cannot belong to a missing agreement");
        snapshot.Movements.Should().BeEmpty();
        snapshot.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.NoCollateralAgreement);
    }

    [Fact]
    public void ATerminatedCollateralAgreement_IsABlocker()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        view = view with { CollateralAgreement = view.CollateralAgreement! with { TerminationDate = AsOf.AddDays(-1) } };

        var snapshot = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m), Valued(view.Contracts[1], 1.25m)], Balances(400_000m), AsOf, "casey", DateTimeOffset.UtcNow);

        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        snapshot.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.CollateralAgreementTerminated);
        snapshot.Movements.Should().BeEmpty();
    }

    [Fact]
    public void Balances_EnterOnlyWhenValuedByTheDate_Positive_AndUnderTheAgreement()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        var balances = Balances(400_000m);
        var late = balances[0] with { BalanceId = Guid.NewGuid(), ValuedAsOf = AsOf.AddDays(1) };
        var returned = balances[0] with { BalanceId = Guid.NewGuid(), Quantity = 0m, ValuedAmount = 0m };
        var elsewhere = balances[0] with { BalanceId = Guid.NewGuid(), CollateralAgreementId = Guid.NewGuid() };

        var snapshot = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m), Valued(view.Contracts[1], 1.25m)], [.. balances, late, returned, elsewhere], AsOf, "casey", DateTimeOffset.UtcNow);

        snapshot.CollateralHeld.Select(line => line.BalanceId).Should().BeEquivalentTo(balances.Select(b => b.BalanceId));
        snapshot.CounterpartyAsSecuredParty.Held.Should().Be(596_245m);
    }

    [Fact]
    public void AValuationInAnotherCurrency_OrMissing_OrWithoutAMark_IsUnvalued()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        var euro = Valued(view.Contracts[0], 1.25m) with { Currency = "EUR" };
        var markless = Valued(view.Contracts[1], 1.25m) with { MarkToMarket = null };

        var snapshot = OtcExposureRules.Compute(view, [euro, markless], Balances(400_000m), AsOf, "casey", DateTimeOffset.UtcNow);
        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        snapshot.Contracts.Should().OnlyContain(c => c.Status == OtcValuationStatus.Unvalued && c.MarkToMarket == null);
        snapshot.Blockers.Select(b => b.Code).Should().BeEquivalentTo(OtcValuationBlockerCodes.CurrencyMismatch, OtcValuationBlockerCodes.MissingInput);

        var missing = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m)], Balances(400_000m), AsOf, "casey", DateTimeOffset.UtcNow);
        missing.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        missing.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.MissingValuation);
    }

    [Fact]
    public void Provenance_FoldsToTheStrongestInput_AndTheDigestIsStable()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        var simulated = Valued(view.Contracts[1], 1.25m) with { Provenance = DataProvenance.Simulated };
        var computedAt = new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero);

        var snapshot = OtcExposureRules.Compute(view, [Valued(view.Contracts[0], 1.25m), simulated], Balances(400_000m), AsOf, "casey", computedAt);
        snapshot.Provenance.Should().Be(DataProvenance.Simulated, "a snapshot is never more real than its least-real input");
        snapshot.Status.Should().Be(OtcExposureSnapshotStatus.Complete, "provenance is reported, not hidden; issuing a call is what refuses it");

        var versioned = snapshot with { Version = 3 };
        var digest = OtcExposureRules.Digest(versioned);
        digest.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        OtcExposureRules.Digest(versioned with { PayloadSha256 = digest }).Should().Be(digest, "the digest field is blanked before hashing");
        OtcExposureRules.Digest(versioned with { Version = 4 }).Should().NotBe(digest);
        OtcExposureRules.SerializePayload(versioned with { PayloadSha256 = digest }).Should().Contain("\"PayloadSha256\":\"\"");
    }

    [Fact]
    public void Compute_RequiresAnActor_AndACurrency()
    {
        var view = View(OtcMasterAgreementType.Isda2002);
        FluentActions.Invoking(() => OtcExposureRules.Compute(view, [], [], AsOf, " ", DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*who computed it*");

        var empty = view with { CollateralAgreement = null, Contracts = [] };
        FluentActions.Invoking(() => OtcExposureRules.ResolveCurrency(empty))
            .Should().Throw<ArgumentException>().WithMessage("*no currency*");
        OtcExposureRules.ResolveCurrency(view).Should().Be("USD");
    }

    private static string? Describe(OtcCollateralMovementDto? movement)
        => movement is null ? null : $"{movement.Kind}:{movement.Mover}:{movement.Amount:0}";

    private static OtcNettingSetViewDto View(OtcMasterAgreementType masterType)
    {
        var now = DateTimeOffset.UtcNow;
        var master = new OtcMasterAgreementDto(MasterId, "Big Bank", null, "Meridian Master Fund", null, masterType, new DateOnly(2024, 1, 15), "New York", null, 1, "casey", now);
        var csa = new OtcCollateralAgreementDto(
            CsaId, MasterId, "USD", 0m, 250_000m, 100_000m, 1_000_000m, 10_000m, "Daily",
            [new OtcEligibleCollateralDto("USD cash", null, 0m), new OtcEligibleCollateralDto("US Treasuries", "Bond", 2m)],
            new DateOnly(2024, 1, 15), null, null, 2, "casey", now);
        var nettingSet = new OtcNettingSetDto(NettingSetId, MasterId, CsaId, "Big Bank ISDA", null, 1, "casey", now);
        return new OtcNettingSetViewDto(nettingSet, master, csa,
        [
            Contract(BuyId, OtcContractDirection.Buy, 10_000_000m, OtcContractStatus.Pending, Trade, Trade.AddYears(5)),
            Contract(SellId, OtcContractDirection.Sell, 4_000_000m, OtcContractStatus.Confirmed, Trade.AddDays(1), Trade.AddYears(5))
        ]);
    }

    private static OtcContractDto Contract(Guid id, OtcContractDirection direction, decimal notional, OtcContractStatus status, DateOnly trade, DateOnly? termination)
        => new(id, SwapId, NettingSetId, null, direction, notional, "USD", trade, trade.AddDays(2), termination, status, null, null, 1, "casey", DateTimeOffset.UtcNow);

    private static OtcCollateralBalanceDto[] Balances(decimal cashPosted) =>
    [
        new(Guid.NewGuid(), CsaId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", cashPosted, 1m, cashPosted, 0m, AsOf.AddDays(-1), null, null, 1, "casey", DateTimeOffset.UtcNow),
        new(Guid.NewGuid(), CsaId, OtcCollateralDirection.Posted, OtcCollateralKind.Security, "US Treasuries", Guid.NewGuid(), "USD", 2_000m, 100.125m, 200_250m, 2m, AsOf.AddDays(-1), null, null, 1, "casey", DateTimeOffset.UtcNow)
    ];

    private static OtcContractValuationDto Valued(OtcContractDto contract, decimal pricePercent, DataProvenance provenance = DataProvenance.Real)
        => new(contract.ContractId, contract.SecurityId, AsOf, OtcValuationStatus.Valued,
            (contract.Direction == OtcContractDirection.Buy ? 1m : -1m) * contract.Notional * pricePercent / 100m,
            "USD", "test", [], [], provenance);

    private static OtcContractValuationDto Unvalued(OtcContractDto contract)
        => new(contract.ContractId, contract.SecurityId, AsOf, OtcValuationStatus.Unvalued, null, "USD", "test", [],
            [new OtcValuationBlockerDto(OtcValuationBlockerCodes.MissingInput, "no golden-copy price")], DataProvenance.Real);
}
