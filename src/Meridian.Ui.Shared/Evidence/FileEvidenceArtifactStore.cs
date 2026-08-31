using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Evidence;

public interface IEvidenceArtifactStore
{
    Task<EvidencePacketExportResponse> WriteManifestAsync(
        EvidencePacketDto packet,
        EvidencePacketExportRequest request,
        CancellationToken ct = default);
    Task<EvidenceVaultIntakeResponseDto> WriteIntakeArtifactAsync(
        EvidenceVaultIntakeRequestDto request,
        CancellationToken ct = default);

    /// <summary>Legacy unscoped manifest lookup retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use TryOpenManifestAsync(..., tenantId, scope, ct); Evidence Vault reads require authenticated scope.")]
    Task<EvidenceManifestFile?> TryOpenManifestAsync(
        string subjectKind,
        string subjectId,
        string fileName,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    Task<EvidenceManifestFile?> TryOpenManifestAsync(
        string subjectKind,
        string subjectId,
        string fileName,
        string tenantId,
        string scope,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    /// <summary>Legacy unscoped vault lookup retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use TryOpenManifestByVaultIdAsync(vaultId, tenantId, scope, ct); Evidence Vault reads require authenticated scope.")]
    Task<EvidenceManifestFile?> TryOpenManifestByVaultIdAsync(
        string vaultId,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    Task<EvidenceManifestFile?> TryOpenManifestByVaultIdAsync(
        string vaultId,
        string tenantId,
        string scope,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    /// <summary>Legacy unscoped identity lookup retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use TryGetVaultIdentityAsync(vaultId, tenantId, scope, ct); Evidence Vault reads require authenticated scope.")]
    Task<EvidenceVaultIdentityDto?> TryGetVaultIdentityAsync(
        string vaultId,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    Task<EvidenceVaultIdentityDto?> TryGetVaultIdentityAsync(
        string vaultId,
        string tenantId,
        string scope,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    Task<IReadOnlyList<EvidenceVaultIdentityDto>> FindByLinkageAsync(
        EvidenceVaultLookupRequestDto request,
        CancellationToken ct = default);

    Task<IReadOnlyList<EvidenceVaultRequestListEntryDto>> ListRequestListsAsync(
        EvidenceVaultRequestListQueryDto query,
        CancellationToken ct = default);

    Task<IReadOnlyList<EvidenceVaultDocumentEntryDto>> ListDocumentsAsync(
        EvidenceVaultDocumentQueryDto query,
        CancellationToken ct = default);

    /// <summary>Legacy unscoped review mutation retained as a fail-closed compatibility shim.</summary>
    [Obsolete("Use ReviewDocumentAsync(..., tenantId, scope, request, ct); Evidence Vault reviews require authenticated scope.")]
    Task<EvidenceVaultDocumentReviewResponseDto?> ReviewDocumentAsync(
        string vaultId,
        string documentId,
        EvidenceVaultDocumentReviewRequestDto request,
        CancellationToken ct = default) =>
        throw UnscopedAccessNotSupported();

    Task<EvidenceVaultDocumentReviewResponseDto?> ReviewDocumentAsync(
        string vaultId,
        string documentId,
        string tenantId,
        string scope,
        EvidenceVaultDocumentReviewRequestDto request,
        CancellationToken ct = default) =>
        throw ScopedImplementationRequired();

    private static NotSupportedException UnscopedAccessNotSupported() =>
        new("Evidence Vault access requires an authenticated tenant and company scope. " +
            "Migrate to the scoped overload.");

    private static NotSupportedException ScopedImplementationRequired() =>
        new("This evidence store implementation must implement the tenant/company-scoped contract.");
}
public sealed record EvidenceManifestFile(
    Stream Content,
    string ContentType,
    string FileName,
    DateTimeOffset LastModified);
public sealed partial class FileEvidenceArtifactStore : IEvidenceArtifactStore
{
    private const string ManifestRelativeRoot = "workstation/evidence/";
    private const string FileManifestStorageKind = "file-manifest";
    private const string FileBundleStorageKind = "file-bundle";
    private const long MaxRetainedArtifactBytes = 100 * 1024 * 1024;
    private static readonly HashSet<string> SupportedCanonicalSubjectKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "run",
        "account",
        "fund",
        "strategy",
        "instrument",
        "reconciliation",
        "reconciliation-case",
        "report",
        "report-pack",
        "approval",
        "statement-run",
        EvidenceSubjectResolver.StrategyRunKind,
        EvidenceSubjectResolver.PaperReadinessKind,
        EvidenceSubjectResolver.ReconciliationReviewKind,
        EvidenceSubjectResolver.ReportPackKind,
        EvidenceSubjectResolver.ProviderTrustKind,
        EvidenceSubjectResolver.AnalysisExportKind,
        EvidenceSubjectResolver.SecurityMasterConflictKind,
        EvidenceSubjectResolver.ApprovalKind,
        EvidenceSubjectResolver.AccountingRecordKind,
        EvidenceSubjectResolver.PrivateCapitalFundEventKind,
        EvidenceSubjectResolver.PaymentIntentKind,
        EvidenceSubjectResolver.ReportPackDeliveryKind
    };
    private readonly string _rootDirectory;
    private readonly ILogger<FileEvidenceArtifactStore> _logger;
    private readonly long _documentVerificationByteLimit;
    private readonly int _documentLocatorInspectionLimit;
    // Serializes read-modify-write cycles on a vault's manifest/index pair. AtomicFileWriter
    // only makes each single write atomic; without this, concurrent document reviews on the
    // same vault could read the same snapshot and silently clobber each other's updates.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _vaultWriteLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public FileEvidenceArtifactStore(string dataRoot, ILogger<FileEvidenceArtifactStore> logger)
        : this(dataRoot, logger, MaxDocumentVerificationBytesPerRequest)
    {
    }

    internal FileEvidenceArtifactStore(
        string dataRoot,
        ILogger<FileEvidenceArtifactStore> logger,
        long documentVerificationByteLimit,
        int documentLocatorInspectionLimit = MaxDocumentLocatorInspections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (documentVerificationByteLimit <= 0
            || documentVerificationByteLimit > MaxDocumentVerificationBytesPerRequest)
        {
            throw new ArgumentOutOfRangeException(nameof(documentVerificationByteLimit));
        }

        if (documentLocatorInspectionLimit <= 0
            || documentLocatorInspectionLimit > MaxDocumentLocatorInspections)
        {
            throw new ArgumentOutOfRangeException(nameof(documentLocatorInspectionLimit));
        }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootDirectory = Path.Combine(dataRoot, "workstation", "evidence");
        _documentVerificationByteLimit = documentVerificationByteLimit;
        _documentLocatorInspectionLimit = documentLocatorInspectionLimit;
    }
    public async Task<EvidencePacketExportResponse> WriteManifestAsync(
        EvidencePacketDto packet,
        EvidencePacketExportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        request ??= new EvidencePacketExportRequest(null, null);
        var (tenantId, scope) = RequireWriteScope(request.TenantId, request.Scope);

        var generatedAt = DateTimeOffset.UtcNow;
        var subjectKind = SanitizePathSegment(packet.Subject.SubjectKind);
        var subjectId = SanitizePathSegment(packet.Subject.SubjectId);
        var supportRequests = BuildSupportRequests(packet);
        var requestLists = BuildRequestLists(packet.Subject, supportRequests);
        var manifest = new RetainedEvidenceManifestDto(
            SchemaVersion: 1,
            ExportedAt: generatedAt,
            RequestedBy: request.RequestedBy,
            Reason: request.Reason,
            ManifestOnly: true,
            Subject: packet.Subject,
            Completeness: packet.Completeness,
            Nodes: packet.Nodes,
            Edges: packet.Edges,
            Actions: packet.Actions,
            Warnings: request.IncludeWarnings ? packet.Warnings : [],
            RequestLists: requestLists,
            SupportRequests: supportRequests,
            VaultIdentity: null,
            Lifecycle: request.Lifecycle,
            Linkage: ResolveManifestLinkage(packet, request))
        {
            TenantId = tenantId,
            Scope = scope
        };
        ValidateRetainedArtifactReferences(packet);
        var retainedExportJson = JsonSerializer.Serialize(manifest, _jsonOptions);
        var contentHash = Sha256Digest.ComputeUtf8(retainedExportJson);
        var vaultId = $"ev-{contentHash[..24]}";
        // The scoped hash suffix prevents different tenants exporting the same subject in the
        // same millisecond from sharing and overwriting one manifest path.
        var fileName = $"{generatedAt:yyyyMMddTHHmmssfffZ}-{contentHash[..12]}-manifest.json";
        var directory = Path.Combine(_rootDirectory, subjectKind, subjectId);
        var manifestPath = Path.Combine(directory, fileName);
        var relativePath = Path.Combine("workstation", "evidence", subjectKind, subjectId, fileName);
        var manifestRoute = $"/workstation/evidence/{RouteSegment(subjectKind)}/{RouteSegment(subjectId)}/{RouteSegment(fileName)}";
        var retainedArtifacts = await RetainLocalArtifactsAsync(packet, vaultId, generatedAt, ct).ConfigureAwait(false);
        var vaultIdentity = new EvidenceVaultIdentityDto(
            VaultId: vaultId,
            SubjectKind: packet.Subject.SubjectKind,
            SubjectId: packet.Subject.SubjectId,
            ManifestPath: relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            ManifestRoute: manifestRoute,
            RetainedAt: generatedAt,
            ContentHashSha256: contentHash,
            SchemaVersion: 1,
            StorageKind: retainedArtifacts.Count == 0 ? FileManifestStorageKind : FileBundleStorageKind)
        {
            TenantId = tenantId,
            Scope = scope,
            Artifacts = retainedArtifacts,
            RequestLists = requestLists,
            SupportRequests = supportRequests,
            Documents = ResolveArtifactDocuments(retainedArtifacts).ToArray(),
            ManifestSnapshot = BuildManifestSnapshot(
                vaultId,
                packet.Subject.SubjectKind,
                packet.Subject.SubjectId,
                generatedAt,
                contentHash,
                ResolveArtifactDocuments(retainedArtifacts),
                supportRequests,
                requestLists)
        };
        manifest = manifest with { VaultIdentity = vaultIdentity };
        vaultIdentity = RefreshVaultIdentityContentHash(vaultIdentity, manifest);
        manifest = manifest with { VaultIdentity = vaultIdentity };

        await AtomicFileWriter
            .WriteAsync(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions), ct)
            .ConfigureAwait(false);
        await WriteVaultIndexAsync(vaultIdentity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Wrote evidence manifest for {SubjectKind}/{SubjectId} to {ManifestPath}.",
            packet.Subject.SubjectKind,
            packet.Subject.SubjectId,
            manifestPath);

        return new EvidencePacketExportResponse(
            SubjectKind: packet.Subject.SubjectKind,
            SubjectId: packet.Subject.SubjectId,
            GeneratedAt: generatedAt,
            ManifestPath: relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            ManifestRoute: manifestRoute,
            EvidenceCount: packet.Nodes.Count,
            WarningCount: request.IncludeWarnings ? packet.Warnings.Count : 0,
            Retained: true)
        {
            VaultIdentity = vaultIdentity
        };
    }

    public async Task<EvidenceVaultIntakeResponseDto> WriteIntakeArtifactAsync(
        EvidenceVaultIntakeRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tenantId, scope) = RequireWriteScope(request.TenantId, request.Scope);
        var subjectKind = RequireText(request.SubjectKind, nameof(request.SubjectKind));
        var subjectId = RequireText(request.SubjectId, nameof(request.SubjectId));
        var intakeChannel = RequireText(request.IntakeChannel, nameof(request.IntakeChannel));
        var fileName = RequireText(request.FileName, nameof(request.FileName));
        var channelKind = ResolveIntakeChannelKind(request, intakeChannel);

        if (!SupportedCanonicalSubjectKinds.Contains(subjectKind))
        {
            throw new ArgumentException(
                $"Evidence vault intake subject kind '{subjectKind}' is not supported.",
                nameof(request));
        }

        var intakeContent = await ResolveIntakeContentAsync(request, ct).ConfigureAwait(false);
        var content = intakeContent.Content;
        var contentHash = intakeContent.ContentHashSha256;
        var capturedAt = DateTimeOffset.UtcNow;
        var vaultId = BuildIntakeVaultId(
            subjectKind,
            subjectId,
            fileName,
            contentHash,
            capturedAt,
            tenantId,
            scope);
        var manifestFileName = $"{capturedAt:yyyyMMddTHHmmssfffZ}-intake-manifest.json";
        var manifestPath = Path.Combine(_rootDirectory, "_vault", vaultId, manifestFileName);
        var manifestRoute = $"/workstation/evidence/_vault/{RouteSegment(vaultId)}/{RouteSegment(manifestFileName)}";
        var safeFileName = BuildIntakeArtifactFileName(fileName, contentHash);
        var artifactDirectory = Path.Combine(_rootDirectory, "_vault", vaultId, "artifacts");
        var artifactPath = Path.Combine(artifactDirectory, safeFileName);
        await AtomicFileWriter.WriteAsync(artifactPath, content, ct).ConfigureAwait(false);

        var artifactRelativePath = Path.Combine("workstation", "evidence", "_vault", vaultId, "artifacts", safeFileName)
            .Replace(Path.DirectorySeparatorChar, '/');
        var capture = new EvidenceArtifactCaptureDto(
            CaptureChannel: intakeChannel,
            SourceSystem: NormalizeOptional(request.SourceSystem),
            ReceivedAt: capturedAt,
            ReceivedBy: NormalizeOptional(request.ReceivedBy),
            SourceReference: FirstNonEmpty(request.SourceReference, intakeContent.SourceReference),
            ReceiptHash: contentHash,
            ChannelKind: channelKind);
        var extractedFields = request.ExtractedFields?.ToArray() ?? [];
        var artifactId = $"intake:{vaultId}";
        var document = BuildEvidenceDocument(
            request,
            vaultId,
            artifactId,
            fileName,
            contentHash,
            capturedAt,
            manifestRoute,
            FirstNonEmpty(request.SourceReference, intakeContent.SourceReference),
            extractedFields);
        var artifact = new EvidenceVaultArtifactDto(
            ArtifactId: artifactId,
            Kind: "vault-intake",
            RelativePath: artifactRelativePath,
            ContentHashSha256: contentHash,
            SizeBytes: content.LongLength,
            RetainedAt: capturedAt,
            SourcePath: intakeContent.SourcePath,
            SourceRoute: FirstNonEmpty(request.SourceReference, intakeContent.SourceReference),
            CanonicalSubjectKind: subjectKind,
            CanonicalSubjectId: subjectId)
        {
            Capture = capture,
            ExtractedFields = extractedFields,
            Document = document
        };
        var subject = new EvidenceSubjectDto(
            SubjectId: subjectId,
            SubjectKind: subjectKind,
            Label: $"{subjectKind}/{subjectId}",
            Workspace: "Evidence Vault",
            Route: null,
            PageTag: "EvidenceVaultIntake");
        var nodeStatus = ResolveIntakeStatus(extractedFields);
        var node = new EvidenceNodeDto(
            EvidenceId: artifactId,
            Subject: subject,
            Kind: "vault-intake",
            Status: nodeStatus,
            Freshness: new EvidenceFreshnessDto(capturedAt, IsStale: false, Reason: null),
            SourceSystem: NormalizeOptional(request.SourceSystem) ?? "evidence-vault",
            Summary: BuildIntakeSummary(fileName, intakeChannel, request.SourceSystem),
            ArtifactRefs:
            [
                new EvidenceArtifactRefDto(
                    ArtifactId: artifactId,
                    Kind: "vault-intake",
                    Path: artifactRelativePath,
                    Route: null,
                    GeneratedAt: capturedAt,
                    Hash: contentHash,
                    Retained: true,
                    CanonicalSubjectKind: subjectKind,
                    CanonicalSubjectId: subjectId)
                {
                    Capture = capture,
                    ExtractedFields = extractedFields
                }
            ],
            RelatedWorkItemIds: []);
        var validationIssues = BuildIntakeValidationIssues(artifactId, extractedFields, node.SourceSystem);
        var completeness = new EvidenceCompletenessDto(
            Score: ResolveIntakeScore(nodeStatus),
            Status: nodeStatus,
            RequiredIds: [artifactId],
            ReadyIds: nodeStatus == EvidenceStatusDto.Ready ? [artifactId] : [],
            MissingIds: [],
            StaleIds: [],
            BlockingWorkItemIds: [])
        {
            ValidationIssues = validationIssues,
            BlockingIssueCount = validationIssues.Count(static issue => issue.Severity == EvidenceValidationSeverityDto.Critical),
            WarningIssueCount = validationIssues.Count(static issue => issue.Severity == EvidenceValidationSeverityDto.Warning)
        };
        var intakePacket = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: capturedAt,
            Nodes: [node],
            Edges: [],
            Completeness: completeness,
            Actions: [],
            Warnings: []);
        var supportRequests = BuildSupportRequests(intakePacket);
        var requestLists = BuildRequestLists(subject, supportRequests);
        var manifest = new RetainedEvidenceManifestDto(
            SchemaVersion: 1,
            ExportedAt: capturedAt,
            RequestedBy: request.ReceivedBy,
            Reason: $"Evidence Vault intake through {intakeChannel}.",
            ManifestOnly: false,
            Subject: subject,
            Completeness: completeness,
            Nodes: [node],
            Edges: [],
            Actions: [],
            Warnings: [],
            RequestLists: requestLists,
            SupportRequests: supportRequests,
            VaultIdentity: null,
            Lifecycle: request.Lifecycle,
            Linkage: ResolveIntakeLinkage(subjectKind, subjectId, request.Linkage))
        {
            TenantId = tenantId,
            Scope = scope
        };
        var manifestJson = JsonSerializer.Serialize(manifest, _jsonOptions);
        var manifestHash = Sha256Digest.ComputeUtf8(manifestJson);
        var vaultIdentity = new EvidenceVaultIdentityDto(
            VaultId: vaultId,
            SubjectKind: subjectKind,
            SubjectId: subjectId,
            ManifestPath: Path.Combine("workstation", "evidence", "_vault", vaultId, manifestFileName)
                .Replace(Path.DirectorySeparatorChar, '/'),
            ManifestRoute: manifestRoute,
            RetainedAt: capturedAt,
            ContentHashSha256: manifestHash,
            SchemaVersion: 1,
            StorageKind: FileBundleStorageKind)
        {
            TenantId = tenantId,
            Scope = scope,
            Artifacts = [artifact],
            RequestLists = requestLists,
            SupportRequests = supportRequests,
            Documents = [document],
            ManifestSnapshot = BuildManifestSnapshot(
                vaultId,
                subjectKind,
                subjectId,
                capturedAt,
                manifestHash,
                [document],
                supportRequests,
                requestLists)
        };
        manifest = manifest with { VaultIdentity = vaultIdentity };
        vaultIdentity = RefreshVaultIdentityContentHash(vaultIdentity, manifest);
        manifest = manifest with { VaultIdentity = vaultIdentity };

        await AtomicFileWriter
            .WriteAsync(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions), ct)
            .ConfigureAwait(false);
        await WriteVaultIndexAsync(vaultIdentity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Retained evidence vault intake {VaultId} for {SubjectKind}/{SubjectId}.",
            vaultId,
            subjectKind,
            subjectId);

        return new EvidenceVaultIntakeResponseDto(
            IntakeId: artifactId,
            SubjectKind: subjectKind,
            SubjectId: subjectId,
            IntakeChannel: intakeChannel,
            FileName: fileName,
            RelativePath: artifactRelativePath,
            ContentHashSha256: contentHash,
            SizeBytes: content.LongLength,
            CapturedAt: capturedAt,
            Capture: capture,
            ExtractedFields: extractedFields,
            VaultIdentity: vaultIdentity)
        {
            Document = document
        };
    }

    private static bool MatchesLookup(EvidenceVaultLookupRequestDto request, EvidenceSubjectLinkageDto? linkage, EvidenceVaultIdentityDto identity)
    {
        if (!string.IsNullOrWhiteSpace(request.EvidenceSubject) && !EvidenceSubjectMatches(request.EvidenceSubject, linkage, identity))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.RunId) && !string.Equals(request.RunId, linkage?.RunId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.PeriodId) && !string.Equals(request.PeriodId, linkage?.PeriodId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.ReportPackId) && !string.Equals(request.ReportPackId, linkage?.ReportPackId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.ReconciliationCaseId) && !ReconciliationCaseIdMatches(request.ReconciliationCaseId, linkage, identity))
            return false;
        if (!string.IsNullOrWhiteSpace(request.AccountingRecordId) && !AccountingRecordIdMatches(request.AccountingRecordId, linkage, identity))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ReportPackDeliveryAttemptId) &&
            !ReportPackDeliveryAttemptIdMatches(request.ReportPackDeliveryAttemptId, linkage, identity))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ReportPackDeliveryPackageId) &&
            !string.Equals(request.ReportPackDeliveryPackageId, linkage?.ReportPackDeliveryPackageId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<EvidenceDocumentDto> ResolveIdentityDocuments(EvidenceVaultIdentityDto identity)
    {
        foreach (var document in identity.Documents)
        {
            yield return document;
        }

        if (identity.Documents.Count > 0)
        {
            yield break;
        }

        foreach (var artifact in identity.Artifacts)
        {
            if (artifact.Document is not null)
            {
                yield return artifact.Document;
            }
        }
    }

    private static EvidenceVaultIdentityDto ReplaceIdentityDocument(
        EvidenceVaultIdentityDto identity,
        EvidenceDocumentDto reviewedDocument)
    {
        var documents = ResolveIdentityDocuments(identity)
            .Select(document => string.Equals(document.DocumentId, reviewedDocument.DocumentId, StringComparison.OrdinalIgnoreCase)
                ? reviewedDocument
                : document)
            .DistinctBy(static document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var artifacts = identity.Artifacts
            .Select(artifact => artifact.Document is not null &&
                                string.Equals(artifact.Document.DocumentId, reviewedDocument.DocumentId, StringComparison.OrdinalIgnoreCase)
                ? artifact with { Document = reviewedDocument }
                : artifact)
            .ToArray();

        return identity with
        {
            Artifacts = artifacts,
            Documents = documents,
            ManifestSnapshot = identity.ManifestSnapshot is null
                ? null
                : ReplaceManifestDocument(identity.ManifestSnapshot, reviewedDocument)
        };
    }

    private EvidenceVaultIdentityDto RefreshVaultIdentityContentHash(
        EvidenceVaultIdentityDto identity,
        RetainedEvidenceManifestDto manifest)
    {
        var contentHash = ComputeManifestContentHash(manifest with
        {
            VaultIdentity = NormalizeVaultIdentityForContentHash(identity)
        });
        return identity with
        {
            ContentHashSha256 = contentHash,
            ManifestSnapshot = identity.ManifestSnapshot is null
                ? null
                : identity.ManifestSnapshot with { ContentHashSha256 = contentHash }
        };
    }

    private string ComputeManifestContentHash(RetainedEvidenceManifestDto manifest)
    {
        var json = JsonSerializer.Serialize(manifest, _jsonOptions);
        return Sha256Digest.ComputeUtf8(json);
    }

    private static EvidenceVaultIdentityDto NormalizeVaultIdentityForContentHash(EvidenceVaultIdentityDto identity)
        => identity with
        {
            ContentHashSha256 = string.Empty,
            ManifestSnapshot = identity.ManifestSnapshot is null
                ? null
                : identity.ManifestSnapshot with { ContentHashSha256 = string.Empty }
        };

    private static EvidenceManifestDto ReplaceManifestDocument(
        EvidenceManifestDto manifest,
        EvidenceDocumentDto reviewedDocument)
    {
        var documents = manifest.Documents
            .Select(document => string.Equals(document.DocumentId, reviewedDocument.DocumentId, StringComparison.OrdinalIgnoreCase)
                ? reviewedDocument
                : document)
            .DistinctBy(static document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var objectLinks = documents
            .SelectMany(static document => document.ObjectLinks)
            .GroupBy(
                static link => $"{link.LinkKind}|{link.ObjectId}|{link.Route}|{link.Relationship}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static link => link.LinkKind)
            .ThenBy(static link => link.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return manifest with
        {
            Documents = documents,
            ObjectLinks = objectLinks
        };
    }

    private static EvidenceExtractionStatusDto ResolveReviewedExtractionStatus(
        EvidenceExtractionStatusDto currentStatus,
        EvidenceDocumentReviewStatusDto reviewStatus)
        => reviewStatus switch
        {
            EvidenceDocumentReviewStatusDto.Accepted => EvidenceExtractionStatusDto.Accepted,
            EvidenceDocumentReviewStatusDto.Rejected => EvidenceExtractionStatusDto.Rejected,
            EvidenceDocumentReviewStatusDto.NeedsReview => EvidenceExtractionStatusDto.NeedsReview,
            _ => currentStatus
        };

    private static IEnumerable<EvidenceDocumentDto> ResolveArtifactDocuments(IEnumerable<EvidenceVaultArtifactDto> artifacts)
    {
        foreach (var artifact in artifacts)
        {
            if (artifact.Document is not null)
            {
                yield return artifact.Document;
            }
        }
    }

    private static bool MatchesDocumentIdentity(
        EvidenceVaultDocumentQueryDto query,
        EvidenceVaultIdentityDto identity)
    {
        if (!MatchesIdentityScope(identity, query.TenantId, query.Scope))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectKind) &&
            !string.Equals(query.SubjectKind, identity.SubjectKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectId) &&
            !string.Equals(query.SubjectId, identity.SubjectId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesDocument(EvidenceVaultDocumentQueryDto query, EvidenceDocumentDto document)
    {
        if (query.Classification.HasValue && document.Classification != query.Classification.Value)
        {
            return false;
        }

        if (query.ChannelKind.HasValue && document.ChannelKind != query.ChannelKind.Value)
        {
            return false;
        }

        if (query.ExtractionStatus.HasValue && document.ExtractionStatus != query.ExtractionStatus.Value)
        {
            return false;
        }

        if (query.ReviewStatus.HasValue && document.ReviewerState.Status != query.ReviewStatus.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.TenantId) &&
            !string.Equals(query.TenantId, document.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Scope) &&
            !string.Equals(query.Scope, document.Scope, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if ((query.LinkKind.HasValue || !string.IsNullOrWhiteSpace(query.ObjectId)) &&
            !document.ObjectLinks.Any(link =>
                (!query.LinkKind.HasValue || link.LinkKind == query.LinkKind.Value) &&
                (string.IsNullOrWhiteSpace(query.ObjectId) ||
                 string.Equals(link.ObjectId, query.ObjectId, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        return true;
    }

    private static int DocumentReviewRank(EvidenceDocumentReviewStatusDto status)
        => status switch
        {
            EvidenceDocumentReviewStatusDto.NeedsReview => 0,
            EvidenceDocumentReviewStatusDto.Unreviewed => 1,
            EvidenceDocumentReviewStatusDto.Rejected => 2,
            EvidenceDocumentReviewStatusDto.Accepted => 3,
            _ => 4
        };

    private static int DocumentExtractionRank(EvidenceExtractionStatusDto status)
        => status switch
        {
            EvidenceExtractionStatusDto.NeedsReview => 0,
            EvidenceExtractionStatusDto.NotExtracted => 1,
            EvidenceExtractionStatusDto.Extracted => 2,
            EvidenceExtractionStatusDto.Rejected => 3,
            EvidenceExtractionStatusDto.Accepted => 4,
            _ => 5
        };

    private static bool MatchesRequestListIdentity(
        EvidenceVaultRequestListQueryDto query,
        EvidenceVaultIdentityDto identity)
    {
        if (!MatchesIdentityScope(identity, query.TenantId, query.Scope))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectKind) &&
            !string.Equals(query.SubjectKind, identity.SubjectKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectId) &&
            !string.Equals(query.SubjectId, identity.SubjectId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesRequestList(
        EvidenceVaultRequestListQueryDto query,
        EvidenceRequestListDto requestList)
    {
        if (!string.IsNullOrWhiteSpace(query.RequestListKind) &&
            !string.Equals(query.RequestListKind, requestList.RequestListKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.RequestListKindCode is { } kindCode &&
            kindCode != EvidenceRequestListKindDto.Unknown &&
            kindCode != ResolveRequestListKindCode(requestList.RequestListKind, requestList.TargetKind))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.TargetKind) &&
            !string.Equals(query.TargetKind, requestList.TargetKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.TargetId) &&
            !string.Equals(query.TargetId, requestList.TargetId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            !string.Equals(query.Status, requestList.Status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int RequestListStatusRank(string status)
        => string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;

    private static bool EvidenceSubjectMatches(
        string evidenceSubject,
        EvidenceSubjectLinkageDto? linkage,
        EvidenceVaultIdentityDto identity)
        => string.Equals(evidenceSubject, linkage?.EvidenceSubject, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(evidenceSubject, $"{identity.SubjectKind}/{identity.SubjectId}", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(evidenceSubject, identity.SubjectId, StringComparison.OrdinalIgnoreCase);

    private static bool ReconciliationCaseIdMatches(
        string reconciliationCaseId,
        EvidenceSubjectLinkageDto? linkage,
        EvidenceVaultIdentityDto identity)
        => string.Equals(reconciliationCaseId, linkage?.ReconciliationCaseId, StringComparison.OrdinalIgnoreCase) ||
           (string.Equals(identity.SubjectKind, "reconciliation-case", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(reconciliationCaseId, identity.SubjectId, StringComparison.OrdinalIgnoreCase)) ||
           ResolveIdentityDocuments(identity).Any(document =>
               document.ObjectLinks.Any(link =>
                   link.LinkKind == EvidenceDocumentLinkKindDto.ReconciliationCase &&
                   string.Equals(link.ObjectId, reconciliationCaseId, StringComparison.OrdinalIgnoreCase)));

    private static bool AccountingRecordIdMatches(
        string accountingRecordId,
        EvidenceSubjectLinkageDto? linkage,
        EvidenceVaultIdentityDto identity)
        => string.Equals(accountingRecordId, linkage?.AccountingRecordId, StringComparison.OrdinalIgnoreCase) ||
           (string.Equals(identity.SubjectKind, EvidenceSubjectResolver.AccountingRecordKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(accountingRecordId, identity.SubjectId, StringComparison.OrdinalIgnoreCase));

    private static bool ReportPackDeliveryAttemptIdMatches(
        string deliveryAttemptId,
        EvidenceSubjectLinkageDto? linkage,
        EvidenceVaultIdentityDto identity)
        => string.Equals(deliveryAttemptId, linkage?.ReportPackDeliveryAttemptId, StringComparison.OrdinalIgnoreCase) ||
           (string.Equals(identity.SubjectKind, EvidenceSubjectResolver.ReportPackDeliveryKind, StringComparison.OrdinalIgnoreCase) &&
            DeliverySubjectIdContainsAttemptId(identity.SubjectId, deliveryAttemptId));

    private static bool DeliverySubjectIdContainsAttemptId(string subjectId, string deliveryAttemptId)
    {
        var normalized = deliveryAttemptId.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return string.Equals(subjectId, normalized, StringComparison.OrdinalIgnoreCase) ||
               subjectId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                   .Any(part => string.Equals(part, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static EvidenceSubjectLinkageDto ResolveManifestLinkage(
        EvidencePacketDto packet,
        EvidencePacketExportRequest request)
        => request.Linkage ?? new EvidenceSubjectLinkageDto(
            $"{packet.Subject.SubjectKind}/{packet.Subject.SubjectId}",
            null,
            null,
            null,
            null,
            string.Equals(packet.Subject.SubjectKind, EvidenceSubjectResolver.AccountingRecordKind, StringComparison.OrdinalIgnoreCase)
                ? packet.Subject.SubjectId
                : null,
            string.Equals(packet.Subject.SubjectKind, EvidenceSubjectResolver.ReportPackDeliveryKind, StringComparison.OrdinalIgnoreCase)
                ? ResolveDeliveryAttemptId(packet.Subject.SubjectId)
                : null,
            ResolveDeliveryPackageId(packet));

    private static string? ResolveDeliveryAttemptId(string subjectId)
    {
        var parts = subjectId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? parts[1] : subjectId;
    }

    private static string? ResolveDeliveryPackageId(EvidencePacketDto packet)
        => string.Equals(packet.Subject.SubjectKind, EvidenceSubjectResolver.ReportPackDeliveryKind, StringComparison.OrdinalIgnoreCase)
            ? ResolveDeliveryPackageIdFromMetadata(packet) ??
              packet.Nodes
                  .Select(static node => node.Summary)
                  .Select(TryResolvePackageId)
                  .FirstOrDefault(static packageId => !string.IsNullOrWhiteSpace(packageId))
            : null;

    private static string? ResolveDeliveryPackageIdFromMetadata(EvidencePacketDto packet)
        => packet.Nodes
            .Where(static node => string.Equals(node.Kind, "delivery-package", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(node.Kind, "delivery-evidence-packet", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(node.Kind, "delivery-record", StringComparison.OrdinalIgnoreCase))
            .Select(static node =>
                node.Metadata.TryGetValue("reportPackDeliveryPackageId", out var packageId)
                    ? packageId
                    : null)
            .FirstOrDefault(static packageId => !string.IsNullOrWhiteSpace(packageId));

    private static string? TryResolvePackageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"pkg-[A-Za-z0-9_.:-]+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }

    private async Task<EvidenceSubjectLinkageDto?> TryReadLinkageAsync(string manifestPath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<RetainedEvidenceManifestDto>(stream, _jsonOptions, ct).ConfigureAwait(false);
        return manifest?.Linkage;
    }

    private static EvidenceManifestDto BuildManifestSnapshot(
        string manifestId,
        string packageKind,
        string packageId,
        DateTimeOffset frozenAt,
        string contentHashSha256,
        IEnumerable<EvidenceDocumentDto> documents,
        IReadOnlyList<EvidenceSupportRequestDto> supportRequests,
        IReadOnlyList<EvidenceRequestListDto> requestLists)
    {
        var documentSnapshots = documents
            .OrderBy(static document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requestSnapshots = supportRequests
            .Select(request =>
            {
                var target = requestLists.FirstOrDefault(list =>
                    list.RequestIds.Contains(request.RequestId, StringComparer.OrdinalIgnoreCase));
                return new EvidenceRequestDto(
                    RequestId: request.RequestId,
                    RequestKind: request.RequestKind,
                    Severity: request.Severity,
                    Status: request.Status,
                    Summary: request.Summary,
                    TargetKind: target?.TargetKind,
                    TargetId: target?.TargetId,
                    BlockedOutput: request.BlockedOutput);
            })
            .OrderBy(static request => SeverityRank(request.Severity))
            .ThenBy(static request => request.RequestKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static request => request.RequestId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var objectLinks = documentSnapshots
            .SelectMany(static document => document.ObjectLinks)
            .GroupBy(
                static link => $"{link.LinkKind}|{link.ObjectId}|{link.Route}|{link.Relationship}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static link => link.LinkKind)
            .ThenBy(static link => link.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new EvidenceManifestDto(
            ManifestId: manifestId,
            FrozenAt: frozenAt,
            PackageKind: packageKind,
            PackageId: packageId,
            ContentHashSha256: contentHashSha256,
            Documents: documentSnapshots,
            Requests: requestSnapshots,
            ObjectLinks: objectLinks)
        {
            PackageKindCode = ResolveManifestPackageKindCode(packageKind, requestLists)
        };
    }

    private static EvidenceManifestPackageKindDto ResolveManifestPackageKindCode(
        string packageKind,
        IReadOnlyList<EvidenceRequestListDto> requestLists)
    {
        if (requestLists.Any(static list => list.RequestListKindCode == EvidenceRequestListKindDto.Close))
        {
            return EvidenceManifestPackageKindDto.CloseBinder;
        }

        if (requestLists.Any(static list => list.RequestListKindCode == EvidenceRequestListKindDto.Audit))
        {
            return EvidenceManifestPackageKindDto.AuditPacket;
        }

        if (requestLists.Any(static list => list.RequestListKindCode == EvidenceRequestListKindDto.ReportPackage))
        {
            return EvidenceManifestPackageKindDto.ReportSupportPackage;
        }

        if (requestLists.Any(static list => list.RequestListKindCode == EvidenceRequestListKindDto.Tax))
        {
            return EvidenceManifestPackageKindDto.TaxSupportPackage;
        }

        if (requestLists.Any(static list => list.RequestListKindCode == EvidenceRequestListKindDto.OperationalEvent))
        {
            return EvidenceManifestPackageKindDto.OperationalEventSupportPackage;
        }

        return packageKind switch
        {
            var value when string.Equals(value, EvidenceSubjectResolver.ReportPackKind, StringComparison.OrdinalIgnoreCase)
                => EvidenceManifestPackageKindDto.ReportSupportPackage,
            var value when string.Equals(value, EvidenceSubjectResolver.ReportPackDeliveryKind, StringComparison.OrdinalIgnoreCase)
                => EvidenceManifestPackageKindDto.ReportSupportPackage,
            _ => EvidenceManifestPackageKindDto.EvidencePacket
        };
    }

    private static IReadOnlyList<EvidenceSupportRequestDto> BuildSupportRequests(EvidencePacketDto packet)
    {
        var nodeById = packet.Nodes
            .GroupBy(static node => node.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var issues = packet.Completeness.ValidationIssues;
        var missingIds = new HashSet<string>(packet.Completeness.MissingIds, StringComparer.OrdinalIgnoreCase);
        var staleIds = new HashSet<string>(packet.Completeness.StaleIds, StringComparer.OrdinalIgnoreCase);
        var blockerIds = new HashSet<string>(packet.Completeness.BlockingWorkItemIds, StringComparer.OrdinalIgnoreCase);
        var blockedOutput = $"{packet.Subject.SubjectKind}/{packet.Subject.SubjectId}";
        var requests = new List<EvidenceSupportRequestDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var evidenceId in packet.Completeness.MissingIds)
        {
            var issue = FindIssue(issues, evidenceId, "missing-required-evidence");
            Add(CreateSupportRequest(
                "MissingEvidence",
                evidenceId,
                issue,
                nodeById,
                EvidenceValidationSeverityDto.Critical,
                issue?.Message ?? $"Required evidence '{evidenceId}' is missing.",
                workItemId: null,
                blockedOutput,
                keyQualifier: null,
                seen));
        }

        foreach (var evidenceId in packet.Completeness.StaleIds)
        {
            var issue = FindIssue(issues, evidenceId, "stale-required-evidence");
            Add(CreateSupportRequest(
                "StaleEvidence",
                evidenceId,
                issue,
                nodeById,
                EvidenceValidationSeverityDto.Warning,
                issue?.Message ?? $"Required evidence '{evidenceId}' is stale.",
                workItemId: null,
                blockedOutput: null,
                keyQualifier: null,
                seen));
        }

        foreach (var workItemId in packet.Completeness.BlockingWorkItemIds)
        {
            var node = packet.Nodes.FirstOrDefault(node =>
                node.RelatedWorkItemIds.Contains(workItemId, StringComparer.OrdinalIgnoreCase));
            var issue = issues.FirstOrDefault(issue =>
                string.Equals(issue.RelatedWorkItemId, workItemId, StringComparison.OrdinalIgnoreCase));
            var evidenceId = !string.IsNullOrWhiteSpace(issue?.EvidenceId)
                ? issue.EvidenceId!
                : node?.EvidenceId ?? workItemId;

            Add(CreateSupportRequest(
                "BlockedWorkItem",
                evidenceId,
                issue,
                nodeById,
                issue?.Severity ?? EvidenceValidationSeverityDto.Critical,
                issue?.Message ?? $"Work item '{workItemId}' blocks evidence support.",
                workItemId,
                blockedOutput,
                keyQualifier: workItemId,
                seen));
        }

        foreach (var issue in issues)
        {
            if (!string.IsNullOrWhiteSpace(issue.EvidenceId))
            {
                if (missingIds.Contains(issue.EvidenceId!) &&
                    string.Equals(issue.Code, "missing-required-evidence", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (staleIds.Contains(issue.EvidenceId!) &&
                    string.Equals(issue.Code, "stale-required-evidence", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(issue.RelatedWorkItemId) && blockerIds.Contains(issue.RelatedWorkItemId!))
            {
                continue;
            }

            var evidenceId = !string.IsNullOrWhiteSpace(issue.EvidenceId)
                ? issue.EvidenceId!
                : issue.RelatedWorkItemId;
            if (string.IsNullOrWhiteSpace(evidenceId))
            {
                continue;
            }

            var requestKind = string.Equals(issue.Code, "orphan-evidence", StringComparison.OrdinalIgnoreCase)
                ? "GraphLinkage"
                : "ValidationIssue";
            Add(CreateSupportRequest(
                requestKind,
                evidenceId!,
                issue,
                nodeById,
                issue.Severity,
                issue.Message,
                issue.RelatedWorkItemId,
                issue.Severity == EvidenceValidationSeverityDto.Critical ? blockedOutput : null,
                keyQualifier: issue.Code,
                seen));
        }

        return requests
            .OrderBy(static request => SeverityRank(request.Severity))
            .ThenBy(static request => request.RequestKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static request => request.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static request => request.RequestId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        void Add(EvidenceSupportRequestDto? request)
        {
            if (request is not null)
            {
                requests.Add(request);
            }
        }
    }

    private static IReadOnlyList<EvidenceRequestListDto> BuildRequestLists(
        EvidenceSubjectDto subject,
        IReadOnlyList<EvidenceSupportRequestDto> requests)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        return requests
            .GroupBy(request => ResolveRequestListTarget(subject, request))
            .Select(group =>
            {
                var orderedRequests = group
                    .OrderBy(static request => SeverityRank(request.Severity))
                    .ThenBy(static request => request.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static request => request.RequestId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var requestIds = orderedRequests
                    .Select(static request => request.RequestId)
                    .ToArray();
                var evidenceKinds = orderedRequests
                    .Select(static request => request.EvidenceKind)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var blockedOutputs = orderedRequests
                    .Select(static request => request.BlockedOutput)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var highestSeverity = orderedRequests
                    .Select(static request => request.Severity)
                    .OrderBy(static severity => SeverityRank(severity))
                    .FirstOrDefault();
                var openCount = orderedRequests.Count(static request =>
                    !string.Equals(request.Status, "Closed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(request.Status, "Resolved", StringComparison.OrdinalIgnoreCase));
                var status = openCount == 0 ? "Resolved" : "Open";
                var target = group.Key;

                return new EvidenceRequestListDto(
                    RequestListId: $"request-list:{SanitizePathSegment(target.RequestListKind)}:{SanitizePathSegment(target.TargetKind)}:{SanitizePathSegment(target.TargetId)}",
                    RequestListKind: target.RequestListKind,
                    TargetKind: target.TargetKind,
                    TargetId: target.TargetId,
                    HighestSeverity: highestSeverity,
                    Status: status,
                    RequestCount: orderedRequests.Length,
                    RequestIds: requestIds,
                    EvidenceKinds: evidenceKinds,
                    BlockedOutputs: blockedOutputs,
                    Summary: BuildRequestListSummary(target, orderedRequests.Length, openCount))
                {
                    RequestListKindCode = target.RequestListKindCode
                };
            })
            .OrderBy(static list => RequestListKindRank(list.TargetKind))
            .ThenBy(static list => list.TargetId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static list => list.RequestListId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static EvidenceRequestListTarget ResolveRequestListTarget(
        EvidenceSubjectDto subject,
        EvidenceSupportRequestDto request)
    {
        var targetId = ResolveRequestListTargetId(subject, request);
        var requestCorpus = string.Join(
            " ",
            new[]
            {
                request.RequestKind,
                request.EvidenceId,
                request.EvidenceKind ?? string.Empty,
                request.Summary,
                request.SourceSystem ?? string.Empty,
                request.WorkItemId ?? string.Empty
            });
        var contextualCorpus = string.Join(
            " ",
            new[]
            {
                requestCorpus,
                subject.SubjectKind,
                subject.SubjectId,
                request.BlockedOutput ?? string.Empty
            });

        var target = ResolveRequestListTargetFromCorpus(requestCorpus, targetId);
        if (target is not null)
        {
            return target;
        }

        target = ResolveRequestListTargetFromCorpus(contextualCorpus, targetId);
        if (target is not null)
        {
            return target;
        }

        return new EvidenceRequestListTarget("EvidenceRequestList", EvidenceRequestListKindDto.Evidence, subject.SubjectKind, targetId);
    }

    private static EvidenceRequestListTarget? ResolveRequestListTargetFromCorpus(
        string corpus,
        string targetId)
    {
        if (ContainsAny(corpus, "tax", "k-1", "k1"))
        {
            return new EvidenceRequestListTarget("TaxRequestList", EvidenceRequestListKindDto.Tax, "tax", targetId);
        }

        if (ContainsAny(corpus, "audit", "auditor"))
        {
            return new EvidenceRequestListTarget("AuditRequestList", EvidenceRequestListKindDto.Audit, "audit", targetId);
        }

        if (ContainsAny(corpus, "fund-event", "capital-call", "distribution", "subscription", "redemption"))
        {
            return new EvidenceRequestListTarget("EventRequestList", EvidenceRequestListKindDto.OperationalEvent, "event", targetId);
        }

        if (ContainsAny(corpus, "report-pack", "report-package", "reporting", "delivery"))
        {
            return new EvidenceRequestListTarget("ReportPackageRequestList", EvidenceRequestListKindDto.ReportPackage, "report-package", targetId);
        }

        if (ContainsAny(corpus, "close", "nav-support", "period-lock"))
        {
            return new EvidenceRequestListTarget("CloseRequestList", EvidenceRequestListKindDto.Close, "close", targetId);
        }

        return null;
    }

    private static EvidenceRequestListKindDto ResolveRequestListKindCode(
        string? requestListKind,
        string? targetKind)
    {
        if (string.Equals(targetKind, "close", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestListKind, "CloseRequestList", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceRequestListKindDto.Close;
        }

        if (string.Equals(targetKind, "audit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestListKind, "AuditRequestList", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceRequestListKindDto.Audit;
        }

        if (string.Equals(targetKind, "tax", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestListKind, "TaxRequestList", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceRequestListKindDto.Tax;
        }

        if (string.Equals(targetKind, "report-package", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestListKind, "ReportPackageRequestList", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceRequestListKindDto.ReportPackage;
        }

        if (string.Equals(targetKind, "event", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestListKind, "EventRequestList", StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceRequestListKindDto.OperationalEvent;
        }

        return string.Equals(requestListKind, "EvidenceRequestList", StringComparison.OrdinalIgnoreCase)
            ? EvidenceRequestListKindDto.Evidence
            : EvidenceRequestListKindDto.Unknown;
    }

    private static string ResolveRequestListTargetId(EvidenceSubjectDto subject, EvidenceSupportRequestDto request)
    {
        var blockedOutput = request.BlockedOutput?.Trim();
        if (!string.IsNullOrWhiteSpace(blockedOutput))
        {
            var separator = blockedOutput.IndexOf('/');
            if (separator >= 0 && separator < blockedOutput.Length - 1)
            {
                return blockedOutput[(separator + 1)..];
            }

            return blockedOutput;
        }

        return subject.SubjectId;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildRequestListSummary(EvidenceRequestListTarget target, int requestCount, int openCount)
    {
        var requestLabel = requestCount == 1 ? "request" : "requests";
        var openLabel = openCount == 1 ? "1 open request" : $"{openCount} open requests";
        var verb = openCount == 1 ? "remains" : "remain";
        return $"{target.TargetKind}/{target.TargetId} has {requestCount} frozen {requestLabel}; {openLabel} {verb}.";
    }

    private static int RequestListKindRank(string targetKind)
        => targetKind switch
        {
            "event" => 0,
            "close" => 1,
            "audit" => 2,
            "tax" => 3,
            "report-package" => 4,
            _ => 5
        };

    private static EvidenceValidationIssueDto? FindIssue(
        IEnumerable<EvidenceValidationIssueDto> issues,
        string evidenceId,
        string code)
        => issues.FirstOrDefault(issue =>
            string.Equals(issue.EvidenceId, evidenceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(issue.Code, code, StringComparison.OrdinalIgnoreCase));

    private static EvidenceSupportRequestDto? CreateSupportRequest(
        string requestKind,
        string evidenceId,
        EvidenceValidationIssueDto? issue,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodeById,
        EvidenceValidationSeverityDto severity,
        string summary,
        string? workItemId,
        string? blockedOutput,
        string? keyQualifier,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(evidenceId))
        {
            return null;
        }

        var node = nodeById.GetValueOrDefault(evidenceId);
        var requestId = $"support-request:{SanitizePathSegment(requestKind)}:{SanitizePathSegment(evidenceId)}";
        if (!string.IsNullOrWhiteSpace(keyQualifier))
        {
            requestId = $"{requestId}:{SanitizePathSegment(keyQualifier)}";
        }

        if (!seen.Add(requestId))
        {
            return null;
        }

        return new EvidenceSupportRequestDto(
            RequestId: requestId,
            RequestKind: requestKind,
            EvidenceId: evidenceId,
            EvidenceKind: issue?.EvidenceKind ?? node?.Kind,
            Severity: severity,
            Status: "Open",
            Summary: summary,
            SourceSystem: issue?.SourceSystem ?? node?.SourceSystem,
            WorkItemId: workItemId,
            BlockedOutput: blockedOutput);
    }

    private static int SeverityRank(EvidenceValidationSeverityDto severity)
        => severity switch
        {
            EvidenceValidationSeverityDto.Critical => 0,
            EvidenceValidationSeverityDto.Warning => 1,
            _ => 2
        };

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static async Task<IntakeContent> ResolveIntakeContentAsync(
        EvidenceVaultIntakeRequestDto request,
        CancellationToken ct)
    {
        var source = request.IntakeSource;
        var sourceKind = source?.SourceKind ?? EvidenceDocumentIntakeSourceKindDto.UploadedContent;
        var expectedHash = FirstNonEmpty(request.ExpectedContentHashSha256, source?.ExpectedContentHashSha256);
        IntakeContent content = sourceKind switch
        {
            EvidenceDocumentIntakeSourceKindDto.UploadedContent => new(
                DecodeBase64(request.ContentBase64),
                SourcePath: null,
                SourceReference: FirstNonEmpty(source?.Uri, source?.Path, source?.DisplayName)),
            EvidenceDocumentIntakeSourceKindDto.LocalFile => await ReadLocalIntakeFileAsync(source, ct).ConfigureAwait(false),
            EvidenceDocumentIntakeSourceKindDto.ImportedFileReference => await ReadImportedIntakeFileAsync(source, ct).ConfigureAwait(false),
            EvidenceDocumentIntakeSourceKindDto.Email or
            EvidenceDocumentIntakeSourceKindDto.Sftp or
            EvidenceDocumentIntakeSourceKindDto.Api or
            EvidenceDocumentIntakeSourceKindDto.PortalDownload => ReadAdapterSeamIntakeContent(request, source, sourceKind),
            _ => throw new ArgumentException($"Evidence vault intake source kind '{sourceKind}' is not supported.", nameof(request))
        };

        if (content.Content.LongLength == 0)
        {
            throw new ArgumentException("Evidence vault intake content must not be empty.", nameof(request));
        }

        if (content.Content.LongLength > MaxRetainedArtifactBytes)
        {
            throw new ArgumentException("Evidence vault intake content exceeds the 100 MB vault artifact limit.", nameof(request));
        }

        var contentHash = Sha256Digest.Compute(content.Content);
        if (!string.IsNullOrWhiteSpace(expectedHash) &&
            !string.Equals(NormalizeHash(expectedHash), contentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Evidence vault intake content hash does not match the expected SHA-256 hash.", nameof(request));
        }

        return content with { ContentHashSha256 = contentHash };
    }

    private static async Task<IntakeContent> ReadLocalIntakeFileAsync(
        EvidenceDocumentIntakeSourceDto? source,
        CancellationToken ct)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Path))
        {
            throw new ArgumentException("Evidence vault local-file intake requires an intakeSource.path value.");
        }

        var fullPath = Path.GetFullPath(source.Path.Trim());
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new ArgumentException($"Evidence vault local-file intake source '{fullPath}' does not exist.");
        }

        if ((file.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
        {
            throw new ArgumentException("Evidence vault local-file intake source must be a file, not a directory.");
        }

        if (file.Length > MaxRetainedArtifactBytes)
        {
            throw new ArgumentException("Evidence vault local-file intake source exceeds the 100 MB vault artifact limit.");
        }

        var content = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        return new IntakeContent(
            content,
            SourcePath: fullPath,
            SourceReference: FirstNonEmpty(source.Uri, source.DisplayName, fullPath));
    }

    private static Task<IntakeContent> ReadImportedIntakeFileAsync(
        EvidenceDocumentIntakeSourceDto? source,
        CancellationToken ct)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Path))
        {
            throw new ArgumentException("Evidence vault imported-file intake requires an intakeSource.path value.");
        }

        ct.ThrowIfCancellationRequested();
        return ReadLocalIntakeFileAsync(source, ct);
    }

    private static IntakeContent ReadAdapterSeamIntakeContent(
        EvidenceVaultIntakeRequestDto request,
        EvidenceDocumentIntakeSourceDto? source,
        EvidenceDocumentIntakeSourceKindDto sourceKind)
    {
        if (string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            throw new ArgumentException(
                $"Evidence vault {sourceKind} intake is an adapter seam in v1 and requires contentBase64 supplied by the caller.",
                nameof(request));
        }

        return new IntakeContent(
            DecodeBase64(request.ContentBase64),
            SourcePath: NormalizeOptional(source?.Path),
            SourceReference: FirstNonEmpty(source?.Uri, source?.Path, source?.DisplayName));
    }

    private static byte[] DecodeBase64(string? contentBase64)
    {
        if (string.IsNullOrWhiteSpace(contentBase64))
        {
            throw new ArgumentException("Evidence vault intake content must be base64 encoded.", nameof(contentBase64));
        }

        try
        {
            return Convert.FromBase64String(contentBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Evidence vault intake content must be valid base64.", nameof(contentBase64), ex);
        }
    }

    private sealed record IntakeContent(
        byte[] Content,
        string? SourcePath,
        string? SourceReference)
    {
        public string ContentHashSha256 { get; init; } = string.Empty;
    }

    private static string BuildIntakeVaultId(
        string subjectKind,
        string subjectId,
        string fileName,
        string contentHash,
        DateTimeOffset capturedAt,
        string? tenantId,
        string? scope)
    {
        var seed = $"{tenantId}|{scope}|{subjectKind}|{subjectId}|{fileName}|{contentHash}|{capturedAt:O}";
        var hash = Sha256Digest.ComputeUtf8(seed);
        return $"ev-{hash[..24]}";
    }

    private static string BuildIntakeArtifactFileName(string fileName, string contentHash)
    {
        var submittedFileName = Path.GetFileName(fileName.Trim());
        var extension = Path.GetExtension(submittedFileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            extension = ".bin";
        }

        var stem = Path.GetFileNameWithoutExtension(submittedFileName);
        return $"{SanitizePathSegment(stem)}-{contentHash[..12]}{extension.ToLowerInvariant()}";
    }

    private static EvidenceStatusDto ResolveIntakeStatus(
        IReadOnlyCollection<EvidenceArtifactExtractionFieldDto> extractedFields)
    {
        if (extractedFields.Count == 0)
        {
            return EvidenceStatusDto.ReviewRequired;
        }

        if (extractedFields.Any(static field =>
                field.ValidationStatus is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing))
        {
            return EvidenceStatusDto.Blocked;
        }

        return extractedFields.Any(static field =>
            field.ValidationStatus is EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale or EvidenceStatusDto.Unknown)
            ? EvidenceStatusDto.ReviewRequired
            : EvidenceStatusDto.Ready;
    }

    private static EvidenceDocumentDto BuildEvidenceDocument(
        EvidenceVaultIntakeRequestDto request,
        string vaultId,
        string artifactId,
        string fileName,
        string contentHash,
        DateTimeOffset capturedAt,
        string manifestRoute,
        string? sourceReference,
        IReadOnlyCollection<EvidenceArtifactExtractionFieldDto> extractedFields)
    {
        var actor = FirstNonEmpty(request.Actor, request.ReceivedBy);
        var extractionStatus = request.ExtractionStatus ?? ResolveDocumentExtractionStatus(extractedFields);
        var reviewerState = NormalizeInitialReviewerState(
            request.ReviewerState ?? BuildDefaultReviewerState(extractionStatus),
            actor ?? "system",
            capturedAt);
        var objectLinks = BuildDocumentObjectLinks(request).ToArray();
        var channelKind = ResolveIntakeChannelKind(request, request.IntakeChannel);
        var tenantId = NormalizeOptional(request.TenantId);
        var scope = NormalizeOptional(request.Scope);
        var sourceSystem = NormalizeOptional(request.SourceSystem);
        var sourceReferenceValue = NormalizeOptional(sourceReference);
        var sourceRecord = new EvidenceDocumentSourceRecordDto(
            SourceHashSha256: contentHash,
            ReceivedAt: capturedAt,
            SourceChannel: request.IntakeChannel,
            ChannelKind: channelKind,
            Actor: actor,
            TenantId: tenantId,
            Scope: scope,
            SourceSystem: sourceSystem,
            SourceReference: sourceReferenceValue,
            ReceiptHash: contentHash);
        var auditTrail = new[]
        {
            new EvidenceDocumentAuditEventDto(
                RecordedAt: capturedAt,
                Actor: actor ?? "system",
                Action: "DocumentIntakeRetained",
                Summary: $"Retained {request.Classification} document '{fileName}' through {request.IntakeChannel} intake.",
                CorrelationId: vaultId)
        };

        return new EvidenceDocumentDto(
            DocumentId: $"doc:{vaultId}",
            FileName: fileName,
            Classification: request.Classification,
            SourceHashSha256: contentHash,
            ReceivedAt: capturedAt,
            SourceChannel: request.IntakeChannel,
            Actor: actor,
            TenantId: tenantId,
            Scope: scope,
            ExtractionStatus: extractionStatus,
            ObjectLinks: objectLinks,
            ReviewerState: reviewerState,
            AuditTrail: auditTrail)
        {
            ContentType = NormalizeOptional(request.ContentType),
            SourceSystem = sourceSystem,
            SourceReference = sourceReferenceValue,
            VaultId = vaultId,
            ArtifactId = artifactId,
            ManifestRoute = manifestRoute,
            ExtractorId = NormalizeOptional(request.ExtractorId),
            ChannelKind = channelKind,
            SourceRecord = sourceRecord,
            ExtractedFields = extractedFields.ToArray()
        };
    }

    private static EvidenceDocumentIntakeChannelDto ResolveIntakeChannelKind(
        EvidenceVaultIntakeRequestDto request,
        string intakeChannel)
    {
        if (request.IntakeChannelKind.HasValue && request.IntakeChannelKind.Value != EvidenceDocumentIntakeChannelDto.Unknown)
        {
            return request.IntakeChannelKind.Value;
        }

        return request.IntakeSource?.SourceKind switch
        {
            EvidenceDocumentIntakeSourceKindDto.LocalFile => EvidenceDocumentIntakeChannelDto.LocalFile,
            EvidenceDocumentIntakeSourceKindDto.ImportedFileReference => EvidenceDocumentIntakeChannelDto.ImportedFileReference,
            EvidenceDocumentIntakeSourceKindDto.Email => EvidenceDocumentIntakeChannelDto.Email,
            EvidenceDocumentIntakeSourceKindDto.Sftp => EvidenceDocumentIntakeChannelDto.Sftp,
            EvidenceDocumentIntakeSourceKindDto.Api => EvidenceDocumentIntakeChannelDto.Api,
            EvidenceDocumentIntakeSourceKindDto.PortalDownload => EvidenceDocumentIntakeChannelDto.PortalDownload,
            _ => ResolveIntakeChannelKind(intakeChannel)
        };
    }

    private static EvidenceDocumentIntakeChannelDto ResolveIntakeChannelKind(string intakeChannel)
    {
        var normalized = NormalizeChannelToken(intakeChannel);
        return normalized switch
        {
            "upload" or "uploadedcontent" or "uploaded" => EvidenceDocumentIntakeChannelDto.Upload,
            "email" or "mail" => EvidenceDocumentIntakeChannelDto.Email,
            "sftp" or "ftp" => EvidenceDocumentIntakeChannelDto.Sftp,
            "api" or "rest" => EvidenceDocumentIntakeChannelDto.Api,
            "portaldownload" or "portal" or "download" => EvidenceDocumentIntakeChannelDto.PortalDownload,
            "localfile" or "local" => EvidenceDocumentIntakeChannelDto.LocalFile,
            "importedfilereference" or "importedfile" or "import" => EvidenceDocumentIntakeChannelDto.ImportedFileReference,
            _ => EvidenceDocumentIntakeChannelDto.Unknown
        };
    }

    private static string NormalizeChannelToken(string value)
        => new(value
            .Trim()
            .Where(static c => char.IsLetterOrDigit(c))
            .Select(static c => char.ToLowerInvariant(c))
            .ToArray());

    private static EvidenceExtractionStatusDto ResolveDocumentExtractionStatus(
        IReadOnlyCollection<EvidenceArtifactExtractionFieldDto> extractedFields)
    {
        if (extractedFields.Count == 0)
        {
            return EvidenceExtractionStatusDto.NotExtracted;
        }

        if (extractedFields.Any(static field =>
                field.ValidationStatus is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing or EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale or EvidenceStatusDto.Unknown))
        {
            return EvidenceExtractionStatusDto.NeedsReview;
        }

        return EvidenceExtractionStatusDto.Extracted;
    }

    private static EvidenceDocumentReviewStateDto BuildDefaultReviewerState(
        EvidenceExtractionStatusDto extractionStatus)
        => extractionStatus switch
        {
            EvidenceExtractionStatusDto.Accepted => new(EvidenceDocumentReviewStatusDto.Accepted),
            EvidenceExtractionStatusDto.Rejected => new(EvidenceDocumentReviewStatusDto.Rejected),
            EvidenceExtractionStatusDto.NeedsReview => new(EvidenceDocumentReviewStatusDto.NeedsReview),
            _ => new(EvidenceDocumentReviewStatusDto.Unreviewed)
        };

    private static EvidenceDocumentReviewStateDto NormalizeInitialReviewerState(
        EvidenceDocumentReviewStateDto reviewerState,
        string actor,
        DateTimeOffset capturedAt)
    {
        var confirmedFields = NormalizeTrustedInitialConfirmedFields(
            reviewerState.ConfirmedFields,
            actor,
            reviewerState.ReviewedAt ?? capturedAt);
        if (reviewerState.Status == EvidenceDocumentReviewStatusDto.Accepted && confirmedFields.Count == 0)
        {
            throw new ArgumentException(
                "Accepted evidence vault intake reviewer state requires at least one human-confirmed field.",
                nameof(reviewerState));
        }

        return reviewerState with
        {
            ConfirmedFields = confirmedFields
        };
    }

    private static IEnumerable<EvidenceDocumentLinkDto> BuildDocumentObjectLinks(
        EvidenceVaultIntakeRequestDto request)
    {
        foreach (var link in request.ObjectLinks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(link.ObjectId))
            {
                yield return link;
            }
        }

        if (request.Linkage is null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(request.Linkage.PeriodId))
        {
            yield return new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Period, request.Linkage.PeriodId!, Relationship: "supports-period");
        }

        if (!string.IsNullOrWhiteSpace(request.Linkage.ReconciliationCaseId))
        {
            yield return new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.ReconciliationCase, request.Linkage.ReconciliationCaseId!, Relationship: "supports-reconciliation-case");
        }

        if (!string.IsNullOrWhiteSpace(request.Linkage.AccountingRecordId))
        {
            yield return new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Journal, request.Linkage.AccountingRecordId!, Relationship: "supports-accounting-record");
        }

        if (string.Equals(request.SubjectKind, "account", StringComparison.OrdinalIgnoreCase))
        {
            yield return new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Account, request.SubjectId, Relationship: "evidence-subject");
        }

        if (string.Equals(request.SubjectKind, "instrument", StringComparison.OrdinalIgnoreCase))
        {
            yield return new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Instrument, request.SubjectId, Relationship: "evidence-subject");
        }
    }

    private static int ResolveIntakeScore(EvidenceStatusDto status)
        => status switch
        {
            EvidenceStatusDto.Ready => 100,
            EvidenceStatusDto.ReviewRequired => 75,
            EvidenceStatusDto.Blocked => 50,
            _ => 0
        };

    private static IReadOnlyList<EvidenceValidationIssueDto> BuildIntakeValidationIssues(
        string artifactId,
        IReadOnlyCollection<EvidenceArtifactExtractionFieldDto> extractedFields,
        string sourceSystem)
    {
        if (extractedFields.Count == 0)
        {
            return
            [
                new EvidenceValidationIssueDto(
                    Code: "intake-extraction-required",
                    Severity: EvidenceValidationSeverityDto.Warning,
                    Message: "Evidence Vault intake has no extracted fields and requires review before it can support close evidence.",
                    EvidenceId: artifactId,
                    EvidenceKind: "vault-intake",
                    SourceSystem: sourceSystem,
                    RelatedWorkItemId: null)
            ];
        }

        return extractedFields
            .Where(static field => field.ValidationStatus != EvidenceStatusDto.Ready)
            .Select(field => new EvidenceValidationIssueDto(
                Code: $"intake-extraction-field-{SanitizePathSegment(field.FieldName)}",
                Severity: MapIntakeValidationSeverity(field.ValidationStatus),
                Message: BuildIntakeValidationMessage(field),
                EvidenceId: artifactId,
                EvidenceKind: "vault-intake",
                SourceSystem: sourceSystem,
                RelatedWorkItemId: null))
            .ToArray();
    }

    private static EvidenceValidationSeverityDto MapIntakeValidationSeverity(EvidenceStatusDto status)
        => status is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing
            ? EvidenceValidationSeverityDto.Critical
            : EvidenceValidationSeverityDto.Warning;

    private static string BuildIntakeValidationMessage(EvidenceArtifactExtractionFieldDto field)
    {
        var fieldName = string.IsNullOrWhiteSpace(field.FieldName) ? "unclassified" : field.FieldName.Trim();
        var detail = string.IsNullOrWhiteSpace(field.ValidationMessage)
            ? $"Evidence Vault intake field '{fieldName}' is {field.ValidationStatus}."
            : field.ValidationMessage.Trim();
        var actual = string.IsNullOrWhiteSpace(field.ExtractedValue)
            ? "no extracted value"
            : $"extracted '{field.ExtractedValue.Trim()}'";
        var expected = string.IsNullOrWhiteSpace(field.ExpectedValue)
            ? "no expected value"
            : $"expected '{field.ExpectedValue.Trim()}'";
        var linkedRecord = string.IsNullOrWhiteSpace(field.LinkedRecordKind) || string.IsNullOrWhiteSpace(field.LinkedRecordId)
            ? "no linked record"
            : $"{field.LinkedRecordKind.Trim()}/{field.LinkedRecordId.Trim()}";
        return $"{detail} {actual}; {expected}; {linkedRecord}.";
    }

    private static string BuildIntakeSummary(string fileName, string intakeChannel, string? sourceSystem)
    {
        var source = NormalizeOptional(sourceSystem);
        return source is null
            ? $"Captured '{fileName}' through Evidence Vault {intakeChannel} intake."
            : $"Captured '{fileName}' through Evidence Vault {intakeChannel} intake from {source}.";
    }

    private static EvidenceSubjectLinkageDto ResolveIntakeLinkage(
        string subjectKind,
        string subjectId,
        EvidenceSubjectLinkageDto? linkage)
        => linkage ?? new EvidenceSubjectLinkageDto(
            $"{subjectKind}/{subjectId}",
            null,
            null,
            null,
            null,
            string.Equals(subjectKind, EvidenceSubjectResolver.AccountingRecordKind, StringComparison.OrdinalIgnoreCase)
                ? subjectId
                : null,
            string.Equals(subjectKind, EvidenceSubjectResolver.ReportPackDeliveryKind, StringComparison.OrdinalIgnoreCase)
                ? ResolveDeliveryAttemptId(subjectId)
                : null,
            null);

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(ch => invalid.Contains(ch) || ch is '/' or '\\' or ':' ? '-' : char.ToLowerInvariant(ch))
            .ToArray();
        var sanitized = new string(chars);
        return string.IsNullOrWhiteSpace(sanitized) || IsReservedPathSegment(sanitized)
            ? "unknown"
            : sanitized;
    }

    private static string? ValidatePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var segment = value.Trim();
        if (IsReservedPathSegment(segment)
            || !string.Equals(Path.GetFileName(segment), segment, StringComparison.Ordinal)
            || segment.Contains('/')
            || segment.Contains('\\')
            || segment.Contains(':')
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        return segment;
    }

    private static bool IsReservedPathSegment(string value)
        => string.Equals(value, ".", StringComparison.Ordinal)
           || string.Equals(value, "..", StringComparison.Ordinal);

    private static string? ValidateManifestFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var fileName = value.Trim();
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.Contains(':')
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !fileName.EndsWith("-manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fileName;
    }

    private async Task WriteVaultIndexAsync(EvidenceVaultIdentityDto identity, CancellationToken ct)
    {
        var indexPath = Path.Combine(_rootDirectory, "_vault", $"{identity.VaultId}.json");
        await AtomicFileWriter
            .WriteAsync(indexPath, JsonSerializer.Serialize(identity, _jsonOptions), ct)
            .ConfigureAwait(false);
    }

    private async Task<EvidenceVaultIdentityDto?> TryReadVaultIdentityAsync(
        string indexPath,
        CancellationToken ct)
    {
        if (!File.Exists(indexPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                indexPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            return await JsonSerializer
                .DeserializeAsync<EvidenceVaultIdentityDto>(stream, _jsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Evidence vault index '{IndexPath}' could not be deserialized.", indexPath);
            return null;
        }
        catch (IOException ex)
        {
            // A locked or transiently unreadable file must skip this entry, not fail the
            // whole vault listing.
            _logger.LogWarning(ex, "Evidence vault index '{IndexPath}' could not be read.", indexPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Evidence vault index '{IndexPath}' could not be accessed.", indexPath);
            return null;
        }
    }

    private async Task<RetainedEvidenceManifestDto?> TryReadRetainedManifestAsync(
        string manifestPath,
        CancellationToken ct)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            return await JsonSerializer
                .DeserializeAsync<RetainedEvidenceManifestDto>(stream, _jsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Evidence vault manifest '{ManifestPath}' could not be deserialized.", manifestPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Evidence vault manifest '{ManifestPath}' could not be read.", manifestPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Evidence vault manifest '{ManifestPath}' could not be accessed.", manifestPath);
            return null;
        }
    }

    private string? ResolveSubjectManifestPath(
        string subjectKind,
        string subjectId,
        string fileName)
    {
        var safeSubjectKind = ValidatePathSegment(subjectKind);
        var safeSubjectId = ValidatePathSegment(subjectId);
        var safeFileName = ValidateManifestFileName(fileName);
        if (safeSubjectKind is null || safeSubjectId is null || safeFileName is null)
        {
            return null;
        }

        try
        {
            var directory = Path.GetFullPath(Path.Combine(
                _rootDirectory,
                safeSubjectKind,
                safeSubjectId));
            var filePath = Path.GetFullPath(Path.Combine(directory, safeFileName));
            var directoryPrefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return filePath.StartsWith(directoryPrefix, PathComparison) && IsUnderRoot(filePath, _rootDirectory)
                ? filePath
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

    private string? ResolveVaultManifestPath(EvidenceVaultIdentityDto identity, string expectedVaultId)
    {
        if (!string.Equals(identity.VaultId, expectedVaultId, StringComparison.OrdinalIgnoreCase) ||
            identity.SchemaVersion <= 0 ||
            !string.Equals(identity.StorageKind, FileManifestStorageKind, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(identity.StorageKind, FileBundleStorageKind, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var manifestPath = identity.ManifestPath.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(manifestPath) ||
            Path.IsPathRooted(manifestPath) ||
            !manifestPath.StartsWith(ManifestRelativeRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativeToEvidenceRoot = manifestPath[ManifestRelativeRoot.Length..];
        var segments = relativeToEvidenceRoot.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var hasVaultLayout = segments.Length == 3
                             && string.Equals(segments[0], "_vault", StringComparison.OrdinalIgnoreCase)
                             && string.Equals(segments[1], expectedVaultId, StringComparison.OrdinalIgnoreCase);
        var hasSubjectLayout = segments.Length == 3
                               && string.Equals(
                                   segments[0],
                                   SanitizePathSegment(identity.SubjectKind),
                                   StringComparison.OrdinalIgnoreCase)
                               && string.Equals(
                                   segments[1],
                                   SanitizePathSegment(identity.SubjectId),
                                   StringComparison.OrdinalIgnoreCase);
        if ((!hasVaultLayout && !hasSubjectLayout) ||
            segments.Any(static segment => segment is "." or "..") ||
            segments.Length != relativeToEvidenceRoot.Split('/').Length ||
            ValidateManifestFileName(segments[^1]) is null)
        {
            return null;
        }

        try
        {
            var filePath = Path.GetFullPath(Path.Combine(
                _rootDirectory,
                Path.Combine(segments)));
            return IsUnderRoot(filePath, _rootDirectory) ? filePath : null;
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

    private static EvidenceManifestFile? OpenManifestFile(string? filePath)
    {
        if (filePath is null || !File.Exists(filePath))
        {
            return null;
        }

        var info = new FileInfo(filePath);
        Stream stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        return new EvidenceManifestFile(
            stream,
            "application/json",
            info.Name,
            new DateTimeOffset(info.LastWriteTimeUtc));
    }

    private async Task<IReadOnlyList<EvidenceVaultArtifactDto>> RetainLocalArtifactsAsync(
        EvidencePacketDto packet,
        string vaultId,
        DateTimeOffset retainedAt,
        CancellationToken ct)
    {
        var retainedArtifacts = packet.Nodes
            .SelectMany(static node => node.ArtifactRefs)
            .Where(static artifact => artifact.Retained && !string.IsNullOrWhiteSpace(artifact.Path))
            .GroupBy(static artifact => artifact.ArtifactId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (retainedArtifacts.Length == 0)
        {
            return [];
        }

        var artifactDirectory = Path.Combine(_rootDirectory, "_vault", vaultId, "artifacts");
        var copied = new List<EvidenceVaultArtifactDto>(retainedArtifacts.Length);
        foreach (var artifact in retainedArtifacts)
        {
            ct.ThrowIfCancellationRequested();
            ValidateRetainedArtifactLinkage(artifact);
            var sourcePath = ResolveRetainableArtifactSourcePath(artifact.Path);
            if (sourcePath is null)
            {
                throw new InvalidOperationException($"Retained artifact '{artifact.ArtifactId}' has an invalid source path.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Retained artifact '{artifact.ArtifactId}' source file was not found.", sourcePath);
            }

            var fileInfo = new FileInfo(sourcePath);
            if (fileInfo.Length > MaxRetainedArtifactBytes)
            {
                throw new InvalidOperationException($"Retained artifact '{artifact.ArtifactId}' exceeds the 100 MB vault artifact limit.");
            }

            var bytes = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
            var hash = Sha256Digest.Compute(bytes);
            if (!string.IsNullOrWhiteSpace(artifact.Hash) &&
                !string.Equals(NormalizeHash(artifact.Hash), hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Retained artifact '{artifact.ArtifactId}' hash does not match source content.");
            }

            var safeFileName = BuildArtifactFileName(artifact, sourcePath);
            var targetPath = Path.Combine(artifactDirectory, safeFileName);
            await AtomicFileWriter.WriteAsync(targetPath, bytes, ct).ConfigureAwait(false);
            copied.Add(new EvidenceVaultArtifactDto(
                ArtifactId: artifact.ArtifactId,
                Kind: artifact.Kind,
                RelativePath: Path.Combine("workstation", "evidence", "_vault", vaultId, "artifacts", safeFileName)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                ContentHashSha256: hash,
                SizeBytes: bytes.LongLength,
                RetainedAt: retainedAt,
                SourcePath: sourcePath,
                SourceRoute: artifact.Route,
                CanonicalSubjectKind: artifact.CanonicalSubjectKind,
                CanonicalSubjectId: artifact.CanonicalSubjectId)
            {
                Capture = artifact.Capture,
                ExtractedFields = artifact.ExtractedFields
            });
        }

        return copied
            .OrderBy(static artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateRetainedArtifactReferences(EvidencePacketDto packet)
    {
        foreach (var artifact in packet.Nodes.SelectMany(static node => node.ArtifactRefs))
        {
            if (!artifact.Retained)
            {
                continue;
            }

            ValidateRetainedArtifactLinkage(artifact);
            if (string.IsNullOrWhiteSpace(artifact.Path) && string.IsNullOrWhiteSpace(artifact.Route))
            {
                throw new InvalidOperationException(
                    $"Retained artifact '{artifact.ArtifactId}' must have a source path or route.");
            }
        }
    }

    private static void ValidateRetainedArtifactLinkage(EvidenceArtifactRefDto artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.CanonicalSubjectKind) ||
            string.IsNullOrWhiteSpace(artifact.CanonicalSubjectId))
        {
            throw new InvalidOperationException(
                $"Retained artifact '{artifact.ArtifactId}' is missing canonical subject linkage.");
        }

        if (!SupportedCanonicalSubjectKinds.Contains(artifact.CanonicalSubjectKind.Trim()))
        {
            throw new InvalidOperationException(
                $"Retained artifact '{artifact.ArtifactId}' links to unsupported canonical subject kind '{artifact.CanonicalSubjectKind}'.");
        }
    }

    private static string? ResolveRetainableArtifactSourcePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
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

    private static string BuildArtifactFileName(EvidenceArtifactRefDto artifact, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            extension = ".bin";
        }

        var stem = SanitizePathSegment(string.IsNullOrWhiteSpace(artifact.Kind) ? artifact.ArtifactId : artifact.Kind);
        var artifactHash = Sha256Digest.ComputeUtf8(artifact.ArtifactId)[..12];
        return $"{stem}-{artifactHash}{extension}";
    }

    private static string NormalizeHash(string hash)
    {
        var trimmed = hash.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
    }

    private static string? ValidateVaultId(string value)
        => EvidenceVaultReference.TryNormalizeVaultId(value, out var normalized)
            ? normalized
            : null;

    private static string RouteSegment(string value)
        => Uri.EscapeDataString(value);

    private static bool IsUnderRoot(string path, string root)
    {
        var rootPath = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(rootPath, PathComparison);
    }

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
