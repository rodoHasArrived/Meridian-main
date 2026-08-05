using System.Data.Common;
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
            artifactManifestHash,
            request.Payload);
        var existing = await _store.GetByIdempotencyKeyAsync(idempotencyKey, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!QueueRequestMatches(existing, request, tenantId, packageId, distributionId, transportId))
            {
                throw new InvalidOperationException(
                    "The reporting distribution idempotency key is already bound to different immutable delivery content.");
            }

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

    /// <summary>True only when a concrete transport adapter is present in this process.</summary>
    public bool IsTransportConfigured(string transportId) =>
        _transports.ContainsKey(NormalizeRequired(transportId, nameof(transportId)));

    /// <summary>Configured adapter identifiers, never provider credentials or endpoints.</summary>
    public IReadOnlyCollection<string> ConfiguredTransportIds =>
        _transports.Keys.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray();

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
        receipt = NormalizeReceipt(receipt);

        for (var attempt = 0; attempt < MaximumConcurrencyAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var job = await _store.GetAsync(normalizedJobId, ct).ConfigureAwait(false)
                      ?? throw new KeyNotFoundException("Reporting delivery job was not found.");
            var updated = PrepareReceiptAppend(job, normalizedTenantId, receipt);
            if (updated.Version == job.Version)
            {
                return job;
            }

            if (await _store.TryUpdateAsync(job.JobId, job.Version, updated, ct).ConfigureAwait(false))
            {
                return updated;
            }
        }

        throw new InvalidOperationException("Delivery receipt update conflicted repeatedly.");
    }

    /// <summary>
    /// Prepares, but does not persist, the append-only receipt transition used by ordinary
    /// dispatcher paths. Downloaded receipts require the dedicated composite preparation below.
    /// </summary>
    internal ReportingDeliveryJobRecord PrepareReceiptAppend(
        ReportingDeliveryJobRecord job,
        string tenantId,
        ReportingDeliveryReceipt receipt) =>
        PrepareReceiptAppendCore(job, tenantId, receipt, allowDownloaded: false);

    /// <summary>
    /// Prepares the Downloaded receipt that the delivery-linked grant exchange commits with the
    /// matching grant use through the PostgreSQL composite boundary.
    /// </summary>
    internal ReportingDeliveryJobRecord PrepareGrantDownloadReceiptAppend(
        ReportingDeliveryJobRecord job,
        string tenantId,
        ReportingDeliveryReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Kind != ReportingDeliveryReceiptKind.Downloaded)
        {
            throw new ArgumentException(
                "The grant-download receipt preparation boundary accepts only Downloaded receipts.",
                nameof(receipt));
        }

        return PrepareReceiptAppendCore(job, tenantId, receipt, allowDownloaded: true);
    }

    private ReportingDeliveryJobRecord PrepareReceiptAppendCore(
        ReportingDeliveryJobRecord job,
        string tenantId,
        ReportingDeliveryReceipt receipt,
        bool allowDownloaded)
    {
        ArgumentNullException.ThrowIfNull(job);
        var normalizedTenantId = NormalizeRequired(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(receipt);
        receipt = NormalizeReceipt(receipt);
        if (receipt.Kind == ReportingDeliveryReceiptKind.Downloaded && !allowDownloaded)
        {
            throw new InvalidOperationException(
                "Downloaded receipts require the atomic access-grant consumption boundary.");
        }

        if (!string.Equals(job.TenantId, normalizedTenantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Reporting delivery job is outside the requested tenant.");
        }

        if (!string.Equals(job.TransportId, receipt.TransportId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Delivery receipt transport does not match the queued transport.");
        }

        var existingReceipt = job.Receipts.FirstOrDefault(item =>
            string.Equals(item.ReceiptId, receipt.ReceiptId, StringComparison.Ordinal));
        if (existingReceipt is not null)
        {
            if (!ReceiptEquals(existingReceipt, receipt))
            {
                throw new InvalidDataException(
                    "A reporting delivery receipt id was replayed with different immutable content.");
            }

            return job;
        }

        var now = _timeProvider.GetUtcNow();
        if (receipt.OccurredAtUtc < job.CreatedAtUtc.AddMinutes(-5)
            || receipt.OccurredAtUtc > now.AddMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(receipt),
                "Delivery receipt time is outside the retained job window.");
        }

        var nextState = ApplyReceiptState(job, receipt.Kind);
        var isFailure = receipt.Kind is ReportingDeliveryReceiptKind.Bounced
            or ReportingDeliveryReceiptKind.Rejected
            or ReportingDeliveryReceiptKind.Failed;
        var resolvesActiveDispatch = job.State == ReportingDeliveryState.Dispatching;
        return job with
        {
            State = nextState,
            AttemptCount = resolvesActiveDispatch
                ? checked(job.AttemptCount + 1)
                : job.AttemptCount,
            UpdatedAtUtc = now,
            NextAttemptAtUtc = null,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            LastErrorCode = isFailure ? receipt.Kind.ToString().ToUpperInvariant() : null,
            LastError = isFailure ? receipt.Detail : null,
            ProviderMessageId = receipt.ProviderReference ?? job.ProviderMessageId,
            Receipts = job.Receipts.Append(receipt).ToArray(),
            Version = checked(job.Version + 1)
        };
    }

    public static string BuildIdempotencyKey(
        string tenantId,
        string packageId,
        string distributionId,
        string transportId,
        string releaseVersion,
        string artifactManifestHash,
        ReportingDeliveryPayload? payload = null)
    {
        // Payload remains in the signature for source compatibility, but DistributionId is the
        // caller's durable idempotency identity. A changed payload is rejected against the retained
        // job instead of silently creating a second recipient notification.
        _ = payload;
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

    private static bool QueueRequestMatches(
        ReportingDeliveryJobRecord existing,
        ReportingDeliveryQueueRequest request,
        string tenantId,
        string packageId,
        string distributionId,
        string transportId) =>
        string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal)
        && string.Equals(existing.PackageId, packageId, StringComparison.Ordinal)
        && string.Equals(existing.DistributionId, distributionId, StringComparison.Ordinal)
        && string.Equals(existing.TransportId, transportId, StringComparison.OrdinalIgnoreCase)
        && existing.MaxAttempts == request.MaxAttempts
        && string.Equals(
            existing.ReleaseAuthorization.AuthorizationProof,
            request.ReleaseAuthorization?.AuthorizationProof,
            StringComparison.Ordinal)
        && string.Equals(
            ComputePayloadHash(existing.Payload),
            ComputePayloadHash(request.Payload),
            StringComparison.Ordinal);

    private async Task<ReportingDeliveryJobRecord> DispatchClaimedAsync(
        ReportingDeliveryJobRecord job,
        CancellationToken ct)
    {
        ReportingDeliveryTransportResult result;
        var release = await _releaseVerifier
            .VerifyAsync(job.ReleaseAuthorization, ct)
            .ConfigureAwait(false);
        if (!release.IsAuthorized)
        {
            result = ReportingDeliveryTransportResult.PermanentFailure(
                $"RELEASE_{NormalizeErrorCode(release.Code)}",
                "The governed release authorization could not be verified immediately before dispatch.");
        }
        else if (!_transports.TryGetValue(job.TransportId, out var transport))
        {
            result = ReportingDeliveryTransportResult.PermanentFailure(
                "MISSING_TRANSPORT",
                $"No reporting delivery transport is registered for '{job.TransportId}'.");
        }
        else
        {
            try
            {
                var transportRequest = new ReportingDeliveryTransportRequest(
                    job.JobId,
                    job.TenantId,
                    job.PackageId,
                    job.DistributionId,
                    job.IdempotencyKey,
                    ResolveProviderAttemptNumber(job),
                    job.ReleaseAuthorization,
                    job.Payload);
                if (transport is IReportingDeliveryAttemptBindingProvider bindingProvider)
                {
                    job = await BindAttemptAccessGrantAsync(
                            job,
                            bindingProvider.ResolveAccessGrantId(transportRequest),
                            ct)
                        .ConfigureAwait(false);
                }

                result = await transport.DeliverAsync(transportRequest, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = IsTransient(ex)
                    ? transport is IReportingDeliveryAttemptBindingProvider
                      && job.AccessGrantId is not null
                        ? new ReportingDeliveryTransportResult(
                            ReportingDeliveryTransportOutcome.TransientFailure,
                            "RELAY_OUTCOME_UNKNOWN",
                            $"Bound relay attempt failed with transient error type {ex.GetType().Name}; the same provider attempt will be replayed.",
                            AccessGrantId: job.AccessGrantId)
                        : ReportingDeliveryTransportResult.TransientFailure(
                            "TRANSPORT_EXCEPTION",
                            $"Transport failed with transient error type {ex.GetType().Name}.")
                    : ReportingDeliveryTransportResult.PermanentFailure(
                        "TRANSPORT_REJECTED",
                        $"Transport rejected the attempt with error type {ex.GetType().Name}.");
            }
        }

        var updated = ProjectTransportResult(job, result);
        var commitCancellationToken = ct.IsCancellationRequested ? CancellationToken.None : ct;
        if (!await _store.TryUpdateAsync(job.JobId, job.Version, updated, commitCancellationToken).ConfigureAwait(false))
        {
            var reconciled = await TryResolveReceiptRaceAsync(job, result, commitCancellationToken)
                .ConfigureAwait(false);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new InvalidOperationException("Delivery result could not be committed against the active lease version.");
        }

        return updated;
    }

    private async Task<ReportingDeliveryJobRecord> BindAttemptAccessGrantAsync(
        ReportingDeliveryJobRecord job,
        string accessGrantId,
        CancellationToken ct)
    {
        var normalizedGrantId = NormalizeIdentifier(accessGrantId)
                                ?? throw new InvalidDataException(
                                    "Delivery transport returned an invalid deterministic access grant id.");
        if (job.AccessGrantId is not null)
        {
            if (!string.Equals(job.AccessGrantId, normalizedGrantId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Delivery transport attempted to replace the immutable attempt access grant binding.");
            }

            return job;
        }

        var bound = job with
        {
            AccessGrantId = normalizedGrantId,
            UpdatedAtUtc = _timeProvider.GetUtcNow(),
            Version = job.Version + 1
        };
        if (!await _store.TryUpdateAsync(job.JobId, job.Version, bound, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Delivery attempt access grant could not be bound before provider dispatch.");
        }

        return bound;
    }

    private async Task<ReportingDeliveryJobRecord?> TryResolveReceiptRaceAsync(
        ReportingDeliveryJobRecord claimed,
        ReportingDeliveryTransportResult result,
        CancellationToken ct)
    {
        var retained = await _store.GetAsync(claimed.JobId, ct).ConfigureAwait(false);
        if (retained is null
            || retained.Version <= claimed.Version
            || retained.State is not (
                ReportingDeliveryState.Sent
                or ReportingDeliveryState.Delivered
                or ReportingDeliveryState.Failed)
            || retained.LeaseOwner is not null
            || retained.LeaseExpiresAtUtc is not null)
        {
            return null;
        }

        var providerMessageId = NormalizeIdentifier(result.ProviderMessageId);
        if (providerMessageId is null
            || !string.Equals(retained.ProviderMessageId, providerMessageId, StringComparison.Ordinal)
            || !string.Equals(
                retained.AccessGrantId,
                result.AccessGrantId ?? claimed.AccessGrantId,
                StringComparison.Ordinal))
        {
            return null;
        }

        return retained;
    }

    private ReportingDeliveryJobRecord ProjectTransportResult(
        ReportingDeliveryJobRecord job,
        ReportingDeliveryTransportResult result)
    {
        var now = _timeProvider.GetUtcNow();
        var attemptCount = job.AttemptCount + 1;
        result = NormalizeTransportResult(result);
        var safeCode = NormalizeErrorCode(result.Code);
        var normalizedResult = result with
        {
            Code = safeCode,
            Detail = NormalizePersistedDetail(result.Detail),
            ProviderMessageId = NormalizeIdentifier(result.ProviderMessageId),
            AccessGrantId = NormalizeIdentifier(result.AccessGrantId)
        };
        var receipt = NormalizeReceipt(
            result.Receipt ?? BuildAttemptReceipt(job, normalizedResult, now, attemptCount));
        if (receipt.Kind == ReportingDeliveryReceiptKind.Downloaded)
        {
            throw new InvalidOperationException(
                "Delivery transports cannot create Downloaded receipts; use the atomic access-grant consumption boundary.");
        }

        var receipts = job.Receipts.Any(item => string.Equals(item.ReceiptId, receipt.ReceiptId, StringComparison.Ordinal))
                ? job.Receipts
                : job.Receipts.Append(receipt).ToArray();
        var state = result.Outcome switch
        {
            ReportingDeliveryTransportOutcome.Sent => ReportingDeliveryState.Sent,
            ReportingDeliveryTransportOutcome.Delivered => ReportingDeliveryState.Delivered,
            ReportingDeliveryTransportOutcome.PermanentFailure => ReportingDeliveryState.Failed,
            ReportingDeliveryTransportOutcome.TransientFailure when attemptCount < job.MaxAttempts => ReportingDeliveryState.RetryScheduled,
            ReportingDeliveryTransportOutcome.TransientFailure
                when normalizedResult.AccessGrantId is not null
                     && IsProviderOutcomeUnknownCode(safeCode) => ReportingDeliveryState.Blocked,
            ReportingDeliveryTransportOutcome.TransientFailure => ReportingDeliveryState.Failed,
            _ => ReportingDeliveryState.Blocked
        };
        var failed = normalizedResult.Outcome is ReportingDeliveryTransportOutcome.TransientFailure or ReportingDeliveryTransportOutcome.PermanentFailure;
        var safeDetail = normalizedResult.Detail;
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
            LastErrorCode = failed ? safeCode : null,
            LastError = failed ? safeDetail : null,
            ProviderMessageId = normalizedResult.ProviderMessageId ?? job.ProviderMessageId,
            AccessGrantId = normalizedResult.AccessGrantId ?? job.AccessGrantId,
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
        ReportingDeliveryJobRecord job,
        ReportingDeliveryReceiptKind receiptKind) =>
        job.State switch
        {
            ReportingDeliveryState.Delivered => ReportingDeliveryState.Delivered,
            ReportingDeliveryState.Failed => ReportingDeliveryState.Failed,
            ReportingDeliveryState.Blocked when !IsAwaitingProviderOutcome(job) => ReportingDeliveryState.Blocked,
            _ => receiptKind switch
            {
                ReportingDeliveryReceiptKind.Delivered or
                ReportingDeliveryReceiptKind.Accessed or
                ReportingDeliveryReceiptKind.Downloaded => ReportingDeliveryState.Delivered,
                ReportingDeliveryReceiptKind.Bounced or
                ReportingDeliveryReceiptKind.Rejected or
                ReportingDeliveryReceiptKind.Failed => ReportingDeliveryState.Failed,
                _ => ReportingDeliveryState.Sent
            }
        };

    private static bool IsAwaitingProviderOutcome(ReportingDeliveryJobRecord job) =>
        job.State == ReportingDeliveryState.Blocked
        && job.AccessGrantId is not null
        && job.ProviderMessageId is null
        && IsProviderOutcomeUnknownCode(job.LastErrorCode);

    private static bool IsProviderOutcomeUnknownCode(string? code) =>
        string.Equals(code, "RELAY_OUTCOME_UNKNOWN", StringComparison.Ordinal)
        || string.Equals(code, "TRANSPORT_CANCELLED", StringComparison.Ordinal);

    private static ReportingDeliveryReceipt BuildAttemptReceipt(
        ReportingDeliveryJobRecord job,
        ReportingDeliveryTransportResult result,
        DateTimeOffset occurredAtUtc,
        int attemptCount)
    {
        var kind = result.Outcome switch
        {
            ReportingDeliveryTransportOutcome.Sent => ReportingDeliveryReceiptKind.Sent,
            ReportingDeliveryTransportOutcome.Delivered => ReportingDeliveryReceiptKind.Delivered,
            _ => ReportingDeliveryReceiptKind.Failed
        };
        var receiptId = SecurePortalReportingDeliveryTransport.BuildStableReference(
            $"delivery-attempt-{attemptCount}",
            $"{job.IdempotencyKey}:{result.Code}:{(int)result.Outcome}");
        return new ReportingDeliveryReceipt(
            receiptId,
            kind,
            occurredAtUtc,
            job.TransportId,
            result.ProviderMessageId,
            EvidenceReference: job.ReleaseAuthorization.ReceiptId,
            Detail: result.Outcome is ReportingDeliveryTransportOutcome.TransientFailure
                or ReportingDeliveryTransportOutcome.PermanentFailure
                ? $"{result.Code}: {NormalizePersistedDetail(result.Detail) ?? "no provider detail"}"
                : result.Code);
    }

    private static bool IsTransient(Exception exception) =>
        exception is DbException
            or TimeoutException
            or TaskCanceledException
            or HttpRequestException
            or IOException;

    private static int ResolveProviderAttemptNumber(ReportingDeliveryJobRecord job) =>
        checked(1 + job.Receipts.Count(static receipt =>
            receipt.Kind == ReportingDeliveryReceiptKind.Failed
            && !IsStableProviderReplayReceipt(receipt)));

    private static bool IsStableProviderReplayReceipt(ReportingDeliveryReceipt receipt) =>
        receipt.Detail?.StartsWith("RELAY_OUTCOME_UNKNOWN:", StringComparison.Ordinal) == true
        || receipt.Detail?.StartsWith("TRANSPORT_CANCELLED:", StringComparison.Ordinal) == true;

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
        if (!Enum.IsDefined(payload.RecipientKind))
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "Delivery recipient kind is invalid.");
        }
        ValidateTokenFreeUri(payload.PortalUri, nameof(payload.PortalUri), requireAbsolute: false);
        if (payload.ExternalAccess is { } access)
        {
            NormalizeRequired(access.Audience, nameof(access.Audience));
            if (!Enum.IsDefined(access.AudienceKind)
                || access.AudienceKind != payload.RecipientKind)
            {
                throw new ArgumentException(
                    "Delivery recipient and external access principal kinds must match.",
                    nameof(payload));
            }
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
        NormalizeRequired(release.RunId, nameof(release.RunId));
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

    private static string ComputePayloadHash(ReportingDeliveryPayload payload)
    {
        IEnumerable<string> artifacts = payload.ExternalAccess?.ArtifactIds?
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .OrderBy(static item => item, StringComparer.Ordinal)
            ?? Enumerable.Empty<string>();
        var canonical = string.Join(
            "\u001f",
            payload.Recipient,
            payload.RecipientRole,
            payload.Destination,
            payload.Subject,
            payload.Body,
            payload.PortalUri,
            ((int)payload.RecipientKind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload.ExternalAccess?.Audience ?? string.Empty,
            payload.ExternalAccess is null
                ? string.Empty
                : ((int)payload.ExternalAccess.AudienceKind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload.ExternalAccess?.AccessBaseUri ?? string.Empty,
            payload.ExternalAccess?.Lifetime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            payload.ExternalAccess?.AllowPackageRead.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            payload.ExternalAccess?.MaxUses.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            string.Join("\u001e", artifacts));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string NormalizeErrorCode(string? code)
    {
        var normalized = new string((code ?? "UNVERIFIED")
            .Trim()
            .ToUpperInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '_')
            .Take(64)
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "UNVERIFIED" : normalized;
    }

    private static string? NormalizePersistedDetail(string? detail)
    {
        var normalized = detail?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("#token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return "Transport detail was suppressed because it contained credential-shaped material.";
        }

        return normalized.Length <= 4_096 ? normalized : normalized[..4_096];
    }

    private static ReportingDeliveryReceipt NormalizeReceipt(ReportingDeliveryReceipt receipt) =>
        receipt with
        {
            ReceiptId = NormalizeRequired(receipt.ReceiptId, nameof(receipt.ReceiptId)),
            TransportId = NormalizeRequired(receipt.TransportId, nameof(receipt.TransportId)),
            ProviderReference = NormalizeProviderReference(receipt.ProviderReference),
            EvidenceReference = NormalizePersistedDetail(receipt.EvidenceReference),
            Detail = NormalizePersistedDetail(receipt.Detail)
        };

    private static ReportingDeliveryTransportResult NormalizeTransportResult(
        ReportingDeliveryTransportResult result)
    {
        if (!TryNormalizeIdentifier(
                result.ProviderMessageId,
                ReportingDistributionValueLimits.ProviderMessageIdLength,
                out var providerMessageId)
            || !TryNormalizeIdentifier(result.AccessGrantId, 256, out var accessGrantId)
            || result.Receipt is { } receipt
            && !TryNormalizeIdentifier(
                receipt.ProviderReference,
                ReportingDistributionValueLimits.ProviderMessageIdLength,
                out _))
        {
            return ReportingDeliveryTransportResult.PermanentFailure(
                "TRANSPORT_IDENTIFIER_INVALID",
                $"Transport identifiers cannot exceed {ReportingDistributionValueLimits.ProviderMessageIdLength} characters or contain credential-shaped material.");
        }

        return result with
        {
            ProviderMessageId = providerMessageId,
            AccessGrantId = accessGrantId
        };
    }

    private static string? NormalizeProviderReference(string? value)
    {
        if (!TryNormalizeIdentifier(
                value,
                ReportingDistributionValueLimits.ProviderMessageIdLength,
                out var normalized))
        {
            throw new ArgumentException(
                $"Provider references cannot exceed {ReportingDistributionValueLimits.ProviderMessageIdLength} characters or contain credential-shaped material.",
                nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeIdentifier(string? value)
    {
        _ = TryNormalizeIdentifier(value, 256, out var normalized);
        return normalized;
    }

    private static bool TryNormalizeIdentifier(
        string? value,
        int maximumLength,
        out string? normalized)
    {
        normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = null;
            return true;
        }

        if (normalized.Length > maximumLength
            || normalized.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("#token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = null;
            return false;
        }

        return true;
    }

    private static bool ReceiptEquals(
        ReportingDeliveryReceipt left,
        ReportingDeliveryReceipt right) =>
        left.Kind == right.Kind
        && left.OccurredAtUtc.Equals(right.OccurredAtUtc)
        && string.Equals(left.TransportId, right.TransportId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ProviderReference, right.ProviderReference, StringComparison.Ordinal)
        && string.Equals(left.EvidenceReference, right.EvidenceReference, StringComparison.Ordinal)
        && string.Equals(left.Detail, right.Detail, StringComparison.Ordinal);
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

public sealed class HttpRelayReportingDeliveryTransport :
    IReportingDeliveryTransport,
    IReportingDeliveryAttemptBindingProvider
{
    private readonly IReportingHttpRelayClient _relayClient;
    private readonly ReportingAccessGrantService _accessGrants;
    private readonly IReportingDeliveryGrantCredentialDeriver _credentialDeriver;
    private readonly TimeProvider _timeProvider;

    public HttpRelayReportingDeliveryTransport(
        IReportingHttpRelayClient relayClient,
        ReportingAccessGrantService accessGrants,
        IReportingDeliveryGrantCredentialDeriver credentialDeriver,
        TimeProvider? timeProvider = null,
        string transportId = "http-relay")
    {
        _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
        _accessGrants = accessGrants ?? throw new ArgumentNullException(nameof(accessGrants));
        _credentialDeriver = credentialDeriver ?? throw new ArgumentNullException(nameof(credentialDeriver));
        _timeProvider = timeProvider ?? TimeProvider.System;
        TransportId = string.IsNullOrWhiteSpace(transportId)
            ? throw new ArgumentException("Transport id is required.", nameof(transportId))
            : transportId.Trim();
    }

    public string TransportId { get; }

    public string ResolveAccessGrantId(ReportingDeliveryTransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var attemptIdempotencyKey = BuildAttemptIdempotencyKey(
            request.IdempotencyKey,
            request.AttemptNumber);
        return _credentialDeriver.Derive(attemptIdempotencyKey).GrantId;
    }

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

        var attemptIdempotencyKey = BuildAttemptIdempotencyKey(
            request.IdempotencyKey,
            request.AttemptNumber);
        ReportingAccessGrantSecret? secret = null;
        try
        {
            var credential = _credentialDeriver.Derive(attemptIdempotencyKey);
            secret = await _accessGrants.IssueIdempotentAsync(
                new ReportingAccessGrantIssueRequest(
                    request.TenantId,
                    access.Audience,
                    request.ReleaseAuthorization.RunId,
                    request.PackageId,
                    _timeProvider.GetUtcNow().Add(access.Lifetime),
                    access.AllowPackageRead,
                    access.ArtifactIds,
                    access.MaxUses,
                    access.AudienceKind),
                credential,
                ct).ConfigureAwait(false);
            var selectedArtifactId = access.ArtifactIds?.Count == 1 ? access.ArtifactIds[0] : null;
            var recipientAccessUri = ReportingAccessGrantService.BuildRecipientAccessUri(
                access.AccessBaseUri,
                secret,
                selectedArtifactId);
            ReportingHttpRelayResult relayResult;
            try
            {
                relayResult = await _relayClient.SendAsync(
                    new ReportingHttpRelayMessage(
                        request.TenantId,
                        request.PackageId,
                        request.Payload.Destination,
                        request.Payload.Subject,
                        request.Payload.Body,
                        recipientAccessUri,
                        attemptIdempotencyKey,
                        request.JobId,
                        BuildReceiptCallbackPath(TransportId, request.JobId)),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsUnknownProviderOutcome(exception))
            {
                return UnknownProviderOutcome(
                    secret.GrantId,
                    $"Relay acceptance was not observed after {exception.GetType().Name}; the same provider idempotency key and access credential will be replayed.");
            }

            if (!relayResult.IsSuccess)
            {
                if (relayResult.IsTransientFailure)
                {
                    return UnknownProviderOutcome(
                        secret.GrantId,
                        $"Relay returned transient result {relayResult.Code}; the same provider idempotency key and access credential will be replayed.");
                }

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

                return ReportingDeliveryTransportResult.PermanentFailure(relayResult.Code, relayResult.Detail);
            }

            if (!TryNormalizeProviderMessageId(relayResult.ProviderMessageId, out var providerMessageId))
            {
                return UnknownProviderOutcome(
                    secret.GrantId,
                    $"Relay returned a missing or invalid provider message id; the same provider idempotency key and access credential will be replayed.");
            }

            var receipt = new ReportingDeliveryReceipt(
                SecurePortalReportingDeliveryTransport.BuildStableReference("relay-accepted", attemptIdempotencyKey),
                ReportingDeliveryReceiptKind.Accepted,
                _timeProvider.GetUtcNow(),
                TransportId,
                providerMessageId,
                Detail: "External relay accepted the notification; recipient delivery is not yet proven.");
            return ReportingDeliveryTransportResult.Sent(
                relayResult.Code,
                providerMessageId,
                secret.GrantId,
                receipt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Once a provider call may have started, caller cancellation cannot prove that the
            // provider rejected the notification. Retain the deterministic credential for an exact
            // replay, but keep cancellation distinct from a relay timeout in durable diagnostics.
            return new ReportingDeliveryTransportResult(
                ReportingDeliveryTransportOutcome.TransientFailure,
                "TRANSPORT_CANCELLED",
                secret is null
                    ? "Relay dispatch was cancelled before an access grant was retained; the same provider attempt will be replayed."
                    : "Relay dispatch was cancelled before provider acceptance was retained; the same provider idempotency key and access credential will be replayed.",
                AccessGrantId: secret?.GrantId);
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

    private static string BuildReceiptCallbackPath(string transportId, string jobId) =>
        $"/hooks/reporting/distribution/{Uri.EscapeDataString(transportId)}/deliveries/{Uri.EscapeDataString(jobId)}/receipts";

    private static ReportingDeliveryTransportResult UnknownProviderOutcome(
        string accessGrantId,
        string detail) =>
        new(
            ReportingDeliveryTransportOutcome.TransientFailure,
            "RELAY_OUTCOME_UNKNOWN",
            detail,
            AccessGrantId: accessGrantId);

    private static bool IsUnknownProviderOutcome(Exception exception) =>
        exception is TimeoutException
            or TaskCanceledException
            or HttpRequestException
            or IOException;

    private static bool TryNormalizeProviderMessageId(string? value, out string? normalized)
    {
        normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > ReportingDistributionValueLimits.ProviderMessageIdLength
            || normalized.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = null;
            return false;
        }

        return true;
    }

    private static string BuildAttemptIdempotencyKey(string deliveryIdempotencyKey, int attemptNumber)
    {
        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        var canonical = string.Join(
            "\u001f",
            deliveryIdempotencyKey,
            attemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
