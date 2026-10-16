using FluentAssertions;
using Meridian.Contracts.Integrity;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotSuccessorMigrationTests
{
    private const string CapitalReturnMigration = "V_ledger_050__atomic_lot_capital_return.sql";

    [LedgerDatabaseFact]
    public async Task CapitalReturnUpgrade_PopulatedLedgerRetainsHistoryThroughReapplyAndRestart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        await using var fixture = await MigrationFixture.CreateAsync(ct, CapitalReturnMigration);
        await fixture.SeedAsync(corporateAction: false, ct);
        await fixture.SeedAsync(corporateAction: true, ct);
        var history = await fixture.HistoryAsync(ct);
        var receipts = await fixture.ReceiptsAsync(excludeSuccessor: true, ct);
        await fixture.WriteSuccessorAsync(fixture.SuccessorSql, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.atomic_tax_lot_posting_batches " +
            "where capital_return_instruction is not null", ct)).Should().Be("0");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.tax_lot_mutations", ct)).Should().Be("13");
        await fixture.SeedAsync(corporateAction: false, ct, capitalReturn: true);
        var withReturn = await fixture.HistoryAsync(ct);
        var constraints = await fixture.ConstraintsAsync(ct);
        var instruction = await fixture.ScalarAsync($"select capital_return_instruction::text " +
            $"from {fixture.Schema}.atomic_tax_lot_posting_batches where mutation_kind = 'CapitalReturn'", ct);
        var reapply = fixture.SuccessorSql + "\n-- test-local checksum drift for explicit 050 reapply\n";
        await fixture.WriteSuccessorAsync(reapply, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.ChecksumAsync(ct)).Should().Be(Sha256Digest.ComputeUtf8(reapply));
        (await fixture.HistoryAsync(ct)).Should().Be(withReturn);
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints);
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select capital_return_instruction::text " +
            $"from {fixture.Schema}.atomic_tax_lot_posting_batches where mutation_kind = 'CapitalReturn'", ct)).Should().Be(instruction);
        var allReceipts = await fixture.ReceiptsAsync(excludeSuccessor: false, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(withReturn);
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints);
        (await fixture.ReceiptsAsync(excludeSuccessor: false, ct)).Should().Be(allReceipts);
    }

    [LedgerDatabaseFact]
    public async Task CapitalReturnUpgrade_FailedDdlRestoresChecksHistoryAndReceiptsBeforeRetry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        await using var fixture = await MigrationFixture.CreateAsync(ct, CapitalReturnMigration);
        await fixture.SeedAsync(corporateAction: false, ct);
        await fixture.SeedAsync(corporateAction: true, ct);
        var history = await fixture.HistoryAsync(ct);
        var constraints = await fixture.ConstraintsAsync(ct);
        var receipts = await fixture.ReceiptsAsync(excludeSuccessor: false, ct);
        const string finalCheck = "alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_cost check (";
        var faultOffset = fixture.SuccessorSql.IndexOf(finalCheck, StringComparison.Ordinal);
        faultOffset.Should().BeGreaterThan(0);
        await fixture.WriteSuccessorAsync(fixture.SuccessorSql.Insert(faultOffset,
            "do $$ begin raise exception 'injected capital-return migration failure'; end $$;\n"), ct);
        var upgrade = () => fixture.Runner().EnsureMigratedAsync(ct);
        await upgrade.Should().ThrowAsync<PostgresException>().WithMessage("*injected capital-return migration failure*");
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints);
        (await fixture.ReceiptsAsync(excludeSuccessor: false, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync("select count(*) from information_schema.columns " +
            $"where table_schema = '{fixture.Schema}' and table_name = 'atomic_tax_lot_posting_batches' " +
            "and column_name = 'capital_return_instruction'", ct)).Should().Be("0");
        await fixture.WriteSuccessorAsync(fixture.SuccessorSql, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ChecksumAsync(ct)).Should().Be(Sha256Digest.ComputeUtf8(fixture.SuccessorSql));
    }
}
