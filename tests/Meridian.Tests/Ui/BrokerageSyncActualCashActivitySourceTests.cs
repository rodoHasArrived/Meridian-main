using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class BrokerageSyncActualCashActivitySourceTests
{
    private static readonly Guid FundAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BondSecurityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly PeriodStart = new(2026, 6, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 6, 30);

    [Fact]
    public async Task GetActualActivityAsync_ReturnsClassifiedCashForHeldSecuritiesInsideThePeriod()
    {
        var source = CreateSource(Projection(
            isStale: false,
            Cash("coupon-1", "Bond Interest Received", 125.50m, "2026-06-15", "ust10"),
            Cash("paydown-1", "PrincipalPaydown", 1_000m, "2026-06-20", "UST10"),
            Cash("dividend-1", "DIV", 12m, "2026-06-10", "UST10"),
            Cash("other-symbol", "Bond Interest Received", 9m, "2026-06-15", "MSFT"),
            Cash("cash-interest", "INT", 3m, "2026-06-15", null),
            Cash("late", "Bond Interest Received", 125.50m, "2026-07-15", "UST10")));

        var rows = await source.GetActualActivityAsync([Position()], PeriodStart, PeriodEnd);

        rows.Select(row => (row.ExternalTransactionId, row.Classification, row.IncomeAmount, row.PrincipalAmount, row.PayDate))
            .Should().Equal(
                ("coupon-1", "Income", 125.50m, 0m, new DateOnly(2026, 6, 15)),
                ("paydown-1", "Principal", 0m, 1_000m, new DateOnly(2026, 6, 20)));
        rows.Should().OnlyContain(row =>
            row.SecurityId == BondSecurityId &&
            row.Symbol == "UST10" &&
            row.AccountId == FundAccountId.ToString("D") &&
            row.SourceName == BrokerageSyncActualCashActivitySource.SourceName);
    }

    [Fact]
    public async Task GetActualActivityAsync_KeepsTheBrokersSign_SoAReversalNetsAgainstItsReceipt()
    {
        var source = CreateSource(Projection(
            isStale: false,
            Cash("coupon-1", "Bond Interest Received", 125.50m, "2026-06-15", "UST10"),
            Cash("coupon-1-reversal", "Bond Interest Reversal", -125.50m, "2026-06-15", "UST10")));

        var rows = await source.GetActualActivityAsync([Position()], PeriodStart, PeriodEnd);

        rows.Select(row => row.IncomeAmount).Should().Equal(125.50m, -125.50m);
        rows.Sum(row => row.CashAmount).Should().Be(0m);
        rows.Should().OnlyContain(row => row.Classification == "Income" && row.PrincipalAmount == 0m);
    }

    [Fact]
    public async Task GetActualActivityAsync_ReturnsNothing_WhenProjectionIsStaleOrMissing()
    {
        var stale = CreateSource(Projection(isStale: true, Cash("coupon-1", "Bond Interest Received", 125.50m, "2026-06-15", "UST10")));
        var missing = new BrokerageSyncActualCashActivitySource((_, _) => Task.FromResult<FundAccountBrokerageSyncActivityDto?>(null));

        (await stale.GetActualActivityAsync([Position()], PeriodStart, PeriodEnd)).Should().BeEmpty();
        (await missing.GetActualActivityAsync([Position()], PeriodStart, PeriodEnd)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetActualActivityAsync_PrefersRetainedHistory_EvenWhenProjectionIsStale()
    {
        // The latest projection no longer carries the coupon paid earlier in the period; the
        // retained history does.
        var projection = Projection(isStale: true);
        var source = new BrokerageSyncActualCashActivitySource(
            (_, _) => Task.FromResult<FundAccountBrokerageSyncActivityDto?>(projection),
            (_, _) => Task.FromResult<IReadOnlyList<FundAccountBrokerageCashTransactionDto>>(
                [Cash("coupon-1", "Bond Interest Received", 125.50m, "2026-06-15", "UST10")]));

        var rows = await source.GetActualActivityAsync([Position()], PeriodStart, PeriodEnd);

        rows.Should().ContainSingle(row => row.ExternalTransactionId == "coupon-1" && row.IncomeAmount == 125.50m);
    }

    [Fact]
    public async Task GetActualActivityAsync_SkipsAccountsThatAreNotLinkedFundAccounts()
    {
        var loads = 0;
        var source = new BrokerageSyncActualCashActivitySource((_, _) =>
        {
            loads++;
            return Task.FromResult<FundAccountBrokerageSyncActivityDto?>(null);
        });

        var rows = await source.GetActualActivityAsync(
            [new SecurityMasterAccountingPosition("UST10", BondSecurityId, "unscoped-account", 10_000m)],
            PeriodStart,
            PeriodEnd);

        rows.Should().BeEmpty();
        loads.Should().Be(0);
    }

    private static BrokerageSyncActualCashActivitySource CreateSource(FundAccountBrokerageSyncActivityDto projection) =>
        new((fundAccountId, _) => Task.FromResult(fundAccountId == FundAccountId ? projection : null));

    private static SecurityMasterAccountingPosition Position() =>
        new("UST10", BondSecurityId, FundAccountId.ToString("D"), 10_000m);

    private static FundAccountBrokerageCashTransactionDto Cash(string id, string type, decimal amount, string date, string? symbol) =>
        new(id, type, amount, "USD", DateTimeOffset.Parse($"{date}T12:00:00Z"), symbol);

    private static FundAccountBrokerageSyncActivityDto Projection(bool isStale, params FundAccountBrokerageCashTransactionDto[] cash)
    {
        var syncedAt = DateTimeOffset.Parse("2026-06-30T23:00:00Z");
        return new FundAccountBrokerageSyncActivityDto(
            FundAccountId,
            new WorkstationBrokerageAccountLinkDto(FundAccountId, "alpaca", "external-1", "Test account", syncedAt),
            new WorkstationBrokerageSyncStatusDto(
                FundAccountId, "alpaca", "external-1", default, IsLinked: true, IsStale: isStale,
                syncedAt, syncedAt, null, 1, 0, 0, cash.Length, 0, []),
            Balance: null,
            Positions: [],
            Orders: [],
            Fills: [],
            CashTransactions: cash,
            SyncedAt: syncedAt,
            RawSnapshotPath: "raw/snapshot.json",
            ProjectionPath: "projections/account.json");
    }
}
