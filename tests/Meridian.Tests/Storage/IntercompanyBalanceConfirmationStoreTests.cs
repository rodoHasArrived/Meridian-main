using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

/// <summary>
/// The append-only confirmation history behaves the same in the file store and the PostgreSQL store: one
/// confirmation per tenant, company, profile and date; versions follow one another; a stale or competing write is
/// refused; an exact replay is idempotent; and another company or tenant sees nothing.
/// </summary>
public abstract class IntercompanyBalanceConfirmationStoreContract
{
    protected const string Tenant = "tenant-a";
    protected const string Company = "company-a";
    private static readonly Guid Arrangement = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly DateOnly MonthEnd = new(2026, 6, 30);

    protected abstract Task<IIntercompanyBalanceConfirmationStore> OpenAsync(string? tenant = Tenant);

    protected async Task AppendsVersionsAndReadsTheLatestInScopeCore()
    {
        var store = await OpenAsync();
        var proposed = Proposal();
        await store.AppendAsync(proposed, 0);
        var confirmed = Confirm(proposed);
        await store.AppendAsync(confirmed, 1);

        Assert.Equal(confirmed.Version, (await store.GetAsync(proposed.ConfirmationId, Company))!.Version);
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne, (await store.FindAsync(Arrangement, MonthEnd, Company))!.Status);
        Assert.Null(await store.FindAsync(Arrangement, MonthEnd.AddDays(-1), Company));
        // Another company of the same tenant, and another tenant, see nothing.
        Assert.Null(await store.GetAsync(proposed.ConfirmationId, "company-b"));
        Assert.Null(await (await OpenAsync("tenant-b")).FindAsync(Arrangement, MonthEnd, Company));
    }

    protected async Task AStaleOrCompetingWriteIsRefusedAndAnExactReplayIsIdempotentCore()
    {
        var store = await OpenAsync();
        var proposed = Proposal();
        await store.AppendAsync(proposed, 0);
        Assert.Equal(proposed.BalanceDigest, (await store.AppendAsync(proposed, 0)).BalanceDigest);

        // A second proposal identity for the same profile and date cannot start a parallel history.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(Proposal(), 0));
        var confirmed = Confirm(proposed);
        await store.AppendAsync(confirmed, 1);
        // A competing step for the same version, built from the earlier read, loses.
        var competing = IntercompanyBalanceConfirmationRules.Apply(proposed, new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Dispute,
            IntercompanyBalanceConfirmationPartyDto.PartyTwo, 1, proposed.BalanceDigest, "Disagree"), "borrower-controller", proposed.Events[0].RecordedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(competing, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(confirmed with { Version = 4 }, 3));
        Assert.Equal(2, (await store.FindAsync(Arrangement, MonthEnd, Company))!.Version);
    }

    protected async Task AWriteForAnotherTenantIsRefusedCore()
    {
        var store = await OpenAsync();
        await Assert.ThrowsAsync<TenantScopeRejectedException>(() => store.AppendAsync(Proposal() with { TenantId = "tenant-b" }, 0));
    }

    private static IntercompanyBalanceConfirmationDto Proposal()
    {
        var one = new IntercompanyBalanceConfirmationSideDto(Guid.NewGuid(), Guid.NewGuid(), "USD", 100m, "USD", 100m, "one", []);
        var two = new IntercompanyBalanceConfirmationSideDto(Guid.NewGuid(), Guid.NewGuid(), "USD", -100m, "USD", -100m, "two", []);
        return IntercompanyBalanceConfirmationRules.Propose(null, Guid.NewGuid(), Guid.NewGuid(), Tenant, Company, Arrangement, Guid.NewGuid(),
            MonthEnd, one, two, "accounting-operations", new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero));
    }

    private static IntercompanyBalanceConfirmationDto Confirm(IntercompanyBalanceConfirmationDto current)
        => IntercompanyBalanceConfirmationRules.Apply(current, new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Confirm,
            IntercompanyBalanceConfirmationPartyDto.PartyOne, current.Version, current.BalanceDigest, null), "lender-controller",
            current.Events[0].RecordedAt.AddMinutes(5));

    protected sealed class TenantAccessor(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }
}

public sealed class FileIntercompanyBalanceConfirmationStoreTests : IntercompanyBalanceConfirmationStoreContract, IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-confirmations-" + Guid.NewGuid().ToString("N"));

    protected override Task<IIntercompanyBalanceConfirmationStore> OpenAsync(string? tenant = Tenant)
        => Task.FromResult<IIntercompanyBalanceConfirmationStore>(new FileIntercompanyBalanceConfirmationStore(
            Path.Combine(_directory, "confirmations.json"), new TenantAccessor(tenant)));

    [Fact] public Task AppendsVersionsAndReadsTheLatestInScope() => AppendsVersionsAndReadsTheLatestInScopeCore();
    [Fact] public Task AStaleOrCompetingWriteIsRefusedAndAnExactReplayIsIdempotent() => AStaleOrCompetingWriteIsRefusedAndAnExactReplayIsIdempotentCore();
    [Fact] public Task AWriteForAnotherTenantIsRefused() => AWriteForAnotherTenantIsRefusedCore();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}

[Trait("Category", "Integration")]
public sealed class PostgresIntercompanyBalanceConfirmationStoreTests : IntercompanyBalanceConfirmationStoreContract, IAsyncLifetime
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

    protected override Task<IIntercompanyBalanceConfirmationStore> OpenAsync(string? tenant = Tenant)
        => Task.FromResult<IIntercompanyBalanceConfirmationStore>(new PostgresIntercompanyBalanceConfirmationStore(_database!.Options, new TenantAccessor(tenant)));

    [LedgerDatabaseFact] public Task AppendsVersionsAndReadsTheLatestInScope() => AppendsVersionsAndReadsTheLatestInScopeCore();
    [LedgerDatabaseFact] public Task AStaleOrCompetingWriteIsRefusedAndAnExactReplayIsIdempotent() => AStaleOrCompetingWriteIsRefusedAndAnExactReplayIsIdempotentCore();
    [LedgerDatabaseFact] public Task AWriteForAnotherTenantIsRefused() => AWriteForAnotherTenantIsRefusedCore();

    [LedgerDatabaseFact]
    public async Task RetainedVersionsCannotBeRewrittenInTheDatabase()
    {
        var store = await OpenAsync();
        await AppendsVersionsAndReadsTheLatestInScopeCore();
        await using var connection = new Npgsql.NpgsqlConnection(_database!.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            $"update \"{_database.Options.SchemaName}\".intercompany_balance_confirmations set payload='{{}}'::jsonb", connection);
        var refusal = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("append-only", refusal.MessageText);
        Assert.NotNull(store);
    }
}
