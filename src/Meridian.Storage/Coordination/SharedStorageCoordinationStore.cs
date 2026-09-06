using System.Text.Json;
using Meridian.Contracts.Coordination;
using Meridian.Core.Config;
using Meridian.Core.IO;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Coordination;

/// <summary>
/// Storage-owned shared lease store using per-resource lock files and JSON lease records.
/// </summary>
public sealed class SharedStorageCoordinationStore : ICoordinationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly TimeSpan _lockWaitTimeout = TimeSpan.FromSeconds(5);
    private readonly RootedPathGuard _pathGuard;

    public SharedStorageCoordinationStore(CoordinationConfig config, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(config);
        _pathGuard = new RootedPathGuard(config.GetResolvedRootPath(dataRoot));
        RootPath = _pathGuard.RootPath;
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public async Task<bool> ExecuteUnderLeaseAsync(
        LeaseRecord lease, Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(action);
        var leasePath = GetLeasePath(lease.ResourceId);
        await using var resourceLock = await AcquireResourceLockAsync(leasePath, ct).ConfigureAwait(false);
        var retained = await ReadLeaseFileAsync(leasePath, ct).ConfigureAwait(false);
        if (retained is null || retained.LeaseVersion != lease.LeaseVersion ||
            !string.Equals(retained.InstanceId, lease.InstanceId, StringComparison.Ordinal) ||
            retained.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return false;

        ct.ThrowIfCancellationRequested();
        await action(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<LeaseAcquireResult> TryAcquireLeaseAsync(
        string resourceId,
        string instanceId,
        TimeSpan leaseTtl,
        TimeSpan takeoverDelay,
        CancellationToken ct = default)
    {
        var leasePath = GetLeasePath(resourceId);
        Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
        _pathGuard.EnsurePath(leasePath);

        await using var resourceLock = await AcquireResourceLockAsync(leasePath, ct).ConfigureAwait(false);

        LeaseRecord? existing = await ReadLeaseFileAsync(leasePath, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        if (existing is not null &&
            string.Equals(existing.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            var renewed = existing with
            {
                ExpiresAtUtc = now.Add(leaseTtl),
                LastRenewedAtUtc = now
            };

            await WriteLeaseFileAsync(leasePath, renewed, ct).ConfigureAwait(false);
            return new LeaseAcquireResult(true, false, renewed, renewed.InstanceId, renewed.ExpiresAtUtc, null);
        }

        if (existing is not null && now < existing.ExpiresAtUtc.Add(takeoverDelay))
        {
            return new LeaseAcquireResult(
                false,
                false,
                existing,
                existing.InstanceId,
                existing.ExpiresAtUtc,
                "Lease is still owned by another instance.");
        }

        var nextVersion = existing?.LeaseVersion + 1 ?? 1L;
        var lease = new LeaseRecord(
            resourceId,
            instanceId,
            nextVersion,
            now,
            now.Add(leaseTtl),
            now);

        await WriteLeaseFileAsync(leasePath, lease, ct).ConfigureAwait(false);
        return new LeaseAcquireResult(true, existing is not null, lease, existing?.InstanceId, existing?.ExpiresAtUtc, null);
    }

    public async Task<bool> RenewLeaseAsync(
        string resourceId,
        string instanceId,
        TimeSpan leaseTtl,
        CancellationToken ct = default)
    {
        var leasePath = GetLeasePath(resourceId);
        if (!File.Exists(leasePath))
            return false;

        await using var resourceLock = await AcquireResourceLockAsync(leasePath, ct).ConfigureAwait(false);
        var existing = await ReadLeaseFileAsync(leasePath, ct).ConfigureAwait(false);
        if (existing is null || !string.Equals(existing.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            return false;

        var now = DateTimeOffset.UtcNow;
        var renewed = existing with
        {
            ExpiresAtUtc = now.Add(leaseTtl),
            LastRenewedAtUtc = now
        };

        await WriteLeaseFileAsync(leasePath, renewed, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ReleaseLeaseAsync(
        string resourceId,
        string instanceId,
        CancellationToken ct = default)
    {
        var leasePath = GetLeasePath(resourceId);
        if (!File.Exists(leasePath))
            return true;

        await using var resourceLock = await AcquireResourceLockAsync(leasePath, ct).ConfigureAwait(false);
        var existing = await ReadLeaseFileAsync(leasePath, ct).ConfigureAwait(false);
        if (existing is null)
        {
            _pathGuard.EnsurePath(leasePath);
            File.Delete(leasePath);
            return true;
        }

        if (!string.Equals(existing.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            return false;

        _pathGuard.EnsurePath(leasePath);
        File.Delete(leasePath);
        return true;
    }

    public async Task<LeaseRecord?> GetLeaseAsync(string resourceId, CancellationToken ct = default)
    {
        var leasePath = GetLeasePath(resourceId);
        if (!File.Exists(leasePath))
            return null;

        return await ReadLeaseFileAsync(leasePath, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LeaseRecord>> GetAllLeasesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(RootPath))
            return Array.Empty<LeaseRecord>();

        var leases = new List<LeaseRecord>();
        foreach (var file in EnumerateLeaseFiles())
        {
            ct.ThrowIfCancellationRequested();
            var lease = await ReadLeaseFileAsync(file, ct).ConfigureAwait(false);
            if (lease is not null)
                leases.Add(lease);
        }

        return leases;
    }

    public Task<IReadOnlyList<string>> GetCorruptedLeaseFilesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(RootPath))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var corrupted = new List<string>();
        foreach (var file in EnumerateLeaseFiles())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _pathGuard.EnsurePath(file);
                var json = File.ReadAllText(file);
                _ = JsonSerializer.Deserialize<LeaseRecord>(json, JsonOptions);
            }
            catch
            {
                corrupted.Add(file);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(corrupted);
    }

    private string GetLeasePath(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (Path.IsPathRooted(resourceId))
            throw new ArgumentException("Resource ID cannot be a rooted path.", nameof(resourceId));

        var rawSegments = resourceId.Split('/', StringSplitOptions.None);
        foreach (var segment in rawSegments)
            ValidateResourceSegment(segment);

        var segments = rawSegments
            .Select(Uri.EscapeDataString)
            .ToArray();

        segments[^1] = $"{segments[^1]}.lease.json";
        return _pathGuard.ResolvePath(segments);
    }

    private static void ValidateResourceSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            throw new ArgumentException("Resource ID path segments cannot be empty or whitespace.", "resourceId");
        if (!string.Equals(segment, segment.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Resource ID path segments cannot have surrounding whitespace.", "resourceId");
        if (string.Equals(segment, ".", StringComparison.Ordinal) ||
            string.Equals(segment, "..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Resource ID cannot contain dot path segments.", "resourceId");
        }
        if (segment.Contains('\\') || segment.Any(char.IsControl))
            throw new ArgumentException("Resource ID path segments cannot contain mixed separators or control characters.", "resourceId");
    }

    private IEnumerable<string> EnumerateLeaseFiles()
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(RootPath, "*.lease.json", options))
        {
            _pathGuard.EnsurePath(file);
            yield return file;
        }
    }

    private async Task<FileStream> AcquireResourceLockAsync(string leasePath, CancellationToken ct)
    {
        var lockPath = leasePath + ".lock";
        var started = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _pathGuard.EnsurePath(lockPath);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
                _pathGuard.EnsurePath(lockPath);
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - started < _lockWaitTimeout)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<LeaseRecord?> ReadLeaseFileAsync(string leasePath, CancellationToken ct)
    {
        _pathGuard.EnsurePath(leasePath);
        if (!File.Exists(leasePath))
            return null;

        string json;
        try
        {
            _pathGuard.EnsurePath(leasePath);
            json = await File.ReadAllTextAsync(leasePath, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            // The lease was released/deleted between the existence check and the read.
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        // A transient IO/permission failure must NOT be swallowed into "no lease": doing so
        // would let a second instance take over a lease that is genuinely held, defeating
        // mutual exclusion. Let it propagate so acquire/renew deny rather than grant.

        try
        {
            return JsonSerializer.Deserialize<LeaseRecord>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Corrupt lease content is recoverable: treat it as absent so a fresh lease can be
            // written over it. GetCorruptedLeaseFilesAsync reports these separately.
            return null;
        }
    }

    private async Task WriteLeaseFileAsync(string leasePath, LeaseRecord lease, CancellationToken ct)
    {
        _pathGuard.EnsurePath(leasePath);
        var json = JsonSerializer.Serialize(lease, JsonOptions);
        await AtomicFileWriter.WriteAsync(leasePath, json, ct).ConfigureAwait(false);
    }
}
