using System.Text.Json;
using Meridian.Core.IO;
using Meridian.Reporting;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class StatementReconciliationReportWorkflowService
{
    private WorkflowLocation BuildWorkflowLocation(
        string workflowId,
        string tenantId,
        string? companyId)
    {
        var scope = new StatementReconciliationReportAuthorityScope(
            tenantId.Trim(),
            RequireCompanyId(companyId),
            workflowId);
        return new WorkflowLocation(workflowId, GetWorkflowDirectory(workflowId), scope);
    }

    private async Task<WorkflowLocation[]> FindRetainedWorkflowLocationsAsync(
        IReadOnlyList<WorkflowLocation> candidates,
        CancellationToken ct)
    {
        var retained = new List<WorkflowLocation>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (await _authorityStore
                    .DocumentExistsAsync(candidate.Scope, "workflow.json", ct)
                    .ConfigureAwait(false))
            {
                retained.Add(candidate);
            }
        }

        return retained.ToArray();
    }

    private async Task HydrateWorkspaceAsync(WorkflowLocation location, CancellationToken ct)
    {
        if (!_authorityStore.IsDurableAuthority)
        {
            return;
        }

        var pathGuard = ValidateWorkspaceLocation(location);
        Directory.CreateDirectory(location.Directory);
        pathGuard.EnsurePath(location.Directory);
        var documentKeys = await _authorityStore
            .ListDocumentKeysAsync(location.Scope, string.Empty, ct)
            .ConfigureAwait(false);
        var authoritativeDocuments = ValidateAuthoritativeDocumentKeys(
            location,
            documentKeys);
        var authoritativeKeys = authoritativeDocuments
            .Select(static document => document.Key)
            .ToHashSet(StringComparer.Ordinal);
        var workspaceTree = EnumerateWorkspaceTree(location, pathGuard);
        foreach (var localPath in workspaceTree.Files)
        {
            ct.ThrowIfCancellationRequested();
            var localKey = Path.GetRelativePath(location.Directory, localPath).Replace('\\', '/');
            if (!authoritativeKeys.Contains(localKey))
            {
                pathGuard.EnsurePath(localPath);
                File.Delete(localPath);
            }
        }

        foreach (var subdirectory in workspaceTree.Directories
                     .OrderByDescending(static path => path.Length))
        {
            ct.ThrowIfCancellationRequested();
            pathGuard.EnsurePath(subdirectory);
            RejectReparsePoint(new DirectoryInfo(subdirectory), location.Directory);
            if (!Directory.EnumerateFileSystemEntries(subdirectory).Any())
            {
                Directory.Delete(subdirectory);
            }
        }

        foreach (var document in authoritativeDocuments.OrderBy(
                     static item => item.Key,
                     StringComparer.Ordinal))
        {
            var content = await _authorityStore
                .TryReadDocumentAsync(location.Scope, document.Key, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Statement reconciliation authority listed missing document '{document.Key}'.");
            var parent = Path.GetDirectoryName(document.Path)
                ?? throw new InvalidDataException(
                    $"Statement reconciliation authority document '{document.Key}' has no workspace parent.");
            pathGuard.EnsurePath(parent);
            Directory.CreateDirectory(parent);
            pathGuard.EnsurePath(parent);
            await AtomicFileWriter.WriteAsync(document.Path, content, ct).ConfigureAwait(false);
        }
    }

    private async Task PersistWorkspaceAsync(WorkflowLocation location, CancellationToken ct)
    {
        // The compatibility file adapter maps directly onto this directory; existing workflow
        // writes are already its atomic authority. Re-reading through sidecar metadata after a
        // direct replacement would manufacture a false integrity race.
        if (!_authorityStore.IsDurableAuthority)
        {
            return;
        }

        var pathGuard = ValidateWorkspaceLocation(location);
        if (!Directory.Exists(location.Directory))
        {
            throw new DirectoryNotFoundException(
                $"Statement reconciliation workflow workspace '{location.Directory}' does not exist.");
        }

        var workspaceTree = EnumerateWorkspaceTree(location, pathGuard);
        var documents = workspaceTree.Files
            .Where(static path =>
                !string.Equals(Path.GetFileName(path), "workflow.lock", StringComparison.Ordinal)
                && !path.EndsWith(".statement-authority.json", StringComparison.Ordinal))
            .Select(path => new
            {
                Path = path,
                Key = Path.GetRelativePath(location.Directory, path).Replace('\\', '/')
            })
            .OrderBy(static document =>
                string.Equals(document.Key, "workflow.json", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(static document => document.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var document in documents)
        {
            pathGuard.EnsurePath(document.Path);
            var content = await File.ReadAllBytesAsync(document.Path, ct).ConfigureAwait(false);
            await _authorityStore
                .WriteDocumentAsync(
                    location.Scope,
                    document.Key,
                    content,
                    IsImmutableAuthorityDocument(document.Key),
                    ct)
                .ConfigureAwait(false);
        }
    }

    private static string ResolveWorkspaceDocumentPath(string directory, string documentKey)
    {
        var normalizedKey = documentKey.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedKey)
            || normalizedKey.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(normalizedKey)
            || normalizedKey.Split('/').Any(static segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                $"Statement reconciliation authority returned unsafe document key '{documentKey}'.");
        }

        foreach (var segment in normalizedKey.Split('/'))
        {
            try
            {
                RootedPathGuard.ValidatePathSegment(segment, nameof(documentKey));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Statement reconciliation authority returned non-portable document key '{documentKey}'.",
                    exception);
            }
        }

        var path = Path.GetFullPath(Path.Combine(
            directory,
            normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        var root = directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Statement reconciliation authority document '{documentKey}' escaped its workflow workspace.");
        }

        return path;
    }

    private RootedPathGuard ValidateWorkspaceLocation(WorkflowLocation location)
    {
        var expectedRoot = location.WorkflowId.StartsWith(
            LegacyWorkflowIdPrefix,
            StringComparison.Ordinal)
            ? _legacyWorkflowRoot
            : _workflowRoot;
        var expectedDirectory = Path.GetFullPath(Path.Combine(expectedRoot, location.WorkflowId));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                Path.GetFullPath(location.Directory),
                expectedDirectory,
                comparison))
        {
            throw new InvalidDataException(
                "Statement reconciliation authority workspace does not match its validated workflow identity.");
        }

        var pathGuard = new RootedPathGuard(_dataRoot);
        pathGuard.EnsurePath(expectedDirectory);
        return pathGuard;
    }

    private static IReadOnlyList<WorkspaceDocumentPath> ValidateAuthoritativeDocumentKeys(
        WorkflowLocation location,
        IReadOnlyList<string> documentKeys)
    {
        var exactKeys = new HashSet<string>(StringComparer.Ordinal);
        var workspacePaths = new Dictionary<string, string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        var documents = new List<WorkspaceDocumentPath>(documentKeys.Count);
        foreach (var documentKey in documentKeys)
        {
            if (!exactKeys.Add(documentKey))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation authority returned duplicate document key '{documentKey}'.");
            }

            var path = ResolveWorkspaceDocumentPath(location.Directory, documentKey);
            if (workspacePaths.TryGetValue(path, out var conflictingKey))
            {
                throw new InvalidDataException(
                    $"Statement reconciliation authority keys '{conflictingKey}' and '{documentKey}' collide in the local workspace.");
            }

            workspacePaths.Add(path, documentKey);
            documents.Add(new WorkspaceDocumentPath(documentKey, path));
        }

        return documents;
    }

    private static WorkspaceTree EnumerateWorkspaceTree(
        WorkflowLocation location,
        RootedPathGuard pathGuard)
    {
        pathGuard.EnsurePath(location.Directory);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(location.Directory));
        var files = new List<string>();
        var directories = new List<string>();
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            directory.Refresh();
            RejectReparsePoint(directory, location.Directory);
            pathGuard.EnsurePath(directory.FullName);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                entry.Refresh();
                RejectReparsePoint(entry, location.Directory);
                pathGuard.EnsurePath(entry.FullName);
                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Add(childDirectory.FullName);
                    pending.Push(childDirectory);
                }
                else if (entry is FileInfo file)
                {
                    files.Add(file.FullName);
                }
                else
                {
                    throw new InvalidDataException(
                        $"Statement reconciliation workspace contains unsupported entry '{entry.FullName}'.");
                }
            }
        }

        return new WorkspaceTree(files, directories);
    }

    private static void RejectReparsePoint(FileSystemInfo entry, string workspaceRoot)
    {
        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException(
                $"Statement reconciliation workspace '{workspaceRoot}' contains symbolic link or reparse point '{entry.FullName}'.");
        }
    }

    private static bool IsImmutableAuthorityDocument(string documentKey) =>
        !string.Equals(documentKey, "workflow.json", StringComparison.Ordinal)
        && (!documentKey.StartsWith("artifacts/", StringComparison.Ordinal)
            || documentKey.StartsWith("artifacts/history/", StringComparison.Ordinal));

    private async Task SaveSnapshotAsync(
        WorkflowLocation location,
        WorkflowSnapshot snapshot,
        CancellationToken ct)
    {
        if (_authorityStore.IsDurableAuthority)
        {
            var pathGuard = ValidateWorkspaceLocation(location);
            pathGuard.EnsurePath(location.Directory);
        }

        await AtomicFileWriter.WriteAsync(
                Path.Combine(location.Directory, "workflow.json"),
                JsonSerializer.Serialize(snapshot, JsonOptions),
                ct)
            .ConfigureAwait(false);
        await PersistWorkspaceAsync(location, ct).ConfigureAwait(false);
    }

    private sealed record WorkspaceDocumentPath(string Key, string Path);

    private sealed record WorkspaceTree(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Directories);
}
