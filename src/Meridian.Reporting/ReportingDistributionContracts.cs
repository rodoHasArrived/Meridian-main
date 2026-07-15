namespace Meridian.Reporting;

public enum ReportingAccessGrantValidationStatus
{
    Valid = 0,
    NotFound = 1,
    TokenMismatch = 2,
    TenantMismatch = 3,
    AudienceMismatch = 4,
    PackageMismatch = 5,
    ArtifactOutOfScope = 6,
    Expired = 7,
    Revoked = 8,
    UseLimitExceeded = 9,
    ConcurrencyConflict = 10
}

public sealed record ReportingAccessGrantRecord(
    string GrantId,
    string TokenHashSha256,
    string TenantId,
    string Audience,
    string PackageId,
    bool AllowPackageRead,
    IReadOnlyList<string> ArtifactIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaxUses,
    int UseCount,
    DateTimeOffset? LastUsedAtUtc = null,
    DateTimeOffset? RevokedAtUtc = null,
    string? RevokedBy = null,
    string? RevocationReason = null,
    long Version = 0);

public sealed record ReportingAccessGrantIssueRequest(
    string TenantId,
    string Audience,
    string PackageId,
    DateTimeOffset ExpiresAtUtc,
    bool AllowPackageRead = true,
    IReadOnlyList<string>? ArtifactIds = null,
    int MaxUses = 1);

/// <summary>
/// Contains the one-time plaintext bearer returned at issuance. Only the corresponding
/// <see cref="ReportingAccessGrantRecord.TokenHashSha256"/> may be retained by a store.
/// </summary>
public sealed record ReportingAccessGrantSecret(
    string GrantId,
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record ReportingAccessGrantValidationRequest(
    string GrantId,
    string Token,
    string TenantId,
    string Audience,
    string PackageId,
    string? ArtifactId = null,
    bool ConsumeUse = true);

public sealed record ReportingAccessGrantValidationResult(
    ReportingAccessGrantValidationStatus Status,
    ReportingAccessGrantRecord? Grant = null)
{
    public bool IsValid => Status == ReportingAccessGrantValidationStatus.Valid;
}

public interface IReportingAccessGrantStore
{
    Task<ReportingAccessGrantRecord?> GetAsync(string grantId, CancellationToken ct = default);

    Task<bool> TryCreateAsync(ReportingAccessGrantRecord grant, CancellationToken ct = default);

    Task<bool> TryUpdateAsync(
        string grantId,
        long expectedVersion,
        ReportingAccessGrantRecord updatedGrant,
        CancellationToken ct = default);
}

public enum ReportingDeliveryState
{
    Queued = 0,
    Dispatching = 1,
    RetryScheduled = 2,
    Sent = 3,
    Delivered = 4,
    Blocked = 5,
    Failed = 6
}

public enum ReportingDeliveryReceiptKind
{
    Accepted = 0,
    Published = 1,
    Sent = 2,
    Delivered = 3,
    Accessed = 4,
    Downloaded = 5,
    Bounced = 6,
    Rejected = 7
}

public enum ReportingDeliveryTransportOutcome
{
    Sent = 0,
    Delivered = 1,
    TransientFailure = 2,
    PermanentFailure = 3
}

public enum ReportingReleaseState
{
    Draft = 0,
    Approved = 1,
    Released = 2,
    Revoked = 3
}

public sealed record ReportingReleasedArtifactReference(
    string ArtifactId,
    string RetainedUri,
    string ContentHashSha256,
    long ByteSize);

/// <summary>
/// Server-issued authorization binding a delivery to one released, immutable package version.
/// The authorization proof is verified through <see cref="IReportingReleaseAuthorizationVerifier"/>
/// before a delivery job can be created.
/// </summary>
public sealed record ReportingDeliveryReleaseAuthorization(
    string ReceiptId,
    ReportingReleaseState State,
    string TenantId,
    string PackageId,
    string ReleaseVersion,
    string ArtifactManifestHashSha256,
    IReadOnlyList<ReportingReleasedArtifactReference> Artifacts,
    IReadOnlyList<string> EvidenceReferences,
    DateTimeOffset ReleasedAtUtc,
    string ReleasedBy,
    string AuthorizationProof);

public sealed record ReportingReleaseAuthorizationResult(
    bool IsAuthorized,
    string Code,
    string? Detail = null);

public interface IReportingReleaseAuthorizationVerifier
{
    Task<ReportingReleaseAuthorizationResult> VerifyAsync(
        ReportingDeliveryReleaseAuthorization authorization,
        CancellationToken ct = default);
}

public sealed record ReportingDeliveryReceipt(
    string ReceiptId,
    ReportingDeliveryReceiptKind Kind,
    DateTimeOffset OccurredAtUtc,
    string TransportId,
    string? ProviderReference = null,
    string? EvidenceReference = null,
    string? Detail = null);

public sealed record ReportingDeliveryAccessPolicy(
    string Audience,
    string AccessBaseUri,
    TimeSpan Lifetime,
    bool AllowPackageRead = true,
    IReadOnlyList<string>? ArtifactIds = null,
    int MaxUses = 1);

/// <summary>
/// Durable, non-secret delivery content. Recipient access bearers are issued by a transport at
/// dispatch time and must never be added to this payload.
/// </summary>
public sealed record ReportingDeliveryPayload(
    string Recipient,
    string RecipientRole,
    string Destination,
    string Subject,
    string Body,
    string PortalUri,
    ReportingDeliveryAccessPolicy? ExternalAccess = null);

public sealed record ReportingDeliveryQueueRequest(
    string TenantId,
    string PackageId,
    ReportingDeliveryReleaseAuthorization? ReleaseAuthorization,
    string DistributionId,
    string TransportId,
    string RequestedBy,
    ReportingDeliveryPayload Payload,
    int MaxAttempts = 3);

public sealed record ReportingDeliveryJobRecord(
    string JobId,
    string TenantId,
    string PackageId,
    string DistributionId,
    string TransportId,
    ReportingDeliveryReleaseAuthorization ReleaseAuthorization,
    string RequestedBy,
    string IdempotencyKey,
    ReportingDeliveryPayload Payload,
    ReportingDeliveryState State,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAtUtc,
    string? LastErrorCode,
    string? LastError,
    string? ProviderMessageId,
    string? AccessGrantId,
    IReadOnlyList<ReportingDeliveryReceipt> Receipts,
    long Version = 0);

public sealed record ReportingDeliveryTransportRequest(
    string JobId,
    string TenantId,
    string PackageId,
    string DistributionId,
    string IdempotencyKey,
    int AttemptNumber,
    ReportingDeliveryReleaseAuthorization ReleaseAuthorization,
    ReportingDeliveryPayload Payload);

public sealed record ReportingDeliveryTransportResult(
    ReportingDeliveryTransportOutcome Outcome,
    string Code,
    string? Detail = null,
    string? ProviderMessageId = null,
    string? AccessGrantId = null,
    ReportingDeliveryReceipt? Receipt = null)
{
    public static ReportingDeliveryTransportResult Sent(
        string code,
        string? providerMessageId = null,
        string? accessGrantId = null,
        ReportingDeliveryReceipt? receipt = null) =>
        new(ReportingDeliveryTransportOutcome.Sent, code, ProviderMessageId: providerMessageId, AccessGrantId: accessGrantId, Receipt: receipt);

    public static ReportingDeliveryTransportResult Delivered(
        string code,
        string? providerMessageId = null,
        string? accessGrantId = null,
        ReportingDeliveryReceipt? receipt = null) =>
        new(ReportingDeliveryTransportOutcome.Delivered, code, ProviderMessageId: providerMessageId, AccessGrantId: accessGrantId, Receipt: receipt);

    public static ReportingDeliveryTransportResult TransientFailure(string code, string? detail = null) =>
        new(ReportingDeliveryTransportOutcome.TransientFailure, code, detail);

    public static ReportingDeliveryTransportResult PermanentFailure(string code, string? detail = null) =>
        new(ReportingDeliveryTransportOutcome.PermanentFailure, code, detail);
}

public interface IReportingDeliveryTransport
{
    string TransportId { get; }

    Task<ReportingDeliveryTransportResult> DeliverAsync(
        ReportingDeliveryTransportRequest request,
        CancellationToken ct = default);
}

public interface IReportingDeliveryStore
{
    Task<ReportingDeliveryJobRecord?> GetAsync(string jobId, CancellationToken ct = default);

    Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken ct = default);

    Task<bool> TryCreateAsync(ReportingDeliveryJobRecord job, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims due queued, retry-scheduled, or lease-expired dispatching jobs. Returned
    /// rows must be in <see cref="ReportingDeliveryState.Dispatching"/> with an incremented version.
    /// </summary>
    Task<IReadOnlyList<ReportingDeliveryJobRecord>> ClaimDueAsync(
        DateTimeOffset nowUtc,
        string leaseOwner,
        TimeSpan leaseDuration,
        int take,
        CancellationToken ct = default);

    Task<bool> TryUpdateAsync(
        string jobId,
        long expectedVersion,
        ReportingDeliveryJobRecord updatedJob,
        CancellationToken ct = default);
}

public sealed record ReportingDeliveryDispatcherOptions(
    TimeSpan LeaseDuration,
    TimeSpan BaseRetryDelay,
    TimeSpan MaximumRetryDelay,
    int BatchSize = 25)
{
    public static ReportingDeliveryDispatcherOptions Default { get; } = new(
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(15));
}

public sealed record ReportingHttpRelayMessage(
    string TenantId,
    string PackageId,
    string Destination,
    string Subject,
    string Body,
    string RecipientAccessUri,
    string IdempotencyKey);

public sealed record ReportingHttpRelayResult(
    bool IsSuccess,
    bool IsTransientFailure,
    string Code,
    string? ProviderMessageId = null,
    string? Detail = null);

public interface IReportingHttpRelayClient
{
    Task<ReportingHttpRelayResult> SendAsync(
        ReportingHttpRelayMessage message,
        CancellationToken ct = default);
}
