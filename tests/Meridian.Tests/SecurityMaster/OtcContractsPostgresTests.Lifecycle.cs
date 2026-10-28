using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class OtcContractsPostgresTests
{
    [SecurityMasterDatabaseFact]
    public async Task Lifecycle_IsAtomicIdempotentImmutable_AndBlocksSnapshotAmendment()
    {
        var service = Build();
        var securityId = Guid.NewGuid();
        await CreateAsync(service, securityId, "Swap", new
        {
            effectiveDate = "2026-03-04", maturityDate = "2031-03-04",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", fixedRate = 0.035m, exchangesPrincipal = false },
                new { legId = "receive", legType = "Floating", currency = "USD", index = "SOFR", exchangesPrincipal = false }
            }
        });
        var store = new PostgresOtcContractStore(_fixture.Options);
        var master = await store.UpsertMasterAgreementAsync(new(null, "Lifecycle Bank", null, "Fund", null, OtcMasterAgreementType.Isda2002, Trade, UpdatedBy: "maker"));
        var nettingSet = await store.UpsertNettingSetAsync(new(null, master.AgreementId, null, "Lifecycle", UpdatedBy: "maker"));
        var original = new UpsertOtcContractRequest(null, securityId, nettingSet.NettingSetId, null,
            OtcContractDirection.Buy, 1000000m, "USD", Trade, Trade, Trade.AddYears(5), OtcContractStatus.Confirmed, UpdatedBy: "maker");
        var current = await store.UpsertContractAsync(original);
        var request = new AppendOtcContractLifecycleRequest(Guid.NewGuid(), current.ContractId, current.Version,
            Trade, "RESET-1", new OtcRateResetData("receive", "SOFR", Trade, 0m), "servicer");
        var concurrentRetries = await Task.WhenAll(store.AppendLifecycleEventAsync(request), store.AppendLifecycleEventAsync(request));
        var item = concurrentRetries[0];
        concurrentRetries[1].Should().BeEquivalentTo(item);
        (await store.AppendLifecycleEventAsync(request)).Should().BeEquivalentTo(item);
        (await store.GetContractAsync(current.ContractId)).Should().Be(item.After);
        (await store.GetLifecycleEventsAsync(current.ContractId)).Should().ContainSingle();
        await store.Invoking(s => s.AppendLifecycleEventAsync(request with { SourceReference = "conflicting" }))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*different content*");
        await store.Invoking(s => s.AppendLifecycleEventAsync(request with { EventId = Guid.NewGuid() }))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*version changed*");
        await store.Invoking(s => s.UpsertContractAsync(original with { ContractId = current.ContractId }))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*lifecycle-managed*");
        (await store.GetContractAsync(current.ContractId))!.Version.Should().Be(2);
        await store.Invoking(s => s.AppendLifecycleEventAsync(new(Guid.NewGuid(), current.ContractId, 2,
            Trade, "CREDIT-1", new OtcCreditEventData(OtcCreditEventKind.Bankruptcy, "Reference Co", "DC-1"), "servicer")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*CreditDefaultSwap*");
        var compressed = await store.AppendLifecycleEventAsync(new(Guid.NewGuid(), current.ContractId, 2,
            Trade, "COMP-1", new OtcCompressionData("CYCLE-1", 500000m), "servicer"));
        var successorMaster = await store.UpsertMasterAgreementAsync(new(null, "Successor Bank", null, "Fund", null, OtcMasterAgreementType.Isda2002, Trade, UpdatedBy: "maker"));
        var successorSet = await store.UpsertNettingSetAsync(new(null, successorMaster.AgreementId, null, "Successor", UpdatedBy: "maker"));
        var successor = await store.UpsertContractAsync(original with
        {
            NettingSetId = successorSet.NettingSetId, Notional = 500000m, EffectiveDate = Trade.AddDays(1)
        });
        var novated = await store.AppendLifecycleEventAsync(new(Guid.NewGuid(), current.ContractId, compressed.After.Version,
            Trade.AddDays(1), "NOVA-1", new OtcNovationData(successor.ContractId), "servicer"));
        novated.After.Status.Should().Be(OtcContractStatus.Novated);
        var history = await store.GetLifecycleEventsAsync(current.ContractId);
        history.Should().HaveCount(3);
        OtcContractLifecycleRules.Replay(history[0].Before, history).Should().Be(novated.After);
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            $"delete from {_fixture.Options.Schema}.otc_contract_lifecycle_events where event_id = @id",
            $"update {_fixture.Options.Schema}.otc_contract_lifecycle_events set actor = 'changed' where event_id = @id",
            $"truncate {_fixture.Options.Schema}.otc_contract_lifecycle_events"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@id", StringComparison.Ordinal))
                command.Parameters.AddWithValue("id", item.EventId);
            await command.Invoking(c => c.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()
                .Where(e => e.SqlState == "23514");
        }
    }
}
