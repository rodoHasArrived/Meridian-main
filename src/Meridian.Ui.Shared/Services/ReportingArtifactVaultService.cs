using System.Collections.Immutable;
using System.Security.Cryptography;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Exact rendered bytes supplied by the reporting renderer. The vault clones these bytes before
/// hashing or awaiting so caller-owned buffers cannot change during retention.
/// </summary>
public sealed record ReportingRenderedArtifact(
    string ArtifactId,
    string FileName,
    string ContentType,
    ReadOnlyMemory<byte> Content);

/// <summary>
/// Immutable package metadata bound to the exact operational, access-policy, and certified-input
/// snapshots used to render it.
/// </summary>
public sealed record ReportingArtifactPackageRetentionRequest(
    string PackageId,
    string RunId,
    string SeriesId,
    int Revision,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot,
    string ManifestId,
    string ManifestHash,
    ImmutableArray<ReportingRenderedArtifact> Artifacts);

/// <summary>
/// Server-resolved access context. API callers must never be allowed to populate this record
/// directly; identity and scope adapters resolve it from the authenticated session.
/// </summary>
public sealed record ReportingArtifactAccessContext(
    string ActorId,
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    string? FundId,
    string BookId,
    string PeriodId,
    ImmutableArray<string> PrincipalIds,
    string CorrelationId,
    ReportingAccessPrincipalScope? DelegatedPrincipal = null);

public sealed record ReportingArtifactRetentionReceipt(
    ReportingRetainedArtifactPackage Package,
    bool CatalogAlreadyExisted,
    ImmutableArray<string> AuditEventIds);

public sealed record ReportingArtifactDownload(
    ReportingRetainedArtifactRecord Artifact,
    byte[] Content,
    DateTimeOffset AccessedAtUtc,
    string AuditEventId);

public sealed class ReportingArtifactVaultAccessDeniedException : UnauthorizedAccessException
{
    public ReportingArtifactVaultAccessDeniedException(string message) : base(message)
    {
    }
}

/// <summary>
/// Connects generated package bytes to tenant-scoped immutable blob storage and authoritative
/// scope metadata. Every successful read is integrity checked and durably audited before bytes are
/// returned; audit or integrity failures therefore fail closed.
/// </summary>
public sealed class ReportingArtifactVaultService
{
    private readonly IReportingArtifactStore _artifactStore;
    private readonly IReportingArtifactCatalog _catalog;
    private readonly IReportingArtifactAuditStore _auditStore;
    private readonly TimeProvider _timeProvider;

    public ReportingArtifactVaultService(
        IReportingArtifactStore artifactStore,
        IReportingArtifactCatalog catalog,
        IReportingArtifactAuditStore auditStore,
        TimeProvider? timeProvider = null)
    {
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReportingArtifactRetentionReceipt> RetainPackageAsync(
        ReportingArtifactPackageRetentionRequest request,
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authority);
        if (!request.Artifacts.IsDefault)
        {
            request = request with
            {
                Artifacts = request.Artifacts
                    .Select(static artifact => artifact with { Content = artifact.Content.ToArray() })
                    .ToImmutableArray()
            };
        }
        ValidateRetentionRequest(request);
        ValidateRetentionAuthority(request.Scope, authority);

        var retained = ImmutableArray.CreateBuilder<ReportingRetainedArtifactRecord>(request.Artifacts.Length);
        var writeResults = new List<ReportingArtifactWriteResult>(request.Artifacts.Length);

        foreach (var artifact in request.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = artifact.Content.ToArray();
            var expectedHash = ComputeSha256(content);
            var expectedIdentity = new ReportingArtifactIdentity(request.Scope.TenantId, expectedHash);
            var write = await _artifactStore
                .StoreAsync(new ReportingArtifactWriteRequest(request.Scope.TenantId, content), cancellationToken)
                .ConfigureAwait(false);

            EnsureWriteMatchesExactBytes(expectedIdentity, content.LongLength, write);
            writeResults.Add(write);
            var retainedRecord = new ReportingRetainedArtifactRecord(
                request.PackageId,
                request.RunId,
                request.SeriesId,
                request.Revision,
                request.Scope,
                request.Access,
                request.Snapshot,
                request.ManifestId,
                request.ManifestHash.ToLowerInvariant(),
                artifact.ArtifactId,
                artifact.FileName,
                artifact.ContentType,
                write.Identity,
                write.ByteSize,
                write.StoredAtUtc);
            var readBack = await _artifactStore
                .ReadAsync(write.Identity, cancellationToken)
                .ConfigureAwait(false);
            VerifyRead(retainedRecord, readBack);
            if (!readBack.Content.AsSpan().SequenceEqual(content))
            {
                throw new ReportingArtifactIntegrityException(
                    write.Identity,
                    $"artifact '{artifact.ArtifactId}' did not read back as the exact renderer bytes");
            }
            retained.Add(retainedRecord);
        }

        var package = new ReportingRetainedArtifactPackage(request.PackageId, retained.MoveToImmutable());
        var catalogWrite = await _catalog.AddPackageAsync(package, cancellationToken).ConfigureAwait(false);
        var persistedPackage = await _catalog
            .GetPackageAsync(request.Scope.TenantId, request.PackageId, cancellationToken)
            .ConfigureAwait(false);
        if (persistedPackage is null || !PackagesEqual(package, persistedPackage))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Artifact catalog did not read back the exact retained package '{request.PackageId}'.");
        }
        foreach (var expected in package.Artifacts)
        {
            var persisted = await _catalog
                .GetArtifactAsync(
                    request.Scope.TenantId,
                    request.PackageId,
                    expected.ArtifactId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (persisted is null || !ArtifactRecordsEqual(expected, persisted))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Artifact catalog did not read back exact metadata for '{request.PackageId}/{expected.ArtifactId}'.");
            }
        }

        var auditEventIds = ImmutableArray.CreateBuilder<string>(persistedPackage.Artifacts.Length);

        for (var index = 0; index < persistedPackage.Artifacts.Length; index++)
        {
            var record = persistedPackage.Artifacts[index];
            var action = catalogWrite.AlreadyExisted || writeResults[index].AlreadyExisted
                ? ReportingArtifactAuditAction.RetentionVerified
                : ReportingArtifactAuditAction.ArtifactRetained;
            var receipt = await AppendAndVerifyAuditAsync(
                new ReportingArtifactAuditEvent(
                    Guid.NewGuid().ToString("N"),
                    _timeProvider.GetUtcNow(),
                    action,
                    authority.ActorId,
                    authority.TenantId,
                    record.Scope.TenantId,
                    record.PackageId,
                    record.ArtifactId,
                    record.Identity.ContentHashSha256,
                    authority.CorrelationId,
                    Reason: null),
                cancellationToken).ConfigureAwait(false);
            auditEventIds.Add(receipt.EventId);
        }

        return new ReportingArtifactRetentionReceipt(
            persistedPackage,
            catalogWrite.AlreadyExisted,
            auditEventIds.MoveToImmutable());
    }

    public async Task<ReportingRetainedArtifactPackage> GetPackageForReleaseAsync(
        string packageId,
        ReportingArtifactAccessContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(context);
        ValidateAccessContext(context);
        var normalizedPackageId = packageId.Trim();
        var package = await _catalog
            .GetPackageAsync(context.TenantId, normalizedPackageId, cancellationToken)
            .ConfigureAwait(false);
        if (package is null
            || !string.Equals(package.PackageId, normalizedPackageId, StringComparison.Ordinal)
            || package.Artifacts.IsDefaultOrEmpty
            || package.Artifacts.Any(static artifact => artifact is null)
            || package.Artifacts.Select(static artifact => artifact.ArtifactId)
                .Distinct(StringComparer.Ordinal).Count() != package.Artifacts.Length)
        {
            throw new ReportingArtifactVaultAccessDeniedException(
                "Artifact package does not exist or is not accessible.");
        }

        foreach (var record in package.Artifacts)
        {
            ValidateCatalogRecord(record);
            if (!string.Equals(record.PackageId, package.PackageId, StringComparison.Ordinal))
            {
                throw new ReportingArtifactCatalogIntegrityException(
                    $"Retained artifact package '{package.PackageId}' contains an artifact bound to another package.");
            }
            if (ResolveAccessDenial(record, context) is not null)
            {
                throw new ReportingArtifactVaultAccessDeniedException(
                    "Artifact package does not exist or is not accessible.");
            }
        }

        return package;
    }

    public async Task<ReportingArtifactDownload> ReadForDownloadAsync(
        string packageId,
        string artifactId,
        ReportingArtifactAccessContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentNullException.ThrowIfNull(context);
        ValidateAccessContext(context);

        var normalizedPackageId = packageId.Trim();
        var normalizedArtifactId = artifactId.Trim();
        var record = await _catalog
            .GetArtifactAsync(context.TenantId, normalizedPackageId, normalizedArtifactId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            await AuditDeniedAsync(
                normalizedPackageId,
                normalizedArtifactId,
                context,
                targetTenantId: context.TenantId,
                contentHash: null,
                "Artifact does not exist or is not accessible.",
                cancellationToken).ConfigureAwait(false);
            throw new ReportingArtifactVaultAccessDeniedException("Artifact does not exist or is not accessible.");
        }

        try
        {
            ValidateCatalogRecord(record);
        }
        catch (ReportingArtifactCatalogIntegrityException ex)
        {
            await AuditIntegrityFailureAsync(record, context, ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }

        var denialReason = ResolveAccessDenial(record, context);
        if (denialReason is not null)
        {
            await AuditDeniedAsync(
                record.PackageId,
                record.ArtifactId,
                context,
                record.Scope.TenantId,
                record.Identity.ContentHashSha256,
                denialReason,
                cancellationToken).ConfigureAwait(false);
            throw new ReportingArtifactVaultAccessDeniedException("Artifact does not exist or is not accessible.");
        }

        ReportingArtifactReadResult read;
        try
        {
            read = await _artifactStore.ReadAsync(record.Identity, cancellationToken).ConfigureAwait(false);
            VerifyRead(record, read);
        }
        catch (Exception ex) when (ex is ReportingArtifactIntegrityException or ReportingArtifactNotFoundException)
        {
            await AuditIntegrityFailureAsync(record, context, ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }

        var accessedAtUtc = _timeProvider.GetUtcNow();
        var auditReceipt = await AppendAndVerifyAuditAsync(
            new ReportingArtifactAuditEvent(
                Guid.NewGuid().ToString("N"),
                accessedAtUtc,
                ReportingArtifactAuditAction.ContentAccessed,
                context.ActorId,
                context.TenantId,
                record.Scope.TenantId,
                record.PackageId,
                record.ArtifactId,
                record.Identity.ContentHashSha256,
                context.CorrelationId,
                Reason: null),
            cancellationToken).ConfigureAwait(false);

        return new ReportingArtifactDownload(record, read.Content, accessedAtUtc, auditReceipt.EventId);
    }

    private async ValueTask AuditDeniedAsync(
        string packageId,
        string artifactId,
        ReportingArtifactAccessContext context,
        string targetTenantId,
        string? contentHash,
        string reason,
        CancellationToken cancellationToken) =>
        await AppendAndVerifyAuditAsync(
            new ReportingArtifactAuditEvent(
                Guid.NewGuid().ToString("N"),
                _timeProvider.GetUtcNow(),
                ReportingArtifactAuditAction.AccessDenied,
                context.ActorId,
                context.TenantId,
                targetTenantId,
                packageId,
                artifactId,
                contentHash,
                context.CorrelationId,
                reason),
            cancellationToken).ConfigureAwait(false);

    private async ValueTask AuditIntegrityFailureAsync(
        ReportingRetainedArtifactRecord record,
        ReportingArtifactAccessContext context,
        string reason,
        CancellationToken cancellationToken) =>
        await AppendAndVerifyAuditAsync(
            new ReportingArtifactAuditEvent(
                Guid.NewGuid().ToString("N"),
                _timeProvider.GetUtcNow(),
                ReportingArtifactAuditAction.IntegrityFailure,
                context.ActorId,
                context.TenantId,
                record.Scope.TenantId,
                record.PackageId,
                record.ArtifactId,
                record.Identity.ContentHashSha256,
                context.CorrelationId,
                reason),
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<ReportingArtifactAuditReceipt> AppendAndVerifyAuditAsync(
        ReportingArtifactAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var receipt = await _auditStore.AppendAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        if (receipt is null
            || !string.Equals(receipt.EventId, auditEvent.EventId, StringComparison.Ordinal)
            || receipt.Sequence <= 0
            || !Sha256Digest.IsWellFormed(receipt.Hash)
            || (receipt.PreviousHash is not null && !Sha256Digest.IsWellFormed(receipt.PreviousHash)))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Artifact audit store returned an invalid receipt for event '{auditEvent.EventId}'.");
        }

        return receipt;
    }

    private static void ValidateRetentionRequest(ReportingArtifactPackageRetentionRequest request)
    {
        RequireValue(request.PackageId, nameof(request.PackageId));
        RequireValue(request.RunId, nameof(request.RunId));
        RequireValue(request.SeriesId, nameof(request.SeriesId));
        RequireValue(request.ManifestId, nameof(request.ManifestId));
        if (request.Revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Revision), "Reporting package revisions must be positive.");
        }

        ValidateOperationalScope(request.Scope);
        ValidateAccessScope(request.Access);
        if (request.Access.Mode == ReportingGovernanceAccessMode.CompanyWide
            && string.IsNullOrWhiteSpace(request.Scope.CompanyId))
        {
            throw new ArgumentException(
                "Company-wide reporting access requires an immutable company scope.",
                nameof(request));
        }
        ValidateSnapshotScope(request.Scope, request.Snapshot);
        RequireSha256(request.ManifestHash, nameof(request.ManifestHash));
        if (request.Artifacts.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A reporting package must contain at least one rendered artifact.", nameof(request));
        }

        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in request.Artifacts)
        {
            RequireValue(artifact.ArtifactId, nameof(artifact.ArtifactId));
            RequireValue(artifact.FileName, nameof(artifact.FileName));
            RequireValue(artifact.ContentType, nameof(artifact.ContentType));
            if (artifact.Content.IsEmpty)
            {
                throw new ArgumentException($"Rendered artifact '{artifact.ArtifactId}' is empty.", nameof(request));
            }

            if (!artifactIds.Add(artifact.ArtifactId))
            {
                throw new ArgumentException($"Rendered artifact id '{artifact.ArtifactId}' is duplicated.", nameof(request));
            }
        }

        var manifestArtifacts = request.Artifacts
            .Where(artifact => string.Equals(
                artifact.ArtifactId,
                request.ManifestId,
                StringComparison.Ordinal))
            .ToArray();
        if (manifestArtifacts.Length != 1)
        {
            throw new ArgumentException(
                $"A reporting package must contain exactly one rendered manifest artifact '{request.ManifestId}'.",
                nameof(request));
        }

        var renderedManifestHash = ComputeSha256(manifestArtifacts[0].Content.Span);
        if (!string.Equals(renderedManifestHash, request.ManifestHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Rendered manifest artifact '{request.ManifestId}' does not match the declared manifest hash.",
                nameof(request));
        }
    }

    private static void ValidateRetentionAuthority(
        ReportingOperationalScope scope,
        ReportingAuthorityScope authority)
    {
        RequireValue(authority.ActorId, nameof(authority.ActorId));
        RequireValue(authority.CorrelationId, nameof(authority.CorrelationId));
        if (!authority.HasPermission(ReportingGovernancePermission.ExecuteRun))
        {
            throw new ReportingArtifactVaultAccessDeniedException(
                "Retaining rendered artifacts requires server-resolved ExecuteRun authority.");
        }

        if (!Same(authority.TenantId, scope.TenantId)
            || !Same(authority.OrganizationId, scope.OrganizationId)
            || !SameOptional(authority.CompanyId, scope.CompanyId))
        {
            throw new ReportingArtifactVaultAccessDeniedException(
                "Artifact retention authority is not bound to the run's tenant and organizational scope.");
        }
    }

    private static void ValidateCatalogRecord(ReportingRetainedArtifactRecord record)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(record.Identity);
            RequireValue(record.PackageId, nameof(record.PackageId));
            RequireValue(record.ArtifactId, nameof(record.ArtifactId));
            RequireValue(record.Identity.TenantId, nameof(record.Identity.TenantId));
            RequireSha256(record.Identity.ContentHashSha256, nameof(record.Identity.ContentHashSha256));
            ValidateOperationalScope(record.Scope);
            ValidateAccessScope(record.Access);
            if (record.Access.Mode == ReportingGovernanceAccessMode.CompanyWide
                && string.IsNullOrWhiteSpace(record.Scope.CompanyId))
            {
                throw new ArgumentException(
                    "Company-wide reporting access has no immutable company scope.",
                    nameof(record));
            }
            ValidateSnapshotScope(record.Scope, record.Snapshot);
            RequireSha256(record.ManifestHash, nameof(record.ManifestHash));
        }
        catch (ArgumentException ex)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Artifact catalog metadata for '{record.PackageId}/{record.ArtifactId}' is invalid: {ex.Message}");
        }

        if (!Same(record.Identity.TenantId, record.Scope.TenantId))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Artifact '{record.PackageId}/{record.ArtifactId}' is bound to a different tenant than its content address.");
        }

        if (record.ByteLength <= 0)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Artifact '{record.PackageId}/{record.ArtifactId}' has an invalid retained byte length.");
        }
    }

    private static string? ResolveAccessDenial(
        ReportingRetainedArtifactRecord record,
        ReportingArtifactAccessContext context)
    {
        var scope = record.Scope;
        if (!Same(context.TenantId, scope.TenantId))
        {
            return "Authenticated tenant does not match retained artifact tenant.";
        }

        if (!Same(context.OrganizationId, scope.OrganizationId)
            || !SameOptional(context.CompanyId, scope.CompanyId)
            || !SameOptional(context.FundId, scope.FundId)
            || !Same(context.BookId, scope.BookId)
            || !Same(context.PeriodId, scope.PeriodId))
        {
            return "Authenticated operational scope does not match retained artifact scope.";
        }

        return record.Access.Mode switch
        {
            ReportingGovernanceAccessMode.Private
                when !HasPrivatePrincipal(record.Access, context) =>
                "Authenticated principal does not own the private artifact.",
            ReportingGovernanceAccessMode.Restricted
                when !HasRestrictedPrincipal(record.Access, context) =>
                "Authenticated principal is not included in the retained access-policy snapshot.",
            ReportingGovernanceAccessMode.CompanyWide when string.IsNullOrWhiteSpace(scope.CompanyId) =>
                "Company-wide artifact has no immutable company binding.",
            _ => null
        };
    }

    private static bool HasPrivatePrincipal(
        ReportingAccessScope access,
        ReportingArtifactAccessContext context)
    {
        if (access.AllowOwnerAccess
            && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
            && (SamePrincipal(access.OwnerPrincipalId, context.ActorId)
                || context.DelegatedPrincipal is
                { Kind: ReportingAccessPrincipalKind.User } delegatedOwner
                && SamePrincipal(access.OwnerPrincipalId, delegatedOwner.PrincipalId)))
        {
            return true;
        }

        return !access.Principals.IsDefaultOrEmpty
               && access.Principals.Any(principal => ContextMatches(principal, context));
    }

    private static bool HasRestrictedPrincipal(
        ReportingAccessScope access,
        ReportingArtifactAccessContext context) =>
        HasPrivatePrincipal(access, context);

    private static bool PackagesEqual(
        ReportingRetainedArtifactPackage expected,
        ReportingRetainedArtifactPackage actual)
    {
        if (!string.Equals(expected.PackageId, actual.PackageId, StringComparison.Ordinal)
            || expected.Artifacts.Length != actual.Artifacts.Length)
        {
            return false;
        }

        var actualById = actual.Artifacts.ToDictionary(
            static artifact => artifact.ArtifactId,
            StringComparer.Ordinal);
        return expected.Artifacts.All(expectedArtifact =>
            actualById.TryGetValue(expectedArtifact.ArtifactId, out var actualArtifact)
            && ArtifactRecordsEqual(expectedArtifact, actualArtifact));
    }

    private static bool ArtifactRecordsEqual(
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

    private static bool AccessScopesEqual(ReportingAccessScope left, ReportingAccessScope right)
    {
        var leftPrincipals = left.Principals.IsDefault
            ? ImmutableArray<ReportingAccessPrincipalScope>.Empty
            : left.Principals
                .OrderBy(static principal => principal.Kind)
                .ThenBy(static principal => principal.PrincipalId, StringComparer.Ordinal)
                .ToImmutableArray();
        var rightPrincipals = right.Principals.IsDefault
            ? ImmutableArray<ReportingAccessPrincipalScope>.Empty
            : right.Principals
                .OrderBy(static principal => principal.Kind)
                .ThenBy(static principal => principal.PrincipalId, StringComparer.Ordinal)
                .ToImmutableArray();
        return string.Equals(left.PolicyId, right.PolicyId, StringComparison.Ordinal)
            && string.Equals(left.PolicyVersion, right.PolicyVersion, StringComparison.Ordinal)
            && left.Mode == right.Mode
            && string.Equals(left.OwnerPrincipalId, right.OwnerPrincipalId, StringComparison.Ordinal)
            && left.AllowOwnerAccess == right.AllowOwnerAccess
            && string.Equals(left.PolicyHash, right.PolicyHash, StringComparison.OrdinalIgnoreCase)
            && leftPrincipals.SequenceEqual(rightPrincipals);
    }

    private static bool ContextMatches(
        ReportingAccessPrincipalScope principal,
        ReportingArtifactAccessContext context)
    {
        if (context.DelegatedPrincipal is { } delegated
            && delegated.Kind == principal.Kind
            && SamePrincipal(delegated.PrincipalId, principal.PrincipalId))
        {
            return true;
        }

        return principal.Kind switch
        {
            ReportingAccessPrincipalKind.User => SamePrincipal(context.ActorId, principal.PrincipalId),
            ReportingAccessPrincipalKind.Group =>
                !context.PrincipalIds.IsDefaultOrEmpty
                && context.PrincipalIds.Contains(principal.PrincipalId, StringComparer.OrdinalIgnoreCase),
            ReportingAccessPrincipalKind.Company => SamePrincipal(context.CompanyId, principal.PrincipalId),
            _ => false
        };
    }

    private static void VerifyRead(
        ReportingRetainedArtifactRecord record,
        ReportingArtifactReadResult read)
    {
        if (!Same(read.Identity.TenantId, record.Identity.TenantId)
            || !string.Equals(
                read.Identity.ContentHashSha256,
                record.Identity.ContentHashSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactIntegrityException(
                record.Identity,
                "artifact store returned a different content address");
        }

        if (read.ByteSize != record.ByteLength || read.Content.LongLength != record.ByteLength)
        {
            throw new ReportingArtifactIntegrityException(
                record.Identity,
                $"catalog size {record.ByteLength} does not match retrieved size {read.Content.LongLength}");
        }

        var actualHash = ComputeSha256(read.Content);
        if (!string.Equals(actualHash, record.Identity.ContentHashSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactIntegrityException(
                record.Identity,
                $"retrieved SHA-256 {actualHash} does not match immutable catalog identity");
        }
    }

    private static void EnsureWriteMatchesExactBytes(
        ReportingArtifactIdentity expectedIdentity,
        long expectedByteLength,
        ReportingArtifactWriteResult write)
    {
        if (!Same(write.Identity.TenantId, expectedIdentity.TenantId)
            || !string.Equals(
                write.Identity.ContentHashSha256,
                expectedIdentity.ContentHashSha256,
                StringComparison.OrdinalIgnoreCase)
            || write.ByteSize != expectedByteLength)
        {
            throw new ReportingArtifactIntegrityException(
                expectedIdentity,
                "artifact store receipt does not match the exact rendered bytes supplied for retention");
        }
    }

    private static void ValidateAccessContext(ReportingArtifactAccessContext context)
    {
        RequireValue(context.ActorId, nameof(context.ActorId));
        RequireValue(context.TenantId, nameof(context.TenantId));
        RequireValue(context.OrganizationId, nameof(context.OrganizationId));
        RequireValue(context.BookId, nameof(context.BookId));
        RequireValue(context.PeriodId, nameof(context.PeriodId));
        RequireValue(context.CorrelationId, nameof(context.CorrelationId));
    }

    private static void ValidateOperationalScope(ReportingOperationalScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        RequireValue(scope.TenantId, nameof(scope.TenantId));
        RequireValue(scope.OrganizationId, nameof(scope.OrganizationId));
        RequireValue(scope.BookId, nameof(scope.BookId));
        RequireValue(scope.PeriodId, nameof(scope.PeriodId));
    }

    private static void ValidateAccessScope(ReportingAccessScope access)
    {
        ArgumentNullException.ThrowIfNull(access);
        RequireValue(access.PolicyId, nameof(access.PolicyId));
        RequireValue(access.PolicyVersion, nameof(access.PolicyVersion));
        RequireSha256(access.PolicyHash, nameof(access.PolicyHash));
        if (!Enum.IsDefined(access.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(access), "Reporting access mode is invalid.");
        }

        if (access.Mode == ReportingGovernanceAccessMode.Private
            && (!access.AllowOwnerAccess || string.IsNullOrWhiteSpace(access.OwnerPrincipalId))
            && access.Principals.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Private reporting access requires enabled owner access or a named user principal.",
                nameof(access));
        }

        if (access.Mode == ReportingGovernanceAccessMode.Restricted
            && (!access.AllowOwnerAccess || string.IsNullOrWhiteSpace(access.OwnerPrincipalId))
            && access.Principals.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Restricted reporting access requires an enabled owner or at least one typed principal.",
                nameof(access));
        }

        if (access.Mode == ReportingGovernanceAccessMode.Private
            && !access.Principals.IsDefaultOrEmpty
            && access.Principals.Any(static principal =>
                principal.Kind != ReportingAccessPrincipalKind.User))
        {
            throw new ArgumentException(
                "Private reporting access can retain only named user principals.",
                nameof(access));
        }

        if (!access.Principals.IsDefaultOrEmpty
            && (access.Principals.Any(static principal =>
                    principal is null
                    || !Enum.IsDefined(principal.Kind)
                    || string.IsNullOrWhiteSpace(principal.PrincipalId))
                || access.Principals.Any(principal => access.Principals.Count(candidate =>
                    candidate.Kind == principal.Kind
                    && Same(candidate.PrincipalId, principal.PrincipalId)) > 1)))
        {
            throw new ArgumentException(
                "Reporting access principals require a valid kind, identity, and unique kind/identity pair.",
                nameof(access));
        }
    }

    private static void ValidateSnapshotScope(
        ReportingOperationalScope scope,
        ReportingCertifiedSnapshotScope snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireValue(snapshot.SnapshotId, nameof(snapshot.SnapshotId));
        RequireSha256(snapshot.SnapshotHash, nameof(snapshot.SnapshotHash));
        RequireValue(snapshot.ReconciliationCheckpointId, nameof(snapshot.ReconciliationCheckpointId));
        if (!Same(snapshot.TenantId, scope.TenantId)
            || !Same(snapshot.OrganizationId, scope.OrganizationId)
            || !SameOptional(snapshot.CompanyId, scope.CompanyId)
            || !SameOptional(snapshot.FundId, scope.FundId)
            || !Same(snapshot.BookId, scope.BookId)
            || !Same(snapshot.PeriodId, scope.PeriodId))
        {
            throw new ArgumentException(
                "Certified snapshot scope does not exactly match the reporting run scope.",
                nameof(snapshot));
        }
    }

    private static void RequireValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static void RequireSha256(string? value, string parameterName)
    {
        if (!Sha256Digest.IsWellFormed(value))
        {
            throw new ArgumentException(
                $"{parameterName} must contain exactly 64 hexadecimal SHA-256 characters.",
                parameterName);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private static bool SamePrincipal(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameOptional(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)
        || Same(left, right);

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
