using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

public sealed class ExternalGlFailureBoundaryTests
{
    [Theory]
    [InlineData("xero", "malformed-amount")]
    [InlineData("xero", "repeated-page")]
    [InlineData("xero", "missing-collection")]
    [InlineData("xero", "wrong-organisation")]
    [InlineData("xero", "unbalanced-journal")]
    [InlineData("netsuite", "wrong-offset")]
    [InlineData("netsuite", "short-page-with-more")]
    [InlineData("netsuite", "malformed-amount")]
    [InlineData("netsuite", "missing-collection")]
    public async Task IncompleteOrMalformedProviderEvidence_FailsClosed(string id, string fault)
    {
        var store = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (fault == "wrong-organisation" && path.EndsWith("Organisation", StringComparison.Ordinal))
                return ExternalGlTestHandler.Json(new { Organisations = new[] { new { OrganisationID = Guid.NewGuid(), BaseCurrency = "USD" } } });
            if (id == "xero" && path.EndsWith("Journals", StringComparison.Ordinal))
            {
                if (fault == "missing-collection")
                    return ExternalGlTestHandler.Raw("{}");
                if (fault == "malformed-amount" || fault == "unbalanced-journal")
                    return ExternalGlTestHandler.Json(new
                    {
                        Journals = new[] { new
                    {
                        JournalID = "bad", JournalNumber = 1, JournalDate = "2026-01-02",
                        JournalLines = new[] { new { JournalLineID = "1", AccountID = "cash", NetAmount = fault == "malformed-amount" ? "NaN" : "1.00" } }
                    } }
                    });
                if (fault == "repeated-page")
                    return ExternalGlTestData.Respond(new HttpRequestMessage(HttpMethod.Get, "https://api.xero.com/api.xro/2.0/Journals?offset=0"), body);
            }
            if (id == "netsuite" && body.Contains("FROM account ORDER", StringComparison.Ordinal))
                return fault switch
                {
                    "wrong-offset" => ExternalGlTestHandler.Page([], offset: 1000),
                    "short-page-with-more" => ExternalGlTestHandler.Page([], more: true),
                    "missing-collection" => ExternalGlTestHandler.Raw("{}"),
                    _ => ExternalGlTestData.Respond(request, body)
                };
            if (id == "netsuite" && fault == "malformed-amount" && body.Contains("AS balance", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([new { accountid = "cash", balance = "1,not-a-number" }]);
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, store, client);
        await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id))).Should().ThrowAsync<InvalidOperationException>();
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData("xero", "unbalanced")]
    [InlineData("netsuite", "unbalanced")]
    [InlineData("xero", "negative")]
    [InlineData("xero", "both-sided")]
    public async Task InvalidTrialBalance_FailsWithoutRetainingSuccessfulEvidence(string id, string fault)
    {
        var store = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (id == "netsuite" && body.Contains("AS balance", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([ExternalGlTestData.NetSuiteBalance("cash", 100.25m)]);
            if (request.RequestUri!.AbsolutePath.EndsWith("TrialBalance", StringComparison.Ordinal))
            {
                var rows = fault switch
                {
                    "negative" => new[] { ExternalGlTestData.XeroBalance("cash", "-100.25", ""), ExternalGlTestData.XeroBalance("capital", "", "-100.25") },
                    "both-sided" => new[] { ExternalGlTestData.XeroBalance("cash", "101.25", "1.00"), ExternalGlTestData.XeroBalance("capital", "", "100.25") },
                    _ => new[] { ExternalGlTestData.XeroBalance("cash", "100.25", "") }
                };
                return ExternalGlTestHandler.Json(new
                {
                    Reports = new[] { new { ReportType = "TrialBalance", Rows = new object[]
                    {
                        new { RowType = "Header", Cells = new[] { "Account", "Debit", "Credit", "YTD Debit", "YTD Credit" }.Select(v => new { Value = v }) },
                        new { RowType = "Section", Rows = rows }
                    } } }
                });
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        await ExternalGlTestData.Provider(id, store, client).Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id)))
            .Should().ThrowAsync<InvalidOperationException>();
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData("Income", "-10.00")]
    [InlineData("Expense", "10.00")]
    [InlineData("OthIncome", "-10.00")]
    [InlineData("OthExpense", "10.00")]
    [InlineData("COGS", "10.00")]
    public async Task NetSuite_PriorYearProfitAndLossBalancesAreNormalizedWithoutLosingCurrentActivity(string accountType, string balance)
    {
        var prior = decimal.Parse(balance, System.Globalization.CultureInfo.InvariantCulture);
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("FROM account ORDER", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([
                    ExternalGlTestData.NetSuiteAccount("cash", "Bank"),
                    ExternalGlTestData.NetSuiteAccount("capital", "Equity"),
                    ExternalGlTestData.NetSuiteAccount("retained", "Equity", "RetEarnings"),
                    ExternalGlTestData.NetSuiteAccount("pnl", accountType)]);
            if (body.Contains("AS prioryearbalance", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([
                    ExternalGlTestData.NetSuiteBalance("cash", -prior - 3m),
                    ExternalGlTestData.NetSuiteBalance("pnl", prior + 3m, prior)]);
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var detail = await ExternalGlTestData.Provider("netsuite", store, client).ImportAsync(ExternalGlTestData.Request("netsuite"));
        detail.TrialBalance.Single(b => b.ExternalAccountId == "pnl").Debit.Should().Be(3m);
        var retained = detail.TrialBalance.Single(b => b.ExternalAccountId == "retained");
        (retained.Debit - retained.Credit).Should().Be(prior);
        detail.TrialBalance.Sum(b => b.Debit - b.Credit).Should().Be(0m);
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeTrue();
        var balanceRequest = handler.Requests.Should().ContainSingle(r => r.Body.Contains("AS prioryearbalance", StringComparison.Ordinal)).Which;
        using var document = JsonDocument.Parse(balanceRequest.Body);
        var query = document.RootElement.GetProperty("q").GetString()!;
        query.Should().Contain("t.trandate < TO_DATE('2026-01-01', 'YYYY-MM-DD')");
        query.Should().Contain("tl.subsidiary = 2").And.Contain("al.accountingbook = 1");
        query.Should().Contain("AS balance");
    }

    [Fact]
    public async Task NetSuite_OffsettingPriorYearAccountsAreBothNormalized()
    {
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("FROM account ORDER", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([
                    ExternalGlTestData.NetSuiteAccount("cash", "Bank"),
                    ExternalGlTestData.NetSuiteAccount("capital", "Equity"),
                    ExternalGlTestData.NetSuiteAccount("retained", "Equity", "RetEarnings"),
                    ExternalGlTestData.NetSuiteAccount("income", "Income"),
                    ExternalGlTestData.NetSuiteAccount("expense", "Expense")]);
            if (body.Contains("AS prioryearbalance", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([
                    ExternalGlTestData.NetSuiteBalance("income", -120m, -100m),
                    ExternalGlTestData.NetSuiteBalance("expense", 120m, 100m)]);
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var detail = await ExternalGlTestData.Provider("netsuite", store, client).ImportAsync(ExternalGlTestData.Request("netsuite"));
        detail.TrialBalance.Single(b => b.ExternalAccountId == "income").Credit.Should().Be(20m);
        detail.TrialBalance.Single(b => b.ExternalAccountId == "expense").Debit.Should().Be(20m);
        detail.TrialBalance.Should().HaveCount(2);
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeTrue();
    }

    [Fact]
    public async Task NetSuite_PaginatesAccountsWithoutFollowingResponseLinks()
    {
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("FROM account ORDER", StringComparison.Ordinal))
            {
                if (request.RequestUri!.Query.Contains("offset=0", StringComparison.Ordinal))
                    return ExternalGlTestHandler.Page(Enumerable.Range(0, 1000).Select(i => (object)new
                    {
                        id = $"extra-{i}",
                        acctnumber = $"A{i}",
                        acctname = $"Account {i}",
                        accttype = "Bank",
                        isinactive = "F",
                        specialaccounttype = "NONE"
                    }).ToArray(), more: true);
                using var original = ExternalGlTestData.Respond(request, body);
                using var doc = JsonDocument.Parse(original.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                return ExternalGlTestHandler.Page(doc.RootElement.GetProperty("items").EnumerateArray().Select(x => (object)x.Clone()).ToArray(), offset: 1000);
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var detail = await ExternalGlTestData.Provider("netsuite", store, client).ImportAsync(ExternalGlTestData.Request("netsuite"));
        detail.ChartAccounts.Should().HaveCount(1003);
        handler.Requests.Should().Contain(r => r.Uri.Query == "?limit=1000&offset=1000");
    }

    [Theory]
    [InlineData("AccountId", "attacker.example/path")]
    [InlineData("SubsidiaryId", "2 OR 1=1")]
    [InlineData("AccountingBookId", "0")]
    public async Task NetSuite_InvalidHostAndQueryScopeNeverReachNetwork(string field, string value)
    {
        var store = new ExternalGlTestStore("netsuite");
        store.Values[field] = value;
        using var handler = new ExternalGlTestHandler(ExternalGlTestData.Respond);
        using var client = new HttpClient(handler);
        await ExternalGlTestData.Provider("netsuite", store, client).Invoking(p => p.ImportAsync(ExternalGlTestData.Request("netsuite")))
            .Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }
}
