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
            Warnings: request.IncludeWarnings ? packet.Warnings : []);

        await AtomicFileWriter
            .WriteAsync(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions), ct)
            .ConfigureAwait(false);

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
            ManifestRoute: $"/workstation/evidence/{RouteSegment(subjectKind)}/{RouteSegment(subjectId)}/{RouteSegment(fileName)}",
            EvidenceCount: packet.Nodes.Count,
            WarningCount: request.IncludeWarnings ? packet.Warnings.Count : 0,
            Retained: true);
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
        IReadOnlyList<string> Warnings);
}
