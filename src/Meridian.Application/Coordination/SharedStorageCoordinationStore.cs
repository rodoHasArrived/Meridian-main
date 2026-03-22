using System.Text.Json;
using Meridian.Application.Config;

namespace Meridian.Application.Coordination;

/// <summary>
/// Shared-storage lease store using per-resource lock files and JSON lease records.
/// </summary>
public sealed class SharedStorageCoordinationStore : ICoordinationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly TimeSpan _lockWaitTimeout = TimeSpan.FromSeconds(5);

    public SharedStorageCoordinationStore(CoordinationConfig config, string dataRoot)
    {
        RootPath = config.GetResolvedRootPath(dataRoot);
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public async Task<LeaseAcquireResult> TryAcquireLeaseAsync(
        string resourceId,
        string instanceId,
        TimeSpan leaseTtl,
        TimeSpan takeoverDelay,
        CancellationToken ct = default)
    {
        var leasePath = GetLeasePath(resourceId);
        Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);

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
            File.Delete(leasePath);
            return true;
        }

        if (!string.Equals(existing.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            return false;

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
        foreach (var file in Directory.GetFiles(RootPath, "*.lease.json", SearchOption.AllDirectories))
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
        foreach (var file in Directory.GetFiles(RootPath, "*.lease.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
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
        var segments = resourceId
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString)
            .ToArray();

        if (segments.Length == 0)
            throw new ArgumentException("Resource ID must contain at least one segment.", nameof(resourceId));

        var directory = RootPath;
        if (segments.Length > 1)
            directory = Path.Combine(RootPath, Path.Combine(segments[..^1]));

        return Path.Combine(directory, $"{segments[^1]}.lease.json");
    }

    private async Task<FileStream> AcquireResourceLockAsync(string leasePath, CancellationToken ct)
    {
        var lockPath = leasePath + ".lock";
        var started = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - started < _lockWaitTimeout)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task<LeaseRecord?> ReadLeaseFileAsync(string leasePath, CancellationToken ct)
    {
        if (!File.Exists(leasePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(leasePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<LeaseRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteLeaseFileAsync(string leasePath, LeaseRecord lease, CancellationToken ct)
    {
        var tempPath = leasePath + ".tmp";
        var json = JsonSerializer.Serialize(lease, JsonOptions);

        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, leasePath, true);
    }
}
