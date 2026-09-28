using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Xunit;
using static Meridian.Tests.DataIntegration.AccountingSystem.ExternalGlTestData;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

public sealed class NetSuiteTrialBalanceTests
{
    [Theory]
    [InlineData("2025-12-31", "2025-12-01", 68, 0, 30, 10, 5, -113)]
    [InlineData("2026-01-01", "2024-04-01", 108, -40, 0, 0, 0, -68)]
    [InlineData("2026-06-30", "2026-06-01", 103, -40, 3, 0, 0, -66)]
    public async Task MultiYearDateReport_PreservesDirectRetainedPostingsAndResetsOnlyIncomeStatementAccounts(
        string end, string start, int cash, int income, int expense, int cogs, int otherExpense, int retained)
    {
        var periodEnd = DateOnly.ParseExact(end, "yyyy-MM-dd");
        var periodStart = DateOnly.ParseExact(start, "yyyy-MM-dd");
        var store = new ExternalGlTestStore("netsuite");
        var postings = History();
        var scope = postings.Where(p => p.Subsidiary == 2 && p.Book == 1 && p.IsPosted && !p.PeriodEndJournal).ToArray();
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("FROM account ORDER", StringComparison.Ordinal))
            {
                body.Should().Contain("sspecacct");
                return ExternalGlTestHandler.Page(Chart());
            }
            if (!body.Contains("FROM transaction t", StringComparison.Ordinal))
                return Respond(request, body);
            using var document = JsonDocument.Parse(body);
            var query = document.RootElement.GetProperty("q").GetString()!;
            query.Should().Contain("tl.subsidiary = 2").And.Contain("al.accountingbook = 1");
            query.Should().Contain("t.posting = 'T'").And.Contain("al.posting = 'T'");
            query.Should().Contain("t.type <> 'PEJrnl'");
            query.Should().Contain("tl.transaction = al.transaction AND tl.id = al.transactionline");
            query.Should().Contain($"t.trandate <= TO_DATE('{end}', 'YYYY-MM-DD')");
            if (query.Contains("AS prioryearbalance", StringComparison.Ordinal))
            {
                var yearStart = new DateOnly(periodEnd.Year, 1, 1);
                query.Should().Contain($"CASE WHEN t.trandate < TO_DATE('{yearStart:yyyy-MM-dd}', 'YYYY-MM-DD')");
                query.Should().NotContain("t.trandate >=");
                return ExternalGlTestHandler.Page(scope.Where(p => p.Date <= periodEnd).GroupBy(p => p.Account)
                    .Select(g => NetSuiteBalance(g.Key, g.Sum(p => p.Amount), g.Where(p => p.Date < yearStart).Sum(p => p.Amount))).ToArray());
            }
            query.Should().Contain($"t.trandate >= TO_DATE('{start}', 'YYYY-MM-DD')");
            return ExternalGlTestHandler.Page(scope.Where(p => p.Date >= periodStart && p.Date <= periodEnd).Select(p => (object)new
            {
                journalid = p.Journal, accountingdate = p.Date.ToString("yyyy-MM-dd"), lineid = p.Account,
                accountid = p.Account, debit = Math.Max(p.Amount, 0), credit = Math.Max(-p.Amount, 0)
            }).ToArray());
        });
        using var client = new HttpClient(handler);
        var provider = Provider("netsuite", store, client);
        var detail = await provider.ImportAsync(Request("netsuite") with { PeriodStart = periodStart, PeriodEnd = periodEnd });

        var amounts = detail.TrialBalance.ToDictionary(b => b.ExternalAccountId, b => b.Debit - b.Credit);
        amounts.Should().BeEquivalentTo(new Dictionary<string, decimal>
        {
            ["cash"] = cash, ["income"] = income, ["other-income"] = 0,
            ["expense"] = expense, ["cogs"] = cogs, ["other-expense"] = otherExpense, ["system-73"] = retained
        });
        amounts.Values.Sum().Should().Be(0m);
        detail.TrialBalance.Should().OnlyContain(b => b.AsOfDate == periodEnd && b.Currency == "USD" &&
            b.EvidenceRef!.Contains("date-accrual-calendar-year") && b.EvidenceRef.Contains("retained-earnings:system-73"));
        detail.JournalEntries.Should().HaveCount(scope.Where(p => p.Date >= periodStart && p.Date <= periodEnd).Select(p => p.Journal).Distinct().Count());
        detail.JournalEntries.SelectMany(j => j.Lines).Should().OnlyContain(l => l.Debit >= 0 && l.Credit >= 0);
        detail.Summary.State.Should().Be(AccountingSystemImportStateDto.Imported);
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeTrue();
        provider.Capabilities.SupportsPosting.Should().BeFalse();
    }

    [Theory]
    [InlineData(-100, 20, -80)]
    [InlineData(100, -20, 80)]
    [InlineData(-100, 100, 0)]
    public async Task PriorProfitOrLoss_IsAddedToDirectRetainedBalance_WithNetDebitCreditPresentation(int prior, int direct, int expected)
    {
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("FROM account ORDER", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page(Chart());
            if (body.Contains("AS balance", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([
                    NetSuiteBalance("cash", -prior - direct), NetSuiteBalance("income", prior, prior),
                    NetSuiteBalance("system-73", direct, direct - 5)]);
            if (body.Contains("AS journalid", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([]);
            return Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var detail = await Provider("netsuite", store, client).ImportAsync(Request("netsuite"));
        var balance = detail.TrialBalance.Single(b => b.ExternalAccountId == "system-73");
        balance.Debit.Should().Be(Math.Max(expected, 0));
        balance.Credit.Should().Be(Math.Max(-expected, 0));
        balance.AccountName.Should().Be("Accumulated results");
        detail.TrialBalance.Single(b => b.ExternalAccountId == "income").Credit.Should().Be(0);
        detail.TrialBalance.Sum(b => b.Debit - b.Credit).Should().Be(0m);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("inactive")]
    [InlineData("wrong-type")]
    [InlineData("missing-metadata")]
    public async Task UnverifiedSystemRetainedEarningsIdentity_FailsBeforeAccountingReads(string fault)
    {
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (!body.Contains("FROM account ORDER", StringComparison.Ordinal))
                return Respond(request, body);
            // A plausible name must never substitute for the provider's system identity.
            var accounts = new List<object> { NetSuiteAccount("lookalike", "Equity", name: "Retained Earnings") };
            if (fault != "missing")
                accounts.Add(fault == "missing-metadata"
                    ? new { id = "system-73", acctnumber = "300", acctname = "Retained Earnings", accttype = "Equity", isinactive = "F" }
                    : NetSuiteAccount("system-73", fault == "wrong-type" ? "Income" : "Equity", "RetEarnings", fault == "inactive" ? "T" : "F"));
            if (fault == "duplicate")
                accounts.Add(NetSuiteAccount("system-74", "Equity", "RetEarnings"));
            return ExternalGlTestHandler.Page(accounts.ToArray());
        });
        using var client = new HttpClient(handler);
        await Provider("netsuite", store, client).Invoking(p => p.ImportAsync(Request("netsuite")))
            .Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().NotContain(r => r.Body.Contains("FROM transaction t"));
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData("missing-prior")]
    [InlineData("malformed-prior")]
    [InlineData("unknown-account")]
    [InlineData("duplicate-account")]
    [InlineData("unbalanced")]
    public async Task InvalidAggregates_CannotBeHiddenByRetainedEarningsAdjustment(string fault)
    {
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("FROM account ORDER", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page(Chart());
            if (body.Contains("AS journalid", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([]);
            if (!body.Contains("AS balance", StringComparison.Ordinal))
                return Respond(request, body);
            var rows = new List<object> { NetSuiteBalance("cash", 100m), NetSuiteBalance("income", -100m, -90m) };
            rows[1] = fault switch
            {
                "missing-prior" => new { accountid = "income", balance = -100m },
                "malformed-prior" => new { accountid = "income", balance = -100m, prioryearbalance = "NaN" },
                "unknown-account" => NetSuiteBalance("unknown", -100m, -90m),
                "unbalanced" => NetSuiteBalance("income", -99m, -90m),
                _ => rows[1]
            };
            if (fault == "duplicate-account")
                rows.AddRange([NetSuiteBalance("income", 1m, 1m), NetSuiteBalance("income", -1m, -1m)]);
            return ExternalGlTestHandler.Page(rows.ToArray());
        });
        using var client = new HttpClient(handler);
        await Provider("netsuite", store, client).Invoking(p => p.ImportAsync(Request("netsuite")))
            .Should().ThrowAsync<InvalidOperationException>();
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    private static object[] Chart() => [
        NetSuiteAccount("cash", "Bank"), NetSuiteAccount("capital", "Equity", name: "Retained Earnings"),
        NetSuiteAccount("income", "Income"), NetSuiteAccount("other-income", "OthIncome"),
        NetSuiteAccount("expense", "Expense"), NetSuiteAccount("cogs", "COGS"), NetSuiteAccount("other-expense", "OthExpense"),
        NetSuiteAccount("system-73", "Equity", "RetEarnings", name: "Accumulated results")];

    private sealed record Posting(string Journal, DateOnly Date, string Account, decimal Amount,
        int Subsidiary, int Book, bool IsPosted, bool PeriodEndJournal);

    private static List<Posting> History()
    {
        var rows = new List<Posting>();
        void Add(string date, string account, decimal amount, int subsidiary = 2, int book = 1, bool posted = true, bool periodEnd = false)
        {
            var id = $"journal-{rows.Count}";
            rows.Add(new(id, DateOnly.ParseExact(date, "yyyy-MM-dd"), account, amount, subsidiary, book, posted, periodEnd));
            rows.Add(new(id, DateOnly.ParseExact(date, "yyyy-MM-dd"), "cash", -amount, subsidiary, book, posted, periodEnd));
        }
        Add("2023-12-31", "income", -100);
        Add("2024-12-31", "other-income", -20);
        Add("2025-01-01", "expense", 30);
        Add("2025-12-31", "cogs", 10);
        Add("2025-12-31", "other-expense", 5);
        Add("2025-12-31", "system-73", 7);
        Add("2026-01-01", "income", -40);
        Add("2026-03-15", "system-73", 2);
        Add("2026-06-01", "expense", 3);
        Add("2026-07-01", "income", -50);
        Add("2024-12-31", "income", -900, subsidiary: 3);
        Add("2024-12-31", "income", -800, book: 2);
        Add("2025-12-31", "income", 100, periodEnd: true);
        Add("2025-12-31", "capital", -600, posted: false);
        return rows;
    }
}
