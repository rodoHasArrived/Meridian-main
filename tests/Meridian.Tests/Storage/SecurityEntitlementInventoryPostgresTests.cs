using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

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
        var (store, book, journal) = await SeedBookAsync(database, ct);
        var securityId = Guid.NewGuid();
        await store.SaveTaxLotAsync(Lot(book.LedgerBookId, securityId, "account-a", journal.Entry.JournalEntryId), ct);
        await store.SaveTaxLotAsync(Lot(book.LedgerBookId, securityId, "account-b", journal.Entry.JournalEntryId), ct);
        await store.SaveTaxLotAsync(Lot(book.LedgerBookId, Guid.NewGuid(), "account-c", journal.Entry.JournalEntryId), ct);

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
        var (store, book, journal) = await SeedBookAsync(database, ct);
        var securityId = Guid.NewGuid();
        await store.SaveTaxLotAsync(Lot(book.LedgerBookId, securityId, "account-a", journal.Entry.JournalEntryId), ct);
        await store.SaveTaxLotAsync(Lot(book.LedgerBookId, Guid.Empty, "legacy-account", journal.Entry.JournalEntryId), ct);

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
        var (store, book, journal) = await SeedBookAsync(database, ct);
        var securityId = Guid.NewGuid();
        await store.SaveTaxLotAsync(Lot(book.LedgerBookId, securityId, "account-a", journal.Entry.JournalEntryId)
            with { OpenQuantity = 0m }, ct);

        await Assert.ThrowsAsync<LedgerValidationException>(() => store.ReadSecurityEntitlementInventoryAsync(
            "tenant-alpha", "company-alpha", book.LedgerBookId, securityId, new DateOnly(2026, 5, 14), ct));
    }

    private static async Task<(PostgresLedgerJournalStore Store, LedgerBookRecord Book, LedgerJournalEntryWrite Journal)>
        SeedBookAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync("fund-alpha", "tenant-alpha", "company-alpha", ct: ct);
        var store = new PostgresLedgerJournalStore(database.Options, new WorkerAccessor(), TenantScopeEnforcementOptions.FailClosed);
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var book = new LedgerBookRecord(Guid.NewGuid(), "fund-alpha", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Entitlement fixture", "USD", now, now);
        await store.SaveLedgerBookAsync(book, ct);
        var period = await store.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "2026-05",
            new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 0), 0, ct: ct);
        var journalId = Guid.NewGuid();
        var journal = new LedgerJournalEntryWrite(new JournalEntry(journalId, now, "Acquisition inventory source",
        [new(Guid.NewGuid(), journalId, now, new("Securities", LedgerAccountType.Asset), 100m, 0m, "Acquisition inventory source"),
         new(Guid.NewGuid(), journalId, now, new("Cash", LedgerAccountType.Asset), 0m, 100m, "Acquisition inventory source")]),
            book.LedgerBookId, period.PeriodId, LedgerBookId: book.LedgerBookId);
        await store.AppendAsync(journal, ct);
        return (store, book, journal);
    }

    private static LedgerTaxLotRecord Lot(Guid bookId, Guid securityId, string account, Guid journalId)
    {
        var date = new DateOnly(2026, 5, 1);
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var evidence = new RetainedEvidenceIdentityDto("lot-source", "provider://acquisition/v1", new string('a', 64),
            "provider", "source", RetainedEvidenceIdentityValidator.AcceptedReviewStatus, "accountant", now, date, 1,
            now, "accountant", AssetAccountingEvidenceSubjects.Event, "source");
        return new(Guid.NewGuid(), bookId, new("Securities", LedgerAccountType.Asset, "FIX", account), account + "-lot",
            date, 10m, 10m, 10m, "USD", now, now, journalId, SecurityId: securityId, BookPositionId: Guid.NewGuid(),
            Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m, 100m, 100m, date, null, [evidence]));
    }

    private sealed class WorkerAccessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => FundScopeTenantAuthority.CurrentTenantId;
    }
}
