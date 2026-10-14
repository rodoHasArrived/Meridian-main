using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [Fact]
    public void Direction_JoinsTheFingerprintOnlyWhenNotHistoricalLong()
    {
        var command = BuildAcquisitionCommand();
        JsonSerializer.Serialize(command).Should().NotContain("Direction");
        var restored = JsonSerializer.Deserialize<AtomicTaxLotJournalCommand>(JsonSerializer.Serialize(command))!;
        restored.Direction.Should().Be(OpenLotDirection.Long);
        restored.WithComputedFingerprint().CanonicalFingerprint.Should().Be(command.CanonicalFingerprint);

        var shortCommand = (command with
        {
            Direction = OpenLotDirection.Short,
            AcquisitionLot = command.AcquisitionLot! with
            { Acquisition = command.AcquisitionLot.Acquisition! with { Direction = OpenLotDirection.Short } }
        }).WithComputedFingerprint();
        shortCommand.CanonicalFingerprint.Should().NotBe(command.CanonicalFingerprint);
        JsonSerializer.Deserialize<AtomicTaxLotJournalCommand>(JsonSerializer.Serialize(shortCommand))!
            .WithComputedFingerprint().CanonicalFingerprint.Should().Be(shortCommand.CanonicalFingerprint);
    }

    [Fact]
    public async Task AcquisitionDirectionMismatch_RefusesBeforeOpeningConnection()
    {
        var command = (BuildAcquisitionCommand() with { Direction = OpenLotDirection.Short }).WithComputedFingerprint();
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var post = () => store.AppendAssetPostingAsync(command);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*direction must match*acquisition facts*");
    }

    [Fact]
    public async Task UnknownDirection_RefusesBeforeOpeningConnection()
    {
        var command = (BuildAcquisitionCommand() with { Direction = (OpenLotDirection)99 }).WithComputedFingerprint();
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var post = () => store.AppendAssetPostingAsync(command);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*direction must be Long or Short*");
    }

    [Fact]
    public async Task DirectSave_CannotBypassTheShortPostingGate()
    {
        var retained = OpenLotConvergenceTests.Lot(1);
        retained = retained with { Acquisition = retained.Acquisition! with { Direction = OpenLotDirection.Short } };
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());

        var save = () => store.SaveTaxLotAsync(retained);

        await save.Should().ThrowAsync<LedgerValidationException>().WithMessage("*direct lot saves cannot enable short accounting*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task NewLongAcquisition_RejectsAMatchingBalancedLiabilityDebit()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var seed = await PrepareFactFreeAcquisitionAsync(database, face: false);
        var authority = await database.SeedLotReferenceAuthorityAsync(seed.LedgerBookId, TestSecurityId, TestBookPositionId);
        var command = (BuildAcquisitionCommand(seed.LedgerBookId, seed.Journal.PeriodId, seed.ExpectedPeriodVersion) with
        { ReferenceVersions = authority.Versions }).WithComputedFingerprint();
        var lot = command.AcquisitionLot!;
        var liability = lot.Account with { AccountType = LedgerAccountType.Liability };
        var entry = command.Journal.Entry;
        var changed = (command with
        {
            AcquisitionLot = lot with { Account = liability },
            Journal = command.Journal with
            {
                Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description,
                    entry.Lines.Select(line => line.Account == lot.Account
                        ? new LedgerEntry(line.EntryId, entry.JournalEntryId, entry.Timestamp, liability,
                            line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency)
                        : line).ToArray(), entry.Metadata)
            }
        }).WithComputedFingerprint();

        var post = () => database.JournalStore.AppendAssetPostingAsync(changed);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*long-lot acquisitions require an asset*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        (await database.JournalStore.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task UnsupportedShortAcquisition_LeavesNoWritesAndDoesNotReserveTheLongRetry()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var seed = await PrepareFactFreeAcquisitionAsync(database, face: false);
        var authority = await database.SeedLotReferenceAuthorityAsync(seed.LedgerBookId, TestSecurityId, TestBookPositionId);
        var command = (BuildAcquisitionCommand(seed.LedgerBookId, seed.Journal.PeriodId, seed.ExpectedPeriodVersion) with
        { ReferenceVersions = authority.Versions }).WithComputedFingerprint();
        var shortCommand = (command with
        {
            Direction = OpenLotDirection.Short,
            AcquisitionLot = command.AcquisitionLot! with
            { Acquisition = command.AcquisitionLot.Acquisition! with { Direction = OpenLotDirection.Short } }
        }).WithComputedFingerprint();
        var before = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;

        var post = () => database.JournalStore.AppendAssetPostingAsync(shortCommand);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*short posting is not enabled*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(before);
        (await database.JournalStore.GetTaxLotsByIdsAsync(command.LedgerBookId, [command.AcquisitionLot!.TaxLotRecordId])).Should().BeEmpty();

        var posted = await database.JournalStore.AppendAssetPostingAsync(command);
        posted.IsExactReplay.Should().BeFalse();
        posted.MutatedLots.Single().Acquisition!.Direction.Should().Be(OpenLotDirection.Long);
        var restarted = new PostgresLedgerJournalStore(database.Options);
        (await restarted.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
        var collide = () => restarted.AppendAssetPostingAsync(shortCommand);
        await collide.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
        (await restarted.GetByPeriodAsync(command.Journal.PeriodId)).Should().ContainSingle();
    }
}
