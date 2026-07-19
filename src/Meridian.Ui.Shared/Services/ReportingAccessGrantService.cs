using System.Security.Cryptography;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

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
        var packageId = NormalizeRequired(request.PackageId, nameof(request.PackageId));
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
                packageId,
                request.AllowPackageRead,
                artifactIds,
                now,
                request.ExpiresAtUtc,
                request.MaxUses,
                UseCount: 0);

            if (await _store.TryCreateAsync(grant, ct).ConfigureAwait(false))
            {
                return new ReportingAccessGrantSecret(grantId, token, request.ExpiresAtUtc);
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique reporting access grant identifier.");
    }

    public async Task<ReportingAccessGrantValidationResult> ValidateAsync(
        ReportingAccessGrantValidationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grantId = NormalizeRequired(request.GrantId, nameof(request.GrantId));
        var tenantId = NormalizeRequired(request.TenantId, nameof(request.TenantId));
        var audience = NormalizeRequired(request.Audience, nameof(request.Audience));
        var packageId = NormalizeRequired(request.PackageId, nameof(request.PackageId));
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

            var status = ValidateScopeAndState(grant, tenantId, audience, packageId, artifactId);
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
                LastUsedAtUtc = _timeProvider.GetUtcNow(),
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
        ReportingAccessGrantSecret secret)
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

        if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            throw new ArgumentException("Recipient access base URI cannot contain a query or fragment.", nameof(accessBaseUri));
        }

        var baseText = parsed.AbsoluteUri.TrimEnd('/');
        return $"{baseText}/{Uri.EscapeDataString(secret.GrantId)}#token={Uri.EscapeDataString(secret.Token)}";
    }

    private ReportingAccessGrantValidationStatus ValidateScopeAndState(
        ReportingAccessGrantRecord grant,
        string tenantId,
        string audience,
        string packageId,
        string? artifactId)
    {
        if (!string.Equals(grant.TenantId, tenantId, StringComparison.Ordinal))
        {
            return ReportingAccessGrantValidationStatus.TenantMismatch;
        }

        if (!string.Equals(grant.Audience, audience, StringComparison.Ordinal))
        {
            return ReportingAccessGrantValidationStatus.AudienceMismatch;
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

        if (grant.ExpiresAtUtc <= _timeProvider.GetUtcNow())
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
}
