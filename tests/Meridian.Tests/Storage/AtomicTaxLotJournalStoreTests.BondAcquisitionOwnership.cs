using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BondIncomeAcquisition_LockedWriterRefusesImplicitFunctionalAndTransactionCurrencyRounding()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, references, _) = await PrepareOwnedBondAcquisitionAsync(database);
        var policy = Substitute.For<IBondIncomeService>();
        var writer = new PostgresLedgerJournalStore(database.Options, backfillSecurityMaster: () => references.Securities,
            backfillPositions: () => references.Positions, bondIncomeAuthority: () => policy);
        foreach (var transactionCurrencyOnly in new[] { false, true })
        {
            const decimal unrepresentableDelta = 0.00000000001m;
            var entry = command.Journal.Entry;
            var lines = entry.Lines.Select(line =>
            {
                var debit = line.Debit == 0m ? 0m : line.Debit + unrepresentableDelta;
                var credit = line.Credit == 0m ? 0m : line.Credit + unrepresentableDelta;
                var currency = transactionCurrencyOnly
                    ? new LedgerEntryCurrency("USD", "USD", debit, credit, 1m) : line.Currency;
                return new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                    transactionCurrencyOnly ? line.Debit : debit, transactionCurrencyOnly ? line.Credit : credit,
                    line.Description, line.Dimensions, currency);
            }).ToArray();
            var invalid = (command with
            {
                Journal = command.Journal with
                { Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) }
            }).WithComputedFingerprint();
            await writer.Invoking(store => store.AppendAssetPostingAsync(invalid)).Should()
                .ThrowAsync<LedgerValidationException>().WithMessage("*ten-decimal journal boundary*");
            (await writer.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
            (await writer.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        }
        await policy.DidNotReceive().RequireAcquisitionPolicyAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(),
            Arg.Any<BondIncomeAcquisitionDto>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BondIncomeAcquisition_LockedWriterPersistsExactOwnedBirthAndReplaysWithoutNewPolicyApproval()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, references, owner) = await PrepareOwnedBondAcquisitionAsync(database);
        var policy = Substitute.For<IBondIncomeService>();
        var writer = new PostgresLedgerJournalStore(database.Options, backfillSecurityMaster: () => references.Securities,
            backfillPositions: () => references.Positions, bondIncomeAuthority: () => policy);

        var result = await writer.AppendAssetPostingAsync(command);

        result.IsExactReplay.Should().BeFalse();
        result.MutatedLots.Single().Account.FinancialAccountId.Should().Be(owner.ToString("D"));
        result.Journal.Entry.Lines.Should().OnlyContain(line => line.Account.FinancialAccountId == owner.ToString("D")
            && line.Dimensions!.AccountId == owner.ToString("D")
            && line.Dimensions.TaxLotId == command.AcquisitionLot!.LotId);
        await policy.Received(1).RequireAcquisitionPolicyAsync(command.LedgerBookId, command.AcquisitionLot!.AcquiredDate,
            command.AcquisitionLot.Acquisition!.BondIncome!, 10_000m, Arg.Any<CancellationToken>());
        // An immutable exact receipt remains readable if today's source/policy services are down.
        var replay = await new PostgresLedgerJournalStore(database.Options).AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.CanonicalFingerprint.Should().Be(result.CanonicalFingerprint);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BondIncomeAcquisition_LockedWriterRejectsForgedOrMissingBirthOwnershipWithoutReservingReceipt()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, references, _) = await PrepareOwnedBondAcquisitionAsync(database);
        var policy = Substitute.For<IBondIncomeService>();
        var writer = new PostgresLedgerJournalStore(database.Options, backfillSecurityMaster: () => references.Securities,
            backfillPositions: () => references.Positions, bondIncomeAuthority: () => policy);
        var before = (await writer.VerifyLedgerEventAuditAsync()).ChainedEvents;
        foreach (var flaw in new[] { "account", "owner-dimension", "book", "lot", "missing-lot", "holding-account" })
        {
            var entry = command.Journal.Entry;
            var lines = entry.Lines.Select(line =>
            {
                var dimensions = line.Dimensions! with
                {
                    AccountId = flaw == "owner-dimension" ? Guid.NewGuid().ToString("D") : line.Dimensions!.AccountId,
                    BookId = flaw == "book" ? Guid.NewGuid().ToString("D") : line.Dimensions!.BookId,
                    TaxLotId = flaw == "lot" ? "another-physical-lot" : flaw == "missing-lot" ? null : line.Dimensions!.TaxLotId
                };
                return new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
                    flaw == "account" ? line.Account with { FinancialAccountId = Guid.NewGuid().ToString("D") } : line.Account,
                    line.Debit, line.Credit, line.Description, dimensions, line.Currency);
            }).ToArray();
            var invalid = (command with
            {
                Journal = command.Journal with { Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) },
                AcquisitionLot = flaw == "holding-account"
                    ? command.AcquisitionLot! with { Account = command.AcquisitionLot.Account with { FinancialAccountId = null } }
                    : command.AcquisitionLot
            }).WithComputedFingerprint();
            await writer.Invoking(store => store.AppendAssetPostingAsync(invalid)).Should()
                .ThrowAsync<LedgerValidationException>().WithMessage("*birth journal*ownership*");
            (await writer.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
            (await writer.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
            (await writer.GetTaxLotsByIdsAsync(command.LedgerBookId, [command.AcquisitionLot!.TaxLotRecordId])).Should().BeEmpty();
            (await writer.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(before);
        }
        await policy.DidNotReceive().RequireAcquisitionPolicyAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(),
            Arg.Any<BondIncomeAcquisitionDto>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
        (await writer.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    private static async Task<(AtomicTaxLotJournalCommand Command, LotReferenceAuthority References, Guid Owner)>
        PrepareOwnedBondAcquisitionAsync(LedgerPostgresTestDatabase database)
    {
        var seed = await PrepareFactFreeAcquisitionAsync(database, face: true);
        var book = (await database.JournalStore.GetLedgerBookAsync(seed.LedgerBookId))!;
        book = book with { FundStructureNodeKind = FundStructureNodeKindDto.Account };
        await database.JournalStore.SaveLedgerBookAsync(book);
        var owner = book.FundStructureNodeId;
        var references = await database.SeedLotReferenceAuthorityAsync(seed.LedgerBookId, TestSecurityId, TestBookPositionId);
        var command = BuildAcquisitionCommand(seed.LedgerBookId, seed.Journal.PeriodId, seed.ExpectedPeriodVersion);
        var lot = command.AcquisitionLot!;
        lot = lot with
        {
            Account = lot.Account with { FinancialAccountId = owner.ToString("D") },
            OriginalFace = 10_000m,
            BookedFactor = 1m,
            ParBasis = 100m,
            Acquisition = lot.Acquisition! with
            {
                QuantityBasis = LotQuantityBasis.Face,
                FaceValueTerms = new(100m, 1m, BondAmortizationMethod.NoAmortization, null),
                BondIncome = new(lot.CreatedAt, 10_000m, 0m, 1m, [], lot.Acquisition.Evidence)
            }
        };
        var entry = command.Journal.Entry;
        var lines = entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account with { FinancialAccountId = owner.ToString("D") }, line.Debit, line.Credit, line.Description,
            line.Dimensions! with { AccountId = owner.ToString("D"), BookId = book.LedgerBookId.ToString("D"), TaxLotId = lot.LotId }, line.Currency)).ToArray();
        return ((command with
        {
            ReferenceVersions = references.Versions,
            AcquisitionLot = lot,
            Journal = command.Journal with { Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) }
        }).WithComputedFingerprint(),
            references, owner);
    }
}
