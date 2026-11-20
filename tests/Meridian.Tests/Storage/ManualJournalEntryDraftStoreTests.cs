using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// One contract for the file-backed and PostgreSQL (ledger migration 077) manual journal draft stores:
/// identical normalization, scope keys, filters and ordering, and exact round-trip of server-held fields.
/// </summary>
public abstract class ManualJournalEntryDraftStoreContract
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    protected static readonly DateTimeOffset At = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    protected abstract IManualJournalEntryDraftStore Open();

    internal static ManualJournalEntryDraftDto Draft(
        Guid? id = null, string fund = "fund-a", Guid? book = null, DateTimeOffset? updated = null,
        string? tenant = null, string? company = null, int version = 1)
        => new(id ?? Guid.NewGuid(), ManualJournalEntryStatusDto.Draft, fund, book, AccountingBasisKindDto.Primary,
            new DateOnly(2026, 9, 30), "2026-09", null, null, "USD", "Memo", "preparer", At, updated ?? At, version,
            [], [], [], TenantId: tenant, CompanyId: company);

    protected async Task NormalizesAndRoundTripsServerHeldFieldsCore()
    {
        var id = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var draft = Draft(id, fund: "  Fund-A ", book: Guid.NewGuid(), tenant: " Tenant-A ", company: "  ") with
        {
            IntercompanyProposalId = proposalId,
            IntercompanyProposalSourceDigest = new string('a', 64),
            PrivateCapitalAuthorityJson = "{\"call\":\"c-1\"}",
            TreasuryContext = new TreasuryLedgerContextDto(new DateOnly(2026, 9, 30), "idem-1", null, "IntercompanyMirror", null, null),
            EvidenceLinks = ["evidence://one"]
        };
        await Open().SaveAsync(draft);

        // A fresh instance simulates a restart.
        var reopened = await Open().GetAsync("fund-a", id, tenantId: "TENANT-A");
        Assert.NotNull(reopened);
        var expected = draft with { FundProfileId = "Fund-A", TenantId = "Tenant-A", CompanyId = null };
        Assert.Equal(JsonSerializer.Serialize(expected, Web), JsonSerializer.Serialize(reopened, Web));
        Assert.Equal((proposalId, new string('a', 64)), (reopened!.IntercompanyProposalId, reopened.IntercompanyProposalSourceDigest));
        Assert.Equal(["Fund-A"], await Open().ListFundProfileIdsAsync());
    }

    protected async Task ScopesFiltersAndOrdersLikeTheFileStoreCore()
    {
        var store = Open();
        var book = Guid.NewGuid();
        var shared = Guid.NewGuid();
        var older = Draft(fund: "fund-a", book: book, updated: At.AddHours(-1), tenant: "tenant-a");
        var newer = Draft(fund: "FUND-A", book: Guid.NewGuid(), updated: At.AddHours(1), tenant: "tenant-a", company: "company-a");
        var otherTenant = Draft(shared, fund: "fund-a", book: book, tenant: "tenant-b");
        var anyTenant = Draft(shared, fund: "fund-a", book: book);
        await store.SaveBatchAsync([older, newer, otherTenant, anyTenant, Draft(fund: " ")]);

        var all = await Open().ListAsync("fund-a");
        Assert.Equal(newer.JournalEntryId, all[0].JournalEntryId);
        Assert.Equal(older.JournalEntryId, all[^1].JournalEntryId);
        Assert.Equal(4, all.Count);
        // The same journal id under a different tenant scope is a separate row.
        Assert.Equal(2, all.Count(item => item.JournalEntryId == shared));

        Assert.Equal(3, (await store.ListAsync("fund-a", book)).Count);
        Assert.Equal([newer.JournalEntryId, older.JournalEntryId],
            (await store.ListAsync("fund-a", tenantId: "TENANT-A")).Select(item => item.JournalEntryId));
        Assert.Single(await store.ListAsync("fund-a", companyId: "Company-A"));
        Assert.Equal("tenant-b", (await store.GetAsync("fund-a", shared, tenantId: "tenant-b"))!.TenantId);
        Assert.Null(await store.GetAsync("fund-a", shared, tenantId: "tenant-c"));
        Assert.Null(await store.GetAsync("fund-b", older.JournalEntryId));
        Assert.Single(await store.ListAsync(""));
        Assert.Equal(["default-fund", "fund-a"], (await store.ListFundProfileIdsAsync()).Select(item => item.ToLowerInvariant()));
    }

    protected async Task ReplacesByScopeKeyAndRefusesNullItemsCore()
    {
        var store = Open();
        var draft = Draft(tenant: "tenant-a");
        await store.SaveAsync(draft);
        await store.SaveAsync(draft with { Version = 2, Memo = "Edited", TenantId = "TENANT-A", UpdatedAtUtc = At.AddMinutes(5) });
        await store.SaveBatchAsync([]);

        var retained = Assert.Single(await Open().ListAsync("fund-a"));
        Assert.Equal((2, "Edited", "TENANT-A"), (retained.Version, retained.Memo, retained.TenantId));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveBatchAsync([Draft(), null!]));
        Assert.Single(await Open().ListAsync("fund-a"));
    }
}

public sealed class FileManualJournalEntryDraftStoreTests : ManualJournalEntryDraftStoreContract, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "meridian-drafts-" + Guid.NewGuid().ToString("N"), "drafts.json");

    protected override IManualJournalEntryDraftStore Open() => new FileManualJournalEntryDraftStore(_path);

    [Fact] public Task NormalizesAndRoundTripsServerHeldFields() => NormalizesAndRoundTripsServerHeldFieldsCore();
    [Fact] public Task ScopesFiltersAndOrdersLikeTheFileStore() => ScopesFiltersAndOrdersLikeTheFileStoreCore();
    [Fact] public Task ReplacesByScopeKeyAndRefusesNullItems() => ReplacesByScopeKeyAndRefusesNullItemsCore();

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

[Trait("Category", "Integration")]
public sealed class PostgresManualJournalEntryDraftStoreTests : ManualJournalEntryDraftStoreContract, IAsyncLifetime
{
    private LedgerPostgresTestDatabase? _database;

    public async Task InitializeAsync()
    {
        if (new LedgerDatabaseFactAttribute().Skip is null)
            _database = await LedgerPostgresTestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    protected override IManualJournalEntryDraftStore Open() => new PostgresManualJournalEntryDraftStore(_database!.Options);

    private PostgresManualJournalMutationRecoveryStore Recovery() => new(_database!.Options);

    [LedgerDatabaseFact] public Task NormalizesAndRoundTripsServerHeldFields() => NormalizesAndRoundTripsServerHeldFieldsCore();
    [LedgerDatabaseFact] public Task ScopesFiltersAndOrdersLikeTheFileStore() => ScopesFiltersAndOrdersLikeTheFileStoreCore();
    [LedgerDatabaseFact] public Task ReplacesByScopeKeyAndRefusesNullItems() => ReplacesByScopeKeyAndRefusesNullItemsCore();

    [LedgerDatabaseFact]
    public void TheWorkbenchPairsPostgresDraftsWithPostgresReceipts()
        => Assert.IsType<PostgresManualJournalMutationRecoveryStore>(
            ManualJournalEntryWorkbenchService.DefaultMutationRecoveryFor(Open()));

    [LedgerDatabaseFact]
    public async Task OneMutationLeaseIsHeldAcrossHostsAtATime()
    {
        var first = await Recovery().OpenSessionAsync();
        var second = Recovery().OpenSessionAsync();
        await Task.Delay(300);
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();
        await using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(await acquired.ListPendingAsync(CancellationToken.None));
    }

    [LedgerDatabaseFact]
    public async Task ReceiptsAreRetainedCompletedDiscardedAndProtected()
    {
        var pending = Intent('a');
        var discarded = Intent('b');
        await using (var session = await Recovery().OpenSessionAsync())
        {
            await session.RetainAsync(pending, CancellationToken.None);
            await session.RetainAsync(discarded, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.RetainAsync(pending, CancellationToken.None));
            await session.DiscardUnappliedAsync(discarded, CancellationToken.None);
        }

        await using (var session = await Recovery().OpenSessionAsync())
        {
            Assert.Equal([pending.CommandKey], (await session.ListPendingAsync(CancellationToken.None)).Select(item => item.CommandKey));
            Assert.Null(await session.GetAsync(discarded.CommandKey, CancellationToken.None));
            await session.CompleteAsync(pending, CancellationToken.None);
            await session.CompleteAsync(pending, CancellationToken.None);
            Assert.True((await session.GetAsync(pending.CommandKey, CancellationToken.None))!.Completed);
            Assert.Empty(await session.ListPendingAsync(CancellationToken.None));
            await session.DiscardUnappliedAsync(pending, CancellationToken.None);
            Assert.NotNull(await session.GetAsync(pending.CommandKey, CancellationToken.None));
        }

        await using var connection = new NpgsqlConnection(_database!.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
                 {
                     "update {0}.manual_journal_mutation_receipts set payload = '{{}}'",
                     "delete from {0}.manual_journal_mutation_receipts"
                 })
        {
            await using var command = new NpgsqlCommand(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, $"\"{_database.Options.SchemaName}\""), connection);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("55000", refused.SqlState);
        }
    }

    [LedgerDatabaseFact]
    public async Task ATamperedReceiptFailsClosed()
    {
        var intent = Intent('c');
        await using (var session = await Recovery().OpenSessionAsync())
            await session.RetainAsync(intent, CancellationToken.None);

        await using (var connection = new NpgsqlConnection(_database!.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var tamper = new NpgsqlCommand(
                $"update \"{_database.Options.SchemaName}\".manual_journal_mutation_receipts set payload = replace(payload, 'request-c', 'request-x')",
                connection);
            Assert.Equal(1, await tamper.ExecuteNonQueryAsync());
        }

        await using var reader = await Recovery().OpenSessionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetAsync(intent.CommandKey, CancellationToken.None));
    }

    private static ManualJournalMutationIntent Intent(char key)
        => new(new string(key, 64), "request-" + key, "scope", Guid.NewGuid(), [], [Draft()], [], null,
            JsonSerializer.SerializeToElement(new { Value = key.ToString() }));
}
