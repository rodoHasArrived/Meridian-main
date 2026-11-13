using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class IntercompanyAccountBindingPostgresTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BindingRoundTrip_IsIdempotentScopedAndDatabaseImmutable()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var binding = IntercompanyAccountBindingRulesTests.Binding();
        await SeedBookAndChartAsync(database, binding);
        var store = Store(database, binding.TenantId);
        await store.AppendAsync(binding, null);
        (await store.AppendAsync(binding, null)).Should().BeEquivalentTo(binding);
        (await store.HistoryAsync(binding.LedgerBookId)).Should().ContainSingle().Which.Should().BeEquivalentTo(binding);
        (await store.ListAsync(binding.LedgerBookId, binding.EffectiveFrom)).Should().ContainSingle();
        (await store.ListAsync(binding.LedgerBookId, binding.EffectiveFrom.AddDays(-1))).Should().BeEmpty();
        (await store.GetRevisionAsync(binding.RevisionId)).Should().BeEquivalentTo(binding);
        var foreign = Store(database, "tenant-b");
        (await foreign.GetRevisionAsync(binding.RevisionId)).Should().BeNull();
        var foreignHistory = () => foreign.HistoryAsync(binding.LedgerBookId);
        await foreignHistory.Should().ThrowAsync<TenantScopeRejectedException>();

        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"delete from \"{database.Options.SchemaName}\".intercompany_account_binding_revisions where revision_id = @id;";
        command.Parameters.AddWithValue("id", binding.RevisionId);
        var remove = () => command.ExecuteNonQueryAsync();
        await remove.Should().ThrowAsync<PostgresException>().WithMessage("*append-only*");
        (await store.HistoryAsync(binding.LedgerBookId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentSuccessors_CannotForkTheRetainedBinding()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var initial = IntercompanyAccountBindingRulesTests.Binding();
        await SeedBookAndChartAsync(database, initial);
        var store = Store(database, initial.TenantId);
        await store.AppendAsync(initial, null);
        var left = IntercompanyAccountBindingRulesTests.Successor(initial);
        var right = IntercompanyAccountBindingRulesTests.Successor(initial);
        var outcomes = await Task.WhenAll(TryAppendAsync(left), TryAppendAsync(right));
        outcomes.Count(x => x is null).Should().Be(1);
        outcomes.OfType<InvalidOperationException>().Should().ContainSingle().Which.Message.Should().Contain("revision changed");
        (await store.HistoryAsync(initial.LedgerBookId)).Should().HaveCount(2);

        async Task<Exception?> TryAppendAsync(IntercompanyAccountBindingDto binding)
        {
            try
            { await store.AppendAsync(binding, initial.RevisionId); return null; }
            catch (Exception exception) { return exception; }
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AuthorityLease_RetainsMappingUntilTheConsumerReleasesIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var initial = IntercompanyAccountBindingRulesTests.Binding();
        await SeedBookAndChartAsync(database, initial, ct);
        var store = Store(database, initial.TenantId);
        await store.AppendAsync(initial, null, ct);
        var lease = await store.AcquireAuthorityLeaseAsync(ct);
        Task<IntercompanyAccountBindingDto>? mutation = null;
        try
        {
            mutation = store.AppendAsync(IntercompanyAccountBindingRulesTests.Successor(initial), initial.RevisionId, ct);
            await WaitForMappingLockAsync(database, ct);
            mutation.IsCompleted.Should().BeFalse();
            (await store.HistoryAsync(initial.LedgerBookId, ct)).Should().ContainSingle();
        }
        finally { await lease.DisposeAsync(); }
        await mutation!;
        (await store.HistoryAsync(initial.LedgerBookId, ct)).Should().HaveCount(2);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task MismatchedChartOrOwnerCannotCreateBindingAuthority()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var binding = IntercompanyAccountBindingRulesTests.Binding();
        await SeedBookAndChartAsync(database, binding);
        var store = Store(database, binding.TenantId);
        var wrongChart = () => store.AppendAsync(binding with { AccountName = "Not the retained account" }, null);
        await wrongChart.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact current*chart node*");
        var wrongCompany = () => store.AppendAsync(binding with { CompanyId = "foreign-company" }, null);
        await wrongCompany.Should().ThrowAsync<InvalidOperationException>().WithMessage("*authoritative owner*");
        var noTenant = Store(database, null);
        var unresolved = () => noTenant.ListAsync(binding.LedgerBookId, binding.EffectiveFrom);
        await unresolved.Should().ThrowAsync<TenantScopeRejectedException>();
        (await store.HistoryAsync(binding.LedgerBookId)).Should().BeEmpty();
    }

    private static PostgresIntercompanyAccountBindingStore Store(LedgerPostgresTestDatabase database, string? tenant)
        => new(database.Options, new TenantAccessor(tenant));

    private static async Task SeedBookAndChartAsync(LedgerPostgresTestDatabase database,
        IntercompanyAccountBindingDto binding, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into "{database.Options.SchemaName}".fund_profile_tenancy(fund_profile_id, tenant_id, company_id)
            values ('binding-fixture', @tenant, @company);
            insert into "{database.Options.SchemaName}".ledger_books(ledger_book_id, fund_profile_id,
                fund_structure_node_id, fund_structure_node_kind, display_name, base_currency, created_at, updated_at, tenant_id)
            values (@book, 'binding-fixture', @entity, 'Entity', 'Binding fixture', 'USD', now(), now(), @tenant);
            """;
        command.Parameters.AddWithValue("tenant", binding.TenantId);
        command.Parameters.AddWithValue("company", binding.CompanyId!);
        command.Parameters.AddWithValue("book", binding.LedgerBookId);
        command.Parameters.AddWithValue("entity", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(ct);
        await database.AccountingConfigurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(
            "binding-fixture", binding.LedgerBookId, AccountingConfigurationStatusDto.Active,
            "binding-v1", binding.RecordedAt, [],
            [new ChartOfAccountsNodeDto(binding.ChartAccountNodeId, binding.AccountPath, binding.AccountName,
                binding.AccountType, IntercompanyRole: binding.Role)], [], [], [], [],
            TenantId: binding.TenantId, CompanyId: binding.CompanyId), ct);
    }

    private static async Task WaitForMappingLockAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select exists(select 1 from pg_stat_activity
                    where wait_event_type = 'Lock' and query like '%intercompany_account_binding_revisions%'
                    and pid <> pg_backend_pid());
                """;
            if ((bool)(await command.ExecuteScalarAsync(ct))!)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(30), ct);
        }
        throw new TimeoutException("The mapping mutation did not wait for its retained authority lease.");
    }

    private sealed class TenantAccessor(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }
}
