using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Setup;

internal sealed record PayloadSource(string RelativePath, Func<Stream> OpenRead);

internal sealed record PayloadManifestEntry(string RelativePath, long Length, string Sha256);

internal sealed record InstallationManifest(
    int SchemaVersion,
    string ProductVersion,
    string Runtime,
    DateTimeOffset StagedAtUtc,
    List<PayloadManifestEntry> Files);

internal sealed record StagedInstallation(string Path, InstallationManifest Manifest);

/// <summary>
/// Builds and verifies a complete sibling installation before atomically promoting it. The
/// previous complete directory is retained as one-generation rollback state.
/// </summary>
internal static class InstallationTransaction
{
    internal const string ManifestFileName = ".meridian-install-manifest.json";
    private const string StagePrefix = ".Meridian.stage-";
    private const string RollbackSuffix = ".rollback";
    private const int ManifestSchemaVersion = 1;

    internal static StagedInstallation Stage(
        string installRoot,
        string productVersion,
        string runtime,
        IReadOnlyCollection<PayloadSource> payload,
        string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtime);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        var resolvedRoot = Path.GetFullPath(installRoot);
        var parent = Directory.GetParent(resolvedRoot)?.FullName
            ?? throw new InvalidOperationException("The Meridian installation root must have a parent directory.");
        Directory.CreateDirectory(parent);
        var stagePath = Path.Combine(
            parent,
            $"{StagePrefix}{SanitizeSegment(productVersion)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagePath);

        try
        {
            var entries = new List<PayloadManifestEntry>(payload.Count + 1);
            var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in payload.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
            {
                var relativePath = NormalizeRelativePath(source.RelativePath);
                if (!relativePaths.Add(relativePath))
                {
                    throw new InvalidDataException($"The installer payload contains duplicate path '{relativePath}'.");
                }

                using var input = source.OpenRead()
                    ?? throw new InvalidDataException($"The installer payload is missing '{relativePath}'.");
                entries.Add(WritePayloadFile(stagePath, relativePath, input));
            }

            const string setupFileName = "Meridian-Setup.exe";
            if (!relativePaths.Add(setupFileName))
            {
                throw new InvalidDataException(
                    $"The product payload must not provide reserved installer path '{setupFileName}'.");
            }

            using (var setup = new FileStream(
                       Path.GetFullPath(installerPath),
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                entries.Add(WritePayloadFile(stagePath, setupFileName, setup));
            }

            if (!relativePaths.Contains("Meridian.exe"))
            {
                throw new InvalidDataException("The signed installer payload does not contain Meridian.exe.");
            }

            var manifest = new InstallationManifest(
                ManifestSchemaVersion,
                productVersion,
                runtime,
                DateTimeOffset.UtcNow,
                entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToList());
            WriteDurableText(
                Path.Combine(stagePath, ManifestFileName),
                JsonSerializer.Serialize(manifest, SetupJsonContext.Default.InstallationManifest));
            Verify(new StagedInstallation(stagePath, manifest));
            return new StagedInstallation(stagePath, manifest);
        }
        catch
        {
            TryDeleteDirectory(stagePath);
            throw;
        }
    }

    internal static void Verify(StagedInstallation staged)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (staged.Manifest.SchemaVersion != ManifestSchemaVersion || staged.Manifest.Files.Count == 0)
        {
            throw new InvalidDataException("The staged Meridian installation manifest is invalid.");
        }

        var root = Path.GetFullPath(staged.Path);
        foreach (var entry in staged.Manifest.Files)
        {
            var path = ResolveContainedPath(root, entry.RelativePath);
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Staged payload file '{entry.RelativePath}' is missing.");
            }

            var info = new FileInfo(path);
            if (info.Length != entry.Length)
            {
                throw new InvalidDataException($"Staged payload file '{entry.RelativePath}' has an invalid length.");
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Staged payload file '{entry.RelativePath}' failed SHA-256 verification.");
            }
        }
    }

    internal static string? Promote(
        StagedInstallation staged,
        string installRoot,
        Action? afterRollbackMove = null)
    {
        Verify(staged);
        var resolvedRoot = Path.GetFullPath(installRoot);
        var resolvedStage = Path.GetFullPath(staged.Path);
        EnsureSiblingStage(resolvedRoot, resolvedStage);
        RecoverInterruptedPromotion(resolvedRoot);

        var rollbackPath = GetRollbackPath(resolvedRoot);
        if (Directory.Exists(rollbackPath))
        {
            DeleteExactSiblingDirectory(resolvedRoot, rollbackPath);
        }

        var movedCurrent = false;
        try
        {
            if (Directory.Exists(resolvedRoot))
            {
                Directory.Move(resolvedRoot, rollbackPath);
                movedCurrent = true;
                afterRollbackMove?.Invoke();
            }

            Directory.Move(resolvedStage, resolvedRoot);
            return movedCurrent ? rollbackPath : null;
        }
        catch
        {
            if (movedCurrent && !Directory.Exists(resolvedRoot) && Directory.Exists(rollbackPath))
            {
                Directory.Move(rollbackPath, resolvedRoot);
            }

            throw;
        }
    }

    internal static bool RecoverInterruptedPromotion(string installRoot)
    {
        var resolvedRoot = Path.GetFullPath(installRoot);
        var rollbackPath = GetRollbackPath(resolvedRoot);
        if (!Directory.Exists(resolvedRoot) && Directory.Exists(rollbackPath))
        {
            Directory.Move(rollbackPath, resolvedRoot);
            return true;
        }

        return false;
    }

    internal static void CleanupAbandonedStages(string installRoot)
    {
        var resolvedRoot = Path.GetFullPath(installRoot);
        var parent = Directory.GetParent(resolvedRoot)?.FullName
            ?? throw new InvalidOperationException("The Meridian installation root must have a parent directory.");
        if (!Directory.Exists(parent))
        {
            return;
        }

        foreach (var candidate in Directory.EnumerateDirectories(parent, $"{StagePrefix}*", SearchOption.TopDirectoryOnly))
        {
            DeleteExactSiblingDirectory(resolvedRoot, candidate);
        }
    }

    internal static string GetRollbackPath(string installRoot)
        => Path.GetFullPath(installRoot) + RollbackSuffix;

    private static PayloadManifestEntry WritePayloadFile(string root, string relativePath, Stream input)
    {
        var target = ResolveContainedPath(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var output = new FileStream(
            target,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            hasher.AppendData(buffer, 0, read);
            length += read;
        }

        output.Flush(flushToDisk: true);
        return new PayloadManifestEntry(relativePath, length, Convert.ToHexString(hasher.GetHashAndReset()));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativePath) ||
            normalized.Length == 0 ||
            normalized.Split(Path.DirectorySeparatorChar).Any(segment => segment is "." or ".." || segment.Length == 0))
        {
            throw new InvalidDataException($"Invalid installer payload path '{relativePath}'.");
        }

        return normalized;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var resolvedRoot = Path.GetFullPath(root);
        var resolved = Path.GetFullPath(Path.Combine(resolvedRoot, normalized));
        var prefix = resolvedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Installer payload path '{relativePath}' escapes the stage root.");
        }

        return resolved;
    }

    private static void EnsureSiblingStage(string installRoot, string stagePath)
    {
        var installParent = Directory.GetParent(installRoot)?.FullName;
        var stageParent = Directory.GetParent(stagePath)?.FullName;
        if (installParent is null ||
            stageParent is null ||
            !string.Equals(installParent, stageParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(stagePath).StartsWith(StagePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The staged installation must be a verified sibling of the install root.");
        }
    }

    private static void DeleteExactSiblingDirectory(string installRoot, string candidate)
    {
        var resolvedRoot = Path.GetFullPath(installRoot);
        var resolvedCandidate = Path.GetFullPath(candidate);
        var rootParent = Directory.GetParent(resolvedRoot)?.FullName;
        var candidateParent = Directory.GetParent(resolvedCandidate)?.FullName;
        var candidateName = Path.GetFileName(resolvedCandidate);
        if (rootParent is null ||
            candidateParent is null ||
            !string.Equals(rootParent, candidateParent, StringComparison.OrdinalIgnoreCase) ||
            (!candidateName.StartsWith(StagePrefix, StringComparison.Ordinal) &&
             !string.Equals(resolvedCandidate, GetRollbackPath(resolvedRoot), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Refusing to delete an unrecognized installer transaction directory.");
        }

        if (Directory.Exists(resolvedCandidate))
        {
            Directory.Delete(resolvedCandidate, recursive: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Preserve the original staging failure. A later installer run removes abandoned stages.
        }
    }

    private static void WriteDurableText(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static string SanitizeSegment(string value)
        => string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-'));
}

[JsonSerializable(typeof(InstallationManifest))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
internal sealed partial class SetupJsonContext : JsonSerializerContext;
