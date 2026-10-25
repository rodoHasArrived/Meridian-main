using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// The governed FactorPaydown mutation against PostgreSQL: the worked lot (1,000,000 of par booked at
/// 0.95 at 101.5) pays 0.95 -> 0.90 and then 0.90 -> 0, every open lot of the scope is restated in
/// one batch, the factor is dated by the as-of read, and stale, partial-scope, changed-reference and
/// faulted postings leave nothing behind.
/// </summary>
public sealed partial class AtomicTaxLotJournalStoreTests
{
    private const string FactorPaydownMigration = "V_ledger_053__atomic_lot_factor_paydown.sql";
    private static readonly DateOnly FirstPaydown = new(2026, 6, 30);
    private static readonly DateOnly SecondPaydown = new(2026, 9, 30);

    [Fact]
    public void FactorPaydownMigration_WidensChecksAddsInstructionColumnAndTightensTheTrigger()
    {
        var sql = ReadMigration(FactorPaydownMigration);
        var ddl = StripSqlComments(sql);

        sql.Should().Contain("add column if not exists factor_paydown_instruction jsonb null");
        sql.Should().Contain("ck_atomic_tax_lot_factor_paydown_instruction");
        foreach (var check in new[] { "ck_atomic_tax_lot_batch_kind", "ck_tax_lot_mutations_kind", "ck_tax_lot_mutations_quantities", "ck_tax_lot_mutations_cost", "ck_tax_lots_current_factor" })
            ddl.Should().Contain($"drop constraint if exists {check}").And.Contain($"add constraint {check}");
        sql.Should().Contain("'CorporateAction', 'CapitalReturn', 'FactorPaydown'", "both kind checks admit the new kind as a strict superset");
        sql.Should().Contain("(mutation_kind = 'FactorPaydown' and quantity_before > 0 and relief_method is null and policy_revision is null");
        sql.Should().Contain("(quantity_delta = 0 or quantity_after = 0)", "a partial paydown keeps quantity, a full paydown closes the lot");
        sql.Should().Contain("or mutation_kind = 'FactorPaydown'", "cost_basis is the basis relieved, which a zero-basis lot states as 0");
        sql.Should().Contain("current_factor >= 0 and", "V053 admits a factor of zero for a fully repaid pool");
        sql.Should().Contain("current_factor < booked_factor");
        sql.Should().Contain("create or replace function __SCHEMA__.protect_open_lot_current_factor()");
        sql.Should().Contain("paydown.mutation_kind = 'FactorPaydown'");
        sql.Should().Contain("birth.mutation_kind = 'CorporateAction'", "a successor may open with its predecessor's factor");
        sql.Should().Contain("new.basis_adjustment ->> 'Reason' is distinct from 'FactorPaydown'");

        // Nothing is dropped beyond the replaced checks, nothing is backfilled and the V052 trigger object is kept.
        ddl.Should().NotContain("drop trigger");
        ddl.Should().NotContain("drop column");
        ddl.Should().NotContain("not null default");
        ddl.Should().NotContain("update __SCHEMA__.tax_lots");
        ddl.Should().NotContain("protect_open_lot_acquisition");
        ddl.Should().NotContain("protect_open_lot_basis_adjustment");
    }

    [Fact]
    public void Fingerprint_FactorPaydownJoinsDigestOnlyWhenPresent()
    {
        var command = BuildAcquisitionCommand(canonicalFacts: false);
        AtomicTaxLotJournalFingerprint.Compute(command).Should().Be(command.CanonicalFingerprint,
            "a command without a paydown instruction keeps the digest it had before the field existed");
        var instruction = FactorPaydownTestData.Build([FactorPaydownTestData.PremiumLot()], 0.95m, 0.90m);
        AtomicTaxLotJournalFingerprint.Compute(command with { FactorPaydown = instruction }).Should().NotBe(command.CanonicalFingerprint);
        System.Text.Json.JsonSerializer.Serialize(command).Should().NotContain("factorPaydown").And.NotContain("FactorPaydown");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_PartialPaydown_SetsFactorRelievesProRataBasisBooksLossAndReplaysAfterRestartAndLock()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var before = fixture.Lot;
        before.ToOpenLot().OpenFunctionalCostBasis.Should().Be(964_250m);
        var command = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown);
        var instruction = command.FactorPaydown!;
        instruction.PrincipalPaydown.Should().Be(50_000m);

        var result = await fixture.Store.AppendAssetPostingAsync(command);

        var after = result.MutatedLots.Should().ContainSingle().Subject;
        after.CurrentFactor.Should().Be(0.90m);
        after.OpenQuantity.Should().Be(before.OpenQuantity, "a partial paydown preserves quantity");
        after.Version.Should().Be(before.Version + 1);
        after.Acquisition.Should().BeEquivalentTo(before.Acquisition);
        after.BookedFactor.Should().Be(0.95m);
        after.ToOpenLot().Should().BeEquivalentTo(OpenLotFactorPaydown.Project(instruction).Single());
        after.ToOpenLot().OpenTransactionCostBasis.Should().Be(913_500m);
        after.ToOpenLot().OpenFunctionalCostBasis.Should().Be(913_500m);
        after.ToFaceValueLot()!.FaceOutstanding.Should().Be(900_000m);
        after.BasisAdjustment.Should().NotBeNull();
        after.BasisAdjustment!.Reason.Should().Be(OpenLotBasisAdjustmentReasons.FactorPaydown);
        after.BasisAdjustment.MutationBatchId.Should().Be(command.MutationBatchId);
        after.BasisAdjustment.FactorPaydown.Should().BeEquivalentTo(instruction);
        var mutation = result.Mutations.Should().ContainSingle().Subject;
        mutation.MutationKind.Should().Be(AtomicTaxLotMutationKind.FactorPaydown);
        mutation.QuantityDelta.Should().Be(0m);
        mutation.CostBasis.Should().Be(50_750m, "the mutation retains the functional basis relieved, not the cash");
        mutation.LotBefore.Should().BeEquivalentTo(before);
        mutation.LotAfter.CurrentFactor.Should().Be(0.90m);
        result.FactorPaydown.Should().BeEquivalentTo(instruction);
        var lines = result.Journal.Entry.Lines;
        lines.Should().HaveCount(3);
        lines.Single(line => line.Account == LedgerAccounts.Cash).Debit.Should().Be(50_000m);
        lines.Single(line => line.Account == AmortAccount).Credit.Should().Be(50_750m);
        lines.Single(line => line.Account == LedgerAccounts.RealizedLoss).Debit.Should().Be(750m);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));

        // Every lot read path sees the factor, and the as-of read dates it: the day before the paydown
        // the lot still reads at its booked factor and full face.
        var restarted = fixture.Restart();
        (await restarted.GetAtomicTaxLotPostingAsync(command.MutationBatchId))!.FactorPaydown.Should().BeEquivalentTo(instruction);
        (await restarted.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Should().ContainSingle().Which.CurrentFactor.Should().Be(0.90m);
        (await restarted.GetTaxLotsByLotIdsAsync(fixture.BookId, AmortAccount, [before.LotId])).Should().ContainSingle().Which.CurrentFactor.Should().Be(0.90m);
        var onDate = (await restarted.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, before.SecurityId, before.BookPositionId, FirstPaydown))
            .Should().ContainSingle().Subject;
        onDate.CurrentFactor.Should().Be(0.90m);
        onDate.OpenQuantity.Should().Be(before.OpenQuantity);
        var dayBefore = (await restarted.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, before.SecurityId, before.BookPositionId, FirstPaydown.AddDays(-1)))
            .Should().ContainSingle().Subject;
        dayBefore.CurrentFactor.Should().BeNull("the lot was still at its booked factor the day before");
        dayBefore.ToFaceValueLot()!.FaceOutstanding.Should().Be(950_000m);
        dayBefore.Version.Should().Be(after.Version, "only quantity and factor are restated by the as-of read");

        // Once applied, the factor moves only through another FactorPaydown batch: a raw decline on a
        // foreign batch, an increase and a removal are all refused and the booked factor stays frozen.
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        var schema = fixture.Options.SchemaName;
        async Task<string> RefusedAsync(string set, Guid batch)
        {
            await using var refused = connection.CreateCommand();
            refused.CommandText = $"""update "{schema}".tax_lots set {set}, version = version + 1, last_mutation_batch_id = @batch where tax_lot_record_id = @lot;""";
            refused.Parameters.AddWithValue("batch", batch);
            refused.Parameters.AddWithValue("lot", before.TaxLotRecordId);
            var act = async () => await refused.ExecuteNonQueryAsync();
            return (await act.Should().ThrowAsync<PostgresException>()).Which.Message;
        }
        (await RefusedAsync("current_factor = 0.85", before.OriginatingMutationBatchId!.Value)).Should().Contain("FactorPaydown mutation batch");
        (await RefusedAsync("current_factor = 0.92", Guid.NewGuid())).Should().Contain("can only decline");
        (await RefusedAsync("current_factor = null", Guid.NewGuid())).Should().Contain("cannot be removed");
        (await RefusedAsync("booked_factor = 0.90", Guid.NewGuid())).Should().Contain("append-only correction lineage");

        // Exact replay after a period lock returns the retained receipt; one journal exists.
        await fixture.LockPeriodAsync();
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(result.Mutations);
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_FullPaydownToZero_ClosesLotsAndIsExcludedFromLaterAsOfReads()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var lot = fixture.Lot;
        await fixture.Store.AppendAssetPostingAsync(await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown));
        var command = await FactorPaydownCommandAsync(fixture, 0m, SecondPaydown);
        command.FactorPaydown!.PrincipalPaydown.Should().Be(900_000m, "par face 1,000,000 x 0.90");

        var result = await fixture.Store.AppendAssetPostingAsync(command);

        var closed = result.MutatedLots.Should().ContainSingle().Subject;
        closed.CurrentFactor.Should().Be(0m);
        closed.OpenQuantity.Should().Be(0m, "a full paydown closes the lot");
        closed.ToOpenLot().OpenFunctionalCostBasis.Should().Be(0m);
        closed.ToFaceValueLot()!.FaceOutstanding.Should().Be(0m);
        closed.Version.Should().Be(lot.Version + 2);
        var mutation = result.Mutations.Single();
        mutation.QuantityDelta.Should().Be(-lot.OpenQuantity);
        mutation.QuantityAfter.Should().Be(0m);
        mutation.CostBasis.Should().Be(913_500m);
        var lines = result.Journal.Entry.Lines;
        lines.Single(line => line.Account == LedgerAccounts.Cash).Debit.Should().Be(900_000m);
        lines.Single(line => line.Account == AmortAccount).Credit.Should().Be(913_500m);
        lines.Single(line => line.Account == LedgerAccounts.RealizedLoss).Debit.Should().Be(13_500m);

        // Tie-out across both paydowns: cash 950,000 is the booked face, basis relieved 964,250 is the
        // acquisition basis, and the cumulative loss 14,250 is the premium paid at acquisition.
        var journals = await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId);
        journals.Should().HaveCount(2);
        journals.SelectMany(journal => journal.Entry.Lines).Where(line => line.Account == LedgerAccounts.Cash).Sum(line => line.Debit).Should().Be(950_000m);
        journals.SelectMany(journal => journal.Entry.Lines).Where(line => line.Account == AmortAccount).Sum(line => line.Credit).Should().Be(964_250m);
        journals.SelectMany(journal => journal.Entry.Lines).Where(line => line.Account == LedgerAccounts.RealizedLoss).Sum(line => line.Debit).Should().Be(14_250m);

        // The closed lot leaves the open-lot reads from its paydown date; before it, it reads at 0.90.
        var restarted = fixture.Restart();
        (await restarted.ListOpenTaxLotsAsync(fixture.BookId, AmortAccount)).Should().BeEmpty();
        (await restarted.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, lot.SecurityId, lot.BookPositionId, SecondPaydown)).Should().BeEmpty();
        var dayBefore = (await restarted.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, lot.SecurityId, lot.BookPositionId, SecondPaydown.AddDays(-1)))
            .Should().ContainSingle().Subject;
        dayBefore.CurrentFactor.Should().Be(0.90m);
        dayBefore.OpenQuantity.Should().Be(lot.OpenQuantity);
        (await restarted.GetTaxLotsByIdsAsync(fixture.BookId, [lot.TaxLotRecordId])).Should().ContainSingle().Which.CurrentFactor.Should().Be(0m);
        (await restarted.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();

        // Nothing is left to pay down: a further paydown finds no open lot in the scope.
        var exhausted = () => FactorPaydownCommandAsync(fixture, 0m, SecondPaydown.AddDays(1));
        await exhausted.Should().ThrowAsync<Exception>();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_MultiLot_AllocatesResidualToLastLotAndRequiresOneCarryingAccount()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        // A second piece of the same pool bought later at par: 1,000 of par face booked at 0.95.
        var second = await fixture.AcquireFactorLotAsync("factor-pool-lot-2", 950m, AmortAcquired.AddMonths(2), pricePercentOfPar: 100m);
        var command = await FactorPaydownCommandAsync(fixture, 0.916667m, FirstPaydown);
        var instruction = command.FactorPaydown!;
        instruction.Lots.Should().HaveCount(2);
        instruction.PrincipalPaydown.Should().Be(33_366.33m, "(1,000,000 + 1,000) x 0.033333 rounded once at the position");
        instruction.Lots[0].Principal.Should().Be(33_333.00m);
        instruction.Lots[1].Principal.Should().Be(33.33m, "the last lot absorbs the position residual");

        var result = await fixture.Store.AppendAssetPostingAsync(command);

        result.Mutations.Should().HaveCount(2);
        result.Mutations.Select(mutation => mutation.SelectionOrdinal).Should().Equal(0, 1);
        result.MutatedLots.Should().OnlyContain(lot => lot.CurrentFactor == 0.916667m);
        result.Mutations.Sum(mutation => mutation.CostBasis).Should().Be(result.Journal.Entry.Lines.Single(line => line.Account == AmortAccount).Credit);
        result.MutatedLots.Single(lot => lot.TaxLotRecordId == second.TaxLotRecordId).ToOpenLot().OpenFunctionalCostBasis.Should().Be(916.67m);
        (await fixture.Store.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, fixture.Lot.SecurityId, fixture.Lot.BookPositionId, FirstPaydown))
            .Should().HaveCount(2).And.OnlyContain(lot => lot.CurrentFactor == 0.916667m);

        // A lot held in another carrying account cannot join the same paydown batch.
        await using var split = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        await split.AcquireFactorLotAsync("factor-pool-lot-other-account", 950m, AmortAcquired.AddMonths(2),
            account: new LedgerAccount("Investment lots (other)", LedgerAccountType.Asset), pricePercentOfPar: 100m);
        var splitCommand = await FactorPaydownCommandAsync(split, 0.90m, FirstPaydown);
        var attempt = () => split.Store.AppendAssetPostingAsync(splitCommand);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*carrying account*");
        (await split.Store.GetByPeriodAsync(split.Period.PeriodId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_StaleLotOrSkippedOpenLotCannotAppendAJournal()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var winner = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown);
        // The same factor row prepared twice shares the projector's deterministic event id, so the
        // second posting is an identity collision; a re-run from different factor evidence against
        // the already paid-down lot fails the lot's version compare.
        var duplicate = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown);
        var stale = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown, sourceHash: new string('f', 64));
        duplicate.SourceEventId.Should().Be(winner.SourceEventId);
        stale.SourceEventId.Should().NotBe(winner.SourceEventId);
        var result = await fixture.Store.AppendAssetPostingAsync(winner);
        var collision = () => fixture.Restart().AppendAssetPostingAsync(duplicate);
        await collision.Should().ThrowAsync<LedgerValidationException>().WithMessage("*collision*");
        var attempt = () => fixture.Restart().AppendAssetPostingAsync(stale);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage("*changed*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(stale.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(duplicate.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(result.MutatedLots.Single());

        // A second paydown that names only one of two open lots would split the position's factor.
        await using var scope = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        await scope.AcquireFactorLotAsync("factor-pool-lot-2", 95_000m, AmortAcquired.AddMonths(2));
        var partialScope = await FactorPaydownCommandAsync(scope, 0.90m, FirstPaydown, lots: [scope.Lot]);
        var skipped = () => scope.Store.AppendAssetPostingAsync(partialScope);
        await skipped.Should().ThrowAsync<LedgerValidationException>().WithMessage("*every lot open in the scope*");
        (await scope.Store.GetByPeriodAsync(scope.Period.PeriodId)).Should().BeEmpty();
        (await scope.Store.GetTaxLotsByIdsAsync(scope.BookId, [scope.Lot.TaxLotRecordId])).Single().CurrentFactor.Should().BeNull();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_ChangedSecurityOrPositionRefusesBeforeWriting()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var command = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown);
        var security = fixture.Security;
        await fixture.Securities.UpsertProjectionAsync(security with { Version = security.Version + 1 });
        var staleSecurity = () => fixture.Restart().AppendAssetPostingAsync(command);
        await staleSecurity.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();

        await using var positionFixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var positionCommand = await FactorPaydownCommandAsync(positionFixture, 0.90m, FirstPaydown);
        await positionFixture.AdvancePositionAsync();
        var stalePosition = () => positionFixture.Restart().AppendAssetPostingAsync(positionCommand);
        await stalePosition.Should().ThrowAsync<LedgerValidationException>().WithMessage("*book-position*");
        (await positionFixture.Store.GetByPeriodAsync(positionFixture.Period.PeriodId)).Should().BeEmpty();
        (await positionFixture.Store.GetTaxLotsByIdsAsync(positionFixture.BookId, [positionFixture.Lot.TaxLotRecordId])).Single()
            .CurrentFactor.Should().BeNull();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_FailureAfterLotUpdateRollsBackJournalBatchFactorAndBasis_ThenRetries()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var command = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        var schema = fixture.Options.SchemaName;
        await using (var fault = connection.CreateCommand())
        {
            fault.CommandText = $"""
                create function {schema}.reject_factor_paydown() returns trigger language plpgsql as $body$
                begin raise exception 'Injected factor-paydown receipt failure'; end $body$;
                create trigger reject_factor_paydown before insert on {schema}.tax_lot_mutations
                for each row when (new.mutation_kind = 'FactorPaydown') execute function {schema}.reject_factor_paydown();
                """;
            await fault.ExecuteNonQueryAsync();
        }
        var attempt = () => fixture.Restart().AppendAssetPostingAsync(command);
        await attempt.Should().ThrowAsync<PostgresException>().WithMessage("*Injected factor-paydown*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        var untouched = (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single();
        untouched.Should().BeEquivalentTo(fixture.Lot);
        untouched.CurrentFactor.Should().BeNull();
        untouched.BasisAdjustment.Should().BeNull();
        await using (var removeFault = connection.CreateCommand())
        {
            removeFault.CommandText = $"drop trigger reject_factor_paydown on {schema}.tax_lot_mutations;";
            await removeFault.ExecuteNonQueryAsync();
        }
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_JournalOnlyAppendAndChangedReplayRefuseWithoutFurtherMutation()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var command = await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown);
        var journalOnly = () => fixture.Store.AppendAsync(command.Journal);
        await journalOnly.Should().ThrowAsync<LedgerValidationException>().WithMessage("*atomic journal/lot*");
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        var changed = command with { RetainedEvidence = [.. command.RetainedEvidence, BuildEvidence("different-review", 'd')] };
        var collision = () => fixture.Restart().AppendAssetPostingAsync(changed.WithComputedFingerprint());
        await collision.Should().ThrowAsync<LedgerValidationException>().WithMessage("*collision*");
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [fixture.Lot.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(result.MutatedLots.Single());
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_LaterDisposalRelievesTheReducedBasis()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        await fixture.Store.AppendAssetPostingAsync(await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown));

        // Sell 20% of the lot (1,900 of 9,500 units): relief is 20% of the paid-down basis 913,500.
        var disposal = await fixture.CurrentBasisDisposalAsync(1_900m, FirstPaydown.AddMonths(1), "factor-later-sale");
        disposal.Relief.FunctionalCostBasis.Should().Be(182_700m);
        var sold = await fixture.Restart().AppendAssetPostingAsync(disposal.Command);
        var remaining = sold.MutatedLots.Single();
        remaining.ToOpenLot().OpenFunctionalCostBasis.Should().Be(730_800m);
        remaining.CurrentFactor.Should().Be(0.90m, "disposal carries the factor forward unchanged");
        remaining.Acquisition.Should().BeEquivalentTo(fixture.Lot.Acquisition);

        // The disposal's retained before-snapshot carries the factor, so the face sold is the
        // paid-down face: 1,900 units of booked face restated at 0.90 / 0.95.
        var snapshot = sold.Mutations.Single().LotBefore!;
        snapshot.CurrentFactor.Should().Be(0.90m);
        snapshot.ToFaceValueLot()!.FaceOutstanding.Should().Be(900_000m);
        (1_900m * LedgerTaxLotFaceValueTerms.LedgerLotParBasis * 0.90m / 0.95m).Should().Be(180_000m);

        // The as-of read before the sale still dates both the quantity and the factor.
        var beforeSale = (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, fixture.Lot.SecurityId,
            fixture.Lot.BookPositionId, FirstPaydown)).Should().ContainSingle().Subject;
        beforeSale.OpenQuantity.Should().Be(9_500m);
        beforeSale.CurrentFactor.Should().Be(0.90m);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FactorPaydown_SuccessorInheritsCurrentFactorAndCanBePaidDownAgain()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        await fixture.Store.AppendAssetPostingAsync(await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown));

        var exchange = await fixture.Store.AppendAssetPostingAsync(await SuccessorCommandAsync(fixture, advanceRefunding: false));
        var predecessor = exchange.MutatedLots.Single(lot => lot.TaxLotRecordId == fixture.Lot.TaxLotRecordId);
        var successor = exchange.MutatedLots.Single(lot => lot.TaxLotRecordId != fixture.Lot.TaxLotRecordId);
        predecessor.OpenQuantity.Should().Be(0m);
        predecessor.CurrentFactor.Should().Be(0.90m, "closing the predecessor does not touch its factor");
        successor.CurrentFactor.Should().Be(0.90m, "the successor inherits the paid-down factor");
        successor.BookedFactor.Should().Be(0.95m);
        successor.ToFaceValueLot()!.FaceOutstanding.Should().Be(900_000m);
        successor.ToOpenLot().OpenFunctionalCostBasis.Should().Be(913_500m);
        var opening = exchange.Mutations.Single(mutation => mutation.TaxLotRecordId == successor.TaxLotRecordId);
        opening.LotAfter.CurrentFactor.Should().Be(0.90m);

        // The successor's as-of read seeds the factor from its opening snapshot, and a later paydown
        // of the successor's scope builds on the inherited factor.
        var successorSecurity = (await fixture.Securities.GetProjectionAsync(successor.SecurityId))!;
        var asOf = (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, successor.SecurityId, successor.BookPositionId,
            exchange.Journal.Entry.Metadata.EffectiveDate!.Value)).Should().ContainSingle().Subject;
        asOf.CurrentFactor.Should().Be(0.90m);
        var paydown = await FactorPaydownCommandAsync(fixture, 0.85m, SecondPaydown, security: successorSecurity);
        paydown.FactorPaydown!.PriorFactor.Should().Be(0.90m);
        paydown.FactorPaydown.PrincipalPaydown.Should().Be(50_000m);
        var again = await fixture.Store.AppendAssetPostingAsync(paydown);
        again.MutatedLots.Single().CurrentFactor.Should().Be(0.85m);
        again.MutatedLots.Single().ToOpenLot().OpenFunctionalCostBasis.Should().Be(862_750m, "913,500 x 0.85 / 0.90");
    }

    private static async Task<AtomicTaxLotJournalCommand> FactorPaydownCommandAsync(AmortFixture fixture, decimal newFactor,
        DateOnly date, IReadOnlyList<LedgerTaxLotRecord>? lots = null, SecurityProjectionRecord? security = null,
        string? sourceHash = null)
    {
        security ??= fixture.Security;
        var position = (await fixture.Positions.GetSecurityAsync(security.SecurityId)).BookPositions.Single();
        lots ??= await fixture.Store.ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, security.SecurityId, position.PositionId, date);
        var open = lots.Select(static lot => lot.ToOpenLot()).ToArray();
        var prior = open[0].CurrentFactor ?? open[0].Acquisition.FaceValueTerms!.BookedFactor;
        var instruction = FactorPaydownTestData.Build(open, prior, newFactor, security.Version, position.Version,
            fixture.Period.PeriodId, fixture.Period.Version, "amort-fund", OpenLotAmortization.SecurityHash(security), date,
            sourceHash, accountingBasis: position.BookContext.AccountingBasis, tenantId: null, companyId: null);
        var retained = FactorPaydownTestData.RetainedEvidence(instruction).ToArray();
        var eventRef = instruction.EconomicEvent;
        var components = OpenLotFactorPaydown.Components(instruction);
        var key = "factor-paydown:" + Guid.NewGuid().ToString("N");
        var journalId = Guid.NewGuid();
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var dimensions = fixture.Dimensions(security.SecurityId, position.PositionId);
        const string description = "Approved principal paydown";
        LedgerEntry Leg(LedgerAccount account, decimal debit, decimal credit)
            => new(Guid.NewGuid(), journalId, at, account, debit, credit, description, dimensions, new("USD", "USD", debit, credit, 1m));
        var lines = new List<LedgerEntry> { Leg(LedgerAccounts.Cash, components[OpenLotFactorPaydown.PrincipalComponent], 0m) };
        if (components[OpenLotFactorPaydown.BasisRelievedComponent] > 0m)
            lines.Add(Leg(lots[0].Account, 0m, components[OpenLotFactorPaydown.BasisRelievedComponent]));
        if (components[OpenLotFactorPaydown.RealizedGainComponent] > 0m)
            lines.Add(Leg(LedgerAccounts.RealizedGain, 0m, components[OpenLotFactorPaydown.RealizedGainComponent]));
        if (components[OpenLotFactorPaydown.RealizedLossComponent] > 0m)
            lines.Add(Leg(LedgerAccounts.RealizedLoss, components[OpenLotFactorPaydown.RealizedLossComponent], 0m));
        var tags = new Dictionary<string, string>(SecurityMasterLineageTags(security.SecurityId))
        { [OpenLotFactorPaydown.JournalFingerprintTag] = OpenLotFactorPaydown.Fingerprint(instruction) };
        var entry = new JournalEntry(journalId, at, description, lines,
            new(SecurityId: security.SecurityId, EffectiveDate: date, IdempotencyKey: key, Tags: tags));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            date, at, key, AccountingPostingIntentDto.Adjustment, eventRef.EventId, ExpectedVersion: fixture.Period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-factor-review",
            OperatorRationale: "Reviewed principal paydown, pro-rata basis relief and realized result", LedgerBookId: fixture.BookId)
        {
            Actor = "independent-controller",
            LotFactorPaydown = instruction,
            SourceEventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            BookContext = position.BookContext with { PeriodId = fixture.Period.PeriodId },
            BookPositionId = position.PositionId,
            EconomicEvent = eventRef,
            ProjectionLineage = instruction.Lineage,
            RulePackReference = new("canonical-factor-paydown", "1", "factor-paydown", "1"),
            Evidence = retained.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference,
                Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate,
                EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray()
        };
        var journal = new LedgerJournalEntryWrite(entry, fixture.BookId, fixture.Period.PeriodId,
            AccountingBasis: position.BookContext.AccountingBasis, AccountingPolicyId: "amort-policy", AccountingPolicyVersion: "1",
            RuleId: "factor-paydown", RuleVersion: "1", SourceEventId: eventRef.EventId, LedgerBookId: fixture.BookId, PostingCommand: posting);
        return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, journal, eventRef.EventId, key,
            fixture.Period.Version, AtomicTaxLotMutationKind.FactorPaydown, retained, factorPaydown: instruction);
    }
}
