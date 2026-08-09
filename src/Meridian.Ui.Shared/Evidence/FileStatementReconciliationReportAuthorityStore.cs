using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Core.IO;
using Meridian.Reporting;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>
/// Local compatibility authority for tests and single-process development. Production composition
/// must use a shared durable implementation.
/// </summary>
public sealed class FileStatementReconciliationReportAuthorityStore
    : IStatementReconciliationReportAuthorityStore
{
    private const string CurrentWorkflowDirectoryName = "statement-reconciliation-report";
    private const string LegacyWorkflowDirectoryName = "statement-to-report";
    private const string LegacyWorkflowIdPrefix = "statement-report-";
    private const string MetadataSuffix = ".statement-authority.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _reportingRoot;
    private readonly RootedPathGuard _pathGuard;

    public FileStatementReconciliationReportAuthorityStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _pathGuard = new RootedPathGuard(dataRoot);
        _reportingRoot = _pathGuard.ResolvePath("reporting");
    }

    public bool IsDurableAuthority => false;

    public string StorageKind => "file";

    public async ValueTask<IAsyncDisposable> AcquireWorkflowLeaseAsync(
        StatementReconciliationReportAuthorityScope scope,
        CancellationToken cancellationToken = default)
    {
        var directory = GetWorkflowDirectory(NormalizeScope(scope));
        _pathGuard.EnsurePath(directory);
        Directory.CreateDirectory(directory);
        _pathGuard.EnsurePath(directory);
        var lockPath = Path.Combine(directory, "workflow.lock");
        _pathGuard.EnsurePath(lockPath);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _pathGuard.EnsurePath(lockPath);
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new TimeoutException(
                    "Another process owns this statement reconciliation report workflow.",
                    ex);
            }
        }
    }

    public ValueTask<bool> DocumentExistsAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetDocumentPath(NormalizeScope(scope), NormalizeDocumentKey(documentKey));
        _pathGuard.EnsurePath(path);
        return ValueTask.FromResult(File.Exists(path));
    }

    public async ValueTask<StatementReconciliationReportAuthorityDocument?> GetDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedScope = NormalizeScope(scope);
        var normalizedKey = NormalizeDocumentKey(documentKey);
        var path = GetDocumentPath(normalizedScope, normalizedKey);
        if (!File.Exists(path))
        {
            return null;
        }

        _pathGuard.EnsurePath(path);
        var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var identity = new ReportingArtifactIdentity(
            normalizedScope.TenantId,
            ComputeSha256(content));
        var metadata = await ReadMetadataAsync(path, cancellationToken).ConfigureAwait(false);
        if (metadata is not null)
        {
            EnsureMetadataScope(metadata, normalizedScope, normalizedKey);
            if (!string.Equals(
                    metadata.ContentHashSha256,
                    identity.ContentHashSha256,
                    StringComparison.Ordinal)
                || metadata.ByteSize != content.LongLength)
            {
                throw new ReportingArtifactIntegrityException(
                    identity,
                    "file authority metadata does not match retained bytes");
            }
        }

        _pathGuard.EnsurePath(path);
        var lastWrite = File.GetLastWriteTimeUtc(path);
        var storedAtUtc = metadata?.StoredAtUtc ?? new DateTimeOffset(lastWrite, TimeSpan.Zero);
        var updatedAtUtc = metadata?.UpdatedAtUtc ?? storedAtUtc;
        return new StatementReconciliationReportAuthorityDocument(
            normalizedScope,
            normalizedKey,
            identity,
            content.LongLength,
            metadata?.IsImmutable ?? InferImmutable(normalizedKey),
            metadata?.Version ?? 1,
            storedAtUtc,
            updatedAtUtc);
    }

    public async ValueTask<byte[]?> TryReadDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(scope, documentKey, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var path = GetDocumentPath(document.Scope, document.DocumentKey);
        _pathGuard.EnsurePath(path);
        var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var actualHash = ComputeSha256(content);
        if (content.LongLength != document.ByteSize
            || !string.Equals(
                actualHash,
                document.Identity.ContentHashSha256,
                StringComparison.Ordinal))
        {
            throw new ReportingArtifactIntegrityException(
                document.Identity,
                "retained file changed after its authority metadata was read");
        }

        return content;
    }

    public async ValueTask<StatementReconciliationReportAuthorityDocument> WriteDocumentAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKey,
        ReadOnlyMemory<byte> content,
        bool isImmutable,
        CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            throw new ArgumentException(
                "Statement reconciliation authority documents cannot be empty.",
                nameof(content));
        }

        var normalizedScope = NormalizeScope(scope);
        var normalizedKey = NormalizeDocumentKey(documentKey);
        var path = GetDocumentPath(normalizedScope, normalizedKey);
        var bytes = content.ToArray();
        var hash = ComputeSha256(bytes);
        var now = DateTimeOffset.UtcNow;
        var existing = await GetDocumentAsync(normalizedScope, normalizedKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null && existing.IsImmutable != isImmutable)
        {
            throw new InvalidOperationException(
                $"Statement reconciliation authority document '{normalizedKey}' cannot change its retention policy.");
        }

        if (existing is not null && existing.IsImmutable)
        {
            if (existing.ByteSize != bytes.LongLength
                || !string.Equals(
                    existing.Identity.ContentHashSha256,
                    hash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Immutable statement reconciliation authority document '{normalizedKey}' cannot be replaced.");
            }

            return existing;
        }

        PrepareParentDirectory(path);
        await AtomicFileWriter.WriteAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        var metadata = new FileDocumentMetadata(
            normalizedScope.TenantId,
            normalizedScope.CompanyId,
            normalizedScope.WorkflowId,
            normalizedKey,
            hash,
            bytes.LongLength,
            isImmutable,
            checked((existing?.Version ?? 0) + 1),
            existing?.StoredAtUtc ?? now,
            now);
        var metadataPath = GetMetadataPath(path);
        PrepareParentDirectory(metadataPath);
        await AtomicFileWriter.WriteAsync(
                metadataPath,
                JsonSerializer.Serialize(metadata, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        return new StatementReconciliationReportAuthorityDocument(
            normalizedScope,
            normalizedKey,
            new ReportingArtifactIdentity(normalizedScope.TenantId, hash),
            bytes.LongLength,
            isImmutable,
            metadata.Version,
            metadata.StoredAtUtc,
            metadata.UpdatedAtUtc);
    }

    public ValueTask<IReadOnlyList<string>> ListDocumentKeysAsync(
        StatementReconciliationReportAuthorityScope scope,
        string documentKeyPrefix,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedScope = NormalizeScope(scope);
        var normalizedPrefix = NormalizeDocumentPrefix(documentKeyPrefix);
        var directory = GetWorkflowDirectory(normalizedScope);
        if (!Directory.Exists(directory))
        {
            return ValueTask.FromResult<IReadOnlyList<string>>([]);
        }

        var keys = EnumerateFilesWithoutFollowingLinks(directory, cancellationToken)
            .Where(static path =>
                !path.EndsWith(MetadataSuffix, StringComparison.Ordinal)
                && !string.Equals(Path.GetFileName(path), "workflow.lock", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Where(key => key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<string>>(keys);
    }

    public ValueTask ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pathGuard.EnsurePath(_reportingRoot);
        Directory.CreateDirectory(_reportingRoot);
        _pathGuard.EnsurePath(_reportingRoot);
        return ValueTask.CompletedTask;
    }

    private string GetWorkflowDirectory(StatementReconciliationReportAuthorityScope scope)
    {
        var workflowRoot = scope.WorkflowId.StartsWith(
            LegacyWorkflowIdPrefix,
            StringComparison.Ordinal)
            ? LegacyWorkflowDirectoryName
            : CurrentWorkflowDirectoryName;
        var directory = Path.Combine(_reportingRoot, workflowRoot, scope.WorkflowId);
        _pathGuard.EnsurePath(directory);
        return directory;
    }

    private string GetDocumentPath(
        StatementReconciliationReportAuthorityScope scope,
        string normalizedDocumentKey)
    {
        var directory = GetWorkflowDirectory(scope);
        var path = Path.GetFullPath(Path.Combine(
            directory,
            normalizedDocumentKey.Replace('/', Path.DirectorySeparatorChar)));
        var rootedDirectory = directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(
                rootedDirectory,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Statement reconciliation authority document escaped its workflow root.");
        }

        _pathGuard.EnsurePath(path);
        return path;
    }

    private async Task<FileDocumentMetadata?> ReadMetadataAsync(
        string documentPath,
        CancellationToken cancellationToken)
    {
        var metadataPath = GetMetadataPath(documentPath);
        _pathGuard.EnsurePath(metadataPath);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        _pathGuard.EnsurePath(metadataPath);
        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<FileDocumentMetadata>(json, JsonOptions)
            ?? throw new InvalidDataException(
                $"Statement reconciliation authority metadata '{metadataPath}' is empty.");
    }

    private IReadOnlyList<string> EnumerateFilesWithoutFollowingLinks(
        string root,
        CancellationToken cancellationToken)
    {
        _pathGuard.EnsurePath(root);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        var files = new List<string>();
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            directory.Refresh();
            RejectReparsePoint(directory, root);
            _pathGuard.EnsurePath(directory.FullName);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                entry.Refresh();
                RejectReparsePoint(entry, root);
                _pathGuard.EnsurePath(entry.FullName);
                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(childDirectory);
                }
                else if (entry is FileInfo file)
                {
                    files.Add(file.FullName);
                }
                else
                {
                    throw new InvalidDataException(
                        $"Statement reconciliation file authority contains unsupported entry '{entry.FullName}'.");
                }
            }
        }

        return files;
    }

    private void PrepareParentDirectory(string path)
    {
        _pathGuard.EnsurePath(path);
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException(
                "Statement reconciliation file authority document has no parent directory.");
        _pathGuard.EnsurePath(parent);
        Directory.CreateDirectory(parent);
        _pathGuard.EnsurePath(parent);
        _pathGuard.EnsurePath(path);
    }

    private static void RejectReparsePoint(FileSystemInfo entry, string authorityRoot)
    {
        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                $"Statement reconciliation file authority '{authorityRoot}' contains symbolic link or reparse point '{entry.FullName}'.");
        }
    }

    private static void EnsureMetadataScope(
        FileDocumentMetadata metadata,
        StatementReconciliationReportAuthorityScope scope,
        string documentKey)
    {
        if (!string.Equals(metadata.TenantId, scope.TenantId, StringComparison.Ordinal)
            || !string.Equals(metadata.CompanyId, scope.CompanyId, StringComparison.Ordinal)
            || !string.Equals(metadata.WorkflowId, scope.WorkflowId, StringComparison.Ordinal)
            || !string.Equals(metadata.DocumentKey, documentKey, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Statement reconciliation authority metadata belongs to another tenant, company, workflow, or document.");
        }
    }

    private static StatementReconciliationReportAuthorityScope NormalizeScope(
        StatementReconciliationReportAuthorityScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new StatementReconciliationReportAuthorityScope(
            NormalizeIdentity(scope.TenantId, nameof(scope.TenantId)),
            NormalizeIdentity(scope.CompanyId, nameof(scope.CompanyId)),
            NormalizePathSegment(scope.WorkflowId, nameof(scope.WorkflowId)));
    }

    private static string NormalizeIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Statement reconciliation authority identities are required.",
                parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > 256)
        {
            throw new ArgumentException(
                "Statement reconciliation authority identities cannot exceed 256 characters.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizePathSegment(string value, string parameterName)
    {
        var normalized = NormalizeIdentity(value, parameterName);
        try
        {
            RootedPathGuard.ValidatePathSegment(normalized, parameterName);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Statement reconciliation workflow identities must be one portable path segment.",
                parameterName,
                exception);
        }

        return normalized;
    }

    private static string NormalizeDocumentKey(string documentKey)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            throw new ArgumentException(
                "Statement reconciliation authority document key is required.",
                nameof(documentKey));
        }

        var normalized = documentKey.Trim().Replace('\\', '/');
        if (normalized.Length > 1024
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(static segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException(
                "Statement reconciliation authority document key must be a safe relative path.",
                nameof(documentKey));
        }

        foreach (var segment in normalized.Split('/'))
        {
            RootedPathGuard.ValidatePathSegment(segment, nameof(documentKey));
        }

        return normalized;
    }

    private static string NormalizeDocumentPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var normalized = prefix.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                "Statement reconciliation authority prefix must be a safe relative path.",
                nameof(prefix));
        }

        return normalized;
    }

    private static bool InferImmutable(string documentKey) =>
        documentKey.StartsWith("input/", StringComparison.Ordinal)
        || documentKey.StartsWith("evidence/", StringComparison.Ordinal)
        || documentKey.StartsWith("artifacts/history/", StringComparison.Ordinal);

    private static string GetMetadataPath(string documentPath) => documentPath + MetadataSuffix;

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed record FileDocumentMetadata(
        string TenantId,
        string CompanyId,
        string WorkflowId,
        string DocumentKey,
        string ContentHashSha256,
        long ByteSize,
        bool IsImmutable,
        long Version,
        DateTimeOffset StoredAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
