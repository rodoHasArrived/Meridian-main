using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>The collateral balance and margin call rules every store applies before touching a table.</summary>
[Trait("Category", "Unit")]
public sealed class OtcCollateralRulesTests
{
    private static readonly DateOnly AsOf = new(2026, 3, 31);
    private static readonly Guid CsaId = Guid.NewGuid();
    private static readonly Guid NettingSetId = Guid.NewGuid();

    [Fact]
    public void Balance_IsNormalized_AndTheActorIsRequired()
    {
        var normalized = OtcCollateralRules.Normalize(new UpsertOtcCollateralBalanceRequest(
            null, CsaId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "  USD cash ", null, "usd", 400_000m, 1m, AsOf, " ref-1 ", " ", " casey "));

        normalized.EligibleCollateralDescription.Should().Be("USD cash");
        normalized.Currency.Should().Be("USD");
        normalized.Reference.Should().Be("ref-1");
        normalized.Notes.Should().BeNull();
        normalized.UpdatedBy.Should().Be("casey");

        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { UpdatedBy = null }))
            .Should().Throw<ArgumentException>().WithMessage("*names the operator*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { CollateralAgreementId = Guid.Empty }))
            .Should().Throw<ArgumentException>().WithMessage("*collateral agreement*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { BalanceId = Guid.Empty }))
            .Should().Throw<ArgumentException>().WithMessage("*identified*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { EligibleCollateralDescription = " " }))
            .Should().Throw<ArgumentException>().WithMessage("*eligible collateral*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { Currency = "US" }))
            .Should().Throw<ArgumentException>().WithMessage("*three-letter*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { ValuedAsOf = default }))
            .Should().Throw<ArgumentException>("an omitted valuation date would otherwise count the balance as held on every date")
            .WithMessage("*date its valuation is good for*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { BalanceId = Guid.NewGuid(), ValuedAsOf = default }))
            .Should().Throw<ArgumentException>().WithMessage("*date its valuation is good for*");
    }

    [Fact]
    public void Balance_Quantity_IsPositiveOnRecord_AndNonNegativeOnAmend()
    {
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { Quantity = 0m }))
            .Should().Throw<ArgumentException>().WithMessage("*must be positive*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { Quantity = -1m }))
            .Should().Throw<ArgumentException>().WithMessage("*must be positive*");

        var amend = Valid() with { BalanceId = Guid.NewGuid() };
        OtcCollateralRules.Normalize(amend with { Quantity = 0m }).Quantity.Should().Be(0m, "zero records a fully returned balance");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(amend with { Quantity = -1m }))
            .Should().Throw<ArgumentException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public void Balance_UnitValue_IsPositive_AndCashIsValuedAtOne_WithoutASecurity()
    {
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { UnitValue = 0m }))
            .Should().Throw<ArgumentException>().WithMessage("*unit value must be positive*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { UnitValue = 1.01m }))
            .Should().Throw<ArgumentException>().WithMessage("*Cash collateral is valued at one*");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(Valid() with { SecurityId = Guid.NewGuid() }))
            .Should().Throw<ArgumentException>().WithMessage("*Cash collateral does not name a security*");

        var security = Valid() with { Kind = OtcCollateralKind.Security, EligibleCollateralDescription = "US Treasuries", Quantity = 2_000m, UnitValue = 100.125m, SecurityId = Guid.NewGuid() };
        OtcCollateralRules.Normalize(security).UnitValue.Should().Be(100.125m);
        OtcCollateralRules.Normalize(security with { SecurityId = null }).SecurityId.Should().BeNull("a security line need not name a Security Master record");
        FluentActions.Invoking(() => OtcCollateralRules.Normalize(security with { SecurityId = Guid.Empty }))
            .Should().Throw<ArgumentException>().WithMessage("*must be identified*");
        OtcCollateralRules.ValuedAmount(2_000m, 100.125m).Should().Be(200_250m);
    }

    [Fact]
    public void EligibleCollateral_IsResolvedByDescriptionRegardlessOfCase_AndCarriesTheAgreementsHaircut()
    {
        var csa = Csa();

        var treasuries = OtcCollateralRules.ResolveEligible(csa, " us treasuries ");
        treasuries.Description.Should().Be("US Treasuries", "the agreement's spelling wins");
        treasuries.HaircutPercent.Should().Be(2m, "the haircut is copied from the agreement, never supplied");

        FluentActions.Invoking(() => OtcCollateralRules.ResolveEligible(csa, "Equities"))
            .Should().Throw<ArgumentException>().WithMessage("*'Equities' is not eligible*");
        FluentActions.Invoking(() => OtcCollateralRules.ResolveEligible(csa with { EligibleCollateral = [] }, "USD cash"))
            .Should().Throw<ArgumentException>().WithMessage("*not eligible*", "an agreement with no eligible lines refuses every posting");
        FluentActions.Invoking(() => OtcCollateralRules.ResolveEligible(csa, " "))
            .Should().Throw<ArgumentException>();

        OtcCollateralRules.FindEligible(csa, "US TREASURIES")!.HaircutPercent.Should().Be(2m);
        OtcCollateralRules.FindEligible(csa, "Equities").Should().BeNull("a lookup that misses returns nothing rather than throwing");
        OtcCollateralRules.FindEligible(csa, " ").Should().BeNull();
    }

    [Fact]
    public void Issue_CopiesTheMovement_FromACompleteRealSnapshot()
    {
        var snapshot = Snapshot(OtcExposureSnapshotStatus.Complete, DataProvenance.Real);
        var request = new IssueOtcMarginCallRequest(snapshot.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, AsOf.AddDays(1), " first call ", "casey");

        var call = OtcMarginCallRules.Issue(snapshot, request, [], new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero));

        call.CallId.Should().NotBeEmpty();
        call.NettingSetId.Should().Be(NettingSetId);
        call.CollateralAgreementId.Should().Be(CsaId);
        call.SnapshotId.Should().Be(snapshot.SnapshotId);
        call.Kind.Should().Be(OtcMovementKind.Delivery);
        call.Mover.Should().Be(OtcMovementParty.Fund);
        call.AmountCalled.Should().Be(330_000m, "the amount is copied from the snapshot, never typed");
        call.Currency.Should().Be("USD");
        call.AmountAgreed.Should().BeNull();
        call.Status.Should().Be(OtcMarginCallStatus.Issued);
        call.IssuedOn.Should().Be(AsOf);
        call.DueOn.Should().Be(AsOf.AddDays(1));
        call.Notes.Should().Be("first call");
        call.Version.Should().Be(1);
        call.UpdatedBy.Should().Be("casey");
    }

    [Fact]
    public void Issue_Refuses_IncompleteOrNonRealSnapshots_MissingMovements_BadDates_AndASecondOpenCallForTheMover()
    {
        var snapshot = Snapshot(OtcExposureSnapshotStatus.Complete, DataProvenance.Real);
        IssueOtcMarginCallRequest Request() => new(snapshot.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey");

        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot with { Status = OtcExposureSnapshotStatus.Incomplete }, Request(), [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*Incomplete*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot with { Provenance = DataProvenance.Simulated }, Request(), [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*simulated*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request() with { Kind = OtcMovementKind.Return }, [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*computes no Return by the Fund*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request() with { Mover = OtcMovementParty.Counterparty }, [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*computes no Delivery by the Counterparty*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request() with { IssuedOn = AsOf.AddDays(-1) }, [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*before the date its snapshot values*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request() with { DueOn = AsOf.AddDays(-1) }, [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*fall due before*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request() with { SnapshotId = Guid.NewGuid() }, [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*different snapshot*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request() with { UpdatedBy = " " }, [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*names the operator*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot with { CollateralAgreementId = null }, Request(), [], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*no collateral agreement*");

        var open = OtcMarginCallRules.Issue(snapshot, Request(), [], DateTimeOffset.UtcNow);
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request(), [open], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage("*already has an open Delivery call for the Fund*");
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request(), [open with { Status = OtcMarginCallStatus.Agreed }], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>("an agreed call is still open");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { Status = OtcMarginCallStatus.Settled }], DateTimeOffset.UtcNow).Should().NotBeNull("a settled call is closed");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { Mover = OtcMovementParty.Counterparty }], DateTimeOffset.UtcNow).Should().NotBeNull("the other mover's call does not block");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { Kind = OtcMovementKind.Return }], DateTimeOffset.UtcNow).Should().NotBeNull("the same mover's open return is a distinct obligation and does not block its delivery");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { NettingSetId = Guid.NewGuid() }], DateTimeOffset.UtcNow).Should().NotBeNull("another netting set's call does not block");
    }

    [Theory]
    [InlineData(OtcMarginCallStatus.Issued, OtcMarginCallAction.Agree, OtcMarginCallStatus.Agreed)]
    [InlineData(OtcMarginCallStatus.Issued, OtcMarginCallAction.Dispute, OtcMarginCallStatus.Disputed)]
    [InlineData(OtcMarginCallStatus.Issued, OtcMarginCallAction.Cancel, OtcMarginCallStatus.Cancelled)]
    [InlineData(OtcMarginCallStatus.Disputed, OtcMarginCallAction.Agree, OtcMarginCallStatus.Agreed)]
    [InlineData(OtcMarginCallStatus.Disputed, OtcMarginCallAction.Cancel, OtcMarginCallStatus.Cancelled)]
    [InlineData(OtcMarginCallStatus.Agreed, OtcMarginCallAction.Settle, OtcMarginCallStatus.Settled)]
    [InlineData(OtcMarginCallStatus.Agreed, OtcMarginCallAction.Cancel, OtcMarginCallStatus.Cancelled)]
    public void Transition_FollowsTheMatrix_AndBumpsTheVersion(OtcMarginCallStatus from, OtcMarginCallAction action, OtcMarginCallStatus expected)
    {
        var current = Call(from);
        var request = new TransitionOtcMarginCallRequest(current.CallId, action, null, "because", AsOf.AddDays(2), null, "casey.doyle");
        var at = new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero);

        var next = OtcMarginCallRules.Transition(current, request, at);

        next.Status.Should().Be(expected);
        next.Version.Should().Be(current.Version + 1);
        next.UpdatedBy.Should().Be("casey.doyle");
        next.UpdatedAt.Should().Be(at);
        next.CallId.Should().Be(current.CallId);
        next.AmountCalled.Should().Be(current.AmountCalled);
    }

    [Theory]
    [InlineData(OtcMarginCallStatus.Issued, OtcMarginCallAction.Settle)]
    [InlineData(OtcMarginCallStatus.Disputed, OtcMarginCallAction.Dispute)]
    [InlineData(OtcMarginCallStatus.Disputed, OtcMarginCallAction.Settle)]
    [InlineData(OtcMarginCallStatus.Agreed, OtcMarginCallAction.Agree)]
    [InlineData(OtcMarginCallStatus.Agreed, OtcMarginCallAction.Dispute)]
    [InlineData(OtcMarginCallStatus.Settled, OtcMarginCallAction.Agree)]
    [InlineData(OtcMarginCallStatus.Settled, OtcMarginCallAction.Cancel)]
    [InlineData(OtcMarginCallStatus.Cancelled, OtcMarginCallAction.Agree)]
    [InlineData(OtcMarginCallStatus.Cancelled, OtcMarginCallAction.Settle)]
    public void Transition_RefusesMovesOutsideTheMatrix(OtcMarginCallStatus from, OtcMarginCallAction action)
    {
        var current = Call(from);
        var request = new TransitionOtcMarginCallRequest(current.CallId, action, null, "because", AsOf.AddDays(2), null, "casey");

        FluentActions.Invoking(() => OtcMarginCallRules.Transition(current, request, DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>().WithMessage($"*{from}*");
    }

    [Fact]
    public void Transition_FieldRules_AgreeDefaultsToTheCalledAmount_DisputeAndCancelNeedAReason_SettleNeedsADate()
    {
        var issued = Call(OtcMarginCallStatus.Issued);
        var at = DateTimeOffset.UtcNow;

        var agreed = OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Agree, UpdatedBy: "casey"), at);
        agreed.AmountAgreed.Should().Be(330_000m);
        OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Agree, 320_000m, UpdatedBy: "casey"), at).AmountAgreed.Should().Be(320_000m);
        FluentActions.Invoking(() => OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Agree, 0m, UpdatedBy: "casey"), at))
            .Should().Throw<ArgumentException>().WithMessage("*agreed amount must be positive*");

        FluentActions.Invoking(() => OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Dispute, UpdatedBy: "casey"), at))
            .Should().Throw<ArgumentException>().WithMessage("*dispute states its reason*");
        var disputed = OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Dispute, Reason: " valuation differs ", UpdatedBy: "casey"), at);
        disputed.DisputeReason.Should().Be("valuation differs");

        FluentActions.Invoking(() => OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Cancel, UpdatedBy: "casey"), at))
            .Should().Throw<ArgumentException>().WithMessage("*cancellation states its reason*");
        var cancelled = OtcMarginCallRules.Transition(issued with { Notes = "first call" }, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Cancel, Reason: "superseded", UpdatedBy: "casey"), at);
        cancelled.Notes.Should().Be("first call; Cancelled: superseded");

        FluentActions.Invoking(() => OtcMarginCallRules.Transition(agreed, new TransitionOtcMarginCallRequest(agreed.CallId, OtcMarginCallAction.Settle, UpdatedBy: "casey"), at))
            .Should().Throw<ArgumentException>().WithMessage("*settlement states the date*");
        FluentActions.Invoking(() => OtcMarginCallRules.Transition(agreed, new TransitionOtcMarginCallRequest(agreed.CallId, OtcMarginCallAction.Settle, SettledOn: AsOf.AddDays(-1), UpdatedBy: "casey"), at))
            .Should().Throw<ArgumentException>().WithMessage("*before it was issued*");
        var settled = OtcMarginCallRules.Transition(agreed, new TransitionOtcMarginCallRequest(agreed.CallId, OtcMarginCallAction.Settle, SettledOn: AsOf.AddDays(1), UpdatedBy: "casey"), at);
        settled.SettledOn.Should().Be(AsOf.AddDays(1));
        settled.SettledAmount.Should().Be(330_000m, "defaults to the agreed amount");
        OtcMarginCallRules.Transition(agreed, new TransitionOtcMarginCallRequest(agreed.CallId, OtcMarginCallAction.Settle, 325_000m, SettledOn: AsOf.AddDays(1), UpdatedBy: "casey"), at).SettledAmount.Should().Be(325_000m);

        FluentActions.Invoking(() => OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(Guid.NewGuid(), OtcMarginCallAction.Agree, UpdatedBy: "casey"), at))
            .Should().Throw<ArgumentException>().WithMessage("*different call*");
        FluentActions.Invoking(() => OtcMarginCallRules.Transition(issued, new TransitionOtcMarginCallRequest(issued.CallId, OtcMarginCallAction.Agree), at))
            .Should().Throw<ArgumentException>().WithMessage("*names the operator*");

        OtcMarginCallRules.EventType(OtcMarginCallAction.Agree).Should().Be("MarginCallAgreed");
        OtcMarginCallRules.EventType(OtcMarginCallAction.Dispute).Should().Be("MarginCallDisputed");
        OtcMarginCallRules.EventType(OtcMarginCallAction.Settle).Should().Be("MarginCallSettled");
        OtcMarginCallRules.EventType(OtcMarginCallAction.Cancel).Should().Be("MarginCallCancelled");
    }

    [Theory]
    [InlineData(OtcMarginCallStatus.Issued, true)]
    [InlineData(OtcMarginCallStatus.Agreed, true)]
    [InlineData(OtcMarginCallStatus.Disputed, true)]
    [InlineData(OtcMarginCallStatus.Settled, false)]
    [InlineData(OtcMarginCallStatus.Cancelled, false)]
    public void OpenStatuses_AreThoseStillAwaitingSettlement(OtcMarginCallStatus status, bool open)
        => OtcMarginCallRules.IsOpen(status).Should().Be(open);

    [Fact]
    public async Task GovernanceStreamReader_RefusesABlankStream_AndAnOutOfRangePage()
    {
        var log = new PostgresSecurityGovernanceEventLog(new SecurityMasterOptions());

        await log.Invoking(l => l.GetForStreamAsync(" ")).Should().ThrowAsync<ArgumentException>();
        await log.Invoking(l => l.GetForStreamAsync(SecurityGovernanceStreams.OtcCollateral, limit: 0)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await log.Invoking(l => l.GetForStreamAsync(SecurityGovernanceStreams.OtcCollateral, limit: 5001)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        SecurityGovernanceStreams.OtcCollateral.Should().Be("OtcCollateral");
    }

    private static UpsertOtcCollateralBalanceRequest Valid()
        => new(null, CsaId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 400_000m, 1m, AsOf, null, null, "casey");

    private static OtcCollateralAgreementDto Csa()
        => new(CsaId, Guid.NewGuid(), "USD", 0m, 250_000m, 100_000m, 1_000_000m, 10_000m, "Daily",
            [new OtcEligibleCollateralDto("USD cash", null, 0m), new OtcEligibleCollateralDto("US Treasuries", "Bond", 2m)],
            new DateOnly(2024, 1, 15), null, null, 1, "casey", DateTimeOffset.UtcNow);

    private static OtcExposureSnapshotDto Snapshot(OtcExposureSnapshotStatus status, DataProvenance provenance)
    {
        var movement = new OtcCollateralMovementDto(OtcMovementKind.Delivery, OtcMovementParty.Fund, 330_000m, "USD");
        var fund = new OtcCreditSupportSideDto(OtcMovementParty.Fund, 75_000m, 0m, 1_000_000m, 250_000m, 0m, 0m, 0m, 0m, null);
        var counterparty = new OtcCreditSupportSideDto(OtcMovementParty.Counterparty, -75_000m, 1_000_000m, 0m, 0m, 925_000m, 596_245m, 328_755m, -328_755m, movement);
        return new OtcExposureSnapshotDto(
            Guid.NewGuid(), NettingSetId, Guid.NewGuid(), CsaId, AsOf, 1, OtcExposureRules.RuleVersion, "USD",
            OtcNettingBasis.CloseOutNet, status, provenance, 1, 2, [], 125_000m, 50_000m, 75_000m, fund, counterparty, [], [movement], [],
            new string('0', 64), "casey", DateTimeOffset.UtcNow);
    }

    private static OtcMarginCallDto Call(OtcMarginCallStatus status)
        => new(Guid.NewGuid(), NettingSetId, CsaId, Guid.NewGuid(), OtcMovementKind.Delivery, OtcMovementParty.Fund, "USD", 330_000m,
            status is OtcMarginCallStatus.Agreed or OtcMarginCallStatus.Settled ? 330_000m : null, status, AsOf, AsOf.AddDays(1),
            status == OtcMarginCallStatus.Disputed ? "valuation differs" : null,
            status == OtcMarginCallStatus.Settled ? AsOf.AddDays(1) : null,
            status == OtcMarginCallStatus.Settled ? 330_000m : null,
            null, 3, "casey", DateTimeOffset.UtcNow.AddHours(-1));
}
