using System.Net;
using System.Security.Cryptography;
using System.Text;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportingDeliveryDispatcher
{
    private const int MaximumConcurrencyAttempts = 8;
    private readonly IReportingDeliveryStore _store;
    private readonly IReportingReleaseAuthorizationVerifier _releaseVerifier;
    private readonly IReadOnlyDictionary<string, IReportingDeliveryTransport> _transports;
    private readonly TimeProvider _timeProvider;
    private readonly ReportingDeliveryDispatcherOptions _options;

    public ReportingDeliveryDispatcher(
        IReportingDeliveryStore store,
        IEnumerable<IReportingDeliveryTransport> transports,
        IReportingReleaseAuthorizationVerifier releaseVerifier,
        TimeProvider? timeProvider = null,
        ReportingDeliveryDispatcherOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _releaseVerifier = releaseVerifier ?? throw new ArgumentNullException(nameof(releaseVerifier));
        ArgumentNullException.ThrowIfNull(transports);
        _transports = transports
            .GroupBy(static transport => NormalizeRequired(transport.TransportId, nameof(IReportingDeliveryTransport.TransportId)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = ValidateOptions(options ?? ReportingDeliveryDispatcherOptions.Default);
    }

    public async Task<ReportingDeliveryJobRecord> QueueAsync(
        ReportingDeliveryQueueRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = NormalizeRequired(request.TenantId, nameof(request.TenantId));
        var packageId = NormalizeRequired(request.PackageId, nameof(request.PackageId));
        var distributionId = NormalizeRequired(request.DistributionId, nameof(request.DistributionId));
        var transportId = NormalizeRequired(request.TransportId, nameof(request.TransportId));
        var release = request.ReleaseAuthorization
            ?? throw new InvalidOperationException("A verified Released authorization is required before distribution can be queued.");
        ValidateReleaseAuthorization(release, tenantId, packageId);
        var authorization = await _releaseVerifier.VerifyAsync(release, ct).ConfigureAwait(false);
        if (!authorization.IsAuthorized)
        {
            throw new UnauthorizedAccessException(
                $"Reporting release authorization was rejected ({authorization.Code}): {authorization.Detail ?? "no detail"}");
        }

        var releaseVersion = release.ReleaseVersion;
        var artifactManifestHash = release.ArtifactManifestHashSha256;
        var requestedBy = NormalizeRequired(request.RequestedBy, nameof(request.RequestedBy));
        ValidatePayload(request.Payload);
        if (request.MaxAttempts is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Delivery max attempts must be between 1 and 100.");
        }

        var idempotencyKey = BuildIdempotencyKey(
            tenantId,
            packageId,
            distributionId,
            transportId,
            releaseVersion,
            artifactManifestHash);
        var existing = await _store.GetByIdempotencyKeyAsync(idempotencyKey, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var now = _timeProvider.GetUtcNow();
        var job = new ReportingDeliveryJobRecord(
            $"delivery_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}",
            tenantId,
            packageId,
            distributionId,
            transportId,
            release,
            requestedBy,
            idempotencyKey,
            request.Payload,
            ReportingDeliveryState.Queued,
            AttemptCount: 0,
            request.MaxAttempts,
            now,
            now,
            NextAttemptAtUtc: now,
            LeaseOwner: null,
            LeaseExpiresAtUtc: null,
            LastErrorCode: null,
            LastError: null,
            ProviderMessageId: null,
            AccessGrantId: null,
            Receipts: []);

        if (await _store.TryCreateAsync(job, ct).ConfigureAwait(false))
        {
            return job;
        }

        return await _store.GetByIdempotencyKeyAsync(idempotencyKey, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The delivery queue rejected a job without retaining its idempotency key.");
    }

    public async Task<IReadOnlyList<ReportingDeliveryJobRecord>> DispatchDueAsync(
        string leaseOwner,
        CancellationToken ct = default)
    {
        var normalizedLeaseOwner = NormalizeRequired(leaseOwner, nameof(leaseOwner));
        var claimed = await _store.ClaimDueAsync(
            _timeProvider.GetUtcNow(),
            normalizedLeaseOwner,
            _options.LeaseDuration,
            _options.BatchSize,
            ct).ConfigureAwait(false);
        var results = new List<ReportingDeliveryJobRecord>(claimed.Count);
        foreach (var job in claimed)
        {
            ct.ThrowIfCancellationRequested();
            if (job.State != ReportingDeliveryState.Dispatching
                || !string.Equals(job.LeaseOwner, normalizedLeaseOwner, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Delivery store returned a job without an active matching dispatch lease.");
            }

            results.Add(await DispatchClaimedAsync(job, ct).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<ReportingDeliveryJobRecord> AppendReceiptAsync(
        string jobId,
        string tenantId,
        ReportingDeliveryReceipt receipt,
        CancellationToken ct = default)
    {
        var normalizedJobId = NormalizeRequired(jobId, nameof(jobId));
        var normalizedTenantId = NormalizeRequired(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(receipt);
        NormalizeRequired(receipt.ReceiptId, nameof(receipt.ReceiptId));

        for (var attempt = 0; attempt < MaximumConcurrencyAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var job = await _store.GetAsync(normalizedJobId, ct).ConfigureAwait(false)
                      ?? throw new KeyNotFoundException("Reporting delivery job was not found.");
            if (!string.Equals(job.TenantId, normalizedTenantId, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Reporting delivery job is outside the requested tenant.");
            }

            if (!string.Equals(job.TransportId, receipt.TransportId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Delivery receipt transport does not match the queued transport.");
            }

            if (job.Receipts.Any(item => string.Equals(item.ReceiptId, receipt.ReceiptId, StringComparison.Ordinal)))
            {
                return job;
            }

            var nextState = ApplyReceiptState(job.State, receipt.Kind);
            var isFailure = receipt.Kind is ReportingDeliveryReceiptKind.Bounced or ReportingDeliveryReceiptKind.Rejected;
            var updated = job with
            {
                State = nextState,
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                NextAttemptAtUtc = null,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                LastErrorCode = isFailure ? receipt.Kind.ToString().ToUpperInvariant() : null,
                LastError = isFailure ? receipt.Detail : null,
                ProviderMessageId = receipt.ProviderReference ?? job.ProviderMessageId,
                Receipts = job.Receipts.Append(receipt).ToArray(),
                Version = job.Version + 1
            };
            if (await _store.TryUpdateAsync(job.JobId, job.Version, updated, ct).ConfigureAwait(false))
            {
                return updated;
            }
        }

        throw new InvalidOperationException("Delivery receipt update conflicted repeatedly.");
    }

    public static string BuildIdempotencyKey(
        string tenantId,
        string packageId,
        string distributionId,
        string transportId,
        string releaseVersion,
        string artifactManifestHash)
    {
        var canonical = string.Join(
            "\u001f",
            NormalizeRequired(tenantId, nameof(tenantId)),
            NormalizeRequired(packageId, nameof(packageId)),
            NormalizeRequired(distributionId, nameof(distributionId)),
            NormalizeRequired(transportId, nameof(transportId)).ToLowerInvariant(),
            NormalizeRequired(releaseVersion, nameof(releaseVersion)),
            NormalizeRequired(artifactManifestHash, nameof(artifactManifestHash)).ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private async Task<ReportingDeliveryJobRecord> DispatchClaimedAsync(
        ReportingDeliveryJobRecord job,
        CancellationToken ct)
    {
        ReportingDeliveryTransportResult result;
        if (!_transports.TryGetValue(job.TransportId, out var transport))
        {
            result = ReportingDeliveryTransportResult.PermanentFailure(
                "MISSING_TRANSPORT",
                $"No reporting delivery transport is registered for '{job.TransportId}'.");
        }
        else
        {
            try
            {
                result = await transport.DeliverAsync(
                    new ReportingDeliveryTransportRequest(
                        job.JobId,
                        job.TenantId,
                        job.PackageId,
                        job.DistributionId,
                        job.IdempotencyKey,
                        job.AttemptCount + 1,
                        job.ReleaseAuthorization,
                        job.Payload),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = IsTransient(ex)
                    ? ReportingDeliveryTransportResult.TransientFailure("TRANSPORT_EXCEPTION", ex.Message)
                    : ReportingDeliveryTransportResult.PermanentFailure("TRANSPORT_REJECTED", ex.Message);
            }
        }

        var updated = ProjectTransportResult(job, result);
        if (!await _store.TryUpdateAsync(job.JobId, job.Version, updated, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Delivery result could not be committed against the active lease version.");
        }

        return updated;
    }

    private ReportingDeliveryJobRecord ProjectTransportResult(
        ReportingDeliveryJobRecord job,
        ReportingDeliveryTransportResult result)
    {
        var now = _timeProvider.GetUtcNow();
        var attemptCount = job.AttemptCount + 1;
        var receipts = result.Receipt is null
            ? job.Receipts
            : job.Receipts.Any(item => string.Equals(item.ReceiptId, result.Receipt.ReceiptId, StringComparison.Ordinal))
                ? job.Receipts
                : job.Receipts.Append(result.Receipt).ToArray();
        var state = result.Outcome switch
        {
            ReportingDeliveryTransportOutcome.Sent => ReportingDeliveryState.Sent,
            ReportingDeliveryTransportOutcome.Delivered => ReportingDeliveryState.Delivered,
            ReportingDeliveryTransportOutcome.PermanentFailure => ReportingDeliveryState.Blocked,
            ReportingDeliveryTransportOutcome.TransientFailure when attemptCount < job.MaxAttempts => ReportingDeliveryState.RetryScheduled,
            ReportingDeliveryTransportOutcome.TransientFailure => ReportingDeliveryState.Failed,
            _ => ReportingDeliveryState.Blocked
        };
        var failed = result.Outcome is ReportingDeliveryTransportOutcome.TransientFailure or ReportingDeliveryTransportOutcome.PermanentFailure;
        return job with
        {
            State = state,
            AttemptCount = attemptCount,
            UpdatedAtUtc = now,
            NextAttemptAtUtc = state == ReportingDeliveryState.RetryScheduled
                ? now.Add(ComputeRetryDelay(attemptCount))
                : null,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            LastErrorCode = failed ? result.Code : null,
            LastError = failed ? result.Detail : null,
            ProviderMessageId = result.ProviderMessageId ?? job.ProviderMessageId,
            AccessGrantId = result.AccessGrantId ?? job.AccessGrantId,
            Receipts = receipts,
            Version = job.Version + 1
        };
    }

    private TimeSpan ComputeRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 30);
        var multiplier = 1L << exponent;
        var requestedTicks = _options.BaseRetryDelay.Ticks > long.MaxValue / multiplier
            ? long.MaxValue
            : _options.BaseRetryDelay.Ticks * multiplier;
        return TimeSpan.FromTicks(Math.Min(requestedTicks, _options.MaximumRetryDelay.Ticks));
    }

    private static ReportingDeliveryState ApplyReceiptState(
        ReportingDeliveryState current,
        ReportingDeliveryReceiptKind receiptKind) =>
        receiptKind switch
        {
            ReportingDeliveryReceiptKind.Delivered or
            ReportingDeliveryReceiptKind.Accessed or
            ReportingDeliveryReceiptKind.Downloaded => ReportingDeliveryState.Delivered,
            ReportingDeliveryReceiptKind.Bounced => ReportingDeliveryState.Failed,
            ReportingDeliveryReceiptKind.Rejected => ReportingDeliveryState.Blocked,
            _ when current == ReportingDeliveryState.Delivered => ReportingDeliveryState.Delivered,
            _ => ReportingDeliveryState.Sent
        };

    private static bool IsTransient(Exception exception) =>
        exception is TimeoutException
            or HttpRequestException
            or IOException;

    private static ReportingDeliveryDispatcherOptions ValidateOptions(ReportingDeliveryDispatcherOptions options)
    {
        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Delivery lease duration must be positive.");
        }

        if (options.BaseRetryDelay <= TimeSpan.Zero || options.MaximumRetryDelay < options.BaseRetryDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Delivery retry delays are invalid.");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Delivery batch size must be between 1 and 1000.");
        }

        return options;
    }

    private static void ValidatePayload(ReportingDeliveryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        NormalizeRequired(payload.Recipient, nameof(payload.Recipient));
        NormalizeRequired(payload.RecipientRole, nameof(payload.RecipientRole));
        NormalizeRequired(payload.Destination, nameof(payload.Destination));
        NormalizeRequired(payload.Subject, nameof(payload.Subject));
        NormalizeRequired(payload.Body, nameof(payload.Body));
        ValidateTokenFreeUri(payload.PortalUri, nameof(payload.PortalUri), requireAbsolute: false);
        if (payload.ExternalAccess is { } access)
        {
            NormalizeRequired(access.Audience, nameof(access.Audience));
            ValidateTokenFreeUri(access.AccessBaseUri, nameof(access.AccessBaseUri), requireAbsolute: true);
            if (access.Lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(payload), "External access lifetime must be positive.");
            }

            if (access.MaxUses <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payload), "External access use limit must be positive.");
            }
        }
    }

    private static void ValidateReleaseAuthorization(
        ReportingDeliveryReleaseAuthorization release,
        string tenantId,
        string packageId)
    {
        ArgumentNullException.ThrowIfNull(release);
        NormalizeRequired(release.ReceiptId, nameof(release.ReceiptId));
        NormalizeRequired(release.ReleaseVersion, nameof(release.ReleaseVersion));
        NormalizeRequired(release.ReleasedBy, nameof(release.ReleasedBy));
        NormalizeRequired(release.AuthorizationProof, nameof(release.AuthorizationProof));
        ValidateSha256(release.ArtifactManifestHashSha256, nameof(release.ArtifactManifestHashSha256));

        if (release.State != ReportingReleaseState.Released)
        {
            throw new InvalidOperationException(
                $"Release authorization '{release.ReceiptId}' is {release.State}; only Released packages can be distributed.");
        }

        if (!string.Equals(release.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(release.PackageId, packageId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Release authorization tenant or package does not match the delivery request.");
        }

        if (release.Artifacts is null || release.Artifacts.Count == 0)
        {
            throw new InvalidOperationException("A release authorization must bind at least one immutable artifact.");
        }

        foreach (var artifact in release.Artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            NormalizeRequired(artifact.ArtifactId, nameof(artifact.ArtifactId));
            var retainedUri = NormalizeRequired(artifact.RetainedUri, nameof(artifact.RetainedUri));
            if (retainedUri.Contains("token=", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Release artifact identities cannot retain bearer tokens.");
            }

            ValidateSha256(artifact.ContentHashSha256, nameof(artifact.ContentHashSha256));
            if (artifact.ByteSize <= 0)
            {
                throw new InvalidOperationException("Released artifact byte size must be positive.");
            }
        }

        if (release.EvidenceReferences is null
            || release.EvidenceReferences.All(static evidence => string.IsNullOrWhiteSpace(evidence)))
        {
            throw new InvalidOperationException("A release authorization must retain release evidence.");
        }
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        var normalized = NormalizeRequired(value, parameterName);
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 values must contain exactly 64 hexadecimal characters.", parameterName);
        }
    }

    private static void ValidateTokenFreeUri(string value, string parameterName, bool requireAbsolute)
    {
        var normalized = NormalizeRequired(value, parameterName);
        if (normalized.Contains("token=", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Durable delivery payloads cannot contain bearer tokens.", parameterName);
        }

        if (requireAbsolute)
        {
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var absolute)
                || !string.IsNullOrEmpty(absolute.Query)
                || !string.IsNullOrEmpty(absolute.Fragment))
            {
                throw new ArgumentException("Recipient access base URI must be absolute and cannot contain a query or fragment.", parameterName);
            }

            return;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            && (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment)))
        {
            throw new ArgumentException("Portal URI cannot contain a query or fragment.", parameterName);
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _)
            && !normalized.StartsWith('/'))
        {
            throw new ArgumentException("Portal URI must be absolute or application-root relative.", parameterName);
        }
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

public sealed class SecurePortalReportingDeliveryTransport : IReportingDeliveryTransport
{
    private readonly TimeProvider _timeProvider;

    public SecurePortalReportingDeliveryTransport(
        TimeProvider? timeProvider = null,
        string transportId = "secure-portal")
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        TransportId = string.IsNullOrWhiteSpace(transportId)
            ? throw new ArgumentException("Transport id is required.", nameof(transportId))
            : transportId.Trim();
    }

    public string TransportId { get; }

    public Task<ReportingDeliveryTransportResult> DeliverAsync(
        ReportingDeliveryTransportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (!IsSafePortalUri(request.Payload.PortalUri))
        {
            return Task.FromResult(ReportingDeliveryTransportResult.PermanentFailure(
                "INVALID_PORTAL_URI",
                "Secure portal publication requires a token-free HTTPS or application-relative route."));
        }

        var providerReference = $"portal:{request.PackageId}";
        var receipt = new ReportingDeliveryReceipt(
            BuildStableReference("portal-published", request.IdempotencyKey),
            ReportingDeliveryReceiptKind.Published,
            _timeProvider.GetUtcNow(),
            TransportId,
            providerReference,
            Detail: "Package is published to the secure portal; recipient access is not yet proven.");
        return Task.FromResult(ReportingDeliveryTransportResult.Sent(
            "PORTAL_PUBLISHED",
            providerReference,
            receipt: receipt));
    }

    private static bool IsSafePortalUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains("token=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.StartsWith('/'))
        {
            return !value.Contains('?')
                   && !value.Contains('#');
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
               || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    internal static string BuildStableReference(string prefix, string idempotencyKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey))).ToLowerInvariant();
        return $"{prefix}:{hash[..24]}";
    }
}

public sealed class HttpRelayReportingDeliveryTransport : IReportingDeliveryTransport
{
    private readonly IReportingHttpRelayClient _relayClient;
    private readonly ReportingAccessGrantService _accessGrants;
    private readonly TimeProvider _timeProvider;

    public HttpRelayReportingDeliveryTransport(
        IReportingHttpRelayClient relayClient,
        ReportingAccessGrantService accessGrants,
        TimeProvider? timeProvider = null,
        string transportId = "http-relay")
    {
        _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
        _accessGrants = accessGrants ?? throw new ArgumentNullException(nameof(accessGrants));
        _timeProvider = timeProvider ?? TimeProvider.System;
        TransportId = string.IsNullOrWhiteSpace(transportId)
            ? throw new ArgumentException("Transport id is required.", nameof(transportId))
            : transportId.Trim();
    }

    public string TransportId { get; }

    public async Task<ReportingDeliveryTransportResult> DeliverAsync(
        ReportingDeliveryTransportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var access = request.Payload.ExternalAccess;
        if (access is null)
        {
            return ReportingDeliveryTransportResult.PermanentFailure(
                "MISSING_ACCESS_POLICY",
                "External relay delivery requires an access-grant policy.");
        }

        ReportingAccessGrantSecret? secret = null;
        try
        {
            secret = await _accessGrants.IssueAsync(
                new ReportingAccessGrantIssueRequest(
                    request.TenantId,
                    access.Audience,
                    request.PackageId,
                    _timeProvider.GetUtcNow().Add(access.Lifetime),
                    access.AllowPackageRead,
                    access.ArtifactIds,
                    access.MaxUses),
                ct).ConfigureAwait(false);
            var recipientAccessUri = ReportingAccessGrantService.BuildRecipientAccessUri(access.AccessBaseUri, secret);
            var relayResult = await _relayClient.SendAsync(
                new ReportingHttpRelayMessage(
                    request.TenantId,
                    request.PackageId,
                    request.Payload.Destination,
                    request.Payload.Subject,
                    request.Payload.Body,
                    recipientAccessUri,
                    request.IdempotencyKey),
                ct).ConfigureAwait(false);
            if (!relayResult.IsSuccess)
            {
                var revoked = await _accessGrants.RevokeAsync(
                    secret.GrantId,
                    request.TenantId,
                    "reporting-delivery-dispatcher",
                    $"Relay delivery failed with {relayResult.Code}.",
                    ct).ConfigureAwait(false);
                if (!revoked)
                {
                    return ReportingDeliveryTransportResult.PermanentFailure(
                        "ACCESS_GRANT_REVOCATION_FAILED",
                        "External delivery failed and its access grant could not be revoked.");
                }

                return relayResult.IsTransientFailure
                    ? ReportingDeliveryTransportResult.TransientFailure(relayResult.Code, relayResult.Detail)
                    : ReportingDeliveryTransportResult.PermanentFailure(relayResult.Code, relayResult.Detail);
            }

            var receipt = new ReportingDeliveryReceipt(
                SecurePortalReportingDeliveryTransport.BuildStableReference("relay-accepted", request.IdempotencyKey),
                ReportingDeliveryReceiptKind.Accepted,
                _timeProvider.GetUtcNow(),
                TransportId,
                relayResult.ProviderMessageId,
                Detail: "External relay accepted the notification; recipient delivery is not yet proven.");
            return ReportingDeliveryTransportResult.Sent(
                relayResult.Code,
                relayResult.ProviderMessageId,
                secret.GrantId,
                receipt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (secret is not null)
            {
                var revoked = await _accessGrants.RevokeAsync(
                    secret.GrantId,
                    request.TenantId,
                    "reporting-delivery-dispatcher",
                    "Relay dispatch threw before acceptance.",
                    CancellationToken.None).ConfigureAwait(false);
                if (!revoked)
                {
                    throw new InvalidOperationException("Relay dispatch failed and its access grant could not be revoked.");
                }
            }

            throw;
        }
    }
}
