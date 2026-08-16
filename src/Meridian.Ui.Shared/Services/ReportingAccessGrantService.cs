using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingDerivedAccessGrantCredential(string GrantId, string Token);

internal sealed record ReportingAccessGrantConsumptionPreparation(
    ReportingAccessGrantValidationStatus Status,
    ReportingAccessGrantRecord? CurrentGrant = null,
    ReportingAccessGrantRecord? ConsumedGrant = null)
{
    public bool IsValid =>
        Status == ReportingAccessGrantValidationStatus.Valid
        && CurrentGrant is not null
        && ConsumedGrant is not null;
}

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
            var tokenHash = Sha256Digest.Compute(rawToken);
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
                AudienceKind: request.AudienceKind,
                ConsumedArtifactIds: []);

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
        var tokenHash = Sha256Digest.Compute(rawToken);
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
            AudienceKind: request.AudienceKind,
            ConsumedArtifactIds: []);
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
            || existing.UseCount >= existing.MaxUses
            || (existing.ConsumedArtifactIds is null
                && existing.ArtifactIds.Count > 1
                && existing.UseCount > 0))
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

        for (var attempt = 0; attempt < MaximumConcurrencyAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var preparation = await PrepareConsumptionAsync(
                    request,
                    consumedAtUtc: null,
                    ct: ct)
                .ConfigureAwait(false);
            if (!preparation.IsValid)
            {
                return new ReportingAccessGrantValidationResult(preparation.Status);
            }

            if (!request.ConsumeUse)
            {
                return new ReportingAccessGrantValidationResult(
                    ReportingAccessGrantValidationStatus.Valid,
                    preparation.CurrentGrant);
            }

            var current = preparation.CurrentGrant!;
            var consumed = preparation.ConsumedGrant!;
            if (await _store
                    .TryUpdateAsync(current.GrantId, current.Version, consumed, ct)
                    .ConfigureAwait(false))
            {
                return new ReportingAccessGrantValidationResult(
                    ReportingAccessGrantValidationStatus.Valid,
                    consumed);
            }
        }

        return new ReportingAccessGrantValidationResult(ReportingAccessGrantValidationStatus.ConcurrencyConflict);
    }

    /// <summary>
    /// Validates a bearer and prepares, but does not persist, its exact one-use transition. This is
    /// used by the PostgreSQL delivery composite so the grant and Downloaded receipt can commit
    /// together without exposing the plaintext bearer to Storage.
    /// </summary>
    internal async Task<ReportingAccessGrantConsumptionPreparation> PrepareConsumptionAsync(
        ReportingAccessGrantValidationRequest request,
        DateTimeOffset? consumedAtUtc,
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
        var grant = await _store.GetAsync(grantId, ct).ConfigureAwait(false);
        if (grant is null)
        {
            return new ReportingAccessGrantConsumptionPreparation(
                ReportingAccessGrantValidationStatus.NotFound);
        }

        if (!MatchesStoredHash(grant.TokenHashSha256, suppliedTokenHash))
        {
            return new ReportingAccessGrantConsumptionPreparation(
                ReportingAccessGrantValidationStatus.TokenMismatch);
        }

        // One authority time governs the state decision and the exact Downloaded receipt. A caller
        // that already completed an audited exact-byte read supplies that audit timestamp; grant
        // state retains it unless a later concurrent read has already advanced the high-water mark.
        var now = consumedAtUtc ?? _timeProvider.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Reporting access-grant consumption time must be UTC.",
                nameof(consumedAtUtc));
        }

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
            return new ReportingAccessGrantConsumptionPreparation(status);
        }

        var consumed = grant with
        {
            UseCount = checked(grant.UseCount + 1),
            // A delivery-linked read can lose its first optimistic commit to a read that occurred
            // later. Preserve the retained high-water mark while the Downloaded receipt keeps the
            // exact earlier audit-event time.
            LastUsedAtUtc = grant.LastUsedAtUtc is { } retainedLastUsedAtUtc
                            && retainedLastUsedAtUtc > now
                ? retainedLastUsedAtUtc
                : now,
            Version = checked(grant.Version + 1),
            ConsumedArtifactIds = PrepareConsumedArtifactIds(grant, artifactId)
        };
        return new ReportingAccessGrantConsumptionPreparation(
            ReportingAccessGrantValidationStatus.Valid,
            grant,
            consumed);
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

        ValidateConsumedArtifactState(grant);
        if (grant.UseCount >= grant.MaxUses)
        {
            return ReportingAccessGrantValidationStatus.UseLimitExceeded;
        }

        if (grant.ConsumedArtifactIds is null)
        {
            // A retained multi-artifact grant that has already been used predates exact artifact
            // consumption tracking. Its prior artifact identity cannot be reconstructed safely.
            if (grant.ArtifactIds.Count > 1 && grant.UseCount > 0)
            {
                return ReportingAccessGrantValidationStatus.UseLimitExceeded;
            }

            // Every post-migration use of a legacy marker must atomically initialize one exact
            // artifact. A package-level read is unambiguous only when exactly one artifact exists.
            // Zero-artifact legacy package grants must be reissued with explicit tracked state.
            return artifactId is not null || grant.ArtifactIds.Count == 1
                ? ReportingAccessGrantValidationStatus.Valid
                : ReportingAccessGrantValidationStatus.ArtifactOutOfScope;
        }

        if (grant.UseCount == 0
            && grant.ConsumedArtifactIds.Count == 0
            && grant.ArtifactIds.Count > 0
            && artifactId is null)
        {
            return ReportingAccessGrantValidationStatus.ArtifactOutOfScope;
        }

        var remainingDistinctArtifacts = grant.ArtifactIds.Count(artifact =>
            !grant.ConsumedArtifactIds.Contains(artifact, StringComparer.Ordinal));
        var identifiesUnconsumedArtifact = artifactId is not null
            && !grant.ConsumedArtifactIds.Contains(artifactId, StringComparer.Ordinal);
        if (grant.MaxUses >= grant.ArtifactIds.Count
            && !identifiesUnconsumedArtifact
            && grant.MaxUses - grant.UseCount <= remainingDistinctArtifacts)
        {
            // Preserve one remaining use for every authorized artifact not yet consumed. Extra
            // configured uses may still support bounded retries after that coverage is reserved.
            return ReportingAccessGrantValidationStatus.UseLimitExceeded;
        }

        return ReportingAccessGrantValidationStatus.Valid;
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

    private static IReadOnlyList<string>? PrepareConsumedArtifactIds(
        ReportingAccessGrantRecord grant,
        string? artifactId)
    {
        if (artifactId is null)
        {
            return grant.ConsumedArtifactIds is null
                   && grant.ArtifactIds.Count == 1
                ? [grant.ArtifactIds[0]]
                : grant.ConsumedArtifactIds;
        }

        if (grant.ConsumedArtifactIds is null)
        {
            // Untouched legacy grants and legacy single-artifact grants can be upgraded without
            // guessing. ValidateScopeAndState rejects previously used multi-artifact grants.
            return [artifactId];
        }

        return grant.ConsumedArtifactIds.Contains(artifactId, StringComparer.Ordinal)
            ? grant.ConsumedArtifactIds
            : grant.ConsumedArtifactIds
                .Append(artifactId)
                .OrderBy(static consumedArtifactId => consumedArtifactId, StringComparer.Ordinal)
                .ToArray();
    }

    private static void ValidateConsumedArtifactState(ReportingAccessGrantRecord grant)
    {
        if (grant.ConsumedArtifactIds is null)
        {
            return;
        }

        var normalized = NormalizeArtifactIds(grant.ConsumedArtifactIds);
        if (!grant.ConsumedArtifactIds.SequenceEqual(normalized, StringComparer.Ordinal)
            || grant.ConsumedArtifactIds.Any(consumed =>
                !grant.ArtifactIds.Contains(consumed, StringComparer.Ordinal))
            || grant.ConsumedArtifactIds.Count > grant.UseCount
            || (grant.UseCount > 0
                && grant.ArtifactIds.Count > 0
                && grant.ConsumedArtifactIds.Count == 0))
        {
            throw new InvalidDataException(
                "The retained reporting access grant has invalid consumed-artifact authority state.");
        }
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static void ValidateAudienceKind(ReportingAccessPrincipalKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Reporting grant audience kind is invalid.");
        }
    }
}
