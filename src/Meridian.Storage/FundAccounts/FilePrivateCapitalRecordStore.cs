using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.PrivateCapital;
using Meridian.Storage.Archival;

namespace Meridian.Storage.FundAccounts;

/// <summary>Durable local authority with a cross-process writer lock and atomic publication.</summary>
public sealed class FilePrivateCapitalRecordStore(string rootDirectory) : IPrivateCapitalRecordStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string PathFor(PrivateCapitalScope scope)
    {
        PrivateCapitalScopeValidation.Require(scope);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(scope, Json))));
        return Path.Combine(rootDirectory, "private-capital", key + ".json");
    }

    public async Task<IReadOnlyList<PrivateCapitalStoredRecord>> ReadAsync(PrivateCapitalScope scope, CancellationToken ct = default)
    {
        var path = PathFor(scope);
        if (!File.Exists(path)) return [];
        var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), Json)
            ?? throw new InvalidDataException("Private-capital records could not be decoded.");
        if (envelope.Scope != scope) throw new InvalidDataException("Private-capital record scope does not match its partition.");
        return envelope.Records;
    }

    public async Task AppendAsync(PrivateCapitalScope scope,
        Func<IReadOnlyList<PrivateCapitalStoredRecord>, IReadOnlyList<PrivateCapitalStoredRecord>> prepare,
        CancellationToken ct = default)
    {
        var path = PathFor(scope);
        var gate = Gates.GetOrAdd(path, static _ => new(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var fileLock = await LockAsync(path + ".lock", ct).ConfigureAwait(false);
            var existing = await ReadAsync(scope, ct).ConfigureAwait(false);
            var added = prepare(existing);
            if (added.Count == 0) return;
            if (added.Select(row => (row.Kind, row.Id)).Distinct().Count() != added.Count ||
                added.Any(row => existing.Any(old => old.Kind == row.Kind && old.Id == row.Id)))
                throw new InvalidOperationException("A retained private-capital identity already exists.");
            await AtomicFileWriter.WriteAsync(path,
                JsonSerializer.Serialize(new Envelope(scope, existing.Concat(added).ToArray()), Json), ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static async Task<FileStream> LockAsync(string path, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(30, ct).ConfigureAwait(false); }
        }
    }

    private sealed record Envelope(PrivateCapitalScope Scope, IReadOnlyList<PrivateCapitalStoredRecord> Records);
}
