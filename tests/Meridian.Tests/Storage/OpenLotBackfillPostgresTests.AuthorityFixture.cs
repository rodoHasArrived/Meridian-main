using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.Storage;

public sealed partial class OpenLotBackfillPostgresTests
{
    private sealed record BackfillAuthority(
        LedgerPostgresTestDatabase Database,
        SecurityMasterOptions SecurityOptions,
        AssetOperationsOptions PositionOptions,
        PostgresSecurityMasterStore Securities,
        PostgresAssetOperationsProjectionStore Positions,
        SecurityProjectionRecord Security,
        InstrumentRoleDto Role,
        BookPositionDto Position)
    {
        public PostgresLedgerJournalStore Restart() => new(Database.Options,
            backfillSecurityMaster: () => Securities, backfillPositions: () => Positions);

        public static async Task<BackfillAuthority> CreateAsync(LedgerPostgresTestDatabase database,
            OpenLotBackfillFactsDto facts, string owner = "fund-alpha", string side = BookPositionSides.Long)
        {
            var securityOptions = new SecurityMasterOptions
            {
                ConnectionString = database.Options.ConnectionString,
                Schema = database.CreateSchemaName("backfill_security"),
                PreloadProjectionCache = false
            };
            var positionOptions = new AssetOperationsOptions
            {
                ConnectionString = database.Options.ConnectionString,
                Schema = database.CreateSchemaName("backfill_positions")
            };
            await new SecurityMasterMigrationRunner(securityOptions).EnsureMigratedAsync();
            await new AssetOperationsMigrationRunner(positionOptions).EnsureMigratedAsync();
            var securities = new PostgresSecurityMasterStore(securityOptions);
            var positions = new PostgresAssetOperationsProjectionStore(positionOptions);
            var json = JsonSerializer.SerializeToElement(new { });
            var security = new SecurityProjectionRecord(facts.SecurityId, "Equity", SecurityStatusDto.Active,
                "Issuer", "EUR", "ISIN", "TEST", json, json, json, facts.SecurityMasterVersion,
                DateTimeOffset.Parse("2020-01-01T00:00:00Z"), null, [], []);
            await securities.UpsertProjectionAsync(security);
            var at = new DateTimeOffset(facts.AcquiredDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var eventId = Guid.NewGuid();
            var evidence = new RetainedEvidenceIdentityDto("backfill-position", "evidence://backfill/position",
                new string('a', 64), "custodian", "acquisition-position", "Accepted", "reviewer",
                at.AddMinutes(1), facts.AcquiredDate, 1, at.AddMinutes(2), "preparer",
                AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"));
            var origin = new EconomicEventReferenceDto(eventId, "Trade", 1, facts.AcquiredDate,
                at, "custodian", "acquisition-position")
            {
                SecurityId = facts.SecurityId,
                BookPositionId = facts.BookPositionId,
                RetainedEvidence = [evidence],
                EvidenceLinks = [evidence.EvidenceUri]
            };
            var role = new InstrumentRoleDto(Guid.NewGuid(), facts.SecurityId, owner, "Fund",
                InstrumentRoleKinds.Holder, InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset,
                facts.AcquiredDate, OriginEvent: origin, EvidenceLinks: [evidence.EvidenceUri])
            { RetainedEvidence = [evidence] };
            var position = new BookPositionDto(facts.BookPositionId, facts.SecurityId, role.RoleId,
                new AccountingBookContextDto(facts.LedgerBookId, owner, BookOwnerNode,
                    FundStructureNodeKindDto.Fund, "Book", "USD", AccountingBasisKindDto.Primary, "policy", "1"),
                side, "Active", facts.AcquiredDate, OriginEvent: origin, EvidenceLinks: [evidence.EvidenceUri])
            { RetainedEvidence = [evidence] };
            for (var version = 1L; version <= facts.BookPositionVersion; version++)
            {
                position = await positions.UpsertAsync(role, position with { Version = version }, null, version - 1,
                    new("reviewer", "evidence://backfill/position-approval", "Reviewed acquisition position", at.AddMinutes(3)));
            }
            return new(database, securityOptions, positionOptions, securities, positions, security, role, position);
        }
    }
}
