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
            if (id == "netsuite" && fault == "malformed-amount" && body.Contains("SUM(", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([new { accountid = "cash", balance = "1,not-a-number" }]);
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, store, client);
        await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id))).Should().ThrowAsync<InvalidOperationException>();
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeFalse();
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
                        isinactive = "F"
                    }).ToArray(), more: true);
                using var original = ExternalGlTestData.Respond(request, body);
                using var doc = JsonDocument.Parse(original.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                return ExternalGlTestHandler.Page(doc.RootElement.GetProperty("items").EnumerateArray().Select(x => (object)x.Clone()).ToArray(), offset: 1000);
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var detail = await ExternalGlTestData.Provider("netsuite", store, client).ImportAsync(ExternalGlTestData.Request("netsuite"));
        detail.ChartAccounts.Should().HaveCount(1002);
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
