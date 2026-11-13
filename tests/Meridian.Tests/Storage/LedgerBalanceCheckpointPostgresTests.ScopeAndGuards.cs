using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class LedgerBalanceCheckpointPostgresTests
{
    [LedgerDatabaseFact]
    public async Task NoCheckpointAndHistoricalDateBeforeClose_MatchImmutableJournalReplay()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAndPeriod(database);
        await Append(database, book, period, 50m, occurredAt: DateTimeOffset.Parse("2026-05-05T12:00:00Z"));
        await Append(database, book, period, 25m, occurredAt: DateTimeOffset.Parse("2026-05-20T12:00:00Z"));
        var historical = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
        var dimensions = new LedgerLineDimensionSet(EntityId: "ENTITY-A");
        var replay = await database.JournalStore.HydrateLedgerAsOfAsync(book.LedgerBookId, historical);
        var expected = replay.TrialBalanceAsOf(historical, " BROKER-A ", dimensions);
        expected[Cash("broker-a")].Should().Be(50m);

        var withoutCheckpoint = await database.JournalStore.ReadTrialBalanceAsOfAsync(
            book.LedgerBookId, historical, " BROKER-A ", dimensions);
        withoutCheckpoint.Should().BeEquivalentTo(expected);
        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoints")).Should().Be(0);

        await HardClose(database, period);

        var beforeCheckpoint = await database.RestartJournalStore().ReadTrialBalanceAsOfAsync(
            book.LedgerBookId, historical, " BROKER-A ", dimensions);
        beforeCheckpoint.Should().BeEquivalentTo(expected,
            "a later close cannot certify balances at an earlier historical cutoff");
        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoints")).Should().Be(1);
    }

    [LedgerDatabaseFact]
    public async Task StrictCheckpointRead_RequiresActualRetainedFundAndBookOwnership()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync("fund-alpha", "tenant-alpha");
        await registry.BindAsync("fund-beta", "tenant-beta");
        var now = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var alpha = await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(Guid.NewGuid(),
            "fund-alpha", Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Alpha checkpoint book", "USD", now, now));
        var beta = await database.JournalStore.SaveLedgerBookAsync(alpha with
        {
            LedgerBookId = Guid.NewGuid(),
            FundProfileId = "fund-beta",
            FundStructureNodeId = Guid.NewGuid()
        });
        var strict = new PostgresLedgerJournalStore(database.Options, new CheckpointWorkerAccessor(),
            TenantScopeEnforcementOptions.FailClosed,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);
        using (FundScopeTenantAuthority.Enter("tenant-alpha", "checkpoint retained owner"))
        {
            var period = await CreatePeriod(database, alpha, 5, strict);
            await Append(database, alpha, period, 100m, store: strict);
            var soft = await strict.SavePeriodAsync(period with { Status = "SoftClosed", ClosedAt = MayEnd },
                period.Version, CloseEvent(period, "SoftClosed"));
            await strict.SaveHardClosedPeriodAsync(soft with { Status = "HardClosed" },
                soft.Version, CloseEvent(soft, "HardClosed"));
            (await strict.ReadTrialBalanceAsOfAsync(alpha.LedgerBookId, MayEnd))[Cash("broker-a")].Should().Be(100m);
            Func<Task> foreignBook = () => strict.ReadTrialBalanceAsOfAsync(beta.LedgerBookId, MayEnd);
            await foreignBook.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoints where tenant_id='tenant-alpha'"))
            .Should().Be(1, "the retained checkpoint itself must carry the real owner scope");

        foreach (var tenant in new string?[] { null, "tenant-beta" })
        {
            using var authority = tenant is null ? null : FundScopeTenantAuthority.Enter(tenant, "denied checkpoint read");
            Func<Task> read = () => strict.ReadTrialBalanceAsOfAsync(alpha.LedgerBookId, MayEnd);
            await read.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }

    [LedgerDatabaseFact]
    public async Task NativeCheckpointTables_RejectUpdateDeleteAndTruncateAtTheDatabase()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAndPeriod(database);
        await Append(database, book, period, 100m);
        await HardClose(database, period);
        var mutations = new[]
        {
            "update {schema}.ledger_balance_checkpoints set content_hash=content_hash",
            "delete from {schema}.ledger_balance_checkpoints",
            "truncate {schema}.ledger_balance_checkpoints, {schema}.ledger_balance_checkpoint_rows",
            "update {schema}.ledger_balance_checkpoint_rows set debit=debit",
            "delete from {schema}.ledger_balance_checkpoint_rows",
            "truncate {schema}.ledger_balance_checkpoint_rows"
        };
        foreach (var mutation in mutations)
        {
            Func<Task> write = () => Execute(database, mutation);
            var rejected = await write.Should().ThrowAsync<PostgresException>();
            rejected.Which.SqlState.Should().Be("55000",
                "the append-only trigger must reject the write before foreign keys or other incidental constraints");
        }

        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoints")).Should().Be(1);
        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoint_rows")).Should().Be(2);
        (await database.RestartJournalStore().ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd))[Cash("broker-a")]
            .Should().Be(100m);
    }

    private sealed class CheckpointWorkerAccessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => FundScopeTenantAuthority.CurrentTenantId;
    }
}
