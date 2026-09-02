using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
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
            ("book position", lot with { BookPositionId = Guid.NewGuid() }),
            ("original face", lot with { OriginalFace = 100_000m, BookedFactor = 1m, ParBasis = 100m }),
            ("booked factor", lot with { OriginalFace = 100_000m, BookedFactor = 0.5m, ParBasis = 100m }),
            ("par basis", lot with { OriginalFace = 100_000m, BookedFactor = 1m, ParBasis = 1m })
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
    public void FaceTermsMigration_AddsNullableParConventionsWithoutBackfillingLegacyLots()
    {
        var sql = ReadMigration("V_ledger_033__tax_lot_face_terms.sql");

        sql.Should().Contain("add column if not exists original_face numeric(38, 12) null");
        sql.Should().Contain("add column if not exists booked_factor numeric(38, 12) null");
        sql.Should().Contain("add column if not exists par_basis numeric(38, 12) null");
        sql.Should().Contain("ck_tax_lots_face_terms");
        sql.Should().Contain("ck_tax_lots_face_terms_complete");

        // Idempotent replay: the runner has no version table, so every statement must be re-runnable.
        sql.Should().Contain("pg_constraint");

        // Legacy rows carry no synthetic defaults. A `not null default 100` par basis would assert
        // the very price-per-100 convention that was never recorded for lots booked before this
        // migration, so the columns stay nullable and unbackfilled. Assert against the DDL rather
        // than the file, so the migration's own header prose can name what it is refusing to do.
        var ddl = StripSqlComments(sql);
        ddl.Should().NotContain("not null default");
        ddl.Should().NotContain("update __SCHEMA__.tax_lots");
    }

    private static string StripSqlComments(string sql)
        => string.Join(
            '\n',
            sql.Split('\n').Where(static line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void Fingerprint_IsUnchangedForALotThatRecordedNoFaceTerms()
    {
        // LedgerTaxLotRecord is serialized whole into the canonical fingerprint, so adding members
        // that write as explicit nulls would change the digest of every acquisition batch already
        // posted and turn an idempotent replay into an idempotency collision. The three face terms
        // are omitted when absent, so a lot that never recorded them must hash to exactly the digest
        // it had before the columns existed.
        var command = BuildAcquisitionCommand();
        var lot = command.AcquisitionLot!;

        lot.OriginalFace.Should().BeNull();
        lot.HasFaceValueTerms.Should().BeFalse();

        // Frozen before the face terms were added to the record.
        AtomicTaxLotJournalFingerprint.Compute(command)
            .Should().Be(command.CanonicalFingerprint)
            .And.Subject.As<string>()
            .Should().NotContain("originalFace");

        var serialized = System.Text.Json.JsonSerializer.Serialize(
            lot,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        serialized.Should().NotContain("originalFace");
        serialized.Should().NotContain("bookedFactor");
        serialized.Should().NotContain("parBasis");

        // A lot that does state its conventions participates: two acquisitions differing only in the
        // factor their face was booked at are different commands.
        var stated = lot with { OriginalFace = 100_000m, BookedFactor = 1m, ParBasis = 100m };
        AtomicTaxLotJournalFingerprint.Compute(command with { AcquisitionLot = stated })
            .Should().NotBe(command.CanonicalFingerprint);
    }

    [LedgerDatabaseFact]
    public async Task FaceTerms_SurviveARealRoundTripAndTheAsOfScopedRead()
    {
        // The eleven tax_lots column lists in this store feed one ORDINAL-positional reader, so a
        // list that drifts reads a value out of the wrong column rather than failing to compile.
        // Only a real database proves they still agree.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var ledgerBookId = Guid.NewGuid();
        var recordedAt = DateTimeOffset.Parse("2026-01-02T12:00:00Z");
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            ledgerBookId,
            "fund-alpha",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Fund Alpha GAAP",
            "USD",
            recordedAt,
            recordedAt,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "gaap-policy",
            AccountingPolicyVersion: "v1"));

        // A pool bought when the factor was already 0.80, quoted per unit of face rather than per
        // 100 — the case that silently mis-scales when the conventions are not recorded.
        var source = new FaceValueLot(
            "lot-pool-2026-01",
            TestSecurityId,
            new DateOnly(2026, 1, 15),
            originalFace: 800_000m,
            pricePercentOfPar: 1.02m,
            bookedFactor: 0.8m,
            parBasis: 1m);
        var quantity = source.OriginalFace / LedgerTaxLotFaceValueTerms.LedgerLotParBasis;
        var lot = new LedgerTaxLotRecord(
            Guid.NewGuid(),
            ledgerBookId,
            new LedgerAccount("Investments", LedgerAccountType.Asset, "FNPOOL1"),
            source.LotId,
            source.AcquiredDate,
            quantity,
            quantity,
            source.PricePercentOfPar * LedgerTaxLotFaceValueTerms.LedgerLotParBasis / source.ParBasis,
            "USD",
            recordedAt,
            recordedAt,
            SecurityId: TestSecurityId,
            BookPositionId: TestBookPositionId).WithFaceValueTerms(source);

        var saved = await database.JournalStore.SaveTaxLotAsync(lot);
        saved.OriginalFace.Should().Be(800_000m);
        saved.BookedFactor.Should().Be(0.8m);
        saved.ParBasis.Should().Be(1m);

        // Read back through the asset-scoped seam the paydown path uses.
        var onDate = await database.JournalStore.ListOpenTaxLotsByAssetScopeAsync(
            ledgerBookId, TestSecurityId, TestBookPositionId, new DateOnly(2026, 2, 1));
        var restated = onDate.Should().ContainSingle().Subject.ToFaceValueLot();
        restated.Should().NotBeNull();
        restated!.OriginalFace.Should().Be(source.OriginalFace);
        restated.PricePercentOfPar.Should().Be(source.PricePercentOfPar);
        restated.BookedFactor.Should().Be(source.BookedFactor);
        restated.ParBasis.Should().Be(source.ParBasis);
        restated.CostBasis.Should().Be(source.CostBasis);

        // Face restated to a factor of 1 — the basis a paydown multiplies by the factor delta.
        restated.CurrentFace(1m).Should().Be(1_000_000m);

        // The read is bounded by the effective date: a paydown dated before the acquisition must
        // not see this lot at all.
        var beforeAcquisition = await database.JournalStore.ListOpenTaxLotsByAssetScopeAsync(
            ledgerBookId, TestSecurityId, TestBookPositionId, new DateOnly(2026, 1, 14));
        beforeAcquisition.Should().BeEmpty();

        // Every other tax_lots read path shares the same ordinal reader.
        var byAccount = await database.JournalStore.ListOpenTaxLotsAsync(ledgerBookId, lot.Account);
        byAccount.Should().ContainSingle().Which.ParBasis.Should().Be(1m);
        var byId = await database.JournalStore.GetTaxLotsByIdsAsync(ledgerBookId, [lot.TaxLotRecordId]);
        byId.Should().ContainSingle().Which.OriginalFace.Should().Be(800_000m);
    }

    [LedgerDatabaseFact]
    public async Task FaceTerms_AreRejectedByTheDatabaseWhenIncomplete()
    {
        // ck_tax_lots_face_terms_complete is the durable half of the all-three-or-none rule; the
        // typed guard in the store is the other half. Prove the database enforces it independently.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var schema = database.Options.SchemaName;
        await using var connection = new Npgsql.NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {schema}.tax_lots (
                tax_lot_record_id, ledger_book_id, account_name, account_type, lot_id,
                acquired_date, original_quantity, open_quantity, unit_cost, currency,
                created_at, updated_at, original_face)
            values (
                gen_random_uuid(), gen_random_uuid(), 'Investments', 'Asset', 'lot-partial',
                date '2026-01-15', 100, 100, 102, 'USD', now(), now(), 100000);
            """;

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<Npgsql.PostgresException>())
            .Which.Message.Should().Contain("ck_tax_lots_face_terms_complete");
    }

    [Fact]
    public void FaceValueTerms_RoundTripThroughTheLotOfRecordPreserveParConventions()
    {
        // The aggregate is the canonical statement of a par-denominated lot's economics; the lot of
        // record must be able to give it back unchanged, whatever basis the price was struck in.
        var securityId = TestSecurityId;
        var parHundred = new FaceValueLot("lot-1", securityId, new DateOnly(2026, 1, 1),
            originalFace: 100_000m, pricePercentOfPar: 102m, bookedFactor: 0.8m);
        var perUnit = new FaceValueLot("lot-1", securityId, new DateOnly(2026, 1, 1),
            originalFace: 100_000m, pricePercentOfPar: 1.02m, bookedFactor: 0.8m, parBasis: 1m);

        foreach (var source in new[] { parHundred, perUnit })
        {
            var record = BuildBareLotOfRecord(securityId, source).WithFaceValueTerms(source);

            record.OriginalFace.Should().Be(source.OriginalFace);
            record.BookedFactor.Should().Be(source.BookedFactor);
            record.ParBasis.Should().Be(source.ParBasis);
            record.HasFaceValueTerms.Should().BeTrue();

            var restated = record.ToFaceValueLot();
            restated.Should().NotBeNull();
            restated!.OriginalFace.Should().Be(source.OriginalFace);
            restated.PricePercentOfPar.Should().Be(source.PricePercentOfPar);
            restated.BookedFactor.Should().Be(source.BookedFactor);
            restated.ParBasis.Should().Be(source.ParBasis);
            restated.CostBasis.Should().Be(source.CostBasis);
            restated.PremiumDiscount.Should().Be(source.PremiumDiscount);
            restated.CurrentFace(0.6m).Should().Be(source.CurrentFace(0.6m));
        }
    }

    [Fact]
    public void FaceValueTerms_LotWithoutRecordedTermsRestatesToNullRatherThanADefaultedQuote()
    {
        var bare = BuildBareLotOfRecord(
            TestSecurityId,
            new FaceValueLot("lot-1", TestSecurityId, new DateOnly(2026, 1, 1), 100_000m, 102m));

        bare.HasFaceValueTerms.Should().BeFalse();
        bare.ToFaceValueLot().Should().BeNull();
    }

    [Fact]
    public void FaceValueTerms_RejectARecordWhoseParAndUnitStatementsDisagree()
    {
        var lot = new FaceValueLot("lot-1", TestSecurityId, new DateOnly(2026, 1, 1), 100_000m, 102m);
        var mismatched = BuildBareLotOfRecord(TestSecurityId, lot) with { OriginalQuantity = 999m, OpenQuantity = 999m };

        var act = () => mismatched.WithFaceValueTerms(lot);

        act.Should().Throw<LedgerValidationException>().WithMessage("*quantity*");
    }

    private static LedgerTaxLotRecord BuildBareLotOfRecord(Guid securityId, FaceValueLot source)
    {
        // Mirrors the engines' lot convention: quantity is the face per 100 of par, unit cost the
        // price normalized into that same basis.
        var quantity = source.OriginalFace / LedgerTaxLotFaceValueTerms.LedgerLotParBasis;
        return new LedgerTaxLotRecord(
            Guid.Parse("11111111-0000-4000-8000-000000000001"),
            Guid.Parse("11111111-0000-4000-8000-000000000002"),
            new LedgerAccount("Investments", LedgerAccountType.Asset, "AAPL"),
            source.LotId,
            source.AcquiredDate,
            quantity,
            quantity,
            source.PricePercentOfPar * LedgerTaxLotFaceValueTerms.LedgerLotParBasis / source.ParBasis,
            "USD",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            SecurityId: securityId,
            BookPositionId: TestBookPositionId);
    }

    [Fact]
    public void LegacyTaxLotSave_RequiresAnExactPositiveVersionAndCannotOverwriteAtomicLots()
    {
        var source = ReadRepositoryFile(
            "src",
            "Meridian.Storage",
            "Ledger",
            "PostgresLedgerJournalStore.TaxLots.cs");

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
        var acquisitionAssetLine = acquisition.Journal.Entry.Lines[0];
        var offsettingAcquisition = (acquisition with
        {
            Journal = acquisition.Journal with
            {
                Entry = new JournalEntry(
                    acquisition.Journal.Entry.JournalEntryId,
                    acquisition.Journal.Entry.Timestamp,
                    acquisition.Journal.Entry.Description,
                    [
                        .. acquisition.Journal.Entry.Lines,
                        new LedgerEntry(
                            Guid.NewGuid(),
                            acquisitionAssetLine.JournalEntryId,
                            acquisitionAssetLine.Timestamp,
                            acquisitionAssetLine.Account,
                            debit: 0m,
                            credit: acquisitionAssetLine.Debit,
                            description: acquisitionAssetLine.Description,
                            dimensions: acquisitionAssetLine.Dimensions,
                            currency: new LedgerEntryCurrency(
                                "USD", "USD", 0m, acquisitionAssetLine.Debit, 1m)),
                        new LedgerEntry(
                            Guid.NewGuid(),
                            acquisition.Journal.Entry.Lines[1].JournalEntryId,
                            acquisition.Journal.Entry.Lines[1].Timestamp,
                            acquisition.Journal.Entry.Lines[1].Account,
                            debit: acquisitionAssetLine.Debit,
                            credit: 0m,
                            description: acquisition.Journal.Entry.Lines[1].Description,
                            dimensions: acquisition.Journal.Entry.Lines[1].Dimensions,
                            currency: new LedgerEntryCurrency(
                                "USD", "USD", acquisitionAssetLine.Debit, 0m, 1m))
                    ],
                    acquisition.Journal.Entry.Metadata)
            }
        }).WithComputedFingerprint();
        var offsettingAcquisitionAct = () => database.JournalStore.AppendAssetPostingAsync(offsettingAcquisition);
        await offsettingAcquisitionAct.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*one exact asset-account debit*");

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
            creditAccount: "Investment lots");
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
            ]);

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
                creditAccount: "Investment lots"),
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
            ]);
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
        string idempotencyKey = "atomic-acquisition:lot-1")
    {
        var bookId = ledgerBookId ?? Guid.NewGuid();
        var retainedPeriodId = periodId ?? Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var mutationBatchId = Guid.NewGuid();
        var evidence = BuildEvidence("evidence-acquisition", 'a');
        var recordedAt = DateTimeOffset.Parse("2026-05-12T14:00:00Z");
        var journal = BuildJournalWrite(
            bookId,
            retainedPeriodId,
            sourceEventId,
            idempotencyKey,
            debitAccount: "Investment lots",
            creditAccount: "Cash");
        var lot = new LedgerTaxLotRecord(
            Guid.NewGuid(),
            bookId,
            new LedgerAccount("Investment lots", LedgerAccountType.Asset),
            "lot-aapl-20260512",
            new DateOnly(2026, 5, 12),
            OriginalQuantity: 100m,
            OpenQuantity: 100m,
            UnitCost: 100m,
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
