using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundStructure;
using Meridian.Tests.Storage.FundAccounts;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Storage.FundStructure;

/// <summary>The same append-only contract over the file store and PostgreSQL (fund-structure migration 009).</summary>
[Trait("Category", "Integration")]
public sealed class IntercompanyRateFixingStoreTests(FundAccountDatabaseFixture fixture) : IClassFixture<FundAccountDatabaseFixture>
{
    private static readonly DateOnly Date = new(2026, 3, 30);

    [FundAccountDatabaseFact]
    public async Task PostgresStore_KeepsEveryVersion_RefusesStaleWrites_AndRejectsRewrites()
    {
        var options = new FundStructureStoreOptions
        {
            ConnectionString = fixture.Options.ConnectionString,
            Schema = "fs_fixings_" + Guid.NewGuid().ToString("N")
        };
        try
        {
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync();
            await AssertContractAsync(tenant => new PostgresIntercompanyRateFixingStore(options, new Tenant(tenant)));

            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                $"update {options.Schema}.intercompany_rate_fixings set payload='{{}}'::jsonb",
                $"delete from {options.Schema}.intercompany_rate_fixings",
                $"truncate {options.Schema}.intercompany_rate_fixings"
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            }
            await using var count = new NpgsqlCommand($"select count(*) from {options.Schema}.intercompany_rate_fixings", connection);
            Assert.Equal(2L, await count.ExecuteScalarAsync());
        }
        finally { await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema); }
    }

    internal static async Task AssertContractAsync(Func<string?, IIntercompanyRateFixingStore> create)
    {
        var store = create("tenant-a");
        var service = new IntercompanyRateFixingService(store);
        var proposed = await service.ProposeAsync("tenant-a", "maker", new(Guid.NewGuid(), "SOFR", Date, 0.043m, "Synthetic publisher", "Fixture", 0));
        var approved = await service.DecideAsync("tenant-a", "checker", proposed.FixingId,
            new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Approve, 1, proposed.Pending!.ValueDigest));

        // A second instance reads what the first retained; an identical retry returns it without a new version.
        var reader = create("tenant-a");
        Assert.Equal(2, (await reader.GetAsync("tenant-a", proposed.FixingId))!.Version);
        Assert.Equal(IntercompanyRateFixingStoreRules.Serialize(approved), IntercompanyRateFixingStoreRules.Serialize(await reader.AppendAsync(approved, 1)));
        var listed = Assert.Single(await reader.ListAsync("tenant-a", "SOFR", Date.AddDays(-1), Date));
        Assert.Equal(0.043m, listed.Approved!.RateFraction);
        Assert.Empty(await reader.ListAsync("tenant-a", "SOFR", Date.AddDays(1), Date.AddDays(5)));

        // Stale versions and history that drops a step are refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.AppendAsync(approved with { Version = 3, Approved = null }, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.AppendAsync(approved with { Version = 3, Events = approved.Events.Take(1).ToArray() }, 2));

        // Another tenant sees nothing, and a caller scoped to one tenant cannot read another's.
        Assert.Null(await create("tenant-b").GetAsync("tenant-b", proposed.FixingId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => create("tenant-b").GetAsync("tenant-a", proposed.FixingId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => create("tenant-b").AppendAsync(approved with { Version = 3 }, 2));
    }

    internal sealed class Tenant(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }
}

/// <summary>The file store needs no database, so it runs without the PostgreSQL fixture.</summary>
public sealed class FileIntercompanyRateFixingStoreTests
{
    [Fact]
    public async Task FileStore_KeepsEveryVersion_RefusesStaleWrites_AndIsTenantScoped()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fixings-" + Guid.NewGuid().ToString("N"));
        try
        {
            await IntercompanyRateFixingStoreTests.AssertContractAsync(tenant => new FileIntercompanyRateFixingStore(root, new IntercompanyRateFixingStoreTests.Tenant(tenant)));
            Assert.Single(Directory.EnumerateFiles(root, "*.json"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
