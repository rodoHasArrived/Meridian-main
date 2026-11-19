using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Moq;

namespace Meridian.Tests.Storage;

/// <summary>ADR-032 confirmation close policies: maker-checker rules, the close gate, and the append-only stores.</summary>
public sealed class IntercompanyConfirmationClosePolicyRulesTests
{
    internal static readonly DateTimeOffset At = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    internal static readonly Guid Book = Guid.Parse("c1111111-1111-1111-1111-111111111111");

    [Fact]
    public void APolicyBlocksOnlyOnceSomeoneElseApprovesIt_AndDisablingRestoresAdvisoryOnly()
    {
        var proposed = IntercompanyConfirmationClosePolicyRules.Propose("tenant-a", "company-a", "maker", Propose("Disputed", " balances-changed "), At);
        Assert.Equal(IntercompanyConfirmationClosePolicyStatusDto.Proposed, proposed.Status);
        Assert.Equal(["disputed", "balances-changed"], proposed.BlockingCodes);

        var self = Assert.Throws<InvalidOperationException>(() => IntercompanyConfirmationClosePolicyRules.Decide(proposed, "MAKER",
            Decide(IntercompanyConfirmationClosePolicyActionDto.Approve, 1), At));
        Assert.Contains("someone other than", self.Message);
        var active = IntercompanyConfirmationClosePolicyRules.Decide(proposed, "checker", Decide(IntercompanyConfirmationClosePolicyActionDto.Approve, 1), At);
        Assert.Equal((IntercompanyConfirmationClosePolicyStatusDto.Active, new DateOnly(2026, 11, 18), 2L), (active.Status, active.ActiveFrom!.Value, active.Version));

        var disabled = IntercompanyConfirmationClosePolicyRules.Decide(active, "checker",
            Decide(IntercompanyConfirmationClosePolicyActionDto.Disable, 2, "Pair wound down."), At.AddDays(3));
        Assert.Equal(IntercompanyConfirmationClosePolicyStatusDto.Disabled, disabled.Status);
        Assert.Equal("Pair wound down.", disabled.Events[^1].Reason);
    }

    [Fact]
    public void UnknownCodesStaleVersionsAndMissingReasonsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => IntercompanyConfirmationClosePolicyRules.Propose("t", null, "maker", Propose("unavailable"), At));
        Assert.Throws<ArgumentException>(() => IntercompanyConfirmationClosePolicyRules.Propose("t", null, "maker", Propose(), At));
        Assert.Throws<ArgumentException>(() => IntercompanyConfirmationClosePolicyRules.Propose("t", null, "maker", Propose("disputed") with { Reason = " " }, At));
        var proposed = IntercompanyConfirmationClosePolicyRules.Propose("t", null, "maker", Propose("disputed"), At);
        Assert.Throws<InvalidOperationException>(() => IntercompanyConfirmationClosePolicyRules.Decide(proposed, "checker", Decide(IntercompanyConfirmationClosePolicyActionDto.Approve, 4), At));
        Assert.Throws<ArgumentException>(() => IntercompanyConfirmationClosePolicyRules.Decide(proposed, "checker", Decide(IntercompanyConfirmationClosePolicyActionDto.Reject, 1, ""), At));
        Assert.Throws<InvalidOperationException>(() => IntercompanyConfirmationClosePolicyRules.Decide(proposed, "checker", Decide(IntercompanyConfirmationClosePolicyActionDto.Disable, 1, "x"), At));
    }

    [Fact]
    public void EvaluationBlocksOnlyListedFindings_AndFailsClosedOnUnreadableBalances()
    {
        var policy = Active("disputed");
        var profile = Guid.NewGuid();
        var confirmation = Guid.NewGuid();
        Assert.Equal("Ready", IntercompanyConfirmationClosePolicyRules.Evaluate(policy, [new("AwaitingConfirmation", "Waiting.", profile, confirmation)], new(2026, 10, 31)).Status);

        var blocked = IntercompanyConfirmationClosePolicyRules.Evaluate(policy, [new("Disputed", "Disputed.", profile, confirmation)], new(2026, 10, 31));
        Assert.Equal("Blocked", blocked.Status);
        Assert.Equal([confirmation.ToString("D")], blocked.Records);
        Assert.Contains("intercompany.confirmation.disputed", blocked.Message);

        var unreadable = IntercompanyConfirmationClosePolicyRules.Evaluate(policy,
            [new("Disputed", "Disputed.", profile, confirmation), new("Unavailable", "Cannot read.", Guid.NewGuid(), null)], new(2026, 10, 31));
        Assert.Equal("Unavailable", unreadable.Status);
    }

    internal static ProposeIntercompanyConfirmationClosePolicyRequest Propose(params string[] codes)
        => new(Guid.NewGuid(), Book, codes, "Disputed balances must be resolved before month-end close.");

    internal static DecideIntercompanyConfirmationClosePolicyRequest Decide(IntercompanyConfirmationClosePolicyActionDto action, long version, string? reason = null)
        => new(Guid.NewGuid(), action, version, reason);

    internal static IntercompanyConfirmationClosePolicyDto Active(params string[] codes)
        => IntercompanyConfirmationClosePolicyRules.Decide(
            IntercompanyConfirmationClosePolicyRules.Propose("tenant-a", "company-a", "maker", Propose(codes), At),
            "checker", Decide(IntercompanyConfirmationClosePolicyActionDto.Approve, 1), At);
}

public sealed class IntercompanyConfirmationCloseGateTests
{
    private static readonly Guid Period = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly CloseReadinessScopeDto Scope = new("fund-a", IntercompanyConfirmationClosePolicyRulesTests.Book, Guid.NewGuid(), "entity-a", Period.ToString("D"));

    [Fact]
    public async Task NoActivePolicyMeansNoContribution()
    {
        var proposed = IntercompanyConfirmationClosePolicyRules.Propose("tenant-a", "company-a", "maker",
            IntercompanyConfirmationClosePolicyRulesTests.Propose("disputed"), IntercompanyConfirmationClosePolicyRulesTests.At);
        var summaries = new Mock<IIntercompanyBalanceConfirmationSummarySource>(MockBehavior.Strict);
        var gate = new IntercompanyConfirmationCloseGate(Store(proposed), summaries.Object, Journals().Object);

        Assert.Null(await gate.EvaluateAsync(Scope, "tenant-a", "company-a"));
        Assert.Null(await gate.EvaluateAsync(Scope with { LedgerBookId = null }, "tenant-a", "company-a"));
    }

    [Fact]
    public async Task AnActivePolicyEvaluatesConfirmationAtTheSelectedPeriodEnd()
    {
        var policy = IntercompanyConfirmationClosePolicyRulesTests.Active("disputed");
        var summaries = new Mock<IIntercompanyBalanceConfirmationSummarySource>();
        summaries.Setup(x => x.SummaryAsync("tenant-a", "company-a", IntercompanyConfirmationClosePolicyRulesTests.Book, new DateOnly(2026, 10, 31), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyBalanceConfirmationSummaryDto(IntercompanyConfirmationClosePolicyRulesTests.Book, new(2026, 10, 31), [],
                [new("Disputed", "Disputed.", Guid.NewGuid(), Guid.NewGuid())]));

        var result = await new IntercompanyConfirmationCloseGate(Store(policy), summaries.Object, Journals().Object).EvaluateAsync(Scope, "tenant-a", "company-a");

        Assert.Equal(("Blocked", policy.PolicyId, 2L), (result!.Status, result.PolicyId, result.PolicyVersion));
    }

    [Fact]
    public async Task AnActivePolicyFailsClosedWithoutAPeriodOrConfirmationService()
    {
        var policy = IntercompanyConfirmationClosePolicyRulesTests.Active("disputed");
        var summaries = new Mock<IIntercompanyBalanceConfirmationSummarySource>();
        summaries.Setup(x => x.SummaryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store offline"));

        Assert.Equal("Unavailable", (await new IntercompanyConfirmationCloseGate(Store(policy), summaries.Object, Journals().Object)
            .EvaluateAsync(Scope with { PeriodId = null }, "tenant-a", "company-a"))!.Status);
        Assert.Equal("Unavailable", (await new IntercompanyConfirmationCloseGate(Store(policy), null, Journals().Object)
            .EvaluateAsync(Scope, "tenant-a", "company-a"))!.Status);
        Assert.Equal("Unavailable", (await new IntercompanyConfirmationCloseGate(Store(policy), summaries.Object, Journals().Object)
            .EvaluateAsync(Scope, "tenant-a", "company-a"))!.Status);
    }

    private static IIntercompanyConfirmationClosePolicyStore Store(params IntercompanyConfirmationClosePolicyDto[] policies)
    {
        var store = new Mock<IIntercompanyConfirmationClosePolicyStore>();
        store.Setup(x => x.ListAsync("tenant-a", IntercompanyConfirmationClosePolicyRulesTests.Book, It.IsAny<CancellationToken>())).ReturnsAsync(policies);
        return store.Object;
    }

    private static Mock<ILedgerJournalStore> Journals()
    {
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetPeriodAsync(Period, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerAccountingPeriod(Period,
            IntercompanyConfirmationClosePolicyRulesTests.Book, 2026, 10, "2026-10", new(2026, 10, 1), new(2026, 10, 31), "Open",
            IntercompanyConfirmationClosePolicyRulesTests.At, null, 1));
        return journals;
    }
}

public abstract class IntercompanyConfirmationClosePolicyStoreContract
{
    protected abstract IIntercompanyConfirmationClosePolicyStore Open(string? tenant);

    protected async Task AppendsListsAndRefusesStaleOrForeignWritesCore()
    {
        var store = Open("tenant-a");
        var proposed = IntercompanyConfirmationClosePolicyRules.Propose("tenant-a", "company-a", "maker",
            IntercompanyConfirmationClosePolicyRulesTests.Propose("disputed"), DateTimeOffset.UnixEpoch);
        await store.AppendAsync(proposed, 0);
        var active = IntercompanyConfirmationClosePolicyRules.Decide(proposed, "checker",
            IntercompanyConfirmationClosePolicyRulesTests.Decide(IntercompanyConfirmationClosePolicyActionDto.Approve, 1), DateTimeOffset.UnixEpoch);
        await store.AppendAsync(active, 1);

        var reader = Open("tenant-a");
        Assert.Equal(2, (await reader.GetAsync("tenant-a", proposed.PolicyId))!.Version);
        Assert.Single(await reader.ListAsync("tenant-a", proposed.LedgerBookId));
        Assert.Empty(await reader.ListAsync("tenant-a", Guid.NewGuid()));
        Assert.Equal(IntercompanyConfirmationClosePolicyStoreRules.Serialize(active),
            IntercompanyConfirmationClosePolicyStoreRules.Serialize(await reader.AppendAsync(active, 1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.AppendAsync(active with { Version = 3 }, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.AppendAsync(active with { Version = 3, BlockingCodes = ["not-proposed"] }, 2));
        Assert.Null(await Open("tenant-b").GetAsync("tenant-b", proposed.PolicyId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Open("tenant-b").GetAsync("tenant-a", proposed.PolicyId));
    }

    protected sealed class Tenant(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }
}

public sealed class FileIntercompanyConfirmationClosePolicyStoreTests : IntercompanyConfirmationClosePolicyStoreContract, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "meridian-close-policies-" + Guid.NewGuid().ToString("N"), "policies.json");

    protected override IIntercompanyConfirmationClosePolicyStore Open(string? tenant) => new FileIntercompanyConfirmationClosePolicyStore(_path, new Tenant(tenant));

    [Fact] public Task AppendsListsAndRefusesStaleOrForeignWrites() => AppendsListsAndRefusesStaleOrForeignWritesCore();

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

[Trait("Category", "Integration")]
public sealed class PostgresIntercompanyConfirmationClosePolicyStoreTests : IntercompanyConfirmationClosePolicyStoreContract, IAsyncLifetime
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

    protected override IIntercompanyConfirmationClosePolicyStore Open(string? tenant) => new PostgresIntercompanyConfirmationClosePolicyStore(_database!.Options, new Tenant(tenant));

    [LedgerDatabaseFact] public Task AppendsListsAndRefusesStaleOrForeignWrites() => AppendsListsAndRefusesStaleOrForeignWritesCore();

    [LedgerDatabaseFact]
    public async Task RetainedVersionsCannotBeRewrittenInTheDatabase()
    {
        await AppendsListsAndRefusesStaleOrForeignWritesCore();
        await using var connection = new Npgsql.NpgsqlConnection(_database!.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] { "update {0}.intercompany_confirmation_close_policies set status='x'",
                     "delete from {0}.intercompany_confirmation_close_policies", "truncate {0}.intercompany_confirmation_close_policies" })
        {
            await using var command = new Npgsql.NpgsqlCommand(string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, $"\"{_database.Options.SchemaName}\""), connection);
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => command.ExecuteNonQueryAsync());
        }
    }
}
