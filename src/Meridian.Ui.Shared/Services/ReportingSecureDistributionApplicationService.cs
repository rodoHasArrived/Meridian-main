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
    string CorrelationId);

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
    int MaxAttempts = 3);

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
    int? MaxUses = null);

/// <summary>
/// The bearer is returned exactly once in the response body. ExchangePath never embeds it in a
/// query, fragment, persisted URI, or route segment.
/// </summary>
public sealed record SecureReportingGrantIssueResult(
    string GrantId,
    string BearerToken,
    string ExchangePath,
    DateTimeOffset ExpiresAtUtc,
    string Audience,
    string PackageId,
    IReadOnlyList<string> ArtifactIds);

public sealed record SecureReportingGrantExchangeCommand(string BearerToken, string ArtifactId);

public sealed record SecureReportingDistributionOptions(
    string PortalPackageBasePath,
    string GrantExchangeBasePath,
    string? ExternalAccessBaseUri,
    TimeSpan DefaultGrantLifetime,
    TimeSpan MaximumGrantLifetime,
    int DefaultGrantMaxUses,
    int MaximumGrantMaxUses,
    string WorkerId,
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
    private readonly IReportingGovernanceRepository _governanceRepository;
    private readonly IReportingArtifactCatalog _artifactCatalog;
    private readonly ReportingDeliveryDispatcher _dispatcher;
    private readonly IReportingDeliveryStore _deliveryStore;
    private readonly ReportingAccessGrantService _accessGrantService;
    private readonly IReportingAccessGrantStore _accessGrantStore;
    private readonly ReportingArtifactVaultService _artifactVault;
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
        TimeProvider? timeProvider = null,
        SecureReportingDistributionOptions? options = null)
    {
        _governanceRepository = governanceRepository ?? throw new ArgumentNullException(nameof(governanceRepository));
        _artifactCatalog = artifactCatalog ?? throw new ArgumentNullException(nameof(artifactCatalog));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _deliveryStore = deliveryStore ?? throw new ArgumentNullException(nameof(deliveryStore));
        _accessGrantService = accessGrantService ?? throw new ArgumentNullException(nameof(accessGrantService));
        _accessGrantStore = accessGrantStore ?? throw new ArgumentNullException(nameof(accessGrantStore));
        _artifactVault = artifactVault ?? throw new ArgumentNullException(nameof(artifactVault));
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
        var audience = ResolveAudience(run.Access, authority, command.RecipientPrincipalId);
        var artifacts = await ResolveArtifactsAsync(run, command.ArtifactIds, ct).ConfigureAwait(false);
        var transportId = NormalizeRequired(command.TransportId, nameof(command.TransportId), 128);
        var portalUri = BuildPath(_options.PortalPackageBasePath, run.RunId);
        ReportingDeliveryAccessPolicy? externalAccess = null;
        if (_options.ExternalGrantTransportIds.Contains(transportId))
        {
            if (string.IsNullOrWhiteSpace(_options.ExternalAccessBaseUri))
            {
                throw new InvalidOperationException(
                    $"External reporting transport '{transportId}' is disabled until a secure access base URI is configured.");
            }

            externalAccess = new ReportingDeliveryAccessPolicy(
                audience,
                _options.ExternalAccessBaseUri,
                ResolveLifetime(command.GrantLifetimeSeconds),
                AllowPackageRead: false,
                artifacts.Select(static artifact => artifact.ArtifactId).ToArray(),
                ResolveMaxUses(command.GrantMaxUses));
        }

        var payload = new ReportingDeliveryPayload(
            Recipient: audience,
            RecipientRole: run.Access.Mode.ToString(),
            Destination: NormalizeTokenFree(command.Destination, nameof(command.Destination), MaximumTextLength),
            Subject: NormalizeTokenFree(command.Subject, nameof(command.Subject), 1_024),
            Body: NormalizeTokenFree(command.Body, nameof(command.Body), MaximumTextLength),
            PortalUri: portalUri,
            ExternalAccess: externalAccess);
        var releaseAuthorization = ReportingDeliveryReleaseAuthorizationFactory.Create(run);
        return await _dispatcher.QueueAsync(
            new ReportingDeliveryQueueRequest(
                authority.TenantId,
                run.RunId,
                releaseAuthorization,
                NormalizeRequired(command.DistributionId, nameof(command.DistributionId), 256),
                transportId,
                authority.ActorId,
                payload,
                command.MaxAttempts),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Claims the global durable outbox. This is deliberately an administrative operation because
    /// the underlying skip-locked claim is cross-tenant; tenant operators cannot use it.
    /// </summary>
    public Task<IReadOnlyList<ReportingDeliveryJobRecord>> ProcessDueAsync(
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: false, requireDeliver: false, requireAdmin: true);
        var leaseOwner = $"{NormalizeRequired(_options.WorkerId, nameof(_options.WorkerId), 128)}:{authority.ActorId}";
        return _dispatcher.DispatchDueAsync(leaseOwner, ct);
    }

    public async Task<ReportingDeliveryJobRecord> RecordProviderReceiptAsync(
        string jobId,
        SecureReportingDeliveryReceiptCommand command,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateAuthority(authority, requireView: false, requireDeliver: true, requireAdmin: false);
        var job = await GetJobInTenantAsync(jobId, authority.TenantId, ct).ConfigureAwait(false);
        if (command.Kind is ReportingDeliveryReceiptKind.Accessed or ReportingDeliveryReceiptKind.Downloaded)
        {
            throw new ArgumentException(
                "Accessed and Downloaded receipts are emitted only by the audited artifact access path.",
                nameof(command));
        }

        var eventId = NormalizeRequired(command.ProviderEventId, nameof(command.ProviderEventId), 512);
        var occurredAt = command.OccurredAtUtc ?? _timeProvider.GetUtcNow();
        if (occurredAt > _timeProvider.GetUtcNow().AddMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Provider receipt time cannot be in the future.");
        }

        var receiptId = ComputeSha256(string.Join(
            "\u001f",
            job.JobId,
            job.TransportId.ToLowerInvariant(),
            eventId));
        var receipt = new ReportingDeliveryReceipt(
            receiptId,
            command.Kind,
            occurredAt,
            job.TransportId,
            NormalizeOptionalTokenFree(command.ProviderReference, nameof(command.ProviderReference), 1_024),
            NormalizeOptionalTokenFree(command.EvidenceReference, nameof(command.EvidenceReference), 4_096),
            NormalizeOptionalTokenFree(command.Detail, nameof(command.Detail), MaximumTextLength));
        return await _dispatcher
            .AppendReceiptAsync(job.JobId, authority.TenantId, receipt, ct)
            .ConfigureAwait(false);
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
        var audience = ResolveAudience(run.Access, authority, command.RecipientPrincipalId);
        var artifacts = await ResolveArtifactsAsync(run, command.ArtifactIds, ct).ConfigureAwait(false);
        var expiresAt = _timeProvider.GetUtcNow().Add(ResolveLifetime(command.LifetimeSeconds));
        var artifactIds = artifacts.Select(static artifact => artifact.ArtifactId).ToArray();
        var secret = await _accessGrantService.IssueAsync(
            new ReportingAccessGrantIssueRequest(
                run.Scope.TenantId,
                audience,
                run.RunId,
                expiresAt,
                AllowPackageRead: false,
                artifactIds,
                ResolveMaxUses(command.MaxUses)),
            ct).ConfigureAwait(false);

        return new SecureReportingGrantIssueResult(
            secret.GrantId,
            secret.Token,
            BuildPath(_options.GrantExchangeBasePath, secret.GrantId, "exchange"),
            secret.ExpiresAtUtc,
            audience,
            run.RunId,
            artifactIds);
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

        return await _accessGrantService.RevokeAsync(
            normalizedGrantId,
            authority.TenantId,
            authority.ActorId,
            NormalizeTokenFree(reason, nameof(reason), 2_048),
            ct).ConfigureAwait(false);
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

        var validation = await _accessGrantService.ValidateAsync(
            new ReportingAccessGrantValidationRequest(
                normalizedGrantId,
                command.BearerToken,
                grant.TenantId,
                grant.Audience,
                grant.PackageId,
                artifactId,
                ConsumeUse: true),
            ct).ConfigureAwait(false);
        if (!validation.IsValid || validation.Grant is null)
        {
            throw new SecureReportingAccessGrantDeniedException(validation.Status);
        }

        var artifact = await _artifactCatalog
            .GetArtifactAsync(grant.TenantId, grant.PackageId, artifactId, ct)
            .ConfigureAwait(false);
        if (artifact is null || !Same(artifact.Scope.TenantId, grant.TenantId))
        {
            throw new SecureReportingAccessGrantDeniedException(ReportingAccessGrantValidationStatus.ArtifactOutOfScope);
        }

        ValidateCatalogBinding(artifact, grant.TenantId, grant.PackageId, artifactId);
        var access = new ReportingArtifactAccessContext(
            ActorId: grant.Audience,
            TenantId: grant.TenantId,
            OrganizationId: artifact.Scope.OrganizationId,
            CompanyId: artifact.Scope.CompanyId,
            FundId: artifact.Scope.FundId,
            BookId: artifact.Scope.BookId,
            PeriodId: artifact.Scope.PeriodId,
            PrincipalIds: ImmutableArray.Create(grant.Audience),
            CorrelationId: NormalizeRequired(correlationId, nameof(correlationId), 256));
        return await _artifactVault
            .ReadForDownloadAsync(grant.PackageId, artifactId, access, ct)
            .ConfigureAwait(false);
    }

    public async Task<ReportingArtifactDownload> DownloadArtifactAsync(
        string runId,
        string artifactId,
        ReportingDistributionAuthority authority,
        CancellationToken ct = default)
    {
        ValidateAuthority(authority, requireView: true, requireDeliver: false, requireAdmin: false);
        var normalizedRunId = NormalizeRequired(runId, nameof(runId), 256);
        var normalizedArtifactId = NormalizeRequired(artifactId, nameof(artifactId), 256);
        var artifact = await _artifactCatalog
            .GetArtifactAsync(authority.TenantId, normalizedRunId, normalizedArtifactId, ct)
            .ConfigureAwait(false)
            ?? throw new ReportingArtifactVaultAccessDeniedException("Artifact does not exist or is not accessible.");
        EnsureArtifactCompanyScope(artifact, authority);
        ValidateCatalogBinding(artifact, authority.TenantId, normalizedRunId, normalizedArtifactId);
        var principals = authority.PrincipalIds
            .Append(authority.ActorId)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var context = new ReportingArtifactAccessContext(
            authority.ActorId,
            authority.TenantId,
            artifact.Scope.OrganizationId,
            artifact.Scope.CompanyId,
            artifact.Scope.FundId,
            artifact.Scope.BookId,
            artifact.Scope.PeriodId,
            principals,
            authority.CorrelationId);
        return await _artifactVault
            .ReadForDownloadAsync(normalizedRunId, normalizedArtifactId, context, ct)
            .ConfigureAwait(false);
    }

    private async Task<GovernedReportingRun> GetReleasedRunAsync(
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
            ?? throw new KeyNotFoundException("Released reporting run was not found.");

        if (run.ExecutionState != GovernedReportingExecutionState.Succeeded
            || run.GovernanceState != GovernedReportingState.Released
            || run.Release is null)
        {
            throw new InvalidOperationException(
                $"Reporting run '{normalizedRunId}' is not Released and cannot be distributed.");
        }

        if (!ReportingGovernanceAuditChain.Verify(run.AuditTrail))
        {
            throw new InvalidDataException(
                $"Reporting run '{normalizedRunId}' has an invalid governance audit chain.");
        }

        return run;
    }

    private async Task<IReadOnlyList<ReportingArtifactReference>> ResolveArtifactsAsync(
        GovernedReportingRun run,
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
                .GetArtifactAsync(run.Scope.TenantId, run.RunId, artifactId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Released artifact '{artifactId}' is missing from immutable storage.");
            ValidateCatalogBinding(retained, run.Scope.TenantId, run.RunId, artifactId);
            ValidateRunArtifactBinding(run, retained, releaseArtifact);
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

    private static string ResolveAudience(
        ReportingAccessScope access,
        ReportingDistributionAuthority authority,
        string? requestedPrincipalId)
    {
        var requested = NormalizeOptional(requestedPrincipalId, 256);
        return access.Mode switch
        {
            ReportingGovernanceAccessMode.Private => ResolvePrivateAudience(access, requested),
            ReportingGovernanceAccessMode.Restricted => ResolveRestrictedAudience(access, requested),
            ReportingGovernanceAccessMode.CompanyWide => ResolveCompanyAudience(authority, requested),
            _ => throw new InvalidDataException("Reporting access-policy mode is invalid.")
        };
    }

    private static string ResolvePrivateAudience(ReportingAccessScope access, string? requested)
    {
        var owner = NormalizeRequired(access.OwnerPrincipalId, nameof(access.OwnerPrincipalId), 256);
        if (requested is not null && !Same(owner, requested))
        {
            throw new UnauthorizedAccessException("Recipient is outside the immutable private access policy.");
        }

        return owner;
    }

    private static string ResolveRestrictedAudience(ReportingAccessScope access, string? requested)
    {
        var allowed = access.PrincipalIds
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var audience = requested ?? (allowed.Length == 1
            ? allowed[0]
            : throw new ArgumentException(
                "Recipient principal is required when the access policy contains multiple principals.",
                nameof(requested)));
        if (!allowed.Contains(audience, StringComparer.Ordinal))
        {
            throw new UnauthorizedAccessException("Recipient is outside the immutable restricted access policy.");
        }

        return audience;
    }

    private static string ResolveCompanyAudience(
        ReportingDistributionAuthority authority,
        string? requested)
    {
        var audience = requested ?? authority.ActorId;
        if (!Same(audience, authority.ActorId)
            && !authority.PrincipalIds.Contains(audience, StringComparer.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Company-wide distribution audience must be resolved from the authenticated principal scope.");
        }

        return audience;
    }

    private static void ValidateRunArtifactBinding(
        GovernedReportingRun run,
        ReportingRetainedArtifactRecord retained,
        ReportingArtifactReference released)
    {
        var release = run.Release!;
        if (!Same(retained.RunId, run.RunId)
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

        if (!string.IsNullOrWhiteSpace(options.ExternalAccessBaseUri))
        {
            var uri = new Uri(options.ExternalAccessBaseUri, UriKind.Absolute);
            if (!string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
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

    private static void ValidatePath(string path, string parameterName)
    {
        var normalized = NormalizeRequired(path, parameterName, 1_024);
        if (!normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains("?", StringComparison.Ordinal)
            || normalized.Contains("#", StringComparison.Ordinal)
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

    private static bool SameOptional(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right) || Same(left, right);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
