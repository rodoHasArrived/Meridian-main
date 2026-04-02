using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Application.Config.Credentials;

/// <summary>
/// Service for testing and validating provider credentials.
/// Tracks authentication status, expiration warnings, and supports OAuth auto-refresh.
/// </summary>
public sealed class CredentialTestingService : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<CredentialTestingService>();
    private readonly HttpClient _httpClient;
    private readonly CredentialExpirationConfig _expirationConfig;
    private readonly ConcurrentDictionary<string, StoredCredentialStatus> _statusCache = new();
    private readonly ConcurrentDictionary<string, OAuthToken> _oauthTokens = new();
    private readonly string _statusPersistencePath;
    private readonly SemaphoreSlim _testLock = new(1, 1);

    // Events for UI updates
    public event Action<CredentialTestResult>? OnCredentialTested;
    public event Action<string, string>? OnExpirationWarning;
#pragma warning disable CS0067 // Event will be raised when OAuth refresh is implemented
    public event Action<string, OAuthRefreshResult>? OnTokenRefreshed;
#pragma warning restore CS0067

    public CredentialTestingService(
        string dataRoot,
        CredentialExpirationConfig? expirationConfig = null,
        HttpClient? httpClient = null)
    {
        _expirationConfig = expirationConfig ?? new CredentialExpirationConfig();
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _statusPersistencePath = Path.Combine(dataRoot, ".mdc", "credential_status.json");
        LoadPersistedStatus();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        var client = HttpClientFactoryProvider.CreateClient(HttpClientNames.CredentialTesting);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Meridian/1.6.1");
        return client;
    }

    /// <summary>
    /// Tests credentials for a specific provider.
    /// </summary>
    public async Task<CredentialTestResult> TestCredentialAsync(
        string providerName,
        string? apiKey = null,
        string? apiSecret = null,
        string? credentialSource = null,
        CancellationToken ct = default)
    {
        await _testLock.WaitAsync(ct);
        try
        {
            var sw = Stopwatch.StartNew();
            var testedAt = DateTimeOffset.UtcNow;

            // Check if credentials are configured
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var result = new CredentialTestResult(
                    ProviderName: providerName,
                    Status: CredentialAuthStatus.NotConfigured,
                    Message: "API key is not configured",
                    TestedAt: testedAt,
                    CredentialSource: credentialSource
                );
                UpdateCache(result);
                OnCredentialTested?.Invoke(result);
                return result;
            }

            // Mask credential for display
            var maskedKey = MaskCredential(apiKey);

            try
            {
                var (status, message, expiresAt) = providerName.ToLowerInvariant() switch
                {
                    "alpaca" => await TestAlpacaCredentialsAsync(apiKey, apiSecret, ct),
                    "polygon" => await TestPolygonCredentialsAsync(apiKey, ct),
                    "tiingo" => await TestTiingoCredentialsAsync(apiKey, ct),
                    "finnhub" => await TestFinnhubCredentialsAsync(apiKey, ct),
                    "alphavantage" => await TestAlphaVantageCredentialsAsync(apiKey, ct),
                    "nasdaqdatalink" or "nasdaq" => await TestNasdaqDataLinkCredentialsAsync(apiKey, ct),
                    _ => (CredentialAuthStatus.Unknown, $"Unknown provider: {providerName}", (DateTimeOffset?)null)
                };

                sw.Stop();

                // Check for expiration warnings
                if (status == CredentialAuthStatus.Valid && expiresAt.HasValue)
                {
                    status = CheckExpirationStatus(expiresAt.Value);
                    if (status == CredentialAuthStatus.ExpiringSoon)
                    {
                        var daysUntil = (expiresAt.Value - DateTimeOffset.UtcNow).TotalDays;
                        message = $"Credential valid but expiring in {daysUntil:F1} days";
                        OnExpirationWarning?.Invoke(providerName, message);
                    }
                }

                var lastSuccess = status == CredentialAuthStatus.Valid
                    ? testedAt
                    : GetLastSuccessfulAuth(providerName);

                var result = new CredentialTestResult(
                    ProviderName: providerName,
                    Status: status,
                    Message: message,
                    TestedAt: testedAt,
                    LastSuccessfulAuth: lastSuccess,
                    ExpiresAt: expiresAt,
                    ResponseTimeMs: sw.ElapsedMilliseconds,
                    CanAutoRefresh: SupportsOAuthRefresh(providerName),
                    CredentialSource: credentialSource,
                    CredentialMasked: maskedKey
                );

                UpdateCache(result);
                OnCredentialTested?.Invoke(result);

                _log.Information(
                    "Credential test for {Provider}: {Status} ({ResponseTimeMs}ms)",
                    providerName, status, sw.ElapsedMilliseconds);

                return result;
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                var result = new CredentialTestResult(
                    ProviderName: providerName,
                    Status: CredentialAuthStatus.TestFailed,
                    Message: $"Network error: {ex.Message}",
                    TestedAt: testedAt,
                    ResponseTimeMs: sw.ElapsedMilliseconds,
                    CredentialSource: credentialSource,
                    CredentialMasked: maskedKey
                );
                UpdateCache(result);
                OnCredentialTested?.Invoke(result);
                return result;
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                var result = new CredentialTestResult(
                    ProviderName: providerName,
                    Status: CredentialAuthStatus.TestFailed,
                    Message: "Request timed out",
                    TestedAt: testedAt,
                    ResponseTimeMs: sw.ElapsedMilliseconds,
                    CredentialSource: credentialSource,
                    CredentialMasked: maskedKey
                );
                UpdateCache(result);
                OnCredentialTested?.Invoke(result);
                return result;
            }
        }
        finally
        {
            _testLock.Release();
        }
    }

    /// <summary>
    /// Tests all configured providers and returns aggregated status.
    /// </summary>
    public async Task<CredentialStatusSummary> TestAllCredentialsAsync(
        AppConfig config,
        CancellationToken ct = default)
    {
        var results = new List<CredentialTestResult>();
        var warnings = new List<string>();

        // Test Alpaca if configured
        if (config.Alpaca is not null && !string.IsNullOrWhiteSpace(config.Alpaca.KeyId))
        {
            var result = await TestCredentialAsync("Alpaca", config.Alpaca.KeyId, config.Alpaca.SecretKey, "Config", ct);
            results.Add(result);
            if (result.Status == CredentialAuthStatus.ExpiringSoon)
                warnings.Add($"Alpaca credentials expiring in {result.TimeUntilExpiration?.TotalDays:F1} days");
        }

        // Test backfill providers
        var backfillProviders = config.Backfill?.Providers;
        if (backfillProviders is not null)
        {
            if (backfillProviders.Polygon is { Enabled: true } && !string.IsNullOrWhiteSpace(backfillProviders.Polygon.ApiKey))
            {
                var result = await TestCredentialAsync("Polygon", backfillProviders.Polygon.ApiKey, null, "Config", ct);
                results.Add(result);
            }

            if (backfillProviders.Tiingo is { Enabled: true } && !string.IsNullOrWhiteSpace(backfillProviders.Tiingo.ApiToken))
            {
                var result = await TestCredentialAsync("Tiingo", backfillProviders.Tiingo.ApiToken, null, "Config", ct);
                results.Add(result);
            }

            if (backfillProviders.Finnhub is { Enabled: true } && !string.IsNullOrWhiteSpace(backfillProviders.Finnhub.ApiKey))
            {
                var result = await TestCredentialAsync("Finnhub", backfillProviders.Finnhub.ApiKey, null, "Config", ct);
                results.Add(result);
            }

            if (backfillProviders.AlphaVantage is { Enabled: true } && !string.IsNullOrWhiteSpace(backfillProviders.AlphaVantage.ApiKey))
            {
                var result = await TestCredentialAsync("AlphaVantage", backfillProviders.AlphaVantage.ApiKey, null, "Config", ct);
                results.Add(result);
            }

            if (backfillProviders.Nasdaq is { Enabled: true } && !string.IsNullOrWhiteSpace(backfillProviders.Nasdaq.ApiKey))
            {
                var result = await TestCredentialAsync("NasdaqDataLink", backfillProviders.Nasdaq.ApiKey, null, "Config", ct);
                results.Add(result);
            }
        }

        var summary = new CredentialStatusSummary(
            Results: results,
            AllValid: results.All(r => r.IsSuccess),
            TestedAt: DateTimeOffset.UtcNow,
            Warnings: warnings
        );

        await PersistStatusAsync();
        return summary;
    }

    /// <summary>
    /// Gets the last successful authentication timestamp for a provider.
    /// </summary>
    public DateTimeOffset? GetLastSuccessfulAuth(string providerName)
    {
        return _statusCache.TryGetValue(providerName, out var status)
            ? status.LastSuccessfulAuth
            : null;
    }

    /// <summary>
    /// Gets cached credential status for a provider without performing a test.
    /// </summary>
    public StoredCredentialStatus? GetCachedStatus(string providerName)
    {
        return _statusCache.TryGetValue(providerName, out var status) ? status : null;
    }

    /// <summary>
    /// Gets all cached credential statuses.
    /// </summary>
    public IReadOnlyDictionary<string, StoredCredentialStatus> GetAllCachedStatuses()
    {
        return _statusCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }


    private async Task<(CredentialAuthStatus status, string message, DateTimeOffset? expiresAt)>
        TestAlpacaCredentialsAsync(string keyId, string? secretKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            return (CredentialAuthStatus.NotConfigured, "Secret key is required for Alpaca", null);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.alpaca.markets/v2/account");
        request.Headers.Add("APCA-API-KEY-ID", keyId);
        request.Headers.Add("APCA-API-SECRET-KEY", secretKey);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return (CredentialAuthStatus.Valid, "Alpaca credentials are valid", null);
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => (CredentialAuthStatus.Invalid, "Invalid API key or secret", null),
            System.Net.HttpStatusCode.Forbidden => (CredentialAuthStatus.Invalid, "Access forbidden - check API key permissions", null),
            _ => (CredentialAuthStatus.TestFailed, $"Unexpected response: {response.StatusCode}", null)
        };
    }

    private async Task<(CredentialAuthStatus status, string message, DateTimeOffset? expiresAt)>
        TestPolygonCredentialsAsync(string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.polygon.io/v3/reference/tickers?limit=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return (CredentialAuthStatus.Valid, "Polygon credentials are valid", null);
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => (CredentialAuthStatus.Invalid, "Invalid API key", null),
            System.Net.HttpStatusCode.Forbidden => (CredentialAuthStatus.Invalid, "Access forbidden - check subscription tier", null),
            _ => (CredentialAuthStatus.TestFailed, $"Unexpected response: {response.StatusCode}", null)
        };
    }

    private async Task<(CredentialAuthStatus status, string message, DateTimeOffset? expiresAt)>
        TestTiingoCredentialsAsync(string apiToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.tiingo.com/api/test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", apiToken);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return (CredentialAuthStatus.Valid, "Tiingo credentials are valid", null);
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => (CredentialAuthStatus.Invalid, "Invalid API token", null),
            System.Net.HttpStatusCode.Forbidden => (CredentialAuthStatus.Invalid, "Access forbidden", null),
            _ => (CredentialAuthStatus.TestFailed, $"Unexpected response: {response.StatusCode}", null)
        };
    }

    private async Task<(CredentialAuthStatus status, string message, DateTimeOffset? expiresAt)>
        TestFinnhubCredentialsAsync(string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://finnhub.io/api/v1/stock/symbol?exchange=US");
        request.Headers.TryAddWithoutValidation("X-Finnhub-Token", apiKey);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return (CredentialAuthStatus.Valid, "Finnhub credentials are valid", null);
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => (CredentialAuthStatus.Invalid, "Invalid API key", null),
            System.Net.HttpStatusCode.Forbidden => (CredentialAuthStatus.Invalid, "Access forbidden - rate limit exceeded or invalid key", null),
            _ => (CredentialAuthStatus.TestFailed, $"Unexpected response: {response.StatusCode}", null)
        };
    }

    private async Task<(CredentialAuthStatus status, string message, DateTimeOffset? expiresAt)>
        TestAlphaVantageCredentialsAsync(string apiKey, CancellationToken ct)
    {
        // Alpha Vantage uses a simple query parameter approach
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY&symbol=IBM&interval=5min&apikey={apiKey}&datatype=json");

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(ct);

            // Alpha Vantage returns JSON even for invalid keys but with error messages
            if (content.Contains("Invalid API call") || content.Contains("Error Message"))
            {
                return (CredentialAuthStatus.Invalid, "Invalid API key or rate limit exceeded", null);
            }

            if (content.Contains("\"Note\":"))
            {
                return (CredentialAuthStatus.Valid, "API key valid but rate limited (5 calls/min)", null);
            }

            return (CredentialAuthStatus.Valid, "Alpha Vantage credentials are valid", null);
        }

        return (CredentialAuthStatus.TestFailed, $"Unexpected response: {response.StatusCode}", null);
    }

    private async Task<(CredentialAuthStatus status, string message, DateTimeOffset? expiresAt)>
        TestNasdaqDataLinkCredentialsAsync(string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://data.nasdaq.com/api/v3/datasets.json?api_key={apiKey}&per_page=1");

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return (CredentialAuthStatus.Valid, "Nasdaq Data Link credentials are valid", null);
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => (CredentialAuthStatus.Invalid, "Invalid API key", null),
            System.Net.HttpStatusCode.Forbidden => (CredentialAuthStatus.Invalid, "Access forbidden", null),
            _ => (CredentialAuthStatus.TestFailed, $"Unexpected response: {response.StatusCode}", null)
        };
    }



    private CredentialAuthStatus CheckExpirationStatus(DateTimeOffset expiresAt)
    {
        var daysUntilExpiration = (expiresAt - DateTimeOffset.UtcNow).TotalDays;

        if (daysUntilExpiration <= 0)
            return CredentialAuthStatus.Expired;

        if (daysUntilExpiration <= _expirationConfig.CriticalDaysBeforeExpiration)
            return CredentialAuthStatus.ExpiringSoon;

        if (daysUntilExpiration <= _expirationConfig.WarnDaysBeforeExpiration)
            return CredentialAuthStatus.ExpiringSoon;

        return CredentialAuthStatus.Valid;
    }

    private static bool SupportsOAuthRefresh(string providerName)
    {
        // Currently, most financial data providers use API keys rather than OAuth
        // This can be extended when OAuth providers are added
        return providerName.ToLowerInvariant() switch
        {
            // Add OAuth-supporting providers here
            _ => false
        };
    }

    private static string MaskCredential(string credential)
    {
        if (string.IsNullOrEmpty(credential) || credential.Length < 8)
            return "****";

        return credential[..4] + new string('*', credential.Length - 8) + credential[^4..];
    }

    private void UpdateCache(CredentialTestResult result)
    {
        var status = new StoredCredentialStatus(
            ProviderName: result.ProviderName,
            LastSuccessfulAuth: result.Status == CredentialAuthStatus.Valid ? result.TestedAt : GetLastSuccessfulAuth(result.ProviderName),
            LastTestResult: result.Status,
            LastTestedAt: result.TestedAt,
            ConsecutiveFailures: result.IsSuccess ? 0 : (GetCachedStatus(result.ProviderName)?.ConsecutiveFailures ?? 0) + 1,
            ExpiresAt: result.ExpiresAt
        );

        _statusCache[result.ProviderName] = status;
    }

    private void LoadPersistedStatus()
    {
        try
        {
            if (!File.Exists(_statusPersistencePath))
                return;

            var json = File.ReadAllText(_statusPersistencePath);
            var statuses = JsonSerializer.Deserialize<Dictionary<string, StoredCredentialStatus>>(json);

            if (statuses != null)
            {
                foreach (var kvp in statuses)
                {
                    _statusCache[kvp.Key] = kvp.Value;
                }
            }

            _log.Debug("Loaded credential status for {Count} providers", _statusCache.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load persisted credential status");
        }
    }

    private async Task PersistStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_statusPersistencePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var statuses = _statusCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var json = JsonSerializer.Serialize(statuses, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(_statusPersistencePath, json);
            _log.Debug("Persisted credential status for {Count} providers", statuses.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to persist credential status");
        }
    }


    public async ValueTask DisposeAsync()
    {
        await PersistStatusAsync();
        _testLock.Dispose();
        _httpClient.Dispose();
    }
}
