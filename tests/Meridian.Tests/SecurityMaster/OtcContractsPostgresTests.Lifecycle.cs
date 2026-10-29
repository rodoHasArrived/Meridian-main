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
        var (store, original, current) = await CreateLifecycleContractAsync();
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
            NettingSetId = successorSet.NettingSetId,
            Notional = 500000m,
            EffectiveDate = Trade.AddDays(1)
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
    [SecurityMasterDatabaseFact]
    public async Task LifecycleDatabaseGuard_RefusesOldSnapshotSqlAtCommit_AndPreservesHistory()
    {
        var (store, _, contract) = await CreateLifecycleContractAsync();
        // Contracts without lifecycle history retain compatibility with the old CRUD writer.
        await LegacyVersionIncrementAsync(contract.ContractId, expectRefusal: false);
        var current = (await store.GetContractAsync(contract.ContractId))!;
        current.Version.Should().Be(2);
        var item = await store.AppendLifecycleEventAsync(new(Guid.NewGuid(), current.ContractId, current.Version,
            Trade, "RESET-DB-GUARD", new OtcRateResetData("receive", "SOFR", Trade, 0m), "servicer"));

        await LegacyVersionIncrementAsync(current.ContractId, expectRefusal: true);

        (await store.GetContractAsync(current.ContractId)).Should().Be(item.After);
        var history = await store.GetLifecycleEventsAsync(current.ContractId);
        history.Should().ContainSingle().Which.Should().BeEquivalentTo(item);
        OtcContractLifecycleRules.Replay(history[0].Before, history).Should().Be(item.After);
    }

    [SecurityMasterDatabaseFact]
    public async Task LifecycleDatabaseGuard_UsesFinalRowVersion_ForMultipleAtomicTransitions()
    {
        var (store, _, contract) = await CreateLifecycleContractAsync();
        var reset = await store.AppendLifecycleEventAsync(new(Guid.NewGuid(), contract.ContractId, contract.Version,
            Trade, "RESET-MULTI", new OtcRateResetData("receive", "SOFR", Trade, 0m), "servicer"));
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var first = OtcContractLifecycleRules.Create(reset.After,
            new(Guid.NewGuid(), contract.ContractId, reset.After.Version, Trade, "COMP-MULTI-1",
                new OtcCompressionData("CYCLE-MULTI-1", 750000m), "servicer"), now, [reset]);
        var second = OtcContractLifecycleRules.Create(first.After,
            new(Guid.NewGuid(), contract.ContractId, first.After.Version, Trade, "COMP-MULTI-2",
                new OtcCompressionData("CYCLE-MULTI-2", 500000m), "servicer"), now, [reset, first]);
        await using (var connection = new NpgsqlConnection(_fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var item in new[] { first, second })
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    update {_fixture.Options.Schema}.otc_contracts
                        set notional = @notional, version = @version, updated_by = @actor, updated_at = @recorded
                        where contract_id = @contract;
                    insert into {_fixture.Options.Schema}.otc_contract_lifecycle_events
                        (event_id, contract_id, contract_version, event_type, effective_date, actor, recorded_at, payload_version, payload)
                        values (@event, @contract, @version, @kind, @effective, @actor, @recorded, @payload_version, @payload);
                    """;
                command.Parameters.AddWithValue("notional", item.After.Notional);
                command.Parameters.AddWithValue("version", item.After.Version);
                command.Parameters.AddWithValue("actor", item.Actor);
                command.Parameters.AddWithValue("recorded", item.RecordedAt.UtcDateTime);
                command.Parameters.AddWithValue("contract", item.After.ContractId);
                command.Parameters.AddWithValue("event", item.EventId);
                command.Parameters.AddWithValue("kind", item.Kind.ToString());
                command.Parameters.AddWithValue("effective", item.EffectiveDate);
                command.Parameters.AddWithValue("payload_version", item.PayloadVersion);
                command.Parameters.AddWithValue("payload", NpgsqlTypes.NpgsqlDbType.Jsonb, OtcContractLifecycleRules.Serialize(item));
                (await command.ExecuteNonQueryAsync()).Should().Be(2);
            }
            await transaction.CommitAsync();
        }
        var history = await store.GetLifecycleEventsAsync(contract.ContractId);
        history.Should().HaveCount(3);
        (await store.GetContractAsync(contract.ContractId)).Should().Be(second.After);
        OtcContractLifecycleRules.Replay(history[0].Before, history).Should().Be(second.After);
    }

    private async Task LegacyVersionIncrementAsync(Guid contractId, bool expectRefusal)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            update {_fixture.Options.Schema}.otc_contracts
                set notional = 900000, version = version + 1, updated_by = 'old-deployed-node'
                where contract_id = @id;
            """;
        command.Parameters.AddWithValue("id", contractId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1, "the constraint is deferred until commit");
        if (expectRefusal)
            await transaction.Invoking(value => value.CommitAsync()).Should().ThrowAsync<PostgresException>()
                .Where(exception => exception.SqlState == "23514" && exception.ConstraintName == "otc_contract_lifecycle_version_guard");
        else
            await transaction.CommitAsync();
    }

    private async Task<(PostgresOtcContractStore Store, UpsertOtcContractRequest Original, OtcContractDto Current)> CreateLifecycleContractAsync()
    {
        var service = Build();
        var securityId = Guid.NewGuid();
        await CreateAsync(service, securityId, "Swap", new
        {
            effectiveDate = "2026-03-04",
            maturityDate = "2031-03-04",
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
        return (store, original, current);
    }

}
