using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.Storage;

internal sealed partial class LedgerPostgresTestDatabase
{
    private PostgresSecurityMasterStore? _lotSecurities;
    private PostgresAssetOperationsProjectionStore? _lotPositions;
    private SecurityMasterOptions? _lotSecurityOptions;
    private AssetOperationsOptions? _lotPositionOptions;

    public PostgresLedgerJournalStore RestartJournalStore() => new(Options,
        backfillSecurityMaster: () => _lotSecurities, backfillPositions: () => _lotPositions);

    // Explicit fixture setup, never called by a posting wrapper. Commands must state the versions
    // they reviewed, and later fixture drift is not silently repaired before exercising the store.
    public async Task<LotReferenceAuthority> SeedLotReferenceAuthorityAsync(
        Guid bookId, Guid securityId, Guid positionId, string currency = "USD", string positionSide = BookPositionSides.Long)
    {
        if (_lotSecurities is null)
        {
            _lotSecurityOptions = new SecurityMasterOptions
            { ConnectionString = Options.ConnectionString, Schema = CreateSchemaName("lot_security"), PreloadProjectionCache = false };
            _lotPositionOptions = new AssetOperationsOptions
            { ConnectionString = Options.ConnectionString, Schema = CreateSchemaName("lot_positions") };
            await new SecurityMasterMigrationRunner(_lotSecurityOptions).EnsureMigratedAsync();
            await new AssetOperationsMigrationRunner(_lotPositionOptions).EnsureMigratedAsync();
            _lotSecurities = new(_lotSecurityOptions);
            _lotPositions = new(_lotPositionOptions);
        }

        var book = await JournalStore.GetLedgerBookAsync(bookId) ?? throw new InvalidOperationException("Seed the book first.");
        var json = JsonSerializer.SerializeToElement(new { });
        var at = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        var date = new DateOnly(2020, 1, 1);
        var security = new SecurityProjectionRecord(securityId, "Equity", SecurityStatusDto.Active,
            "Reference fixture", currency, "ISIN", securityId.ToString("N"), json, json, json, 1, at, null, [], []);
        await _lotSecurities.UpsertProjectionAsync(security);
        var eventId = Guid.NewGuid();
        var evidence = new RetainedEvidenceIdentityDto("lot-position", "evidence://lot/position", new string('a', 64),
            "custodian", "lot-position-origin", "Accepted", "reviewer", at.AddMinutes(1), date, 1,
            at.AddMinutes(2), "preparer", AssetAccountingEvidenceSubjects.Event, eventId.ToString("D"));
        var origin = new EconomicEventReferenceDto(eventId, "Trade", 1, date, at, "custodian", "lot-position-origin")
        { SecurityId = securityId, BookPositionId = positionId, RetainedEvidence = [evidence], EvidenceLinks = [evidence.EvidenceUri] };
        var role = new InstrumentRoleDto(Guid.NewGuid(), securityId, book.FundProfileId, "Fund", InstrumentRoleKinds.Holder,
            InstrumentAccountingSides.Debit, InstrumentEconomicSides.Asset, date, OriginEvent: origin,
            EvidenceLinks: [evidence.EvidenceUri])
        { RetainedEvidence = [evidence] };
        var context = new AccountingBookContextDto(bookId, book.FundProfileId, book.FundStructureNodeId,
            book.FundStructureNodeKind, book.DisplayName, book.BaseCurrency, book.AccountingBasis,
            book.AccountingPolicyId, book.AccountingPolicyVersion);
        var position = await _lotPositions!.UpsertAsync(role,
            new BookPositionDto(positionId, securityId, role.RoleId, context, positionSide, "Active", date,
                OriginEvent: origin, EvidenceLinks: [evidence.EvidenceUri])
            { RetainedEvidence = [evidence] }, null, 0,
            new("reviewer", "evidence://lot/position-approval", "Reviewed fixture position", at.AddMinutes(3)));
        return new(_lotSecurityOptions!, _lotPositionOptions!, _lotSecurities, _lotPositions, security, role, position);
    }
}

internal sealed record LotReferenceAuthority(
    SecurityMasterOptions SecurityOptions,
    AssetOperationsOptions PositionOptions,
    PostgresSecurityMasterStore Securities,
    PostgresAssetOperationsProjectionStore Positions,
    SecurityProjectionRecord Security,
    InstrumentRoleDto Role,
    BookPositionDto Position)
{
    public AtomicLotReferenceVersions Versions => new(Security.Version, Position.Version);
}
