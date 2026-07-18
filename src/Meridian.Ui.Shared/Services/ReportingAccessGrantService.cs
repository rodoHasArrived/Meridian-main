using System.Security.Cryptography;
using System.Text;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingDerivedAccessGrantCredential(string GrantId, string Token);

/// <summary>
/// Derives a stable, opaque delivery credential without persisting its plaintext bearer. The
/// binding key must be the durable delivery idempotency identity plus its committed attempt.
/// </summary>
public interface IReportingDeliveryGrantCredentialDeriver
{
    ReportingDerivedAccessGrantCredential Derive(string bindingKey);
}

public sealed class HmacReportingDeliveryGrantCredentialDeriver : IReportingDeliveryGrantCredentialDeriver
{
    private const int MinimumKeyBytes = 32;
    private readonly byte[] _key;

    public HmacReportingDeliveryGrantCredentialDeriver(ReadOnlySpan<byte> key)
    {
        if (key.Length < MinimumKeyBytes)
        {
            throw new ArgumentException("Reporting delivery credential keys must contain at least 256 bits.", nameof(key));
        }

        _key = key.ToArray();
    }

    public ReportingDerivedAccessGrantCredential Derive(string bindingKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingKey);
        var normalized = bindingKey.Trim();
        var grantIdBytes = HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes($"meridian-reporting-grant-id\0{normalized}"));
        var tokenBytes = HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes($"meridian-reporting-grant-token\0{normalized}"));
        return new ReportingDerivedAccessGrantCredential(
            $"grant_{Convert.ToHexString(grantIdBytes.AsSpan(0, 16)).ToLowerInvariant()}",
            Convert.ToHexString(tokenBytes).ToLowerInvariant());
    }
}

public sealed class ReportingAccessGrantService
{
    private const int TokenByteCount = 32;
    private const int GrantIdByteCount = 16;
    private const int MaximumConcurrencyAttempts = 8;
    private readonly IReportingAccessGrantStore _store;
    private readonly TimeProvider _timeProvider;

    public ReportingAccessGrantService(
        IReportingAccessGrantStore store,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReportingAccessGrantSecret> IssueAsync(
        ReportingAccessGrantIssueRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = NormalizeRequired(request.TenantId, nameof(request.TenantId));
        var audience = NormalizeRequired(request.Audience, nameof(request.Audience));
        var runId = NormalizeRequired(request.RunId, nameof(request.RunId));
        var packageId = NormalizeRequired(request.PackageId, nameof(request.PackageId));
        ValidateAudienceKind(request.AudienceKind);
        var now = _timeProvider.GetUtcNow();
        if (request.ExpiresAtUtc <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Access grant expiration must be in the future.");
        }

        if (request.MaxUses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Access grant use limit must be positive.");
        }

        var artifactIds = NormalizeArtifactIds(request.ArtifactIds);
        if (!request.AllowPackageRead && artifactIds.Count == 0)
        {
            throw new ArgumentException("An access grant must allow the package or at least one artifact.", nameof(request));
        }

        for (var collisionAttempt = 0; collisionAttempt < 4; collisionAttempt++)
        {
            ct.ThrowIfCancellationRequested();
            var rawToken = RandomNumberGenerator.GetBytes(TokenByteCount);
            var token = Convert.ToHexString(rawToken).ToLowerInvariant();
            var tokenHash = Convert.ToHexString(SHA256.HashData(rawToken)).ToLowerInvariant();
            var grantId = $"grant_{Convert.ToHexString(RandomNumberGenerator.GetBytes(GrantIdByteCount)).ToLowerInvariant()}";
            var grant = new ReportingAccessGrantRecord(
                grantId,
                tokenHash,
                tenantId,
                audience,
                runId,
                packageId,
                request.AllowPackageRead,
                artifactIds,
                now,
                request.ExpiresAtUtc,
                request.MaxUses,
                UseCount: 0,
                AudienceKind: request.AudienceKind);

            if (await _store.TryCreateAsync(grant, ct).ConfigureAwait(false))
            {
                return new ReportingAccessGrantSecret(grantId, token, request.ExpiresAtUtc);
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique reporting access grant identifier.");
    }

    /// <summary>
    /// Creates or reopens the exact same active credential for one durable delivery attempt. A
    /// restart can therefore reconstruct a provider-accepted bearer from stable key material while
    /// the store continues to retain only its SHA-256 hash.
    /// </summary>
    public async Task<ReportingAccessGrantSecret> IssueIdempotentAsync(
        ReportingAccessGrantIssueRequest request,
        ReportingDerivedAccessGrantCredential credential,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credential);
        var tenantId = NormalizeRequired(request.TenantId, nameof(request.TenantId));
        var audience = NormalizeRequired(request.Audience, nameof(request.Audience));
        var runId = NormalizeRequired(request.RunId, nameof(request.RunId));
        var packageId = NormalizeRequired(request.PackageId, nameof(request.PackageId));
        ValidateAudienceKind(request.AudienceKind);
        var grantId = NormalizeRequired(credential.GrantId, nameof(credential.GrantId));
        var rawToken = ParseToken(credential.Token);
        var token = Convert.ToHexString(rawToken).ToLowerInvariant();
        var tokenHash = Convert.ToHexString(SHA256.HashData(rawToken)).ToLowerInvariant();
        var now = _timeProvider.GetUtcNow();
        if (request.ExpiresAtUtc <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Access grant expiration must be in the future.");
        }

        if (request.MaxUses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Access grant use limit must be positive.");
        }

        var artifactIds = NormalizeArtifactIds(request.ArtifactIds);
        if (!request.AllowPackageRead && artifactIds.Count == 0)
        {
            throw new ArgumentException("An access grant must allow the package or at least one artifact.", nameof(request));
        }

        var candidate = new ReportingAccessGrantRecord(
            grantId,
            tokenHash,
            tenantId,
            audience,
            runId,
            packageId,
            request.AllowPackageRead,
            artifactIds,
            now,
            request.ExpiresAtUtc,
            request.MaxUses,
            UseCount: 0,
            AudienceKind: request.AudienceKind);
        if (await _store.TryCreateAsync(candidate, ct).ConfigureAwait(false))
        {
            return new ReportingAccessGrantSecret(grantId, token, candidate.ExpiresAtUtc);
        }

        var existing = await _store.GetAsync(grantId, ct).ConfigureAwait(false)
                       ?? throw new InvalidDataException(
                           "The deterministic access-grant identifier collided without a retained record.");
        EnsureIdempotentGrantMatches(existing, candidate);
        if (existing.RevokedAtUtc is not null
            || existing.ExpiresAtUtc <= now
            || existing.UseCount >= existing.MaxUses)
        {
            throw new InvalidOperationException(
                "The deterministic delivery access grant is no longer active and cannot be reused.");
        }

        return new ReportingAccessGrantSecret(grantId, token, existing.ExpiresAtUtc);
    }

    public async Task<ReportingAccessGrantValidationResult> ValidateAsync(
        ReportingAccessGrantValidationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grantId = NormalizeRequired(request.GrantId, nameof(request.GrantId));
        var tenantId = NormalizeRequired(request.TenantId, nameof(request.TenantId));
        var audience = NormalizeRequired(request.Audience, nameof(request.Audience));
        var runId = NormalizeRequired(request.RunId, nameof(request.RunId));
        var packageId = NormalizeRequired(request.PackageId, nameof(request.PackageId));
        ValidateAudienceKind(request.AudienceKind);
        var artifactId = NormalizeOptional(request.ArtifactId);
        var suppliedTokenHash = HashSuppliedToken(request.Token);

        for (var attempt = 0; attempt < MaximumConcurrencyAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var grant = await _store.GetAsync(grantId, ct).ConfigureAwait(false);
            if (grant is null)
            {
                return new ReportingAccessGrantValidationResult(ReportingAccessGrantValidationStatus.NotFound);
            }

            if (!MatchesStoredHash(grant.TokenHashSha256, suppliedTokenHash))
            {
                return new ReportingAccessGrantValidationResult(ReportingAccessGrantValidationStatus.TokenMismatch);
            }

            // One authority time governs both the state decision and the committed use. Reading
            // the clock again after validation could retain a use whose LastUsedAtUtc crossed the
            // immutable expiry boundary.
            var now = _timeProvider.GetUtcNow();
            var status = ValidateScopeAndState(
                grant,
                tenantId,
                audience,
                runId,
                packageId,
                artifactId,
                now,
                request.AudienceKind);
            if (status != ReportingAccessGrantValidationStatus.Valid)
            {
                return new ReportingAccessGrantValidationResult(status);
            }

            if (!request.ConsumeUse)
            {
                return new ReportingAccessGrantValidationResult(ReportingAccessGrantValidationStatus.Valid, grant);
            }

            var updated = grant with
            {
                UseCount = grant.UseCount + 1,
                LastUsedAtUtc = now,
                Version = grant.Version + 1
            };
            if (await _store.TryUpdateAsync(grant.GrantId, grant.Version, updated, ct).ConfigureAwait(false))
            {
                return new ReportingAccessGrantValidationResult(ReportingAccessGrantValidationStatus.Valid, updated);
            }
        }

        return new ReportingAccessGrantValidationResult(ReportingAccessGrantValidationStatus.ConcurrencyConflict);
    }

    public async Task<bool> RevokeAsync(
        string grantId,
        string tenantId,
        string revokedBy,
        string reason,
        CancellationToken ct = default)
    {
        var normalizedGrantId = NormalizeRequired(grantId, nameof(grantId));
        var normalizedTenantId = NormalizeRequired(tenantId, nameof(tenantId));
        var actor = NormalizeRequired(revokedBy, nameof(revokedBy));
        var normalizedReason = NormalizeRequired(reason, nameof(reason));

        for (var attempt = 0; attempt < MaximumConcurrencyAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var grant = await _store.GetAsync(normalizedGrantId, ct).ConfigureAwait(false);
            if (grant is null || !string.Equals(grant.TenantId, normalizedTenantId, StringComparison.Ordinal))
            {
                return false;
            }

            if (grant.RevokedAtUtc is not null)
            {
                return true;
            }

            var updated = grant with
            {
                RevokedAtUtc = _timeProvider.GetUtcNow(),
                RevokedBy = actor,
                RevocationReason = normalizedReason,
                Version = grant.Version + 1
            };
            if (await _store.TryUpdateAsync(grant.GrantId, grant.Version, updated, ct).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildRecipientAccessUri(
        string accessBaseUri,
        ReportingAccessGrantSecret secret,
        string? artifactId = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var normalizedBaseUri = NormalizeRequired(accessBaseUri, nameof(accessBaseUri));
        if (!Uri.TryCreate(normalizedBaseUri, UriKind.Absolute, out var parsed))
        {
            throw new ArgumentException("Recipient access base URI must be absolute.", nameof(accessBaseUri));
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && !(parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback))
        {
            throw new ArgumentException("Recipient access base URI must use HTTPS outside loopback development.", nameof(accessBaseUri));
        }

        if (!string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            throw new ArgumentException(
                "Recipient access base URI cannot contain credentials, a query, or a fragment.",
                nameof(accessBaseUri));
        }

        var baseText = parsed.AbsoluteUri.TrimEnd('/');
        var fragment = $"token={Uri.EscapeDataString(secret.Token)}";
        if (!string.IsNullOrWhiteSpace(artifactId))
        {
            fragment = $"{fragment}&artifact={Uri.EscapeDataString(artifactId.Trim())}";
        }

        return $"{baseText}/{Uri.EscapeDataString(secret.GrantId)}/exchange#{fragment}";
    }

    private ReportingAccessGrantValidationStatus ValidateScopeAndState(
        ReportingAccessGrantRecord grant,
        string tenantId,
        string audience,
        string runId,
        string packageId,
        string? artifactId,
        DateTimeOffset now,
        ReportingAccessPrincipalKind audienceKind)
    {
        if (!string.Equals(grant.TenantId, tenantId, StringComparison.Ordinal))
        {
            return ReportingAccessGrantValidationStatus.TenantMismatch;
        }

        if (!string.Equals(grant.Audience, audience, StringComparison.Ordinal)
            || grant.AudienceKind != audienceKind)
        {
            return ReportingAccessGrantValidationStatus.AudienceMismatch;
        }

        if (!string.Equals(grant.RunId, runId, StringComparison.Ordinal))
        {
            return ReportingAccessGrantValidationStatus.PackageMismatch;
        }

        if (!string.Equals(grant.PackageId, packageId, StringComparison.Ordinal))
        {
            return ReportingAccessGrantValidationStatus.PackageMismatch;
        }

        if (artifactId is null)
        {
            if (!grant.AllowPackageRead)
            {
                return ReportingAccessGrantValidationStatus.ArtifactOutOfScope;
            }
        }
        else if (!grant.ArtifactIds.Contains(artifactId, StringComparer.Ordinal))
        {
            return ReportingAccessGrantValidationStatus.ArtifactOutOfScope;
        }

        if (grant.RevokedAtUtc is not null)
        {
            return ReportingAccessGrantValidationStatus.Revoked;
        }

        if (grant.ExpiresAtUtc <= now)
        {
            return ReportingAccessGrantValidationStatus.Expired;
        }

        return grant.UseCount >= grant.MaxUses
            ? ReportingAccessGrantValidationStatus.UseLimitExceeded
            : ReportingAccessGrantValidationStatus.Valid;
    }

    private static byte[] HashSuppliedToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != TokenByteCount * 2)
        {
            return SHA256.HashData(Array.Empty<byte>());
        }

        try
        {
            var rawToken = Convert.FromHexString(token);
            return rawToken.Length == TokenByteCount
                ? SHA256.HashData(rawToken)
                : SHA256.HashData(Array.Empty<byte>());
        }
        catch (FormatException)
        {
            return SHA256.HashData(Array.Empty<byte>());
        }
    }

    private static byte[] ParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != TokenByteCount * 2)
        {
            throw new ArgumentException("Derived access grant tokens must contain 64 hexadecimal characters.", nameof(token));
        }

        try
        {
            var bytes = Convert.FromHexString(token);
            return bytes.Length == TokenByteCount
                ? bytes
                : throw new ArgumentException(
                    "Derived access grant tokens must contain 64 hexadecimal characters.",
                    nameof(token));
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                "Derived access grant tokens must contain 64 hexadecimal characters.",
                nameof(token),
                ex);
        }
    }

    private static void EnsureIdempotentGrantMatches(
        ReportingAccessGrantRecord existing,
        ReportingAccessGrantRecord expected)
    {
        if (!string.Equals(existing.TokenHashSha256, expected.TokenHashSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.TenantId, expected.TenantId, StringComparison.Ordinal)
            || !string.Equals(existing.Audience, expected.Audience, StringComparison.Ordinal)
            || existing.AudienceKind != expected.AudienceKind
            || !string.Equals(existing.RunId, expected.RunId, StringComparison.Ordinal)
            || !string.Equals(existing.PackageId, expected.PackageId, StringComparison.Ordinal)
            || existing.AllowPackageRead != expected.AllowPackageRead
            || existing.MaxUses != expected.MaxUses
            || !existing.ArtifactIds.SequenceEqual(expected.ArtifactIds, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The deterministic delivery access grant is already bound to different immutable content.");
        }
    }

    private static bool MatchesStoredHash(string storedHash, byte[] suppliedHash)
    {
        byte[] storedHashBytes;
        try
        {
            storedHashBytes = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            storedHashBytes = new byte[SHA256.HashSizeInBytes];
        }

        if (storedHashBytes.Length != SHA256.HashSizeInBytes)
        {
            storedHashBytes = new byte[SHA256.HashSizeInBytes];
        }

        return CryptographicOperations.FixedTimeEquals(storedHashBytes, suppliedHash);
    }

    private static IReadOnlyList<string> NormalizeArtifactIds(IReadOnlyList<string>? artifactIds) =>
        artifactIds?
            .Where(static artifactId => !string.IsNullOrWhiteSpace(artifactId))
            .Select(static artifactId => artifactId.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static artifactId => artifactId, StringComparer.Ordinal)
            .ToArray()
        ?? [];

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateAudienceKind(ReportingAccessPrincipalKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Reporting grant audience kind is invalid.");
        }
    }
}
