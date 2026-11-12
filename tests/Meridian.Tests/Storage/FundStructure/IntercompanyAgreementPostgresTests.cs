using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Tests.Storage.FundAccounts;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Storage.FundStructure;

[Trait("Category", "Integration")]
public sealed class IntercompanyAgreementPostgresTests(FundAccountDatabaseFixture fixture) : IClassFixture<FundAccountDatabaseFixture>
{
    [FundAccountDatabaseFact]
    public async Task History_IsScopedIdempotentImmutableAndFencedAcrossStoreInstances()
    {
        var options = new FundStructureStoreOptions { ConnectionString = fixture.Options.ConnectionString,
            Schema = "fs_agreements_" + Guid.NewGuid().ToString("N") };
        try
        {
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync();
            var first = new PostgresIntercompanyAgreementStore(options);
            var second = new PostgresIntercompanyAgreementStore(options);
            var snapshot = Draft();
            var created = new IntercompanyAgreementEventDto(Guid.NewGuid(), new string('a', 64), IntercompanyAgreementActionDto.Create,
                "owner", snapshot.UpdatedAt, null, snapshot);
            await first.AppendAsync("tenant-a", 0, created);
            var submitted = new IntercompanyAgreementEventDto(Guid.NewGuid(), new string('b', 64), IntercompanyAgreementActionDto.Submit,
                "owner", snapshot.UpdatedAt, null, snapshot with { Status = IntercompanyAgreementStatusDto.Submitted, Version = 2 });
            await using (var lease = await first.AcquireAuthorityLeaseAsync())
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.AppendAsync("tenant-a", 1, submitted, cancellation.Token));
                Assert.Equal(1, (await second.GetAsync("tenant-a", snapshot.AgreementId))!.Current.Version);
            }
            await second.AppendAsync("tenant-a", 1, submitted);
            Assert.Equal(1, (await second.AppendAsync("tenant-a", 0, created)).Version);
            Assert.Null(await second.GetAsync("tenant-b", snapshot.AgreementId));
            Assert.Empty(await second.ListAsync("tenant-b"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => second.AppendAsync("tenant-a", 1,
                submitted with { RequestId = Guid.NewGuid() }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => second.AppendAsync("tenant-a", 0,
                created with { RequestDigest = new string('c', 64) }));
            var history = (await first.GetAsync("tenant-a", snapshot.AgreementId))!;
            Assert.Equal(2, history.Events.Count);
            Assert.Equal(history.Events[0].ContentHash, history.Events[1].PreviousHash);

            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                $"update {options.Schema}.intercompany_agreement_events set payload='{{}}'::jsonb",
                $"delete from {options.Schema}.intercompany_agreement_events",
                $"truncate {options.Schema}.intercompany_agreement_events"
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            }
            Assert.Equal(2, (await first.GetAsync("tenant-a", snapshot.AgreementId))!.Events.Count);
        }
        finally { await FundAccountDatabaseFixture.DropSchemaAsync(options.ConnectionString, options.Schema); }
    }

    private static IntercompanyAgreementDto Draft()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new(Guid.NewGuid(), Guid.NewGuid(), "facility", IntercompanyAgreementKindDto.Loan, first, second,
            "Lender", "Borrower", "USD", "Monthly", now, null, Guid.NewGuid(), 1, 1, IntercompanyAgreementStatusDto.Draft,
            [new(first, Guid.NewGuid(), "ar", "ap"), new(second, Guid.NewGuid(), "ar", "ap")],
            null, "owner", now, "owner", now);
    }
}
