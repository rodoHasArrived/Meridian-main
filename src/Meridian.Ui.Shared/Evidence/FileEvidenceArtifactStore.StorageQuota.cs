using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Documents;
using Meridian.Ui.Shared.Serialization;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    private WorkstationOperationsJsonContext? _quotaIndexJsonContext;
    private sealed record StoragePublicationIntent(string PackagePath, string ManifestPath, string IndexPath);

    [JsonSerializable(typeof(StoragePublicationIntent))]
    private sealed partial class StoragePublicationJsonContext : JsonSerializerContext
    {
    }

    private (long Bytes, int Count) EstimateExportStorage(EvidencePacketDto packet)
    {
        var artifacts = packet.Nodes.SelectMany(static node => node.ArtifactRefs)
            .Where(static artifact => artifact.Retained && !string.IsNullOrWhiteSpace(artifact.Path))
            .DistinctBy(static artifact => artifact.ArtifactId, StringComparer.OrdinalIgnoreCase).ToArray();
        long bytes = 0;
        foreach (var artifact in artifacts)
        {
            var path = ResolveRetainableArtifactSourcePath(artifact.Path);
            if (path is null)
            {
                throw new InvalidOperationException($"Retained artifact '{artifact.ArtifactId}' has an invalid source path.");
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Retained artifact '{artifact.ArtifactId}' source file was not found.", path);
            }
            var length = new FileInfo(path).Length;
            if (length > MaxRetainedArtifactBytes)
            {
                throw new InvalidOperationException($"Retained artifact '{artifact.ArtifactId}' exceeds the 100 MB vault artifact limit.");
            }
            CheckConfiguredArtifactLimit(length);
            bytes = checked(bytes + length);
        }
        return (bytes, artifacts.Length);
    }

    private long EstimateIntakeStorage(EvidenceVaultIntakeRequestDto request)
    {
        long length;
        if (request.IntakeSource?.SourceKind is EvidenceDocumentIntakeSourceKindDto.LocalFile
            or EvidenceDocumentIntakeSourceKindDto.ImportedFileReference)
        {
            var path = request.IntakeSource.Path;
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            length = new FileInfo(Path.GetFullPath(path.Trim())).Length;
        }
        else
        {
            // Base64 may contain whitespace. Count its decoded size without allocating the
            // content; the existing decoder remains responsible for full format validation.
            var encoded = request.ContentBase64 ?? string.Empty;
            long symbols = 0;
            var padding = 0;
            foreach (var character in encoded)
            {
                if (!char.IsWhiteSpace(character))
                {
                    symbols++;
                    if (character == '=')
                    {
                        padding++;
                    }
                }
            }
            length = Math.Max(0, checked((symbols + 3) / 4 * 3) - Math.Min(padding, 2));
        }
        CheckConfiguredArtifactLimit(length);
        return length;
    }

    private void CheckConfiguredArtifactLimit(long length)
    {
        if (length > _quotaOptions.MaxArtifactBytes)
        {
            throw new EvidenceStorageQuotaExceededException("artifact-bytes", "Evidence exceeds the configured artifact byte limit.");
        }
    }

    private long MeasurePublishedTenantBytes(string tenantId)
    {
        var vault = Path.Combine(_rootDirectory, "_vault");
        if (!Directory.Exists(vault))
        {
            return 0;
        }
        long bytes = 0;
        // Quota usage is measured under the coordinator gate; reuse metadata with the exact
        // retained-index options, independently of whether legacy writes already froze them.
        var jsonContext = _quotaIndexJsonContext ??=
            new WorkstationOperationsJsonContext(new JsonSerializerOptions(_jsonOptions));
        foreach (var index in Directory.EnumerateFiles(vault, "*.json", SearchOption.TopDirectoryOnly))
        {
            // Fail closed on unreadable indexes: treating corrupt evidence as zero usage would
            // admit new writes against a budget whose retained usage is unknown.
            var identity = JsonSerializer.Deserialize(File.ReadAllText(index), jsonContext.EvidenceVaultIdentityDto)
                ?? throw new InvalidDataException("Evidence quota could not read a published index.");
            if (!string.Equals(identity.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            {
                index,
                ResolveQuotaEvidencePath(identity.ManifestPath)
            };
            foreach (var artifact in identity.Artifacts)
            {
                files.Add(ResolveQuotaEvidencePath(artifact.RelativePath));
            }
            foreach (var path in files)
            {
                bytes = checked(bytes + new FileInfo(path).Length);
            }
        }
        return bytes;
    }

    private string ResolveQuotaEvidencePath(string relativePath)
    {
        if (!relativePath.StartsWith(ManifestRelativeRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Evidence quota found an invalid retained path.");
        }
        return RequireQuotaChildPath(Path.Combine(_rootDirectory, relativePath[ManifestRelativeRoot.Length..]));
    }

    private string RequireQuotaChildPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(_rootDirectory + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException("Evidence quota found a path outside its storage root.");
        }
        return fullPath;
    }

    private void EnsurePublicationTargetsUnclaimed(string attemptId, params string[] targets)
    {
        // A failed cleanup may have deleted final files but retained its durable intent. Do not
        // let another attempt reuse those targets until that intent has been fully reclaimed.
        var staging = Path.Combine(_rootDirectory, "_staging");
        foreach (var directory in Directory.EnumerateDirectories(staging))
        {
            if (Path.GetFileName(directory) == attemptId)
            {
                continue;
            }
            var intentPath = Path.Combine(directory, "publication.json");
            if (!File.Exists(intentPath))
            {
                continue;
            }
            var intent = JsonSerializer.Deserialize(File.ReadAllText(intentPath),
                StoragePublicationJsonContext.Default.StoragePublicationIntent)
                ?? throw new InvalidDataException("Invalid evidence publication intent.");
            var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (targets.Intersect([intent.PackagePath, intent.ManifestPath, intent.IndexPath], comparison).Any())
            {
                throw new IOException("Evidence publication targets are still owned by an earlier attempt.");
            }
        }
    }

    private Task<bool> RecoverStorageAttemptAsync(string attemptId, CancellationToken ct)
    {
        // The coordinator holds both the root lock and this attempt's ownership lease. No TTL
        // or PID heuristic may authorize cleanup of a live writer's private staging.
        if (!Guid.TryParseExact(attemptId, "N", out _))
        {
            throw new InvalidDataException("Invalid evidence storage attempt identifier.");
        }
        var stage = Path.Combine(_rootDirectory, "_staging", attemptId);
        try
        {
            if (!Directory.Exists(stage))
            {
                return Task.FromResult(true);
            }
            var intentPath = Path.Combine(stage, "publication.json");
            if (File.Exists(intentPath))
            {
                var intent = JsonSerializer.Deserialize(File.ReadAllText(intentPath),
                    StoragePublicationJsonContext.Default.StoragePublicationIntent)
                    ?? throw new InvalidDataException("Invalid evidence publication intent.");
                var package = RequireQuotaChildPath(intent.PackagePath);
                var manifest = RequireQuotaChildPath(intent.ManifestPath);
                var index = RequireQuotaChildPath(intent.IndexPath);
                if (Path.GetDirectoryName(package) != Path.Combine(_rootDirectory, "_vault")
                    || index != package + ".json")
                {
                    throw new InvalidDataException("Invalid evidence publication targets.");
                }
                // Index existence is deliberately conservative. Never dismantle evidence that
                // crossed the publication boundary, even if the index later became unreadable.
                if (!File.Exists(index))
                {
                    File.Delete(manifest);
                    if (Directory.Exists(package))
                    {
                        Directory.Delete(package, recursive: true);
                    }
                }
            }
            Directory.Delete(stage, recursive: true);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Evidence storage recovery retained reservation for attempt {AttemptId} after cleanup failed.", attemptId);
            return Task.FromResult(false);
        }
    }
}
