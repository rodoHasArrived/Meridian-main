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
/// fund account as of the statement period end, labels them with the run's external account key, and
/// fails closed to an empty book on any resolution gap so the matcher never fabricates a match.
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
        positions.GetLatestSnapshotAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountSnapshotRecord(
                "run-1", AccountId.ToString("D"), "Fund One", "Brokerage", 2500.25m, 0m, 0m, 0m,
                [new PositionRecord("SPY", 10m, 500m, 0m, 0m)],
                new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero)));

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

        result.LedgerTransactions.Should().BeEmpty("ledger-transaction population is not sourced yet");
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
        positions.GetLatestSnapshotAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountSnapshotRecord(
                "run-1", AccountId.ToString("D"), "Fund One", "Brokerage", 2500.25m, 0m, 0m, 0m,
                [new PositionRecord("SPY", 10m, 500m, 0m, 0m)],
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)));

        var provider = new RetainedInternalReconciliationPopulationProvider(accounts, positions);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Positions.Should().BeEmpty(
            "a snapshot captured after the statement period end fails closed rather than reconciling against a later book state");
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
}
