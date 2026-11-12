using System.Net.Http;

namespace Meridian.Mcp.Services;

/// <summary>Read-only bridge to the workstation's authenticated, tenant-scoped ledger API.</summary>
public sealed class LedgerApiClient(HttpClient client)
{
    public async Task<string> ReadAsync(string runId, string view, IReadOnlyDictionary<string, string?>? query, CancellationToken ct)
    {
        ValidateSegment(runId, nameof(runId));
        if (view is not ("entries" or "accounts" or "trial-balance/as-of" or "running-balance") && !view.StartsWith("symbols/", StringComparison.Ordinal))
            throw new ArgumentException("Unsupported ledger view.", nameof(view));
        var path = $"api/workstation/runs/{Uri.EscapeDataString(runId)}/ledger/{view}";
        if (query is not null)
        {
            var pairs = query.Where(pair => pair.Value is not null)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
            path += "?" + string.Join('&', pairs);
        }
        return await SendReadAsync(path, ct).ConfigureAwait(false);
    }

    public Task<string> ReadPeriodAsync(Guid periodId, string view, string? account, CancellationToken ct)
    {
        if (periodId == Guid.Empty || view is not ("journal-entries" or "trial-balance" or "accounts/running-balance"))
            throw new ArgumentException("A period and supported ledger view are required.");
        var path = $"api/ledger/periods/{periodId:D}/{view}";
        if (account is not null)
            path += "?account=" + Uri.EscapeDataString(account);
        return SendReadAsync(path, ct);
    }

    public Task<string> ReadCompletenessAsync(Guid ledgerBookId, Guid periodId, Guid aggregateId,
        string financialAccountId, string sourceKinds, string? sessionId, CancellationToken ct)
    {
        if (ledgerBookId == Guid.Empty || periodId == Guid.Empty || aggregateId == Guid.Empty)
            throw new ArgumentException("Book, period and aggregate identifiers are required.");
        var path = $"api/ledger/books/{ledgerBookId:D}/periods/{periodId:D}/completeness" +
            $"?aggregateId={aggregateId:D}&financialAccountId={Uri.EscapeDataString(financialAccountId)}&sourceKinds={Uri.EscapeDataString(sourceKinds)}";
        if (sessionId is not null)
            path += "&sessionId=" + Uri.EscapeDataString(sessionId);
        return SendReadAsync(path, ct);
    }

    private async Task<string> SendReadAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (Environment.GetEnvironmentVariable("MDC_API_KEY") is { Length: > 0 } key)
            request.Headers.Add("X-Api-Key", key);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    internal static void ValidateSegment(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value is "." or ".." || value.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A nonempty route identifier without path separators is required.", parameter);
    }

    public static HttpClient CreateHttpClient()
    {
        var address = Environment.GetEnvironmentVariable("MDC_API_URL") ?? "http://localhost:8080/";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("MDC_API_URL must be an HTTPS API origin or a loopback HTTP origin, without credentials, query or fragment.");
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}
