using System.Net;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

public sealed class ExternalGlLiveProviderTests
{
    [Theory]
    [InlineData("xero", false)]
    [InlineData("netsuite", false)]
    [InlineData("xero", true)]
    [InlineData("netsuite", true)]
    public async Task TransportTimeout_IsRecordedAsFailure_WhileCallerCancellationRemainsCancellation(string id, bool callerCancels)
    {
        var store = new ExternalGlTestStore(id);
        using var client = new HttpClient(new StalledHandler())
        {
            Timeout = callerCancels ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(100)
        };
        using var cancellation = new CancellationTokenSource();
        if (callerCancels)
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var provider = ExternalGlTestData.Provider(id, store, client);
        if (callerCancels)
        {
            await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id), cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();
            store.Verifications.Should().BeEmpty();
        }
        else
        {
            var error = await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id), cancellation.Token))
                .Should().ThrowAsync<InvalidOperationException>();
            error.Which.Message.Should().Be("Read-only GL import failed. Check credentials, scope, permissions and provider response completeness.");
            error.Which.InnerException.Should().BeNull();
            store.Verifications.Should().ContainSingle().Which.Success.Should().BeFalse();
            var verification = await provider.VerifyConnectionAsync();
            verification.Success.Should().BeFalse();
            verification.LastError.Should().Be("Provider verification failed. Check credentials, account scope, permissions and availability.");
            store.Verifications.Should().HaveCount(2).And.OnlyContain(v => !v.Success);
        }
    }

    private sealed class StalledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The stalled transport must end through cancellation.");
        }
    }

    [Theory]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task Import_UsesCredentialedTransport_RotatesTokenAndReturnsScopedBalancedEvidence(string id)
    {
        var store = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler(ExternalGlTestData.Respond);
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, store, client);
        var detail = await provider.ImportAsync(ExternalGlTestData.Request(id));

        provider.Capabilities.RequiresCredentials.Should().BeTrue();
        provider.Capabilities.SupportsPosting.Should().BeFalse();
        detail.Summary.State.Should().Be(AccountingSystemImportStateDto.Imported);
        detail.Summary.TenantId.Should().Be("tenant");
        detail.Summary.CompanyId.Should().Be("company");
        detail.ChartAccounts.Should().HaveCount(id == "netsuite" ? 3 : 2);
        detail.JournalEntries.Should().ContainSingle().Which.TotalDebits.Should().Be(100.25m);
        detail.TrialBalance.Should().Contain(b => b.ExternalAccountId == "cash" && b.Debit == 100.25m);
        detail.TrialBalance.Should().OnlyContain(b => b.Currency == "USD" && b.AsOfDate == new DateOnly(2026, 1, 31));
        detail.JournalEntries.Single().AccountingDate.Should().Be(new DateOnly(2026, 1, 6));
        store.Values["RefreshToken"].Should().Be("rotated-never-return");
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeTrue();
        handler.Requests.First().Authorization.Should().Be("Basic");
        handler.Requests.Skip(1).Should().OnlyContain(r => r.Authorization == "Bearer");
        if (id == "xero")
        {
            handler.Requests.Skip(1).Should().OnlyContain(r => r.Method == HttpMethod.Get && r.Tenant == ExternalGlTestStore.Tenant);
            handler.Requests.Should().Contain(r => r.Uri.Query.Contains("offset=7"));
        }
        else
        {
            handler.Requests.Should().OnlyContain(r => r.Uri.Host == "123-sb1.suitetalk.api.netsuite.com");
            handler.Requests.Skip(1).Should().OnlyContain(r => r.Uri.AbsolutePath == "/services/rest/query/v1/suiteql" && r.Prefer == "transient");
            handler.Requests.Where(r => r.Body.Contains("FROM transaction t")).Should().OnlyContain(r =>
                r.Body.Contains("tl.subsidiary = 2") && r.Body.Contains("al.accountingbook = 1") && r.Body.Contains("2026-01-31"));
        }
        System.Text.Json.JsonSerializer.Serialize(detail).Should().NotContain("never-return");
    }

    [Theory]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task MissingCredentialsAndInvalidPeriods_FailBeforeNetwork(string id)
    {
        var store = new ExternalGlTestStore(id);
        store.Values.Remove("ClientSecret");
        using var handler = new ExternalGlTestHandler(ExternalGlTestData.Respond);
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, store, client);
        (await provider.GetConnectionMetadataAsync()).HasLocalConfig.Should().BeFalse();
        await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id))).Should().ThrowAsync<InvalidOperationException>();
        await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id) with { PeriodEnd = new(2025, 1, 1) }))
            .Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetSuite_NoPriorYearProfitAndLossBalance_PreservesScopedCurrentYearImports(bool hasZeroPriorYearBalance)
    {
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("AS prioryearbalance", StringComparison.Ordinal))
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                var query = document.RootElement.GetProperty("q").GetString()!;
                // The report uses the selected account/book and the report's calendar boundary,
                // not the requested period start or balance-sheet account history.
                query.Should().Contain("tl.subsidiary = 2").And.Contain("al.accountingbook = 1");
                query.Should().Contain("t.posting = 'T'").And.Contain("al.posting = 'T'");
                query.Should().Contain("tl.transaction = al.transaction AND tl.id = al.transactionline");
                query.Should().Contain("t.trandate < TO_DATE('2026-01-01', 'YYYY-MM-DD')");
                query.Should().Contain("t.type <> 'PEJrnl'");
                // Historic balance-sheet amounts must remain cumulative even though P&L resets.
                return ExternalGlTestHandler.Page([
                    ExternalGlTestData.NetSuiteBalance("cash", 100.25m, hasZeroPriorYearBalance ? 0m : 90m),
                    ExternalGlTestData.NetSuiteBalance("capital", -100.25m, hasZeroPriorYearBalance ? 0m : -90m)]);
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var detail = await ExternalGlTestData.Provider("netsuite", store, client)
            .ImportAsync(ExternalGlTestData.Request("netsuite") with { PeriodStart = new(2026, 1, 5) });
        detail.Summary.State.Should().Be(AccountingSystemImportStateDto.Imported);
        detail.JournalEntries.Should().ContainSingle();
        detail.TrialBalance.Should().HaveCount(2);
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("xero", 401)]
    [InlineData("xero", 429)]
    [InlineData("netsuite", 403)]
    [InlineData("netsuite", 503)]
    public async Task ProviderFailure_NeverReturnsPartialEvidenceOrSecretResponse(string id, int status)
    {
        var store = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler((request, body) =>
            request.RequestUri!.AbsolutePath.EndsWith("token", StringComparison.Ordinal)
                ? ExternalGlTestData.Respond(request, body)
                : ExternalGlTestHandler.Raw("secret-never-return", (HttpStatusCode)status));
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, store, client);
        var exception = await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id))).Should().ThrowAsync<InvalidOperationException>();
        exception.Which.ToString().Should().NotContain("secret-never-return");
        var verification = await provider.VerifyConnectionAsync();
        verification.Success.Should().BeFalse();
        verification.LastError.Should().NotContain("secret-never-return");
        store.Verifications.Should().OnlyContain(v => !v.Success);
    }

    [Theory]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task Cancellation_DoesNotRecordCredentialFailure(string id)
    {
        var store = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler(ExternalGlTestData.Respond);
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, store, client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request(id), cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        store.Verifications.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public void CatalogAndSetup_ExposeCredentialOnlyReadOnlyAdapters(string id)
    {
        var catalog = ProviderCredentialCatalog.Find(id)!;
        catalog.RequiredFields.Should().Contain(f => f.Name == "RefreshToken" && f.Required);
        var descriptor = DefaultProviderSetupHandlers.Create().Single(h => h.CanHandle(id)).Descriptor;
        descriptor.CredentialOnly.Should().BeTrue();
        descriptor.SupportsVerification.Should().BeTrue();
        descriptor.EnableBindingsImmediately.Should().BeFalse();
        descriptor.DefaultRoutingMode.ToString().Should().Be("ReadOnly");
    }
}
