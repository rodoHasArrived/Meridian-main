using FluentAssertions;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Npgsql;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed class ImpactDependencyGuardTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FinalizationLease_FencesPeriodMutationsUntilReleased_AndChecksTenant()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        const string tenant = "impact-tenant";
        const string fund = "impact-fund";
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(fund, tenant);
        var accessor = Substitute.For<IFundScopeTenantAccessor>();
        accessor.ResolveCallerTenant().Returns(tenant);
        var guarded = new PostgresLedgerJournalStore(database.Options, accessor);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using (var lease = await guarded.AcquireImpactDependencyLeaseAsync(tenant, fund))
        {
            await using var write = connection.CreateCommand();
            write.CommandText = $"set lock_timeout = '150ms'; update \"{database.Options.SchemaName}\".accounting_periods set status = status where false";
            await FluentActions.Invoking(() => write.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()
                .Where(error => error.SqlState == PostgresErrorCodes.LockNotAvailable);
        }
        await using var allowed = connection.CreateCommand();
        allowed.CommandText = $"update \"{database.Options.SchemaName}\".accounting_periods set status = status where false";
        await allowed.ExecuteNonQueryAsync();
        await FluentActions.Invoking(() => guarded.AcquireImpactDependencyLeaseAsync("foreign-tenant", fund))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
