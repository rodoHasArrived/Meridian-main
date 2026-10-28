using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.Tests.FundStructure;

/// <summary>
/// A transaction-feed delivery stamps its rows with the latest value date it carries, so a
/// historical as-of view must admit each row by its own value date; a complete statement remains
/// evidence as of its header date.
/// </summary>
public sealed class GovernanceCashFlowTransactionFeedTests
{
    private static readonly DateOnly EarlierDate = new(2026, 10, 12);
    private static readonly DateOnly HeaderDate = new(2026, 10, 15);
    private static readonly DateTimeOffset AsOfBetween = new(2026, 10, 13, 16, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CashFlowView_AsOfBetweenADeliverysValueDates_KeepsEarlierFeedRowsButNotStatementRows(
        bool transactionFeed, bool earlierRowRealized)
    {
        var accounts = new InMemoryFundAccountService();
        var structure = new InMemoryFundStructureService(accounts);
        var fundId = await SeedFundAsync(structure);
        var account = await accounts.CreateAccountAsync(new CreateAccountRequest(Guid.NewGuid(), AccountTypeDto.Bank,
            "BANK-FEED", "Feed account", "USD", HeaderDate.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), "ops",
            FundId: fundId, LedgerReference: "LEDGER"));
        var batch = Guid.NewGuid();
        await accounts.IngestBankStatementAsync(new IngestBankStatementRequest(batch, account.AccountId, HeaderDate, "Bank", null,
            [
                new BankStatementLineDto(Guid.NewGuid(), batch, account.AccountId, EarlierDate, EarlierDate, 100m, "USD",
                    "Deposit", "Earlier receipt", null, 100m),
                new BankStatementLineDto(Guid.NewGuid(), batch, account.AccountId, HeaderDate, HeaderDate, 50m, "USD",
                    "Deposit", "Later receipt", null, 150m)
            ], "ops", IsBackfill: true, IsTransactionFeed: transactionFeed));

        var view = await structure.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account, AccountId: account.AccountId, AsOf: AsOfBetween, Currency: "USD",
            HistoricalDays: 7, ForecastDays: 7, BucketDays: 7));

        view.Should().NotBeNull();
        var realized = view!.RealizedEntries.Where(entry => entry.AccountId == account.AccountId).ToArray();
        realized.Should().NotContain(entry => entry.Amount == 50m, "the later row is after the as-of date either way");
        if (earlierRowRealized)
            realized.Should().ContainSingle(entry => entry.Amount == 100m && entry.EventDate.Date == EarlierDate.ToDateTime(TimeOnly.MinValue),
                "a feed row effective before the as-of date is realized cash even when its delivery also carried a later row");
        else
            realized.Should().BeEmpty("a statement dated after the as-of date is not yet evidence for that view");
    }

    // Accounts enter the cash-flow scope through their fund's organization and business.
    private static async Task<Guid> SeedFundAsync(InMemoryFundStructureService structure)
    {
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var effectiveFrom = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);
        await structure.CreateOrganizationAsync(new CreateOrganizationRequest(organizationId,
            $"ORG-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Feed Organization", "USD", effectiveFrom, "test"));
        await structure.CreateBusinessAsync(new CreateBusinessRequest(businessId, organizationId, BusinessKindDto.FundManager,
            $"BUS-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Feed Business", "USD", effectiveFrom, "test"));
        await structure.CreateFundAsync(new CreateFundRequest(fundId, $"FND-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            "Feed Fund", "USD", effectiveFrom, "test", BusinessId: businessId));
        return fundId;
    }
}
