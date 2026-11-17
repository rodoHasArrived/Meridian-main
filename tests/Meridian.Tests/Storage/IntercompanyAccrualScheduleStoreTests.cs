using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;

namespace Meridian.Tests.Storage;

/// <summary>Maker-checker schedule rules plus the same append-only contract over the file store and PostgreSQL (ledger migration 075).</summary>
public sealed class IntercompanyAccrualScheduleRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AProposedScheduleIsActiveOnlyOnceSomeoneElseApprovesIt_FromTheApprovalDate()
    {
        var proposed = IntercompanyAccrualScheduleRules.Propose("tenant-a", "company-a", "maker", Propose(), At);
        Assert.Equal(IntercompanyAccrualScheduleStatusDto.Proposed, proposed.Status);

        var self = Assert.Throws<InvalidOperationException>(() => IntercompanyAccrualScheduleRules.Decide(proposed, "MAKER", Decide(IntercompanyAccrualScheduleActionDto.Approve, 1), At));
        Assert.Contains("someone other than", self.Message);
        var active = IntercompanyAccrualScheduleRules.Decide(proposed, "checker", Decide(IntercompanyAccrualScheduleActionDto.Approve, 1), At.AddDays(1));
        Assert.Equal((IntercompanyAccrualScheduleStatusDto.Active, new DateOnly(2026, 7, 3), 2L), (active.Status, active.ActiveFrom!.Value, active.Version));

        var disabled = IntercompanyAccrualScheduleRules.Decide(active, "checker", Decide(IntercompanyAccrualScheduleActionDto.Disable, 2, "Loan repaid."), At.AddDays(9));
        Assert.Equal(IntercompanyAccrualScheduleStatusDto.Disabled, disabled.Status);
        Assert.Equal("Loan repaid.", disabled.Events[^1].Reason);
    }

    [Fact]
    public void InvalidOrStaleDecisionsAreRefused()
    {
        var proposed = IntercompanyAccrualScheduleRules.Propose("tenant-a", null, "maker", Propose(), At);

        Assert.Throws<InvalidOperationException>(() => IntercompanyAccrualScheduleRules.Decide(proposed, "checker", Decide(IntercompanyAccrualScheduleActionDto.Approve, 5), At));
        Assert.Throws<ArgumentException>(() => IntercompanyAccrualScheduleRules.Decide(proposed, "checker", Decide(IntercompanyAccrualScheduleActionDto.Reject, 1, " "), At));
        Assert.Throws<InvalidOperationException>(() => IntercompanyAccrualScheduleRules.Decide(proposed, "checker", Decide(IntercompanyAccrualScheduleActionDto.Disable, 1, "x"), At));
        Assert.Throws<ArgumentException>(() => IntercompanyAccrualScheduleRules.Propose("tenant-a", null, "maker", Propose() with { PrincipalArrangementId = null }, At));
        Assert.Throws<ArgumentException>(() => IntercompanyAccrualScheduleRules.Propose("tenant-a", null, "maker", Propose() with { Reason = "" }, At));
    }

    internal static ProposeIntercompanyAccrualScheduleRequest Propose()
        => new(Guid.NewGuid(), Guid.Parse("b1111111-1111-1111-1111-111111111111"), IntercompanyAccrualScheduleKindDto.LoanInterest,
            Guid.Parse("b2222222-2222-2222-2222-222222222222"), Guid.Parse("b3333333-3333-3333-3333-333333333333"), "Monthly loan interest.");

    internal static DecideIntercompanyAccrualScheduleRequest Decide(IntercompanyAccrualScheduleActionDto action, long version, string? reason = null)
        => new(Guid.NewGuid(), action, version, reason);
}

public abstract class IntercompanyAccrualScheduleStoreContract
{
    protected abstract IIntercompanyAccrualScheduleStore Open(string? tenant);

    protected async Task AppendsListsAndRefusesStaleOrForeignWritesCore()
    {
        var store = Open("tenant-a");
        var proposed = IntercompanyAccrualScheduleRules.Propose("tenant-a", "company-a", "maker", IntercompanyAccrualScheduleRulesTests.Propose(), DateTimeOffset.UnixEpoch);
        await store.AppendAsync(proposed, 0);
        var active = IntercompanyAccrualScheduleRules.Decide(proposed, "checker",
            IntercompanyAccrualScheduleRulesTests.Decide(IntercompanyAccrualScheduleActionDto.Approve, 1), DateTimeOffset.UnixEpoch);
        await store.AppendAsync(active, 1);

        var reader = Open("tenant-a");
        Assert.Equal(2, (await reader.GetAsync("tenant-a", proposed.ScheduleId))!.Version);
        Assert.Single(await reader.ListAsync("tenant-a", proposed.LedgerBookId));
        Assert.Empty(await reader.ListAsync("tenant-a", Guid.NewGuid()));
        Assert.Contains(await Open(null).ListActiveAsync(), item => item.ScheduleId == proposed.ScheduleId);
        Assert.Equal(IntercompanyAccrualScheduleStoreRules.Serialize(active), IntercompanyAccrualScheduleStoreRules.Serialize(await reader.AppendAsync(active, 1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.AppendAsync(active with { Version = 3 }, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.AppendAsync(active with { Version = 3, LedgerBookId = Guid.NewGuid() }, 2));
        Assert.Null(await Open("tenant-b").GetAsync("tenant-b", proposed.ScheduleId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Open("tenant-b").GetAsync("tenant-a", proposed.ScheduleId));
    }

    protected sealed class Tenant(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }
}

public sealed class FileIntercompanyAccrualScheduleStoreTests : IntercompanyAccrualScheduleStoreContract, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "meridian-schedules-" + Guid.NewGuid().ToString("N"), "schedules.json");

    protected override IIntercompanyAccrualScheduleStore Open(string? tenant) => new FileIntercompanyAccrualScheduleStore(_path, new Tenant(tenant));

    [Fact] public Task AppendsListsAndRefusesStaleOrForeignWrites() => AppendsListsAndRefusesStaleOrForeignWritesCore();

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

[Trait("Category", "Integration")]
public sealed class PostgresIntercompanyAccrualScheduleStoreTests : IntercompanyAccrualScheduleStoreContract, IAsyncLifetime
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

    protected override IIntercompanyAccrualScheduleStore Open(string? tenant) => new PostgresIntercompanyAccrualScheduleStore(_database!.Options, new Tenant(tenant));

    [LedgerDatabaseFact] public Task AppendsListsAndRefusesStaleOrForeignWrites() => AppendsListsAndRefusesStaleOrForeignWritesCore();

    [LedgerDatabaseFact]
    public async Task RetainedVersionsCannotBeRewrittenInTheDatabase()
    {
        await AppendsListsAndRefusesStaleOrForeignWritesCore();
        await using var connection = new Npgsql.NpgsqlConnection(_database!.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] { "update {0}.intercompany_accrual_schedules set status='x'", "delete from {0}.intercompany_accrual_schedules",
            "truncate {0}.intercompany_accrual_schedules" })
        {
            await using var command = new Npgsql.NpgsqlCommand(string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, $"\"{_database.Options.SchemaName}\""), connection);
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => command.ExecuteNonQueryAsync());
        }
    }
}
