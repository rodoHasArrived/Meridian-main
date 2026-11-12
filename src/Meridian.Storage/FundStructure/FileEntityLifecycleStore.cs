using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Local immutable revision files published by rename after fsync; never rewrites history.</summary>
public sealed class FileEntityLifecycleStore(string directory) : IEntityLifecycleStore
{
    private readonly string _directory = Path.GetFullPath(directory);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string Key(string tenantId, Guid entityId) => $"{_directory}:{InMemoryEntityLifecycleStore.NormalizeTenant(tenantId)}:{entityId:D}";
    private string EntityDirectory(string tenantId, Guid entityId)
        => Path.Combine(_directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(InMemoryEntityLifecycleStore.NormalizeTenant(tenantId)))), entityId.ToString("N"));

    public async Task<IReadOnlyList<EntityLifecycleRevisionDto>> GetHistoryAsync(string tenantId, Guid entityId, CancellationToken ct = default)
    {
        var path = EntityDirectory(tenantId, entityId);
        if (!Directory.Exists(path)) return [];
        var rows = new List<EntityLifecycleRevisionDto>();
        foreach (var file in Directory.EnumerateFiles(path, "*.json").OrderBy(value => value, StringComparer.Ordinal))
        {
            var row = JsonSerializer.Deserialize<EntityLifecycleRevisionDto>(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false), JsonOptions)
                ?? throw new InvalidOperationException("Lifecycle revision file is empty.");
            if (row.TenantId != InMemoryEntityLifecycleStore.NormalizeTenant(tenantId) || row.EntityId != entityId)
                throw new InvalidOperationException("Lifecycle revision file scope does not match its retained path.");
            EntityLifecycleRevisionIntegrity.ValidateRevision(row, rows.LastOrDefault(), rows.LastOrDefault()?.Revision ?? 0);
            rows.Add(row);
        }
        return rows;
    }

    public async Task<EntityLifecycleRevisionDto?> GetLatestAsync(string tenantId, Guid entityId, CancellationToken ct = default)
        => (await GetHistoryAsync(tenantId, entityId, ct).ConfigureAwait(false)).LastOrDefault();

    public async Task<EntityLifecycleRevisionDto?> GetRevisionAsync(string tenantId, Guid entityId, long revision, CancellationToken ct = default)
        => (await GetHistoryAsync(tenantId, entityId, ct).ConfigureAwait(false)).SingleOrDefault(row => row.Revision == revision);

    public async Task<EntityLifecycleRevisionDto> AppendAsync(EntityLifecycleRevisionDto revision, long expectedRevision, CancellationToken ct = default)
    {
        await using var graphLease = await EntityLifecycleLeaseGate.For(_directory + ":successor-graph").EnterWriteAsync(ct).ConfigureAwait(false);
        await using var graphFileLease = await AcquireFileLeaseAsync(Path.Combine(_directory, ".successor-graph.lock"), shared: false, ct).ConfigureAwait(false);
        await using var lease = await EntityLifecycleLeaseGate.For(Key(revision.TenantId, revision.EntityId)).EnterWriteAsync(ct).ConfigureAwait(false);
        await using var entityFileLease = await AcquireFileLeaseAsync(Path.Combine(EntityDirectory(revision.TenantId, revision.EntityId), ".authority.lock"), shared: false, ct).ConfigureAwait(false);
        await EntityLifecycleStoreRules.ValidateSuccessorsAsync(revision, id => GetLatestAsync(revision.TenantId, id, ct)).ConfigureAwait(false);
        var previous = await GetLatestAsync(revision.TenantId, revision.EntityId, ct).ConfigureAwait(false);
        EntityLifecycleRevisionIntegrity.ValidateRevision(revision, previous, expectedRevision);
        var path = EntityDirectory(revision.TenantId, revision.EntityId);
        Directory.CreateDirectory(path);
        var target = Path.Combine(path, $"{revision.Revision:D20}.json");
        var temporary = Path.Combine(path, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, revision, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: false);
            return revision with { Timeline = revision.Timeline.ToArray() };
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<IAsyncDisposable> AcquireReadLeaseAsync(string tenantId, IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        var localLease = await EntityLifecycleLeaseGate.AcquireManyAsync(entityIds.Select(id => Key(tenantId, id)), ct).ConfigureAwait(false);
        var files = new List<FileStream>();
        try
        {
            foreach (var id in entityIds.Distinct().OrderBy(value => value))
                files.Add(await AcquireFileLeaseAsync(Path.Combine(EntityDirectory(tenantId, id), ".authority.lock"), shared: true, ct).ConfigureAwait(false));
            return new EntityLifecycleLeaseGate.Lease(async () =>
            {
                for (var index = files.Count - 1; index >= 0; index--)
                    await files[index].DisposeAsync().ConfigureAwait(false);
                await localLease.DisposeAsync().ConfigureAwait(false);
            });
        }
        catch
        {
            for (var index = files.Count - 1; index >= 0; index--)
                await files[index].DisposeAsync().ConfigureAwait(false);
            await localLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<FileStream> AcquireFileLeaseAsync(string path, bool shared, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, shared ? FileAccess.Read : FileAccess.ReadWrite,
                    shared ? FileShare.Read : FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 11 or 32 or 33)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }
}
