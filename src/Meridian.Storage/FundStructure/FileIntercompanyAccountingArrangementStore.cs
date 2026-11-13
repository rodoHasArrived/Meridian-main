using System.Diagnostics;
using System.Text;
using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Local durable agreement history. Writers share a bounded cross-process lease and publish atomically.</summary>
public sealed class FileIntercompanyAccountingArrangementStore(string rootDirectory) : IIntercompanyAccountingArrangementStore
{
    private readonly string _root = Path.GetFullPath(rootDirectory);

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        return await AcquireLeaseAsync(Path.Combine(_root, ".authority.lock"), ct).ConfigureAwait(false);
    }

    private string TenantDirectory(string tenantId) => Path.Combine(_root, IntercompanyAccountingArrangementStoreRules.FileKey(tenantId, Guid.Empty));
    private string HistoryPath(string tenantId, Guid id) => Path.Combine(TenantDirectory(tenantId), id.ToString("D") + ".json");

    public async Task<IntercompanyAccountingArrangementHistoryDto?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default)
    {
        var path = HistoryPath(tenantId, agreementId);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return IntercompanyAccountingArrangementStoreRules.Deserialize(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<IntercompanyAccountingArrangementDto>> ListAsync(string tenantId, Guid? organizationId = null, CancellationToken ct = default)
    {
        var directory = TenantDirectory(tenantId);
        if (!Directory.Exists(directory)) return [];
        var result = new List<IntercompanyAccountingArrangementDto>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id)) throw new InvalidDataException("Agreement history file identity is invalid.");
            var history = await GetAsync(tenantId, id, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("A retained agreement disappeared during enumeration.");
            if (organizationId is null || history.Current.OrganizationId == organizationId) result.Add(history.Current);
        }
        return result.OrderBy(x => x.ArrangementReference).ThenBy(x => x.ArrangementId).ToArray();
    }

    public Task<IntercompanyAccountingArrangementDto> AppendAsync(string tenantId, long expectedVersion, IntercompanyAccountingArrangementEventDto item, CancellationToken ct = default)
        => MutateAsync(tenantId, item.Snapshot.ArrangementId, item.RequestId, item.RequestDigest, (old, _) =>
        {
            if ((old?.Current.Version ?? 0) != expectedVersion) throw new InvalidOperationException("The agreement version changed.");
            return Task.FromResult(item);
        }, ct);

    public async Task<IntercompanyAccountingArrangementDto> MutateAsync(string tenantId, Guid agreementId, Guid requestId, string requestDigest,
        Func<IntercompanyAccountingArrangementHistoryDto?, CancellationToken, Task<IntercompanyAccountingArrangementEventDto>> build, CancellationToken ct = default)
    {
        var path = HistoryPath(tenantId, agreementId);
        await using var authority = await AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var lease = await AcquireLeaseAsync(path + ".lock", ct).ConfigureAwait(false);
        var old = await GetAsync(tenantId, agreementId, ct).ConfigureAwait(false);
        if (IntercompanyAccountingArrangementStoreRules.Retry(old, requestId, requestDigest) is { } retry) return retry;
        var item = await build(old, ct).ConfigureAwait(false);
        if (item.Snapshot.ArrangementId != agreementId || item.RequestId != requestId || item.RequestDigest != requestDigest)
            throw new InvalidOperationException("The agreement mutation changed its locked identity.");
        var result = IntercompanyAccountingArrangementStoreRules.Append(old, old?.Current.Version ?? 0, item);
        if (!result.Changed) return result.Result;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(IntercompanyAccountingArrangementStoreRules.Serialize(result.History));
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return result.Result;
    }

    private static async Task<FileStream> AcquireLeaseAsync(string path, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            { await Task.Delay(25, ct).ConfigureAwait(false); }
            catch (IOException ex) { throw new TimeoutException("Timed out waiting for the agreement writer lease.", ex); }
        }
    }
}
