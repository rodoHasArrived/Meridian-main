using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    private static readonly DateOnly ReliefPrecisionDate = new(2026, 9, 12);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AppendAssetPostingAsync_ReliefPrecisionUnderflowRefusesWithoutWritesAndFullCloseReplays()
        => VerifyReliefPrecisionRefusalAsync(100_000_000.0000000001m, 1m, 100_000_000m,
            0.000000000001m, underflows: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AppendAssetPostingAsync_ReliefPrecisionBeyondDurableScaleRefusesWithoutWritesAndFullCloseReplays()
        => VerifyReliefPrecisionRefusalAsync(7m, 2m, 4m, 1m, underflows: false);

    private static async Task VerifyReliefPrecisionRefusalAsync(decimal quantity, decimal unitCost,
        decimal capitalReturn, decimal partialQuantity, bool underflows)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var bookId = Guid.NewGuid();
        var openedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        const string fund = "fund-relief-precision";
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(bookId, fund, Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Relief Precision Book", "USD", openedAt, openedAt), ct);
        var authority = await database.SeedLotReferenceAuthorityAsync(bookId, TestSecurityId, TestBookPositionId)
            .WaitAsync(ct);
        var period = await database.JournalStore.SavePeriodAsync(new LedgerAccountingPeriod(Guid.NewGuid(),
            bookId, 2026, 9, "2026-09", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
            "Open", openedAt, ClosedAt: null, Version: 0), expectedVersion: 0, ct: ct);
        await database.JournalStore.SaveTaxLotPolicyAsync(new LedgerAccountTaxLotPolicyRecord(Guid.NewGuid(),
            bookId, AverageCostAccount, LedgerTaxLotReliefMethod.AverageCost, "tax-policy-avg",
            new DateOnly(2026, 9, 1), openedAt, openedAt), ct);

        var acquisition = BuildCanonicalAcquisition(bookId, period.PeriodId, period.Version,
            "lot-relief-precision", unitCost);
        var original = acquisition.AcquisitionLot!;
        var basis = quantity * unitCost;
        var acquisitionJournal = BuildJournalWrite(bookId, period.PeriodId, acquisition.SourceEventId,
            acquisition.IdempotencyKey, amount: basis);
        acquisition = (acquisition with
        {
            Journal = acquisitionJournal,
            AcquisitionLot = original with
            {
                OriginalQuantity = quantity,
                OpenQuantity = quantity,
                SourceJournalEntryId = acquisitionJournal.Entry.JournalEntryId,
                Acquisition = original.Acquisition! with { TransactionCostBasis = basis, FunctionalCostBasis = basis }
            }
        }).WithComputedFingerprint();
        acquisition = DateReliefPrecisionCommand(acquisition);
        var acquired = (await database.JournalStore.AppendAssetPostingAsync(acquisition, ct)).MutatedLots.Single();

        // A real governed cash return leaves the small retained basis; no precision-invalid
        // quantity, acquisition quote, SQL fixture, or fabricated adjustment is needed.
        var returnCommand = BuildReliefPrecisionCapitalReturn(acquisition, acquired.ToOpenLot(),
            authority, period, fund, capitalReturn);
        var before = (await database.JournalStore.AppendAssetPostingAsync(returnCommand, ct)).MutatedLots.Single();
        var canonical = before.ToOpenLot();
        canonical.OpenFunctionalCostBasis.Should().Be(basis - capitalReturn);
        before.BasisAdjustment!.Reason.Should().Be(OpenLotBasisAdjustmentReasons.CapitalReturn);
        var countsBefore = await ReadReliefPrecisionPostingCountsAsync(database, ct);
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents;

        decimal expectedPartialBasis;
        string refusalMessage;
        if (underflows)
        {
            var preview = () => new OpenLotReliefService().Select([canonical], partialQuantity,
                OpenLotReliefMethod.AverageCost);
            preview.Should().Throw<InvalidOperationException>().WithMessage("*positive*decimal precision*");
            // A caller cannot repair underflow by asserting the smallest positive storage amount.
            // It passes command-shape validation so the locked canonical guard must refuse it.
            expectedPartialBasis = 0.000000000001m;
            refusalMessage = "*positive*decimal precision*";
        }
        else
        {
            expectedPartialBasis = new OpenLotReliefService().Select([canonical], partialQuantity,
                OpenLotReliefMethod.AverageCost).FunctionalCostBasis;
            expectedPartialBasis.Should().Be(10m / 7m);
            decimal.Round(expectedPartialBasis, 12).Should().NotBe(expectedPartialBasis);
            refusalMessage = "*durable twelve-decimal precision*";
        }

        const string key = "relief-precision:dispose";
        var partial = DateReliefPrecisionCommand(BuildAverageCostDisposal(bookId, period.PeriodId, period.Version, key,
            [(before, before.Version, before.OpenQuantity, partialQuantity, expectedPartialBasis)]));
        var attempt = () => database.JournalStore.AppendAssetPostingAsync(partial, ct);
        await attempt.Should().ThrowAsync<LedgerValidationException>().WithMessage(refusalMessage);
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(partial.MutationBatchId, ct)).Should().BeNull();
        (await database.JournalStore.GetTaxLotsByIdsAsync(bookId, [before.TaxLotRecordId], ct))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(before);
        (await ReadReliefPrecisionPostingCountsAsync(database, ct)).Should().Be(countsBefore);
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(auditBefore);

        // Reuse the refused source and idempotency identities with a fully representable plan.
        // Refusal must leave both identities available for a new reviewed command.
        var closing = DateReliefPrecisionCommand(BuildAverageCostDisposal(bookId, period.PeriodId, period.Version, key,
            [(before, before.Version, before.OpenQuantity, before.OpenQuantity, canonical.OpenFunctionalCostBasis)]));
        closing = (closing with
        {
            MutationBatchId = partial.MutationBatchId,
            SourceEventId = partial.SourceEventId,
            Journal = closing.Journal with { SourceEventId = partial.SourceEventId }
        }).WithComputedFingerprint();
        var closed = await database.JournalStore.AppendAssetPostingAsync(closing, ct);
        closed.IsExactReplay.Should().BeFalse();
        closed.MutatedLots.Should().ContainSingle().Which.OpenQuantity.Should().Be(0m);
        closed.Mutations.Should().ContainSingle().Which.CostBasis.Should().Be(canonical.OpenFunctionalCostBasis);
        closed.MutatedLots.Single().Acquisition.Should().BeEquivalentTo(acquired.Acquisition);
        (await database.JournalStore.ListOpenTaxLotsAsync(bookId, AverageCostAccount, ct)).Should().BeEmpty();
        (await ReadReliefPrecisionPostingCountsAsync(database, ct)).Should().Be(
            (countsBefore.Journals + 1, countsBefore.Batches + 1, countsBefore.Mutations + 1));

        var restarted = database.RestartJournalStore();
        var retainedJournal = (await restarted.GetByPeriodAsync(period.PeriodId, ct))
            .Single(record => record.Entry.JournalEntryId == closing.Journal.Entry.JournalEntryId).Entry;
        retainedJournal.Lines.Sum(static line => line.Debit).Should().Be(canonical.OpenFunctionalCostBasis);
        retainedJournal.Lines.Sum(static line => line.Credit).Should().Be(canonical.OpenFunctionalCostBasis);
        var auditAfterClose = (await restarted.VerifyLedgerEventAuditAsync(ct)).ChainedEvents;
        var replay = await restarted.AppendAssetPostingAsync(closing, ct);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(closed.Mutations);
        (await ReadReliefPrecisionPostingCountsAsync(database, ct)).Should().Be(
            (countsBefore.Journals + 1, countsBefore.Batches + 1, countsBefore.Mutations + 1));
        (await restarted.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(auditAfterClose);
    }

    private static AtomicTaxLotJournalCommand BuildReliefPrecisionCapitalReturn(
        AtomicTaxLotJournalCommand acquisition, OpenLotDto lot, LotReferenceAuthority authority,
        LedgerAccountingPeriod period, string fund, decimal amount)
    {
        var instruction = CapitalReturnTestData.Build(lot, amount, amount, authority.Position.Version,
            authority.Security.Version, OpenLotAmortization.SecurityHash(authority.Security), period.PeriodId,
            period.Version, fund, date: ReliefPrecisionDate,
            accountingBasis: authority.Position.BookContext.AccountingBasis);
        var request = CapitalReturnTestData.MapRequest(instruction);
        var retained = request.RetainedEvidence.ToArray();
        var eventRef = request.Projection.EconomicEvent! with
        { RetainedEvidence = retained, EvidenceLinks = retained.Select(item => item.EvidenceUri).ToArray() };
        var key = "relief-precision:capital-return";
        var journalId = Guid.NewGuid();
        var date = eventRef.EffectiveDate;
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lot.SecurityId) { PositionId = lot.BookPositionId };
        var tags = new Dictionary<string, string>(SecurityMasterLineageTags(lot.SecurityId))
        { [OpenLotCapitalReturn.JournalFingerprintTag] = OpenLotCapitalReturn.Fingerprint(instruction) };
        const string description = "Reviewed return of capital before precision-boundary relief";
        var entry = new JournalEntry(journalId, at, description,
            [new LedgerEntry(Guid.NewGuid(), journalId, at, AverageCostAccount, 0m, amount, description,
                dimensions, new("USD", "USD", 0m, amount, 1m)),
             new LedgerEntry(Guid.NewGuid(), journalId, at, LedgerAccounts.Cash, amount, 0m, description,
                dimensions, new("USD", "USD", amount, 0m, 1m))],
            new(SecurityId: lot.SecurityId, EffectiveDate: date, IdempotencyKey: key, Tags: tags));
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), lot.LedgerBookId, period.PeriodId,
            date, at, key, AccountingPostingIntentDto.Adjustment, eventRef.EventId, ExpectedVersion: period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-capital-return-review",
            OperatorRationale: "Reviewed current-basis reduction", LedgerBookId: lot.LedgerBookId)
        {
            Actor = "independent-controller",
            LotCapitalReturn = instruction,
            SourceEventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            BookContext = authority.Position.BookContext with { PeriodId = period.PeriodId },
            BookPositionId = lot.BookPositionId,
            EconomicEvent = eventRef,
            ProjectionLineage = instruction.Projection.ProjectionLineage! with
            { TriggerEvent = eventRef, BookPositionId = lot.BookPositionId, RetainedEvidence = retained, EvidenceLinks = eventRef.EvidenceLinks },
            RulePackReference = new("canonical-capital-return", "1", "capital-return", "1"),
            Evidence = retained.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference,
                Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate,
                EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray()
        };
        var journal = acquisition.Journal with
        {
            Entry = entry,
            SourceEventId = eventRef.EventId,
            PostingCommand = posting,
            RuleId = "capital-return",
            RuleVersion = "1"
        };
        return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), lot.LedgerBookId, journal, eventRef.EventId, key,
            period.Version, AtomicTaxLotMutationKind.CapitalReturn, retained, capitalReturn: instruction);
    }

    private static AtomicTaxLotJournalCommand DateReliefPrecisionCommand(AtomicTaxLotJournalCommand command)
    {
        // The shared acquisition/disposal builders use May. This scenario exercises a real
        // corporate-action profile adopted on August 25, so all fixture facts belong to September.
        var entry = command.Journal.Entry;
        var at = new DateTimeOffset(ReliefPrecisionDate.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero);
        static RetainedEvidenceIdentityDto DateEvidence(RetainedEvidenceIdentityDto evidence) => evidence with
        {
            EffectiveDate = ReliefPrecisionDate,
            ReviewedAtUtc = evidence.ReviewedAtUtc.AddMonths(4),
            RetainedAtUtc = evidence.RetainedAtUtc.AddMonths(4)
        };
        var datedEntry = new JournalEntry(entry.JournalEntryId, at, entry.Description,
            entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, at,
                line.Account, line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency)).ToArray(),
            entry.Metadata with { EffectiveDate = ReliefPrecisionDate });
        return (command with
        {
            Journal = command.Journal with { Entry = datedEntry },
            RetainedEvidence = command.RetainedEvidence.Select(DateEvidence).ToArray(),
            AcquisitionLot = command.AcquisitionLot is { } lot ? lot with
            {
                AcquiredDate = ReliefPrecisionDate,
                CreatedAt = at,
                UpdatedAt = at,
                Acquisition = lot.Acquisition! with
                {
                    HoldingPeriodStartDate = ReliefPrecisionDate,
                    Evidence = lot.Acquisition!.Evidence.Select(DateEvidence).ToArray()
                }
            } : null
        }).WithComputedFingerprint();
    }

    private static async Task<(long Journals, long Batches, long Mutations)> ReadReliefPrecisionPostingCountsAsync(
        LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var query = connection.CreateCommand();
        query.CommandText = $"""
            select (select count(*) from "{database.Options.SchemaName}".journal_entries),
                   (select count(*) from "{database.Options.SchemaName}".atomic_tax_lot_posting_batches),
                   (select count(*) from "{database.Options.SchemaName}".tax_lot_mutations);
            """;
        await using var reader = await query.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }
}
