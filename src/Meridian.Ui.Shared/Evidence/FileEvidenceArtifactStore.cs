using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public interface IEvidenceArtifactStore
{
    Task<EvidencePacketExportResponse> WriteManifestAsync(
        EvidencePacketDto packet,
        EvidencePacketExportRequest request,
        CancellationToken ct = default);

    Task<EvidenceManifestFile?> TryOpenManifestAsync(
        string subjectKind,
        string subjectId,
        string fileName,
        CancellationToken ct = default);

    Task<EvidenceManifestFile?> TryOpenManifestByVaultIdAsync(
        string vaultId,
        CancellationToken ct = default);

    Task<EvidenceVaultIdentityDto?> TryGetVaultIdentityAsync(
        string vaultId,
        CancellationToken ct = default);

    Task<IReadOnlyList<EvidenceVaultIdentityDto>> FindByLinkageAsync(
        EvidenceVaultLookupRequestDto request,
        CancellationToken ct = default);
}

public sealed record EvidenceManifestFile(
    Stream Content,
    string ContentType,
    string FileName,
    DateTimeOffset LastModified);

public sealed class FileEvidenceArtifactStore : IEvidenceArtifactStore
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
        EvidenceSubjectResolver.StrategyRunKind,
        EvidenceSubjectResolver.PaperReadinessKind,
        EvidenceSubjectResolver.ReconciliationReviewKind,
        EvidenceSubjectResolver.ReportPackKind,
        EvidenceSubjectResolver.ProviderTrustKind,
        EvidenceSubjectResolver.AnalysisExportKind,
        EvidenceSubjectResolver.SecurityMasterConflictKind,
        EvidenceSubjectResolver.ApprovalKind
    };

    private readonly string _rootDirectory;
    private readonly ILogger<FileEvidenceArtifactStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileEvidenceArtifactStore(string dataRoot, ILogger<FileEvidenceArtifactStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootDirectory = Path.Combine(dataRoot, "workstation", "evidence");
    }

    public async Task<EvidencePacketExportResponse> WriteManifestAsync(
        EvidencePacketDto packet,
        EvidencePacketExportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        request ??= new EvidencePacketExportRequest(null, null);

        var generatedAt = DateTimeOffset.UtcNow;
        var subjectKind = SanitizePathSegment(packet.Subject.SubjectKind);
        var subjectId = SanitizePathSegment(packet.Subject.SubjectId);
        var fileName = $"{generatedAt:yyyyMMddTHHmmssfffZ}-manifest.json";
        var directory = Path.Combine(_rootDirectory, subjectKind, subjectId);
        var manifestPath = Path.Combine(directory, fileName);
        var relativePath = Path.Combine("workstation", "evidence", subjectKind, subjectId, fileName);
        var manifestRoute = $"/workstation/evidence/{RouteSegment(subjectKind)}/{RouteSegment(subjectId)}/{RouteSegment(fileName)}";
        var manifest = new EvidenceManifestDto(
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
            VaultIdentity: null,
            Lifecycle: request.Lifecycle,
            Linkage: request.Linkage);
        ValidateRetainedArtifactReferences(packet);
        var retainedExportJson = JsonSerializer.Serialize(manifest, _jsonOptions);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(retainedExportJson))).ToLowerInvariant();
        var vaultId = $"ev-{contentHash[..24]}";
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
            Artifacts = retainedArtifacts
        };
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

    public Task<EvidenceManifestFile?> TryOpenManifestAsync(
        string subjectKind,
        string subjectId,
        string fileName,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var filePath = ResolveSubjectManifestPath(subjectKind, subjectId, fileName);
        return Task.FromResult(OpenManifestFile(filePath));
    }

    public async Task<EvidenceManifestFile?> TryOpenManifestByVaultIdAsync(
        string vaultId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var identity = await TryGetVaultIdentityAsync(vaultId, ct).ConfigureAwait(false);
        var manifestPath = identity is null
            ? null
            : ResolveVaultManifestPath(identity, identity.VaultId);
        if (manifestPath is null)
        {
            return null;
        }

        return OpenManifestFile(manifestPath);
    }

    public async Task<EvidenceVaultIdentityDto?> TryGetVaultIdentityAsync(
        string vaultId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var safeVaultId = ValidateVaultId(vaultId);
        if (safeVaultId is null)
        {
            return null;
        }

        var indexPath = Path.Combine(_rootDirectory, "_vault", $"{safeVaultId}.json");
        return await TryReadVaultIdentityAsync(indexPath, ct).ConfigureAwait(false);
    }

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
            if (identity is null)
            {
                continue;
            }

            var manifestPath = ResolveVaultManifestPath(identity, identity.VaultId);
            if (manifestPath is null || !File.Exists(manifestPath))
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

    private static bool MatchesLookup(EvidenceVaultLookupRequestDto request, EvidenceSubjectLinkageDto? linkage, EvidenceVaultIdentityDto identity)
    {
        if (!string.IsNullOrWhiteSpace(request.EvidenceSubject) && !string.Equals(request.EvidenceSubject, linkage?.EvidenceSubject, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.RunId) && !string.Equals(request.RunId, linkage?.RunId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.PeriodId) && !string.Equals(request.PeriodId, linkage?.PeriodId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.ReportPackId) && !string.Equals(request.ReportPackId, linkage?.ReportPackId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(request.ReconciliationCaseId) && !string.Equals(request.ReconciliationCaseId, linkage?.ReconciliationCaseId, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private async Task<EvidenceSubjectLinkageDto?> TryReadLinkageAsync(string manifestPath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<EvidenceManifestDto>(stream, _jsonOptions, ct).ConfigureAwait(false);
        return manifest?.Linkage;
    }

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
        if (segments.Length < 3 ||
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
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
                CanonicalSubjectId: artifact.CanonicalSubjectId));
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
        var artifactHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.ArtifactId))).ToLowerInvariant()[..12];
        return $"{stem}-{artifactHash}{extension}";
    }

    private static string NormalizeHash(string hash)
    {
        var trimmed = hash.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
    }

    private static string? ValidateVaultId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!normalized.StartsWith("ev-", StringComparison.Ordinal) || normalized.Length != 27)
        {
            return null;
        }

        return normalized[3..].All(static ch => ch is (>= '0' and <= '9') or (>= 'a' and <= 'f'))
            ? normalized
            : null;
    }

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

    private sealed record EvidenceManifestDto(
        int SchemaVersion,
        DateTimeOffset ExportedAt,
        string? RequestedBy,
        string? Reason,
        bool ManifestOnly,
        EvidenceSubjectDto Subject,
        EvidenceCompletenessDto Completeness,
        IReadOnlyList<EvidenceNodeDto> Nodes,
        IReadOnlyList<EvidenceEdgeDto> Edges,
        IReadOnlyList<WorkflowActionDto> Actions,
        IReadOnlyList<string> Warnings,
        EvidenceVaultIdentityDto? VaultIdentity,
        EvidenceLifecycleMetadataDto? Lifecycle,
        EvidenceSubjectLinkageDto? Linkage);
}
