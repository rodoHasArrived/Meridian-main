using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class SecurityEntitlementInventoryPostgresTests
{
    [LedgerDatabaseFact]
    public async Task CompleteEntitlementInventory_InspectsWholeBookAndIncludesEveryRelatedAccount()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "complete book entitlement regression");
        var (store, book, period) = await SeedBookAsync(database, ct);
        var securityId = Guid.NewGuid();
        await SaveAcquiredLotAsync(store, book, period, securityId, "account-a", ct);
        await SaveAcquiredLotAsync(store, book, period, securityId, "account-b", ct);
        await SaveAcquiredLotAsync(store, book, period, Guid.NewGuid(), "account-c", ct);

        var snapshot = await store.ReadSecurityEntitlementInventoryAsync("tenant-alpha", "company-alpha",
            book.LedgerBookId, securityId, new DateOnly(2026, 5, 14), ct);

        snapshot.InspectedBookLotCount.Should().Be(3);
        snapshot.HeldLots.Should().HaveCount(2);
        snapshot.HeldLots.Select(lot => lot.Account.FinancialAccountId).Should().BeEquivalentTo("account-a", "account-b");
        snapshot.SnapshotHash.Should().Be(Sha256Digest.ComputeUtf8(snapshot.SnapshotJson));
        using var proof = JsonDocument.Parse(snapshot.SnapshotJson);
        proof.RootElement.GetProperty("sourceBookLots").GetArrayLength().Should().Be(3);
        proof.RootElement.GetProperty("complete").GetBoolean().Should().BeTrue();
        (await store.ReadSecurityEntitlementInventoryAsync("tenant-alpha", "company-alpha", book.LedgerBookId,
            securityId, new DateOnly(2026, 5, 14), ct)).SnapshotHash.Should().Be(snapshot.SnapshotHash);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadSecurityEntitlementInventoryAsync(
            "tenant-alpha", "foreign-company", book.LedgerBookId, securityId, new DateOnly(2026, 5, 14), ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReadSecurityEntitlementInventoryAsync(
            "foreign-tenant", "company-alpha", book.LedgerBookId, securityId, new DateOnly(2026, 5, 14), ct));
    }

    [LedgerDatabaseFact]
    public async Task CompleteEntitlementInventory_UnknownLegacyIdentityCannotDisappearBehindSecurityFilter()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "unknown entitlement inventory regression");
        var (store, book, period) = await SeedBookAsync(database, ct);
        var securityId = Guid.NewGuid();
        await SaveAcquiredLotAsync(store, book, period, securityId, "account-a", ct);
        // The unidentified imported legacy row deliberately has no typed acquisition journal;
        // filtering it away by security would falsely certify the book population as complete.
        await store.SaveTaxLotAsync(Lot(book.LedgerBookId, Guid.Empty, "legacy-account", Guid.NewGuid())
            with
        { SourceJournalEntryId = null, BookPositionId = Guid.Empty, Acquisition = null }, ct);

        var error = await Assert.ThrowsAsync<LedgerValidationException>(() => store.ReadSecurityEntitlementInventoryAsync(
            "tenant-alpha", "company-alpha", book.LedgerBookId, securityId, new DateOnly(2026, 5, 14), ct));
        error.Message.Should().Contain("unidentified legacy lots");
    }

    [LedgerDatabaseFact]
    public async Task CompleteEntitlementInventory_MissingHistoricalQuantityChainFailsClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        using var authority = FundScopeTenantAuthority.Enter("tenant-alpha", "incomplete quantity history regression");
        var (store, book, period) = await SeedBookAsync(database, ct);
        var securityId = Guid.NewGuid();
        await SaveAcquiredLotAsync(store, book, period, securityId, "account-a", ct, openQuantity: 0m);

        await Assert.ThrowsAsync<LedgerValidationException>(() => store.ReadSecurityEntitlementInventoryAsync(
            "tenant-alpha", "company-alpha", book.LedgerBookId, securityId, new DateOnly(2026, 5, 14), ct));
    }

    private static async Task<(PostgresLedgerJournalStore Store, LedgerBookRecord Book, LedgerAccountingPeriod Period)>
        SeedBookAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync("fund-alpha", "tenant-alpha", "company-alpha", ct: ct);
        var store = new PostgresLedgerJournalStore(database.Options, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var book = new LedgerBookRecord(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Entitlement fixture", "USD", now, now);
        await store.SaveLedgerBookAsync(book, ct);
        // These inventory regressions inspect retained acquisitions, including unidentified
        // imported legacy lots. Seed their explicit pre-controls compatibility profile;
        // ordinary newly created books still default to Enforced in the production store.
        await using (var connection = new NpgsqlConnection(database.Options.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                update "{database.Options.SchemaName}".ledger_book_controls
                set mode='LegacyWarning'
                where ledger_book_id=@book and mode='Enforced' and version=1
                    and activated_at_utc is null and migration_receipt_id is null;
                """;
            command.Parameters.AddWithValue("book", book.LedgerBookId);
            (await command.ExecuteNonQueryAsync(ct)).Should().Be(1);
        }
        (await store.GetBookControlsAsync(book.LedgerBookId, ct))!.Mode.Should().Be(LedgerBookControlModeDto.LegacyWarning);
        var period = await store.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "2026-05",
            new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 0), 0, ct: ct);
        return (store, book, period);
    }

    private static async Task SaveAcquiredLotAsync(PostgresLedgerJournalStore store, LedgerBookRecord book,
        LedgerAccountingPeriod period, Guid securityId, string account, CancellationToken ct, decimal openQuantity = 10m)
    {
        var journalId = Guid.NewGuid();
        var lot = Lot(book.LedgerBookId, securityId, account, journalId);
        var now = lot.CreatedAt;
        const string description = "Synthetic approved Security Master acquisition inventory source";
        var provenance = $"security-master:{securityId:N};snapshot:fixture-acquisition;approved:true";
        var metadata = new JournalEntryMetadata(ActivityType: "Acquisition", Symbol: "FIX", SecurityId: securityId,
            FinancialAccountId: account, EffectiveDate: lot.AcquiredDate,
            Tags: new Dictionary<string, string>
            {
                ["securityMasterProvenance"] = provenance,
                ["securityMasterLineage"] = $"FIX:{securityId:N}:ledger-map:fix-securities:sm-approval:fixture-review:security-status:active:{provenance}"
            }, EvidenceReferences: lot.Acquisition!.Evidence.Select(evidence => new JournalEvidenceReference(
                evidence.EvidenceId, evidence.EvidenceUri, "Acquisition", evidence.SourceSystem, evidence.RetainedAtUtc,
                evidence.RetainedBy, evidence.SubjectId, evidence.ContentHashSha256, SourceReference: evidence.SourceReference,
                ReviewStatus: evidence.ReviewStatus, ReviewedBy: evidence.ReviewedBy, ReviewedAtUtc: evidence.ReviewedAtUtc,
                EffectiveDate: evidence.EffectiveDate, EvidenceVersion: evidence.EvidenceVersion, SubjectType: evidence.SubjectType)).ToArray());
        var journal = new LedgerJournalEntryWrite(new JournalEntry(journalId, now, description,
        [new(Guid.NewGuid(), journalId, now, lot.Account, 100m, 0m, description),
         new(Guid.NewGuid(), journalId, now, new("Cash", LedgerAccountType.Asset, FinancialAccountId: account), 0m, 100m, description)], metadata),
            book.LedgerBookId, period.PeriodId, SourceEventId: Guid.NewGuid(), LedgerBookId: book.LedgerBookId);
        await store.AppendAsync(journal, ct);
        await store.SaveTaxLotAsync(lot with { OpenQuantity = openQuantity }, ct);
    }

    private static LedgerTaxLotRecord Lot(Guid bookId, Guid securityId, string account, Guid journalId)
    {
        var date = new DateOnly(2026, 5, 1);
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var lotId = Guid.NewGuid();
        var evidence = new RetainedEvidenceIdentityDto("lot-source", "provider://acquisition/v1", new string('a', 64),
            "provider", "source", RetainedEvidenceIdentityValidator.AcceptedReviewStatus, "accountant", now, date, 1,
            now, "accountant", "OpenLotAcquisition", lotId.ToString("D"));
        return new(lotId, bookId, new("Securities", LedgerAccountType.Asset, "FIX", account), account + "-lot",
            date, 10m, 10m, 10m, "USD", now, now, journalId, SecurityId: securityId, BookPositionId: Guid.NewGuid(),
            Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m, 100m, 100m, date, null, [evidence]));
    }

    private sealed class WorkerAccessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => FundScopeTenantAuthority.CurrentTenantId;
    }
}
