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
    private static readonly Guid BuyId = Guid.NewGuid();
    private static readonly Guid SellId = Guid.NewGuid();
    private static readonly Guid CashId = Guid.NewGuid();
    private static readonly Guid TreasuriesId = Guid.NewGuid();
    private static readonly Guid SwapId = Guid.NewGuid();
    private const string Recompute = "Recompute the snapshot before issuing a call.";

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
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request() with { IssuedOn = AsOf.AddDays(1) }, [], new DateTimeOffset(2026, 3, 30, 23, 0, 0, TimeSpan.Zero)))
            .Should().Throw<ArgumentException>("a snapshot dated after the current UTC date is not yet a valuation, even for a forward-dated call")
            .WithMessage($"Snapshot {snapshot.SnapshotId:D} values 2026-03-31, after the current date 2026-03-30 (UTC)*");
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
        FluentActions.Invoking(() => OtcMarginCallRules.Issue(snapshot, Request(), [open with { Status = OtcMarginCallStatus.Settled }], DateTimeOffset.UtcNow))
            .Should().Throw<ArgumentException>("the snapshot's movement was already called and settled; a retry must not demand it twice")
            .WithMessage($"*{open.CallId:D}*Settled*recompute the snapshot*");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { Status = OtcMarginCallStatus.Cancelled }], DateTimeOffset.UtcNow).Should().NotBeNull("a cancelled call leaves the snapshot's movement uncalled");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { Status = OtcMarginCallStatus.Settled, SnapshotId = Guid.NewGuid() }], DateTimeOffset.UtcNow).Should().NotBeNull("a settled call from an earlier snapshot is closed");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { Mover = OtcMovementParty.Counterparty }], DateTimeOffset.UtcNow).Should().NotBeNull("the other mover's call does not block");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { Kind = OtcMovementKind.Return }], DateTimeOffset.UtcNow).Should().NotBeNull("the same mover's open return is a distinct obligation and does not block its delivery");
        OtcMarginCallRules.Issue(snapshot, Request(), [open with { NettingSetId = Guid.NewGuid(), SnapshotId = Guid.NewGuid() }], DateTimeOffset.UtcNow).Should().NotBeNull("another netting set's call, issued from its own snapshot, does not block");
    }

    [Fact]
    public void UnchangedInputs_AreAccepted_InAnyOrder()
    {
        var snapshot = Snapshot(OtcExposureSnapshotStatus.Complete, DataProvenance.Real);
        var recorded = snapshot.Inputs!;

        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot, recorded with { }, []))
            .Should().NotThrow();
        var reordered = recorded with
        {
            Contracts = recorded.Contracts.Reverse().ToArray(),
            CollateralBalances = recorded.CollateralBalances.Reverse().ToArray()
        };
        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot, reordered, []))
            .Should().NotThrow("the store reads the same records in its own order");
    }

    [Fact]
    public void ChangedInputs_AreRefused_NamingEachChange()
    {
        var snapshot = Snapshot(OtcExposureSnapshotStatus.Complete, DataProvenance.Real);
        var recorded = snapshot.Inputs!;
        var added = Guid.NewGuid();
        var other = Guid.NewGuid();
        void Refused(OtcExposureSnapshotInputsDto current, string expected, string because)
            => FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot, current, []))
                .Should().Throw<ArgumentException>(because)
                .WithMessage($"Exposure snapshot {snapshot.SnapshotId:D} was computed from records that have changed since: {expected}. {Recompute}");

        Refused(recorded with { NettingSetVersion = 2 },
            $"netting set {NettingSetId:D} was amended (version 1 to 2)", "a netting set amendment can move contracts or terms");
        Refused(recorded with { Contracts = [.. recorded.Contracts, new OtcExposureInputVersionDto(added, 1)] },
            $"contract(s) added: {added:D}", "a contract booked into the netting set changes the exposure");
        Refused(recorded with { Contracts = recorded.Contracts.Where(i => i.Id != SellId).ToArray() },
            $"contract(s) removed: {SellId:D}", "a contract moved out of the netting set changes the exposure");
        Refused(recorded with { Contracts = recorded.Contracts.Select(i => i.Id == BuyId ? i with { Version = 2 } : i).ToArray() },
            $"contract(s) amended: {BuyId:D} (version 1 to 2)", "an amended notional, status or trade date changes the exposure");
        Refused(recorded with { CollateralBalances = [.. recorded.CollateralBalances, new OtcExposureInputVersionDto(added, 1)] },
            $"collateral balance(s) recorded: {added:D}", "collateral recorded after the computation changes what is held");
        Refused(recorded with { CollateralBalances = recorded.CollateralBalances.Select(i => i.Id == CashId ? i with { Version = 3 } : i).ToArray() },
            $"collateral balance(s) amended: {CashId:D} (version 2 to 3)", "a returned or revalued balance changes what is held");
        Refused(recorded with { CollateralBalances = recorded.CollateralBalances.Where(i => i.Id != TreasuriesId).ToArray() },
            $"collateral balance(s) removed: {TreasuriesId:D}", "a balance no longer recorded changes what is held");
        Refused(recorded with { NettingSetsUnderAgreement = [NettingSetId, other] },
            $"netting set(s) now referencing collateral agreement {CsaId:D}: {other:D}", "a second netting set would be credited the same collateral");
        Refused(recorded with { NettingSetsUnderAgreement = [] },
            $"netting set(s) no longer referencing collateral agreement {CsaId:D}: {NettingSetId:D}", "the agreement no longer secures this netting set");

        var both = new[] { added, other }.Order().Select(id => id.ToString("D")).ToArray();
        var several = recorded with
        {
            NettingSetVersion = 2,
            Contracts = [.. recorded.Contracts, new OtcExposureInputVersionDto(added, 1), new OtcExposureInputVersionDto(other, 1)],
            CollateralBalances = recorded.CollateralBalances.Select(i => i with { Version = i.Version + 1 }).ToArray()
        };
        Refused(several,
            $"netting set {NettingSetId:D} was amended (version 1 to 2); contract(s) added: {both[0]}, {both[1]}; " +
            $"collateral balance(s) amended: {string.Join(", ", recorded.CollateralBalances.Select(i => $"{i.Id:D} (version {i.Version} to {i.Version + 1})"))}",
            "every change is named, in a stable order");
    }

    [Fact]
    public void ASnapshotWithoutRecordedInputs_IsRefused()
    {
        var snapshot = Snapshot(OtcExposureSnapshotStatus.Complete, DataProvenance.Real);

        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot with { Inputs = null }, snapshot.Inputs!, []))
            .Should().Throw<ArgumentException>("a payload stored without its inputs cannot be shown to be current")
            .WithMessage($"Exposure snapshot {snapshot.SnapshotId:D} does not record the versions of the records it was computed from*{Recompute}");
        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot with { Inputs = snapshot.Inputs! with { CollateralBalances = null! } }, snapshot.Inputs!, []))
            .Should().Throw<ArgumentException>().WithMessage($"*does not record the versions*{Recompute}");
        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot with { Inputs = snapshot.Inputs! with { KnowledgeAsOf = default } }, snapshot.Inputs!, []))
            .Should().Throw<ArgumentException>("without a knowledge time no later market data can be told apart").WithMessage($"*knowledge time of its market data*{Recompute}");
        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot with { Inputs = snapshot.Inputs! with { Securities = null } }, snapshot.Inputs!, []))
            .Should().Throw<ArgumentException>("without the valued securities their market data cannot be checked").WithMessage($"*does not record the versions*{Recompute}");
    }

    [Fact]
    public void MarketDataRecordedAfterTheKnowledgeTime_IsRefused_NamingEachRecord()
    {
        var snapshot = Snapshot(OtcExposureSnapshotStatus.Complete, DataProvenance.Real);
        var knownAt = snapshot.ComputedAt.UtcDateTime.ToString("O");
        var priceAsOf = new DateTimeOffset(2026, 3, 31, 16, 0, 0, TimeSpan.Zero);
        var later = snapshot.ComputedAt.AddMinutes(5);
        var first = new Guid("00000000-0000-0000-0000-00000000000a");
        var second = new Guid("00000000-0000-0000-0000-00000000000b");
        OtcMarketDataRecordDto[] recorded =
        [
            new(second, "price from vendor-b", priceAsOf, later),
            new(first, "pricing hierarchy", priceAsOf.AddDays(-1), later.AddMinutes(1)),
            new(first, "price from vendor-a", priceAsOf, later)
        ];
        static string Named(OtcMarketDataRecordDto r) => $"security {r.SecurityId:D} {r.Record} effective {r.EffectiveAt.UtcDateTime:O} (recorded {r.RecordedAt.UtcDateTime:O})";

        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot, snapshot.Inputs!, recorded))
            .Should().Throw<ArgumentException>("a corrected or late price, or a changed hierarchy, may change what a recomputation selects")
            .WithMessage($"Exposure snapshot {snapshot.SnapshotId:D} was computed from records that have changed since: market data recorded after {knownAt}: " +
                $"{Named(recorded[2])}, {Named(recorded[1])}, {Named(recorded[0])}. {Recompute}", "each record is named by security, then by when it was recorded");
        OtcMarginCallRules.MarketDataKnownAt(snapshot).Should().Be(snapshot.ComputedAt);
        OtcMarginCallRules.MarketDataKnownAt(snapshot with { Inputs = snapshot.Inputs! with { KnowledgeAsOf = snapshot.ComputedAt.AddHours(1) } })
            .Should().Be(snapshot.ComputedAt, "with a knowledge time after the computation, what was recorded meanwhile may or may not have been read");
    }

    [Fact]
    public void ASnapshotValuedAtAnEarlierKnowledgeTime_IsAReplay_AndIsRefused()
    {
        var snapshot = Snapshot(OtcExposureSnapshotStatus.Complete, DataProvenance.Real);
        var replay = snapshot with { Inputs = snapshot.Inputs! with { KnowledgeAsOf = snapshot.ComputedAt.AddHours(-15) } };

        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(replay, snapshot.Inputs!, []))
            .Should().Throw<ArgumentException>("a replay of what was known yesterday evening is evidence, not a live call")
            .WithMessage($"Exposure snapshot {snapshot.SnapshotId:D} was valued with market data as known at {replay.Inputs!.KnowledgeAsOf.UtcDateTime:O}, before it was computed at {snapshot.ComputedAt.UtcDateTime:O}: it replays what was known then*Recompute the snapshot without a knowledge time before issuing a call.");
        OtcMarginCallRules.MarketDataKnownAt(replay).Should().Be(replay.Inputs.KnowledgeAsOf);
        FluentActions.Invoking(() => OtcMarginCallRules.RequireUnchangedInputs(snapshot with { Inputs = snapshot.Inputs! with { KnowledgeAsOf = snapshot.ComputedAt.AddHours(1) } }, snapshot.Inputs!, []))
            .Should().NotThrow("a knowledge time after the computation reads at least what the computation could");
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
        var computedAt = DateTimeOffset.UtcNow;
        var inputs = new OtcExposureSnapshotInputsDto(
            1,
            new[] { new OtcExposureInputVersionDto(BuyId, 1), new OtcExposureInputVersionDto(SellId, 1) }.OrderBy(i => i.Id).ToArray(),
            new[] { new OtcExposureInputVersionDto(CashId, 2), new OtcExposureInputVersionDto(TreasuriesId, 1) }.OrderBy(i => i.Id).ToArray(),
            [NettingSetId],
            computedAt,
            [SwapId]);
        return new OtcExposureSnapshotDto(
            Guid.NewGuid(), NettingSetId, Guid.NewGuid(), CsaId, AsOf, 1, OtcExposureRules.RuleVersion, "USD",
            OtcNettingBasis.CloseOutNet, status, provenance, 1, 2, [], 125_000m, 50_000m, 75_000m, fund, counterparty, [], [movement], [],
            inputs, new string('0', 64), "casey", computedAt);
    }

    private static OtcMarginCallDto Call(OtcMarginCallStatus status)
        => new(Guid.NewGuid(), NettingSetId, CsaId, Guid.NewGuid(), OtcMovementKind.Delivery, OtcMovementParty.Fund, "USD", 330_000m,
            status is OtcMarginCallStatus.Agreed or OtcMarginCallStatus.Settled ? 330_000m : null, status, AsOf, AsOf.AddDays(1),
            status == OtcMarginCallStatus.Disputed ? "valuation differs" : null,
            status == OtcMarginCallStatus.Settled ? AsOf.AddDays(1) : null,
            status == OtcMarginCallStatus.Settled ? 330_000m : null,
            null, 3, "casey", DateTimeOffset.UtcNow.AddHours(-1));
}
