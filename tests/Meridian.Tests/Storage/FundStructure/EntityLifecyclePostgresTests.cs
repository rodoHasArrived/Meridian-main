using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Tests.Storage.FundAccounts;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Storage.FundStructure;

[Trait("Category", "Integration")]
public sealed class EntityLifecyclePostgresTests(FundAccountDatabaseFixture fixture) : IClassFixture<FundAccountDatabaseFixture>
{
    [FundAccountDatabaseFact]
    public async Task History_IsImmutableTenantScopedAndLeaseBlocksConcurrentDissolution()
    {
        var options = new FundStructureStoreOptions { ConnectionString = fixture.Options.ConnectionString, Schema = "fs_lifecycle_" + Guid.NewGuid().ToString("N") };
        try
        {
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync();
            var entities = new PostgresFundStructureStore(options);
            var entity = new LegalEntitySummaryDto(Guid.NewGuid(), LegalEntityTypeDto.Vehicle, "IC-A", "Intercompany A", "US", "USD", true,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
            await entities.UpsertLegalEntityAsync(entity);
            await entities.StampNodeTenantAsync(entity.EntityId, "tenant-a");
            var history = new PostgresEntityLifecycleStore(options);
            var first = EntityLifecycleRevisionIntegrity.Seal(new("tenant-a", entity.EntityId, 1, DateTimeOffset.UtcNow, "test-preparer", "Initial status",
                "retained://formation", "", "", [new(Guid.NewGuid(), entity.EffectiveFrom, null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation)]));
            await history.AppendAsync(first, 0);
            Assert.Null(await history.GetLatestAsync("tenant-b", entity.EntityId));

            var next = EntityLifecycleRevisionIntegrity.Seal(first with { Revision = 2, PreviousHash = first.ContentHash,
                Reason = "Evidenced dissolution", Timeline = [first.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Dissolved }] });
            await using var lease = await history.AcquireReadLeaseAsync("tenant-a", [entity.EntityId]);
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => history.AppendAsync(next, 1, cancellation.Token));
            Assert.Equal(1, (await history.GetLatestAsync("tenant-a", entity.EntityId))!.Revision);
            await lease.DisposeAsync();
            await history.AppendAsync(next, 1);
            Assert.Equal(LegalEntityLifecycleStatusDto.Dissolved, (await entities.GetLegalEntityAsync(entity.EntityId))!.LifecycleStatus);

            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                $"update {options.Schema}.entity_lifecycle_revision set recorded_by='rewriter'",
                $"delete from {options.Schema}.entity_lifecycle_revision",
                $"truncate {options.Schema}.entity_lifecycle_revision"
            })
            {
                await using var mutation = new NpgsqlCommand(sql, connection);
                await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());
            }
            Assert.Equal(first.ContentHash, (await history.GetRevisionAsync("tenant-a", entity.EntityId, 1))!.ContentHash);
        }
        finally { await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema); }
    }

    [FundAccountDatabaseFact]
    public async Task MigrationBaseline_DoesNotInventStatusBeforeCutoff()
    {
        var options = new FundStructureStoreOptions { ConnectionString = fixture.Options.ConnectionString, Schema = "fs_lifecycle_base_" + Guid.NewGuid().ToString("N") };
        try
        {
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync();
            var entities = new PostgresFundStructureStore(options);
            var entity = new LegalEntitySummaryDto(Guid.NewGuid(), LegalEntityTypeDto.Vehicle, "OLD", "Legacy entity", "US", "USD", true,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null, LifecycleStatus: LegalEntityLifecycleStatusDto.WindingDown);
            await entities.UpsertLegalEntityAsync(entity);
            await entities.StampNodeTenantAsync(entity.EntityId, "tenant-a");
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "FundStructure", "Migrations", "007_entity_lifecycle.sql"));
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            await using var migration = new NpgsqlCommand(sql.Replace("__SCHEMA__", options.Schema, StringComparison.Ordinal), connection);
            await migration.ExecuteNonQueryAsync();
            var history = await new PostgresEntityLifecycleStore(options).GetLatestAsync("tenant-a", entity.EntityId);
            Assert.NotNull(history);
            Assert.True(history.LegacySnapshot);
            Assert.True(history.Timeline[0].EffectiveFrom > entity.EffectiveFrom);
            Assert.Equal(LegalEntityLifecycleStatusDto.WindingDown, history.Timeline[0].Status);
        }
        finally { await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema); }
    }
}
