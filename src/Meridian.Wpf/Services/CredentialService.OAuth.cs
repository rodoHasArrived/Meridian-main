using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Credentials;
using Meridian.Core.IO;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Wpf.Services;

public sealed partial class CredentialService
{
    private const int MaxOAuthResponseBytes = 64 * 1024;
    private static readonly TimeSpan OAuthRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OAuthRefreshLeaseTimeout = TimeSpan.FromSeconds(25);

    // TradeStation's documented refresh grant supports the public PKCE client used here.
    // Add approvals by provider and exact endpoint when the matching grant is supported.
    private static readonly Uri TradeStationTokenEndpoint = new("https://signin.tradestation.com/oauth/token");

    public async Task SaveOAuthTokenAsync(
        string providerId,
        string accessToken,
        string? refreshToken,
        DateTime expiresAt,
        string? tokenEndpoint = null,
        string? clientId = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (providerId.EndsWith(OAuthRefreshTokenSuffix, StringComparison.Ordinal))
            throw new ArgumentException("The OAuth provider ID collides with the refresh-token resource.", nameof(providerId));

        var resource = $"{OAuthTokenResource}.{providerId}";
        var metadata = new CredentialMetadata
        {
            Resource = resource,
            CredentialType = CredentialType.OAuth2Token,
            ExpiresAt = expiresAt,
            TokenEndpoint = tokenEndpoint,
            ClientId = clientId,
            AutoRefreshEnabled = !string.IsNullOrWhiteSpace(refreshToken)
        };
        using (await AcquireVaultWriteLeaseAsync(ct).ConfigureAwait(false))
        {
            lock (_vaultLock)
            {
                var vault = ReadVaultForWrite();
                StoreOAuthPair(vault, resource, accessToken, refreshToken, metadata);
                PublishVault(vault, ct);
            }
        }

        NotifyOAuthMetadata(resource, metadata);
    }

    public string? GetOAuthToken(string providerId)
    {
        LoadVault();
        return GetCredential($"{OAuthTokenResource}.{providerId}")?.Password;
    }

    public async Task<bool> RefreshOAuthTokenAsync(string providerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(providerId) || providerId.EndsWith(OAuthRefreshTokenSuffix, StringComparison.Ordinal))
            return false;

        var provider = ProviderIdentity.NormalizeId(providerId);
        var resource = $"{OAuthTokenResource}.{providerId}";
        var requestedAt = DateTime.UtcNow;
        try
        {
            if (provider != "tradestation")
                return await RefreshCurrentOAuthTokenAsync(resource, null, requestedAt, ct).ConfigureAwait(false);

            var lockPath = $"{_vaultPath}.oauth.{provider}.lock";
            FileStream lease;
            try
            {
                // A refusal means another process or service instance owns this request.
                lease = ExclusiveFileLock.Acquire(lockPath, TimeSpan.Zero);
            }
            catch (TimeoutException)
            {
                using var joined = await ExclusiveFileLock.AcquireAsync(lockPath, OAuthRefreshLeaseTimeout, ct).ConfigureAwait(false);
                var retained = await ReadOAuthSnapshotAsync(resource, ct).ConfigureAwait(false);
                return retained.Access is not null && retained.Metadata is { RefreshFailureCount: 0, LastRefreshAttemptAt: not null } metadata
                    && metadata.LastAuthenticatedAt >= requestedAt && metadata.ExpiresAt > DateTime.UtcNow;
            }

            using (lease)
            {
                return await RefreshCurrentOAuthTokenAsync(resource, TradeStationTokenEndpoint, requestedAt, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or TimeoutException)
        {
            LoggingService.Instance.LogWarning("OAuth credential refresh storage operation failed.", ("exception", ex.GetType().Name));
            return false;
        }
    }

    private async Task<bool> RefreshCurrentOAuthTokenAsync(string resource, Uri? approvedEndpoint, DateTime requestedAt, CancellationToken ct)
    {
        var snapshot = await ReadOAuthSnapshotAsync(resource, ct).ConfigureAwait(false);
        if (snapshot.Metadata is not { } metadata || string.IsNullOrWhiteSpace(snapshot.Refresh?.Password)
            || string.IsNullOrWhiteSpace(metadata.TokenEndpoint))
            return false;

        OAuthTokenResponse? response = null;
        if (approvedEndpoint is not null &&
            Uri.TryCreate(metadata.TokenEndpoint, UriKind.Absolute, out var configured) &&
            string.Equals(configured.AbsoluteUri, approvedEndpoint.AbsoluteUri, StringComparison.Ordinal))
        {
            // Equal null generations represent a complete pre-migration pair. A manual
            // replacement of only one member creates a generation mismatch.
            if (!snapshot.HasCompletePair)
                return false;

            response = await RequestOAuthRefreshAsync(approvedEndpoint, snapshot.Refresh.Password, metadata.ClientId, ct)
                .ConfigureAwait(false);
        }

        // A successfully parsed rotation may already have invalidated the old refresh token.
        // Finish its bounded local commit even during shutdown. Publication still requires the
        // exact credential generation used for the request.
        return await PublishOAuthRefreshAsync(resource, snapshot, response, requestedAt, response?.GetExpirationTime(),
            response is null ? ct : CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<OAuthTokenResponse?> RequestOAuthRefreshAsync(Uri endpoint, string refreshToken, string? clientId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OAuthRequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = clientId ?? ""
                })
            };
            using var response = await _oauthHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = response.Content;
            await content.LoadIntoBufferAsync(MaxOAuthResponseBytes, timeout.Token).ConfigureAwait(false);
            using var body = await content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var tokens = await JsonSerializer.DeserializeAsync<OAuthTokenResponse>(body, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            return tokens is { ExpiresInSeconds: > 0 }
                && !string.IsNullOrWhiteSpace(tokens.AccessToken)
                && string.Equals(tokens.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
                && (tokens.RefreshToken is null || !string.IsNullOrWhiteSpace(tokens.RefreshToken))
                ? tokens
                : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            // The response and exception text can echo secrets.
            return null;
        }
    }

    private async Task<OAuthSnapshot> ReadOAuthSnapshotAsync(string resource, CancellationToken ct)
    {
        using var lease = await AcquireVaultWriteLeaseAsync(ct).ConfigureAwait(false);
        lock (_vaultLock)
        {
            _vault = ReadVault();
            _vaultLoadFailed = false;
            return ReadOAuthSnapshot(_vault, resource);
        }
    }

    private OAuthSnapshot ReadOAuthSnapshot(Dictionary<string, StoredCredential> vault, string resource)
    {
        var access = vault.GetValueOrDefault(resource);
        var refresh = vault.GetValueOrDefault(resource + OAuthRefreshTokenSuffix);
        var metadata = access?.OAuthMetadata ?? ReadLegacyMetadata().GetValueOrDefault(resource);
        return new(access, refresh, metadata);
    }

    private async Task<bool> PublishOAuthRefreshAsync(
        string resource, OAuthSnapshot snapshot, OAuthTokenResponse? response,
        DateTime requestedAt, DateTime? expiresAt, CancellationToken ct)
    {
        CredentialMetadata metadata;
        using (await AcquireVaultWriteLeaseAsync(ct).ConfigureAwait(false))
        {
            lock (_vaultLock)
            {
                var vault = ReadVault();
                _vaultLoadFailed = false;
                var current = ReadOAuthSnapshot(vault, resource);
                if (current.Metadata is null || !snapshot.Matches(current))
                {
                    _vault = vault;
                    return false;
                }

                metadata = current.Metadata;
                metadata.LastRefreshAttemptAt = requestedAt;
                if (response is not null)
                {
                    metadata.ExpiresAt = expiresAt;
                    metadata.LastAuthenticatedAt = DateTime.UtcNow;
                    metadata.RefreshFailureCount = 0;
                    StoreOAuthPair(vault, resource, response.AccessToken, response.RefreshToken ?? snapshot.Refresh!.Password, metadata);
                    PublishVault(vault, ct);
                }
                else
                {
                    metadata.RefreshFailureCount++;
                    if (current.Access is not null)
                    {
                        vault[resource] = current.Access with { OAuthMetadata = metadata };
                        PublishVault(vault, ct);
                    }
                    else
                    {
                        // An old vault may have an orphaned refresh entry and sidecar metadata.
                        var legacy = ReadLegacyMetadata();
                        legacy[resource] = metadata;
                        SaveLegacyMetadata(legacy, ct);
                    }
                }
            }
        }

        NotifyOAuthMetadata(resource, metadata);
        return response is not null;
    }

    private static void StoreOAuthPair(Dictionary<string, StoredCredential> vault, string resource,
        string accessToken, string? refreshToken, CredentialMetadata metadata)
    {
        var generation = Guid.NewGuid();
        vault[resource] = new StoredCredential
        {
            Username = "oauth",
            Password = accessToken,
            OAuthGeneration = generation,
            OAuthMetadata = metadata
        };
        var refreshResource = resource + OAuthRefreshTokenSuffix;
        if (string.IsNullOrWhiteSpace(refreshToken))
            vault.Remove(refreshResource);
        else
            vault[refreshResource] = new StoredCredential
            {
                Username = "oauth_refresh",
                Password = refreshToken,
                OAuthGeneration = generation
            };
    }

    private void NotifyOAuthMetadata(string resource, CredentialMetadata metadata)
    {
        try
        {
            NotifyMetadataUpdated(resource, metadata);
        }
        catch (Exception ex)
        {
            // An observer cannot change the outcome of a completed vault write.
            LoggingService.Instance.LogWarning("OAuth credential metadata observer failed.", ("exception", ex.GetType().Name));
        }
    }

    public async Task<bool> EnsureTokenValidAsync(string providerId, int refreshThresholdMinutes = 5, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        LoadVault();
        var metadata = GetMetadata($"{OAuthTokenResource}.{providerId}");
        if (metadata is null)
            return false;
        if (metadata.ExpiresAt is not { } expiry)
            return true;

        var remaining = expiry - DateTime.UtcNow;
        if (remaining.TotalMinutes > refreshThresholdMinutes)
            return true;

        return metadata.AutoRefreshEnabled
            ? await RefreshOAuthTokenAsync(providerId, ct).ConfigureAwait(false)
            : remaining.TotalSeconds > 0;
    }

    private static bool IsOAuthResource(string resource)
        => resource.StartsWith(OAuthTokenResource + ".", StringComparison.Ordinal);

    private static bool IsOAuthAccessResource(string resource)
        => IsOAuthResource(resource) && !resource.EndsWith(OAuthRefreshTokenSuffix, StringComparison.Ordinal);

    private sealed record OAuthSnapshot(StoredCredential? Access, StoredCredential? Refresh, CredentialMetadata? Metadata)
    {
        public bool HasCompletePair => Access is not null && Refresh is not null && Access.OAuthGeneration == Refresh.OAuthGeneration;

        public bool Matches(OAuthSnapshot other)
            => SameCredential(Access, other.Access) && SameCredential(Refresh, other.Refresh)
               && string.Equals(Metadata?.TokenEndpoint, other.Metadata?.TokenEndpoint, StringComparison.Ordinal)
               && string.Equals(Metadata?.ClientId, other.Metadata?.ClientId, StringComparison.Ordinal);

        private static bool SameCredential(StoredCredential? left, StoredCredential? right)
            => left?.OAuthGeneration == right?.OAuthGeneration
               && string.Equals(left?.Username, right?.Username, StringComparison.Ordinal)
               && string.Equals(left?.Password, right?.Password, StringComparison.Ordinal);
    }
}
