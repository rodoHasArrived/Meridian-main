using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using NSubstitute;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Verifies the retained internal-book provider that replaces the empty default across the browser
/// workstation and CLI graphs: it resolves Meridian's retained cash balance and position snapshot for a
/// fund account as of the statement period end, projects the account's posted journals into the
/// ledger-transaction population through the composed <see cref="IInternalLedgerTransactionSource"/>,
/// labels every record with the run's external account key, and fails closed to an empty book on any
/// resolution gap so the matcher never fabricates a match.
/// </summary>
public sealed class RetainedInternalReconciliationPopulationProviderTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetPopulationsAsync_MapsRetainedCashAndPositions()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account("FUND-1", "run-1"));
        accounts.GetBalanceTimelineAsync(Arg.Any<Guid>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new List<AccountBalanceSnapshotDto> { Balance(new DateOnly(2026, 5, 31), 2500.25m) });

        var positions = Substitute.For<IPositionSnapshotStore>();
        positions.GetSnapshotHistoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => AsyncStream(Snapshot(
                new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
                new PositionRecord("SPY", 10m, 500m, 0m, 0m))));

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts, positions);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        var cash = result.CashBalances.Should().ContainSingle().Subject;
        cash.Account.Should().Be("EXT-1", "internal records are keyed by the run's external account");
        cash.Currency.Should().Be("USD");
        cash.Balance.Should().Be(2500.25m);

        var position = result.Positions.Should().ContainSingle().Subject;
        position.Account.Should().Be("EXT-1");
        position.SecurityId.Should().Be("SPY");
        position.Quantity.Should().Be(10m);
        position.AsOfDate.Should().Be(new DateOnly(2026, 5, 28));
        position.MarketValue.Should().BeNull("market value is left unspecified so the engine matches on quantity");

        result.LedgerTransactions.Should().BeEmpty("no ledger-transaction source is composed for this provider");
    }

    [Fact]
    public async Task GetPopulationsAsync_ProjectsLedgerTransactionsThroughComposedSource()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account("FUND-1", "run-1"));

        var ledgerSource = Substitute.For<IInternalLedgerTransactionSource>();
        InternalLedgerTransactionQuery? captured = null;
        ledgerSource
            .GetTransactionsAsync(Arg.Do<InternalLedgerTransactionQuery>(query => captured = query), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new InternalLedgerTransaction(
                    "internal-txn:EXT-1:j-1", "EXT-9", "EXT-1", "MSFT", "USD",
                    new DateOnly(2026, 5, 28), new DateOnly(2026, 5, 30), "trade", 5m, -100m, "internal:journal:j-1"),
            });

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts, ledgerTransactionSource: ledgerSource);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        var transaction = result.LedgerTransactions.Should().ContainSingle().Subject;
        transaction.Account.Should().Be("EXT-1", "projected transactions are labeled with the run's external account key");
        captured.Should().NotBeNull();
        captured!.AccountKey.Should().Be("EXT-1");
        captured.AccountAliases.Should().Contain(
            AccountId.ToString("D"), "journals stamped with the fund-account GUID must attribute to this account");
        captured.AccountAliases.Should().Contain(
            "EXT-1", "journals stamped with the external custodian key must attribute to this account");
        captured.PeriodStart.Should().Be(new DateOnly(2026, 5, 1));
        captured.PeriodEnd.Should().Be(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public async Task GetPopulationsAsync_ResolvesCashAsOfPeriodEnd()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account("FUND-1", "run-1"));
        accounts.GetBalanceTimelineAsync(Arg.Any<Guid>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new List<AccountBalanceSnapshotDto>
            {
                Balance(new DateOnly(2026, 5, 31), 2500.25m),
                Balance(new DateOnly(2026, 6, 30), 9999.99m),
            });

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        var cash = result.CashBalances.Should().ContainSingle().Subject;
        cash.Balance.Should().Be(
            2500.25m,
            "the balance at or before the statement period end is used, never one recorded after the period closes");
    }

    [Fact]
    public async Task GetPopulationsAsync_ReturnsLatestBalancePerCurrency()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account("FUND-1", "run-1"));
        accounts.GetBalanceTimelineAsync(Arg.Any<Guid>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new List<AccountBalanceSnapshotDto>
            {
                Balance(new DateOnly(2026, 5, 20), 100m, "USD"),
                Balance(new DateOnly(2026, 5, 31), 2500.25m, "USD"),
                Balance(new DateOnly(2026, 5, 31), 900m, "EUR"),
            });

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.CashBalances.Should().HaveCount(2, "each retained currency keeps its own period-end balance");
        result.CashBalances.Should().ContainSingle(cash => cash.Currency == "USD" && cash.Balance == 2500.25m);
        result.CashBalances.Should().ContainSingle(cash => cash.Currency == "EUR" && cash.Balance == 900m);
    }

    [Fact]
    public async Task GetPopulationsAsync_SnapshotAfterPeriodEnd_ExcludesPositions()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account("FUND-1", "run-1"));

        var positions = Substitute.For<IPositionSnapshotStore>();
        positions.GetSnapshotHistoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => AsyncStream(Snapshot(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
                new PositionRecord("SPY", 10m, 500m, 0m, 0m))));

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts, positions);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Positions.Should().BeEmpty(
            "a snapshot captured after the statement period end fails closed rather than reconciling against a later book state");
    }

    [Fact]
    public async Task GetPopulationsAsync_SelectsLatestSnapshotAtOrBeforePeriodEnd()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account("FUND-1", "run-1"));

        // Both a period-end (31 May) snapshot and a later (15 June) snapshot are retained for a 31 May
        // run. The period-appropriate 31 May book must be used, not discarded because a newer one exists.
        var positions = Substitute.For<IPositionSnapshotStore>();
        positions.GetSnapshotHistoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => AsyncStream(
                Snapshot(new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero), new PositionRecord("SPY", 10m, 500m, 0m, 0m)),
                Snapshot(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), new PositionRecord("QQQ", 99m, 900m, 0m, 0m))));

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts, positions);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        var position = result.Positions.Should().ContainSingle().Subject;
        position.SecurityId.Should().Be("SPY", "the 31 May snapshot is the latest at or before the period end");
        position.AsOfDate.Should().Be(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public async Task GetPopulationsAsync_ConflictingLatestInPeriodSnapshots_FailsClosedToEmpty()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account("FUND-1", "run-1"));

        var positions = Substitute.For<IPositionSnapshotStore>();
        positions.GetSnapshotHistoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => AsyncStream(
                Snapshot(new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero), new PositionRecord("SPY", 10m, 500m, 0m, 0m)),
                Snapshot(new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero), new PositionRecord("QQQ", 99m, 900m, 0m, 0m))));

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts, positions);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Should().BeSameAs(
            InternalReconciliationPopulations.Empty,
            "conflicting snapshots at the latest in-period timestamp must not select a file-order-dependent internal book");
    }

    [Fact]
    public async Task GetPopulationsAsync_NonGuidFundAccount_FailsClosedToEmpty()
    {
        var provider = new RetainedInternalReconciliationPopulationProvider(Substitute.For<IAccountQueryService>());

        var result = await provider.GetPopulationsAsync(Context("FUND-LABEL"));

        result.Should().BeSameAs(InternalReconciliationPopulations.Empty);
    }

    [Fact]
    public async Task GetPopulationsAsync_InactiveAccount_ReturnsEmptyBook()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Account("FUND-1", "run-1") with { IsActive = false });
        var provider = new RetainedInternalReconciliationPopulationProvider(accounts);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Positions.Should().BeEmpty();
        result.CashBalances.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPopulationsAsync_WithoutSources_FailsClosedToEmpty()
    {
        var provider = new RetainedInternalReconciliationPopulationProvider();

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Should().BeSameAs(InternalReconciliationPopulations.Empty);
    }

    private static InternalReconciliationPopulationContext Context(string fundAccountId) =>
        new(fundAccountId, "EXT-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "USD");

    private static AccountBalanceSnapshotDto Balance(DateOnly asOf, decimal cash, string currency = "USD") => new(
        Guid.NewGuid(), AccountId, null, asOf, currency, cash,
        null, null, null, "internal", DateTimeOffset.UnixEpoch, null);

    private static AccountSummaryDto Account(string code, string runId) => new(
        AccountId,
        AccountTypeDto.Brokerage,
        EntityId: null,
        FundId: null,
        SleeveId: null,
        VehicleId: null,
        AccountCode: code,
        DisplayName: "Fund One",
        BaseCurrency: "USD",
        Institution: null,
        IsActive: true,
        EffectiveFrom: DateTimeOffset.UnixEpoch,
        EffectiveTo: null,
        PortfolioId: null,
        LedgerReference: null,
        StrategyId: null,
        RunId: runId);

    private static AccountSnapshotRecord Snapshot(DateTimeOffset asOf, params PositionRecord[] positions) => new(
        "run-1", AccountId.ToString("D"), "Fund One", "Brokerage", 2500.25m, 0m, 0m, 0m, positions, asOf);

    private static async IAsyncEnumerable<AccountSnapshotRecord> AsyncStream(params AccountSnapshotRecord[] records)
    {
        foreach (var record in records)
        {
            yield return record;
        }

        await Task.CompletedTask;
    }
}
