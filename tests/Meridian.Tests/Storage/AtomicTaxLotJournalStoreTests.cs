using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class AtomicTaxLotJournalStoreTests
{
    private static readonly Guid TestSecurityId = Guid.Parse("8f9c129c-acde-4b5b-98b0-92085bc38047");
    private static readonly Guid TestBookPositionId = Guid.Parse("90f82b97-c8b7-4d06-a3ad-e6142b35c867");

    [Fact]
    public void Fingerprint_IsStableAcrossEvidenceAndSelectionOrdering_AndExcludesItself()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var firstEvidence = BuildEvidence("evidence-a", 'a');
        var secondEvidence = BuildEvidence("evidence-b", 'b');
        var firstSelection = new LedgerTaxLotDisposalSelection(
            Guid.NewGuid(), "lot-a", 2, 80m, 25m, 0, firstEvidence.EvidenceId, 100m, 2_500m);
        var secondSelection = new LedgerTaxLotDisposalSelection(
            Guid.NewGuid(), "lot-b", 4, 40m, 10m, 1, secondEvidence.EvidenceId, 100m, 1_000m);
        var journal = BuildJournalWrite(
            ledgerBookId,
            periodId,
            sourceEventId,
            "atomic-disposal:fingerprint");

        var first = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            journal,
            sourceEventId,
            "atomic-disposal:fingerprint",
            expectedPeriodVersion: 3,
            AtomicTaxLotMutationKind.Disposal,
            [secondEvidence, firstEvidence],
            disposalSelections: [secondSelection, firstSelection],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v3");
        var reordered = AtomicTaxLotJournalCommand.Create(
            first.MutationBatchId,
            ledgerBookId,
            journal,
            sourceEventId,
            "atomic-disposal:fingerprint",
            expectedPeriodVersion: 3,
            AtomicTaxLotMutationKind.Disposal,
            [firstEvidence, secondEvidence],
            disposalSelections: [firstSelection, secondSelection],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v3");

        reordered.CanonicalFingerprint.Should().Be(first.CanonicalFingerprint);
        first.CanonicalFingerprint.Should().MatchRegex("^sha256:[0-9a-f]{64}$");

        var populatedWithDifferentFingerprint = first with
        {
            CanonicalFingerprint = $"sha256:{new string('f', 64)}"
        };
        populatedWithDifferentFingerprint.WithComputedFingerprint().CanonicalFingerprint
            .Should().Be(first.CanonicalFingerprint);
    }

    [Fact]
    public void Fingerprint_ChangesForEveryPostingIdentityAndDisposalEvidenceMutation()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var evidence = BuildEvidence("evidence-fingerprint", 'c');
        var selection = new LedgerTaxLotDisposalSelection(
            Guid.NewGuid(), "lot-fingerprint", 2, 80m, 25m, 0, evidence.EvidenceId, 100m, 2_500m);
        var command = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            BuildJournalWrite(
                ledgerBookId,
                periodId,
                sourceEventId,
                "atomic-disposal:fingerprint-mutations"),
            sourceEventId,
            "atomic-disposal:fingerprint-mutations",
            expectedPeriodVersion: 3,
            AtomicTaxLotMutationKind.Disposal,
            [evidence],
            disposalSelections: [selection],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v3");

        var mutations = new (string Name, AtomicTaxLotJournalCommand Command)[]
        {
            ("mutation batch", command with { MutationBatchId = Guid.NewGuid() }),
            ("ledger book", command with { LedgerBookId = Guid.NewGuid() }),
            ("journal", command with
            {
                Journal = command.Journal with { AccountingPolicyVersion = "policy-v2" }
            }),
            ("source event", command with { SourceEventId = Guid.NewGuid() }),
            ("idempotency key", command with { IdempotencyKey = "atomic-disposal:changed" }),
            ("period version", command with { ExpectedPeriodVersion = 4 }),
            ("mutation kind", command with { MutationKind = AtomicTaxLotMutationKind.Acquisition }),
            ("retained evidence", command with
            {
                RetainedEvidence = [evidence with { EvidenceVersion = evidence.EvidenceVersion + 1 }]
            }),
            ("selected lot", command with
            {
                DisposalSelections = [selection with { Quantity = selection.Quantity + 1m }]
            }),
            ("correction lineage", command with { CorrectsMutationBatchId = Guid.NewGuid() }),
            ("relief method", command with { ReliefMethod = "Hifo" }),
            ("policy revision", command with { PolicyRevision = "tax-policy-v4" })
        };

        foreach (var mutation in mutations)
        {
            AtomicTaxLotJournalFingerprint.Compute(mutation.Command)
                .Should().NotBe(command.CanonicalFingerprint, mutation.Name);
        }
    }

    [Fact]
    public void Fingerprint_ChangesForEveryAcquisitionLotStateMutation()
    {
        var command = BuildAcquisitionCommand();
        var lot = command.AcquisitionLot!;
        var mutations = new (string Name, LedgerTaxLotRecord Lot)[]
        {
            ("tax-lot identity", lot with { TaxLotRecordId = Guid.NewGuid() }),
            ("account", lot with
            {
                Account = new LedgerAccount("Investment lots", LedgerAccountType.Asset, "MSFT")
            }),
            ("lot id", lot with { LotId = "lot-aapl-changed" }),
            ("acquired date", lot with { AcquiredDate = lot.AcquiredDate.AddDays(1) }),
            ("original quantity", lot with { OriginalQuantity = lot.OriginalQuantity + 1m }),
            ("open quantity", lot with { OpenQuantity = lot.OpenQuantity - 1m }),
            ("unit cost", lot with { UnitCost = lot.UnitCost + 1m }),
            ("currency", lot with { Currency = "EUR" }),
            ("created timestamp", lot with { CreatedAt = lot.CreatedAt.AddMinutes(1) }),
            ("updated timestamp", lot with { UpdatedAt = lot.UpdatedAt.AddMinutes(1) }),
            ("source journal", lot with { SourceJournalEntryId = Guid.NewGuid() }),
            ("lot evidence", lot with { EvidenceRef = "evidence-changed" }),
            ("version", lot with { Version = lot.Version + 1 }),
            ("originating batch", lot with { OriginatingMutationBatchId = Guid.NewGuid() }),
            ("last mutation batch", lot with { LastMutationBatchId = Guid.NewGuid() }),
            ("security", lot with { SecurityId = Guid.NewGuid() }),
            ("book position", lot with { BookPositionId = Guid.NewGuid() })
        };

        foreach (var mutation in mutations)
        {
            AtomicTaxLotJournalFingerprint.Compute(command with { AcquisitionLot = mutation.Lot })
                .Should().NotBe(command.CanonicalFingerprint, mutation.Name);
        }
    }

    [Fact]
    public async Task AppendAssetPostingAsync_RejectsFingerprintMismatchBeforeOpeningConnection()
    {
        var command = BuildAcquisitionCommand();
        command = command with
        {
            CanonicalFingerprint = command.CanonicalFingerprint[..^1] +
                (command.CanonicalFingerprint[^1] == '0' ? "1" : "0")
        };
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var act = () => store.AppendAssetPostingAsync(command);

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*does not match the complete command payload*");
    }

    [Fact]
    public async Task AppendAssetPostingAsync_RejectsAcquisitionLotOutsideJournalAssetScopeBeforeOpeningConnection()
    {
        var command = BuildAcquisitionCommand();
        command = (command with
        {
            AcquisitionLot = command.AcquisitionLot! with { SecurityId = Guid.NewGuid() }
        }).WithComputedFingerprint();
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var act = () => store.AppendAssetPostingAsync(command);

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*Security Master and book-position identities must exactly match*");
    }

    [Fact]
    public async Task AppendAssetPostingAsync_RejectsSelectionWithoutRetainedEvidenceBeforeOpeningConnection()
    {
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var evidence = BuildEvidence("evidence-a", 'a');
        var command = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            BuildJournalWrite(ledgerBookId, periodId, sourceEventId, "atomic-disposal:missing-evidence"),
            sourceEventId,
            "atomic-disposal:missing-evidence",
            expectedPeriodVersion: 0,
            AtomicTaxLotMutationKind.Disposal,
            [evidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    Guid.NewGuid(),
                    "lot-a",
                    ExpectedVersion: 1,
                    ExpectedOpenQuantity: 100m,
                    Quantity: 25m,
                    SelectionOrdinal: 0,
                    SelectionEvidenceId: "evidence-not-retained",
                    ExpectedUnitCost: 100m,
                    ExpectedCostBasis: 2_500m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v1");
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var act = () => store.AppendAssetPostingAsync(command);

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*does not reference retained evidence*");
    }

    [Fact]
    public async Task AppendAssetPostingAsync_RejectsIncompleteTypedEvidenceBeforeOpeningConnection()
    {
        var command = BuildAcquisitionCommand();
        command = (command with
        {
            RetainedEvidence =
            [
                command.RetainedEvidence[0] with
                {
                    ContentHashSha256 = "not-a-sha256"
                }
            ]
        }).WithComputedFingerprint();
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var act = () => store.AppendAssetPostingAsync(command);

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*retained evidence*incomplete*");
    }

    [Fact]
    public async Task AppendAssetPostingAsync_AcceptsInitialVersionOneLineageStampedByTheCommand()
    {
        var command = BuildAcquisitionCommand();
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var act = () => store.AppendAssetPostingAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ConnectionString is not configured*");
    }

    [Fact]
    public void AtomicTaxLotMigration_DefinesVersionedCasBatchesAndImmutableMutationEvidence()
    {
        var sql = ReadMigration("V_ledger_027__atomic_tax_lot_posting.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.atomic_tax_lot_posting_batches");
        sql.Should().Contain("canonical_fingerprint text not null");
        sql.Should().Contain("ux_atomic_tax_lot_batch_journal");
        sql.Should().Contain("ux_atomic_tax_lot_batch_book_source_event");
        sql.Should().Contain("ux_atomic_tax_lot_batch_book_idempotency");
        sql.Should().Contain("add column if not exists version bigint not null default 1");
        sql.Should().Contain("add column if not exists security_id uuid null");
        sql.Should().Contain("add column if not exists book_position_id uuid null");
        sql.Should().Contain("ck_tax_lots_asset_scope");
        sql.Should().Contain("ck_atomic_tax_lot_batch_asset_scope");
        sql.Should().Contain("ck_tax_lot_mutations_asset_scope");
        sql.Should().Contain("add constraint ck_tax_lots_version check (version > 0)");
        sql.Should().Contain("originating_mutation_batch_id uuid null");
        sql.Should().Contain("last_mutation_batch_id uuid null");
        sql.Should().Contain("create table if not exists __SCHEMA__.tax_lot_mutations");
        sql.Should().Contain("quantity_before numeric(38, 12) not null");
        sql.Should().Contain("quantity_delta numeric(38, 12) not null");
        sql.Should().Contain("quantity_after numeric(38, 12) not null");
        sql.Should().Contain("expected_version bigint not null");
        sql.Should().Contain("result_version bigint not null");
        sql.Should().Contain("selection_evidence_id text not null");
        sql.Should().Contain("lot_snapshot_before jsonb null");
        sql.Should().Contain("lot_snapshot_after jsonb not null");
        sql.Should().Contain("corrects_mutation_batch_id uuid null");
        sql.Should().Contain("trg_atomic_tax_lot_batches_immutable");
        sql.Should().Contain("trg_tax_lot_mutations_immutable");
    }

    [Fact]
    public void LegacyTaxLotSave_RequiresAnExactPositiveVersionAndCannotOverwriteAtomicLots()
    {
        var source = ReadRepositoryFile(
            "src",
            "Meridian.Storage",
            "Ledger",
            "PostgresLedgerJournalStore.cs");

        source.Should().Contain("where retained.originating_mutation_batch_id is null");
        source.Should().Contain("and @expected_version > 0");
        source.Should().Contain("and retained.version = @expected_version");
        source.Should().NotContain("@expected_version = 0 or retained.version = @expected_version");
    }

    [Fact]
    public async Task GetTaxLotsByIdsAsync_RejectsMissingAuthorityBeforeOpeningConnection()
    {
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var missingBook = () => store.GetTaxLotsByIdsAsync(Guid.Empty, [Guid.NewGuid()]);
        var missingLots = () => store.GetTaxLotsByIdsAsync(Guid.NewGuid(), []);

        await missingBook.Should().ThrowAsync<ArgumentException>().WithMessage("*Ledger book id*");
        await missingLots.Should().ThrowAsync<ArgumentException>().WithMessage("*tax-lot record id*");
    }

    [Fact]
    public void AtomicTaxLotStore_UsesOneSerializableTransactionForJournalLotAndAuditWrites()
    {
        var source = ReadRepositoryFile(
            "src",
            "Meridian.Storage",
            "Ledger",
            "PostgresLedgerJournalStore.AtomicTaxLots.cs");

        source.Should().Contain("BeginTransactionAsync(IsolationLevel.Serializable");
        source.Should().Contain("await AppendAsync(connection, transaction, command.Journal, ct)");
        source.Should().Contain("await InsertAtomicTaxLotBatchAsync(connection, transaction, command");
        source.Should().Contain("await InsertTaxLotMutationAsync(connection, transaction, mutation, ct)");
        source.Should().Contain("and version = @expected_version");
        source.Should().Contain("and open_quantity = @expected_open_quantity");
        source.Should().Contain("and security_id = @security_id");
        source.Should().Contain("and book_position_id = @book_position_id");
        source.Should().Contain("lot.UnitCost != selection.ExpectedUnitCost");
        source.Should().Contain("ValidateAtomicJournalLotEconomics(command, mutations)");
        source.Should().Contain("await transaction.CommitAsync(ct)");
    }

    [LedgerDatabaseFact]
    public async Task AppendAssetPostingAsync_PersistedFifoRejectsNewerLotAndPolicyMismatchWithoutMutatingOpenLots()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var ownerNodeId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            ledgerBookId,
            "fund-authoritative-fifo",
            ownerNodeId,
            FundStructureNodeKindDto.Fund,
            "Authoritative FIFO Book",
            "USD",
            openedAt,
            openedAt));
        var period = await database.JournalStore.SavePeriodAsync(
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                5,
                "2026-05",
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31),
                "Open",
                openedAt,
                ClosedAt: null,
                Version: 0),
            expectedVersion: 0);

        var olderAcquisition = BuildAcquisitionCommand(
            ledgerBookId,
            periodId,
            expectedPeriodVersion: period.Version,
            idempotencyKey: "atomic-acquisition:fifo-older",
            lotId: "lot-aapl-20260510",
            acquiredDate: new DateOnly(2026, 5, 10),
            unitCost: 125m,
            evidenceId: "evidence-fifo-older",
            evidenceHashCharacter: 'e');
        var older = await database.JournalStore.AppendAssetPostingAsync(olderAcquisition);
        var newerAcquisition = BuildAcquisitionCommand(
            ledgerBookId,
            periodId,
            expectedPeriodVersion: period.Version,
            idempotencyKey: "atomic-acquisition:fifo-newer",
            lotId: "lot-aapl-20260512",
            acquiredDate: new DateOnly(2026, 5, 12),
            unitCost: 100m,
            evidenceId: "evidence-fifo-newer",
            evidenceHashCharacter: 'f');
        var newer = await database.JournalStore.AppendAssetPostingAsync(newerAcquisition);

        await database.JournalStore.SaveTaxLotPolicyAsync(
            new LedgerAccountTaxLotPolicyRecord(
                PolicyRecordId: Guid.NewGuid(),
                LedgerBookId: ledgerBookId,
                Account: older.MutatedLots[0].Account,
                ReliefMethod: LedgerTaxLotReliefMethod.Fifo,
                PolicyId: "tax-policy-fifo-v1",
                EffectiveDate: new DateOnly(2026, 5, 1),
                CreatedAt: openedAt,
                UpdatedAt: openedAt,
                Rationale: "Persisted FIFO authority for deterministic open-lot selection"));

        async Task AssertRejectedPostingRolledBackAsync(AtomicTaxLotJournalCommand rejected)
        {
            var unchangedLots = (await database.JournalStore.GetTaxLotsByIdsAsync(
                    ledgerBookId,
                    [older.MutatedLots[0].TaxLotRecordId, newer.MutatedLots[0].TaxLotRecordId]))
                .ToDictionary(static lot => lot.TaxLotRecordId);
            unchangedLots[older.MutatedLots[0].TaxLotRecordId].Version.Should().Be(1);
            unchangedLots[older.MutatedLots[0].TaxLotRecordId].OpenQuantity.Should().Be(100m);
            unchangedLots[older.MutatedLots[0].TaxLotRecordId].LastMutationBatchId
                .Should().Be(olderAcquisition.MutationBatchId);
            unchangedLots[newer.MutatedLots[0].TaxLotRecordId].Version.Should().Be(1);
            unchangedLots[newer.MutatedLots[0].TaxLotRecordId].OpenQuantity.Should().Be(100m);
            unchangedLots[newer.MutatedLots[0].TaxLotRecordId].LastMutationBatchId
                .Should().Be(newerAcquisition.MutationBatchId);

            var retainedJournals = await database.JournalStore.GetByPeriodAsync(periodId);
            retainedJournals.Select(static item => item.Entry.JournalEntryId).Should().BeEquivalentTo(
                new[]
                {
                    olderAcquisition.Journal.Entry.JournalEntryId,
                    newerAcquisition.Journal.Entry.JournalEntryId
                });
            retainedJournals.Should().NotContain(item =>
                item.Entry.JournalEntryId == rejected.Journal.Entry.JournalEntryId);
            (await database.JournalStore.GetAtomicTaxLotPostingAsync(rejected.MutationBatchId))
                .Should().BeNull();
        }

        var disposalEvidence = BuildEvidence("evidence-fifo-disposal", 'd');
        var nonFifoSourceEventId = Guid.NewGuid();
        var nonFifoJournal = BuildJournalWrite(
            ledgerBookId,
            periodId,
            nonFifoSourceEventId,
            "atomic-disposal:fifo-newer-first",
            debitAccount: "Cash",
            creditAccount: "Investment lots",
            amount: 1_000m);
        var nonFifoDisposal = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            nonFifoJournal,
            nonFifoSourceEventId,
            "atomic-disposal:fifo-newer-first",
            period.Version,
            AtomicTaxLotMutationKind.Disposal,
            [disposalEvidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    newer.MutatedLots[0].TaxLotRecordId,
                    newer.MutatedLots[0].LotId,
                    ExpectedVersion: 1,
                    ExpectedOpenQuantity: 100m,
                    Quantity: 10m,
                    SelectionOrdinal: 0,
                    disposalEvidence.EvidenceId,
                    ExpectedUnitCost: 100m,
                    ExpectedCostBasis: 1_000m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-fifo-v1");

        var nonFifoAct = () => database.JournalStore.AppendAssetPostingAsync(nonFifoDisposal);

        await nonFifoAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*does not match the authoritative Fifo open-lot relief plan*");
        await AssertRejectedPostingRolledBackAsync(nonFifoDisposal);

        var policyMismatchSourceEventId = Guid.NewGuid();
        var policyMismatchJournal = BuildJournalWrite(
            ledgerBookId,
            periodId,
            policyMismatchSourceEventId,
            "atomic-disposal:fifo-policy-mismatch",
            debitAccount: "Cash",
            creditAccount: "Investment lots",
            amount: 1_250m);
        var policyMismatchDisposal = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            policyMismatchJournal,
            policyMismatchSourceEventId,
            "atomic-disposal:fifo-policy-mismatch",
            period.Version,
            AtomicTaxLotMutationKind.Disposal,
            [disposalEvidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    older.MutatedLots[0].TaxLotRecordId,
                    older.MutatedLots[0].LotId,
                    ExpectedVersion: 1,
                    ExpectedOpenQuantity: 100m,
                    Quantity: 10m,
                    SelectionOrdinal: 0,
                    disposalEvidence.EvidenceId,
                    ExpectedUnitCost: 125m,
                    ExpectedCostBasis: 1_250m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-fifo-v2");

        var policyMismatchAct = () => database.JournalStore.AppendAssetPostingAsync(policyMismatchDisposal);

        await policyMismatchAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*does not match effective policy*");
        await AssertRejectedPostingRolledBackAsync(policyMismatchDisposal);

        var legitimateSourceEventId = Guid.NewGuid();
        var legitimateJournal = BuildJournalWrite(
            ledgerBookId,
            periodId,
            legitimateSourceEventId,
            "atomic-disposal:fifo-oldest-first",
            debitAccount: "Cash",
            creditAccount: "Investment lots",
            amount: 5_000m);
        var legitimateDisposal = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            legitimateJournal,
            legitimateSourceEventId,
            "atomic-disposal:fifo-oldest-first",
            period.Version,
            AtomicTaxLotMutationKind.Disposal,
            [disposalEvidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    older.MutatedLots[0].TaxLotRecordId,
                    older.MutatedLots[0].LotId,
                    ExpectedVersion: 1,
                    ExpectedOpenQuantity: 100m,
                    Quantity: 40m,
                    SelectionOrdinal: 0,
                    disposalEvidence.EvidenceId,
                    ExpectedUnitCost: 125m,
                    ExpectedCostBasis: 5_000m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-fifo-v1");

        var legitimate = await database.JournalStore.AppendAssetPostingAsync(legitimateDisposal);

        legitimate.Mutations.Should().ContainSingle().Which.Should().Match<LedgerTaxLotMutationRecord>(mutation =>
            mutation.QuantityBefore == 100m &&
            mutation.QuantityDelta == -40m &&
            mutation.QuantityAfter == 60m &&
            mutation.UnitCost == 125m &&
            mutation.CostBasis == 5_000m &&
            mutation.ExpectedVersion == 1 &&
            mutation.ResultVersion == 2);
        var retainedAfterLegitimatePosting = await database.JournalStore.GetByPeriodAsync(periodId);
        retainedAfterLegitimatePosting.Select(static item => item.Entry.JournalEntryId).Should().BeEquivalentTo(
            new[]
            {
                olderAcquisition.Journal.Entry.JournalEntryId,
                newerAcquisition.Journal.Entry.JournalEntryId,
                legitimateJournal.Entry.JournalEntryId
            });
        var finalLots = (await database.JournalStore.GetTaxLotsByIdsAsync(
                ledgerBookId,
                [older.MutatedLots[0].TaxLotRecordId, newer.MutatedLots[0].TaxLotRecordId]))
            .ToDictionary(static lot => lot.TaxLotRecordId);
        finalLots[older.MutatedLots[0].TaxLotRecordId].Version.Should().Be(2);
        finalLots[older.MutatedLots[0].TaxLotRecordId].OpenQuantity.Should().Be(60m);
        finalLots[older.MutatedLots[0].TaxLotRecordId].LastMutationBatchId
            .Should().Be(legitimateDisposal.MutationBatchId);
        finalLots[newer.MutatedLots[0].TaxLotRecordId].Version.Should().Be(1);
        finalLots[newer.MutatedLots[0].TaxLotRecordId].OpenQuantity.Should().Be(100m);
        finalLots[newer.MutatedLots[0].TaxLotRecordId].LastMutationBatchId
            .Should().Be(newerAcquisition.MutationBatchId);
    }

    [LedgerDatabaseFact]
    public async Task AppendAssetPostingAsync_AcquisitionReplayDisposalAndRollbackShareOneTransaction()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var ledgerBookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var ownerNodeId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            ledgerBookId,
            "fund-atomic-lots",
            ownerNodeId,
            FundStructureNodeKindDto.Fund,
            "Atomic Lots Primary Book",
            "USD",
            openedAt,
            openedAt));
        var period = await database.JournalStore.SavePeriodAsync(
            new LedgerAccountingPeriod(
                periodId,
                ledgerBookId,
                2026,
                5,
                "2026-05",
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 31),
                "Open",
                openedAt,
                ClosedAt: null,
                Version: 0),
            expectedVersion: 0);

        var acquisition = BuildAcquisitionCommand(
            ledgerBookId,
            periodId,
            expectedPeriodVersion: period.Version);
        var acquired = await database.JournalStore.AppendAssetPostingAsync(acquisition);

        acquired.IsExactReplay.Should().BeFalse();
        acquired.Journal.Entry.JournalEntryId.Should().Be(acquisition.Journal.Entry.JournalEntryId);
        acquired.MutatedLots.Should().ContainSingle();
        acquired.MutatedLots[0].Version.Should().Be(1);
        acquired.MutatedLots[0].OpenQuantity.Should().Be(100m);
        acquired.MutatedLots[0].OriginatingMutationBatchId.Should().Be(acquisition.MutationBatchId);
        acquired.MutatedLots[0].SecurityId.Should().Be(TestSecurityId);
        acquired.MutatedLots[0].BookPositionId.Should().Be(TestBookPositionId);
        acquired.Mutations.Should().ContainSingle().Which.Should().Match<LedgerTaxLotMutationRecord>(mutation =>
            mutation.QuantityBefore == 0m &&
            mutation.QuantityDelta == 100m &&
            mutation.QuantityAfter == 100m &&
            mutation.ExpectedVersion == 0 &&
            mutation.ResultVersion == 1);

        await database.JournalStore.SaveTaxLotPolicyAsync(
            new LedgerAccountTaxLotPolicyRecord(
                PolicyRecordId: Guid.NewGuid(),
                LedgerBookId: ledgerBookId,
                Account: acquired.MutatedLots[0].Account,
                ReliefMethod: LedgerTaxLotReliefMethod.Fifo,
                PolicyId: "tax-policy-v1",
                EffectiveDate: new DateOnly(2026, 5, 1),
                CreatedAt: openedAt,
                UpdatedAt: openedAt,
                Rationale: "Authoritative FIFO integration fixture"));

        var advancedPeriod = await database.JournalStore.SavePeriodAsync(
            period with { Version = period.Version + 1 },
            expectedVersion: period.Version);
        var acquisitionReplay = await database.JournalStore.AppendAssetPostingAsync(acquisition);
        acquisitionReplay.IsExactReplay.Should().BeTrue();
        acquisitionReplay.Mutations[0].MutationRecordId.Should().Be(acquired.Mutations[0].MutationRecordId);
        acquisitionReplay.MutatedLots[0].Version.Should().Be(1);

        var changedReplay = (acquisition with
        {
            AcquisitionLot = acquisition.AcquisitionLot! with
            {
                UnitCost = acquisition.AcquisitionLot.UnitCost + 1m
            }
        }).WithComputedFingerprint();
        var changedReplayAct = () => database.JournalStore.AppendAssetPostingAsync(changedReplay);
        await changedReplayAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*identity collision*complete canonical fingerprint*");

        var stalePeriodAcquisition = BuildAcquisitionCommand(
            ledgerBookId,
            periodId,
            expectedPeriodVersion: period.Version,
            idempotencyKey: "atomic-acquisition:stale-period");
        var stalePeriodAct = () => database.JournalStore.AppendAssetPostingAsync(stalePeriodAcquisition);
        await stalePeriodAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*does not match atomic tax-lot expected version*");

        var disposalEvidence = BuildEvidence("evidence-disposal", 'd');
        var crossScopeSourceEventId = Guid.NewGuid();
        var crossScopeJournal = BuildJournalWrite(
            ledgerBookId,
            periodId,
            crossScopeSourceEventId,
            "atomic-disposal:cross-scope",
            debitAccount: "Cash",
            creditAccount: "Investment lots",
            amount: 1_000m,
            securityId: Guid.NewGuid(),
            bookPositionId: Guid.NewGuid());
        var crossScopeDisposal = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            crossScopeJournal,
            crossScopeSourceEventId,
            "atomic-disposal:cross-scope",
            advancedPeriod.Version,
            AtomicTaxLotMutationKind.Disposal,
            [disposalEvidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    acquired.MutatedLots[0].TaxLotRecordId,
                    acquired.MutatedLots[0].LotId,
                    ExpectedVersion: 1,
                    ExpectedOpenQuantity: 100m,
                    Quantity: 10m,
                    SelectionOrdinal: 0,
                    disposalEvidence.EvidenceId,
                    ExpectedUnitCost: 100m,
                    ExpectedCostBasis: 1_000m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v1");
        var crossScopeAct = () => database.JournalStore.AppendAssetPostingAsync(crossScopeDisposal);
        await crossScopeAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*failed the disposal scope, cost basis, quantity, or version*");

        var disposalSourceEventId = Guid.NewGuid();
        var disposal = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            BuildJournalWrite(
                ledgerBookId,
                periodId,
                disposalSourceEventId,
                "atomic-disposal:lot-1",
                debitAccount: "Cash",
                creditAccount: "Investment lots",
                amount: 4_000m),
            disposalSourceEventId,
            "atomic-disposal:lot-1",
            advancedPeriod.Version,
            AtomicTaxLotMutationKind.Disposal,
            [disposalEvidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    acquired.MutatedLots[0].TaxLotRecordId,
                    acquired.MutatedLots[0].LotId,
                    ExpectedVersion: 1,
                    ExpectedOpenQuantity: 100m,
                    Quantity: 40m,
                    SelectionOrdinal: 0,
                    disposalEvidence.EvidenceId,
                    ExpectedUnitCost: 100m,
                    ExpectedCostBasis: 4_000m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v1");
        var disposed = await database.JournalStore.AppendAssetPostingAsync(disposal);

        disposed.IsExactReplay.Should().BeFalse();
        disposed.ReliefMethod.Should().Be("Fifo");
        disposed.PolicyRevision.Should().Be("tax-policy-v1");
        disposed.MutatedLots[0].Version.Should().Be(2);
        disposed.MutatedLots[0].OpenQuantity.Should().Be(60m);
        disposed.Mutations[0].QuantityBefore.Should().Be(100m);
        disposed.Mutations[0].QuantityDelta.Should().Be(-40m);
        disposed.Mutations[0].QuantityAfter.Should().Be(60m);
        disposed.Mutations[0].CostBasis.Should().Be(4_000m);
        disposed.Mutations[0].ExpectedVersion.Should().Be(1);
        disposed.Mutations[0].ResultVersion.Should().Be(2);

        var disposalReplay = await database.JournalStore.AppendAssetPostingAsync(disposal);
        disposalReplay.IsExactReplay.Should().BeTrue();
        disposalReplay.Mutations[0].MutationRecordId.Should().Be(disposed.Mutations[0].MutationRecordId);
        disposalReplay.MutatedLots[0].Version.Should().Be(2);
        disposalReplay.MutatedLots[0].OpenQuantity.Should().Be(60m);

        var staleSourceEventId = Guid.NewGuid();
        var staleJournal = BuildJournalWrite(
            ledgerBookId,
            periodId,
            staleSourceEventId,
            "atomic-disposal:stale-lot",
            debitAccount: "Cash",
            creditAccount: "Investment lots",
            amount: 1_000m);
        var stale = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            staleJournal,
            staleSourceEventId,
            "atomic-disposal:stale-lot",
            advancedPeriod.Version,
            AtomicTaxLotMutationKind.Disposal,
            [disposalEvidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    acquired.MutatedLots[0].TaxLotRecordId,
                    acquired.MutatedLots[0].LotId,
                    ExpectedVersion: 1,
                    ExpectedOpenQuantity: 100m,
                    Quantity: 10m,
                    SelectionOrdinal: 0,
                    disposalEvidence.EvidenceId,
                    ExpectedUnitCost: 100m,
                    ExpectedCostBasis: 1_000m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v1");

        var staleAct = () => database.JournalStore.AppendAssetPostingAsync(stale);
        await staleAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*compare-and-swap guard*");

        var idempotencyCollisionSourceId = Guid.NewGuid();
        var idempotencyCollision = AtomicTaxLotJournalCommand.Create(
            Guid.NewGuid(),
            ledgerBookId,
            BuildJournalWrite(
                ledgerBookId,
                periodId,
                idempotencyCollisionSourceId,
                disposal.IdempotencyKey,
                debitAccount: "Cash",
                creditAccount: "Investment lots",
                amount: 500m),
            idempotencyCollisionSourceId,
            disposal.IdempotencyKey,
            advancedPeriod.Version,
            AtomicTaxLotMutationKind.Disposal,
            [disposalEvidence],
            disposalSelections:
            [
                new LedgerTaxLotDisposalSelection(
                    disposed.MutatedLots[0].TaxLotRecordId,
                    disposed.MutatedLots[0].LotId,
                    ExpectedVersion: 2,
                    ExpectedOpenQuantity: 60m,
                    Quantity: 5m,
                    SelectionOrdinal: 0,
                    disposalEvidence.EvidenceId,
                    ExpectedUnitCost: 100m,
                    ExpectedCostBasis: 500m)
            ],
            reliefMethod: "Fifo",
            policyRevision: "tax-policy-v1");
        var collisionAct = () => database.JournalStore.AppendAssetPostingAsync(idempotencyCollision);
        await collisionAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*identity collision*");

        var retainedJournals = await database.JournalStore.GetByPeriodAsync(periodId);
        retainedJournals.Select(static item => item.Entry.JournalEntryId).Should().BeEquivalentTo(
            new[]
            {
                acquisition.Journal.Entry.JournalEntryId,
                disposal.Journal.Entry.JournalEntryId
            });
        retainedJournals.Should().NotContain(item => item.Entry.JournalEntryId == staleJournal.Entry.JournalEntryId);
        retainedJournals.Should().NotContain(item => item.Entry.JournalEntryId == crossScopeJournal.Entry.JournalEntryId);
        retainedJournals.Should().NotContain(item =>
            item.Entry.JournalEntryId == stalePeriodAcquisition.Journal.Entry.JournalEntryId);

        var openLots = await database.JournalStore.ListOpenTaxLotsAsync(
            ledgerBookId,
            acquisition.AcquisitionLot!.Account);
        openLots.Should().ContainSingle();
        openLots[0].Version.Should().Be(2);
        openLots[0].OpenQuantity.Should().Be(60m);
        openLots[0].LastMutationBatchId.Should().Be(disposal.MutationBatchId);
    }

    private static AtomicTaxLotJournalCommand BuildAcquisitionCommand(
        Guid? ledgerBookId = null,
        Guid? periodId = null,
        long expectedPeriodVersion = 0,
        string idempotencyKey = "atomic-acquisition:lot-1",
        string lotId = "lot-aapl-20260512",
        DateOnly? acquiredDate = null,
        decimal quantity = 100m,
        decimal unitCost = 100m,
        string evidenceId = "evidence-acquisition",
        char evidenceHashCharacter = 'a')
    {
        var bookId = ledgerBookId ?? Guid.NewGuid();
        var retainedPeriodId = periodId ?? Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var mutationBatchId = Guid.NewGuid();
        var evidence = BuildEvidence(evidenceId, evidenceHashCharacter);
        var recordedAt = DateTimeOffset.Parse("2026-05-12T14:00:00Z");
        var journal = BuildJournalWrite(
            bookId,
            retainedPeriodId,
            sourceEventId,
            idempotencyKey,
            debitAccount: "Investment lots",
            creditAccount: "Cash",
            amount: quantity * unitCost);
        var lot = new LedgerTaxLotRecord(
            Guid.NewGuid(),
            bookId,
            new LedgerAccount("Investment lots", LedgerAccountType.Asset),
            lotId,
            acquiredDate ?? new DateOnly(2026, 5, 12),
            OriginalQuantity: quantity,
            OpenQuantity: quantity,
            UnitCost: unitCost,
            Currency: "USD",
            CreatedAt: recordedAt,
            UpdatedAt: recordedAt,
            SourceJournalEntryId: journal.Entry.JournalEntryId,
            EvidenceRef: evidence.EvidenceId,
            Version: 1,
            OriginatingMutationBatchId: mutationBatchId,
            LastMutationBatchId: mutationBatchId,
            SecurityId: TestSecurityId,
            BookPositionId: TestBookPositionId);

        return AtomicTaxLotJournalCommand.Create(
            mutationBatchId,
            bookId,
            journal,
            sourceEventId,
            idempotencyKey,
            expectedPeriodVersion,
            AtomicTaxLotMutationKind.Acquisition,
            [evidence],
            acquisitionLot: lot);
    }

    private static LedgerJournalEntryWrite BuildJournalWrite(
        Guid ledgerBookId,
        Guid periodId,
        Guid sourceEventId,
        string idempotencyKey,
        string debitAccount = "Investment lots",
        string creditAccount = "Cash",
        decimal amount = 10_000m,
        Guid? securityId = null,
        Guid? bookPositionId = null)
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-05-12T14:00:00Z");
        const string description = "Atomic tax-lot posting";
        var scopedSecurityId = securityId ?? TestSecurityId;
        var scopedBookPositionId = bookPositionId ?? TestBookPositionId;
        return new LedgerJournalEntryWrite(
            new JournalEntry(
                journalEntryId,
                timestamp,
                description,
                [
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        timestamp,
                        new LedgerAccount(debitAccount, LedgerAccountType.Asset),
                        debit: amount,
                        credit: 0m,
                        description,
                        dimensions: new LedgerLineDimensionSet(InstrumentId: scopedSecurityId)
                        {
                            PositionId = scopedBookPositionId
                        },
                        currency: new LedgerEntryCurrency("USD", "USD", amount, 0m, 1m)),
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        timestamp,
                        new LedgerAccount(creditAccount, LedgerAccountType.Asset),
                        debit: 0m,
                        credit: amount,
                        description,
                        dimensions: new LedgerLineDimensionSet(InstrumentId: scopedSecurityId)
                        {
                            PositionId = scopedBookPositionId
                        },
                        currency: new LedgerEntryCurrency("USD", "USD", 0m, amount, 1m))
                ],
                new JournalEntryMetadata(
                    SecurityId: scopedSecurityId,
                    EffectiveDate: new DateOnly(2026, 5, 12),
                    IdempotencyKey: idempotencyKey)),
            AggregateId: ledgerBookId,
            PeriodId: periodId,
            SourceEventId: sourceEventId,
            LedgerBookId: ledgerBookId);
    }

    private static RetainedEvidenceIdentityDto BuildEvidence(string evidenceId, char hashCharacter)
        => new(
            evidenceId,
            $"evidence://asset-accounting/{evidenceId}",
            new string(hashCharacter, 64),
            "BrokerStatement",
            $"statement:{evidenceId}",
            RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "fund-controller",
            DateTimeOffset.Parse("2026-05-12T13:30:00Z"),
            new DateOnly(2026, 5, 12),
            EvidenceVersion: 1,
            DateTimeOffset.Parse("2026-05-12T13:45:00Z"),
            "data-operations",
            "AssetAccountingEvent",
            $"event:{evidenceId}");

    private static string ReadMigration(string fileName)
        => ReadRepositoryFile(
            "src",
            "Meridian.Storage",
            "Ledger",
            "Migrations",
            fileName);

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Unable to locate repository file '{Path.Combine(pathParts)}'.");
    }
}
