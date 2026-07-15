using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>Server-resolved authority for one secure reporting distribution command.</summary>
public sealed record ReportingDistributionAuthority(
    string ActorId,
    string TenantId,
    string? CompanyId,
    ImmutableArray<string> PrincipalIds,
    bool CanView,
    bool CanDeliver,
    bool CanAdminister,
    string CorrelationId,
    ReportingAccessPrincipalScope? DelegatedPrincipal = null);

public sealed record SecureReportingDeliveryQueueCommand(
    string RunId,
    string DistributionId,
    string TransportId,
    string? RecipientPrincipalId,
    string Destination,
    string Subject,
    string Body,
    IReadOnlyList<string>? ArtifactIds = null,
    int? GrantLifetimeSeconds = null,
    int? GrantMaxUses = null,
    int MaxAttempts = 3,
    ReportingAccessPrincipalKind? RecipientPrincipalKind = null);

public sealed record SecureReportingDeliveryReceiptCommand(
    string ProviderEventId,
    ReportingDeliveryReceiptKind Kind,
    DateTimeOffset? OccurredAtUtc = null,
    string? ProviderReference = null,
    string? EvidenceReference = null,
    string? Detail = null);

public sealed record SecureReportingGrantIssueCommand(
    string RunId,
    string? RecipientPrincipalId,
    IReadOnlyList<string>? ArtifactIds = null,
    int? LifetimeSeconds = null,
    int? MaxUses = null,
    ReportingAccessPrincipalKind? RecipientPrincipalKind = null);

/// <summary>
/// The bearer is returned exactly once inside <see cref="RecipientAccessUri"/>'s fragment and is
/// not duplicated into a second result property or persisted by the service.
/// </summary>
public sealed record SecureReportingGrantIssueResult(
    string GrantId,
    string RecipientAccessUri,
    DateTimeOffset ExpiresAtUtc,
    string Audience,
    string RunId,
    string PackageId,
    IReadOnlyList<string> ArtifactIds,
    ReportingAccessPrincipalKind AudienceKind = ReportingAccessPrincipalKind.User);

public sealed record SecureReportingGrantExchangeCommand(string BearerToken, string ArtifactId);

/// <summary>Non-secret operator view of a durable recipient access grant.</summary>
public sealed record SecureReportingAccessGrantSummary(
    string GrantId,
    string RunId,
    string PackageId,
    string Audience,
    bool AllowPackageRead,
    IReadOnlyList<string> ArtifactIds,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaxUses,
    int UseCount,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedBy,
    string? RevocationReason,
    ReportingAccessPrincipalKind AudienceKind = ReportingAccessPrincipalKind.User);

/// <summary>Credential-free transport readiness exposed to authenticated reporting operators.</summary>
public sealed record SecureReportingTransportCapability(
    string TransportId,
    string DisplayName,
    string DeliveryMode,
    bool IsExternal,
    // True only when the caller must supply an authoritative destination. Governed external
    // transports resolve destinations server-side, so their field is an optional assertion.
    bool RequiresDestination,
    bool UsesGovernedRecipientScope,
    bool IssuesAccessGrant,
    bool SupportsProviderReceipts,
    bool IsConfigured,
    bool IsInfrastructureReady,
    string? InfrastructureDisabledReasonCode,
    bool IsReady,
    string? DisabledReasonCode);

/// <summary>Caller-specific distribution action readiness plus configured transport truth.</summary>
public sealed record SecureReportingDistributionCapabilityCatalog(
    bool CanQueueDelivery,
    bool CanIssueAccessGrant,
    bool CanRevokeAccessGrant,
    string? ActionDisabledReasonCode,
    IReadOnlyList<SecureReportingTransportCapability> Transports);

/// <summary>
/// Complete server-side identity used to resolve an external notification destination. The
/// public delivery command may assert the resolved value, but it is never authoritative.
/// </summary>
public sealed record ReportingRecipientDestinationRequest(
    string TenantId,
    string? CompanyId,
    string PrincipalId,
    string TransportId,
    ReportingAccessPrincipalKind PrincipalKind = ReportingAccessPrincipalKind.User);

public sealed record ReportingRecipientDestinationBinding(
    string TenantId,
    string? CompanyId,
    string PrincipalId,
    string TransportId,
    string Destination,
    ReportingAccessPrincipalKind PrincipalKind = ReportingAccessPrincipalKind.User);

/// <summary>
/// Resolves transport destinations from a governed principal. Deployments must replace the
/// rejecting implementation with their tenant-bound identity or recipient directory adapter.
/// </summary>
public interface IReportingRecipientDestinationResolver
{
    bool IsConfigured { get; }

    ValueTask<string?> ResolveDestinationAsync(
        ReportingRecipientDestinationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class RejectingReportingRecipientDestinationResolver : IReportingRecipientDestinationResolver
{
    public bool IsConfigured => false;

    public ValueTask<string?> ResolveDestinationAsync(
        ReportingRecipientDestinationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
}

/// <summary>
/// Exact-scope production resolver populated from deployment-owned configuration. No tenant,
/// company, principal, or transport fallback is allowed, and duplicate keys fail at startup.
/// </summary>
public sealed class ConfiguredReportingRecipientDestinationResolver : IReportingRecipientDestinationResolver
{
    private readonly IReadOnlyDictionary<DestinationKey, string> _destinations;

    public ConfiguredReportingRecipientDestinationResolver(
        IEnumerable<ReportingRecipientDestinationBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var destinations = new Dictionary<DestinationKey, string>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            var key = DestinationKey.Create(
                binding.TenantId,
                binding.CompanyId,
                binding.PrincipalId,
                binding.TransportId,
                binding.PrincipalKind);
            var destination = RequireConfiguredValue(
                binding.Destination,
                nameof(binding.Destination),
                MaximumDestinationLength);
            if (ContainsConfiguredBearer(destination))
            {
                throw new ArgumentException(
                    "Configured reporting recipient destinations cannot contain bearer-shaped material.",
                    nameof(bindings));
            }

            if (!destinations.TryAdd(key, destination))
            {
                throw new ArgumentException(
                    "Reporting recipient destination configuration contains a duplicate tenant/company/principal/transport binding.",
                    nameof(bindings));
            }
        }

        if (destinations.Count == 0)
        {
            throw new ArgumentException(
                "Configured reporting recipient destinations must contain at least one exact-scope binding.",
                nameof(bindings));
        }

        _destinations = destinations;
    }

    public bool IsConfigured => true;

    public ValueTask<string?> ResolveDestinationAsync(
        ReportingRecipientDestinationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var key = DestinationKey.Create(
            request.TenantId,
            request.CompanyId,
            request.PrincipalId,
            request.TransportId,
            request.PrincipalKind);
        return ValueTask.FromResult(_destinations.GetValueOrDefault(key));
    }

    private const int MaximumDestinationLength = 2_048;

    private static string RequireConfiguredValue(string? value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Configured reporting value '{parameterName}' is required and cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeConfiguredOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"Configured reporting values cannot exceed {maximumLength} characters.");
    }

    private static bool ContainsConfiguredBearer(string value) =>
        value.Contains("token=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("#token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("bearer ", StringComparison.OrdinalIgnoreCase);

    private readonly record struct DestinationKey(
        string TenantId,
        string? CompanyId,
        string PrincipalId,
        string TransportId,
        ReportingAccessPrincipalKind PrincipalKind)
    {
        public static DestinationKey Create(
            string tenantId,
            string? companyId,
            string principalId,
            string transportId,
            ReportingAccessPrincipalKind principalKind)
        {
            if (!Enum.IsDefined(principalKind))
            {
                throw new ArgumentOutOfRangeException(nameof(principalKind));
            }

            return
            new(
                RequireConfiguredValue(tenantId, nameof(tenantId), 256),
                NormalizeConfiguredOptional(companyId, 256),
                RequireConfiguredValue(principalId, nameof(principalId), 256).ToUpperInvariant(),
                RequireConfiguredValue(transportId, nameof(transportId), 128).ToLowerInvariant(),
                principalKind);
        }
    }
}

public sealed record SecureReportingDistributionOptions(
    string PortalPackageBasePath,
    string GrantExchangeBasePath,
    string? ExternalAccessBaseUri,
    TimeSpan DefaultGrantLifetime,
    TimeSpan MaximumGrantLifetime,
    int DefaultGrantMaxUses,
    int MaximumGrantMaxUses,
    string WorkerId,
    TimeSpan WorkerPollInterval,
    IReadOnlySet<string> ExternalGrantTransportIds)
{
    public static SecureReportingDistributionOptions Default { get; } = new(
        "/portal/reporting/secure/packages",
        "/portal/reporting/access-grants",
        ExternalAccessBaseUri: null,
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(24),
        DefaultGrantMaxUses: 1,
        MaximumGrantMaxUses: 100,
        WorkerId: Environment.MachineName,
        WorkerPollInterval: TimeSpan.FromSeconds(5),
        ExternalGrantTransportIds: new HashSet<string>(["http-relay"], StringComparer.OrdinalIgnoreCase));
}

public sealed class SecureReportingAccessGrantDeniedException : UnauthorizedAccessException
{
    public SecureReportingAccessGrantDeniedException(ReportingAccessGrantValidationStatus status)
        : base("The reporting access grant is invalid or no longer available.")
    {
        Status = status;
    }

    public ReportingAccessGrantValidationStatus Status { get; }
}

/// <summary>
/// Canonical application boundary for reporting distribution. It derives actor and tenant from
/// authenticated server context, derives the audience from the immutable access-policy snapshot,
/// reconstructs release authority from governed state, and requires tenant-keyed artifact catalog
/// matches before creating any outbox job or access grant.
/// </summary>
public sealed class ReportingSecureDistributionApplicationService
{
    private const int MaximumTextLength = 16_384;
    private const int MaximumDestinationLength = 2_048;
    private readonly IReportingGovernanceRepository _governanceRepository;
    private readonly IReportingArtifactCatalog _artifactCatalog;
    private readonly ReportingDeliveryDispatcher _dispatcher;
    private readonly IReportingDeliveryStore _deliveryStore;
    private readonly ReportingAccessGrantService _accessGrantService;
    private readonly IReportingAccessGrantStore _accessGrantStore;
    private readonly ReportingArtifactVaultService _artifactVault;
    private readonly IReportingReleaseAuthorizationVerifier _releaseVerifier;
    private readonly IReportingProviderReceiptAuthenticator _receiptAuthenticator;
    private readonly IReportingRecipientDestinationResolver _destinationResolver;
    private readonly TimeProvider _timeProvider;
    private readonly SecureReportingDistributionOptions _options;

    public ReportingSecureDistributionApplicationService(
        IReportingGovernanceRepository governanceRepository,
        IReportingArtifactCatalog artifactCatalog,
        ReportingDeliveryDispatcher dispatcher,
        IReportingDeliveryStore deliveryStore,
        ReportingAccessGrantService accessGrantService,
        IReportingAccessGrantStore accessGrantStore,
        ReportingArtifactVaultService artifactVault,
        IReportingReleaseAuthorizationVerifier releaseVerifier,
        IReportingProviderReceiptAuthenticator receiptAuthenticator,
        TimeProvider? timeProvider = null,
        SecureReportingDistributionOptions? options = null,
        IReportingRecipientDestinationResolver? destinationResolver = null)
    {
        _governanceRepository = governanceRepository ?? throw new ArgumentNullException(nameof(governanceRepository));
        _artifactCatalog = artifactCatalog ?? throw new ArgumentNullException(nameof(artifactCatalog));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _deliveryStore = deliveryStore ?? throw new ArgumentNullException(nameof(deliveryStore));
        _accessGrantService = accessGrantService ?? throw new ArgumentNullException(nameof(accessGrantService));
        _accessGrantStore = accessGrantStore ?? throw new ArgumentNullException(nameof(accessGrantStore));
        _artifactVault = artifactVault ?? throw new ArgumentNullException(nameof(artifactVault));
        _releaseVerifier = releaseVerifier ?? throw new ArgumentNullException(nameof(releaseVerifier));
        _receiptAuthenticator = receiptAuthenticator ?? throw new ArgumentNullException(nameof(receiptAuthenticator));
        _destinationResolver = destinationResolver ?? new RejectingReportingRecipientDestinationResolver();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = ValidateOptions(options ?? SecureReportingDistributionOptions.Default);
    }

    public async Task<ReportingDeliveryJobRecord> QueueDeliveryAsync(
        SecureReportingDeliveryQueueCommand command,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateAuthority(authority, requireView: false, requireDeliver: true, requireAdmin: false);
        var run = await GetReleasedRunAsync(authority.TenantId, command.RunId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        var audience = ResolveAudience(
            run.Access,
            authority,
            command.RecipientPrincipalId,
            command.RecipientPrincipalKind);
        var packageId = ReportingArtifactPackageIdentity.Create(run);
        var artifacts = await ResolveArtifactsAsync(run, packageId, command.ArtifactIds, ct).ConfigureAwait(false);
        var transportId = NormalizeRequired(command.TransportId, nameof(command.TransportId), 128);
        var transport = BuildTransportCapabilities(authority)
            .SingleOrDefault(item => string.Equals(item.TransportId, transportId, StringComparison.OrdinalIgnoreCase));
        if (transport is null || !transport.IsReady)
        {
            throw new InvalidOperationException(
                $"Reporting transport '{transportId}' is unavailable ({transport?.DisabledReasonCode ?? "ADAPTER_NOT_CONFIGURED"}).");
        }
        var portalUri = BuildPath(_options.PortalPackageBasePath, run.RunId);
        ReportingDeliveryAccessPolicy? externalAccess = null;
        var isExternalTransport = _options.ExternalGrantTransportIds.Contains(transportId);
        if (isExternalTransport)
        {
            if (string.IsNullOrWhiteSpace(_options.ExternalAccessBaseUri))
            {
                throw new InvalidOperationException(
                    $"External reporting transport '{transportId}' is disabled until a secure access base URI is configured.");
            }

            externalAccess = new ReportingDeliveryAccessPolicy(
                audience.PrincipalId,
                _options.ExternalAccessBaseUri,
                ResolveLifetime(command.GrantLifetimeSeconds),
                AllowPackageRead: false,
                artifacts.Select(static artifact => artifact.ArtifactId).ToArray(),
                ResolveMaxUses(command.GrantMaxUses),
                audience.Kind);
        }

        var destination = isExternalTransport
            ? await ResolveExternalDestinationAsync(
                    authority,
                    audience,
                    transportId,
                    command.Destination,
                    ct)
                .ConfigureAwait(false)
            : NormalizeOptionalTokenFree(command.Destination, nameof(command.Destination), MaximumDestinationLength)
              ?? audience.PrincipalId;
        var payload = new ReportingDeliveryPayload(
            Recipient: audience.PrincipalId,
            RecipientRole: run.Access.Mode.ToString(),
            Destination: destination,
            Subject: NormalizeTokenFree(command.Subject, nameof(command.Subject), 1_024),
            Body: NormalizeTokenFree(command.Body, nameof(command.Body), MaximumTextLength),
            PortalUri: portalUri,
            ExternalAccess: externalAccess,
            RecipientKind: audience.Kind);
        var releaseAuthorization = ReportingDeliveryReleaseAuthorizationFactory.Create(run);
        return await _dispatcher.QueueAsync(
            new ReportingDeliveryQueueRequest(
                authority.TenantId,
                packageId,
                releaseAuthorization,
                NormalizeRequired(command.DistributionId, nameof(command.DistributionId), 256),
                transportId,
                authority.ActorId,
                payload,
                command.MaxAttempts),
            ct).ConfigureAwait(false);
    }

    public async Task<ReportingDeliveryJobRecord> GetDeliveryAsync(
        string jobId,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: true, requireDeliver: false, requireAdmin: false);
        var job = await GetJobInTenantAsync(jobId, authority.TenantId, ct).ConfigureAwait(false);
        var run = await GetGovernedRunAsync(authority.TenantId, job.ReleaseAuthorization.RunId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        ValidateDeliveryRunBinding(job, run);
        return job;
    }

    public async Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListDeliveriesAsync(
        string runId,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: true, requireDeliver: false, requireAdmin: false);
        var run = await GetGovernedRunAsync(authority.TenantId, runId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        var jobs = await _deliveryStore
            .ListByPackageAsync(authority.TenantId, ReportingArtifactPackageIdentity.Create(run), ct)
            .ConfigureAwait(false);
        foreach (var job in jobs)
        {
            ValidateDeliveryRunBinding(job, run);
        }

        return jobs;
    }

    public IReadOnlyList<SecureReportingTransportCapability> GetTransportCapabilities(
        ReportingDistributionAuthority authority)
    {
        ValidateAuthority(authority, requireView: true, requireDeliver: false, requireAdmin: false);
        return BuildTransportCapabilities(authority);
    }

    private IReadOnlyList<SecureReportingTransportCapability> BuildTransportCapabilities(
        ReportingDistributionAuthority authority)
    {
        var configured = _dispatcher.ConfiguredTransportIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = configured
            .Concat(_options.ExternalGrantTransportIds)
            .Append("secure-portal")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase);
        return ids.Select(id =>
        {
            var isExternal = _options.ExternalGrantTransportIds.Contains(id);
            var isConfigured = configured.Contains(id);
            var hasAccessBase = !isExternal || !string.IsNullOrWhiteSpace(_options.ExternalAccessBaseUri);
            var receiptAuthenticationReady = !string.Equals(id, "http-relay", StringComparison.OrdinalIgnoreCase)
                                             || _receiptAuthenticator is not RejectingReportingProviderReceiptAuthenticator;
            var recipientDirectoryReady = !isExternal || _destinationResolver.IsConfigured;
            var isInfrastructureReady = isConfigured
                                        && hasAccessBase
                                        && receiptAuthenticationReady
                                        && recipientDirectoryReady;
            var infrastructureReason = isInfrastructureReady
                ? null
                : !isConfigured
                    ? "ADAPTER_NOT_CONFIGURED"
                    : !hasAccessBase
                        ? "EXTERNAL_ACCESS_URI_NOT_CONFIGURED"
                        : !receiptAuthenticationReady
                            ? "PROVIDER_RECEIPT_AUTH_NOT_CONFIGURED"
                            : "RECIPIENT_DESTINATION_DIRECTORY_NOT_CONFIGURED";
            var isReady = isInfrastructureReady && authority.CanDeliver;
            var reason = !authority.CanDeliver
                ? "DELIVER_PERMISSION_REQUIRED"
                : infrastructureReason;
            var isHttpRelay = string.Equals(id, "http-relay", StringComparison.OrdinalIgnoreCase);
            return new SecureReportingTransportCapability(
                id,
                string.Equals(id, "secure-portal", StringComparison.OrdinalIgnoreCase)
                    ? "Secure portal"
                    : isHttpRelay
                        ? "HTTP notification relay"
                        : id,
                isExternal ? "ExternalNotification" : "SecurePortal",
                isExternal,
                RequiresDestination: false,
                UsesGovernedRecipientScope: true,
                IssuesAccessGrant: isExternal,
                SupportsProviderReceipts: isHttpRelay,
                IsConfigured: isConfigured,
                IsInfrastructureReady: isInfrastructureReady,
                InfrastructureDisabledReasonCode: infrastructureReason,
                IsReady: isReady,
                DisabledReasonCode: reason);
        }).ToArray();
    }

    public SecureReportingDistributionCapabilityCatalog GetDistributionCapabilities(
        ReportingDistributionAuthority authority)
    {
        var transports = GetTransportCapabilities(authority);
        var canDeliver = authority.CanDeliver;
        return new SecureReportingDistributionCapabilityCatalog(
            CanQueueDelivery: canDeliver,
            CanIssueAccessGrant: canDeliver,
            CanRevokeAccessGrant: canDeliver,
            ActionDisabledReasonCode: canDeliver ? null : "DELIVER_PERMISSION_REQUIRED",
            transports);
    }

    public async Task<ReportingDeliveryJobRecord> RecordVerifiedProviderReceiptAsync(
        string transportId,
        string jobId,
        SecureReportingDeliveryReceiptCommand command,
        ReportingProviderReceiptAuthentication authentication,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalizedTransportId = NormalizeRequired(transportId, nameof(transportId), 128);
        var normalizedJobId = NormalizeRequired(jobId, nameof(jobId), 256);
        var authenticated = await _receiptAuthenticator
            .AuthenticateAsync(
                new ReportingProviderReceiptAuthenticationRequest(
                    normalizedTransportId,
                    normalizedJobId,
                    command,
                    authentication),
                ct)
            .ConfigureAwait(false);
        if (!authenticated)
        {
            throw new UnauthorizedAccessException("Provider receipt authentication failed.");
        }

        var job = await _deliveryStore.GetAsync(normalizedJobId, ct).ConfigureAwait(false)
                  ?? throw new KeyNotFoundException("Reporting delivery job was not found.");
        if (!Same(job.TransportId, normalizedTransportId))
        {
            throw new UnauthorizedAccessException("Provider receipt transport does not match the delivery job.");
        }

        var eventId = NormalizeRequired(command.ProviderEventId, nameof(command.ProviderEventId), 512);
        var receiptId = ComputeSha256(string.Join(
            "\u001f",
            job.JobId,
            job.TransportId.ToLowerInvariant(),
            eventId));
        var isRetainedFailureReplay = job.State == ReportingDeliveryState.Failed
            && command.Kind is ReportingDeliveryReceiptKind.Bounced or ReportingDeliveryReceiptKind.Rejected
            && job.Receipts.Any(receipt => Same(receipt.ReceiptId, receiptId));

        var canEstablishProviderAcceptance =
            job.AccessGrantId is not null
            && (job.State == ReportingDeliveryState.Dispatching
                || job.State is ReportingDeliveryState.RetryScheduled or ReportingDeliveryState.Blocked
                && job.ProviderMessageId is null
                && IsProviderOutcomeUnknownCode(job.LastErrorCode));
        if (job.State is not (ReportingDeliveryState.Sent or ReportingDeliveryState.Delivered)
            && !canEstablishProviderAcceptance
            && !isRetainedFailureReplay)
        {
            throw new UnauthorizedAccessException(
                "Provider receipts require a dispatched delivery, a previously accepted delivery, or a specifically retained unknown provider outcome.");
        }

        if (!canEstablishProviderAcceptance && string.IsNullOrWhiteSpace(job.ProviderMessageId))
        {
            throw new UnauthorizedAccessException(
                "Provider receipts require a retained provider message id.");
        }

        if (command.Kind is not (
            ReportingDeliveryReceiptKind.Accepted or
            ReportingDeliveryReceiptKind.Sent or
            ReportingDeliveryReceiptKind.Delivered or
            ReportingDeliveryReceiptKind.Bounced or
            ReportingDeliveryReceiptKind.Rejected))
        {
            throw new ArgumentException(
                "The provider receipt kind is not accepted on the verified webhook path.",
                nameof(command));
        }

        if (command.OccurredAtUtc is null)
        {
            throw new ArgumentException(
                "Verified provider receipts require the provider event time.",
                nameof(command));
        }

        var occurredAt = command.OccurredAtUtc.Value.ToUniversalTime();
        if (occurredAt < job.CreatedAtUtc.AddMinutes(-5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Provider receipt time cannot predate the delivery job beyond the allowed provider clock skew.");
        }

        if (occurredAt > _timeProvider.GetUtcNow().AddMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Provider receipt time cannot be in the future.");
        }

        var providerReference = NormalizeOptionalTokenFree(
            command.ProviderReference,
            nameof(command.ProviderReference),
            ReportingDistributionValueLimits.ProviderMessageIdLength);
        if (providerReference is null
            || job.ProviderMessageId is not null && !Same(job.ProviderMessageId, providerReference))
        {
            throw new UnauthorizedAccessException(
                "Provider receipt message reference does not match the retained delivery provider message.");
        }

        var receipt = new ReportingDeliveryReceipt(
            receiptId,
            command.Kind,
            occurredAt,
            job.TransportId,
            providerReference,
            NormalizeOptionalTokenFree(command.EvidenceReference, nameof(command.EvidenceReference), 4_096),
            NormalizeOptionalTokenFree(command.Detail, nameof(command.Detail), MaximumTextLength));
        var retained = await _dispatcher
            .AppendReceiptAsync(job.JobId, job.TenantId, receipt, ct)
            .ConfigureAwait(false);
        if (command.Kind is ReportingDeliveryReceiptKind.Bounced or ReportingDeliveryReceiptKind.Rejected
            && job.AccessGrantId is not null)
        {
            var revoked = await _accessGrantService.RevokeAsync(
                    job.AccessGrantId,
                    job.TenantId,
                    "reporting-provider-receipt",
                    $"Provider retained a verified {command.Kind} receipt for delivery {job.JobId}.",
                    ct)
                .ConfigureAwait(false);
            if (!revoked)
            {
                throw new IOException(
                    "The provider failure receipt was retained, but linked access-grant revocation remains pending reconciliation.");
            }
        }

        return retained;
    }

    internal async Task<int> ReconcileFailedDeliveryAccessGrantsAsync(
        int take = 100,
        CancellationToken ct = default)
    {
        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        var candidates = await _deliveryStore
            .ListPendingAccessGrantRevocationsAsync(take, ct)
            .ConfigureAwait(false);
        var reconciled = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var revoked = await _accessGrantService.RevokeAsync(
                    candidate.AccessGrantId,
                    candidate.TenantId,
                    "reporting-provider-receipt-reconciler",
                    $"Reconciled retained failed provider receipt for delivery {candidate.JobId}.",
                    ct)
                .ConfigureAwait(false);
            if (!revoked)
            {
                throw new IOException(
                    "A retained failed provider receipt could not reconcile its linked access grant.");
            }

            reconciled++;
        }

        return reconciled;
    }

    public async Task<SecureReportingGrantIssueResult> IssueAccessGrantAsync(
        SecureReportingGrantIssueCommand command,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateAuthority(authority, requireView: false, requireDeliver: true, requireAdmin: false);
        var run = await GetReleasedRunAsync(authority.TenantId, command.RunId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        var audience = ResolveAudience(
            run.Access,
            authority,
            command.RecipientPrincipalId,
            command.RecipientPrincipalKind);
        var packageId = ReportingArtifactPackageIdentity.Create(run);
        var artifacts = await ResolveArtifactsAsync(run, packageId, command.ArtifactIds, ct).ConfigureAwait(false);
        var releaseVerification = await _releaseVerifier
            .VerifyAsync(ReportingDeliveryReleaseAuthorizationFactory.Create(run), ct)
            .ConfigureAwait(false);
        if (!releaseVerification.IsAuthorized)
        {
            throw new InvalidDataException(
                $"Released reporting bytes failed exact integrity verification ({releaseVerification.Code}).");
        }

        var expiresAt = _timeProvider.GetUtcNow().Add(ResolveLifetime(command.LifetimeSeconds));
        var artifactIds = artifacts.Select(static artifact => artifact.ArtifactId).ToArray();
        var secret = await _accessGrantService.IssueAsync(
            new ReportingAccessGrantIssueRequest(
                run.Scope.TenantId,
                audience.PrincipalId,
                run.RunId,
                packageId,
                expiresAt,
                AllowPackageRead: false,
                artifactIds,
                ResolveMaxUses(command.MaxUses),
                audience.Kind),
            ct).ConfigureAwait(false);

        return new SecureReportingGrantIssueResult(
            secret.GrantId,
            BuildFragmentAccessPath(
                BuildPath(_options.GrantExchangeBasePath, secret.GrantId, "exchange"),
                secret.Token,
                artifactIds.Length == 1 ? artifactIds[0] : null),
            secret.ExpiresAtUtc,
            audience.PrincipalId,
            run.RunId,
            packageId,
            artifactIds,
            audience.Kind);
    }

    public async Task<bool> RevokeAccessGrantAsync(
        string grantId,
        string reason,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: false, requireDeliver: true, requireAdmin: false);
        var normalizedGrantId = NormalizeRequired(grantId, nameof(grantId), 256);
        var existing = await _accessGrantStore.GetAsync(normalizedGrantId, ct).ConfigureAwait(false);
        if (existing is null || !Same(existing.TenantId, authority.TenantId))
        {
            return false;
        }

        var run = await GetGovernedRunAsync(authority.TenantId, existing.RunId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        ValidateGrantRunBinding(existing, run);

        return await _accessGrantService.RevokeAsync(
            normalizedGrantId,
            authority.TenantId,
            authority.ActorId,
            NormalizeTokenFree(reason, nameof(reason), 2_048),
            ct).ConfigureAwait(false);
    }

    public async Task<SecureReportingAccessGrantSummary> GetAccessGrantAsync(
        string grantId,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: false, requireDeliver: true, requireAdmin: false);
        var grant = await _accessGrantStore
            .GetAsync(NormalizeRequired(grantId, nameof(grantId), 256), ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Reporting access grant was not found.");
        if (!Same(grant.TenantId, authority.TenantId))
        {
            throw new UnauthorizedAccessException("Reporting access grant is outside the authenticated scope.");
        }

        var run = await GetGovernedRunAsync(authority.TenantId, grant.RunId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        ValidateGrantRunBinding(grant, run);
        return ProjectGrant(grant);
    }

    public async Task<IReadOnlyList<SecureReportingAccessGrantSummary>> ListAccessGrantsAsync(
        string runId,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: false, requireDeliver: true, requireAdmin: false);
        var run = await GetGovernedRunAsync(authority.TenantId, runId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        var packageId = ReportingArtifactPackageIdentity.Create(run);
        var grants = await _accessGrantStore
            .ListByPackageAsync(authority.TenantId, packageId, ct)
            .ConfigureAwait(false);
        foreach (var grant in grants)
        {
            ValidateGrantRunBinding(grant, run);
        }

        return grants.Select(ProjectGrant).ToArray();
    }

    /// <summary>
    /// Exchanges a one-time opaque bearer in a POST body directly for one audited exact-byte
    /// download. Tenant, package, audience, and artifact policy are read from durable grant and
    /// catalog state; the public caller cannot supply them.
    /// </summary>
    public async Task<ReportingArtifactDownload> ExchangeGrantForDownloadAsync(
        string grantId,
        SecureReportingGrantExchangeCommand command,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalizedGrantId = NormalizeRequired(grantId, nameof(grantId), 256);
        var artifactId = NormalizeRequired(command.ArtifactId, nameof(command.ArtifactId), 256);
        var grant = await _accessGrantStore.GetAsync(normalizedGrantId, ct).ConfigureAwait(false);
        if (grant is null)
        {
            throw new SecureReportingAccessGrantDeniedException(ReportingAccessGrantValidationStatus.NotFound);
        }

        var linkedDelivery = await _deliveryStore
            .GetByAccessGrantIdAsync(grant.GrantId, ct)
            .ConfigureAwait(false);
        if (linkedDelivery is not null && RequiresAccessGrantRevocation(linkedDelivery))
        {
            _ = await _accessGrantService.RevokeAsync(
                    grant.GrantId,
                    grant.TenantId,
                    "reporting-grant-exchange-reconciler",
                    $"Denied access after retained failed provider receipt for delivery {linkedDelivery.JobId}.",
                    ct)
                .ConfigureAwait(false);
            throw new SecureReportingAccessGrantDeniedException(
                ReportingAccessGrantValidationStatus.Revoked);
        }

        var validation = await _accessGrantService.ValidateAsync(
            new ReportingAccessGrantValidationRequest(
                normalizedGrantId,
                command.BearerToken,
                grant.TenantId,
                grant.Audience,
                grant.RunId,
                grant.PackageId,
                artifactId,
                ConsumeUse: false,
                grant.AudienceKind),
            ct).ConfigureAwait(false);
        if (!validation.IsValid || validation.Grant is null)
        {
            throw new SecureReportingAccessGrantDeniedException(validation.Status);
        }

        grant = validation.Grant;

        var run = await GetReleasedRunAsync(grant.TenantId, grant.RunId, ct).ConfigureAwait(false);
        var expectedPackageId = ReportingArtifactPackageIdentity.Create(run);
        if (!Same(expectedPackageId, grant.PackageId))
        {
            throw new SecureReportingAccessGrantDeniedException(ReportingAccessGrantValidationStatus.PackageMismatch);
        }

        var artifact = await _artifactCatalog
            .GetArtifactAsync(grant.TenantId, grant.PackageId, artifactId, ct)
            .ConfigureAwait(false);
        if (artifact is null || !Same(artifact.Scope.TenantId, grant.TenantId))
        {
            throw new SecureReportingAccessGrantDeniedException(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        }

        ValidateCatalogBinding(artifact, grant.TenantId, grant.PackageId, artifactId);
        var releasedArtifact = run.Release!.Artifacts.SingleOrDefault(released =>
            Same(released.ArtifactId, artifactId))
            ?? throw new SecureReportingAccessGrantDeniedException(
                ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        ValidateRunArtifactBinding(run, artifact, releasedArtifact, expectedPackageId);
        var access = new ReportingArtifactAccessContext(
            ActorId: $"grant:{grant.GrantId}",
            TenantId: grant.TenantId,
            OrganizationId: artifact.Scope.OrganizationId,
            CompanyId: artifact.Scope.CompanyId,
            FundId: artifact.Scope.FundId,
            BookId: artifact.Scope.BookId,
            PeriodId: artifact.Scope.PeriodId,
            PrincipalIds: ImmutableArray<string>.Empty,
            CorrelationId: NormalizeRequired(correlationId, nameof(correlationId), 256),
            DelegatedPrincipal: new ReportingAccessPrincipalScope(grant.AudienceKind, grant.Audience));
        var download = await _artifactVault
            .ReadForDownloadAsync(expectedPackageId, artifactId, access, ct)
            .ConfigureAwait(false);
        var consumption = await _accessGrantService.ValidateAsync(
            new ReportingAccessGrantValidationRequest(
                normalizedGrantId,
                command.BearerToken,
                grant.TenantId,
                grant.Audience,
                grant.RunId,
                grant.PackageId,
                artifactId,
                ConsumeUse: true,
                grant.AudienceKind),
            ct).ConfigureAwait(false);
        if (!consumption.IsValid)
        {
            throw new SecureReportingAccessGrantDeniedException(consumption.Status);
        }

        var delivery = linkedDelivery;
        if (delivery is not null)
        {
            if (!Same(delivery.TenantId, grant.TenantId)
                || !Same(delivery.PackageId, expectedPackageId)
                || !Same(delivery.AccessGrantId, grant.GrantId))
            {
                throw new InvalidDataException("Delivery access-grant linkage failed immutable scope verification.");
            }

            await AppendAuditedDownloadReceiptAsync(delivery, download, ct).ConfigureAwait(false);
        }

        return download;
    }

    private static bool RequiresAccessGrantRevocation(ReportingDeliveryJobRecord delivery) =>
        delivery.Receipts.Any(receipt =>
            receipt.Kind is ReportingDeliveryReceiptKind.Bounced or ReportingDeliveryReceiptKind.Rejected
            || delivery.State == ReportingDeliveryState.Failed
            && receipt.Kind == ReportingDeliveryReceiptKind.Failed
            && !IsStableProviderReplayFailure(receipt));

    private static bool IsStableProviderReplayFailure(ReportingDeliveryReceipt receipt) =>
        receipt.Detail?.StartsWith("RELAY_OUTCOME_UNKNOWN:", StringComparison.Ordinal) == true
        || receipt.Detail?.StartsWith("TRANSPORT_CANCELLED:", StringComparison.Ordinal) == true;

    public async Task<ReportingArtifactDownload> DownloadArtifactAsync(
        string runId,
        string artifactId,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: true, requireDeliver: false, requireAdmin: false);
        var normalizedRunId = NormalizeRequired(runId, nameof(runId), 256);
        var normalizedArtifactId = NormalizeRequired(artifactId, nameof(artifactId), 256);
        var run = await GetReleasedRunAsync(authority.TenantId, normalizedRunId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
        var packageId = ReportingArtifactPackageIdentity.Create(run);
        var releasedArtifact = run.Release!.Artifacts.SingleOrDefault(released =>
            Same(released.ArtifactId, normalizedArtifactId))
            ?? throw new ReportingArtifactVaultAccessDeniedException("Artifact does not exist or is not accessible.");
        var artifact = await _artifactCatalog
            .GetArtifactAsync(authority.TenantId, packageId, normalizedArtifactId, ct)
            .ConfigureAwait(false)
            ?? throw new ReportingArtifactVaultAccessDeniedException("Artifact does not exist or is not accessible.");
        EnsureArtifactCompanyScope(artifact, authority);
        ValidateCatalogBinding(artifact, authority.TenantId, packageId, normalizedArtifactId);
        ValidateRunArtifactBinding(run, artifact, releasedArtifact, packageId);
        var context = new ReportingArtifactAccessContext(
            authority.ActorId,
            authority.TenantId,
            artifact.Scope.OrganizationId,
            artifact.Scope.CompanyId,
            artifact.Scope.FundId,
            artifact.Scope.BookId,
            artifact.Scope.PeriodId,
            authority.PrincipalIds,
            authority.CorrelationId);
        var download = await _artifactVault
            .ReadForDownloadAsync(packageId, normalizedArtifactId, context, ct)
            .ConfigureAwait(false);
        return download;
    }

    /// <summary>
    /// Authorizes the authenticated secure-portal landing route against the Released run's exact
    /// tenant, company, and immutable access-policy snapshot before the browser workstation opens.
    /// </summary>
    public async Task AuthorizePortalPackageAsync(
        string runId,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: true, requireDeliver: false, requireAdmin: false);
        var run = await GetReleasedRunAsync(authority.TenantId, runId, ct).ConfigureAwait(false);
        EnsureRunScope(run, authority);
        EnsureAuthorityCanAccessRun(run, authority);
    }

    private Task<ReportingDeliveryJobRecord> AppendAuditedDownloadReceiptAsync(
        ReportingDeliveryJobRecord delivery,
        ReportingArtifactDownload download,
        CancellationToken ct)
    {
        var receipt = new ReportingDeliveryReceipt(
            ComputeSha256(string.Join(
                "\u001f",
                delivery.JobId,
                download.Artifact.ArtifactId,
                download.AuditEventId)),
            ReportingDeliveryReceiptKind.Downloaded,
            download.AccessedAtUtc,
            delivery.TransportId,
            delivery.ProviderMessageId,
            download.AuditEventId,
            "Exact retained bytes were integrity-verified and access-audited before download.");
        return _dispatcher.AppendReceiptAsync(delivery.JobId, delivery.TenantId, receipt, ct);
    }

    private async Task<GovernedReportingRun> GetReleasedRunAsync(
        string tenantId,
        string runId,
        CancellationToken ct)
    {
        var run = await GetGovernedRunAsync(tenantId, runId, ct).ConfigureAwait(false);

        if (run.ExecutionState != GovernedReportingExecutionState.Succeeded
            || run.GovernanceState != GovernedReportingState.Released
            || run.Release is null)
        {
            throw new InvalidOperationException(
                $"Reporting run '{run.RunId}' is not Released and cannot be distributed.");
        }

        return run;
    }

    private async Task<GovernedReportingRun> GetGovernedRunAsync(
        string tenantId,
        string runId,
        CancellationToken ct)
    {
        var normalizedTenantId = NormalizeRequired(tenantId, nameof(tenantId), 256);
        var normalizedRunId = NormalizeRequired(runId, nameof(runId), 256);
        var run = await _governanceRepository.ExecuteTransactionAsync(
            (transaction, cancellationToken) => transaction.GetRunAsync(
                normalizedTenantId,
                normalizedRunId,
                cancellationToken),
            ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Reporting run was not found.");

        if (!ReportingGovernanceAuditChain.Verify(run.AuditTrail))
        {
            throw new InvalidDataException(
                $"Reporting run '{normalizedRunId}' has an invalid governance audit chain.");
        }

        return run;
    }

    private SecureReportingAccessGrantSummary ProjectGrant(ReportingAccessGrantRecord grant)
    {
        var state = grant.RevokedAtUtc is not null
            ? "Revoked"
            : grant.ExpiresAtUtc <= _timeProvider.GetUtcNow()
                ? "Expired"
                : grant.UseCount >= grant.MaxUses
                    ? "Exhausted"
                    : "Active";
        return new SecureReportingAccessGrantSummary(
            grant.GrantId,
            grant.RunId,
            grant.PackageId,
            grant.Audience,
            grant.AllowPackageRead,
            grant.ArtifactIds,
            state,
            grant.CreatedAtUtc,
            grant.ExpiresAtUtc,
            grant.MaxUses,
            grant.UseCount,
            grant.LastUsedAtUtc,
            grant.RevokedAtUtc,
            grant.RevokedBy,
            grant.RevocationReason,
            grant.AudienceKind);
    }

    private static void ValidateDeliveryRunBinding(
        ReportingDeliveryJobRecord job,
        GovernedReportingRun run)
    {
        var expectedPackageId = ReportingArtifactPackageIdentity.Create(run);
        if (!Same(job.TenantId, run.Scope.TenantId)
            || !Same(job.PackageId, expectedPackageId)
            || !Same(job.ReleaseAuthorization.TenantId, run.Scope.TenantId)
            || !Same(job.ReleaseAuthorization.RunId, run.RunId)
            || !Same(job.ReleaseAuthorization.PackageId, expectedPackageId))
        {
            throw new InvalidDataException(
                "Delivery history failed immutable governed-run scope verification.");
        }
    }

    private static void ValidateGrantRunBinding(
        ReportingAccessGrantRecord grant,
        GovernedReportingRun run)
    {
        var expectedPackageId = ReportingArtifactPackageIdentity.Create(run);
        if (!Same(grant.TenantId, run.Scope.TenantId)
            || !Same(grant.RunId, run.RunId)
            || !Same(grant.PackageId, expectedPackageId))
        {
            throw new InvalidDataException(
                "Access-grant history failed immutable governed-run scope verification.");
        }
    }

    private async Task<IReadOnlyList<ReportingArtifactReference>> ResolveArtifactsAsync(
        GovernedReportingRun run,
        string packageId,
        IReadOnlyList<string>? requestedArtifactIds,
        CancellationToken ct)
    {
        var release = run.Release ?? throw new InvalidOperationException("Released run has no release receipt.");
        var released = release.Artifacts.ToDictionary(static artifact => artifact.ArtifactId, StringComparer.Ordinal);
        var artifactIds = NormalizeArtifactIds(requestedArtifactIds);
        if (artifactIds.Count == 0)
        {
            artifactIds = released.Keys.OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        }

        if (artifactIds.Count == 0)
        {
            throw new InvalidOperationException("A Released run must contain at least one immutable artifact.");
        }

        var result = new List<ReportingArtifactReference>(artifactIds.Count);
        foreach (var artifactId in artifactIds)
        {
            if (!released.TryGetValue(artifactId, out var releaseArtifact))
            {
                throw new UnauthorizedAccessException(
                    $"Artifact '{artifactId}' is not part of the immutable Released manifest.");
            }

            var retained = await _artifactCatalog
                .GetArtifactAsync(run.Scope.TenantId, packageId, artifactId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Released artifact '{artifactId}' is missing from immutable storage.");
            ValidateCatalogBinding(retained, run.Scope.TenantId, packageId, artifactId);
            ValidateRunArtifactBinding(run, retained, releaseArtifact, packageId);
            result.Add(releaseArtifact);
        }

        return result;
    }

    private async Task<ReportingDeliveryJobRecord> GetJobInTenantAsync(
        string jobId,
        string tenantId,
        CancellationToken ct)
    {
        var job = await _deliveryStore
            .GetAsync(NormalizeRequired(jobId, nameof(jobId), 256), ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Reporting delivery job was not found.");
        if (!Same(job.TenantId, tenantId))
        {
            throw new UnauthorizedAccessException("Reporting delivery job is outside the authenticated tenant.");
        }

        return job;
    }

    private static ReportingAccessPrincipalScope ResolveAudience(
        ReportingAccessScope access,
        ReportingDistributionAuthority authority,
        string? requestedPrincipalId,
        ReportingAccessPrincipalKind? requestedPrincipalKind)
    {
        var requested = NormalizeOptional(requestedPrincipalId, 256);
        if (requestedPrincipalKind is { } kind && !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPrincipalKind));
        }

        return access.Mode switch
        {
            ReportingGovernanceAccessMode.Private or ReportingGovernanceAccessMode.Restricted =>
                ResolveNamedAudience(access, requested, requestedPrincipalKind),
            ReportingGovernanceAccessMode.CompanyWide =>
                ResolveCompanyAudience(authority, requested, requestedPrincipalKind),
            _ => throw new InvalidDataException("Reporting access-policy mode is invalid.")
        };
    }

    private static ReportingAccessPrincipalScope ResolveNamedAudience(
        ReportingAccessScope access,
        string? requested,
        ReportingAccessPrincipalKind? requestedKind)
    {
        var allowed = (access.Principals.IsDefault
                ? Enumerable.Empty<ReportingAccessPrincipalScope>()
                : access.Principals)
            .Concat(access.AllowOwnerAccess && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                ? [new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    access.OwnerPrincipalId.Trim())]
                : [])
            .DistinctBy(
                static principal => (principal.Kind, principal.PrincipalId),
                ReportingAccessPrincipalScopeKeyComparer.Instance)
            .ToArray();
        if (allowed.Length == 0)
        {
            throw new InvalidDataException("Reporting access policy has no enabled recipient principal.");
        }

        if (requested is null)
        {
            if (requestedKind is not null || allowed.Length != 1)
            {
                throw new ArgumentException(
                    "Recipient principal and kind are required when the access policy contains multiple typed principals.",
                    nameof(requested));
            }

            return allowed[0];
        }

        var matches = allowed
            .Where(principal =>
                SamePrincipal(principal.PrincipalId, requested)
                && (requestedKind is null || principal.Kind == requestedKind))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            > 1 => throw new ArgumentException(
                "Recipient principal kind is required because the identifier appears in multiple access-principal namespaces.",
                nameof(requestedKind)),
            _ => throw new UnauthorizedAccessException(
                "Recipient is outside the immutable reporting access policy.")
        };
    }

    private async Task<string> ResolveExternalDestinationAsync(
        ReportingDistributionAuthority authority,
        ReportingAccessPrincipalScope audience,
        string transportId,
        string? assertedDestination,
        CancellationToken cancellationToken)
    {
        if (!_destinationResolver.IsConfigured)
        {
            throw new InvalidOperationException(
                "External reporting delivery is disabled until a tenant-bound recipient destination directory is configured.");
        }

        var resolved = await _destinationResolver.ResolveDestinationAsync(
                new ReportingRecipientDestinationRequest(
                    authority.TenantId,
                    authority.CompanyId,
                    audience.PrincipalId,
                    transportId,
                    audience.Kind),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new UnauthorizedAccessException(
                "The governed recipient has no server-resolved destination for this transport.");
        }

        var destination = NormalizeTokenFree(
            resolved,
            nameof(IReportingRecipientDestinationResolver),
            MaximumDestinationLength);
        var assertion = NormalizeOptionalTokenFree(
            assertedDestination,
            nameof(assertedDestination),
            MaximumDestinationLength);
        if (assertion is not null && !Same(assertion, destination))
        {
            throw new UnauthorizedAccessException(
                "The requested destination does not match the server-resolved governed recipient destination.");
        }

        return destination;
    }

    private static ReportingAccessPrincipalScope ResolveCompanyAudience(
        ReportingDistributionAuthority authority,
        string? requested,
        ReportingAccessPrincipalKind? requestedKind)
    {
        var audience = new ReportingAccessPrincipalScope(
            requestedKind ?? ReportingAccessPrincipalKind.User,
            requested ?? authority.ActorId);
        if (!AuthorityMatches(authority, audience))
        {
            throw new UnauthorizedAccessException(
                "Company-wide distribution audience must be resolved from the authenticated principal scope.");
        }

        return audience;
    }

    private static void ValidateRunArtifactBinding(
        GovernedReportingRun run,
        ReportingRetainedArtifactRecord retained,
        ReportingArtifactReference released,
        string packageId)
    {
        var release = run.Release!;
        if (!Same(retained.PackageId, packageId)
            || !Same(retained.RunId, run.RunId)
            || !Same(retained.Scope.TenantId, run.Scope.TenantId)
            || !Same(retained.Scope.OrganizationId, run.Scope.OrganizationId)
            || !SameOptional(retained.Scope.CompanyId, run.Scope.CompanyId)
            || !SameOptional(retained.Scope.FundId, run.Scope.FundId)
            || !Same(retained.Scope.BookId, run.Scope.BookId)
            || !Same(retained.Scope.PeriodId, run.Scope.PeriodId)
            || !Same(retained.Access.PolicyId, run.Access.PolicyId)
            || !Same(retained.Access.PolicyVersion, run.Access.PolicyVersion)
            || !string.Equals(retained.Access.PolicyHash, run.Access.PolicyHash, StringComparison.OrdinalIgnoreCase)
            || !Same(retained.Snapshot.SnapshotId, run.Snapshot.SnapshotId)
            || !string.Equals(retained.Snapshot.SnapshotHash, run.Snapshot.SnapshotHash, StringComparison.OrdinalIgnoreCase)
            || !Same(retained.ManifestId, release.ManifestId)
            || !string.Equals(retained.ManifestHash, release.ManifestHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(retained.Identity.ContentHashSha256, released.ArtifactHash, StringComparison.OrdinalIgnoreCase)
            || retained.ByteLength != released.ByteLength)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact '{retained.ArtifactId}' does not match the immutable Released governance receipt.");
        }
    }

    private static void ValidateCatalogBinding(
        ReportingRetainedArtifactRecord artifact,
        string tenantId,
        string packageId,
        string artifactId)
    {
        if (!Same(artifact.Scope.TenantId, tenantId)
            || !Same(artifact.Identity.TenantId, tenantId)
            || !Same(artifact.PackageId, packageId)
            || !Same(artifact.ArtifactId, artifactId))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Artifact catalog returned metadata outside the requested tenant content address.");
        }
    }

    private static void EnsureRunScope(
        GovernedReportingRun run,
        ReportingDistributionAuthority authority)
    {
        if (!Same(run.Scope.TenantId, authority.TenantId)
            || !SameOptional(run.Scope.CompanyId, authority.CompanyId))
        {
            throw new UnauthorizedAccessException("Reporting run is outside the authenticated tenant/company scope.");
        }
    }

    private static void EnsureAuthorityCanAccessRun(
        GovernedReportingRun run,
        ReportingDistributionAuthority authority)
    {
        var allowed = run.Access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private or ReportingGovernanceAccessMode.Restricted =>
                run.Access.AllowOwnerAccess
                && !string.IsNullOrWhiteSpace(run.Access.OwnerPrincipalId)
                && AuthorityMatches(
                    authority,
                    new ReportingAccessPrincipalScope(
                        ReportingAccessPrincipalKind.User,
                        run.Access.OwnerPrincipalId))
                || (!run.Access.Principals.IsDefaultOrEmpty
                    && run.Access.Principals.Any(principal => AuthorityMatches(authority, principal))),
            _ => false
        };
        if (!allowed)
        {
            throw new UnauthorizedAccessException(
                "Authenticated authority is outside the immutable reporting access scope.");
        }
    }

    private static bool AuthorityMatches(
        ReportingDistributionAuthority authority,
        ReportingAccessPrincipalScope principal)
    {
        if (authority.DelegatedPrincipal is { } delegated
            && delegated.Kind == principal.Kind
            && SamePrincipal(delegated.PrincipalId, principal.PrincipalId))
        {
            return true;
        }

        return principal.Kind switch
        {
            ReportingAccessPrincipalKind.User => SamePrincipal(authority.ActorId, principal.PrincipalId),
            ReportingAccessPrincipalKind.Group =>
                !authority.PrincipalIds.IsDefaultOrEmpty
                && authority.PrincipalIds.Contains(principal.PrincipalId, StringComparer.OrdinalIgnoreCase),
            ReportingAccessPrincipalKind.Company => SamePrincipal(authority.CompanyId, principal.PrincipalId),
            _ => false
        };
    }

    private sealed class ReportingAccessPrincipalScopeKeyComparer
        : IEqualityComparer<(ReportingAccessPrincipalKind Kind, string PrincipalId)>
    {
        public static ReportingAccessPrincipalScopeKeyComparer Instance { get; } = new();

        public bool Equals(
            (ReportingAccessPrincipalKind Kind, string PrincipalId) x,
            (ReportingAccessPrincipalKind Kind, string PrincipalId) y) =>
            x.Kind == y.Kind && SamePrincipal(x.PrincipalId, y.PrincipalId);

        public int GetHashCode((ReportingAccessPrincipalKind Kind, string PrincipalId) value) =>
            HashCode.Combine(value.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(value.PrincipalId));
    }

    private static void EnsureArtifactCompanyScope(
        ReportingRetainedArtifactRecord artifact,
        ReportingDistributionAuthority authority)
    {
        if (!Same(artifact.Scope.TenantId, authority.TenantId)
            || !SameOptional(artifact.Scope.CompanyId, authority.CompanyId))
        {
            throw new ReportingArtifactVaultAccessDeniedException("Artifact does not exist or is not accessible.");
        }
    }

    private static void ValidateAuthority(
        ReportingDistributionAuthority authority,
        bool requireView,
        bool requireDeliver,
        bool requireAdmin)
    {
        ArgumentNullException.ThrowIfNull(authority);
        NormalizeRequired(authority.ActorId, nameof(authority.ActorId), 256);
        NormalizeRequired(authority.TenantId, nameof(authority.TenantId), 256);
        NormalizeRequired(authority.CorrelationId, nameof(authority.CorrelationId), 256);
        if (requireView && !authority.CanView
            || requireDeliver && !authority.CanDeliver
            || requireAdmin && !authority.CanAdminister)
        {
            throw new UnauthorizedAccessException("Authenticated authority cannot perform this reporting distribution action.");
        }
    }

    private TimeSpan ResolveLifetime(int? seconds)
    {
        var lifetime = seconds is null ? _options.DefaultGrantLifetime : TimeSpan.FromSeconds(seconds.Value);
        if (lifetime <= TimeSpan.Zero || lifetime > _options.MaximumGrantLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Access-grant lifetime is outside the configured range.");
        }

        return lifetime;
    }

    private int ResolveMaxUses(int? maxUses)
    {
        var value = maxUses ?? _options.DefaultGrantMaxUses;
        if (value <= 0 || value > _options.MaximumGrantMaxUses)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUses), "Access-grant use limit is outside the configured range.");
        }

        return value;
    }

    private static SecureReportingDistributionOptions ValidateOptions(SecureReportingDistributionOptions options)
    {
        ValidatePath(options.PortalPackageBasePath, nameof(options.PortalPackageBasePath));
        ValidatePath(options.GrantExchangeBasePath, nameof(options.GrantExchangeBasePath));
        if (options.DefaultGrantLifetime <= TimeSpan.Zero
            || options.MaximumGrantLifetime < options.DefaultGrantLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Access-grant lifetime options are invalid.");
        }

        if (options.DefaultGrantMaxUses <= 0
            || options.MaximumGrantMaxUses < options.DefaultGrantMaxUses)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Access-grant use-limit options are invalid.");
        }

        if (options.ExternalGrantTransportIds is null)
        {
            throw new ArgumentNullException(nameof(options.ExternalGrantTransportIds));
        }

        if (string.IsNullOrWhiteSpace(options.WorkerId)
            || options.WorkerPollInterval < TimeSpan.FromMilliseconds(250)
            || options.WorkerPollInterval > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Reporting delivery worker options are invalid.");
        }

        if (!string.IsNullOrWhiteSpace(options.ExternalAccessBaseUri))
        {
            var uri = new Uri(options.ExternalAccessBaseUri, UriKind.Absolute);
            if (!string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            {
                throw new ArgumentException(
                    "External reporting access base URI must be token-free HTTPS outside loopback development.",
                    nameof(options));
            }
        }

        return options;
    }

    private static IReadOnlyList<string> NormalizeArtifactIds(IReadOnlyList<string>? artifactIds) =>
        artifactIds?
            .Select(static item => NormalizeRequired(item, nameof(artifactIds), 256))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray()
        ?? [];

    private static string BuildPath(string basePath, params string[] segments)
    {
        ValidatePath(basePath, nameof(basePath));
        return string.Join(
            '/',
            new[] { basePath.TrimEnd('/') }
                .Concat(segments.Select(Uri.EscapeDataString)));
    }

    private static string BuildFragmentAccessPath(
        string exchangePath,
        string bearerToken,
        string? artifactId)
    {
        ValidatePath(exchangePath, nameof(exchangePath));
        var token = NormalizeRequired(bearerToken, nameof(bearerToken), 512);
        var fragment = $"token={Uri.EscapeDataString(token)}";
        if (!string.IsNullOrWhiteSpace(artifactId))
        {
            fragment = $"{fragment}&artifact={Uri.EscapeDataString(artifactId.Trim())}";
        }

        return $"{exchangePath}#{fragment}";
    }

    private static void ValidatePath(string path, string parameterName)
    {
        var normalized = NormalizeRequired(path, parameterName, 1_024);
        if (!normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains('?')
            || normalized.Contains('#')
            || ContainsBearer(normalized))
        {
            throw new ArgumentException("Reporting access paths must be application-root relative and token-free.", parameterName);
        }
    }

    private static string NormalizeTokenFree(string value, string parameterName, int maximumLength)
    {
        var normalized = NormalizeRequired(value, parameterName, maximumLength);
        if (ContainsBearer(normalized))
        {
            throw new ArgumentException("Durable reporting distribution values cannot contain bearer tokens.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptionalTokenFree(string? value, string parameterName, int maximumLength)
    {
        var normalized = NormalizeOptional(value, maximumLength);
        if (normalized is not null && ContainsBearer(normalized))
        {
            throw new ArgumentException("Durable reporting receipt values cannot contain bearer tokens.", parameterName);
        }

        return normalized;
    }

    private static bool ContainsBearer(string value) =>
        value.Contains("token=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("#token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("bearer ", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRequired(string? value, string parameterName, int maximumLength)
    {
        var normalized = NormalizeOptional(value, maximumLength);
        return normalized ?? throw new ArgumentException($"{parameterName} is required.", parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Value exceeds {maximumLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return normalized;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private static bool SamePrincipal(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderOutcomeUnknownCode(string? code) =>
        string.Equals(code, "RELAY_OUTCOME_UNKNOWN", StringComparison.Ordinal)
        || string.Equals(code, "TRANSPORT_CANCELLED", StringComparison.Ordinal);

    private static bool SameOptional(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right) || Same(left, right);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
