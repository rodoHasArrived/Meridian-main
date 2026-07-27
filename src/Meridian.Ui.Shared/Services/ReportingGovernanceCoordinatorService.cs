using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Request identity resolved from the authenticated workstation session. Organization identity is
/// deliberately absent: the coordinator resolves it from the server-owned run scope, so an HTTP
/// caller cannot authorize itself into another reporting organization.
/// </summary>
public sealed record ReportingGovernanceCallerContext(
    string ActorId,
    string TenantId,
    string? CompanyId,
    UserPermission Permissions,
    ReportingCommandOrigin Origin,
    string CorrelationId,
    ImmutableArray<string> PrincipalIds = default)
{
    public bool Matches(ReportingAccessPrincipalScope principal) =>
        principal.Kind switch
        {
            ReportingAccessPrincipalKind.User =>
                string.Equals(ActorId.Trim(), principal.PrincipalId, StringComparison.OrdinalIgnoreCase),
            ReportingAccessPrincipalKind.Group =>
                !PrincipalIds.IsDefaultOrEmpty
                && PrincipalIds.Any(group =>
                    string.Equals(group?.Trim(), principal.PrincipalId, StringComparison.OrdinalIgnoreCase)),
            ReportingAccessPrincipalKind.Company =>
                string.Equals(CompanyId?.Trim(), principal.PrincipalId, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}

/// <summary>
/// Exact output returned by the server-side renderer/artifact producer. Artifact ids must be the
/// same ids declared by the completed orchestration manifest; the coordinator rejects partial or
/// augmented output sets before retaining any bytes.
/// </summary>
public sealed record ReportingAuthoritativeAsOfCertification(
    DateOnly AsOfDate,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot,
    string SourceCheckpointId,
    string SourceCheckpointHash,
    bool IsAuthoritative,
    ImmutableArray<string> EvidenceIds);

public sealed record ReportingGovernedArtifactProduction(
    string RunId,
    string ManifestArtifactId,
    ReportingAuthoritativeAsOfCertification Certification,
    ImmutableArray<ReportingRenderedArtifact> Artifacts);

public sealed record ReportingGovernedArtifactDescriptor(
    string ArtifactId,
    string FileName,
    string ContentType,
    long ByteLength,
    string ContentHashSha256,
    ReportingDeclaredArtifactKind Kind,
    bool IsPreview,
    string DownloadRoute);

public sealed record ReportingGovernedArtifactDownload(
    ReportingGovernedArtifactDescriptor Descriptor,
    byte[] Content,
    string AccessAuditEventId);

internal static class ReportingArtifactRouteToken
{
    private const string Prefix = "b64_";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Encode(string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(artifactId.Trim()))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string routeToken, out string artifactId)
    {
        artifactId = string.Empty;
        if (string.IsNullOrWhiteSpace(routeToken))
        {
            return false;
        }

        var token = routeToken.Trim();
        if (!token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            artifactId = token;
            return true;
        }

        var encoded = token[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        if (encoded.Length == 0 || encoded.Length % 4 == 1)
        {
            return false;
        }

        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        try
        {
            artifactId = StrictUtf8.GetString(Convert.FromBase64String(encoded));
            return !string.IsNullOrWhiteSpace(artifactId)
                && string.Equals(Encode(artifactId), token, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            artifactId = string.Empty;
            return false;
        }
    }
}

/// <summary>
/// Narrow bridge to the renderer that owns the exact output bytes. Implementations must return the
/// bytes actually rendered for the supplied manifest; callers and HTTP payloads are never an
/// artifact source.
/// </summary>
public interface IReportingCertifiedArtifactProducer
{
    ValueTask<ReportingGovernedArtifactProduction> ProduceAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the service-principal authority used only to retain renderer output. This keeps a human
/// release authority from being silently elevated to ExecuteRun and preserves an explicit machine
/// actor in the artifact audit chain.
/// </summary>
public interface IReportingArtifactRetentionAuthorityProvider
{
    ValueTask<ReportingAuthorityScope> ResolveAsync(
        GovernedReportingRun governedRun,
        ReportingGovernanceCallerContext releaseCaller,
        CancellationToken cancellationToken = default);
}

/// <summary>Server-owned source for the changed-line disclosure on a restatement request.</summary>
public interface IReportingRestatementChangedLineResolver
{
    ValueTask<ImmutableArray<ReportingRestatementChangedLine>> ResolveAsync(
        GovernedReportingRun releasedPredecessor,
        string reason,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inputs from server-owned read models used to certify a replacement snapshot. Dataset rows,
/// source identity, template metadata, and access context must not be populated from an HTTP body.
/// </summary>
public sealed record ReportingRestatementCertificationInput(
    ReportingTemplateMetadata Template,
    ReportingRunReadinessDto Readiness,
    ReportAccessQueryContext AccessContext);

public interface IReportingRestatementCertificationInputProvider
{
    ValueTask<ReportingRestatementCertificationInput> ResolveAsync(
        ReportingRestatementRequest request,
        GovernedReportingRun releasedPredecessor,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);
}

public sealed record ReportingGovernanceSeriesHistory(
    string SeriesId,
    IReadOnlyList<GovernedReportingRun> Runs,
    IReadOnlyList<ReportingRestatementRequest> RestatementRequests);

/// <summary>Production service-principal identity used only for immutable artifact retention.</summary>
public sealed class ReportingArtifactRetentionAuthorityProvider : IReportingArtifactRetentionAuthorityProvider
{
    public ValueTask<ReportingAuthorityScope> ResolveAsync(
        GovernedReportingRun governedRun,
        ReportingGovernanceCallerContext releaseCaller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(governedRun);
        ArgumentNullException.ThrowIfNull(releaseCaller);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ReportingAuthorityScope(
            "reporting-artifact-retention",
            governedRun.Scope.TenantId,
            governedRun.Scope.OrganizationId,
            governedRun.Scope.CompanyId,
            [ReportingGovernancePermission.ExecuteRun],
            ReportingCommandOrigin.ServicePrincipal,
            $"{releaseCaller.CorrelationId.Trim()}:artifact-retention",
            []));
    }
}

/// <summary>
/// Resolves the approved template and reconstructs immutable parameters exclusively from the
/// released predecessor snapshot. No HTTP restatement body can replace source rows or parameters.
/// </summary>
public sealed class GovernedReportingRestatementCertificationInputProvider :
    IReportingRestatementCertificationInputProvider
{
    private readonly GovernedReportingTemplateCatalog _templateCatalog;
    private readonly ReportingRunReadinessService _readiness;

    public GovernedReportingRestatementCertificationInputProvider(
        GovernedReportingTemplateCatalog templateCatalog,
        ReportingRunReadinessService readiness)
    {
        _templateCatalog = templateCatalog ?? throw new ArgumentNullException(nameof(templateCatalog));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public async ValueTask<ReportingRestatementCertificationInput> ResolveAsync(
        ReportingRestatementRequest request,
        GovernedReportingRun releasedPredecessor,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(releasedPredecessor);
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrWhiteSpace(releasedPredecessor.Snapshot.ParametersCanonicalJson)
            || string.IsNullOrWhiteSpace(releasedPredecessor.Snapshot.ParametersHash))
        {
            throw new ReportingGovernanceException(
                "Released predecessor has no immutable normalized parameter snapshot and cannot be restated.");
        }

        var parameters = ReportingRunCertificationService.DeserializeParameters(
            releasedPredecessor.Snapshot.ParametersCanonicalJson);
        var templateId = new VersionedReportTemplateIdDto(
            releasedPredecessor.TemplateId,
            int.Parse(releasedPredecessor.TemplateVersion, CultureInfo.InvariantCulture));
        var accessContext = new ReportAccessQueryContext(
            caller.ActorId.Trim(),
            caller.PrincipalIds.IsDefault ? [] : caller.PrincipalIds,
            releasedPredecessor.Scope.CompanyId,
            HasGlobalOverride: false,
            TenantId: releasedPredecessor.Scope.TenantId,
            RequireBoundScope: true);
        var template = _templateCatalog.Get(templateId, accessContext);
        var readiness = await _readiness.AssessAsync(
            new ReportingRunRequestDto(
                template.TemplateId,
                AsOfDate: parameters.AsOfDate,
                RequestedBy: caller.ActorId,
                Template: templateId,
                Parameters: parameters),
            accessContext,
            cancellationToken).ConfigureAwait(false);
        return new ReportingRestatementCertificationInput(template, readiness, accessContext);
    }
}

/// <summary>
/// Server-owned placeholder disclosure at request time. Approval replaces it with an exact diff of
/// the predecessor and recertified immutable source rows before the request is marked Approved.
/// </summary>
public sealed class GovernedReportingRestatementChangedLineResolver :
    IReportingRestatementChangedLineResolver
{
    public ValueTask<ImmutableArray<ReportingRestatementChangedLine>> ResolveAsync(
        GovernedReportingRun releasedPredecessor,
        string reason,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releasedPredecessor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        if (releasedPredecessor.GovernanceState != GovernedReportingState.Released
            || releasedPredecessor.Release is null)
        {
            throw new ReportingGovernanceException(
                "Changed-line discovery requires an immutable Released predecessor.");
        }

        return ValueTask.FromResult(ImmutableArray.Create(new ReportingRestatementChangedLine(
            "authoritative-recertification",
            releasedPredecessor.Snapshot.SnapshotHash,
            "pending-exact-source-recapture",
            [
                $"snapshot:{releasedPredecessor.Snapshot.SnapshotId}:{releasedPredecessor.Snapshot.SnapshotHash}",
                $"release-manifest:{releasedPredecessor.Release.ManifestId}:{releasedPredecessor.Release.ManifestHash}"
            ])));
    }
}

/// <summary>
/// Endpoint-facing governed reporting facade. Every caller context is server derived, while
/// readiness, artifacts, release evidence, changed lines, and replacement certification are
/// resolved behind the service boundary.
/// </summary>
public interface IReportingGovernanceEndpointCoordinator
{
    Task<GovernedReportingRun> GetAsync(
        string runId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReportingGovernedArtifactDescriptor>> ListRetainedArtifactsAsync(
        string runId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<ReportingGovernedArtifactDescriptor>>(
            new NotSupportedException("Governed reporting artifact discovery is unavailable."));

    Task<ReportingGovernedArtifactDownload> DownloadRetainedArtifactAsync(
        string runId,
        string artifactId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ReportingGovernedArtifactDownload>(
            new NotSupportedException("Governed reporting artifact download is unavailable."));

    Task<IReadOnlyList<GovernedReportingRun>> ListAsync(
        string seriesId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<ReportingGovernanceSeriesHistory> GetSeriesHistoryAsync(
        string seriesId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ReportingGovernanceSeriesHistory>(
            new NotSupportedException("Reporting series discovery is unavailable."));

    Task<IReadOnlyList<ReportingRestatementRequest>> ListRestatementRequestsAsync(
        string predecessorRunId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<ReportingRestatementRequest>>(
            new NotSupportedException("Reporting restatement discovery is unavailable."));

    Task<ReportingRestatementRequest> GetRestatementRequestAsync(
        string requestId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ReportingRestatementRequest>(
            new NotSupportedException("Reporting restatement discovery is unavailable."));

    Task<GovernedReportingRun> CreateFromCompletedCertifiedManifestAsync(
        string manifestRunId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> ValidateAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> SubmitAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> ApproveAsync(
        string runId,
        long expectedVersion,
        string decisionNote,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<GovernedReportingRun> ReleaseAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<ReportingRestatementRequest> RequestRestatementAsync(
        string predecessorRunId,
        long expectedPredecessorVersion,
        string reason,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);

    Task<ReportingRestatementApprovalResult> ApproveRestatementAsync(
        string requestId,
        long expectedRequestVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates the completed renderer manifest with the canonical reporting governance aggregate,
/// immutable artifact vault, and server-owned certification/readiness evidence. The legacy
/// orchestration approval state is intentionally not mutated; governance has one authoritative
/// lifecycle here.
/// </summary>
public sealed partial class ReportingGovernanceCoordinatorService : IReportingGovernanceEndpointCoordinator
{
    private static readonly UserPermission ReportingReadPermissions =
        UserPermission.ViewReporting
        | UserPermission.ManageReporting
        | UserPermission.ApproveReporting
        | UserPermission.DeliverReporting
        | UserPermission.AdminMaintenance;

    private static readonly ImmutableArray<ReportingGovernancePermission> AllGovernancePermissions =
        Enum.GetValues<ReportingGovernancePermission>().ToImmutableArray();

    private readonly ReportingGovernanceService _governance;
    private readonly IReportingGovernanceRepository _repository;
    private readonly ReportingRunCertificationService _certification;
    private readonly IReportingOrchestrationService _orchestration;
    private readonly ReportingArtifactVaultService _artifactVault;
    private readonly IReportingCertifiedArtifactProducer _artifactProducer;
    private readonly IReportingArtifactRetentionAuthorityProvider _retentionAuthorityProvider;
    private readonly IReportingRestatementChangedLineResolver _restatementChangedLineResolver;
    private readonly IReportingRestatementCertificationInputProvider _restatementCertificationProvider;

    public ReportingGovernanceCoordinatorService(
        ReportingGovernanceService governance,
        IReportingGovernanceRepository repository,
        ReportingRunCertificationService certification,
        IReportingOrchestrationService orchestration,
        ReportingArtifactVaultService artifactVault,
        IReportingCertifiedArtifactProducer artifactProducer,
        IReportingArtifactRetentionAuthorityProvider retentionAuthorityProvider,
        IReportingRestatementChangedLineResolver restatementChangedLineResolver,
        IReportingRestatementCertificationInputProvider restatementCertificationProvider)
    {
        _governance = governance ?? throw new ArgumentNullException(nameof(governance));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _certification = certification ?? throw new ArgumentNullException(nameof(certification));
        _orchestration = orchestration ?? throw new ArgumentNullException(nameof(orchestration));
        _artifactVault = artifactVault ?? throw new ArgumentNullException(nameof(artifactVault));
        _artifactProducer = artifactProducer ?? throw new ArgumentNullException(nameof(artifactProducer));
        _retentionAuthorityProvider = retentionAuthorityProvider
            ?? throw new ArgumentNullException(nameof(retentionAuthorityProvider));
        _restatementChangedLineResolver = restatementChangedLineResolver
            ?? throw new ArgumentNullException(nameof(restatementChangedLineResolver));
        _restatementCertificationProvider = restatementCertificationProvider
            ?? throw new ArgumentNullException(nameof(restatementCertificationProvider));
    }

    public async Task<GovernedReportingRun> GetAsync(
        string runId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(runId, nameof(runId));
        ValidateCaller(caller);
        EnsureReadPermission(caller);

        var run = await ReadRunAsync(caller.TenantId, runId.Trim(), cancellationToken).ConfigureAwait(false);
        if (run is null || !CanRead(run, caller))
        {
            throw new ReportingGovernanceNotFoundException(
                $"Reporting run '{runId.Trim()}' was not found in the caller tenant.");
        }

        return run;
    }

    public async Task<IReadOnlyList<ReportingGovernedArtifactDescriptor>> ListRetainedArtifactsAsync(
        string runId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var run = await GetAsync(runId, caller, cancellationToken).ConfigureAwait(false);
        EnsureRetainedArtifactsAreDiscoverable(run);
        var manifest = GetRequiredManifestForRun(run);
        var declarations = ReportingArtifactDeclaration.Build(manifest)
            .ToDictionary(static item => item.ArtifactId, StringComparer.Ordinal);
        var access = BuildArtifactAccessContext(run, caller);
        var package = await _artifactVault
            .GetPackageForReleaseAsync(BuildPackageId(run), access, cancellationToken)
            .ConfigureAwait(false);
        if (package.Artifacts.Length != declarations.Count
            || package.Artifacts.Any(record => !declarations.ContainsKey(record.ArtifactId)))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact package for run '{run.RunId}' is incomplete.");
        }

        return package.Artifacts
            .OrderBy(static item => item.ArtifactId, StringComparer.Ordinal)
            .Where(record =>
                declarations[record.ArtifactId].Kind == ReportingDeclaredArtifactKind.Preview
                || run.GovernanceState == GovernedReportingState.Released)
            .Select(record => BuildArtifactDescriptor(
                run,
                record,
                declarations.TryGetValue(record.ArtifactId, out var declaration)
                    ? declaration
                    : throw new ReportingArtifactCatalogIntegrityException(
                        $"Retained artifact '{record.ArtifactId}' is not declared by run '{run.RunId}'.")))
            .ToArray();
    }

    public async Task<ReportingGovernedArtifactDownload> DownloadRetainedArtifactAsync(
        string runId,
        string artifactId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(artifactId, nameof(artifactId));
        var run = await GetAsync(runId, caller, cancellationToken).ConfigureAwait(false);
        EnsureRetainedArtifactsAreDiscoverable(run);
        var manifest = GetRequiredManifestForRun(run);
        var declaration = ReportingArtifactDeclaration.Build(manifest)
            .SingleOrDefault(item => string.Equals(item.ArtifactId, artifactId.Trim(), StringComparison.Ordinal))
            ?? throw new ReportingGovernanceNotFoundException(
                $"Artifact '{artifactId.Trim()}' is not declared by reporting run '{run.RunId}'.");
        EnsureArtifactDownloadIsAuthorized(run, declaration, caller);
        var download = await _artifactVault
            .ReadForDownloadAsync(
                BuildPackageId(run),
                declaration.ArtifactId,
                BuildArtifactAccessContext(run, caller),
                cancellationToken)
            .ConfigureAwait(false);
        var descriptor = BuildArtifactDescriptor(run, download.Artifact, declaration);
        if (download.Content.LongLength != descriptor.ByteLength
            || !string.Equals(ComputeSha256(download.Content), descriptor.ContentHashSha256, StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact '{run.RunId}/{declaration.ArtifactId}' failed download verification.");
        }

        return new ReportingGovernedArtifactDownload(descriptor, download.Content, download.AuditEventId);
    }

    public async Task<IReadOnlyList<GovernedReportingRun>> ListAsync(
        string seriesId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(seriesId, nameof(seriesId));
        ValidateCaller(caller);
        EnsureReadPermission(caller);

        var runs = await _repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListRunsBySeriesAsync(
                caller.TenantId.Trim(),
                seriesId.Trim(),
                ct),
            cancellationToken).ConfigureAwait(false);

        return runs
            .Where(run => CanRead(run, caller))
            .OrderBy(static run => run.Revision)
            .ToArray();
    }

    public async Task<ReportingGovernanceSeriesHistory> GetSeriesHistoryAsync(
        string seriesId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(seriesId, nameof(seriesId));
        var runs = await ListAsync(seriesId, caller, cancellationToken).ConfigureAwait(false);
        if (runs.Count == 0)
        {
            throw new ReportingGovernanceNotFoundException(
                $"Reporting series '{seriesId.Trim()}' was not found in the caller tenant and access scope.");
        }

        var accessibleRunIds = runs
            .Select(static run => run.RunId)
            .ToHashSet(StringComparer.Ordinal);
        var requests = await _repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListRestatementRequestsBySeriesAsync(
                caller.TenantId.Trim(),
                seriesId.Trim(),
                ct),
            cancellationToken).ConfigureAwait(false);
        return new ReportingGovernanceSeriesHistory(
            seriesId.Trim(),
            runs,
            requests
                .Where(request => accessibleRunIds.Contains(request.PredecessorRunId))
                .OrderBy(static request => request.RequestedAtUtc)
                .ThenBy(static request => request.RequestId, StringComparer.Ordinal)
                .ToArray());
    }

    public async Task<IReadOnlyList<ReportingRestatementRequest>> ListRestatementRequestsAsync(
        string predecessorRunId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(predecessorRunId, nameof(predecessorRunId));
        var predecessor = await GetAsync(predecessorRunId, caller, cancellationToken).ConfigureAwait(false);
        var requests = await _repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.ListRestatementRequestsBySeriesAsync(
                caller.TenantId.Trim(),
                predecessor.SeriesId,
                ct),
            cancellationToken).ConfigureAwait(false);
        return requests
            .Where(request => string.Equals(
                request.PredecessorRunId,
                predecessor.RunId,
                StringComparison.Ordinal))
            .OrderBy(static request => request.RequestedAtUtc)
            .ThenBy(static request => request.RequestId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ReportingRestatementRequest> GetRestatementRequestAsync(
        string requestId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(requestId, nameof(requestId));
        ValidateCaller(caller);
        EnsureReadPermission(caller);
        var request = await _repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.GetRestatementRequestAsync(
                caller.TenantId.Trim(),
                requestId.Trim(),
                ct),
            cancellationToken).ConfigureAwait(false)
            ?? throw new ReportingGovernanceNotFoundException(
                $"Restatement request '{requestId.Trim()}' was not found in the caller tenant.");
        _ = await GetAsync(request.PredecessorRunId, caller, cancellationToken).ConfigureAwait(false);
        return request;
    }

    public async Task<GovernedReportingRun> CreateFromCompletedCertifiedManifestAsync(
        string manifestRunId,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        ValidateCaller(caller);
        var manifest = GetRequiredCompletedCertifiedManifest(caller.TenantId, manifestRunId);
        var scope = manifest.OperationalScope!;
        var authority = ResolveAuthority(caller, scope);
        EnsurePermission(authority, ReportingGovernancePermission.CreateRun);
        EnsurePermission(authority, ReportingGovernancePermission.ExecuteRun);
        EnsureAuthorityCanAccess(manifest.ImmutableAccessScope!, authority);
        var existingRun = await ReadRunAsync(
            scope.TenantId,
            manifest.RunId,
            cancellationToken).ConfigureAwait(false);
        if (existingRun is not null && existingRun.GovernanceState != GovernedReportingState.Draft)
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' is already governed as {existingRun.GovernanceState}.");
        }

        var request = new ReportingRunCreationRequest(
            manifest.RunId,
            NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId,
            manifest.ResolvedTemplate!.Name.Trim(),
            manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture),
            scope,
            manifest.ImmutableAccessScope!,
            manifest.CertifiedSnapshot!);

        var run = existingRun;
        if (run is null)
        {
            run = await _governance.CreateRunAsync(request, authority, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            EnsureRunMatchesManifest(run, manifest);
            EnsureAuthorityCanAccess(run, authority);
        }

        if (run.GovernanceState != GovernedReportingState.Draft)
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' is already governed as {run.GovernanceState}.");
        }

        if (run.ExecutionState is GovernedReportingExecutionState.Queued or GovernedReportingExecutionState.Failed)
        {
            run = await _governance
                .BeginExecutionAsync(run.RunId, run.Version, authority, cancellationToken)
                .ConfigureAwait(false);
        }

        if (run.ExecutionState is not (
                GovernedReportingExecutionState.Running or GovernedReportingExecutionState.Succeeded))
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' cannot reconcile governance execution state {run.ExecutionState}.");
        }

        try
        {
            // Persist the execution intent before invoking the renderer. Every downstream failure
            // can then be reconciled to this governed attempt instead of disappearing before a run
            // exists or leaving the run indefinitely Running.
            var production = await _artifactProducer
                .ProduceAsync(manifest, cancellationToken)
                .ConfigureAwait(false);
            ValidateProduction(manifest, production);
            ValidateRenderedManifestBinding(manifest, run, production);

            var retentionAuthority = await _retentionAuthorityProvider
                .ResolveAsync(run, caller, cancellationToken)
                .ConfigureAwait(false);
            ValidateRetentionAuthority(
                run,
                releaseAuthority: null,
                retentionAuthority);
            await RetainAndVerifyProductionAsync(
                run,
                production,
                retentionAuthority,
                cancellationToken).ConfigureAwait(false);

            // Succeeded is a verified terminal state: do not publish it until every declared byte
            // has been durably retained and read-back verification has matched the renderer output.
            if (run.ExecutionState == GovernedReportingExecutionState.Running)
            {
                run = await _governance
                    .CompleteExecutionAsync(run.RunId, run.Version, authority, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (run.ExecutionState != GovernedReportingExecutionState.Succeeded)
            {
                throw new ReportingGovernanceException(
                    $"Completed manifest '{manifest.RunId}' did not reach Succeeded after artifact retention verification.");
            }
        }
        catch (Exception exception)
        {
            await PersistFailedExecutionAsync(run, authority, exception).ConfigureAwait(false);
            throw;
        }

        return run;
    }

    private async Task PersistFailedExecutionAsync(
        GovernedReportingRun attemptedRun,
        ReportingAuthorityScope authority,
        Exception operationFailure)
    {
        GovernedReportingRun? current;
        try
        {
            current = await ReadRunAsync(
                    attemptedRun.Scope.TenantId,
                    attemptedRun.RunId,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception readFailure)
        {
            throw new AggregateException(
                "Reporting execution failed and its terminal state could not be read for recovery.",
                operationFailure,
                readFailure);
        }

        if (current?.ExecutionState == GovernedReportingExecutionState.Succeeded)
        {
            return;
        }

        if (current?.ExecutionState != GovernedReportingExecutionState.Running)
        {
            return;
        }

        try
        {
            _ = await _governance
                .FailExecutionAsync(
                    current.RunId,
                    current.Version,
                    "Artifact production or retention verification failed. Inspect the governed audit evidence, repair the renderer or artifact store, and retry the same run.",
                    authority,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception terminalStateFailure)
        {
            throw new AggregateException(
                "Reporting execution failed and its Failed terminal receipt could not be persisted.",
                operationFailure,
                terminalStateFailure);
        }
    }

    public async Task<GovernedReportingRun> ValidateAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        var manifest = GetRequiredManifestForRun(run);
        var readiness = _certification.BuildGovernanceReadiness(
            run.RunId,
            new CertifiedReportingRunContext(
                run.Scope,
                run.Access,
                run.Snapshot,
                manifest.AuthoritativeSource!,
                manifest.CertifiedDatasetRows,
                manifest.Readiness!),
            manifest.Readiness!);
        var authority = ResolveAuthority(caller, run.Scope);
        return await _governance
            .ValidateAsync(run.RunId, expectedVersion, readiness, authority, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GovernedReportingRun> SubmitAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        return await _governance
            .SubmitAsync(run.RunId, expectedVersion, ResolveAuthority(caller, run.Scope), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GovernedReportingRun> ApproveAsync(
        string runId,
        long expectedVersion,
        string decisionNote,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(decisionNote, nameof(decisionNote));
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        return await _governance
            .ApproveAsync(
                run.RunId,
                expectedVersion,
                decisionNote.Trim(),
                ResolveAuthority(caller, run.Scope),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GovernedReportingRun> ReleaseAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        var run = await GetForMutationAsync(runId, expectedVersion, caller, cancellationToken).ConfigureAwait(false);
        if (run.ExecutionState != GovernedReportingExecutionState.Succeeded
            || run.GovernanceState != GovernedReportingState.Approved
            || run.Approval is null)
        {
            throw new ReportingGovernanceException(
                $"Only an approved, successfully executed run can retain artifacts and release; run '{run.RunId}' is {run.ExecutionState}/{run.GovernanceState}.");
        }

        ReportingGovernanceCanonicalValidation.ValidateFinalReleaseSnapshot(run.Snapshot);

        var releaseAuthority = ResolveAuthority(caller, run.Scope);
        var retained = await ReadAndVerifyRetainedPackageAsync(
            run,
            caller,
            cancellationToken).ConfigureAwait(false);
        var evidenceIds = BuildReleaseEvidence(run, retained.Sections, retained.AuditEventIds);
        var releaseEvidence = new ReportingReleaseEvidence(
            retained.ManifestId,
            retained.ManifestHash,
            retained.Artifacts,
            evidenceIds);

        // This is deliberately the last awaited external read before the governance commit. Read
        // and hash the retained package first, then recheck ledger + canonical queue state so a
        // long artifact download cannot widen the stale-certification window.
        var manifest = GetRequiredManifestForRun(run);
        if (ReportingCertifiedLedgerPresentationBinding.IsRequired(manifest)
            && !run.Snapshot.RequiresCertifiedLedgerPresentation)
        {
            throw new ReportingGovernanceException(
                "Final release is blocked because this retained capital-account client package predates exact checkpoint-bound ledger-presentation certification. Regenerate and reapprove it from a fresh certified snapshot.");
        }

        await _certification.RevalidateForReleaseAsync(
                manifest.ResolvedParameters
                    ?? throw new ReportingGovernanceException(
                        $"Reporting run '{run.RunId}' has no retained parameter snapshot for final release revalidation."),
                manifest.TemplateId,
                manifest.AuthoritativeSource
                    ?? throw new ReportingGovernanceException(
                        $"Reporting run '{run.RunId}' has no retained authoritative source for final release revalidation."),
                run.Snapshot,
                new ReportAccessQueryContext(
                    caller.ActorId.Trim(),
                    caller.PrincipalIds.IsDefault ? [] : caller.PrincipalIds,
                    run.Scope.CompanyId,
                    HasGlobalOverride: false,
                    TenantId: run.Scope.TenantId,
                    RequireBoundScope: true),
                cancellationToken)
            .ConfigureAwait(false);

        return await _governance
            .ReleaseAsync(run.RunId, expectedVersion, releaseEvidence, releaseAuthority, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReportingRestatementRequest> RequestRestatementAsync(
        string predecessorRunId,
        long expectedPredecessorVersion,
        string reason,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(reason, nameof(reason));
        var predecessor = await GetForMutationAsync(
            predecessorRunId,
            expectedPredecessorVersion,
            caller,
            cancellationToken).ConfigureAwait(false);
        var changedLines = await _restatementChangedLineResolver
            .ResolveAsync(predecessor, reason.Trim(), caller, cancellationToken)
            .ConfigureAwait(false);
        var command = new ReportingRestatementRequestCommand(
            predecessor.RunId,
            expectedPredecessorVersion,
            reason.Trim(),
            changedLines);
        return await _governance
            .RequestRestatementAsync(command, ResolveAuthority(caller, predecessor.Scope), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReportingRestatementApprovalResult> ApproveRestatementAsync(
        string requestId,
        long expectedRequestVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken = default)
    {
        RequireText(requestId, nameof(requestId));
        ValidateCaller(caller);
        EnsureReadPermission(caller);

        var state = await _repository.ExecuteTransactionAsync(async (transaction, ct) =>
        {
            var request = await transaction
                .GetRestatementRequestAsync(caller.TenantId.Trim(), requestId.Trim(), ct)
                .ConfigureAwait(false);
            if (request is null)
            {
                return null;
            }

            var predecessor = await transaction
                .GetRunAsync(caller.TenantId.Trim(), request.PredecessorRunId, ct)
                .ConfigureAwait(false);
            return predecessor is null ? null : new RestatementState(request, predecessor);
        }, cancellationToken).ConfigureAwait(false);

        if (state is null || !CanRead(state.Predecessor, caller))
        {
            throw new ReportingGovernanceNotFoundException(
                $"Restatement request '{requestId.Trim()}' was not found in the caller tenant.");
        }

        if (state.Request.Version != expectedRequestVersion)
        {
            throw new ReportingGovernanceConcurrencyException(
                $"Aggregate '{state.Request.RequestId}' version conflict: expected {expectedRequestVersion}, actual {state.Request.Version}.");
        }

        var approvalAuthority = ResolveAuthority(caller, state.Predecessor.Scope);
        if (!approvalAuthority.HasPermission(ReportingGovernancePermission.ExecuteRun))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Restatement approval also requires ExecuteRun permission before replacement artifacts can be generated and retained.");
        }

        var certificationInput = await _restatementCertificationProvider
            .ResolveAsync(state.Request, state.Predecessor, caller, cancellationToken)
            .ConfigureAwait(false);
        ValidateRestatementCertificationInput(state.Predecessor, caller, certificationInput);
        certificationInput = certificationInput with
        {
            // A restatement is a new certification of the released run's data, not a new access
            // grant. Rebind the current, server-resolved template to the predecessor's retained
            // policy snapshot so actor-defaulted owners (and later template-policy edits) cannot
            // silently replace the immutable audience when a different operator approves it.
            Template = BindRestatementAccessPolicy(
                certificationInput.Template,
                state.Predecessor)
        };
        var certified = await _certification.CertifyAsync(
            certificationInput.Template,
            certificationInput.Readiness,
            certificationInput.AccessContext,
            cancellationToken).ConfigureAwait(false);
        if (!Equals(certified.OperationalScope, state.Predecessor.Scope)
            || !AccessScopesEqual(certified.AccessScope, state.Predecessor.Access))
        {
            throw new ReportingGovernanceException(
                "Restatement certification must preserve the predecessor's immutable operational and access scope.");
        }

        if (string.Equals(
            certified.Snapshot.SnapshotHash,
            state.Predecessor.Snapshot.SnapshotHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                "Restatement certification must produce a changed certified snapshot.");
        }

        var predecessorManifest = GetRequiredManifestForRun(state.Predecessor);
        var replacementManifest = await _orchestration.ExecuteAsync(
            new ReportingJobContract(
                JobId: $"restatement-{state.Request.RequestId}",
                TemplateId: certificationInput.Template.TemplateId,
                AsOfDate: certified.Readiness.ResolvedParameters.AsOfDate,
                Trigger: ReportingRunTrigger.AdHoc,
                MaxRetries: 0,
                RequestedBy: state.Request.RequestedBy.ActorId,
                RequestedAtUtc: DateTimeOffset.UtcNow,
                DatasetRows: certified.DatasetRows,
                ReportWriterDatasetSourceId: certified.AuthoritativeSource.SourceId,
                ReportWriterDatasetSourceLabel: "Certified durable ledger journal",
                AccessPolicy: certificationInput.Template.AccessPolicy,
                RetryReason: state.Request.Reason,
                AllowRestatement: true,
                ResolvedTemplate: certified.Readiness.ResolvedTemplate,
                ResolvedParameters: certified.Readiness.ResolvedParameters,
                Readiness: certified.Readiness,
                OperationalScope: certified.OperationalScope,
                ImmutableAccessScope: certified.AccessScope,
                CertifiedSnapshot: certified.Snapshot,
                AuthoritativeSource: certified.AuthoritativeSource,
                GovernedRunSeriesId: state.Predecessor.SeriesId),
            cancellationToken).ConfigureAwait(false);
        ValidateCompletedCertifiedManifest(replacementManifest);
        if (!string.Equals(replacementManifest.RunSeriesId, state.Predecessor.SeriesId, StringComparison.Ordinal)
            || !Equals(replacementManifest.CertifiedSnapshot, certified.Snapshot))
        {
            throw new ReportingGovernanceException(
                "Restatement renderer did not preserve the governed series and replacement certification.");
        }

        var production = await _artifactProducer
            .ProduceAsync(replacementManifest, cancellationToken)
            .ConfigureAwait(false);
        ValidateProduction(replacementManifest, production);
        var changedLines = BuildRestatementChangedLines(
            state.Predecessor,
            predecessorManifest,
            replacementManifest);

        var command = new ReportingRestatementApprovalCommand(
            state.Request.RequestId,
            expectedRequestVersion,
            certified.Snapshot,
            replacementManifest.RunId,
            changedLines);
        var prospectiveRun = new GovernedReportingRun(
            replacementManifest.RunId,
            state.Predecessor.SeriesId,
            checked(state.Predecessor.Revision + 1),
            state.Predecessor.TemplateId,
            state.Predecessor.TemplateVersion,
            certified.OperationalScope,
            certified.AccessScope,
            certified.Snapshot,
            ResolveAuthority(caller, state.Predecessor.Scope),
            DateTimeOffset.UtcNow,
            state.Predecessor.RunId,
            GovernedReportingExecutionState.Queued,
            GovernedReportingState.Draft,
            Version: 1,
            Readiness: null,
            Approval: null,
            Release: null,
            AuditTrail: [],
            RestatementRequestId: state.Request.RequestId);
        var retentionAuthority = await _retentionAuthorityProvider
            .ResolveAsync(prospectiveRun, caller, cancellationToken)
            .ConfigureAwait(false);
        ValidateRetentionAuthority(prospectiveRun, releaseAuthority: null, retentionAuthority);
        await RetainAndVerifyProductionAsync(
            prospectiveRun,
            production,
            retentionAuthority,
            cancellationToken).ConfigureAwait(false);

        var approved = await _governance
            .ApproveRestatementAsync(
                command,
                approvalAuthority,
                cancellationToken)
            .ConfigureAwait(false);
        var draftRun = approved.DraftRun;
        if (!string.Equals(draftRun.RunId, replacementManifest.RunId, StringComparison.Ordinal)
            || !Equals(draftRun.Snapshot, certified.Snapshot))
        {
            throw new ReportingGovernanceException(
                "Approved restatement draft is not bound to the recertified renderer manifest.");
        }
        if (draftRun.ExecutionState == GovernedReportingExecutionState.Queued)
        {
            // The approving human authorizes the governed execution transition. The separate
            // service-principal authority is intentionally limited to artifact retention and must
            // not gain report access by impersonating principals from the immutable audience.
            draftRun = await _governance.BeginExecutionAsync(
                draftRun.RunId,
                draftRun.Version,
                approvalAuthority,
                cancellationToken).ConfigureAwait(false);
        }
        if (draftRun.ExecutionState == GovernedReportingExecutionState.Running)
        {
            draftRun = await _governance.CompleteExecutionAsync(
                draftRun.RunId,
                draftRun.Version,
                approvalAuthority,
                cancellationToken).ConfigureAwait(false);
        }
        if (draftRun.ExecutionState != GovernedReportingExecutionState.Succeeded
            || draftRun.GovernanceState != GovernedReportingState.Draft)
        {
            throw new ReportingGovernanceException(
                "Restatement approval did not finish in the required Succeeded/Draft state.");
        }

        return new ReportingRestatementApprovalResult(approved.Request, draftRun);
    }

    private async Task<GovernedReportingRun> GetForMutationAsync(
        string runId,
        long expectedVersion,
        ReportingGovernanceCallerContext caller,
        CancellationToken cancellationToken)
    {
        var run = await GetAsync(runId, caller, cancellationToken).ConfigureAwait(false);
        if (expectedVersion <= 0 || run.Version != expectedVersion)
        {
            throw new ReportingGovernanceConcurrencyException(
                $"Aggregate '{run.RunId}' version conflict: expected {expectedVersion}, actual {run.Version}.");
        }

        return run;
    }

    private ValueTask<GovernedReportingRun?> ReadRunAsync(
        string tenantId,
        string runId,
        CancellationToken cancellationToken) =>
        _repository.ExecuteTransactionAsync(
            (transaction, ct) => transaction.GetRunAsync(tenantId.Trim(), runId.Trim(), ct),
            cancellationToken);

    private ReportingOutputManifest GetRequiredManifestForRun(GovernedReportingRun run)
    {
        var manifest = GetRequiredCompletedCertifiedManifest(run.Scope.TenantId, run.RunId);
        EnsureRunMatchesManifest(run, manifest);
        return manifest;
    }

    private ReportingOutputManifest GetRequiredCompletedCertifiedManifest(
        string tenantId,
        string manifestRunId)
    {
        RequireText(tenantId, nameof(tenantId));
        RequireText(manifestRunId, nameof(manifestRunId));
        var manifest = _orchestration.GetManifest(tenantId.Trim(), manifestRunId.Trim())
            ?? throw new ReportingGovernanceNotFoundException(
                $"Completed reporting manifest '{manifestRunId.Trim()}' was not found.");
        ValidateCompletedCertifiedManifest(manifest);
        return manifest;
    }

    private static void ValidateCompletedCertifiedManifest(ReportingOutputManifest manifest)
    {
        if (manifest.Status != ReportingRunStatus.Draft
            || manifest.AttemptCount <= 0
            || !string.IsNullOrWhiteSpace(manifest.FailureReason))
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' is not a successfully completed Draft.");
        }

        RequireText(manifest.RunId, nameof(manifest.RunId));
        RequireText(manifest.TemplateId, nameof(manifest.TemplateId));
        if (manifest.ResolvedTemplate is null
            || manifest.ResolvedParameters is null
            || manifest.Readiness is null
            || manifest.OperationalScope is null
            || manifest.ImmutableAccessScope is null
            || manifest.CertifiedSnapshot is null
            || manifest.AuthoritativeSource is null
            || manifest.CertifiedDatasetRows.IsDefault
            || manifest.CertifiedDatasetRows.Length != manifest.AuthoritativeSource.LedgerLineCount)
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' is missing certified template, parameter, readiness, scope, access, or snapshot state.");
        }

        if (!string.Equals(
                manifest.TemplateId.Trim(),
                manifest.ResolvedTemplate.Name.Trim(),
                StringComparison.Ordinal)
            || manifest.ResolvedTemplate.Version <= 0
            || manifest.RunAttemptOrdinal is <= 0)
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' has inconsistent template or run-version identity.");
        }

        ValidateReadiness(manifest.Readiness, manifest.ResolvedTemplate, manifest.ResolvedParameters);
        ValidateManifestScope(manifest);

        if (manifest.Sections.IsDefaultOrEmpty
            || manifest.Sections.Any(section =>
                string.IsNullOrWhiteSpace(section.SectionId)
                || !IsSha256(section.Hash)
                || section.Lineage is null
                || !string.Equals(section.DatasetSnapshotId, manifest.CertifiedSnapshot.SnapshotId, StringComparison.Ordinal)
                || !string.Equals(section.ReconciliationCheckpointId, manifest.CertifiedSnapshot.ReconciliationCheckpointId, StringComparison.Ordinal)
                || !string.Equals(section.Lineage.SectionId, section.SectionId, StringComparison.Ordinal)
                || !string.Equals(section.Lineage.DatasetSnapshotId, manifest.CertifiedSnapshot.SnapshotId, StringComparison.Ordinal)
                || !string.Equals(section.Lineage.DatasetSnapshotHash, manifest.CertifiedSnapshot.SnapshotHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(section.Lineage.ReconciliationCheckpointId, manifest.CertifiedSnapshot.ReconciliationCheckpointId, StringComparison.Ordinal)
                || section.Lineage.CapturedAtUtc != manifest.CertifiedSnapshot.CapturedAtUtc))
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' does not have complete lineage bound to its certified snapshot.");
        }

        if (manifest.Artifacts.IsDefaultOrEmpty
            || manifest.Artifacts.Any(string.IsNullOrWhiteSpace)
            || manifest.Artifacts.Distinct(StringComparer.Ordinal).Count() != manifest.Artifacts.Length)
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' does not declare a complete unique artifact set.");
        }
    }

    internal static void ValidateManifestScope(ReportingOutputManifest manifest)
    {
        var scope = manifest.OperationalScope!;
        var snapshot = manifest.CertifiedSnapshot!;
        var access = manifest.ImmutableAccessScope!;
        RequireText(scope.TenantId, nameof(scope.TenantId));
        RequireText(scope.OrganizationId, nameof(scope.OrganizationId));
        RequireText(scope.CompanyId, nameof(scope.CompanyId));
        RequireText(scope.BookId, nameof(scope.BookId));
        RequireText(scope.PeriodId, nameof(scope.PeriodId));
        RequireText(access.PolicyId, nameof(access.PolicyId));
        RequireText(access.PolicyVersion, nameof(access.PolicyVersion));
        if (!IsSha256(access.PolicyHash) || !IsSha256(snapshot.SnapshotHash))
        {
            throw new ReportingGovernanceException(
                "Certified snapshot and immutable access policy hashes must be SHA-256 values.");
        }

        if (!string.Equals(scope.TenantId, snapshot.TenantId, StringComparison.Ordinal)
            || !string.Equals(scope.OrganizationId, snapshot.OrganizationId, StringComparison.Ordinal)
            || !SameOptional(scope.CompanyId, snapshot.CompanyId)
            || !SameOptional(scope.FundId, snapshot.FundId)
            || !string.Equals(scope.BookId, snapshot.BookId, StringComparison.Ordinal)
            || !string.Equals(scope.PeriodId, snapshot.PeriodId, StringComparison.Ordinal)
            || manifest.ResolvedParameters!.AsOfDate != manifest.AsOfDate
            || !string.Equals(manifest.ResolvedParameters.PeriodId, scope.PeriodId, StringComparison.Ordinal)
            || !string.Equals(manifest.ResolvedParameters.Scope.FundProfileId, scope.FundId, StringComparison.Ordinal)
            || !string.Equals(ResolveBookId(manifest.ResolvedParameters), scope.BookId, StringComparison.Ordinal))
        {
            throw new ReportingGovernanceException(
                $"Orchestration manifest '{manifest.RunId}' has scope, parameter, readiness, or snapshot drift.");
        }
    }

    private static void ValidateReadiness(
        ReportingRunReadinessDto readiness,
        VersionedReportTemplateIdDto template,
        ReportingRunParametersDto parameters)
    {
        var finality = parameters.Finality;
        var hasRequestedAuthority = finality == ReportingFinalityDto.Final
            ? readiness.CanGenerateFinal
            : readiness.CanGenerateDraft;
        if (!hasRequestedAuthority
            || readiness.Checks is null
            || readiness.Checks.Count == 0
            || readiness.BlockingReasons is null
            || readiness.BlockingReasons.Count != 0
            || !IsSha256(readiness.EvidenceHash)
            || !Equals(readiness.ResolvedTemplate, template)
            || readiness.ResolvedParameters.AsOfDate != parameters.AsOfDate
            || !string.Equals(readiness.ResolvedParameters.PeriodId, parameters.PeriodId, StringComparison.Ordinal)
            || readiness.Checks.Any(check =>
                string.IsNullOrWhiteSpace(check.CheckId)
                || check.EvidenceReferences is null
                || check.EvidenceReferences.Count == 0
                || check.EvidenceReferences.Any(string.IsNullOrWhiteSpace)
                || IsRequiredForFinality(check, finality)
                    && check.Status != ReportingRunReadinessStatusDto.Ready)
            || readiness.Checks.Select(static check => check.CheckId).Distinct(StringComparer.Ordinal).Count() != readiness.Checks.Count)
        {
            throw new ReportingGovernanceException(
                "A governed final report requires server-owned Ready status, final generation authority, and unique evidence-backed checks.");
        }
    }

    private static void EnsureRunMatchesManifest(
        GovernedReportingRun run,
        ReportingOutputManifest manifest)
    {
        if (!string.Equals(run.RunId, manifest.RunId, StringComparison.Ordinal)
            || !string.Equals(run.SeriesId, NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId, StringComparison.Ordinal)
            || !string.Equals(run.TemplateId, manifest.ResolvedTemplate!.Name, StringComparison.Ordinal)
            || !string.Equals(
                run.TemplateVersion,
                manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            || !Equals(run.Scope, manifest.OperationalScope)
            || !AccessScopesEqual(run.Access, manifest.ImmutableAccessScope)
            || !Equals(run.Snapshot, manifest.CertifiedSnapshot))
        {
            throw new ReportingGovernanceException(
                $"Completed manifest '{manifest.RunId}' does not match the immutable governed run snapshot.");
        }
    }

    private void ValidateRetainedManifestBinding(
        GovernedReportingRun run,
        ReportingRetainedManifestDocument document)
    {
        var mismatches = new List<string>();
        AddMismatch(mismatches, "runId", string.Equals(document.RunId, run.RunId, StringComparison.Ordinal));
        AddMismatch(mismatches, "seriesId", string.Equals(document.SeriesId, run.SeriesId, StringComparison.Ordinal));
        AddMismatch(mismatches, "templateId", string.Equals(document.TemplateId, run.TemplateId, StringComparison.Ordinal));
        AddMismatch(mismatches, "resolvedTemplate.name", string.Equals(document.ResolvedTemplate.Name, run.TemplateId, StringComparison.Ordinal));
        AddMismatch(
            mismatches,
            "resolvedTemplate.version",
            string.Equals(
                document.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture),
                run.TemplateVersion,
                StringComparison.Ordinal));
        AddMismatch(mismatches, "scope", Equals(document.Scope, run.Scope));
        AddMismatch(mismatches, "access", AccessScopesEqual(document.Access, run.Access));
        AddMismatch(mismatches, "snapshot", Equals(document.Snapshot, run.Snapshot));
        AddMismatch(
            mismatches,
            "parametersCanonicalJson",
            string.Equals(document.ParametersCanonicalJson, run.Snapshot.ParametersCanonicalJson, StringComparison.Ordinal));
        AddMismatch(
            mismatches,
            "parametersHash",
            string.Equals(document.ParametersHash, run.Snapshot.ParametersHash, StringComparison.OrdinalIgnoreCase));
        AddMismatch(
            mismatches,
            "sourceCheckpointId",
            string.Equals(document.AuthoritativeSource.CheckpointId, run.Snapshot.SourceCheckpointId, StringComparison.Ordinal));
        AddMismatch(
            mismatches,
            "sourceCheckpointHash",
            string.Equals(document.AuthoritativeSource.CheckpointHash, run.Snapshot.SourceCheckpointHash, StringComparison.OrdinalIgnoreCase));
        AddMismatch(mismatches, "source.tenantId", string.Equals(document.AuthoritativeSource.TenantId, run.Scope.TenantId, StringComparison.Ordinal));
        AddMismatch(mismatches, "source.organizationId", string.Equals(document.AuthoritativeSource.OrganizationId, run.Scope.OrganizationId, StringComparison.Ordinal));
        AddMismatch(mismatches, "source.companyId", string.Equals(document.AuthoritativeSource.CompanyId, run.Scope.CompanyId, StringComparison.Ordinal));
        AddMismatch(mismatches, "source.fundId", string.Equals(document.AuthoritativeSource.FundId, run.Scope.FundId, StringComparison.Ordinal));
        AddMismatch(mismatches, "source.ledgerBookId", string.Equals(document.AuthoritativeSource.LedgerBookId, run.Scope.BookId, StringComparison.Ordinal));
        AddMismatch(mismatches, "source.accountingPeriodId", string.Equals(document.AuthoritativeSource.AccountingPeriodId, run.Scope.PeriodId, StringComparison.Ordinal));
        AddMismatch(mismatches, "certifiedDatasetRowCount", document.CertifiedDatasetRowCount == document.AuthoritativeSource.LedgerLineCount);
        AddMismatch(
            mismatches,
            "snapshotHash",
            string.Equals(
                ComputeRetainedSnapshotHash(document),
                run.Snapshot.SnapshotHash,
                StringComparison.OrdinalIgnoreCase));
        if (mismatches.Count > 0)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reporting manifest '{document.RunId}' is not bound to the exact governed run, scope, access, parameters, or authoritative source. " +
                $"Mismatched bindings: {string.Join(", ", mismatches)}.");
        }

        var rebuiltReadiness = _certification.BuildGovernanceReadiness(
            run.RunId,
            new CertifiedReportingRunContext(
                document.Scope,
                document.Access,
                document.Snapshot,
                document.AuthoritativeSource,
                ImmutableArray<IReadOnlyDictionary<string, string>>.Empty,
                document.Readiness),
            document.Readiness);
        ReportingGovernanceCanonicalValidation.ValidateReadiness(rebuiltReadiness, run);
        // During pre-retention validation the governed run is still Draft/Running and therefore
        // cannot have a governance readiness receipt yet. Once validation has occurred (including
        // every release/read-back path), bind the retained document to that exact receipt.
        if (run.Readiness is not null
            && !string.Equals(
                rebuiltReadiness.ReceiptHash,
                run.Readiness.ReceiptHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reporting manifest '{document.RunId}' readiness evidence is not the exact governed receipt.");
        }
    }

    private static void AddMismatch(List<string> mismatches, string binding, bool matches)
    {
        if (!matches)
        {
            mismatches.Add(binding);
        }
    }

    private static bool AccessScopesEqual(ReportingAccessScope left, ReportingAccessScope right)
    {
        var leftPrincipals = left.Principals.IsDefault
            ? ImmutableArray<ReportingAccessPrincipalScope>.Empty
            : left.Principals;
        var rightPrincipals = right.Principals.IsDefault
            ? ImmutableArray<ReportingAccessPrincipalScope>.Empty
            : right.Principals;
        return string.Equals(left.PolicyId, right.PolicyId, StringComparison.Ordinal)
            && string.Equals(left.PolicyVersion, right.PolicyVersion, StringComparison.Ordinal)
            && left.Mode == right.Mode
            && string.Equals(left.OwnerPrincipalId, right.OwnerPrincipalId, StringComparison.Ordinal)
            && left.AllowOwnerAccess == right.AllowOwnerAccess
            && string.Equals(left.PolicyHash, right.PolicyHash, StringComparison.OrdinalIgnoreCase)
            && leftPrincipals.SequenceEqual(rightPrincipals);
    }

    private static bool RetainedArtifactRecordsEqual(
        ReportingRetainedArtifactRecord left,
        ReportingRetainedArtifactRecord right) =>
        string.Equals(left.PackageId, right.PackageId, StringComparison.Ordinal)
        && string.Equals(left.RunId, right.RunId, StringComparison.Ordinal)
        && string.Equals(left.SeriesId, right.SeriesId, StringComparison.Ordinal)
        && left.Revision == right.Revision
        && Equals(left.Scope, right.Scope)
        && AccessScopesEqual(left.Access, right.Access)
        && Equals(left.Snapshot, right.Snapshot)
        && string.Equals(left.ManifestId, right.ManifestId, StringComparison.Ordinal)
        && string.Equals(left.ManifestHash, right.ManifestHash, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ArtifactId, right.ArtifactId, StringComparison.Ordinal)
        && string.Equals(left.FileName, right.FileName, StringComparison.Ordinal)
        && string.Equals(left.ContentType, right.ContentType, StringComparison.Ordinal)
        && Equals(left.Identity, right.Identity)
        && left.ByteLength == right.ByteLength
        && left.StoredAtUtc == right.StoredAtUtc;

    private static string ComputeRetainedSnapshotHash(ReportingRetainedManifestDocument document) =>
        document.Snapshot.RequiresCertifiedLedgerPresentation
            ? ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                template = new
                {
                    document.ResolvedTemplate.Name,
                    document.ResolvedTemplate.Version
                },
                scope = document.Scope,
                access = document.Access,
                parametersHash = document.ParametersHash,
                sourceCheckpointId = document.AuthoritativeSource.CheckpointId,
                sourceCheckpointHash = document.AuthoritativeSource.CheckpointHash,
                reconciliationId = document.Snapshot.ReconciliationCheckpointId,
                reconciliationHash = document.Snapshot.ReconciliationCheckpointHash,
                readinessHash = document.Readiness.EvidenceHash,
                certifiedDatasetHash = document.CertifiedDatasetHashSha256,
                requiresCertifiedLedgerPresentation = true
            })))
            : ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                template = new
                {
                    document.ResolvedTemplate.Name,
                    document.ResolvedTemplate.Version
                },
                scope = document.Scope,
                access = document.Access,
                parametersHash = document.ParametersHash,
                sourceCheckpointId = document.AuthoritativeSource.CheckpointId,
                sourceCheckpointHash = document.AuthoritativeSource.CheckpointHash,
                reconciliationId = document.Snapshot.ReconciliationCheckpointId,
                reconciliationHash = document.Snapshot.ReconciliationCheckpointHash,
                readinessHash = document.Readiness.EvidenceHash,
                certifiedDatasetHash = document.CertifiedDatasetHashSha256
            })));

    private static void VerifyRetention(
        GovernedReportingRun run,
        ReportingGovernedArtifactProduction production,
        string manifestHash,
        ReportingArtifactRetentionReceipt retention)
    {
        ArgumentNullException.ThrowIfNull(retention);
        if (!string.Equals(retention.Package.PackageId, BuildPackageId(run), StringComparison.Ordinal)
            || retention.Package.Artifacts.Length != production.Artifacts.Length
            || retention.AuditEventIds.Length != production.Artifacts.Length
            || retention.AuditEventIds.Any(string.IsNullOrWhiteSpace)
            || retention.AuditEventIds.Distinct(StringComparer.Ordinal).Count() != retention.AuditEventIds.Length)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Artifact retention receipt for run '{run.RunId}' is incomplete or bound to another package.");
        }

        var retainedById = retention.Package.Artifacts.ToDictionary(
            static artifact => artifact.ArtifactId,
            StringComparer.Ordinal);
        foreach (var produced in production.Artifacts)
        {
            if (!retainedById.TryGetValue(produced.ArtifactId, out var retained)
                || !string.Equals(retained.RunId, run.RunId, StringComparison.Ordinal)
                || !string.Equals(retained.SeriesId, run.SeriesId, StringComparison.Ordinal)
                || retained.Revision != run.Revision
                || !Equals(retained.Scope, run.Scope)
                || !AccessScopesEqual(retained.Access, run.Access)
                || !Equals(retained.Snapshot, run.Snapshot)
                || !string.Equals(retained.ManifestId, production.ManifestArtifactId, StringComparison.Ordinal)
                || !string.Equals(retained.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(retained.FileName, produced.FileName, StringComparison.Ordinal)
                || !string.Equals(retained.ContentType, produced.ContentType, StringComparison.Ordinal)
                || retained.ByteLength != produced.Content.Length
                || !string.Equals(
                    retained.Identity.ContentHashSha256,
                    ComputeSha256(produced.Content.Span),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Retained artifact receipt '{run.RunId}/{produced.ArtifactId}' does not match the exact renderer bytes and immutable run scope.");
            }
        }

        if (!string.Equals(
            retainedById[production.ManifestArtifactId].Identity.ContentHashSha256,
            manifestHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained manifest '{production.ManifestArtifactId}' hash does not match its exact rendered bytes.");
        }
    }

    private static ImmutableArray<string> BuildReleaseEvidence(
        GovernedReportingRun run,
        ImmutableArray<ReportingSectionManifest> sections,
        ImmutableArray<string> artifactAuditEventIds)
    {
        var evidence = new HashSet<string>(StringComparer.Ordinal)
        {
            $"readiness:{run.Readiness!.ReceiptId}:{run.Readiness.ReceiptHash}",
            $"snapshot:{run.Snapshot.SnapshotId}:{run.Snapshot.SnapshotHash}",
            $"reconciliation:{run.Snapshot.ReconciliationCheckpointId}"
        };
        foreach (var id in run.Readiness.Checks.SelectMany(static check => check.EvidenceIds))
        {
            evidence.Add(id);
        }

        foreach (var id in artifactAuditEventIds)
        {
            evidence.Add($"artifact-audit:{id}");
        }

        foreach (var section in sections)
        {
            evidence.Add($"section:{section.SectionId}:{section.Hash}");
        }

        return evidence.OrderBy(static id => id, StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<ReportingRestatementChangedLine> BuildRestatementChangedLines(
        GovernedReportingRun predecessor,
        ReportingOutputManifest predecessorManifest,
        ReportingOutputManifest replacementManifest)
    {
        var before = IndexCertifiedRows(predecessorManifest.CertifiedDatasetRows);
        var after = IndexCertifiedRows(replacementManifest.CertifiedDatasetRows);
        var evidence = ImmutableArray.Create(
            $"snapshot:{predecessor.Snapshot.SnapshotId}:{predecessor.Snapshot.SnapshotHash}",
            $"snapshot:{replacementManifest.CertifiedSnapshot!.SnapshotId}:{replacementManifest.CertifiedSnapshot.SnapshotHash}",
            BuildSourceCheckpointEvidence(
                replacementManifest.AuthoritativeSource!.CheckpointId,
                replacementManifest.AuthoritativeSource.CheckpointHash));
        var changes = ImmutableArray.CreateBuilder<ReportingRestatementChangedLine>();
        foreach (var key in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal))
        {
            before.TryGetValue(key, out var previous);
            after.TryGetValue(key, out var current);
            if (string.Equals(previous, current, StringComparison.Ordinal))
            {
                continue;
            }
            changes.Add(new ReportingRestatementChangedLine(
                key,
                previous ?? "<not-present>",
                current ?? "<not-present>",
                evidence));
        }

        if (changes.Count == 0)
        {
            changes.Add(new ReportingRestatementChangedLine(
                "certification-evidence",
                predecessor.Snapshot.SnapshotHash,
                replacementManifest.CertifiedSnapshot!.SnapshotHash,
                evidence));
        }
        return changes.DrainToImmutable();
    }

    private static Dictionary<string, string> IndexCertifiedRows(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        if (rows.IsDefault)
        {
            throw new ReportingGovernanceException(
                "Restatement comparison requires immutable certified source rows on both revisions.");
        }

        var indexed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var canonical = SerializeCanonicalRow(row);
            var key = row.TryGetValue("entryId", out var entryId) && !string.IsNullOrWhiteSpace(entryId)
                ? $"ledger-line:{entryId.Trim()}"
                : $"ledger-row:{ComputeSha256(Encoding.UTF8.GetBytes(canonical))}";
            if (!indexed.TryAdd(key, canonical))
            {
                throw new ReportingGovernanceException(
                    $"Certified source contains duplicate immutable row identity '{key}'.");
            }
        }
        return indexed;
    }

    private static string SerializeCanonicalRow(IReadOnlyDictionary<string, string> row)
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var pair in row.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void ValidateRetentionAuthority(
        GovernedReportingRun run,
        ReportingAuthorityScope? releaseAuthority,
        ReportingAuthorityScope retentionAuthority)
    {
        ArgumentNullException.ThrowIfNull(retentionAuthority);
        if (retentionAuthority.Origin != ReportingCommandOrigin.ServicePrincipal
            || !retentionAuthority.HasPermission(ReportingGovernancePermission.ExecuteRun)
            || !string.Equals(retentionAuthority.TenantId, run.Scope.TenantId, StringComparison.Ordinal)
            || !string.Equals(retentionAuthority.OrganizationId, run.Scope.OrganizationId, StringComparison.Ordinal)
            || !SameOptional(retentionAuthority.CompanyId, run.Scope.CompanyId)
            || string.IsNullOrWhiteSpace(retentionAuthority.ActorId)
            || string.IsNullOrWhiteSpace(retentionAuthority.CorrelationId)
            || (releaseAuthority is not null
                && string.Equals(retentionAuthority.ActorId, releaseAuthority.ActorId, StringComparison.Ordinal)))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Artifact retention requires a distinct server service-principal authority bound to the governed run scope.");
        }
    }

    private static void ValidateRestatementCertificationInput(
        GovernedReportingRun predecessor,
        ReportingGovernanceCallerContext caller,
        ReportingRestatementCertificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Template);
        ArgumentNullException.ThrowIfNull(input.Readiness);
        ArgumentNullException.ThrowIfNull(input.AccessContext);
        if (!string.Equals(input.Template.TemplateId, predecessor.TemplateId, StringComparison.Ordinal)
            || ResolveTemplateMajorVersion(input.Template.Version) != int.Parse(predecessor.TemplateVersion, CultureInfo.InvariantCulture)
            || !string.Equals(input.Readiness.ResolvedTemplate.Name, predecessor.TemplateId, StringComparison.Ordinal)
            || input.Readiness.ResolvedTemplate.Version != int.Parse(predecessor.TemplateVersion, CultureInfo.InvariantCulture)
            || !string.Equals(input.AccessContext.ActorPrincipalId, caller.ActorId, StringComparison.Ordinal)
            || !string.Equals(input.AccessContext.TenantId, caller.TenantId, StringComparison.Ordinal)
            || !SameOptional(input.AccessContext.CompanyId, caller.CompanyId)
            || !input.AccessContext.RequireBoundScope)
        {
            throw new ReportingGovernanceException(
                "Restatement certification input is not server-bound to the predecessor template and authenticated caller scope.");
        }

        ValidateReadiness(input.Readiness, input.Readiness.ResolvedTemplate, input.Readiness.ResolvedParameters);
    }

    private static ReportingTemplateMetadata BindRestatementAccessPolicy(
        ReportingTemplateMetadata template,
        GovernedReportingRun predecessor)
    {
        var access = predecessor.Access;
        var principals = (access.Principals.IsDefault
                ? ImmutableArray<ReportingAccessPrincipalScope>.Empty
                : access.Principals)
            .Select(static principal => new ReportAccessPrincipalDto(
                principal.Kind switch
                {
                    ReportingAccessPrincipalKind.User => ReportAccessPrincipalKindDto.User,
                    ReportingAccessPrincipalKind.Group => ReportAccessPrincipalKindDto.Group,
                    ReportingAccessPrincipalKind.Company => ReportAccessPrincipalKindDto.Company,
                    _ => throw new ReportingGovernanceException(
                        $"Unsupported immutable reporting access principal kind '{principal.Kind}'.")
                },
                principal.PrincipalId))
            .ToArray();
        var mode = access.Mode switch
        {
            ReportingGovernanceAccessMode.Private => ReportAccessModeDto.Private,
            ReportingGovernanceAccessMode.Restricted => ReportAccessModeDto.Restricted,
            ReportingGovernanceAccessMode.CompanyWide => ReportAccessModeDto.CompanyWide,
            _ => throw new ReportingGovernanceException(
                $"Unsupported immutable reporting access mode '{access.Mode}'.")
        };

        return template with
        {
            AccessPolicy = new ReportAccessPolicyDto(
                mode,
                access.OwnerPrincipalId,
                principals,
                predecessor.Scope.CompanyId,
                access.AllowOwnerAccess)
        };
    }

    private static ReportingAuthorityScope ResolveAuthority(
        ReportingGovernanceCallerContext caller,
        ReportingOperationalScope scope)
    {
        ValidateCaller(caller);
        if (!string.Equals(caller.TenantId.Trim(), scope.TenantId, StringComparison.Ordinal)
            || !SameOptional(caller.CompanyId, scope.CompanyId))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Authenticated tenant and company do not match the immutable reporting scope.");
        }

        var permissions = ResolveGovernancePermissions(caller.Permissions);
        var principals = caller.PrincipalIds.IsDefault
            ? ImmutableArray<string>.Empty
            : caller.PrincipalIds
                .Where(static principal => !string.IsNullOrWhiteSpace(principal))
                .Select(static principal => principal.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static principal => principal, StringComparer.Ordinal)
                .ToImmutableArray();
        return new ReportingAuthorityScope(
            caller.ActorId.Trim(),
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            permissions,
            caller.Origin,
            caller.CorrelationId.Trim(),
            principals);
    }

    private static ImmutableArray<ReportingGovernancePermission> ResolveGovernancePermissions(
        UserPermission permissions)
    {
        if (permissions.HasFlag(UserPermission.AdminMaintenance))
        {
            return AllGovernancePermissions;
        }

        var resolved = ImmutableArray.CreateBuilder<ReportingGovernancePermission>();
        if (permissions.HasFlag(UserPermission.ManageReporting))
        {
            resolved.Add(ReportingGovernancePermission.CreateRun);
            resolved.Add(ReportingGovernancePermission.ExecuteRun);
            resolved.Add(ReportingGovernancePermission.ValidateRun);
            resolved.Add(ReportingGovernancePermission.SubmitRun);
            resolved.Add(ReportingGovernancePermission.RequestRestatement);
        }

        if (permissions.HasFlag(UserPermission.ApproveReporting))
        {
            resolved.Add(ReportingGovernancePermission.ApproveRun);
            resolved.Add(ReportingGovernancePermission.ApproveRestatement);
        }

        if (permissions.HasFlag(UserPermission.DeliverReporting))
        {
            resolved.Add(ReportingGovernancePermission.ReleaseRun);
        }

        return resolved.Distinct().ToImmutableArray();
    }

    private static bool CanRead(GovernedReportingRun run, ReportingGovernanceCallerContext caller)
    {
        if (!string.Equals(run.Scope.TenantId, caller.TenantId.Trim(), StringComparison.Ordinal)
            || !SameOptional(run.Scope.CompanyId, caller.CompanyId))
        {
            return false;
        }

        return run.Access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private =>
                (run.Access.AllowOwnerAccess
                    && string.Equals(run.Access.OwnerPrincipalId, caller.ActorId.Trim(), StringComparison.OrdinalIgnoreCase))
                || run.Access.Principals.Any(principal =>
                    principal.Kind == ReportingAccessPrincipalKind.User
                    && caller.Matches(principal)),
            ReportingGovernanceAccessMode.Restricted =>
                (run.Access.AllowOwnerAccess
                    && !string.IsNullOrWhiteSpace(run.Access.OwnerPrincipalId)
                    && string.Equals(run.Access.OwnerPrincipalId, caller.ActorId.Trim(), StringComparison.OrdinalIgnoreCase))
                || run.Access.Principals.Any(caller.Matches),
            _ => false
        };
    }

    private static void EnsureAuthorityCanAccess(
        GovernedReportingRun run,
        ReportingAuthorityScope authority) =>
        EnsureAuthorityCanAccess(run.Access, authority);

    private static void EnsureAuthorityCanAccess(
        ReportingAccessScope access,
        ReportingAuthorityScope authority)
    {
        var allowed = access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private =>
                (access.AllowOwnerAccess
                    && string.Equals(access.OwnerPrincipalId, authority.ActorId, StringComparison.OrdinalIgnoreCase))
                || access.Principals.Any(principal =>
                    principal.Kind == ReportingAccessPrincipalKind.User
                    && authority.Matches(principal)),
            ReportingGovernanceAccessMode.Restricted =>
                (access.AllowOwnerAccess
                    && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                    && string.Equals(access.OwnerPrincipalId, authority.ActorId, StringComparison.OrdinalIgnoreCase))
                || access.Principals.Any(authority.Matches),
            _ => false
        };
        if (!allowed)
        {
            throw new ReportingGovernanceAuthorizationException(
                "Authenticated caller is not included in the immutable reporting access scope.");
        }
    }

    private static void EnsureReadPermission(ReportingGovernanceCallerContext caller)
    {
        if ((caller.Permissions & ReportingReadPermissions) == 0)
        {
            throw new ReportingGovernanceAuthorizationException(
                "Authenticated caller lacks reporting read permission.");
        }
    }

    private static void EnsurePermission(
        ReportingAuthorityScope authority,
        ReportingGovernancePermission permission)
    {
        if (!authority.HasPermission(permission))
        {
            throw new ReportingGovernanceAuthorizationException(
                $"Authenticated caller lacks explicit '{permission}' permission.");
        }
    }

    private static void ValidateCaller(ReportingGovernanceCallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        RequireText(caller.ActorId, nameof(caller.ActorId));
        RequireText(caller.TenantId, nameof(caller.TenantId));
        RequireText(caller.CompanyId, nameof(caller.CompanyId));
        RequireText(caller.CorrelationId, nameof(caller.CorrelationId));
        if (!Enum.IsDefined(caller.Origin))
        {
            throw new ReportingGovernanceAuthorizationException("Reporting command origin is invalid.");
        }
    }

    private sealed record RestatementState(
        ReportingRestatementRequest Request,
        GovernedReportingRun Predecessor);

    private sealed record RetainedProduction(
        string ManifestHash,
        ReportingArtifactRetentionReceipt Retention);

    private sealed record VerifiedReleasePackage(
        string ManifestId,
        string ManifestHash,
        ImmutableArray<ReportingArtifactReference> Artifacts,
        ImmutableArray<string> AuditEventIds,
        ImmutableArray<ReportingSectionManifest> Sections);
}
