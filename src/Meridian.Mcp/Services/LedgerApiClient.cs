using System.Net.Http;
using System.Net;
using System.Net.Http.Json;

namespace Meridian.Mcp.Services;

/// <summary>Read-only bridge to the workstation's authenticated, tenant-scoped ledger API.</summary>
public sealed class LedgerApiClient
{
    private const string SessionCookieName = "mdc-session";
    private readonly HttpClient client;
    private readonly LedgerApiCredentials? _credentials;
    private readonly CookieContainer _cookies = new();
    private readonly SemaphoreSlim _signInGate = new(1, 1);

    public LedgerApiClient(HttpClient client, LedgerApiCredentials? credentials = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        ValidateAddress(client.BaseAddress);
        _credentials = credentials;
    }

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
        var session = await EnsureSessionAsync(rejectedSession: null, ct).ConfigureAwait(false);
        using var first = await SendSessionReadAsync(path, ct).ConfigureAwait(false);
        if (first.StatusCode != HttpStatusCode.Unauthorized)
        {
            first.EnsureSuccessStatusCode();
            return await first.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        // A revoked/expired server session may be replaced once. Permission/scope refusals never
        // trigger another sign-in or a fallback identity, and concurrent reads reuse a renewed cookie.
        await EnsureSessionAsync(session, ct).ConfigureAwait(false);
        using var renewed = await SendSessionReadAsync(path, ct).ConfigureAwait(false);
        renewed.EnsureSuccessStatusCode();
        return await renewed.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendSessionReadAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", _cookies.GetCookieHeader(client.BaseAddress!));
        return await client.SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<string> EnsureSessionAsync(string? rejectedSession, CancellationToken ct)
    {
        await _signInGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var token = _cookies.GetCookies(client.BaseAddress!)[SessionCookieName]?.Value;
            if (!string.IsNullOrWhiteSpace(token) && token != rejectedSession)
                return token;
            var credentials = _credentials ?? LedgerApiCredentials.FromEnvironment();
            if (!string.IsNullOrEmpty(credentials.SessionToken) && credentials.SessionToken != rejectedSession)
            {
                if (credentials.SessionToken.Length != 64 || !credentials.SessionToken.All(char.IsAsciiHexDigit))
                    throw new InvalidOperationException("MDC_LEDGER_SESSION_TOKEN must contain only the issued session token, without a cookie name or headers.");
                _cookies.Add(client.BaseAddress!, new Cookie(SessionCookieName, credentials.SessionToken, "/")
                { Secure = client.BaseAddress!.Scheme == Uri.UriSchemeHttps, HttpOnly = true });
                return credentials.SessionToken;
            }
            if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrEmpty(credentials.Password))
                throw new HttpRequestException("The configured Meridian operator session was refused or expired. Supply a new issued session token or configure operator sign-in credentials.",
                    null, HttpStatusCode.Unauthorized);
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(new LedgerApiLoginRequest(credentials.Username, credentials.Password),
                    LedgerApiAuthenticationJsonContext.Default.LedgerApiLoginRequest)
            };
            // Some deployments protect the login transport with their existing API-key policy.
            // It is never forwarded on ledger reads and never supplies their company/tenant scope.
            if (!string.IsNullOrEmpty(credentials.ApiKey))
                request.Headers.Add("X-Api-Key", credentials.ApiKey);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Meridian ledger sign-in failed. Check the configured ledger operator credentials and server login policy.",
                    null, response.StatusCode);
            try
            {
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                    foreach (var cookie in cookies)
                        _cookies.SetCookies(client.BaseAddress!, cookie);
            }
            catch (CookieException)
            {
                throw new InvalidOperationException("Meridian login returned invalid session cookies.");
            }
            token = _cookies.GetCookies(client.BaseAddress!)[SessionCookieName]?.Value;
            if (string.IsNullOrWhiteSpace(token) && client.BaseAddress!.Scheme == Uri.UriSchemeHttp)
            {
                var httpsOrigin = new UriBuilder(client.BaseAddress) { Scheme = Uri.UriSchemeHttps }.Uri;
                if (_cookies.GetCookies(httpsOrigin)[SessionCookieName]?.Secure == true)
                    throw new InvalidOperationException("Meridian issued a Secure session cookie. Use HTTPS in MDC_API_URL; loopback HTTP sign-in requires the server's declared LocalWorkstation posture and loopback connection. The Secure cookie will not be sent over HTTP.");
            }
            if (string.IsNullOrWhiteSpace(token) || token == rejectedSession)
                throw new InvalidOperationException("Meridian login did not issue a usable session cookie. A configured operator login is required for scoped ledger reads.");
            return token;
        }
        finally { _signInGate.Release(); }
    }

    internal static void ValidateSegment(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value is "." or ".." || value.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A nonempty route identifier without path separators is required.", parameter);
    }

    public static HttpClient CreateHttpClient()
    {
        var address = Environment.GetEnvironmentVariable("MDC_API_URL") ?? "http://localhost:8080/";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("MDC_API_URL must be an absolute API URL.");
        ValidateAddress(uri);
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static void ValidateAddress(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("MDC_API_URL must be an HTTPS API origin or a loopback HTTP origin, without credentials, query or fragment.");
    }
}
