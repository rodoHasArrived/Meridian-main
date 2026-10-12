using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_SeparateDeferralRegardlessOfCarryOrSaleDate_RefusesWithoutAnyWrite()
    {
        foreach (var (carryOffset, saleOffset) in new[] { (-1, -1), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 1) })
        {
            await using var fixture = await AmortFixture.CreateAsync(premium: true);
            var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
            var carry = SuccessorCarry(fixture, fixture.Lot,
                command.CorporateAction!.ExpectedLot.Acquisition.HoldingPeriodStartDate.AddDays(carryOffset),
                command.CorporateAction.Projection.EconomicEvent!.EffectiveDate.AddDays(saleOffset));
            await fixture.Store.SaveWashSaleDeferralsAsync([carry]);

            var post = () => fixture.Restart().AppendAssetPostingAsync(command);
            await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*separately retained wash-sale basis or holding-period claim*");
            await AssertSuccessorUnchangedAsync(fixture, command);
            (await fixture.Store.ListWashSaleDeferralsAsync(fixture.BookId, DateOnly.MinValue, DateOnly.MaxValue))
                .Should().ContainSingle().Which.Should().BeEquivalentTo(carry);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_PrecedingDeferralWriterIsVisibleBeforeSerializableSnapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var inherited = command.CorporateAction!.ExpectedLot.Acquisition.HoldingPeriodStartDate;
        var carry = SuccessorCarry(fixture, fixture.Lot, inherited, OpenLotSuccessorTestData.EffectiveDate);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var writer = await connection.BeginTransactionAsync(ct);
        await InsertSuccessorCarryFixtureAsync(connection, writer, fixture.Options.SchemaName, carry, ct);

        var posting = fixture.Restart().AppendAssetPostingAsync(command, ct);
        await WaitForSuccessorCarryLockAsync(connection, writer, fixture.Options.SchemaName, ct);
        posting.IsCompleted.Should().BeFalse("the successor must wait for preceding deferral retention before taking its snapshot");
        await writer.CommitAsync(ct);
        var completed = () => posting;
        await completed.Should().ThrowAsync<LedgerValidationException>().WithMessage("*separately retained wash-sale basis or holding-period claim*");
        await AssertSuccessorUnchangedAsync(fixture, command);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_LateCarryRefusesAndLegacyAncestorCarryDoesNotBlockExactReplay()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var source = await AcquireSplitAncestryUnitsAsync(fixture, holdingPeriodStart: AmortAsOf.AddYears(-1));
        var date = OpenLotSuccessorTestData.EffectiveDate;
        var command = await SplitAncestryCommandAsync(fixture, source, Guid.NewGuid(), date, 2m, legacyProjection: false);
        var inherited = source.Acquisition!.HoldingPeriodStartDate;
        var posted = await fixture.Store.AppendAssetPostingAsync(command);
        var counts = await ReadSplitAncestryCountsAsync(fixture);
        var successor = posted.Mutations.Single(mutation => mutation.LotBefore is null).LotAfter;
        successor.Acquisition!.HoldingPeriodStartDate.Should().Be(inherited).And.BeBefore(source.AcquiredDate);
        var legacyCarry = SuccessorCarry(fixture, source, inherited, date);
        foreach (var offset in new[] { -1, 0, 1 })
        {
            var late = () => fixture.Restart().SaveWashSaleDeferralsAsync([legacyCarry with
            { HoldingPeriodCarryDate = inherited.AddDays(offset) }]);
            await late.Should().ThrowAsync<LedgerValidationException>().WithMessage("*cannot be retained after its replacement lot became a successor predecessor*");
        }
        (await fixture.Store.ListWashSaleDeferralsAsync(fixture.BookId, DateOnly.MinValue, DateOnly.MaxValue))
            .Should().BeEmpty();

        // Reproduce retained legacy data predating the guard, without teaching the supported
        // writer to mutate or duplicate a retained deferral. Replay must still return its receipt.
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await InsertSuccessorCarryFixtureAsync(connection, null, fixture.Options.SchemaName, legacyCarry, default);
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(posted.Mutations);
        await fixture.Restart().SaveWashSaleDeferralsAsync([legacyCarry]);
        foreach (var changed in new[]
        {
            legacyCarry with { DeferralId = Guid.NewGuid() },
            legacyCarry with { DisallowedAmount = legacyCarry.DisallowedAmount + 1m },
            legacyCarry with { PolicyId = "different-retained-policy" }
        })
        {
            var changedReplay = () => fixture.Restart().SaveWashSaleDeferralsAsync([changed]);
            await changedReplay.Should().ThrowAsync<LedgerValidationException>().WithMessage("*cannot be retained after its replacement lot became a successor predecessor*");
        }
        (await fixture.Store.ListWashSaleDeferralsAsync(fixture.BookId, DateOnly.MinValue, DateOnly.MaxValue))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(legacyCarry);
        var next = await SplitAncestryCommandAsync(fixture, successor, Guid.NewGuid(), date.AddDays(1), 2m, legacyProjection: false);
        var repeat = () => fixture.Restart().AppendAssetPostingAsync(next);
        await repeat.Should().ThrowAsync<LedgerValidationException>().WithMessage("*separately retained wash-sale basis or holding-period claim*");
        (await ReadSplitAncestryCountsAsync(fixture)).Should().Be(counts);
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [successor.TaxLotRecordId]))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(successor);
    }

    private static WashSaleDeferralRecord SuccessorCarry(AmortFixture fixture, LedgerTaxLotRecord recipient,
        DateOnly holdingDate, DateOnly saleDate)
        => new(Guid.NewGuid(), fixture.BookId, fixture.Lot.LastMutationBatchId!.Value, recipient.SecurityId,
            saleDate, recipient.Account, recipient.TaxLotRecordId, recipient.LotId, 1m, 1m, holdingDate,
            "amort-fifo-v1", 30, WashSaleReplacementScope.LedgerBook,
            DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    private static async Task InsertSuccessorCarryFixtureAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string schema, WashSaleDeferralRecord carry, CancellationToken ct)
    {
        // Direct fixture insertion models an in-flight writer or data retained before the new guard.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into "{schema}".wash_sale_deferrals (
                deferral_id, ledger_book_id, disposal_mutation_batch_id, security_id, sale_date,
                disposal_account_name, disposal_account_type, disposal_symbol, disposal_financial_account_id,
                replacement_tax_lot_record_id, replacement_lot_id, disallowed_amount, matched_quantity,
                holding_period_carry_date, policy_id, window_days, scope, recorded_at)
            values (@id, @book, @batch, @security, @sale, @account, @type, @symbol, @financial,
                @lot, @label, @amount, @quantity, @carry, @policy, @window, @scope, @recorded);
            """;
        command.Parameters.AddWithValue("id", carry.DeferralId);
        command.Parameters.AddWithValue("book", carry.LedgerBookId);
        command.Parameters.AddWithValue("batch", carry.DisposalMutationBatchId);
        command.Parameters.AddWithValue("security", carry.SecurityId);
        command.Parameters.AddWithValue("sale", carry.SaleDate);
        command.Parameters.AddWithValue("account", carry.DisposalAccount.Name);
        command.Parameters.AddWithValue("type", carry.DisposalAccount.AccountType.ToString());
        command.Parameters.AddWithValue("symbol", NpgsqlTypes.NpgsqlDbType.Text, (object?)carry.DisposalAccount.Symbol ?? DBNull.Value);
        command.Parameters.AddWithValue("financial", NpgsqlTypes.NpgsqlDbType.Text, (object?)carry.DisposalAccount.FinancialAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("lot", carry.ReplacementTaxLotRecordId);
        command.Parameters.AddWithValue("label", carry.ReplacementLotId);
        command.Parameters.AddWithValue("amount", carry.DisallowedAmount);
        command.Parameters.AddWithValue("quantity", carry.MatchedReplacementQuantity);
        command.Parameters.AddWithValue("carry", carry.HoldingPeriodCarryDate);
        command.Parameters.AddWithValue("policy", carry.PolicyId);
        command.Parameters.AddWithValue("window", carry.WindowDays);
        command.Parameters.AddWithValue("scope", carry.Scope.ToString());
        command.Parameters.AddWithValue("recorded", carry.RecordedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task WaitForSuccessorCarryLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string schema, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select exists(select 1 from pg_locks where relation = @relation::regclass and mode = 'ShareLock' and not granted);";
        command.Parameters.AddWithValue("relation", $"\"{schema}\".wash_sale_deferrals");
        while (!(bool)(await command.ExecuteScalarAsync(ct))!)
            await Task.Delay(20, ct);
    }
}
