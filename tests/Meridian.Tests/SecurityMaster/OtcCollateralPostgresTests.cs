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
/// refused for a second open call, an incomplete snapshot or a superseded one.
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
        first.CounterpartyAsSecuredParty.Held.Should().Be(596_245m);
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

        // Once the call is settled, a second one for the fund may be issued; an incomplete snapshot refuses it.
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
