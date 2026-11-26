using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

/// <summary>
/// Scope bindings that keep the file-backed trade-fill journal fail-closed: a posting scope stays
/// on the backend that acknowledged its fills, and paper sessions stay bound to their accounting
/// scope even when no financing service is composed.
/// </summary>
public sealed class TradeFillLedgerScopeBindingTests : IDisposable
{
    private static readonly TradeFillLedgerPostingContext NovemberScope = new(
        "ledger-book/test/period/november/aggregate/test",
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"));

    private static readonly TradeFillLedgerPostingContext DecemberScope = NovemberScope with
    {
        PostingScope = "ledger-book/test/period/december/aggregate/test",
        PeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444")
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-scope-binding-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(TradeFillLedgerStoreKind.File)]
    [InlineData(TradeFillLedgerStoreKind.Postgres)]
    public void BackendBinding_FirstUseRecordsTheStoreAndTheSameStorePassesOnRestart(TradeFillLedgerStoreKind store)
    {
        var options = new TradeFillPostingStoreOptions(_root, NovemberScope);

        TradeFillLedgerBackendBinding.Ensure(options, store);
        var restart = () => TradeFillLedgerBackendBinding.Ensure(options, store);

        restart.Should().NotThrow();
        File.Exists(Path.Combine(options.ScopeDirectory, TradeFillLedgerBackendBinding.FileName)).Should().BeTrue();
    }

    [Theory]
    [InlineData(TradeFillLedgerStoreKind.File, TradeFillLedgerStoreKind.Postgres)]
    [InlineData(TradeFillLedgerStoreKind.Postgres, TradeFillLedgerStoreKind.File)]
    public void BackendBinding_SwitchingStoreForAnExistingScope_IsRefused(
        TradeFillLedgerStoreKind bound, TradeFillLedgerStoreKind requested)
    {
        var options = new TradeFillPostingStoreOptions(_root, NovemberScope);
        TradeFillLedgerBackendBinding.Ensure(options, bound);

        var switchStore = () => TradeFillLedgerBackendBinding.Ensure(options, requested);

        switchStore.Should().Throw<InvalidOperationException>()
            .WithMessage($"*bound to the {bound} ledger store*Store is {requested}*");
    }

    [Fact]
    public void BackendBinding_NewScopeMayUseTheOtherStore()
    {
        TradeFillLedgerBackendBinding.Ensure(new TradeFillPostingStoreOptions(_root, NovemberScope), TradeFillLedgerStoreKind.Postgres);

        var rollover = () => TradeFillLedgerBackendBinding.Ensure(
            new TradeFillPostingStoreOptions(_root, DecemberScope), TradeFillLedgerStoreKind.File);

        rollover.Should().NotThrow();
    }

    [Fact]
    public void BackendBinding_LegacyHandoffDataWithoutBinding_IsTreatedAsPostgres()
    {
        var options = new TradeFillPostingStoreOptions(_root, NovemberScope);
        Directory.CreateDirectory(options.WalDirectory);
        File.WriteAllText(Path.Combine(options.WalDirectory, "wal_legacy.wal"), "MDCWAL02|2|2026-11-01T00:00:00Z\n");

        var fileMode = () => TradeFillLedgerBackendBinding.Ensure(options, TradeFillLedgerStoreKind.File);
        var postgresMode = () => TradeFillLedgerBackendBinding.Ensure(options, TradeFillLedgerStoreKind.Postgres);

        fileMode.Should().Throw<InvalidOperationException>().WithMessage("*bound to the Postgres ledger store*");
        postgresMode.Should().NotThrow();
    }

    [Fact]
    public void BackendBinding_ForeignBindingFile_IsRejected()
    {
        var november = new TradeFillPostingStoreOptions(_root, NovemberScope);
        TradeFillLedgerBackendBinding.Ensure(november, TradeFillLedgerStoreKind.File);
        var december = new TradeFillPostingStoreOptions(_root, DecemberScope);
        Directory.CreateDirectory(december.ScopeDirectory);
        File.Copy(
            Path.Combine(november.ScopeDirectory, TradeFillLedgerBackendBinding.FileName),
            Path.Combine(december.ScopeDirectory, TradeFillLedgerBackendBinding.FileName));

        var open = () => TradeFillLedgerBackendBinding.Ensure(december, TradeFillLedgerStoreKind.File);

        open.Should().Throw<InvalidDataException>().WithMessage("*does not belong to scope*");
    }

    [Fact]
    public async Task SessionsWithoutFinancing_BindToTheAccountingScopeAndBecomeReadOnlyAfterRollover()
    {
        var store = new JsonlFilePaperSessionStore(Path.Combine(_root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        string id;
        await using (var sessions = Sessions(store, NovemberScope))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            sessions.GetActivePortfolio(id).Should().NotBeNull();
        }

        await using var current = Sessions(store, DecemberScope);
        await current.InitialiseAsync();
        var created = await current.CreateSessionAsync(new("new-strategy", null, 2_000m));

        var records = await store.LoadAllSessionsAsync();
        records.Single(record => record.SessionId == id).AccountingContext.Should().Be(NovemberScope);
        records.Single(record => record.SessionId == id).FinancialAccountId.Should().Be(PaperTradingPortfolio.DefaultAccountId);
        records.Single(record => record.SessionId == created.SessionId).AccountingContext.Should().Be(DecemberScope);
        current.GetActivePortfolio(id).Should().BeNull("a session bound to a prior accounting scope is read-only");
        current.GetActivePortfolio(created.SessionId).Should().NotBeNull();
    }

    [Fact]
    public async Task SessionsWithoutFinancing_SameScopeAfterRestart_RemainTradable()
    {
        var store = new JsonlFilePaperSessionStore(Path.Combine(_root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        string id;
        await using (var sessions = Sessions(store, NovemberScope))
            id = (await sessions.CreateSessionAsync(new("strategy", null, 1_000m))).SessionId;

        await using var restarted = Sessions(store, NovemberScope);
        await restarted.InitialiseAsync();

        restarted.GetActivePortfolio(id).Should().NotBeNull();
        restarted.GetSession(id)!.AccountingUnavailableReason.Should().BeNull();
    }

    private static PaperSessionPersistenceService Sessions(IPaperSessionStore store, TradeFillLedgerPostingContext scope)
        => new(NullLogger<PaperSessionPersistenceService>.Instance, store, accountingContext: scope);
}
