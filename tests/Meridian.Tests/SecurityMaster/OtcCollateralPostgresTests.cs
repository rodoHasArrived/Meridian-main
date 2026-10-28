using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Otc;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Exposure snapshots, collateral balances and margin calls in PostgreSQL on the worked fixture:
/// balances are recorded with the agreement's haircut and every write appends to the governance
/// log; a computation appends a versioned, append-only snapshot whose digest is reproducible; a
/// call copies the snapshot's movement, is agreed and settled with an event on each step, and is
/// refused for a second open call, an incomplete snapshot, a superseded one, one with a later-dated
/// snapshot, one computed under collateral agreement terms that have since changed or become shared
/// with another netting set, one whose contracts, netting set or collateral balances have changed
/// since, one with market data recorded after its knowledge time, a replay, and a future-dated one,
/// which neither is computed nor shadows the latest valuation; balance and contract writes and an
/// issue wait for each other's locks.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OtcCollateralPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly DateOnly Trade = new(2026, 3, 2);
    private static readonly DateOnly AsOf = new(2026, 3, 31);
    private readonly SecurityMasterDatabaseFixture _fixture;

    public OtcCollateralPostgresTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task Balances_Snapshots_AndCalls_RoundTrip_WithGovernanceEvents()
    {
        var contracts = new PostgresOtcContractStore(_fixture.Options);
        var collateral = new PostgresOtcCollateralStore(_fixture.Options);
        var log = new PostgresSecurityGovernanceEventLog(_fixture.Options);
        var (nettingSet, csa, swapId) = await BuildFixtureAsync(contracts);
        var eventsBefore = (await log.GetForStreamAsync(SecurityGovernanceStreams.OtcCollateral, limit: 5000)).Count;
        var floor = eventsBefore == 0 ? 0 : (await log.GetForStreamAsync(SecurityGovernanceStreams.OtcCollateral, limit: 5000))[^1].GlobalSequence;

        // Balances: the haircut comes from the agreement, the description takes the agreement's spelling.
        var cash = await collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
            null, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "usd", 400_000m, 1m, AsOf.AddDays(-1), "CASH-1", null, "casey"));
        cash.Version.Should().Be(1);
        cash.HaircutPercent.Should().Be(0m);
        cash.ValuedAmount.Should().Be(400_000m);
        cash.Currency.Should().Be("USD");

        var treasuries = await collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
            null, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Security, "us treasuries", swapId, "USD", 2_000m, 100.125m, AsOf.AddDays(-1), null, null, "casey"));
        treasuries.EligibleCollateralDescription.Should().Be("US Treasuries");
        treasuries.HaircutPercent.Should().Be(2m, "copied from the agreement's eligible line");
        treasuries.ValuedAmount.Should().Be(200_250m);

        var amended = await collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
            cash.BalanceId, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 400_000m, 1m, AsOf.AddDays(-1), "CASH-1", "confirmed with custodian", "casey"));
        amended.Version.Should().Be(2);
        (await collateral.ListCollateralBalancesAsync(csa.CollateralAgreementId)).Should().HaveCount(2)
            .And.ContainSingle(b => b.BalanceId == cash.BalanceId && b.Notes == "confirmed with custodian" && b.Version == 2);

        await collateral.Invoking(s => s.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
                null, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Security, "Equities", null, "USD", 1m, 1m, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*not eligible*");
        await collateral.Invoking(s => s.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
                null, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "EUR", 1m, 1m, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*recorded in USD*");
        await collateral.Invoking(s => s.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
                null, Guid.NewGuid(), OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 1m, 1m, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*collateral agreement*does not exist*");
        await collateral.Invoking(s => s.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
                Guid.NewGuid(), csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 1m, 1m, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*collateral balance*does not exist*");
        await collateral.Invoking(s => s.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
                null, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Security, "US Treasuries", Guid.NewGuid(), "USD", 1m, 1m, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*security*does not exist*");

        var balanceEvents = await log.GetForStreamAsync(SecurityGovernanceStreams.OtcCollateral, floor);
        balanceEvents.Select(e => e.EventType).Should().Equal("CollateralBalanceRecorded", "CollateralBalanceRecorded", "CollateralBalanceAmended");
        balanceEvents.Should().OnlyContain(e => e.Actor == "casey" && e.SecurityId == null && e.Stream == SecurityGovernanceStreams.OtcCollateral);
        balanceEvents[2].Payload.GetProperty("changes").EnumerateArray().Select(c => c.GetString()).Should().Equal("notes");
        balanceEvents[2].Payload.GetProperty("previous").GetProperty("version").GetInt64().Should().Be(1);
        balanceEvents[0].Payload.GetProperty("balance").GetProperty("haircutPercent").GetDecimal().Should().Be(0m);

        // Case A through the service: Buy 10,000,000 and Sell 4,000,000 at 1.25% of notional.
        var service = new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(_ => 1.25m), NullLogger<OtcExposureService>.Instance);
        (await service.ComputeAsync(Guid.NewGuid(), AsOf, "casey")).Should().BeNull("an unknown netting set is not found, not refused");

        var first = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        first.Version.Should().Be(1);
        first.Status.Should().Be(OtcExposureSnapshotStatus.Complete);
        first.NettingBasis.Should().Be(OtcNettingBasis.CloseOutNet);
        first.Provenance.Should().Be(DataProvenance.Real);
        first.NetExposure.Should().Be(75_000m);
        first.GrossPositive.Should().Be(125_000m);
        first.GrossNegative.Should().Be(50_000m);
        first.CounterpartyAsSecuredParty!.Held.Should().Be(596_245m);
        first.Movements.Should().ContainSingle().Which.Should().BeEquivalentTo(new OtcCollateralMovementDto(OtcMovementKind.Delivery, OtcMovementParty.Fund, 330_000m, "USD"));
        first.ComputedBy.Should().Be("casey");
        first.PayloadSha256.Should().HaveLength(64);
        OtcExposureRules.Digest(first).Should().Be(first.PayloadSha256);

        var read = await collateral.GetExposureSnapshotAsync(first.SnapshotId);
        read.Should().BeEquivalentTo(first, "the payload round-trips through jsonb");
        OtcExposureRules.Digest(read!).Should().Be(first.PayloadSha256, "the digest is reproducible from the stored payload");

        var second = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        second.Version.Should().Be(2, "recomputing a date appends the next version");
        (await collateral.ListExposureSnapshotsAsync(nettingSet.NettingSetId)).Select(s => s.Version).Should().Equal(2);
        (await collateral.ListExposureSnapshotsAsync(nettingSet.NettingSetId, AsOf, latestVersionOnly: false)).Select(s => s.Version).Should().Equal(2, 1);
        (await collateral.ListExposureSnapshotsAsync(nettingSet.NettingSetId, AsOf.AddDays(1))).Should().BeEmpty();

        foreach (var mutation in new[]
                 {
                     $"update {_fixture.Options.Schema}.otc_exposure_snapshots set status = 'Complete' where snapshot_id = @id",
                     $"delete from {_fixture.Options.Schema}.otc_exposure_snapshots where snapshot_id = @id"
                 })
        {
            await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(mutation, connection);
            command.Parameters.AddWithValue("id", first.SnapshotId);
            var act = () => command.ExecuteNonQueryAsync();
            await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514", mutation);
        }

        // The call copies the movement; one open call per mover; agree then settle, each with an event.
        var call = await collateral.IssueMarginCallAsync(new IssueOtcMarginCallRequest(second.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, AsOf.AddDays(1), "day-one call", "casey"));
        call.AmountCalled.Should().Be(330_000m);
        call.Status.Should().Be(OtcMarginCallStatus.Issued);
        call.Version.Should().Be(1);
        call.CollateralAgreementId.Should().Be(csa.CollateralAgreementId);
        (await collateral.GetMarginCallAsync(call.CallId)).Should().BeEquivalentTo(call, o => o.Excluding(c => c.UpdatedAt));

        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(second.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*already has an open Delivery call for the Fund*");
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(first.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*superseded by version 2*");
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(Guid.NewGuid(), OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*snapshot*does not exist*");

        var agreed = await collateral.TransitionMarginCallAsync(new TransitionOtcMarginCallRequest(call.CallId, OtcMarginCallAction.Agree, UpdatedBy: "casey.doyle"));
        agreed.Status.Should().Be(OtcMarginCallStatus.Agreed);
        agreed.AmountAgreed.Should().Be(330_000m);
        agreed.Version.Should().Be(2);
        await collateral.Invoking(s => s.TransitionMarginCallAsync(new TransitionOtcMarginCallRequest(call.CallId, OtcMarginCallAction.Dispute, Reason: "late", UpdatedBy: "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Agreed*");
        var settled = await collateral.TransitionMarginCallAsync(new TransitionOtcMarginCallRequest(call.CallId, OtcMarginCallAction.Settle, SettledOn: AsOf.AddDays(1), UpdatedBy: "casey"));
        settled.Status.Should().Be(OtcMarginCallStatus.Settled);
        settled.SettledAmount.Should().Be(330_000m);
        settled.Version.Should().Be(3);
        await collateral.Invoking(s => s.TransitionMarginCallAsync(new TransitionOtcMarginCallRequest(call.CallId, OtcMarginCallAction.Cancel, Reason: "x", UpdatedBy: "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Settled*");
        await collateral.Invoking(s => s.TransitionMarginCallAsync(new TransitionOtcMarginCallRequest(Guid.NewGuid(), OtcMarginCallAction.Agree, UpdatedBy: "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*margin call*does not exist*");
        (await collateral.ListMarginCallsAsync(nettingSet.NettingSetId)).Should().ContainSingle().Which.Status.Should().Be(OtcMarginCallStatus.Settled);

        // A retried issue from the same snapshot after settlement would demand the movement twice: refused.
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(second.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage($"*{call.CallId:D}*Settled*recompute the snapshot*");

        // Once the call is settled, a second one for the fund needs a recomputed snapshot; an incomplete one refuses it.
        var incomplete = (await new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(c => c.Direction == OtcContractDirection.Buy ? 1.25m : null), NullLogger<OtcExposureService>.Instance)
            .ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        incomplete.Version.Should().Be(3);
        incomplete.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        incomplete.Movements.Should().BeEmpty();
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(incomplete.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Incomplete*");

        var events = await log.GetForStreamAsync(SecurityGovernanceStreams.OtcCollateral, floor);
        events.Select(e => e.EventType).Should().Equal(
            "CollateralBalanceRecorded", "CollateralBalanceRecorded", "CollateralBalanceAmended",
            "ExposureSnapshotRecorded", "ExposureSnapshotRecorded",
            "MarginCallIssued", "MarginCallAgreed", "MarginCallSettled",
            "ExposureSnapshotRecorded");
        events.Select(e => e.Actor).Should().Equal("casey", "casey", "casey", "casey", "casey", "casey", "casey.doyle", "casey", "casey");
        events.Should().OnlyContain(e => e.SecurityId == null);
        events[3].Payload.GetProperty("payloadSha256").GetString().Should().Be(first.PayloadSha256);
        events[3].Payload.GetProperty("version").GetInt32().Should().Be(1);
        events[5].Payload.GetProperty("call").GetProperty("amountCalled").GetDecimal().Should().Be(330_000m);
        events[7].Payload.GetProperty("previous").GetProperty("status").GetString().Should().Be("Agreed");
        events[7].Payload.GetProperty("call").GetProperty("status").GetString().Should().Be("Settled");

        var page = await log.GetForStreamAsync(SecurityGovernanceStreams.OtcCollateral, events[2].GlobalSequence, limit: 2);
        page.Select(e => e.GlobalSequence).Should().Equal(new[] { events[3].GlobalSequence, events[4].GlobalSequence }, "paging by sequence and limit");
    }

    [SecurityMasterDatabaseFact]
    public async Task Calls_AreRefused_UnderAmendedOrSharedTerms_AndTheSnapshotAppliesTheCurrentHaircut()
    {
        var contracts = new PostgresOtcContractStore(_fixture.Options);
        var collateral = new PostgresOtcCollateralStore(_fixture.Options);
        var (nettingSet, csa, swapId) = await BuildFixtureAsync(contracts);
        var treasuries = await PostCaseACollateralAsync(collateral, csa, swapId);
        var service = new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(_ => 1.25m), NullLogger<OtcExposureService>.Instance);
        (await collateral.ListNettingSetsUnderCollateralAgreementAsync(csa.CollateralAgreementId)).Should().Equal(nettingSet.NettingSetId);

        var underOriginalTerms = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        underOriginalTerms.Status.Should().Be(OtcExposureSnapshotStatus.Complete);
        underOriginalTerms.CollateralAgreementVersion.Should().Be(csa.Version);

        // The agreement is amended (Treasuries haircut 2% → 5%): the old snapshot can no longer be called.
        var amended = await contracts.UpsertCollateralAgreementAsync(new UpsertOtcCollateralAgreementRequest(
            csa.CollateralAgreementId, csa.MasterAgreementId, "USD", 0m, 250_000m, 100_000m, 1_000_000m, 10_000m, "Daily",
            [new OtcEligibleCollateralDto("USD cash", null, 0m), new OtcEligibleCollateralDto("US Treasuries", "Bond", 5m)],
            new DateOnly(2024, 1, 15), null, "haircut amended", "casey"));
        amended.Version.Should().Be(csa.Version + 1);
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(underOriginalTerms.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage($"*version {csa.Version}*now under*version {amended.Version}*Recompute the snapshot*");

        // Recomputed under the amended terms, the held line takes the agreement's current haircut.
        var underAmendedTerms = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        underAmendedTerms.Status.Should().Be(OtcExposureSnapshotStatus.Complete);
        underAmendedTerms.CollateralAgreementVersion.Should().Be(amended.Version);
        underAmendedTerms.CollateralHeld.Should().ContainSingle(line => line.BalanceId == treasuries.BalanceId)
            .Which.HaircutPercent.Should().Be(5m, "the balance row still carries 2%, but the snapshot applies the amended line");
        underAmendedTerms.CounterpartyAsSecuredParty!.Held.Should().Be(590_237.5m);
        underAmendedTerms.Movements.Should().ContainSingle().Which.Amount.Should().Be(340_000m);

        // The master agreement is amended (it decides netting versus gross): that snapshot can no longer be called either.
        var master = await contracts.GetMasterAgreementAsync(csa.MasterAgreementId);
        var amendedMaster = await contracts.UpsertMasterAgreementAsync(new UpsertOtcMasterAgreementRequest(
            master!.AgreementId, master.Counterparty, master.CounterpartyLei, master.OurEntity, master.OurEntityId,
            master.AgreementType, master.AgreementDate, "English", "governing law amended", "casey"));
        amendedMaster.Version.Should().Be(master.Version + 1);
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(underAmendedTerms.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage($"*master agreement*version {master.Version}*now under*version {amendedMaster.Version}*Recompute the snapshot*");
        var underAmendedMaster = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        underAmendedMaster.MasterAgreementVersion.Should().Be(amendedMaster.Version);

        // A second netting set now references the same agreement: the call is refused and a recompute is blocked.
        var sibling = await contracts.UpsertNettingSetAsync(new UpsertOtcNettingSetRequest(null, csa.MasterAgreementId, csa.CollateralAgreementId, "Big Bank ISDA (second set)", null, "casey"));
        (await collateral.ListNettingSetsUnderCollateralAgreementAsync(csa.CollateralAgreementId)).Should().BeEquivalentTo(new[] { nettingSet.NettingSetId, sibling.NettingSetId });
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(underAmendedMaster.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>().WithMessage($"*{sibling.NettingSetId:D}*not allocated across netting sets*");
        var shared = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        shared.Status.Should().Be(OtcExposureSnapshotStatus.Incomplete);
        shared.Blockers.Should().ContainSingle(b => b.Code == OtcValuationBlockerCodes.SharedCollateralAgreement);
        shared.Movements.Should().BeEmpty();
        (await collateral.ListMarginCallsAsync(nettingSet.NettingSetId)).Should().BeEmpty("every issue above was refused");

        // A netting set with no agreement reports exposures only; its null credit support sides round-trip.
        var bare = await contracts.UpsertNettingSetAsync(new UpsertOtcNettingSetRequest(null, csa.MasterAgreementId, null, "Big Bank uncollateralised", null, "casey"));
        await contracts.UpsertContractAsync(new UpsertOtcContractRequest(
            null, swapId, bare.NettingSetId, "BB-0003-" + swapId.ToString("N")[..6], OtcContractDirection.Buy, 1_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Confirmed, null, null, "casey"));
        var exposuresOnly = (await service.ComputeAsync(bare.NettingSetId, AsOf, "casey"))!;
        exposuresOnly.FundAsSecuredParty.Should().BeNull();
        exposuresOnly.CounterpartyAsSecuredParty.Should().BeNull();
        exposuresOnly.NetExposure.Should().Be(12_500m);
        var read = await collateral.GetExposureSnapshotAsync(exposuresOnly.SnapshotId);
        read.Should().BeEquivalentTo(exposuresOnly, "null sides round-trip through jsonb");
        OtcExposureRules.Digest(read!).Should().Be(exposuresOnly.PayloadSha256);
    }

    [SecurityMasterDatabaseFact]
    public async Task ACall_IsRefused_OnceCollateralIsRecordedAfterComputation_AndFollowsTheRecomputedMovement()
    {
        var contracts = new PostgresOtcContractStore(_fixture.Options);
        var collateral = new PostgresOtcCollateralStore(_fixture.Options);
        var (nettingSet, csa, _) = await BuildFixtureAsync(contracts);
        var service = new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(_ => 25m), NullLogger<OtcExposureService>.Instance);
        IssueOtcMarginCallRequest CounterpartyDelivery(OtcExposureSnapshotDto snapshot) => new(snapshot.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Counterparty, AsOf, null, null, "casey");

        // Nothing is held: net exposure 1,500,000, so the counterparty owes the fund 250,000.
        var beforeReceipt = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        beforeReceipt.Status.Should().Be(OtcExposureSnapshotStatus.Complete);
        beforeReceipt.FundAsSecuredParty!.Held.Should().Be(0m);
        beforeReceipt.Movements.Should().ContainSingle().Which.Should().BeEquivalentTo(new OtcCollateralMovementDto(OtcMovementKind.Delivery, OtcMovementParty.Counterparty, 250_000m, "USD"));
        beforeReceipt.Inputs!.CollateralBalances.Should().BeEmpty();

        // The delivery arrives and is recorded before the call goes out: the snapshot no longer stands.
        var received = await collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
            null, csa.CollateralAgreementId, OtcCollateralDirection.Received, OtcCollateralKind.Cash, "USD cash", null, "USD", 250_000m, 1m, AsOf, null, null, "casey"));
        await collateral.Invoking(s => s.IssueMarginCallAsync(CounterpartyDelivery(beforeReceipt)))
            .Should().ThrowAsync<ArgumentException>("the snapshot would otherwise demand collateral the fund already holds")
            .WithMessage($"*collateral balance(s) recorded: {received.BalanceId:D}. Recompute the snapshot before issuing a call.");

        // Recomputed, the fund holds what it was owed and there is nothing to call.
        var afterReceipt = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        afterReceipt.FundAsSecuredParty!.Held.Should().Be(250_000m);
        afterReceipt.Movements.Should().BeEmpty();
        await collateral.Invoking(s => s.IssueMarginCallAsync(CounterpartyDelivery(afterReceipt)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*computes no Delivery by the Counterparty*");

        // Part of it is returned: the amendment changes the input again, and the recomputed call is for the shortfall.
        var partlyReturned = await collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
            received.BalanceId, csa.CollateralAgreementId, OtcCollateralDirection.Received, OtcCollateralKind.Cash, "USD cash", null, "USD", 100_000m, 1m, AsOf, null, null, "casey"));
        await collateral.Invoking(s => s.IssueMarginCallAsync(CounterpartyDelivery(afterReceipt)))
            .Should().ThrowAsync<ArgumentException>().WithMessage($"*collateral balance(s) amended: {received.BalanceId:D} (version 1 to {partlyReturned.Version}). Recompute the snapshot*");
        var afterReturn = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        afterReturn.Movements.Should().ContainSingle().Which.Amount.Should().Be(150_000m);
        var call = await collateral.IssueMarginCallAsync(CounterpartyDelivery(afterReturn));
        call.AmountCalled.Should().Be(150_000m, "the call follows the movement recomputed from the balance now held");
        call.SnapshotId.Should().Be(afterReturn.SnapshotId);
        (await collateral.ListMarginCallsAsync(nettingSet.NettingSetId)).Should().ContainSingle().Which.CallId.Should().Be(call.CallId);
    }

    [SecurityMasterDatabaseFact]
    public async Task ACall_IsRefused_OnceAContractOrTheNettingSetChanges_OrALaterDateIsValued()
    {
        var contracts = new PostgresOtcContractStore(_fixture.Options);
        var collateral = new PostgresOtcCollateralStore(_fixture.Options);
        var (nettingSet, csa, swapId) = await BuildFixtureAsync(contracts);
        await PostCaseACollateralAsync(collateral, csa, swapId);
        var service = new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(_ => 1.25m), NullLogger<OtcExposureService>.Instance);
        IssueOtcMarginCallRequest FundDelivery(OtcExposureSnapshotDto snapshot) => new(snapshot.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, snapshot.AsOfDate, null, null, "casey");

        // The sold contract's notional is amended after the computation.
        var computed = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        computed.Movements.Should().ContainSingle().Which.Amount.Should().Be(330_000m);
        var sell = (await contracts.GetNettingSetAsync(nettingSet.NettingSetId))!.Contracts.Single(c => c.Direction == OtcContractDirection.Sell);
        var amendedSell = await contracts.UpsertContractAsync(new UpsertOtcContractRequest(
            sell.ContractId, sell.SecurityId, nettingSet.NettingSetId, sell.ExternalTradeId, OtcContractDirection.Sell, 5_000_000m, "USD",
            sell.TradeDate, sell.EffectiveDate, sell.TerminationDate, sell.Status, sell.ConfirmationReference, null, "casey"));
        amendedSell.Version.Should().Be(sell.Version + 1);
        await collateral.Invoking(s => s.IssueMarginCallAsync(FundDelivery(computed)))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*contract(s) amended: {sell.ContractId:D} (version {sell.Version} to {amendedSell.Version}). Recompute the snapshot before issuing a call.");

        // A contract is booked into the netting set after the recomputation.
        var afterAmendment = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        afterAmendment.NetExposure.Should().Be(62_500m);
        afterAmendment.Movements.Should().ContainSingle().Which.Amount.Should().Be(350_000m);
        var booked = await contracts.UpsertContractAsync(new UpsertOtcContractRequest(
            null, swapId, nettingSet.NettingSetId, "BB-0004-" + swapId.ToString("N")[..6], OtcContractDirection.Buy, 1_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Confirmed, null, null, "casey"));
        await collateral.Invoking(s => s.IssueMarginCallAsync(FundDelivery(afterAmendment)))
            .Should().ThrowAsync<ArgumentException>().WithMessage($"*contract(s) added: {booked.ContractId:D}. Recompute the snapshot*");

        // The netting set itself is amended after the next recomputation.
        var afterBooking = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        var renamed = await contracts.UpsertNettingSetAsync(new UpsertOtcNettingSetRequest(nettingSet.NettingSetId, nettingSet.MasterAgreementId, csa.CollateralAgreementId, "Big Bank ISDA (renamed)", null, "casey"));
        await collateral.Invoking(s => s.IssueMarginCallAsync(FundDelivery(afterBooking)))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*netting set {nettingSet.NettingSetId:D} was amended (version {nettingSet.Version} to {renamed.Version}). Recompute the snapshot*");

        // A later date is valued: the earlier snapshot is no longer the netting set's latest valuation.
        var current = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        var nextDay = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf.AddDays(1), "casey"))!;
        await collateral.Invoking(s => s.IssueMarginCallAsync(FundDelivery(current)))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*values 2026-03-31, but netting set {nettingSet.NettingSetId:D} also has snapshot {nextDay.SnapshotId:D} (version 1, Complete) for the later date 2026-04-01; a call is issued only from the netting set's latest valuation*");

        var call = await collateral.IssueMarginCallAsync(FundDelivery(nextDay));
        call.AmountCalled.Should().Be(330_000m, "net exposure is back to 75,000 with the booked contract");
        (await collateral.ListMarginCallsAsync(nettingSet.NettingSetId)).Should().ContainSingle("every earlier issue was refused");
    }

    [SecurityMasterDatabaseFact]
    public async Task ContractAndBalanceWrites_AndAnIssue_WaitForEachOther()
    {
        var contracts = new PostgresOtcContractStore(_fixture.Options);
        var collateral = new PostgresOtcCollateralStore(_fixture.Options);
        var (nettingSet, csa, swapId) = await BuildFixtureAsync(contracts);
        var service = new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(_ => 25m), NullLogger<OtcExposureService>.Instance);
        var sell = (await contracts.GetNettingSetAsync(nettingSet.NettingSetId))!.Contracts.Single(c => c.Direction == OtcContractDirection.Sell);

        // While the scopes an issue takes are held, a balance write, a new contract and an amendment wait.
        await using (var connection = new NpgsqlConnection(_fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await OtcAdvisoryLocks.LockNettingSetContractsAsync(connection, transaction, [nettingSet.NettingSetId], CancellationToken.None);
            await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.CollateralBalances(csa.CollateralAgreementId), CancellationToken.None);

            var balanceWrite = collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
                null, csa.CollateralAgreementId, OtcCollateralDirection.Received, OtcCollateralKind.Cash, "USD cash", null, "USD", 1_000m, 1m, AsOf, null, null, "casey"));
            var contractWrite = contracts.UpsertContractAsync(new UpsertOtcContractRequest(
                null, swapId, nettingSet.NettingSetId, "BB-0005-" + swapId.ToString("N")[..6], OtcContractDirection.Buy, 1_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Confirmed, null, null, "casey"));
            var amendment = contracts.UpsertContractAsync(new UpsertOtcContractRequest(
                sell.ContractId, sell.SecurityId, nettingSet.NettingSetId, sell.ExternalTradeId, OtcContractDirection.Sell, sell.Notional, "USD",
                sell.TradeDate, sell.EffectiveDate, sell.TerminationDate, sell.Status, sell.ConfirmationReference, "amended under the lock", "casey"));
            await Task.Delay(300);
            balanceWrite.IsCompleted.Should().BeFalse("a balance write under the agreement waits for the issue");
            contractWrite.IsCompleted.Should().BeFalse("a contract booked into the netting set waits for the issue");
            amendment.IsCompleted.Should().BeFalse("a contract amended in the netting set waits for the issue");

            await transaction.CommitAsync();
            await Task.WhenAll(balanceWrite, contractWrite, amendment).WaitAsync(TimeSpan.FromSeconds(30));
        }

        // While a balance write under the agreement is in flight, an issue waits for it, then sees it.
        var recomputed = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        recomputed.Status.Should().Be(OtcExposureSnapshotStatus.Complete);
        var balanceId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.CollateralBalances(csa.CollateralAgreementId), CancellationToken.None);
            await using (var insert = new NpgsqlCommand(
                $"""
                insert into {_fixture.Options.Schema}.otc_collateral_balances (
                    balance_id, collateral_agreement_id, direction, kind, eligible_collateral_description, currency,
                    quantity, unit_value, valued_amount, haircut_percent, valued_as_of, version, updated_by, updated_at)
                values (@id, @csa, 'Received', 'Cash', 'USD cash', 'USD', 5000, 1, 5000, 0, @as_of, 1, 'casey', now());
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("id", balanceId);
                insert.Parameters.AddWithValue("csa", csa.CollateralAgreementId);
                insert.Parameters.AddWithValue("as_of", AsOf);
                await insert.ExecuteNonQueryAsync();
            }

            var issue = collateral.IssueMarginCallAsync(new IssueOtcMarginCallRequest(recomputed.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Counterparty, AsOf, null, null, "casey"));
            await Task.Delay(300);
            issue.IsCompleted.Should().BeFalse("the issue waits for the balance write in flight under the agreement");

            await transaction.CommitAsync();
            await issue.Awaiting(t => t.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().ThrowAsync<ArgumentException>().WithMessage($"*collateral balance(s) recorded: {balanceId:D}. Recompute the snapshot*");
        }

        (await collateral.ListMarginCallsAsync(nettingSet.NettingSetId)).Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task ACall_IsRefused_OnceMarketDataIsRecordedAfterTheKnowledgeTime_OrFromAReplay()
    {
        var contracts = new PostgresOtcContractStore(_fixture.Options);
        var collateral = new PostgresOtcCollateralStore(_fixture.Options);
        var pricing = new PostgresSecurityMasterPricingStore(_fixture.Options);
        var (nettingSet, csa, swapId) = await BuildFixtureAsync(contracts);
        await PostCaseACollateralAsync(collateral, csa, swapId);
        var service = new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(_ => 1.25m), NullLogger<OtcExposureService>.Instance);
        IssueOtcMarginCallRequest FundDelivery(OtcExposureSnapshotDto snapshot) => new(snapshot.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey");
        var close = new DateTimeOffset(AsOf.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero);

        var computed = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        computed.Inputs!.Securities.Should().Equal(swapId);
        computed.Inputs.KnowledgeAsOf.Should().Be(computed.ComputedAt);

        // A price observed after the as-of date bears on no valuation of it; a corrected close for
        // the date, recorded after the computation, would change what a recomputation selects.
        await pricing.RecordRawPriceAsync(new RecordRawPriceRequest(swapId, "vendor-b", 1.30m, close.AddDays(1), "casey", SecurityPriceUnit.PercentOfPar));
        await pricing.RecordRawPriceAsync(new RecordRawPriceRequest(swapId, "vendor-a", 1.30m, close, "casey", SecurityPriceUnit.PercentOfPar));
        (await collateral.Invoking(s => s.IssueMarginCallAsync(FundDelivery(computed)))
                .Should().ThrowAsync<ArgumentException>("the call would otherwise go out on a superseded price")
                .WithMessage($"*market data recorded after {computed.ComputedAt.UtcDateTime:O}: security {swapId:D} price from vendor-a effective {close.UtcDateTime:O} (recorded *). Recompute the snapshot before issuing a call."))
            .Which.Message.Should().NotContain("vendor-b");

        // Recomputed, the price is part of what was known; a pricing hierarchy changed afterwards is new again.
        var repriced = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        await pricing.UpsertHierarchyAsync(new SecurityPricingHierarchyDto(swapId, null, [new PricingHierarchyEntryDto(1, "vendor-a", "Vendor A", 3)], close.AddDays(-1), "casey"));
        (await collateral.Invoking(s => s.IssueMarginCallAsync(FundDelivery(repriced)))
                .Should().ThrowAsync<ArgumentException>()
                .WithMessage($"*market data recorded after {repriced.ComputedAt.UtcDateTime:O}: security {swapId:D} pricing hierarchy effective {close.AddDays(-1).UtcDateTime:O} (recorded *). Recompute the snapshot*"))
            .Which.Message.Should().NotContain("price from", "the price recorded before this computation was known to it");

        // A replay of what was known an hour ago is appended as the latest version, but is evidence, not a call.
        var replay = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey", DateTimeOffset.UtcNow.AddHours(-1)))!;
        replay.Status.Should().Be(OtcExposureSnapshotStatus.Complete);
        replay.Inputs!.KnowledgeAsOf.Should().BeBefore(replay.ComputedAt);
        await collateral.Invoking(s => s.IssueMarginCallAsync(FundDelivery(replay)))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Exposure snapshot {replay.SnapshotId:D} was valued with market data as known at *, before it was computed at *: it replays what was known then*");

        var live = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        var call = await collateral.IssueMarginCallAsync(FundDelivery(live));
        call.AmountCalled.Should().Be(330_000m);
        (await collateral.ListMarginCallsAsync(nettingSet.NettingSetId)).Should().ContainSingle("every earlier issue was refused");
    }

    [SecurityMasterDatabaseFact]
    public async Task AFutureDate_IsNotComputed_AndAStrayFutureDatedSnapshot_NeitherShadowsTheLatestValuation_NorIsCalled()
    {
        var contracts = new PostgresOtcContractStore(_fixture.Options);
        var collateral = new PostgresOtcCollateralStore(_fixture.Options);
        var (nettingSet, csa, swapId) = await BuildFixtureAsync(contracts);
        await PostCaseACollateralAsync(collateral, csa, swapId);
        var service = new OtcExposureService(contracts, collateral, new FixedPriceValuationSource(_ => 1.25m), NullLogger<OtcExposureService>.Instance);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await service.Invoking(s => s.ComputeAsync(nettingSet.NettingSetId, today.AddMonths(1), "casey"))
            .Should().ThrowAsync<ArgumentException>("a mistyped month must not be stored for good").WithMessage("*after the current date*");
        (await collateral.ListExposureSnapshotsAsync(nettingSet.NettingSetId)).Should().BeEmpty();

        // A future-dated snapshot stored before the refusal existed stays, since snapshots are append-only.
        var computed = (await service.ComputeAsync(nettingSet.NettingSetId, AsOf, "casey"))!;
        var stray = await collateral.AppendExposureSnapshotAsync(computed with { SnapshotId = Guid.NewGuid(), AsOfDate = today.AddMonths(1), Version = 0, PayloadSha256 = string.Empty });
        (await collateral.ListExposureSnapshotsAsync(nettingSet.NettingSetId)).Select(s => s.AsOfDate).Should().Equal(stray.AsOfDate, AsOf);

        // It is not yet a valuation: it neither blocks the call from the latest real one nor can be called itself.
        var call = await collateral.IssueMarginCallAsync(new IssueOtcMarginCallRequest(computed.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "casey"));
        call.AmountCalled.Should().Be(330_000m);
        await collateral.Invoking(s => s.IssueMarginCallAsync(new IssueOtcMarginCallRequest(stray.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, stray.AsOfDate, null, null, "casey")))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Snapshot {stray.SnapshotId:D} values {stray.AsOfDate:yyyy-MM-dd}, after the current date {today:yyyy-MM-dd} (UTC)*");
        (await collateral.ListMarginCallsAsync(nettingSet.NettingSetId)).Should().ContainSingle().Which.CallId.Should().Be(call.CallId);
    }

    /// <summary>
    /// Case A's collateral: the fund has posted 400,000 cash and 2,000 Treasuries at 100.125, 596,245
    /// after the haircut. Returns the Treasuries balance.
    /// </summary>
    private static async Task<OtcCollateralBalanceDto> PostCaseACollateralAsync(PostgresOtcCollateralStore collateral, OtcCollateralAgreementDto csa, Guid securityId)
    {
        await collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
            null, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 400_000m, 1m, AsOf.AddDays(-1), null, null, "casey"));
        return await collateral.UpsertCollateralBalanceAsync(new UpsertOtcCollateralBalanceRequest(
            null, csa.CollateralAgreementId, OtcCollateralDirection.Posted, OtcCollateralKind.Security, "US Treasuries", securityId, "USD", 2_000m, 100.125m, AsOf.AddDays(-1), null, null, "casey"));
    }

    private async Task<(OtcNettingSetDto NettingSet, OtcCollateralAgreementDto Csa, Guid SwapId)> BuildFixtureAsync(PostgresOtcContractStore store)
    {
        var service = Build();
        var swapId = Guid.NewGuid();
        await CreateAsync(service, swapId, "Swap", new
        {
            effectiveDate = "2026-03-04",
            maturityDate = "2031-03-04",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 0.035m, exchangesPrincipal = false },
                new { legType = "Floating", currency = "USD", floatingIndex = "SOFR", exchangesPrincipal = false }
            }
        });

        var master = await store.UpsertMasterAgreementAsync(new UpsertOtcMasterAgreementRequest(
            null, "Big Bank", "5493001KJTIIGC8Y1R12", "Meridian Master Fund", null, OtcMasterAgreementType.Isda2002, new DateOnly(2024, 1, 15), "New York", null, "casey"));
        var csa = await store.UpsertCollateralAgreementAsync(new UpsertOtcCollateralAgreementRequest(
            null, master.AgreementId, "USD", 0m, 250_000m, 100_000m, 1_000_000m, 10_000m, "Daily",
            [new OtcEligibleCollateralDto("USD cash", null, 0m), new OtcEligibleCollateralDto("US Treasuries", "Bond", 2m)],
            new DateOnly(2024, 1, 15), null, "standard CSA", "casey"));
        var nettingSet = await store.UpsertNettingSetAsync(new UpsertOtcNettingSetRequest(null, master.AgreementId, csa.CollateralAgreementId, "Big Bank ISDA", null, "casey"));
        await store.UpsertContractAsync(new UpsertOtcContractRequest(
            null, swapId, nettingSet.NettingSetId, "BB-0001-" + swapId.ToString("N")[..6], OtcContractDirection.Buy, 10_000_000m, "USD", Trade, Trade.AddDays(2), Trade.AddYears(5), OtcContractStatus.Pending, null, null, "casey"));
        await store.UpsertContractAsync(new UpsertOtcContractRequest(
            null, swapId, nettingSet.NettingSetId, "BB-0002-" + swapId.ToString("N")[..6], OtcContractDirection.Sell, 4_000_000m, "USD", Trade.AddDays(1), Trade.AddDays(3), Trade.AddYears(5), OtcContractStatus.Confirmed, "CONF-2", null, "casey"));
        return (nettingSet, csa, swapId);
    }

    private SecurityMasterService Build()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        return new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance);
    }

    private static async Task CreateAsync(SecurityMasterService service, Guid securityId, string assetClass, object terms)
    {
        var validFrom = DateTimeOffset.UtcNow.AddDays(-30);
        var code = "OTX" + securityId.ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            assetClass,
            JsonSerializer.SerializeToElement(new
            {
                displayName = code,
                currency = "USD",
                countryOfRisk = "US",
                issuerName = code,
                exchange = "XNAS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(terms),
            new[] { new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, code, true, validFrom, null, null) },
            validFrom,
            "test",
            "codex",
            null,
            "create for the OTC collateral model"));
    }

    /// <summary>Values every contract at a fixed percent of notional to the buyer, or leaves it unvalued when the price is null.</summary>
    private sealed class FixedPriceValuationSource(Func<OtcContractDto, decimal?> price) : IOtcContractValuationSource
    {
        public Task<OtcContractValuationDto> ValueAsync(OtcContractValuationRequest request, CancellationToken ct = default)
        {
            var contract = request.Contract;
            var percent = price(contract);
            var input = new OtcValuationInputDto("GoldenCopyPrice", "test-vendor", new DateTimeOffset(request.AsOfDate.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero), null, DataProvenance.Real);
            return Task.FromResult(percent is { } p
                ? new OtcContractValuationDto(contract.ContractId, contract.SecurityId, request.AsOfDate, OtcValuationStatus.Valued,
                    (contract.Direction == OtcContractDirection.Buy ? 1m : -1m) * contract.Notional * p / 100m, contract.NotionalCurrency, "test", [input], [], DataProvenance.Real)
                : new OtcContractValuationDto(contract.ContractId, contract.SecurityId, request.AsOfDate, OtcValuationStatus.Unvalued,
                    null, request.Currency, "test", [], [new OtcValuationBlockerDto(OtcValuationBlockerCodes.MissingInput, "no price")], DataProvenance.Real));
        }
    }
}
