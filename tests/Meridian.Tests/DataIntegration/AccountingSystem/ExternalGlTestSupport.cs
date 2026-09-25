using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.AccountingSystem;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

internal sealed class ExternalGlTestStore(string providerId) : IProviderCredentialStore
{
    public const string Tenant = "11111111-2222-3333-4444-555555555555";
    public Dictionary<string, string> Values { get; } = new()
    {
        ["ClientId"] = "test-client",
        ["ClientSecret"] = "secret-never-return",
        ["RefreshToken"] = "refresh-never-return",
        ["TenantId"] = Tenant,
        ["AccountId"] = "123_SB1",
        ["SubsidiaryId"] = "2",
        ["AccountingBookId"] = "1"
    };
    public List<ProviderCredentialVerificationUpdate> Verifications { get; } = [];
    public string VaultPath => "unused-test-vault";
    public Task<ProviderCredentialReadResult?> ReadForProviderAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<ProviderCredentialReadResult?>(new(providerId, ProviderCredentialSourceDto.LocalEncryptedStore,
            new Dictionary<string, string>(Values), "sandbox", null, null, null, null, null, null, new Dictionary<string, string>()));
    }
    public Task<ProviderCredentialStoreStatus> GetStatusAsync(string id, CancellationToken ct = default)
        => Task.FromResult(new ProviderCredentialStoreStatus(providerId, providerId, ProviderCredentialStateDto.Configured,
            ProviderCredentialSourceDto.LocalEncryptedStore, ProviderVerificationStateDto.NotVerified, null, null, null, null, null,
            null, null, "sandbox", null, [], Values.Keys.ToArray(), new Dictionary<string, string>()));
    public Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
    {
        foreach (var field in request.Credentials)
            Values[field.Key] = field.Value!;
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string id, string? actor = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
    {
        Verifications.Add(update);
        return Task.CompletedTask;
    }
}

internal sealed class ExternalGlTestHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri Uri, string? Authorization, string Body, string? Tenant, string? Prefer)> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.Scheme, body,
            request.Headers.TryGetValues("xero-tenant-id", out var tenant) ? tenant.Single() : null,
            request.Headers.TryGetValues("Prefer", out var prefer) ? prefer.Single() : null));
        return respond(request, body);
    }
    public static HttpResponseMessage Json(object value) => Raw(JsonSerializer.Serialize(value));
    public static HttpResponseMessage Raw(string value, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Page(object[] items, int offset = 0, bool more = false)
        => Json(new { items, offset, count = items.Length, hasMore = more });
}

internal static class ExternalGlTestData
{
    public static readonly Guid Book = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    public static AccountingSystemImportRequestDto Request(string provider) => new(provider, "fund", Book,
        new(2026, 1, 1), new(2026, 1, 31), TenantId: "tenant", CompanyId: "company");

    public static CredentialedAccountingProvider Provider(string id, ExternalGlTestStore store, HttpClient client)
        => id == "xero" ? new XeroAccountingProvider(store, client) : new NetSuiteAccountingProvider(store, client);

    public static HttpResponseMessage Respond(HttpRequestMessage request, string body)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("token", StringComparison.Ordinal))
            return ExternalGlTestHandler.Json(new { access_token = "access-never-return", refresh_token = "rotated-never-return" });
        if (path.EndsWith("Organisation", StringComparison.Ordinal))
            return ExternalGlTestHandler.Json(new { Organisations = new[] { new { OrganisationID = ExternalGlTestStore.Tenant, BaseCurrency = "USD" } } });
        if (path.EndsWith("Accounts", StringComparison.Ordinal))
            return ExternalGlTestHandler.Json(new
            {
                Accounts = new[]
            {
                new { AccountID = "cash", Code = "100", Name = "Cash", Type = "BANK", Status = "ACTIVE" },
                new { AccountID = "capital", Code = "300", Name = "Capital", Type = "EQUITY", Status = "ACTIVE" }
            }
            });
        if (path.EndsWith("Journals", StringComparison.Ordinal))
            return request.RequestUri.Query.Contains("offset=0", StringComparison.Ordinal)
                ? ExternalGlTestHandler.Json(new
                {
                    Journals = new[] { new
                {
                    JournalID = "journal-7", JournalNumber = 7, JournalDate = "/Date(1767657600000+0000)/", Reference = "Contribution",
                    JournalLines = new[]
                    {
                        new { JournalLineID = "1", AccountID = "cash", NetAmount = 100.25m },
                        new { JournalLineID = "2", AccountID = "capital", NetAmount = -100.25m }
                    }
                } }
                })
                : ExternalGlTestHandler.Json(new { Journals = Array.Empty<object>() });
        if (path.EndsWith("TrialBalance", StringComparison.Ordinal))
            return ExternalGlTestHandler.Json(new
            {
                Reports = new[] { new { ReportType = "TrialBalance", Rows = new object[]
            {
                new { RowType = "Header", Cells = new[] { "Account", "Debit", "Credit", "YTD Debit", "YTD Credit" }.Select(v => new { Value = v }) },
                new { RowType = "Section", Rows = new[] { XeroBalance("cash", "100.25", ""), XeroBalance("capital", "", "100.25") } }
            } } }
            });
        using var queryBody = JsonDocument.Parse(body);
        var query = queryBody.RootElement.GetProperty("q").GetString()!;
        if (query.Contains("FROM subsidiary", StringComparison.Ordinal))
            return ExternalGlTestHandler.Page([new { id = "2", currency = "USD" }]);
        if (query.Contains("FROM accountingbook", StringComparison.Ordinal))
            return ExternalGlTestHandler.Page([new { id = "1" }]);
        if (query.Contains("FROM account ORDER", StringComparison.Ordinal))
            return ExternalGlTestHandler.Page([
                new { id = "cash", acctnumber = "100", acctname = "Cash", accttype = "Bank", isinactive = "F" },
                new { id = "capital", acctnumber = "300", acctname = "Capital", accttype = "Equity", isinactive = "F" }
            ]);
        if (query.Contains("SUM(", StringComparison.Ordinal))
            return ExternalGlTestHandler.Page([new { accountid = "cash", balance = "100.25" }, new { accountid = "capital", balance = "-100.25" }]);
        return ExternalGlTestHandler.Page([
            new { journalid = "7", accountingdate = "2026-01-06", lineid = "1", accountid = "cash", debit = "100.25", credit = "0" },
            new { journalid = "7", accountingdate = "2026-01-06", lineid = "2", accountid = "capital", debit = "0", credit = "100.25" }
        ]);
    }

    private static object XeroBalance(string id, string debit, string credit) => new
    {
        RowType = "Row",
        Cells = new object[]
        {
            new { Value = id, Attributes = new[] { new { Id = "account", Value = id } } },
            new { Value = "1.00" }, new { Value = "" }, new { Value = debit }, new { Value = credit }
        }
    };
}
