using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

/// <summary>
/// Verifies the workstation internal-book provider that replaces the empty default: it resolves
/// Meridian's retained cash balance and position snapshot for a fund account as of the statement
/// period end, labels them with the run's external account key, and fails closed to an empty book on
/// any resolution gap so the matcher never fabricates a match.
/// </summary>
public sealed class WorkstationInternalReconciliationPopulationProviderTests
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

        var provider = new WorkstationInternalReconciliationPopulationProvider(accounts, positions);

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

        var provider = new WorkstationInternalReconciliationPopulationProvider(accounts);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        var cash = result.CashBalances.Should().ContainSingle().Subject;
        cash.Balance.Should().Be(
            2500.25m,
            "the balance at or before the statement period end is used, never one recorded after the period closes");
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

        var provider = new WorkstationInternalReconciliationPopulationProvider(accounts, positions);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Positions.Should().BeEmpty(
            "a snapshot captured after the statement period end fails closed rather than reconciling against a later book state");
    }

    [Fact]
    public async Task GetPopulationsAsync_NonGuidFundAccount_FailsClosedToEmpty()
    {
        var provider = new WorkstationInternalReconciliationPopulationProvider(Substitute.For<IAccountQueryService>());

        var result = await provider.GetPopulationsAsync(Context("FUND-LABEL"));

        result.Should().BeSameAs(InternalReconciliationPopulations.Empty);
    }

    [Fact]
    public async Task GetPopulationsAsync_InactiveAccount_ReturnsEmptyBook()
    {
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Account("FUND-1", "run-1") with { IsActive = false });
        var provider = new WorkstationInternalReconciliationPopulationProvider(accounts);

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Positions.Should().BeEmpty();
        result.CashBalances.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPopulationsAsync_WithoutSources_FailsClosedToEmpty()
    {
        var provider = new WorkstationInternalReconciliationPopulationProvider();

        var result = await provider.GetPopulationsAsync(Context(AccountId.ToString("D")));

        result.Should().BeSameAs(InternalReconciliationPopulations.Empty);
    }

    private static InternalReconciliationPopulationContext Context(string fundAccountId) =>
        new(fundAccountId, "EXT-1", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "USD");

    private static AccountBalanceSnapshotDto Balance(DateOnly asOf, decimal cash) => new(
        Guid.NewGuid(), AccountId, null, asOf, "USD", cash,
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
