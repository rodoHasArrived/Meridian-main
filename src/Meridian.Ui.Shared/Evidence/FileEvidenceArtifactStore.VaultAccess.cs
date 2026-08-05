using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    private const int DocumentVerificationOverscan = 8;
    private const int MinDocumentLocatorInspections = 64;
    private const int MaxDocumentLocatorInspections = 512;
    private const int MaxDocumentVerificationCandidates = 512;
    private const long MaxDocumentVerificationBytesPerRequest = 256L * 1024 * 1024;

    [Obsolete("Use TryOpenManifestAsync(..., tenantId, scope, ct); Evidence Vault reads require authenticated scope.")]
    public Task<EvidenceManifestFile?> TryOpenManifestAsync(
        string subjectKind,
        string subjectId,
        string fileName,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    public async Task<EvidenceManifestFile?> TryOpenManifestAsync(
        string subjectKind,
        string subjectId,
        string fileName,
        string tenantId,
        string scope,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var filePath = ResolveSubjectManifestPath(subjectKind, subjectId, fileName);
        if (filePath is null)
        {
            return null;
        }

        var identity = await FindIdentityForManifestPathAsync(
                filePath,
                tenantId,
                scope,
                ct)
            .ConfigureAwait(false);
        return identity is not null
               && await ManifestMatchesScopeAsync(filePath, identity, tenantId, scope, ct).ConfigureAwait(false)
            ? OpenManifestFile(filePath)
            : null;
    }

    [Obsolete("Use TryOpenManifestByVaultIdAsync(vaultId, tenantId, scope, ct); Evidence Vault reads require authenticated scope.")]
    public Task<EvidenceManifestFile?> TryOpenManifestByVaultIdAsync(
        string vaultId,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    public async Task<EvidenceManifestFile?> TryOpenManifestByVaultIdAsync(
        string vaultId,
        string tenantId,
        string scope,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var identity = await TryGetVaultIdentityAsync(vaultId, tenantId, scope, ct).ConfigureAwait(false);
        var manifestPath = identity is null || !MatchesIdentityScope(identity, tenantId, scope)
            ? null
            : ResolveVaultManifestPath(identity, identity.VaultId);
        if (manifestPath is null
            || !await ManifestMatchesScopeAsync(
                    manifestPath,
                    identity!,
                    tenantId,
                    scope,
                    ct)
                .ConfigureAwait(false))
        {
            return null;
        }

        return OpenManifestFile(manifestPath);
    }

    [Obsolete("Use TryGetVaultIdentityAsync(vaultId, tenantId, scope, ct); Evidence Vault reads require authenticated scope.")]
    public Task<EvidenceVaultIdentityDto?> TryGetVaultIdentityAsync(
        string vaultId,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    public async Task<EvidenceVaultIdentityDto?> TryGetVaultIdentityAsync(
        string vaultId,
        string tenantId,
        string scope,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!HasRequiredScope(tenantId, scope))
        {
            return null;
        }

        var safeVaultId = ValidateVaultId(vaultId);
        if (safeVaultId is null)
        {
            return null;
        }

        var indexPath = Path.Combine(_rootDirectory, "_vault", $"{safeVaultId}.json");
        var identity = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
        if (identity is null || !MatchesIdentityScope(identity, tenantId, scope))
        {
            return null;
        }

        var manifestPath = ResolveVaultManifestPath(identity, safeVaultId);
        var manifest = manifestPath is null
            ? null
            : await TryReadRetainedManifestAsync(manifestPath, ct).ConfigureAwait(false);
        return manifest is not null
               && TryResolveManifestAuthority(
                   manifest,
                   identity,
                   tenantId,
                   scope,
                   out var manifestIdentity)
            ? manifestIdentity
            : null;
    }

    private async Task<EvidenceVaultIdentityDto?> FindIdentityForManifestPathAsync(
        string manifestPath,
        string tenantId,
        string scope,
        CancellationToken ct)
    {
        var vaultDirectory = Path.Combine(_rootDirectory, "_vault");
        if (!Directory.Exists(vaultDirectory))
        {
            return null;
        }

        var expectedPath = Path.GetFullPath(manifestPath);
        foreach (var indexPath in Directory.EnumerateFiles(vaultDirectory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var identity = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
            if (identity is null || !MatchesIdentityScope(identity, tenantId, scope))
            {
                continue;
            }

            var indexedManifestPath = ResolveVaultManifestPath(identity, identity.VaultId);
            if (indexedManifestPath is not null
                && string.Equals(
                    Path.GetFullPath(indexedManifestPath),
                    expectedPath,
                    PathComparison))
            {
                return identity;
            }
        }

        return null;
    }

    private async Task<bool> ManifestMatchesScopeAsync(
        string manifestPath,
        EvidenceVaultIdentityDto identity,
        string tenantId,
        string scope,
        CancellationToken ct)
    {
        var manifest = await TryReadRetainedManifestAsync(manifestPath, ct).ConfigureAwait(false);
        return manifest is not null
               && TryResolveManifestAuthority(
                   manifest,
                   identity,
                   tenantId,
                   scope,
                   out _);
    }

    private static bool MatchesManifestScope(
        RetainedEvidenceManifestDto manifest,
        EvidenceVaultIdentityDto identity,
        string tenantId,
        string scope)
        => HasRequiredScope(manifest.TenantId, manifest.Scope)
           && string.Equals(manifest.TenantId!.Trim(), tenantId.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(manifest.Scope!.Trim(), scope.Trim(), StringComparison.OrdinalIgnoreCase)
           && manifest.VaultIdentity is { } embeddedIdentity
           && string.Equals(embeddedIdentity.VaultId, identity.VaultId, StringComparison.OrdinalIgnoreCase)
           && MatchesIdentityScope(embeddedIdentity, tenantId, scope);

    private bool TryResolveManifestAuthority(
        RetainedEvidenceManifestDto manifest,
        EvidenceVaultIdentityDto locator,
        string tenantId,
        string scope,
        out EvidenceVaultIdentityDto? manifestIdentity)
    {
        manifestIdentity = manifest.VaultIdentity;
        if (manifestIdentity is null
            || !MatchesManifestScope(manifest, locator, tenantId, scope)
            || !ManifestLocatorMatches(locator, manifestIdentity)
            || !HasValidManifestContentHash(manifest, manifestIdentity))
        {
            manifestIdentity = null;
            return false;
        }

        return true;
    }

    private static bool ManifestLocatorMatches(
        EvidenceVaultIdentityDto locator,
        EvidenceVaultIdentityDto manifestIdentity)
        => string.Equals(locator.VaultId, manifestIdentity.VaultId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(locator.SubjectKind, manifestIdentity.SubjectKind, StringComparison.OrdinalIgnoreCase)
           && string.Equals(locator.SubjectId, manifestIdentity.SubjectId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(locator.ManifestPath, manifestIdentity.ManifestPath, StringComparison.Ordinal)
           && string.Equals(locator.ManifestRoute, manifestIdentity.ManifestRoute, StringComparison.Ordinal)
           && locator.RetainedAt == manifestIdentity.RetainedAt
           && locator.SchemaVersion == manifestIdentity.SchemaVersion
           && string.Equals(locator.StorageKind, manifestIdentity.StorageKind, StringComparison.OrdinalIgnoreCase)
           && string.Equals(locator.TenantId, manifestIdentity.TenantId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(locator.Scope, manifestIdentity.Scope, StringComparison.OrdinalIgnoreCase);

    private bool HasValidManifestContentHash(
        RetainedEvidenceManifestDto manifest,
        EvidenceVaultIdentityDto manifestIdentity)
    {
        var retainedHash = NormalizeHash(manifestIdentity.ContentHashSha256);
        if (!IsSha256Hash(retainedHash))
        {
            return false;
        }

        var currentHash = ComputeManifestContentHash(manifest with
        {
            VaultIdentity = NormalizeVaultIdentityForContentHash(manifestIdentity)
        });
        if (string.Equals(currentHash, retainedHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Schema-v1 manifests written before the embedded identity became part of the canonical
        // hash were hashed with a null identity. Keep those manifests readable while still
        // rejecting any semantic mutation that does not preserve their retained hash.
        var legacyHash = ComputeManifestContentHash(manifest with { VaultIdentity = null });
        return string.Equals(legacyHash, retainedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256Hash(string value)
        => value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');

    public static string ResolveDataRoot(IServiceProvider services)
    {
        var applicationConfig = services.GetService<Meridian.Application.UI.ConfigStore>();
        if (applicationConfig is not null)
        {
            return applicationConfig.GetDataRoot();
        }

        var sharedConfig = services.GetService<Meridian.Ui.Shared.Services.ConfigStore>();
        if (sharedConfig is not null)
        {
            return sharedConfig.GetDataRoot();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
    }

    public async Task<IReadOnlyList<EvidenceVaultIdentityDto>> FindByLinkageAsync(
        EvidenceVaultLookupRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!HasRequiredScope(request.TenantId, request.Scope))
        {
            return [];
        }

        var vaultDir = Path.Combine(_rootDirectory, "_vault");
        if (!Directory.Exists(vaultDir))
        {
            return [];
        }

        var matches = new List<EvidenceVaultIdentityDto>();
        foreach (var indexPath in Directory.EnumerateFiles(vaultDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var identity = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
            if (identity is null || !MatchesIdentityScope(identity, request.TenantId, request.Scope))
            {
                continue;
            }

            var manifestPath = ResolveVaultManifestPath(identity, identity.VaultId);
            if (manifestPath is null
                || !await ManifestMatchesScopeAsync(
                        manifestPath,
                        identity,
                        request.TenantId!,
                        request.Scope!,
                        ct)
                    .ConfigureAwait(false))
            {
                continue;
            }

            var linkage = await TryReadLinkageAsync(manifestPath, ct).ConfigureAwait(false);
            if (MatchesLookup(request, linkage, identity))
            {
                matches.Add(identity);
            }
        }

        return matches.OrderByDescending(x => x.RetainedAt).ToArray();
    }

    public async Task<IReadOnlyList<EvidenceVaultRequestListEntryDto>> ListRequestListsAsync(
        EvidenceVaultRequestListQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new EvidenceVaultRequestListQueryDto();
        if (!HasRequiredScope(query.TenantId, query.Scope))
        {
            return [];
        }

        var vaultDir = Path.Combine(_rootDirectory, "_vault");
        if (!Directory.Exists(vaultDir))
        {
            return [];
        }

        var matches = new List<EvidenceVaultRequestListEntryDto>();
        foreach (var indexPath in Directory.EnumerateFiles(vaultDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var identity = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
            if (identity is null)
            {
                continue;
            }

            if (!MatchesRequestListIdentity(query, identity))
            {
                continue;
            }

            foreach (var requestList in identity.RequestLists)
            {
                if (!MatchesRequestList(query, requestList))
                {
                    continue;
                }

                var supportRequests = identity.SupportRequests
                    .Where(request => requestList.RequestIds.Contains(request.RequestId, StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(static request => request.Severity)
                    .ThenBy(static request => request.RequestKind, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static request => request.RequestId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                matches.Add(new EvidenceVaultRequestListEntryDto(
                    RequestListId: requestList.RequestListId,
                    RequestListKind: requestList.RequestListKind,
                    TargetKind: requestList.TargetKind,
                    TargetId: requestList.TargetId,
                    HighestSeverity: requestList.HighestSeverity,
                    Status: requestList.Status,
                    RequestCount: requestList.RequestCount,
                    OpenRequestCount: supportRequests.Count(static request => string.Equals(request.Status, "Open", StringComparison.OrdinalIgnoreCase)),
                    RequestIds: requestList.RequestIds,
                    EvidenceKinds: requestList.EvidenceKinds,
                    BlockedOutputs: requestList.BlockedOutputs,
                    Summary: requestList.Summary,
                    VaultId: identity.VaultId,
                    SubjectKind: identity.SubjectKind,
                    SubjectId: identity.SubjectId,
                    ManifestRoute: identity.ManifestRoute,
                    RetainedAt: identity.RetainedAt,
                    SupportRequests: supportRequests)
                {
                    RequestListKindCode = ResolveRequestListKindCode(requestList.RequestListKind, requestList.TargetKind)
                });
            }
        }

        var maxResults = Math.Clamp(query.MaxResults ?? 250, 1, 500);
        return matches
            .OrderBy(static entry => RequestListStatusRank(entry.Status))
            .ThenByDescending(static entry => entry.HighestSeverity)
            .ThenByDescending(static entry => entry.RetainedAt)
            .ThenBy(static entry => entry.TargetKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.TargetId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.RequestListKind, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    public async Task<IReadOnlyList<EvidenceVaultDocumentEntryDto>> ListDocumentsAsync(
        EvidenceVaultDocumentQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new EvidenceVaultDocumentQueryDto();
        if (!HasRequiredScope(query.TenantId, query.Scope))
        {
            return [];
        }

        var vaultDir = Path.Combine(_rootDirectory, "_vault");
        if (!Directory.Exists(vaultDir))
        {
            return [];
        }

        var maxResults = Math.Clamp(query.MaxResults ?? 250, 1, 500);
        var locatorInspectionLimit = Math.Min(
            _documentLocatorInspectionLimit,
            Math.Max(
                MinDocumentLocatorInspections,
                maxResults + DocumentVerificationOverscan));
        var locators = new List<EvidenceVaultIdentityDto>(locatorInspectionLimit);
        var inspectedLocatorCount = 0;
        foreach (var indexPath in Directory.EnumerateFiles(vaultDir, "*.json"))
        {
            if (inspectedLocatorCount >= locatorInspectionLimit)
            {
                break;
            }

            ct.ThrowIfCancellationRequested();
            inspectedLocatorCount++;
            var locator = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
            if (locator is null || !MatchesIdentityScope(locator, query.TenantId, query.Scope))
            {
                continue;
            }

            // Index data is only a scoped locator. Document fields in the index may trail an
            // atomically committed manifest after a split review write and cannot participate in
            // query filtering or priority ordering.
            locators.Add(locator);
        }

        var semanticCandidateLimit = Math.Min(
            locatorInspectionLimit,
            MaxDocumentVerificationCandidates);
        var semanticCandidates = new List<VerifiedDocumentCandidate>(semanticCandidateLimit);
        foreach (var locator in locators)
        {
            ct.ThrowIfCancellationRequested();
            if (semanticCandidates.Count >= semanticCandidateLimit)
            {
                break;
            }

            var resolved = await TryResolveVerifiedDocumentCandidatesAsync(
                    locator,
                    query,
                    semanticCandidateLimit - semanticCandidates.Count,
                    ct)
                .ConfigureAwait(false);
            foreach (var candidate in resolved)
            {
                semanticCandidates.Add(candidate);
            }
        }

        var matches = new List<EvidenceVaultDocumentEntryDto>(maxResults);
        var remainingVerificationBytes = Math.Min(
            checked(maxResults * MaxRetainedArtifactBytes),
            _documentVerificationByteLimit);
        foreach (var candidate in semanticCandidates
                     .OrderBy(static candidate => DocumentReviewRank(candidate.Document.ReviewerState.Status))
                     .ThenBy(static candidate => DocumentExtractionRank(candidate.Document.ExtractionStatus))
                     .ThenByDescending(static candidate => candidate.Document.ReceivedAt)
                     .ThenBy(static candidate => candidate.Document.FileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static candidate => candidate.Identity.VaultId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static candidate => candidate.Document.DocumentId, StringComparer.OrdinalIgnoreCase))
        {
            if (matches.Count == maxResults)
            {
                break;
            }

            if (candidate.Artifact.SizeBytes < 0
                || candidate.Artifact.SizeBytes > MaxRetainedArtifactBytes
                || candidate.Artifact.SizeBytes > remainingVerificationBytes)
            {
                continue;
            }

            // Reserve the declared retained size even when verification later fails. This makes
            // the maximum bytes hashed proportional to the requested result count.
            remainingVerificationBytes -= candidate.Artifact.SizeBytes;
            if (!await HasVerifiedRetainedDocumentContentAsync(candidate, ct).ConfigureAwait(false))
            {
                continue;
            }

            var identity = candidate.Identity;
            matches.Add(new EvidenceVaultDocumentEntryDto(
                Document: candidate.Document,
                VaultId: identity.VaultId,
                SubjectKind: identity.SubjectKind,
                SubjectId: identity.SubjectId,
                ManifestRoute: identity.ManifestRoute,
                RetainedAt: identity.RetainedAt,
                StorageKind: identity.StorageKind,
                OpenRequestCount: identity.SupportRequests.Count(static request =>
                    string.Equals(request.Status, "Open", StringComparison.OrdinalIgnoreCase)),
                SupportRequests: identity.SupportRequests));
        }

        return matches;
    }

    private async Task<IReadOnlyList<VerifiedDocumentCandidate>> TryResolveVerifiedDocumentCandidatesAsync(
        EvidenceVaultIdentityDto locator,
        EvidenceVaultDocumentQueryDto query,
        int maxCandidates,
        CancellationToken ct)
    {
        if (maxCandidates <= 0)
        {
            return [];
        }

        var manifestPath = ResolveVaultManifestPath(locator, locator.VaultId);
        var manifest = manifestPath is null
            ? null
            : await TryReadRetainedManifestAsync(manifestPath, ct).ConfigureAwait(false);
        if (manifest is null
            || !TryResolveManifestAuthority(
                manifest,
                locator,
                query.TenantId!,
                query.Scope!,
                out var manifestIdentity)
            || manifestIdentity is null
            || !MatchesDocumentIdentity(query, manifestIdentity))
        {
            return [];
        }

        var candidates = new List<VerifiedDocumentCandidate>(Math.Min(maxCandidates, manifestIdentity.Documents.Count));
        foreach (var document in ResolveIdentityDocuments(manifestIdentity))
        {
            if (candidates.Count >= maxCandidates)
            {
                break;
            }

            if (!MatchesDocument(query, document)
                || !TryResolveUniqueDocument(manifestIdentity, document.DocumentId, out var manifestDocument)
                || manifestDocument is null
                || !DocumentSemanticsMatch(document, manifestDocument)
                || !TryResolveUniqueDocumentArtifact(manifestIdentity, manifestDocument, out var artifact)
                || artifact?.Document is null
                || !DocumentSemanticsMatch(manifestDocument, artifact.Document)
                || !ManifestSnapshotDocumentMatches(manifestIdentity, manifestDocument)
                || !ArtifactContentMatchesDocument(artifact, manifestDocument))
            {
                continue;
            }

            candidates.Add(new VerifiedDocumentCandidate(manifestIdentity, manifestDocument, artifact));
        }

        return candidates;
    }

    private async Task<bool> HasVerifiedRetainedDocumentContentAsync(
        VerifiedDocumentCandidate candidate,
        CancellationToken ct)
    {
        var identity = candidate.Identity;
        var artifact = candidate.Artifact;

        var artifactPath = ResolveVaultArtifactPath(identity.VaultId, artifact.RelativePath);
        if (artifactPath is null)
        {
            return false;
        }

        try
        {
            await using var stream = new FileStream(
                artifactPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            if (stream.Length != artifact.SizeBytes)
            {
                return false;
            }

            var contentHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false))
                .ToLowerInvariant();
            return string.Equals(
                contentHash,
                NormalizeHash(artifact.ContentHashSha256),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Evidence vault artifact '{ArtifactPath}' could not be verified.",
                artifactPath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "Evidence vault artifact '{ArtifactPath}' could not be accessed for verification.",
                artifactPath);
            return false;
        }
    }

    private static bool TryResolveUniqueDocument(
        EvidenceVaultIdentityDto identity,
        string documentId,
        out EvidenceDocumentDto? document)
    {
        var matches = ResolveIdentityDocuments(identity)
            .Where(candidate => string.Equals(
                candidate.DocumentId,
                documentId,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        document = matches.Length == 1 ? matches[0] : null;
        return document is not null;
    }

    private static bool TryResolveUniqueDocumentArtifact(
        EvidenceVaultIdentityDto identity,
        EvidenceDocumentDto document,
        out EvidenceVaultArtifactDto? artifact)
    {
        var matches = identity.Artifacts
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(document.ArtifactId)
                && string.Equals(
                    candidate.ArtifactId,
                    document.ArtifactId,
                    StringComparison.OrdinalIgnoreCase)
                && candidate.Document is { } artifactDocument
                && string.Equals(
                    artifactDocument.DocumentId,
                    document.DocumentId,
                    StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        artifact = matches.Length == 1 ? matches[0] : null;
        return artifact is not null;
    }

    private bool DocumentSemanticsMatch(
        EvidenceDocumentDto first,
        EvidenceDocumentDto second)
        => JsonSerializer.SerializeToUtf8Bytes(first, _jsonOptions)
            .AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(second, _jsonOptions));

    private bool ManifestSnapshotDocumentMatches(
        EvidenceVaultIdentityDto identity,
        EvidenceDocumentDto document)
    {
        if (identity.ManifestSnapshot is null)
        {
            return true;
        }

        var matches = identity.ManifestSnapshot.Documents
            .Where(candidate => string.Equals(
                candidate.DocumentId,
                document.DocumentId,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 && DocumentSemanticsMatch(document, matches[0]);
    }

    private static bool ArtifactContentMatchesDocument(
        EvidenceVaultArtifactDto artifact,
        EvidenceDocumentDto document)
        => artifact.SizeBytes >= 0
           && string.Equals(artifact.ArtifactId, document.ArtifactId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(artifact.ContentHashSha256, document.SourceHashSha256, StringComparison.OrdinalIgnoreCase);

    private string? ResolveVaultArtifactPath(string vaultId, string relativePath)
    {
        var safeVaultId = ValidateVaultId(vaultId);
        if (safeVaultId is null || string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalizedPath = relativePath.Replace('\\', '/').Trim();
        var expectedPrefix = $"{ManifestRelativeRoot}_vault/{safeVaultId}/artifacts/";
        if (Path.IsPathRooted(normalizedPath)
            || !normalizedPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileName = normalizedPath[expectedPrefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/')
            || fileName is "." or ".."
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        try
        {
            var artifactDirectory = Path.GetFullPath(Path.Combine(
                _rootDirectory,
                "_vault",
                safeVaultId,
                "artifacts"));
            var artifactPath = Path.GetFullPath(Path.Combine(artifactDirectory, fileName));
            var directoryPrefix = artifactDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return artifactPath.StartsWith(directoryPrefix, PathComparison)
                   && IsUnderRoot(artifactPath, _rootDirectory)
                ? artifactPath
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    [Obsolete("Use ReviewDocumentAsync(..., tenantId, scope, request, ct); Evidence Vault reviews require authenticated scope.")]
    public Task<EvidenceVaultDocumentReviewResponseDto?> ReviewDocumentAsync(
        string vaultId,
        string documentId,
        EvidenceVaultDocumentReviewRequestDto request,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    public async Task<EvidenceVaultDocumentReviewResponseDto?> ReviewDocumentAsync(
        string vaultId,
        string documentId,
        string tenantId,
        string scope,
        EvidenceVaultDocumentReviewRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var safeVaultId = ValidateVaultId(vaultId);
        if (safeVaultId is null)
        {
            throw new ArgumentException("Evidence vault id is invalid.", nameof(vaultId));
        }

        var normalizedDocumentId = RequireTrimmed(documentId, nameof(documentId));
        var normalizedTenantId = RequireTrimmed(tenantId, nameof(tenantId));
        var normalizedScope = RequireTrimmed(scope, nameof(scope));
        var reviewer = RequireTrimmed(request.Reviewer, nameof(request.Reviewer));

        // Avoid retaining a process-lifetime lock for a vault that does not exist. The identity is
        // read again after acquiring the lock so a concurrent review still operates on the latest
        // persisted state.
        var indexPath = Path.Combine(_rootDirectory, "_vault", $"{safeVaultId}.json");
        var identity = await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
        if (identity is null
            || !MatchesIdentityScope(identity, normalizedTenantId, normalizedScope)
            || !ContainsScopedDocument(
                identity,
                normalizedDocumentId,
                normalizedTenantId,
                normalizedScope))
        {
            return null;
        }

        var vaultLock = _vaultWriteLocks.GetOrAdd(safeVaultId, static _ => new SemaphoreSlim(1, 1));
        await vaultLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReviewDocumentUnderLockAsync(
                    safeVaultId,
                    normalizedDocumentId,
                    normalizedTenantId,
                    normalizedScope,
                    reviewer,
                    request,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            vaultLock.Release();
        }
    }

    private static bool ContainsScopedDocument(
        EvidenceVaultIdentityDto identity,
        string documentId,
        string tenantId,
        string scope) =>
        ResolveIdentityDocuments(identity).Any(document =>
            string.Equals(document.DocumentId, documentId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(document.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(document.Scope, scope, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<EvidenceDocumentConfirmedFieldDto> NormalizeConfirmedFields(
        IReadOnlyList<EvidenceDocumentConfirmedFieldDto>? confirmedFields,
        string reviewer,
        DateTimeOffset reviewedAt)
    {
        if (confirmedFields is null || confirmedFields.Count == 0)
        {
            return [];
        }

        return confirmedFields
            .Where(static field =>
                !string.IsNullOrWhiteSpace(field.FieldName) &&
                !string.IsNullOrWhiteSpace(field.ConfirmedValue))
            .Select(field => field with
            {
                FieldName = field.FieldName.Trim(),
                ConfirmedValue = field.ConfirmedValue.Trim(),
                ConfirmedBy = reviewer,
                ConfirmedAt = reviewedAt,
                SourceFieldName = NormalizeOptional(field.SourceFieldName),
                Notes = NormalizeOptional(field.Notes)
            })
            .GroupBy(static field => field.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static field => field.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<EvidenceDocumentConfirmedFieldDto> NormalizeTrustedInitialConfirmedFields(
        IReadOnlyList<EvidenceDocumentConfirmedFieldDto>? confirmedFields,
        string defaultReviewer,
        DateTimeOffset defaultConfirmedAt)
    {
        if (confirmedFields is null || confirmedFields.Count == 0)
        {
            return [];
        }

        return confirmedFields
            .Where(static field =>
                !string.IsNullOrWhiteSpace(field.FieldName) &&
                !string.IsNullOrWhiteSpace(field.ConfirmedValue))
            .Select(field => field with
            {
                FieldName = field.FieldName.Trim(),
                ConfirmedValue = field.ConfirmedValue.Trim(),
                ConfirmedBy = FirstNonEmpty(field.ConfirmedBy, defaultReviewer)!,
                ConfirmedAt = field.ConfirmedAt == default ? defaultConfirmedAt : field.ConfirmedAt,
                SourceFieldName = NormalizeOptional(field.SourceFieldName),
                Notes = NormalizeOptional(field.Notes)
            })
            .GroupBy(static field => field.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static field => field.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasRequiredScope(string? tenantId, string? scope)
        => !string.IsNullOrWhiteSpace(tenantId)
           && !string.IsNullOrWhiteSpace(scope);

    private static (string TenantId, string Scope) RequireWriteScope(string? tenantId, string? scope)
    {
        if (!HasRequiredScope(tenantId, scope))
        {
            throw new ArgumentException(
                "Evidence Vault writes require non-empty tenant and company scope.");
        }

        return (tenantId!.Trim(), scope!.Trim());
    }

    private static NotSupportedException UnscopedAccessNotSupported() =>
        new("Evidence Vault access requires an authenticated tenant and company scope. " +
            "Migrate to the scoped overload.");

    private static bool MatchesIdentityScope(
        EvidenceVaultIdentityDto identity,
        string? tenantId,
        string? scope)
        => HasRequiredScope(identity.TenantId, identity.Scope)
           && HasRequiredScope(tenantId, scope)
           && string.Equals(identity.TenantId!.Trim(), tenantId!.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(identity.Scope!.Trim(), scope!.Trim(), StringComparison.OrdinalIgnoreCase);
}
