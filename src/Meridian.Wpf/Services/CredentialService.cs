using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Credentials;
using Meridian.Ui.Services;
using HttpClientFactoryProvider = Meridian.Ui.Services.HttpClientFactoryProvider;
using HttpClientNames = Meridian.Ui.Services.HttpClientNames;

namespace Meridian.Wpf.Services;

/// <summary>
/// Types of credential operations that can fail.
/// </summary>
public enum CredentialOperation : byte
{
    PromptCredentials,
    PromptApiKey,
    Save,
    Retrieve,
    Remove,
    ListAll,
    Test,
    Refresh
}

/// <summary>
/// Severity levels for credential errors.
/// </summary>
public enum CredentialErrorSeverity : byte
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Event args for credential operation errors.
/// </summary>
public sealed class CredentialErrorEventArgs : EventArgs
{
    public CredentialOperation Operation { get; }
    public string? Resource { get; }
    public string Message { get; }
    public Exception? Exception { get; }
    public CredentialErrorSeverity Severity { get; }
    public DateTime Timestamp { get; }

    public CredentialErrorEventArgs(
        CredentialOperation operation,
        string? resource,
        string message,
        Exception? exception = null,
        CredentialErrorSeverity severity = CredentialErrorSeverity.Error,
        DateTime timestamp = default)
    {
        Operation = operation;
        Resource = resource;
        Message = message;
        Exception = exception;
        Severity = severity;
        Timestamp = timestamp == default ? DateTime.UtcNow : timestamp;
    }
}

/// <summary>
/// Event args for credential metadata updates.
/// </summary>
public sealed class CredentialMetadataEventArgs : EventArgs
{
    public string Resource { get; }
    public CredentialMetadata Metadata { get; }

    public CredentialMetadataEventArgs(string resource, CredentialMetadata metadata)
    {
        Resource = resource;
        Metadata = metadata;
    }
}

/// <summary>
/// Event args for credential expiration warnings.
/// </summary>
public sealed class CredentialExpirationEventArgs : EventArgs
{
    public string Resource { get; }
    public DateTime ExpiresAt { get; }
    public TimeSpan TimeRemaining => ExpiresAt - DateTime.UtcNow;

    public CredentialExpirationEventArgs(string resource, DateTime expiresAt)
    {
        Resource = resource;
        ExpiresAt = expiresAt;
    }
}

/// <summary>
/// Service for secure credential management using DPAPI-encrypted file storage.
/// Enhanced with OAuth support, expiration tracking, and credential testing capabilities.
/// </summary>
public sealed class CredentialService : IDisposable
{
    private const string ResourcePrefix = "Meridian";
    private const string CredentialVaultFileName = "credentials.enc";
    private const string MetadataFileName = "credential_metadata.json";
    private const string LogPrefix = "[CredentialService]";

    // Credential resource names
    public const string AlpacaCredentialResource = $"{ResourcePrefix}.Alpaca";
    public const string NasdaqApiKeyResource = $"{ResourcePrefix}.NasdaqDataLink";
    public const string OpenFigiApiKeyResource = $"{ResourcePrefix}.OpenFigi";
    public const string OAuthTokenResource = $"{ResourcePrefix}.OAuth";

    // Alpaca API endpoints for testing
    private const string AlpacaPaperBaseUrl = "https://paper-api.alpaca.markets";
    private const string AlpacaLiveBaseUrl = "https://api.alpaca.markets";

    private readonly HttpClient _httpClient;
    private readonly string _vaultPath;
    private readonly string _metadataPath;
    private readonly object _vaultLock = new();
    private readonly object _metadataLock = new();
    private Dictionary<string, StoredCredential> _vault = new();
    private Dictionary<string, CredentialMetadata> _metadataCache = new();
    private bool _disposed;
    private bool _metadataLoaded;

    /// <summary>
    /// Event raised when credential metadata is updated.
    /// </summary>
    public event EventHandler<CredentialMetadataEventArgs>? MetadataUpdated;

    /// <summary>
    /// Event raised when a credential is about to expire.
    /// </summary>
    public event EventHandler<CredentialExpirationEventArgs>? CredentialExpiring;

    /// <summary>
    /// Event raised when a credential operation fails.
    /// </summary>
    public event EventHandler<CredentialErrorEventArgs>? CredentialError;

    // Telemetry counters
    private int _credentialRetrievalFailures;
    private int _credentialSaveFailures;
    private int _credentialRemovalFailures;
    private int _vaultAccessFailures;

    public (int RetrievalFailures, int SaveFailures, int RemovalFailures, int VaultAccessFailures) GetTelemetryCounters()
        => (_credentialRetrievalFailures, _credentialSaveFailures, _credentialRemovalFailures, _vaultAccessFailures);

    public CredentialService()
    {
        _httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.CredentialTest);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        Directory.CreateDirectory(appDataDir);

        _vaultPath = Path.Combine(appDataDir, CredentialVaultFileName);
        _metadataPath = Path.Combine(appDataDir, MetadataFileName);

        LoadVault();
    }

    /// <summary>
    /// Initializes the credential service by loading metadata.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_metadataLoaded)
            return;
        await LoadMetadataAsync();
        _metadataLoaded = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _httpClient.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }


    private sealed class StoredCredential
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    private void LoadVault()
    {
        lock (_vaultLock)
        {
            try
            {
                if (File.Exists(_vaultPath))
                {
                    var encrypted = File.ReadAllBytes(_vaultPath);
                    var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(decrypted);
                    _vault = JsonSerializer.Deserialize<Dictionary<string, StoredCredential>>(json) ?? new();
                }
            }
            catch (Exception)
            {
                _vault = new();
            }
        }
    }

    private void SaveVault()
    {
        lock (_vaultLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_vault);
                var bytes = Encoding.UTF8.GetBytes(json);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_vaultPath, encrypted);
            }
            catch (Exception)
            {
            }
        }
    }



    /// <summary>
    /// Saves username/password credentials to the encrypted credential vault.
    /// </summary>
    public void SaveCredential(string resource, string username, string password)
    {
        try
        {
            lock (_vaultLock)
            {
                _vault[resource] = new StoredCredential { Username = username, Password = password };
            }
            SaveVault();
            LogCredentialOperation("SaveCredential", resource, true);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _credentialSaveFailures);
            RaiseCredentialError(CredentialOperation.Save, resource, ex.Message, ex);
        }
    }

    /// <summary>
    /// Saves an API key (single value) to the credential vault.
    /// </summary>
    public void SaveApiKey(string resource, string apiKey)
    {
        SaveCredential(resource, "apikey", apiKey);
    }

    /// <summary>
    /// Retrieves credentials from the credential vault.
    /// </summary>
    public (string Username, string Password)? GetCredential(string resource)
    {
        try
        {
            lock (_vaultLock)
            {
                if (_vault.TryGetValue(resource, out var cred))
                {
                    LogCredentialOperation("GetCredential", resource, true);
                    return (cred.Username, cred.Password);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _credentialRetrievalFailures);
            RaiseCredentialError(CredentialOperation.Retrieve, resource, ex.Message, ex);
            return null;
        }
    }

    /// <summary>
    /// Retrieves an API key from the credential vault.
    /// </summary>
    public string? GetApiKey(string resource)
    {
        return GetCredential(resource)?.Password;
    }

    /// <summary>
    /// Removes a credential from the vault.
    /// </summary>
    public void RemoveCredential(string resource)
    {
        try
        {
            bool removed;
            lock (_vaultLock)
            {
                removed = _vault.Remove(resource);
            }

            if (removed)
            {
                SaveVault();
                LogCredentialOperation("RemoveCredential", resource, true);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _credentialRemovalFailures);
            RaiseCredentialError(CredentialOperation.Remove, resource, ex.Message, ex);
        }
    }

    /// <summary>
    /// Checks if a credential exists in the vault.
    /// </summary>
    public bool HasCredential(string resource)
    {
        lock (_vaultLock)
        {
            return _vault.ContainsKey(resource);
        }
    }

    /// <summary>
    /// Gets all stored credential resource names for this application.
    /// </summary>
    public IReadOnlyList<string> GetAllStoredResources()
    {
        try
        {
            lock (_vaultLock)
            {
                return _vault.Keys
                    .Where(k => k.StartsWith(ResourcePrefix))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _vaultAccessFailures);
            RaiseCredentialError(CredentialOperation.ListAll, ResourcePrefix, ex.Message, ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Removes all stored credentials for this application.
    /// </summary>
    public void RemoveAllCredentials()
    {
        var resources = GetAllStoredResources();
        foreach (var resource in resources)
        {
            RemoveCredential(resource);
        }
    }



    /// <summary>
    /// Gets stored Alpaca credentials.
    /// </summary>
    public (string KeyId, string SecretKey)? GetAlpacaCredentials()
    {
        var credential = GetCredential(AlpacaCredentialResource);
        return credential.HasValue ? (credential.Value.Username, credential.Value.Password) : null;
    }

    /// <summary>
    /// Saves Alpaca credentials.
    /// </summary>
    public void SaveAlpacaCredentials(string keyId, string secretKey)
    {
        SaveCredential(AlpacaCredentialResource, keyId, secretKey);
    }

    /// <summary>
    /// Checks if Alpaca credentials are stored.
    /// </summary>
    public bool HasAlpacaCredentials()
    {
        return HasCredential(AlpacaCredentialResource);
    }

    /// <summary>
    /// Removes stored Alpaca credentials.
    /// </summary>
    public void RemoveAlpacaCredentials()
    {
        RemoveCredential(AlpacaCredentialResource);
    }



    public string? GetNasdaqApiKey() => GetApiKey(NasdaqApiKeyResource);
    public void SaveNasdaqApiKey(string apiKey) => SaveApiKey(NasdaqApiKeyResource, apiKey);



    public string? GetOpenFigiApiKey() => GetApiKey(OpenFigiApiKeyResource);
    public void SaveOpenFigiApiKey(string apiKey) => SaveApiKey(OpenFigiApiKeyResource, apiKey);



    private async Task LoadMetadataAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_metadataPath))
            {
                var json = await File.ReadAllTextAsync(_metadataPath);
                var metadata = JsonSerializer.Deserialize<Dictionary<string, CredentialMetadata>>(json);
                if (metadata != null)
                {
                    lock (_metadataLock)
                    {
                        _metadataCache = metadata;
                    }
                }
            }
        }
        catch (Exception)
        {
            lock (_metadataLock)
            {
                _metadataCache = new Dictionary<string, CredentialMetadata>();
            }
        }
    }

    private async Task SaveMetadataAsync(CancellationToken ct = default)
    {
        try
        {
            Dictionary<string, CredentialMetadata> snapshot;
            lock (_metadataLock)
            {
                snapshot = new Dictionary<string, CredentialMetadata>(_metadataCache);
            }

            var json = JsonSerializer.Serialize(snapshot, DesktopJsonOptions.PrettyPrint);
            await File.WriteAllTextAsync(_metadataPath, json);
        }
        catch (Exception)
        {
        }
    }

    public CredentialMetadata? GetMetadata(string resource)
    {
        ThrowIfDisposed();
        lock (_metadataLock)
        {
            return _metadataCache.TryGetValue(resource, out var metadata) ? metadata : null;
        }
    }

    public async Task UpdateMetadataAsync(string resource, Action<CredentialMetadata> update, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        CredentialMetadata metadata;
        lock (_metadataLock)
        {
            if (!_metadataCache.TryGetValue(resource, out metadata!))
            {
                metadata = new CredentialMetadata { Resource = resource };
                _metadataCache[resource] = metadata;
            }
            update(metadata);
        }

        await SaveMetadataAsync();
        MetadataUpdated?.Invoke(this, new CredentialMetadataEventArgs(resource, metadata));

        if (metadata.ExpiresAt.HasValue)
        {
            var remaining = metadata.ExpiresAt.Value - DateTime.UtcNow;
            if (remaining.TotalDays <= 7 && remaining.TotalSeconds > 0)
            {
                CredentialExpiring?.Invoke(this, new CredentialExpirationEventArgs(resource, metadata.ExpiresAt.Value));
            }
        }
    }

    public async Task RecordAuthenticationAsync(string resource, CancellationToken ct = default)
    {
        await UpdateMetadataAsync(resource, m =>
        {
            m.LastAuthenticatedAt = DateTime.UtcNow;
            m.TestStatus = CredentialTestStatus.Success;
        });
    }

    public List<CredentialInfo> GetAllCredentialsWithMetadata()
    {
        var credentials = new List<CredentialInfo>();
        var resources = GetAllStoredResources();

        foreach (var resource in resources)
        {
            var metadata = GetMetadata(resource);
            var (name, credType) = GetCredentialDisplayInfo(resource);

            credentials.Add(new CredentialInfo
            {
                Name = name,
                Resource = resource,
                Status = GetCredentialStatusDisplay(resource, metadata),
                CredentialType = credType,
                ExpiresAt = metadata?.ExpiresAt,
                LastAuthenticatedAt = metadata?.LastAuthenticatedAt,
                LastTestedAt = metadata?.LastTestedAt,
                TestStatus = metadata?.TestStatus ?? CredentialTestStatus.Unknown,
                CanAutoRefresh = metadata?.AutoRefreshEnabled ?? false,
                RefreshToken = metadata?.RefreshToken
            });
        }

        return credentials;
    }

    private static (string Name, CredentialType Type) GetCredentialDisplayInfo(string resource)
    {
        return resource switch
        {
            var r when r.Contains("Alpaca") => ("Alpaca API Credentials", CredentialType.ApiKeyWithSecret),
            var r when r.Contains("NasdaqDataLink") => ("Nasdaq Data Link API Key", CredentialType.ApiKey),
            var r when r.Contains("OpenFigi") => ("OpenFIGI API Key", CredentialType.ApiKey),
            var r when r.Contains("OAuth") => ("OAuth Token", CredentialType.OAuth2Token),
            _ => (resource.Replace(ResourcePrefix + ".", ""), CredentialType.ApiKey)
        };
    }

    private static string GetCredentialStatusDisplay(string resource, CredentialMetadata? metadata)
    {
        if (metadata == null)
            return "Active";

        if (metadata.ExpiresAt.HasValue)
        {
            if (metadata.ExpiresAt.Value <= DateTime.UtcNow)
                return "Expired";
            if (metadata.ExpiresAt.Value <= DateTime.UtcNow.AddDays(1))
                return "Expires soon";
        }

        if (metadata.LastAuthenticatedAt.HasValue)
        {
            var elapsed = DateTime.UtcNow - metadata.LastAuthenticatedAt.Value;
            if (elapsed.TotalHours < 1)
                return "Active - Just used";
            if (elapsed.TotalHours < 24)
                return $"Active - Used {(int)elapsed.TotalHours}h ago";
            return $"Active - Used {(int)elapsed.TotalDays}d ago";
        }

        return "Active";
    }

    public List<CredentialInfo> GetExpiringCredentials(int withinDays = 7)
    {
        return GetAllCredentialsWithMetadata()
            .Where(c => c.IsExpiringSoon || c.IsExpired)
            .ToList();
    }



    public async Task<CredentialTestResult> TestAlpacaCredentialsAsync(bool useSandbox = false, CancellationToken ct = default)
    {
        var credentials = GetAlpacaCredentials();
        if (credentials == null)
            return CredentialTestResult.CreateFailure("No Alpaca credentials stored");

        return await TestAlpacaCredentialsAsync(credentials.Value.KeyId, credentials.Value.SecretKey, useSandbox);
    }

    public async Task<CredentialTestResult> TestAlpacaCredentialsAsync(string keyId, string secretKey, bool useSandbox = false, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var baseUrl = useSandbox ? AlpacaPaperBaseUrl : AlpacaLiveBaseUrl;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/account");
            request.Headers.Add("APCA-API-KEY-ID", keyId);
            request.Headers.Add("APCA-API-SECRET-KEY", secretKey);

            var response = await _httpClient.SendAsync(request);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var account = JsonSerializer.Deserialize<JsonElement>(content);

                await UpdateMetadataAsync(AlpacaCredentialResource, m =>
                {
                    m.LastTestedAt = DateTime.UtcNow;
                    m.LastAuthenticatedAt = DateTime.UtcNow;
                    m.TestStatus = CredentialTestStatus.Success;
                    m.CredentialType = CredentialType.ApiKeyWithSecret;
                });

                var accountStatus = account.TryGetProperty("status", out var status) ? status.GetString() : "Unknown";
                return new CredentialTestResult
                {
                    Success = true,
                    Message = $"Authentication successful. Account status: {accountStatus}",
                    ResponseTimeMs = sw.ElapsedMilliseconds,
                    TestedAt = DateTime.UtcNow,
                    ServerInfo = useSandbox ? "Alpaca Paper Trading" : "Alpaca Live Trading"
                };
            }

            var errorMessage = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Invalid API key or secret",
                System.Net.HttpStatusCode.Forbidden => "API key does not have required permissions",
                _ => $"API returned {(int)response.StatusCode}: {response.ReasonPhrase}"
            };

            await UpdateMetadataAsync(AlpacaCredentialResource, m =>
            {
                m.LastTestedAt = DateTime.UtcNow;
                m.TestStatus = CredentialTestStatus.Failed;
            });

            return CredentialTestResult.CreateFailure(errorMessage);
        }
        catch (TaskCanceledException)
        {
            return CredentialTestResult.CreateFailure("Connection timed out");
        }
        catch (HttpRequestException ex)
        {
            return CredentialTestResult.CreateFailure($"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return CredentialTestResult.CreateFailure($"Error: {ex.Message}");
        }
    }

    public async Task<CredentialTestResult> TestNasdaqApiKeyAsync(CancellationToken ct = default)
    {
        var apiKey = GetNasdaqApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return CredentialTestResult.CreateFailure("No Nasdaq Data Link API key stored");

        var sw = Stopwatch.StartNew();
        try
        {
            var testUrl = $"https://data.nasdaq.com/api/v3/datasets.json?api_key={apiKey}&per_page=1";
            var response = await _httpClient.GetAsync(testUrl);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                await UpdateMetadataAsync(NasdaqApiKeyResource, m =>
                {
                    m.LastTestedAt = DateTime.UtcNow;
                    m.LastAuthenticatedAt = DateTime.UtcNow;
                    m.TestStatus = CredentialTestStatus.Success;
                    m.CredentialType = CredentialType.ApiKey;
                });

                return new CredentialTestResult
                {
                    Success = true,
                    Message = "API key validated successfully",
                    ResponseTimeMs = sw.ElapsedMilliseconds,
                    TestedAt = DateTime.UtcNow,
                    ServerInfo = "Nasdaq Data Link"
                };
            }

            var errorMessage = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Invalid API key",
                System.Net.HttpStatusCode.Forbidden => "API key does not have required permissions",
                (System.Net.HttpStatusCode)429 => "Rate limit exceeded",
                _ => $"API returned {(int)response.StatusCode}"
            };

            await UpdateMetadataAsync(NasdaqApiKeyResource, m =>
            {
                m.LastTestedAt = DateTime.UtcNow;
                m.TestStatus = CredentialTestStatus.Failed;
            });

            return CredentialTestResult.CreateFailure(errorMessage);
        }
        catch (Exception ex)
        {
            return CredentialTestResult.CreateFailure($"Error: {ex.Message}");
        }
    }

    public async Task<CredentialTestResult> TestOpenFigiApiKeyAsync(CancellationToken ct = default)
    {
        var apiKey = GetOpenFigiApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return CredentialTestResult.CreateFailure("No OpenFIGI API key stored");

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openfigi.com/v3/mapping");
            request.Headers.Add("X-OPENFIGI-APIKEY", apiKey);
            request.Content = new StringContent(
                "[{\"idType\":\"TICKER\",\"idValue\":\"AAPL\"}]",
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                await UpdateMetadataAsync(OpenFigiApiKeyResource, m =>
                {
                    m.LastTestedAt = DateTime.UtcNow;
                    m.LastAuthenticatedAt = DateTime.UtcNow;
                    m.TestStatus = CredentialTestStatus.Success;
                    m.CredentialType = CredentialType.ApiKey;
                });

                return new CredentialTestResult
                {
                    Success = true,
                    Message = "API key validated successfully",
                    ResponseTimeMs = sw.ElapsedMilliseconds,
                    TestedAt = DateTime.UtcNow,
                    ServerInfo = "OpenFIGI API v3"
                };
            }

            await UpdateMetadataAsync(OpenFigiApiKeyResource, m =>
            {
                m.LastTestedAt = DateTime.UtcNow;
                m.TestStatus = CredentialTestStatus.Failed;
            });

            return CredentialTestResult.CreateFailure($"API returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return CredentialTestResult.CreateFailure($"Error: {ex.Message}");
        }
    }

    public async Task<CredentialTestResult> TestCredentialAsync(string resource, CancellationToken ct = default)
    {
        return resource switch
        {
            var r when r.Contains("Alpaca") => await TestAlpacaCredentialsAsync(),
            var r when r.Contains("NasdaqDataLink") => await TestNasdaqApiKeyAsync(),
            var r when r.Contains("OpenFigi") => await TestOpenFigiApiKeyAsync(),
            _ => CredentialTestResult.CreateFailure($"No test available for {resource}")
        };
    }

    public async Task<Dictionary<string, CredentialTestResult>> TestAllCredentialsAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, CredentialTestResult>();
        var resources = GetAllStoredResources();

        foreach (var resource in resources)
        {
            results[resource] = await TestCredentialAsync(resource);
        }

        return results;
    }



    public async Task SaveOAuthTokenAsync(
        string providerId,
        string accessToken,
        string? refreshToken,
        DateTime expiresAt,
        string? tokenEndpoint = null,
        string? clientId = null, CancellationToken ct = default)
    {
        var resource = $"{OAuthTokenResource}.{providerId}";
        SaveCredential(resource, "oauth", accessToken);

        await UpdateMetadataAsync(resource, m =>
        {
            m.CredentialType = CredentialType.OAuth2Token;
            m.ExpiresAt = expiresAt;
            m.RefreshToken = refreshToken;
            m.TokenEndpoint = tokenEndpoint;
            m.ClientId = clientId;
            m.AutoRefreshEnabled = !string.IsNullOrEmpty(refreshToken);
            m.CreatedAt = DateTime.UtcNow;
        });
    }

    public string? GetOAuthToken(string providerId)
    {
        var resource = $"{OAuthTokenResource}.{providerId}";
        return GetCredential(resource)?.Password;
    }

    public async Task<bool> RefreshOAuthTokenAsync(string providerId, CancellationToken ct = default)
    {
        var resource = $"{OAuthTokenResource}.{providerId}";
        var metadata = GetMetadata(resource);

        if (metadata == null || string.IsNullOrEmpty(metadata.RefreshToken) ||
            string.IsNullOrEmpty(metadata.TokenEndpoint))
        {
            return false;
        }

        try
        {
            await UpdateMetadataAsync(resource, m => m.LastRefreshAttemptAt = DateTime.UtcNow);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = metadata.RefreshToken,
                ["client_id"] = metadata.ClientId ?? ""
            });

            var response = await _httpClient.PostAsync(metadata.TokenEndpoint, content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(json);

                if (tokenResponse != null)
                {
                    await SaveOAuthTokenAsync(
                        providerId,
                        tokenResponse.AccessToken,
                        tokenResponse.RefreshToken ?? metadata.RefreshToken,
                        tokenResponse.GetExpirationTime(),
                        metadata.TokenEndpoint,
                        metadata.ClientId);

                    await UpdateMetadataAsync(resource, m =>
                    {
                        m.RefreshFailureCount = 0;
                        m.LastAuthenticatedAt = DateTime.UtcNow;
                    });

                    return true;
                }
            }

            await UpdateMetadataAsync(resource, m => m.RefreshFailureCount++);
            return false;
        }
        catch (Exception)
        {
            await UpdateMetadataAsync(resource, m => m.RefreshFailureCount++);
            return false;
        }
    }

    public async Task<bool> EnsureTokenValidAsync(string providerId, int refreshThresholdMinutes = 5, CancellationToken ct = default)
    {
        var resource = $"{OAuthTokenResource}.{providerId}";
        var metadata = GetMetadata(resource);

        if (metadata == null)
            return false;

        if (!metadata.ExpiresAt.HasValue)
            return true;

        var remaining = metadata.ExpiresAt.Value - DateTime.UtcNow;
        if (remaining.TotalMinutes > refreshThresholdMinutes)
            return true;

        if (remaining.TotalSeconds <= 0)
        {
            if (metadata.AutoRefreshEnabled)
                return await RefreshOAuthTokenAsync(providerId);
            return false;
        }

        if (metadata.AutoRefreshEnabled)
            return await RefreshOAuthTokenAsync(providerId);

        return true;
    }



    private void LogCredentialOperation(string operation, string resource, bool success, string? details = null)
    {
        var timestamp = DateTime.UtcNow.ToString("o");
        if (success)
        {
        }
        else
        {
            var detailInfo = !string.IsNullOrEmpty(details) ? $", Details={details}" : "";
        }
    }

    private void RaiseCredentialError(CredentialOperation operation, string? resource, string message, Exception? ex = null, CredentialErrorSeverity severity = CredentialErrorSeverity.Error)
    {
        LogCredentialOperation(operation.ToString(), resource ?? "N/A", false, message);
        CredentialError?.Invoke(this, new CredentialErrorEventArgs(operation, resource, message, ex, severity, DateTime.UtcNow));
    }

}
