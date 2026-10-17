using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterPostgresProjectionNotificationTests
{
    [SecurityMasterDatabaseFact]
    public async Task PersistProjectionBatchAsync_ConcurrentStaleWriter_PreservesNewerParentChildrenAndCheckpoint()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var securityId = Guid.NewGuid();
        var stale = WithProjectionAlias(CreateProjection(securityId, UniqueTicker(), "Before correction", 1), "OLD");
        var newer = WithProjectionAlias(CreateDepositProjection(securityId, UniqueTicker(), "Corrected deposit", 2), "NEW");
        var newerApplication = $"sm-newer-{Guid.NewGuid():N}";
        var staleApplication = $"sm-stale-{Guid.NewGuid():N}";
        var newerStore = new PostgresSecurityMasterStore(ProjectionWriterOptions(newerApplication));
        var staleStore = new PostgresSecurityMasterStore(ProjectionWriterOptions(staleApplication));
        var checkpoint = $"projection-race-{securityId:N}";
        await staleStore.UpsertProjectionAsync(stale, ct);
        (await new PostgresEquityReferenceProjectionStore(_fixture.Options).GetEquityAsync(securityId, ct))
            .Should().NotBeNull("the stale writer would recreate a previously valid equity child");
        await using var listener = await ListeningNode.StartAsync(_fixture.Options);

        await using var blocker = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await blocker.OpenAsync(ct);
        var lockKey = Random.Shared.NextInt64(1, long.MaxValue);
        var triggerName = $"pause_projection_{Guid.NewGuid():N}";
        var functionName = $"{_fixture.Options.Schema}.{triggerName}";
        Task? newerWrite = null;
        Task? staleWrite = null;
        try
        {
            await using (var setup = blocker.CreateCommand())
            {
                // Pause the real store only after its parent upsert holds the securities row.
                // The trigger is unique and applies to this security/version alone.
                setup.CommandText = $"""
                    create function {functionName}() returns trigger language plpgsql as $$
                    begin
                        perform pg_advisory_xact_lock({lockKey});
                        return new;
                    end;
                    $$;
                    create trigger {triggerName} after insert or update
                    on {_fixture.Options.Schema}.securities
                    for each row when (new.security_id = '{securityId:D}'::uuid and new.version = 2)
                    execute function {functionName}();
                    """;
                await setup.ExecuteNonQueryAsync(ct);
            }

            await using (var gate = await blocker.BeginTransactionAsync(ct))
            {
                await using (var hold = blocker.CreateCommand())
                {
                    hold.Transaction = gate;
                    hold.CommandText = "select pg_advisory_xact_lock(@key);";
                    hold.Parameters.AddWithValue("key", lockKey);
                    await hold.ExecuteNonQueryAsync(ct);
                }

                newerWrite = newerStore.PersistProjectionBatchAsync(checkpoint, 200, [newer], ct);
                var newerPid = await WaitForProjectionWriterBlockAsync(newerWrite, newerApplication, blocker.ProcessID, ct);
                staleWrite = staleStore.PersistProjectionBatchAsync(checkpoint, 100, [stale], ct);
                await WaitForProjectionWriterBlockAsync(staleWrite, staleApplication, newerPid, ct);

                // Both server-side dependencies are observed before releasing the newer writer.
                // Its commit must make PostgreSQL recheck the stale writer's version predicate.
                await gate.CommitAsync(ct);
            }

            await Task.WhenAll(newerWrite, staleWrite);

            // The standalone and deactivation paths must obey the same lower-version boundary.
            await staleStore.UpsertProjectionAsync(stale, ct);
            await staleStore.DeactivateProjectionAsync(securityId, DateTimeOffset.UtcNow, stale.Version, ct);

            // A subsequent commit is a delivery barrier for all earlier notifications.
            var sentinelId = Guid.NewGuid();
            var sentinel = listener.WaitForAsync(sentinelId, SecurityProjectionChangeOutcome.Refreshed);
            await newerStore.UpsertProjectionAsync(CreateProjection(sentinelId, UniqueTicker(), "Delivery barrier", 1), ct);
            await sentinel.WaitAsync(DeliveryTimeout, ct);

            var persisted = (await newerStore.GetProjectionAsync(securityId, ct))!;
            persisted.Should().NotBeNull();
            persisted.Version.Should().Be(2);
            persisted.Status.Should().Be(SecurityStatusDto.Active);
            persisted.EffectiveTo.Should().BeNull();
            persisted.AssetClass.Should().Be("Deposit");
            persisted.DisplayName.Should().Be(newer.DisplayName);
            persisted.PrimaryIdentifierValue.Should().Be(newer.PrimaryIdentifierValue);
            persisted.CommonTerms.GetProperty("displayName").GetString().Should().Be(newer.DisplayName);
            persisted.AssetSpecificTerms.GetProperty("interestRate").GetDecimal().Should().Be(0.0425m);
            AssertProjectionIdentifiers(persisted.Identifiers, newer.Identifiers);
            persisted.Aliases.Select(static alias => (alias.AliasId, alias.AliasValue))
                .Should().BeEquivalentTo(newer.Aliases.Select(static alias => (alias.AliasId, alias.AliasValue)));
            (await newerStore.GetCheckpointAsync(checkpoint, ct)).Should().Be(200);

            var deposit = await new PostgresDepositReferenceProjectionStore(_fixture.Options).GetDepositAsync(securityId, ct);
            deposit.Should().NotBeNull();
            deposit!.Version.Should().Be(2);
            deposit.DisplayName.Should().Be(newer.DisplayName);
            deposit.PrimaryIdentifierValue.Should().Be(newer.PrimaryIdentifierValue);
            deposit.InstitutionName.Should().Be("Projection Test Bank");
            deposit.InterestRate.Should().Be(0.0425m);
            (await new PostgresEquityReferenceProjectionStore(_fixture.Options).GetEquityAsync(securityId, ct))
                .Should().BeNull("a rejected stale equity must neither delete the deposit nor recreate its equity child");

            listener.Cache.Get(securityId)!.Version.Should().Be(2);
            var announcements = listener.HandledPayloads
                .Select(payload => SecurityProjectionChangeNotification.TryParse(payload, out var notification) ? notification : null)
                .Where(notification => notification?.SecurityId == securityId)
                .ToArray();
            announcements.Should().ContainSingle("rejected older writes must not announce a projection change");
            announcements[0]!.Version.Should().Be(2);
        }
        finally
        {
            // Cancel and observe outstanding store tasks before removing the trigger, including
            // assertion-failure paths where the gate was released by transaction disposal.
            await timeout.CancelAsync();
            foreach (var write in new[] { newerWrite, staleWrite }.OfType<Task>())
            {
                try
                {
                    await write.WaitAsync(DeliveryTimeout);
                }
                catch
                {
                    // The main path observes write failures; cleanup must still remove the trigger.
                }
            }

            using var cleanupTimeout = new CancellationTokenSource(DeliveryTimeout);
            await using var cleanup = blocker.CreateCommand();
            cleanup.CommandText = $"""
                drop trigger if exists {triggerName} on {_fixture.Options.Schema}.securities;
                drop function if exists {functionName}();
                """;
            await cleanup.ExecuteNonQueryAsync(cleanupTimeout.Token);
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task UpsertProjectionAsync_EqualVersionRepair_ReplacesAndRemovesIdentifiersAndObsoleteAssetRows()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        var original = WithProjectionAlias(CreateProjection(securityId, UniqueTicker(), "Misclassified equity", 5), "OLD");
        await store.UpsertProjectionAsync(original, ct);
        var equityStore = new PostgresEquityReferenceProjectionStore(_fixture.Options);
        (await equityStore.GetEquityAsync(securityId, ct)).Should().NotBeNull();

        // Projection repair does not append an aggregate event: its version remains unchanged.
        var repair = WithProjectionAlias(CreateDepositProjection(securityId, UniqueTicker(), "Repaired deposit", 5), "REPAIRED");
        await store.UpsertProjectionAsync(repair, ct);

        var persisted = (await store.GetProjectionAsync(securityId, ct))!;
        persisted.Version.Should().Be(5);
        persisted.AssetClass.Should().Be("Deposit");
        AssertProjectionIdentifiers(persisted.Identifiers, repair.Identifiers);
        persisted.Identifiers.Should().NotContain(identifier => identifier.Value == original.PrimaryIdentifierValue);
        persisted.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("REPAIRED");
        (await equityStore.GetEquityAsync(securityId, ct)).Should().BeNull("equal-version repair cleans up the old asset-class child");
        var depositStore = new PostgresDepositReferenceProjectionStore(_fixture.Options);
        var deposit = (await depositStore.GetDepositAsync(securityId, ct))!;
        deposit.Version.Should().Be(5);
        deposit.PrimaryIdentifierValue.Should().Be(repair.PrimaryIdentifierValue);
        deposit.InterestRate.Should().Be(0.0425m);

        await store.UpsertProjectionAsync(repair with { Identifiers = [], Aliases = [] }, ct);
        var cleared = (await store.GetProjectionAsync(securityId, ct))!;
        cleared.Version.Should().Be(5);
        cleared.Identifiers.Should().BeEmpty("an accepted equal-version repair can withdraw all identifier rows");
        cleared.Aliases.Should().BeEmpty();
        (await depositStore.GetDepositAsync(securityId, ct))!.Version.Should().Be(5);
        (await equityStore.GetEquityAsync(securityId, ct)).Should().BeNull();
    }

    private SecurityMasterOptions ProjectionWriterOptions(string applicationName)
        => new()
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(_fixture.Options.ConnectionString)
            {
                ApplicationName = applicationName
            }.ConnectionString,
            Schema = _fixture.Options.Schema,
            PreloadProjectionCache = false
        };

    private async Task<int> WaitForProjectionWriterBlockAsync(
        Task writer, string applicationName, int blockerPid, CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await observer.OpenAsync(ct);
        await using var query = observer.CreateCommand();
        query.CommandText = """
            select pid from pg_stat_activity
            where application_name = @application_name and @blocker = any(pg_blocking_pids(pid));
            """;
        query.Parameters.AddWithValue("application_name", applicationName);
        query.Parameters.AddWithValue("blocker", blockerPid);
        while (true)
        {
            if (await query.ExecuteScalarAsync(ct) is int writerPid)
            {
                writer.IsCompleted.Should().BeFalse("the writer must remain blocked until its predecessor is released");
                return writerPid;
            }

            if (writer.IsCompleted)
            {
                await writer;
                Assert.Fail("The projection writer completed before the expected PostgreSQL row-lock dependency was observed.");
            }

            // Poll an observed server lock dependency; elapsed time never counts as proof.
            await Task.Delay(10, ct);
        }
    }

    private static SecurityProjectionRecord CreateDepositProjection(Guid securityId, string ticker, string displayName, long version)
        => CreateProjection(securityId, ticker, displayName, version) with
        {
            AssetClass = "Deposit",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                depositType = "Term",
                institutionName = "Projection Test Bank",
                maturity = "2030-06-30",
                interestRate = 0.0425m,
                dayCount = "Actual365Fixed",
                isCallable = false
            })
        };

    private static void AssertProjectionIdentifiers(
        IReadOnlyList<SecurityIdentifierDto> actual, IReadOnlyList<SecurityIdentifierDto> expected)
    {
        // Database reads populate normalization fields and PostgreSQL timestamps have microsecond
        // precision, so compare the supplied identifier material separately from that enrichment.
        actual.Select(static identifier => (identifier.Kind, identifier.Value, identifier.IsPrimary, identifier.Provider, identifier.ValidTo))
            .Should().BeEquivalentTo(expected.Select(static identifier =>
                (identifier.Kind, identifier.Value, identifier.IsPrimary, identifier.Provider, identifier.ValidTo)));
        actual.Should().ContainSingle().Which.ValidFrom.Should().BeCloseTo(expected[0].ValidFrom, TimeSpan.FromMicroseconds(1));
    }

    private static SecurityProjectionRecord WithProjectionAlias(SecurityProjectionRecord projection, string aliasValue)
        => projection with
        {
            Aliases =
            [
                new SecurityAliasDto(
                    Guid.NewGuid(), projection.SecurityId, "Ticker", aliasValue, null, SecurityAliasScope.Operations,
                    null, "projection-test", projection.EffectiveFrom, projection.EffectiveFrom, null, true)
            ]
        };
}
