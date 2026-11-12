using System.Data;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class LedgerReplayAfterControlsPostgresTests
{
    [LedgerDatabaseFact]
    public async Task ExactStandaloneAndAmbientReplay_PreserveReceiptAfterRealLifecycleBecomesReadOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var write = fixture.Write();
        await fixture.Store.AppendAsync(write);
        var before = await fixture.Store.VerifyLedgerEventAuditAsync();
        await fixture.DissolveAsync();

        await fixture.Store.AppendAsync(write);
        await using var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await using var authority = await fixture.Store.AcquirePostingAuthorityAsync(connection, transaction, [write]);
            await fixture.Store.AppendAsync(connection, transaction, write);
            await transaction.CommitAsync();
        }

        Assert.Single(await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId));
        Assert.Equal(before, await fixture.Store.VerifyLedgerEventAuditAsync());
        await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Store.AppendAsync(fixture.Write()));
    }

    [LedgerDatabaseFact]
    public async Task ChangedEconomicsOrBookClaim_CannotUseCommittedIdentityToBypassCurrentControls()
    {
        await using var fixture = await Fixture.CreateAsync();
        var write = fixture.Write();
        await fixture.Store.AppendAsync(write);
        await fixture.DissolveAsync();
        var altered = fixture.Write(101m, write.Entry.JournalEntryId);
        var append = () => fixture.Store.AppendAsync(altered);
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
        var wrongBook = () => fixture.Store.AppendAsync(write with { LedgerBookId = Guid.NewGuid() });
        await wrongBook.Should().ThrowAsync<LedgerValidationException>().WithMessage("*replay book*");
        Assert.Single(await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId));
    }

    [LedgerDatabaseFact]
    public async Task ExactReplay_StillVerifiesRetainedAuditIntegrity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var write = fixture.Write();
        await fixture.Store.AppendAsync(write);
        await fixture.DissolveAsync();
        await using (var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString))
        {
            await connection.OpenAsync();
            var schema = new NpgsqlCommandBuilder().QuoteIdentifier(fixture.Database.Options.SchemaName);
            await using var command = new NpgsqlCommand(
                $"update {schema}.ledger_event_audit_head set schema_version=2 where chain_id=1", connection);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var replay = () => fixture.Store.AppendAsync(write);
        await replay.Should().ThrowAsync<LedgerValidationException>().WithMessage("*unsupported version*");
        Assert.Single(await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId));
    }

    [LedgerDatabaseFact]
    public async Task AmbientBatch_MixesCommittedReceiptAndNewPostingWithoutReapplyingReceipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = fixture.Write();
        await fixture.Store.AppendAsync(original);
        var next = fixture.Write();
        await using var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using var authority = await fixture.Store.AcquirePostingAuthorityAsync(connection, transaction, [original, next]);
        await fixture.Store.AppendAsync(connection, transaction, original);
        await fixture.Store.AppendAsync(connection, transaction, next);
        await transaction.CommitAsync();
        Assert.Equal(2, (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Count);
        _ = await fixture.Store.VerifyLedgerEventAuditAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTimeOffset Effective = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Formed = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public required LedgerPostgresTestDatabase Database { get; init; }
        public required PostgresLedgerJournalStore Store { get; init; }
        public required LedgerAccountingPeriod Period { get; init; }
        public required Guid EntityId { get; init; }
        public required EntityLifecycleRevisionDto Initial { get; init; }
        public required InMemoryEntityLifecycleStore History { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var database = await LedgerPostgresTestDatabase.CreateAsync();
            try
            {
                var entityId = Guid.NewGuid();
                var history = new InMemoryEntityLifecycleStore();
                var initial = EntityLifecyclePolicy.Seal(new EntityLifecycleRevisionDto("tenant-a", entityId, 1,
                    DateTimeOffset.UtcNow, "synthetic-owner", "Evidenced fixture formation", "fixture:formation", "", "",
                    [new(Guid.NewGuid(), Formed, null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation)]));
                await history.AppendAsync(initial, 0);
                var entity = new LegalEntitySummaryDto(entityId, LegalEntityTypeDto.Vehicle, "FIXTURE", "Synthetic replay entity",
                    "US", "USD", true, Formed, null);
                var graph = new OrganizationStructureGraphDto([], [], [], [], [], [], [entity], [], [], [], [], []);
                var structure = new Mock<IFundStructureService>();
                structure.Setup(x => x.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(graph);
                var lifecycle = new LifecycleLedgerPostingAuthority(structure.Object, history, () => null);
                // This test exercises real lifecycle eligibility; intercompany policy is unrelated
                // to its ordinary cash/equity fixture and receives an explicitly named test decision.
                var store = new PostgresLedgerJournalStore(database.Options, new FixtureTenant(),
                    postingAuthorities: () => [lifecycle, new OrdinaryFixtureIntercompanyAuthority()]);
                var owners = new PostgresFundProfileTenancyRegistry(database.Options);
                await owners.BindAsync("replay-fixture", "tenant-a", "company-a");
                var book = await store.SaveLedgerBookAsync(new(Guid.NewGuid(), "replay-fixture", entityId,
                    FundStructureNodeKindDto.Entity, "Synthetic entity book", "USD", Formed, Formed));
                var period = await store.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 6, "June",
                    new(2026, 6, 1), new(2026, 6, 30), "Open", Formed, null, 0), 0);
                return new() { Database = database, Store = store, Period = period, EntityId = entityId, Initial = initial, History = history };
            }
            catch { await database.DisposeAsync(); throw; }
        }

        public LedgerJournalEntryWrite Write(decimal amount = 100m, Guid? journalId = null)
        {
            var id = journalId ?? Guid.NewGuid();
            var dimensions = new LedgerLineDimensionSet(EntityId: EntityId.ToString("D"), BookId: Period.LedgerBookId!.Value.ToString("D"));
            var journal = new JournalEntry(id, Effective, "Synthetic replay posting",
                [new(Guid.NewGuid(), id, Effective, new("Assets:Cash", LedgerAccountType.Asset), amount, 0m, "Synthetic replay posting", dimensions),
                 new(Guid.NewGuid(), id, Effective, new("Equity:Capital", LedgerAccountType.Equity), 0m, amount, "Synthetic replay posting", dimensions)],
                new JournalEntryMetadata { EffectiveDate = new(2026, 6, 15), IdempotencyKey = $"replay-fixture:{id:D}" });
            return new(journal, EntityId, Period.PeriodId, LedgerBookId: Period.LedgerBookId);
        }

        public Task<EntityLifecycleRevisionDto> DissolveAsync()
        {
            var cutoff = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
            return History.AppendAsync(EntityLifecyclePolicy.Seal(Initial with
            {
                Revision = 2, PreviousHash = Initial.ContentHash, ContentHash = "", RecordedAtUtc = DateTimeOffset.UtcNow,
                Reason = "Evidenced fixture dissolution", EvidenceReference = "fixture:dissolution",
                Timeline = [Initial.Timeline[0] with { EffectiveTo = cutoff },
                    new(Guid.NewGuid(), cutoff, null, LegalEntityLifecycleStatusDto.Dissolved, LegalEntityLifecycleEventKindDto.Dissolution)]
            }), 1);
        }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
    private sealed class FixtureTenant : IFundScopeTenantAccessor { public string ResolveCallerTenant() => "tenant-a"; }
    private sealed class OrdinaryFixtureIntercompanyAuthority : ILedgerPostingAuthority
    {
        public string ControlName => "intercompany";
        public Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default)
        {
            foreach (var context in contexts) context.Decisions.Add(new(ControlName, "ordinary-synthetic-fixture", true,
                "This replay fixture contains no intercompany accounts."));
            return Task.FromResult<IAsyncDisposable>(EmptyLedgerAuthorityLease.Instance);
        }
    }
}
