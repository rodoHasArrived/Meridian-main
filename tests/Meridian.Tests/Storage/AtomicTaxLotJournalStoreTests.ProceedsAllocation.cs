using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [Fact]
    public void DisposalAllocation_AbsentQuotePreservesFrozenPrePriceFingerprint()
    {
        // Frozen against the version-one payload schema at 9d48c1ac, independently canonicalized
        // and hashed without the current implementation. This minimal envelope isolates its
        // serialization contract; PostgreSQL tests separately cover accepted retained evidence.
        const string legacyFingerprint = "sha256:72a38c5cf54f1044692e3ce8ea9988b84b192401c786a65c5711deba8b6f5f4b";
        var bookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var journalId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var sourceEventId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var at = DateTimeOffset.Parse("2026-05-12T14:00:00-07:00");
        const string description = "Legacy disposal";
        var journal = new LedgerJournalEntryWrite(new JournalEntry(journalId, at, description,
        [
            new LedgerEntry(Guid.Parse("44444444-4444-4444-4444-444444444444"), journalId, at,
                new("Cash", LedgerAccountType.Asset), 10m, 0m, description),
            new LedgerEntry(Guid.Parse("55555555-5555-5555-5555-555555555555"), journalId, at,
                new("Investments", LedgerAccountType.Asset), 0m, 10m, description)
        ]), bookId, Guid.Parse("66666666-6666-6666-6666-666666666666"),
            SourceEventId: sourceEventId, LedgerBookId: bookId);
        var command = AtomicTaxLotJournalCommand.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), bookId, journal, sourceEventId,
            "legacy-disposal", 0, AtomicTaxLotMutationKind.Disposal, [],
            disposalSelections: [new(Guid.Parse("88888888-8888-8888-8888-888888888888"),
                "legacy-lot", 1, 1m, 1m, 0, "legacy-evidence", 10m, 10m)],
            reliefMethod: "Fifo", policyRevision: "legacy-v1");

        command.CanonicalFingerprint.Should().Be(legacyFingerprint);
        JsonSerializer.Serialize(command).Should().NotContain("DisposalSalePrice");
        (command with { DisposalSalePrice = 10m }).WithComputedFingerprint().CanonicalFingerprint
            .Should().NotBe(legacyFingerprint);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalAllocation_NewUnquotedDisposalRetainsCurrentConvention()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, _, expected) = await PrepareProceedsDisposalAsync(database, precisePrice: false);
        command.DisposalSalePrice.Should().BeNull();
        var posted = await database.JournalStore.AppendAssetPostingAsync(command);
        var history = (await database.JournalStore.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
            [command.Journal.Entry.JournalEntryId])).Single();

        history.ProceedsAllocationVersion.Should().Be(LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion);
        history.SalePrice.Should().BeNull();
        var rebuilt = CanonicalDisposalHistoryProjector.Project(history, posted.Journal.Entry,
            command.LedgerBookId, "USD");
        rebuilt.Selections.Select(static selection => selection.Proceeds)
            .Should().Equal(expected.Selections.Select(static selection => selection.Proceeds));
        rebuilt.Selections.Select(static selection => selection.Proceeds)
            .Should().Equal(0.01m, 0.01m, 0.01m, 0.02m, 0.02m);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalAllocation_NonzeroWashSaleDeferralPreservesActualCashProceeds()
    {
        foreach (var explicitPrice in new[] { false, true })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false, washSale: true);
            var replacementAccount = lots[0].Account with { FinancialAccountId = "broker-2" };
            var timestamp = command.Journal.Entry.Timestamp;
            var replacement = await database.JournalStore.SaveTaxLotAsync(new LedgerTaxLotRecord(
                Guid.NewGuid(), command.LedgerBookId, replacementAccount, "wash-replacement", new(2026, 5, 15),
                1m, 1m, 80m, "USD", timestamp, timestamp, SecurityId: TestSecurityId, BookPositionId: TestBookPositionId));
            var journal = command.Journal.Entry;
            // The exact disposed-account credit remains 100. The deferred 20 is capitalized into a
            // separate replacement account, leaving 80 actual cash and zero currently recognized loss.
            command = (command with
            {
                DisposalSalePrice = explicitPrice ? 80m : null,
                Journal = command.Journal with
                {
                    Entry = new JournalEntry(journal.JournalEntryId, timestamp, journal.Description,
                        journal.Lines.Select(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name
                            ? new LedgerEntry(line.EntryId, journal.JournalEntryId, timestamp, replacementAccount,
                                20m, 0m, journal.Description, line.Dimensions, line.Currency)
                            : line).ToArray(), journal.Metadata)
                }
            }).WithComputedFingerprint();
            command.DisposalSalePrice.Should().Be(explicitPrice ? 80m : null);
            var posted = await database.JournalStore.AppendAssetPostingAsync(command);
            await database.JournalStore.SaveWashSaleDeferralsAsync([new(Guid.NewGuid(), command.LedgerBookId,
                command.MutationBatchId, TestSecurityId, new(2026, 5, 12), lots[0].Account,
                replacement.TaxLotRecordId, replacement.LotId, 20m, 1m, lots[0].AcquiredDate,
                "wash-sale-policy", 30, WashSaleReplacementScope.LedgerBook, timestamp)]);

            var reopened = new PostgresLedgerJournalStore(database.Options);
            var history = (await reopened.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
                [command.Journal.Entry.JournalEntryId])).Single();
            history.ProceedsAllocationVersion.Should().Be(LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion);
            history.SalePrice.Should().Be(explicitPrice ? 80m : null);
            var rebuilt = CanonicalDisposalHistoryProjector.Project(history, posted.Journal.Entry,
                command.LedgerBookId, "USD");
            rebuilt.Proceeds.Should().Be(80m);
            rebuilt.CostBasis.Should().Be(100m);
            rebuilt.RealizedGainOrLoss.Should().Be(-20m);
            rebuilt.DisallowedWashSaleLoss.Should().Be(20m);
            rebuilt.RecognizedGainOrLoss.Should().Be(0m);
            var increase = rebuilt.WashSale!.BasisIncreases.Should().ContainSingle().Which;
            increase.ReplacementLotId.Should().Be(replacement.LotId);
            increase.Amount.Should().Be(20m);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalAllocation_ExactPriceAndVersionSurvivePostgresRoundTrip()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, _, expected) = await PrepareProceedsDisposalAsync(database, precisePrice: true);
        var posted = await database.JournalStore.AppendAssetPostingAsync(command);
        var reopened = new PostgresLedgerJournalStore(database.Options);
        var history = (await reopened.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
            [command.Journal.Entry.JournalEntryId])).Single();

        history.ProceedsAllocationVersion.Should().Be(LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion);
        history.SalePrice.Should().Be(0.0051m);
        var rebuilt = CanonicalDisposalHistoryProjector.Project(history, posted.Journal.Entry,
            command.LedgerBookId, "USD");
        rebuilt.Selections.Select(static selection => selection.Proceeds)
            .Should().Equal(expected.Selections.Select(static selection => selection.Proceeds));
        rebuilt.LongTermRealizedGainOrLoss.Should().Be(-0.08m);
        rebuilt.ShortTermRealizedGainOrLoss.Should().Be(0.02m);
        rebuilt.RecognizedGainOrLoss.Should().Be(-0.06m);

        // Both prices produce the same rounded aggregate proceeds. Recovering .005 from .03/6
        // would nevertheless restate which holding-period bucket recognized two cents.
        var inferred = CanonicalDisposalHistoryProjector.Project(history with { SalePrice = 0.005m },
            posted.Journal.Entry, command.LedgerBookId, "USD");
        inferred.LongTermRealizedGainOrLoss.Should().Be(-0.06m);
        inferred.ShortTermRealizedGainOrLoss.Should().Be(0m);

        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = $"""
            update "{database.Options.SchemaName}".atomic_tax_lot_posting_batches
            set proceeds_allocation_version = null, disposal_sale_price = null
            where mutation_batch_id = @batch_id;
            """;
        update.Parameters.AddWithValue("batch_id", command.MutationBatchId);
        Func<Task> mutate = async () => { await update.ExecuteNonQueryAsync(); };
        await mutate.Should().ThrowAsync<PostgresException>();
        var after = (await reopened.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
            [command.Journal.Entry.JournalEntryId])).Single();
        after.ProceedsAllocationVersion.Should().Be(history.ProceedsAllocationVersion);
        after.SalePrice.Should().Be(history.SalePrice);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalAllocation_PriceDisagreeingWithJournalRollsBackWithoutMutation()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: true);
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var wrongPrice = (command with { DisposalSalePrice = 0.10m }).WithComputedFingerprint();
        wrongPrice.CanonicalFingerprint.Should().NotBe(command.CanonicalFingerprint);

        var post = () => database.JournalStore.AppendAssetPostingAsync(wrongPrice);
        await post.Should().ThrowAsync<LedgerValidationException>();

        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        (await database.JournalStore.GetTaxLotsByIdsAsync(command.LedgerBookId,
            lots.Select(static lot => lot.TaxLotRecordId).ToArray()))
            .Should().BeEquivalentTo(lots);
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalAllocation_LegacyNullBatchRetryKeepsFingerprintAndHistoricalParcels()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false);
        command.DisposalSalePrice.Should().BeNull();
        JsonSerializer.Serialize(command).Should().NotContain("DisposalSalePrice");
        var fingerprint = command.CanonicalFingerprint;
        await RetainLegacyProceedsDisposalAsync(database, command, lots);

        var reopened = new PostgresLedgerJournalStore(database.Options);
        var before = (await reopened.GetAtomicTaxLotPostingAsync(command.MutationBatchId))!;
        var replay = await reopened.AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.CanonicalFingerprint.Should().Be(fingerprint);
        replay.Mutations.Select(static mutation => mutation.MutationRecordId)
            .Should().Equal(before.Mutations.Select(static mutation => mutation.MutationRecordId));
        var history = (await reopened.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
            [command.Journal.Entry.JournalEntryId])).Single();
        history.ProceedsAllocationVersion.Should().BeNull();
        history.SalePrice.Should().BeNull();
        var rebuilt = CanonicalDisposalHistoryProjector.Project(history, replay.Journal.Entry,
            command.LedgerBookId, "USD");
        rebuilt.Selections.Select(static selection => selection.Proceeds)
            .Should().Equal(0.01m, 0.01m, 0.01m, 0.01m, 0.03m);
        rebuilt.Selections.Select(static selection => selection.RealizedGainOrLoss)
            .Should().Equal(0m, 0m, 0m, 0m, 0.01m);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalAllocation_ResultScopeAndSideAreRequiredForInferredAndExplicitPrices()
    {
        foreach (var explicitPrice in new[] { false, true })
            foreach (var gain in new[] { false, true })
                foreach (var defect in new[] { "sibling", "unscoped", "side", "type", "symbol", "symbol-without-lineage" })
                {
                    await using var database = await LedgerPostgresTestDatabase.CreateAsync();
                    var (command, lots, _) = await PrepareProceedsDisposalAsync(database, precisePrice: false,
                        financialAccountId: "broker-1");
                    var entry = command.Journal.Entry;
                    var resultAccount = gain ? LedgerAccounts.RealizedGainFor("broker-1")
                        : LedgerAccounts.RealizedLossFor("broker-1");
                    var wrongSide = defect == "side";
                    var debitResult = gain == wrongSide;
                    var cash = debitResult ? 0.05m : 0.07m;
                    resultAccount = defect switch
                    {
                        "sibling" => resultAccount with { FinancialAccountId = "broker-2" },
                        "unscoped" => resultAccount with { FinancialAccountId = null },
                        "type" => resultAccount with { AccountType = LedgerAccountType.Asset },
                        "symbol" or "symbol-without-lineage" => resultAccount with { Symbol = "OTHER" },
                        _ => resultAccount
                    };
                    var metadata = entry.Metadata;
                    if (defect == "symbol")
                    {
                        // Valid lineage lets this case reach exact result-account validation.
                        // The separate missing-lineage case retains the earlier posting guard proof.
                        var tags = SecurityMasterLineageTags(TestSecurityId);
                        tags["securityMasterLineage"] =
                            $"OTHER:{TestSecurityId:N}:ledger-map:OTHER:sm-approval:lot-controller:security-status:active:{tags["securityMasterProvenance"]}";
                        metadata = metadata with { Tags = tags };
                    }
                    LedgerEntry Line(LedgerAccount account, decimal debit, decimal credit) => new(
                        Guid.NewGuid(), entry.JournalEntryId, entry.Timestamp, account, debit, credit,
                        entry.Description, entry.Lines[0].Dimensions,
                        new LedgerEntryCurrency("USD", "USD", debit, credit, 1m));
                    command = (command with
                    {
                        DisposalSalePrice = explicitPrice ? cash / 5m : null,
                        Journal = command.Journal with
                        {
                            Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
                            [
                                Line(LedgerAccounts.CashAccount("broker-1"), cash, 0m),
                                Line(lots[0].Account, 0m, 0.06m),
                                Line(resultAccount, debitResult ? 0.01m : 0m, debitResult ? 0m : 0.01m)
                            ], metadata)
                        }
                    }).WithComputedFingerprint();
                    var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
                    var post = () => database.JournalStore.AppendAssetPostingAsync(command);
                    await post.Should().ThrowAsync<LedgerValidationException>()
                        .WithMessage(defect == "symbol-without-lineage"
                            ? "*declares instrument symbol 'OTHER' without matching Security Master lineage*"
                            : "*disposing account's exact*");
                    (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
                    (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
                    (await database.JournalStore.GetTaxLotsByIdsAsync(command.LedgerBookId,
                        lots.Select(static lot => lot.TaxLotRecordId).ToArray())).Should().BeEquivalentTo(lots);
                    (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);
                }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task DisposalAllocation_ScopedProjectorResultsRetainProceedsAndExactReplay()
    {
        foreach (var explicitPrice in new[] { false, true })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (command, _, expected) = await PrepareProceedsDisposalAsync(database, explicitPrice,
                financialAccountId: "broker-1");
            var posted = await database.JournalStore.AppendAssetPostingAsync(command);
            var replay = await database.JournalStore.AppendAssetPostingAsync(command);
            replay.IsExactReplay.Should().BeTrue();
            replay.CanonicalFingerprint.Should().Be(command.CanonicalFingerprint);
            var history = (await database.JournalStore.GetTaxLotDisposalHistoryAsync(command.LedgerBookId,
                [command.Journal.Entry.JournalEntryId])).Single();
            var rebuilt = CanonicalDisposalHistoryProjector.Project(history, posted.Journal.Entry,
                command.LedgerBookId, "USD");
            rebuilt.Proceeds.Should().Be(expected.Proceeds);
            rebuilt.Selections.Select(static selection => selection.Proceeds)
                .Should().Equal(expected.Selections.Select(static selection => selection.Proceeds));
        }
    }

    private static async Task<(AtomicTaxLotJournalCommand Command, LedgerTaxLotRecord[] Lots,
        LedgerTaxLotReliefProjection Projection)> PrepareProceedsDisposalAsync(
        LedgerPostgresTestDatabase database, bool precisePrice, bool washSale = false, string? financialAccountId = null)
    {
        var bookId = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new(bookId, "proceeds-allocation", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Proceeds allocation", "USD", at, at));
        var period = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), bookId, 2026, 5,
            "2026-05", new(2026, 5, 1), new(2026, 5, 31), "Open", at, null, 0), 0);
        var account = new LedgerAccount("Investment lots", LedgerAccountType.Asset, FinancialAccountId: financialAccountId);
        await database.JournalStore.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), bookId, account,
            LedgerTaxLotReliefMethod.Fifo, "tax-policy-v1", new(2026, 5, 1), at, at));
        var lots = new List<LedgerTaxLotRecord>();
        for (var i = 0; i < (washSale ? 1 : precisePrice ? 9 : 5); i++)
        {
            var quantity = precisePrice ? (i < 8 ? 0.5m : 2m) : 1m;
            var unitCost = washSale ? 100m : precisePrice ? (i < 8 ? 0.02m : 0.005m) : (i < 4 ? 0.01m : 0.02m);
            var acquired = new DateOnly(precisePrice && i == 8 ? 2026 : 2024, 1, i + 1);
            var id = Guid.NewGuid();
            var proof = BuildEvidence($"proceeds-acquisition-{i}", 'a') with
            {
                SubjectType = "OpenLotAcquisition",
                SubjectId = id.ToString("D"),
                EffectiveDate = acquired
            };
            lots.Add(await database.JournalStore.SaveTaxLotAsync(new(id, bookId, account,
                $"proceeds-lot-{i}", acquired, quantity, quantity, unitCost, "USD", at, at,
                Version: 1, SecurityId: TestSecurityId, BookPositionId: TestBookPositionId,
                Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m, quantity * unitCost,
                    quantity * unitCost, acquired, null, [proof]))));
        }
        var price = washSale ? 80m : precisePrice ? 0.0051m : 0.014m;
        var projection = LedgerTaxLotReliefProjector.Project(new(account, new(2026, 5, 12),
            lots.Sum(static lot => lot.OpenQuantity), price, LedgerTaxLotReliefMethod.Fifo,
            lots.Select(static lot => new LedgerTaxLot(lot.LotId, lot.AcquiredDate, lot.OpenQuantity, lot.UnitCost)).ToArray()));
        var sourceEventId = Guid.NewGuid();
        var journal = BuildJournalWrite(bookId, period.PeriodId, sourceEventId, "proceeds-disposal");
        journal = journal with
        {
            Entry = new JournalEntry(journal.Entry.JournalEntryId, journal.Entry.Timestamp,
                journal.Entry.Description, projection.Lines.Select(line => new LedgerEntry(Guid.NewGuid(),
                    journal.Entry.JournalEntryId, journal.Entry.Timestamp, line.account, line.debit, line.credit,
                    journal.Entry.Description,
                    new LedgerLineDimensionSet(InstrumentId: TestSecurityId) { PositionId = TestBookPositionId },
                    new LedgerEntryCurrency("USD", "USD", line.debit, line.credit, 1m))).ToArray(), journal.Entry.Metadata)
        };
        var evidence = BuildEvidence("proceeds-disposal", 'd');
        var command = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), bookId, journal, sourceEventId,
            "proceeds-disposal", period.Version, AtomicTaxLotMutationKind.Disposal, [evidence],
            disposalSelections: lots.Select((lot, index) => new LedgerTaxLotDisposalSelection(lot.TaxLotRecordId,
                lot.LotId, lot.Version, lot.OpenQuantity, lot.OpenQuantity, index, evidence.EvidenceId,
                lot.UnitCost, lot.OpenQuantity * lot.UnitCost)).ToArray(),
            reliefMethod: "Fifo", policyRevision: "tax-policy-v1", disposalSalePrice: precisePrice ? price : null);
        return (command, lots.ToArray(), projection);
    }

    private static async Task RetainLegacyProceedsDisposalAsync(LedgerPostgresTestDatabase database,
        AtomicTaxLotJournalCommand command, IReadOnlyList<LedgerTaxLotRecord> lots)
    {
        // Import the pre-versioning retained shape with initial INSERTs. No protected history is
        // edited, no append-only trigger is disabled, and the original command has no new fields.
        await database.JournalStore.AppendAsync(command.Journal);
        var afterLots = new List<LedgerTaxLotRecord>();
        foreach (var lot in lots)
            afterLots.Add(await database.JournalStore.SaveTaxLotAsync(lot with { OpenQuantity = 0m }));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var evidenceJson = JsonSerializer.Serialize(command.RetainedEvidence, jsonOptions);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var batch = connection.CreateCommand())
        {
            batch.Transaction = transaction;
            batch.CommandText = $"""
                insert into "{database.Options.SchemaName}".atomic_tax_lot_posting_batches (
                    mutation_batch_id, ledger_book_id, period_id, journal_entry_id, source_event_id,
                    idempotency_key, canonical_fingerprint, expected_period_version, mutation_kind,
                    retained_evidence, relief_method, policy_revision, created_at, security_id,
                    book_position_id, proceeds_allocation_version, disposal_sale_price)
                values (@batch, @book, @period, @journal, @event, @key, @fingerprint, @version,
                    'Disposal', cast(@evidence as jsonb), 'Fifo', 'tax-policy-v1', @at, @security, @position, null, null);
                """;
            batch.Parameters.AddWithValue("batch", command.MutationBatchId);
            batch.Parameters.AddWithValue("book", command.LedgerBookId);
            batch.Parameters.AddWithValue("period", command.Journal.PeriodId);
            batch.Parameters.AddWithValue("journal", command.Journal.Entry.JournalEntryId);
            batch.Parameters.AddWithValue("event", command.SourceEventId);
            batch.Parameters.AddWithValue("key", command.IdempotencyKey);
            batch.Parameters.AddWithValue("fingerprint", command.CanonicalFingerprint);
            batch.Parameters.AddWithValue("version", command.ExpectedPeriodVersion);
            batch.Parameters.AddWithValue("evidence", evidenceJson);
            batch.Parameters.AddWithValue("at", command.Journal.Entry.Timestamp.UtcDateTime);
            batch.Parameters.AddWithValue("security", TestSecurityId);
            batch.Parameters.AddWithValue("position", TestBookPositionId);
            await batch.ExecuteNonQueryAsync();
        }
        for (var i = 0; i < lots.Count; i++)
        {
            var lot = lots[i];
            await using var mutation = connection.CreateCommand();
            mutation.Transaction = transaction;
            mutation.CommandText = $"""
                insert into "{database.Options.SchemaName}".tax_lot_mutations (
                    mutation_record_id, mutation_batch_id, mutation_kind, tax_lot_record_id, lot_id,
                    selection_ordinal, quantity_before, quantity_delta, quantity_after, unit_cost,
                    cost_basis, expected_version, result_version, selection_evidence_id, retained_evidence,
                    journal_entry_id, source_event_id, relief_method, policy_revision, lot_snapshot_before,
                    lot_snapshot_after, recorded_at, security_id, book_position_id)
                values (@id, @batch, 'Disposal', @lot, @lot_id, @ordinal, @quantity, -@quantity, 0,
                    @cost, @basis, @version, @version + 1, @evidence_id, cast(@evidence as jsonb),
                    @journal, @event, 'Fifo', 'tax-policy-v1', cast(@before as jsonb), cast(@after as jsonb),
                    @at, @security, @position);
                """;
            mutation.Parameters.AddWithValue("id", Guid.NewGuid());
            mutation.Parameters.AddWithValue("batch", command.MutationBatchId);
            mutation.Parameters.AddWithValue("lot", lot.TaxLotRecordId);
            mutation.Parameters.AddWithValue("lot_id", lot.LotId);
            mutation.Parameters.AddWithValue("ordinal", i);
            mutation.Parameters.AddWithValue("quantity", lot.OpenQuantity);
            mutation.Parameters.AddWithValue("cost", lot.UnitCost);
            mutation.Parameters.AddWithValue("basis", lot.OpenQuantity * lot.UnitCost);
            mutation.Parameters.AddWithValue("version", lot.Version);
            mutation.Parameters.AddWithValue("evidence_id", command.RetainedEvidence[0].EvidenceId);
            mutation.Parameters.AddWithValue("evidence", evidenceJson);
            mutation.Parameters.AddWithValue("journal", command.Journal.Entry.JournalEntryId);
            mutation.Parameters.AddWithValue("event", command.SourceEventId);
            mutation.Parameters.AddWithValue("before", JsonSerializer.Serialize(lot, jsonOptions));
            mutation.Parameters.AddWithValue("after", JsonSerializer.Serialize(afterLots[i], jsonOptions));
            mutation.Parameters.AddWithValue("at", command.Journal.Entry.Timestamp.UtcDateTime);
            mutation.Parameters.AddWithValue("security", TestSecurityId);
            mutation.Parameters.AddWithValue("position", TestBookPositionId);
            await mutation.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
