using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Moq;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [Fact]
    public void ReferenceAuthority_VersionsJoinTheFingerprintWhileHistoricalAbsenceRoundTrips()
    {
        var historical = BuildAcquisitionCommand();
        var json = JsonSerializer.Serialize(historical);
        json.Should().NotContain("ReferenceVersions");
        JsonSerializer.Deserialize<AtomicTaxLotJournalCommand>(json)!.WithComputedFingerprint()
            .CanonicalFingerprint.Should().Be(historical.CanonicalFingerprint);
        var current = (historical with { ReferenceVersions = new(7, 11) }).WithComputedFingerprint();
        current.CanonicalFingerprint.Should().NotBe(historical.CanonicalFingerprint);
        foreach (var versions in new[] { new AtomicLotReferenceVersions(8, 11), new(7, 12) })
            (current with { ReferenceVersions = versions }).WithComputedFingerprint().CanonicalFingerprint
                .Should().NotBe(current.CanonicalFingerprint);
        JsonSerializer.Deserialize<AtomicTaxLotJournalCommand>(JsonSerializer.Serialize(current))!
            .WithComputedFingerprint().CanonicalFingerprint.Should().Be(current.CanonicalFingerprint);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task NewAcquisition_RequiresRetainedReferenceAuthorityWithoutWrites()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var prepared = await PrepareFactFreeAcquisitionAsync(database, face: false);
        var command = BuildAcquisitionCommand(prepared.LedgerBookId, prepared.Journal.PeriodId,
            prepared.ExpectedPeriodVersion);
        var auditBefore = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;

        var post = () => database.JournalStore.AppendAssetPostingAsync(command);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*reference*authority*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        (await database.JournalStore.GetTaxLotsByIdsAsync(command.LedgerBookId,
            [command.AcquisitionLot!.TaxLotRecordId])).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AcquisitionReferenceAuthority_RejectsStaleSecurityAndPositionWithoutReservingRetry()
        => AssertReferenceDriftAsync(disposal: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task DisposalReferenceAuthority_RejectsStaleSecurityAndPositionWithoutReservingRetry()
        => AssertReferenceDriftAsync(disposal: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReferenceAuthority_RequiresTransactionalColocatedStoresWithoutWrites()
    {
        foreach (var disposal in new[] { false, true })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (command, authority) = await PrepareReferencePostingAsync(database, disposal);
            var before = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
            var otherDatabase = new NpgsqlConnectionStringBuilder(database.Options.ConnectionString)
            { Database = "lot_reference_authority_in_another_database" }.ConnectionString;
            var invalid = new (PostgresLedgerJournalStore Store, string Message)[]
            {
                (new(database.Options), "*reference authority requires PostgreSQL*"),
                (new(database.Options, backfillSecurityMaster: () => Mock.Of<ISecurityMasterStore>(),
                    backfillPositions: () => Mock.Of<IInstrumentPositionProjectionStore>()), "*reference authority requires PostgreSQL*"),
                (new(database.Options, backfillSecurityMaster: () => new PostgresSecurityMasterStore(new()
                    { ConnectionString = otherDatabase, Schema = authority.SecurityOptions.Schema }),
                    backfillPositions: () => authority.Positions), "*Security Master in the same PostgreSQL database*"),
                (new(database.Options, backfillSecurityMaster: () => authority.Securities,
                    backfillPositions: () => new PostgresAssetOperationsProjectionStore(new()
                    { ConnectionString = otherDatabase, Schema = authority.PositionOptions.Schema })),
                    "*book positions in the same PostgreSQL database*")
            };
            foreach (var item in invalid)
            {
                var post = () => item.Store.AppendAssetPostingAsync(command);
                await post.Should().ThrowAsync<LedgerValidationException>().WithMessage(item.Message);
                (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
                (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(before);
            }
            foreach (var versions in new AtomicLotReferenceVersions?[] { null, new(0, 1), new(1, 0), new(-1, 1) })
            {
                var invalidCommand = (command with { ReferenceVersions = versions }).WithComputedFingerprint();
                var post = () => database.JournalStore.AppendAssetPostingAsync(invalidCommand);
                await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*positive retained reference authority versions*");
            }
            (await database.JournalStore.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReferenceAuthority_RejectsInactiveIneffectiveCurrencyAndFullBookScopeDrift()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, authority) = await PrepareReferencePostingAsync(database, disposal: false);
        foreach (var security in new[]
        {
            authority.Security with { Status = SecurityStatusDto.Inactive },
            authority.Security with { EffectiveFrom = command.Journal.Entry.Timestamp.AddDays(1) },
            authority.Security with { EffectiveTo = command.Journal.Entry.Timestamp.AddDays(-1) },
            authority.Security with { Currency = "GBP" }
        })
        {
            await authority.Securities.UpsertProjectionAsync(security);
            var post = () => database.JournalStore.AppendAssetPostingAsync(command);
            await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*reference authority*");
            (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        }
        await authority.Securities.UpsertProjectionAsync(authority.Security);
        var book = (await database.JournalStore.GetLedgerBookAsync(command.LedgerBookId))!;
        foreach (var changed in new[]
        {
            book with { FundProfileId = "different-owner" },
            book with { FundStructureNodeId = Guid.NewGuid() },
            book with { AccountingPolicyId = "unreviewed-policy" },
            book with { AccountingPolicyVersion = "unreviewed-version" },
            book with { AccountingBasis = Meridian.Contracts.Ledger.AccountingBasisKindDto.Gaap },
            book with { BaseCurrency = "GBP" }
        })
        {
            await database.JournalStore.SaveLedgerBookAsync(changed);
            var post = () => database.JournalStore.AppendAssetPostingAsync(command);
            await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*reference authority*scope*");
            (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        }
        await database.JournalStore.SaveLedgerBookAsync(book);
        (await database.JournalStore.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReferenceAuthority_RejectsInactiveIneffectiveAndWrongPeriodPositions()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, authority) = await PrepareReferencePostingAsync(database, disposal: false);
        var position = authority.Position;
        var date = command.AcquisitionLot!.AcquiredDate;
        foreach (var changed in new[]
        {
            position with { Status = "Inactive" },
            position with { EffectiveFrom = date.AddDays(1) },
            position with { EffectiveTo = date.AddDays(-1) },
            position with { BookContext = position.BookContext with { PeriodId = Guid.NewGuid() } }
        })
        {
            position = await authority.Positions.UpsertAsync(authority.Role, changed with { Version = position.Version + 1 },
                null, position.Version, new("reviewer", "evidence://lot/revised-position", "Reviewed position revision", DateTimeOffset.UtcNow));
            command = (command with { ReferenceVersions = new(authority.Security.Version, position.Version) }).WithComputedFingerprint();
            var post = () => database.RestartJournalStore().AppendAssetPostingAsync(command);
            await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*reference authority*");
            (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
            (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        }
        position = await authority.Positions.UpsertAsync(authority.Role, authority.Position with { Version = position.Version + 1 },
            null, position.Version, new("reviewer", "evidence://lot/restored-position", "Reviewed effective long position", DateTimeOffset.UtcNow));
        command = (command with { ReferenceVersions = new(authority.Security.Version, position.Version) }).WithComputedFingerprint();
        (await database.RestartJournalStore().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReferenceAuthority_RequiresEconomicDateBeforeCheckingPositionEffectivity()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, _) = await PrepareReferencePostingAsync(database, disposal: false);
        var entry = command.Journal.Entry;
        command = (command with
        {
            Journal = command.Journal with
            {
                PostingCommand = null,
                Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines,
                    entry.Metadata with { EffectiveDate = null })
            }
        }).WithComputedFingerprint();
        var before = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var post = () => database.JournalStore.AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*governed journal effective date*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(before);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ReferenceAuthority_RejectsShortPositionWithoutWrites()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, _) = await PrepareReferencePostingAsync(database, disposal: false, positionSide: BookPositionSides.Short);
        var before = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var post = () => database.JournalStore.AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*book-position reference authority*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.GetByPeriodAsync(command.Journal.PeriodId)).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(before);
    }

    private static async Task AssertReferenceDriftAsync(bool disposal)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, authority) = await PrepareReferencePostingAsync(database, disposal);
        var before = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        await authority.Securities.UpsertProjectionAsync(authority.Security with { Version = 2 });
        var staleSecurity = () => database.JournalStore.AppendAssetPostingAsync(command);
        await staleSecurity.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*stale*");
        command = (command with { ReferenceVersions = new(2, 1) }).WithComputedFingerprint();
        await authority.Positions.UpsertAsync(authority.Role, authority.Position with { Version = 2 }, null, 1,
            new("reviewer", "evidence://lot/later-position", "Reviewed later position", DateTimeOffset.Parse("2026-05-13T00:00:00Z")));
        var stalePosition = () => database.RestartJournalStore().AppendAssetPostingAsync(command);
        await stalePosition.Should().ThrowAsync<LedgerValidationException>().WithMessage("*book-position*stale*");
        (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(before);

        command = (command with { ReferenceVersions = new(2, 2) }).WithComputedFingerprint();
        var posted = await database.RestartJournalStore().AppendAssetPostingAsync(command);
        posted.IsExactReplay.Should().BeFalse();
        await authority.Securities.UpsertProjectionAsync(authority.Security with { Version = 3, Status = SecurityStatusDto.Inactive });
        var restartedWithoutReferences = new PostgresLedgerJournalStore(database.Options);
        var replay = await restartedWithoutReferences.AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Select(mutation => mutation.MutationRecordId).Should().Equal(posted.Mutations.Select(mutation => mutation.MutationRecordId));
        foreach (var versions in new AtomicLotReferenceVersions?[] { null, new(3, 2), new(2, 3) })
        {
            var changed = (command with { ReferenceVersions = versions }).WithComputedFingerprint();
            var collision = () => restartedWithoutReferences.AppendAssetPostingAsync(changed);
            await collision.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
        }
    }

    private static async Task<(AtomicTaxLotJournalCommand Command, LotReferenceAuthority Authority)> PrepareReferencePostingAsync(
        LedgerPostgresTestDatabase database, bool disposal, string positionSide = BookPositionSides.Long)
    {
        var seed = await PrepareFactFreeAcquisitionAsync(database, face: false);
        var authority = await database.SeedLotReferenceAuthorityAsync(seed.LedgerBookId, TestSecurityId, TestBookPositionId,
            positionSide: positionSide);
        var command = (BuildAcquisitionCommand(seed.LedgerBookId, seed.Journal.PeriodId, seed.ExpectedPeriodVersion) with
        { ReferenceVersions = authority.Versions }).WithComputedFingerprint();
        if (!disposal)
            return (command, authority);
        var lot = (await database.JournalStore.AppendAssetPostingAsync(command)).MutatedLots.Single();
        await database.JournalStore.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), command.LedgerBookId, lot.Account,
            LedgerTaxLotReliefMethod.Fifo, "reference-fifo", lot.AcquiredDate, lot.CreatedAt, lot.CreatedAt));
        var eventId = Guid.NewGuid();
        var journal = BuildJournalWrite(command.LedgerBookId, command.Journal.PeriodId, eventId,
            "reference-disposal", "Cash", "Investment lots", 4_000m);
        var evidence = BuildEvidence("reference-disposal", 'd');
        var sale = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), command.LedgerBookId, journal, eventId,
            "reference-disposal", command.ExpectedPeriodVersion, AtomicTaxLotMutationKind.Disposal, [evidence],
            disposalSelections: [new(lot.TaxLotRecordId, lot.LotId, lot.Version, 100m, 40m, 0, evidence.EvidenceId, 100m, 4_000m)],
            reliefMethod: "Fifo", policyRevision: "reference-fifo", referenceVersions: authority.Versions);
        return (sale, authority);
    }
}
