using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
}

public sealed record EvidenceManifestFile(
    Stream Content,
    string ContentType,
    string FileName,
    DateTimeOffset LastModified);

public sealed class FileEvidenceArtifactStore : IEvidenceArtifactStore
{
    private readonly string _rootDirectory;
    private readonly ILogger<FileEvidenceArtifactStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
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
            VaultIdentity: null);
        var preimage = JsonSerializer.Serialize(manifest, _jsonOptions);
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preimage))).ToLowerInvariant();
        var vaultIdentity = new EvidenceVaultIdentityDto(
            VaultId: $"ev-{contentHash[..24]}",
            SubjectKind: packet.Subject.SubjectKind,
            SubjectId: packet.Subject.SubjectId,
            ManifestPath: relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            ManifestRoute: manifestRoute,
            RetainedAt: generatedAt,
            ContentHashSha256: contentHash,
            SchemaVersion: 1,
            StorageKind: "file-manifest");
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

        var safeFileName = ValidateManifestFileName(fileName);
        if (safeFileName is null)
        {
            return Task.FromResult<EvidenceManifestFile?>(null);
        }

        var directory = Path.GetFullPath(Path.Combine(
            _rootDirectory,
            SanitizePathSegment(subjectKind),
            SanitizePathSegment(subjectId)));
        var filePath = Path.GetFullPath(Path.Combine(directory, safeFileName));
        var directoryPrefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!filePath.StartsWith(directoryPrefix, PathComparison) || !File.Exists(filePath))
        {
            return Task.FromResult<EvidenceManifestFile?>(null);
        }

        var info = new FileInfo(filePath);
        Stream stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        return Task.FromResult<EvidenceManifestFile?>(new EvidenceManifestFile(
            stream,
            "application/json",
            info.Name,
            new DateTimeOffset(info.LastWriteTimeUtc)));
    }

    public async Task<EvidenceManifestFile?> TryOpenManifestByVaultIdAsync(
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
        if (!File.Exists(indexPath))
        {
            return null;
        }

        await using var stream = new FileStream(
            indexPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        var identity = await JsonSerializer
            .DeserializeAsync<EvidenceVaultIdentityDto>(stream, _jsonOptions, ct)
            .ConfigureAwait(false);
        if (identity is null)
        {
            return null;
        }

        var fileName = Path.GetFileName(identity.ManifestPath);
        return await TryOpenManifestAsync(identity.SubjectKind, identity.SubjectId, fileName, ct)
            .ConfigureAwait(false);
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
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

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
        EvidenceVaultIdentityDto? VaultIdentity);
}
