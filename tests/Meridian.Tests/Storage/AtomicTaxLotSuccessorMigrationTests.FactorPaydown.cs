using FluentAssertions;
using Meridian.Contracts.Integrity;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotSuccessorMigrationTests
{
    private const string FactorPaydownMigration = "V_ledger_053__atomic_lot_factor_paydown.sql";

    [LedgerDatabaseFact]
    public async Task FactorPaydownUpgrade_PopulatedLedgerRetainsHistoryThroughReapplyAndRestart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        await using var fixture = await MigrationFixture.CreateAsync(ct, FactorPaydownMigration);
        await fixture.SeedAsync(corporateAction: false, ct);
        await fixture.SeedAsync(corporateAction: true, ct);
        await fixture.SeedAsync(corporateAction: false, ct, capitalReturn: true);
        var history = await fixture.HistoryAsync(ct);
        var receipts = await fixture.ReceiptsAsync(excludeSuccessor: true, ct);
        var triggerBefore = await fixture.TriggerFunctionAsync("protect_open_lot_current_factor", ct);
        triggerBefore.Should().NotContain("FactorPaydown", "through 052 the trigger does not yet know the paydown batch kind");

        await fixture.WriteSuccessorAsync(fixture.SuccessorSql, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);

        (await fixture.HistoryAsync(ct)).Should().Be(history, "053 must preserve every retained row, including the paid-up-to-052 columns");
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.atomic_tax_lot_posting_batches " +
            "where factor_paydown_instruction is not null", ct)).Should().Be("0");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.tax_lot_mutations", ct)).Should().Be("14");
        (await fixture.TriggerFunctionAsync("protect_open_lot_current_factor", ct)).Should().Contain("FactorPaydown mutation batch");
        (await fixture.ScalarAsync($"select pg_get_constraintdef(oid) from pg_constraint where conname = 'ck_tax_lots_current_factor' " +
            $"and conrelid = '{fixture.Schema}.tax_lots'::regclass", ct)).Should().Contain(">= (0)");

        // Partial, full and zero-basis paydown rows are admitted by the widened checks.
        await fixture.SeedAsync(corporateAction: false, ct, factorPaydown: true);
        var withPaydowns = await fixture.HistoryAsync(ct);
        var constraints = await fixture.ConstraintsAsync(ct);
        var instructions = await fixture.ScalarAsync($"select jsonb_agg(factor_paydown_instruction order by mutation_batch_id)::text " +
            $"from {fixture.Schema}.atomic_tax_lot_posting_batches where mutation_kind = 'FactorPaydown'", ct);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.tax_lot_mutations where mutation_kind = 'FactorPaydown'", ct)).Should().Be("3");

        // Reapply through the production drift policy, with only a test-local comment changed.
        var reapply = fixture.SuccessorSql + "\n-- test-local checksum drift for explicit 053 reapply\n";
        await fixture.WriteSuccessorAsync(reapply, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.ChecksumAsync(ct)).Should().Be(Sha256Digest.ComputeUtf8(reapply));
        (await fixture.HistoryAsync(ct)).Should().Be(withPaydowns);
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints);
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select jsonb_agg(factor_paydown_instruction order by mutation_batch_id)::text " +
            $"from {fixture.Schema}.atomic_tax_lot_posting_batches where mutation_kind = 'FactorPaydown'", ct)).Should().Be(instructions);

        var allReceipts = await fixture.ReceiptsAsync(excludeSuccessor: false, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(withPaydowns);
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints);
        (await fixture.ReceiptsAsync(excludeSuccessor: false, ct)).Should().Be(allReceipts);
    }

    [LedgerDatabaseFact]
    public async Task FactorPaydownUpgrade_FailedDdlRestoresChecksTriggerHistoryAndReceiptsBeforeRetry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        await using var fixture = await MigrationFixture.CreateAsync(ct, FactorPaydownMigration);
        await fixture.SeedAsync(corporateAction: false, ct);
        await fixture.SeedAsync(corporateAction: true, ct);
        var history = await fixture.HistoryAsync(ct);
        var constraints = await fixture.ConstraintsAsync(ct);
        var receipts = await fixture.ReceiptsAsync(excludeSuccessor: false, ct);
        var trigger = await fixture.TriggerFunctionAsync("protect_open_lot_current_factor", ct);
        const string finalStatement = "create or replace function __SCHEMA__.protect_open_lot_current_factor()";
        var faultOffset = fixture.SuccessorSql.IndexOf(finalStatement, StringComparison.Ordinal);
        faultOffset.Should().BeGreaterThan(0);
        await fixture.WriteSuccessorAsync(fixture.SuccessorSql.Insert(faultOffset,
            "do $$ begin raise exception 'injected factor-paydown migration failure'; end $$;\n"), ct);

        var upgrade = () => fixture.Runner().EnsureMigratedAsync(ct);
        await upgrade.Should().ThrowAsync<PostgresException>().WithMessage("*injected factor-paydown migration failure*");
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints,
            "the transaction must restore every check replaced earlier in the failed migration, including ck_tax_lots_current_factor");
        (await fixture.TriggerFunctionAsync("protect_open_lot_current_factor", ct)).Should().Be(trigger);
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeSuccessor: false, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync("select count(*) from information_schema.columns " +
            $"where table_schema = '{fixture.Schema}' and table_name = 'atomic_tax_lot_posting_batches' " +
            "and column_name = 'factor_paydown_instruction'", ct)).Should().Be("0");

        await fixture.WriteSuccessorAsync(fixture.SuccessorSql, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ChecksumAsync(ct)).Should().Be(Sha256Digest.ComputeUtf8(fixture.SuccessorSql));
        (await fixture.TriggerFunctionAsync("protect_open_lot_current_factor", ct)).Should().NotBe(trigger);
    }
}
