using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Workstation;

/// <summary>Deterministic, durable synthetic-host adapter. Production uses PostgreSQL.</summary>
public sealed class FileOperatorTriageStore(string root) : IOperatorTriageStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OperatorTriageState> GetAsync(OperatorTriageScope scope, string workItemId, CancellationToken ct = default)
    {
        var path = PathFor(scope, workItemId);
        await using var writerLock = await AcquireAsync(path, ct).ConfigureAwait(false);
        return await LoadAsync(path, ct).ConfigureAwait(false);
    }

    public async Task<OperatorTriageState> UpdateAsync(OperatorTriageScope scope, string workItemId,
        Func<OperatorTriageState, CancellationToken, Task<OperatorTriageState>> update, CancellationToken ct = default)
    {
        var path = PathFor(scope, workItemId);
        await using var writerLock = await AcquireAsync(path, ct).ConfigureAwait(false);
        var current = await LoadAsync(path, ct).ConfigureAwait(false);
        var next = await update(current, ct).ConfigureAwait(false);
        if (next.Version != current.Version)
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(next, Json), ct).ConfigureAwait(false);
        return next;
    }

    private string PathFor(OperatorTriageScope scope, string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        var identity = JsonSerializer.Serialize(new[] { scope.TenantId, scope.CompanyId, workItemId });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var directory = Path.Combine(root, "operator-triage");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, hash + ".json");
    }

    private static async Task<FileStream> AcquireAsync(string path, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline) { await Task.Delay(25, ct).ConfigureAwait(false); }
        }
    }

    private static async Task<OperatorTriageState> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return OperatorTriageState.Empty;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<OperatorTriageState>(stream, Json, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("The retained triage state is empty.");
    }
}
