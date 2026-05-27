using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record BrokerageConnectionOptions(
    string ProviderId,
    string DisplayName,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string RevokeEndpoint,
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    string Scope)
{
    public static BrokerageConnectionOptions RobinhoodFromEnvironment() => new(
        ProviderId: "robinhood",
        DisplayName: "Robinhood read-only",
        AuthorizationEndpoint: ReadEnv("ROBINHOOD_BROKERAGE_AUTHORIZATION_ENDPOINT"),
        TokenEndpoint: ReadEnv("ROBINHOOD_BROKERAGE_TOKEN_ENDPOINT"),
        RevokeEndpoint: ReadEnv("ROBINHOOD_BROKERAGE_REVOKE_ENDPOINT"),
        ClientId: ReadEnv("ROBINHOOD_BROKERAGE_CLIENT_ID"),
        ClientSecret: ReadEnv("ROBINHOOD_BROKERAGE_CLIENT_SECRET"),
        RedirectUri: ReadEnv("ROBINHOOD_BROKERAGE_REDIRECT_URI"),
        Scope: ReadEnv("ROBINHOOD_BROKERAGE_SCOPE", "read_accounts read_holdings read_transactions"));

    private static string ReadEnv(string name, string fallback = "")
        => Environment.GetEnvironmentVariable(name) ?? fallback;
}

/// <summary>
/// Manages the read-only brokerage OAuth handoff used by workstation portfolio sync.
/// Tokens are stored through the existing environment-backed credential pattern and
/// are never returned in endpoint payloads.
/// </summary>
public sealed class BrokerageConnectionService
{
    private const string AccessTokenEnv = "ROBINHOOD_BROKERAGE_ACCESS_TOKEN";
    private const string RefreshTokenEnv = "ROBINHOOD_BROKERAGE_REFRESH_TOKEN";
    private const string TokenExpiresAtEnv = "ROBINHOOD_BROKERAGE_TOKEN_EXPIRES_AT";
    private const string ConnectedAtEnv = "ROBINHOOD_BROKERAGE_CONNECTED_AT";
    private const string PendingStateEnv = "ROBINHOOD_BROKERAGE_OAUTH_STATE";

    private readonly BrokerageConnectionOptions _options;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<BrokerageConnectionService> _logger;

    public BrokerageConnectionService(
        BrokerageConnectionOptions options,
        ILogger<BrokerageConnectionService> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
    }

    public Task<BrokerageConnectionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(BuildStatus(authorizationUrl: null, lastError: null));
    }

    public Task<BrokerageConnectionStatusDto> StartConnectionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsConfigured)
        {
            return Task.FromResult(BuildStatus(
                authorizationUrl: null,
                lastError: "Robinhood read-only OAuth is not configured. Set authorization endpoint, token endpoint, client id, and redirect URI."));
        }

        var state = Guid.NewGuid().ToString("N");
        SetCredential(PendingStateEnv, state);
        var authorizationUrl = BuildAuthorizationUrl(state);
        return Task.FromResult(BuildStatus(authorizationUrl, lastError: null));
    }

    public async Task<BrokerageConnectionStatusDto> CompleteCallbackAsync(
        string? code,
        string? state,
        string? error,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BuildStatus(null, $"Robinhood authorization failed: {error}");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BuildStatus(null, "Robinhood authorization callback did not include a code.");
        }

        var expectedState = Environment.GetEnvironmentVariable(PendingStateEnv);
        if (string.IsNullOrWhiteSpace(expectedState) || !string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            return BuildStatus(null, "Robinhood authorization state did not match the pending request.");
        }

        if (string.IsNullOrWhiteSpace(_options.TokenEndpoint))
        {
            return BuildStatus(null, "Robinhood authorization code received, but no token endpoint is configured.");
        }

        using var client = _httpClientFactory?.CreateClient(nameof(BrokerageConnectionService)) ?? new HttpClient();
        using var response = await client.PostAsync(
            _options.TokenEndpoint,
            new FormUrlEncodedContent(BuildTokenRequest(code)),
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Robinhood read-only token exchange failed with status {StatusCode}: {Body}",
                response.StatusCode,
                body);
            return BuildStatus(null, $"Robinhood token exchange failed with status {(int)response.StatusCode}.");
        }

        var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            return BuildStatus(null, "Robinhood token exchange did not return an access token.");
        }

        SetCredential(AccessTokenEnv, token.AccessToken);
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            SetCredential(RefreshTokenEnv, token.RefreshToken);
        }

        if (token.ExpiresIn > 0)
        {
            SetCredential(TokenExpiresAtEnv, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn).ToString("O"));
        }

        SetCredential(ConnectedAtEnv, DateTimeOffset.UtcNow.ToString("O"));
        ClearCredential(PendingStateEnv);
        return BuildStatus(authorizationUrl: null, lastError: null);
    }

    public async Task<BrokerageConnectionStatusDto> RevokeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var token = Environment.GetEnvironmentVariable(AccessTokenEnv);
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(_options.RevokeEndpoint))
        {
            try
            {
                using var client = _httpClientFactory?.CreateClient(nameof(BrokerageConnectionService)) ?? new HttpClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, _options.RevokeEndpoint)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["token"] = token,
                        ["client_id"] = _options.ClientId,
                        ["client_secret"] = _options.ClientSecret
                    })
                };
                using var _ = await client.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Robinhood read-only token revoke failed; clearing local token state anyway.");
            }
        }

        ClearCredential(AccessTokenEnv);
        ClearCredential(RefreshTokenEnv);
        ClearCredential(TokenExpiresAtEnv);
        ClearCredential(ConnectedAtEnv);
        ClearCredential(PendingStateEnv);
        return BuildStatus(authorizationUrl: null, lastError: null);
    }

    private bool IsConfigured
        => !string.IsNullOrWhiteSpace(_options.AuthorizationEndpoint)
           && !string.IsNullOrWhiteSpace(_options.TokenEndpoint)
           && !string.IsNullOrWhiteSpace(_options.ClientId)
           && !string.IsNullOrWhiteSpace(_options.RedirectUri);

    private BrokerageConnectionStatusDto BuildStatus(string? authorizationUrl, string? lastError)
    {
        var connectedAt = ParseDate(ConnectedAtEnv);
        var expiresAt = ParseDate(TokenExpiresAtEnv);
        var hasToken = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AccessTokenEnv));
        var hasPendingState = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PendingStateEnv));
        var warnings = new List<string>();
        var state = BrokerageConnectionStateDto.Disconnected;

        if (!IsConfigured)
        {
            state = BrokerageConnectionStateDto.NotConfigured;
            warnings.Add("Read-only Robinhood OAuth requires an authorized aggregation provider configuration.");
        }
        else if (!string.IsNullOrWhiteSpace(lastError))
        {
            state = BrokerageConnectionStateDto.Degraded;
            warnings.Add(lastError);
        }
        else if (hasToken && expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow)
        {
            state = BrokerageConnectionStateDto.ReauthorizationRequired;
            warnings.Add("Stored Robinhood read-only token is expired.");
        }
        else if (hasToken)
        {
            state = BrokerageConnectionStateDto.Connected;
        }
        else if (hasPendingState)
        {
            state = BrokerageConnectionStateDto.AuthorizationPending;
        }

        return new BrokerageConnectionStatusDto(
            ProviderId: _options.ProviderId,
            DisplayName: _options.DisplayName,
            State: state,
            IsConfigured: IsConfigured,
            IsConnected: state == BrokerageConnectionStateDto.Connected,
            AuthorizationUrl: authorizationUrl,
            ConnectedAt: connectedAt,
            ExpiresAt: expiresAt,
            LastError: lastError,
            Warnings: warnings,
            Scopes: _options.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private string BuildAuthorizationUrl(string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = _options.Scope,
            ["state"] = state
        };

        return AddQueryString(_options.AuthorizationEndpoint, parameters);
    }

    private Dictionary<string, string> BuildTokenRequest(string code)
    {
        var request = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["client_id"] = _options.ClientId
        };

        if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            request["client_secret"] = _options.ClientSecret;
        }

        return request;
    }

    private static string AddQueryString(string uri, IReadOnlyDictionary<string, string> parameters)
    {
        var separator = uri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var query = string.Join(
            "&",
            parameters
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{uri}{separator}{query}";
    }

    private static DateTimeOffset? ParseDate(string name)
        => DateTimeOffset.TryParse(Environment.GetEnvironmentVariable(name), out var parsed) ? parsed : null;

    private static void SetCredential(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
    }

    private static void ClearCredential(string name)
    {
        Environment.SetEnvironmentVariable(name, null);
        Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
    }

    private sealed record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
