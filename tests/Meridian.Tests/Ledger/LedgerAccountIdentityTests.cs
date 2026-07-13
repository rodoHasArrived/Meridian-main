using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Locks in the account-identity contract: <see cref="LedgerAccount.FinancialAccountId"/> is
/// case-insensitive so posting-side dictionary keying agrees with read-side
/// <c>Ledger.MatchesFinancialAccount</c> scoping, while <c>Name</c> and <c>Symbol</c> remain
/// case-sensitive.
/// </summary>
public sealed class LedgerAccountIdentityTests
{
    [Fact]
    public void Equals_FinancialAccountIdDiffersOnlyByCase_AreEqual()
    {
        var upper = new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "ACC-1");
        var lower = new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "acc-1");

        upper.Should().Be(lower);
        upper.GetHashCode().Should().Be(lower.GetHashCode());
    }

    [Fact]
    public void Equals_NameOrSymbolDiffersByCase_AreNotEqual()
    {
        var account = new LedgerAccount("Cash", LedgerAccountType.Asset, Symbol: "AAPL");

        account.Should().NotBe(account with { Name = "cash" });
        account.Should().NotBe(account with { Symbol = "aapl" });
    }

    [Fact]
    public void TrialBalance_MixedCaseFinancialAccountId_AccumulatesInOneAccount()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;
        var cashUpper = new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "ACC-1");
        var revenueUpper = new LedgerAccount("Revenue", LedgerAccountType.Revenue, FinancialAccountId: "ACC-1");
        var cashLower = new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "acc-1");
        var revenueLower = new LedgerAccount("Revenue", LedgerAccountType.Revenue, FinancialAccountId: "acc-1");

        ledger.PostLines(timestamp, "sale-1", new[]
        {
            (cashUpper, 100m, 0m),
            (revenueUpper, 0m, 100m),
        });
        ledger.PostLines(timestamp, "sale-2", new[]
        {
            (cashLower, 40m, 0m),
            (revenueLower, 0m, 40m),
        });

        var trialBalance = ledger.TrialBalance();

        trialBalance.Should().HaveCount(2);
        trialBalance[cashUpper].Should().Be(140m);
        trialBalance[revenueUpper].Should().Be(140m);
    }

    [Fact]
    public void TrialBalance_ScopedByAnyCasing_ReturnsSameMergedAccounts()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;

        ledger.PostLines(timestamp, "sale-1", new[]
        {
            (new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "ACC-1"), 100m, 0m),
            (new LedgerAccount("Revenue", LedgerAccountType.Revenue, FinancialAccountId: "ACC-1"), 0m, 100m),
        });
        ledger.PostLines(timestamp, "sale-2", new[]
        {
            (new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "acc-1"), 40m, 0m),
            (new LedgerAccount("Revenue", LedgerAccountType.Revenue, FinancialAccountId: "acc-1"), 0m, 40m),
        });

        var scopedUpper = ledger.TrialBalance(financialAccountId: "ACC-1");
        var scopedLower = ledger.TrialBalance(financialAccountId: "acc-1");

        scopedUpper.Should().HaveCount(2);
        scopedUpper.Should().BeEquivalentTo(scopedLower);
        scopedUpper.Values.Sum(Math.Abs).Should().Be(280m);
    }

    [Fact]
    public void SummarizeAccounts_MixedCaseFinancialAccountId_ReportsSingleAccountPerName()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;

        ledger.PostLines(timestamp, "sale-1", new[]
        {
            (new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "ACC-1"), 100m, 0m),
            (new LedgerAccount("Revenue", LedgerAccountType.Revenue, FinancialAccountId: "acc-1"), 0m, 100m),
        });
        ledger.PostLines(timestamp, "sale-2", new[]
        {
            (new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "acc-1"), 40m, 0m),
            (new LedgerAccount("Revenue", LedgerAccountType.Revenue, FinancialAccountId: "ACC-1"), 0m, 40m),
        });

        var summaries = ledger.SummarizeAccounts(financialAccountId: "Acc-1");

        summaries.Should().HaveCount(2);
        summaries.Select(summary => summary.Account.Name)
            .Should().BeEquivalentTo(["Cash", "Revenue"]);
    }
}
