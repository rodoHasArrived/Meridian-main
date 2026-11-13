using System.Diagnostics;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.FixedIncome;

/// <summary>Immutable server-generated planning/comparison evidence, partitioned by exact owner scope.</summary>
public sealed class BondWorkspaceSnapshotStore(string dataRoot)
{
    private readonly string _root = Path.Combine(Path.GetFullPath(dataRoot), "bonds", "workspace");

    public async Task RetainAsync<T>(AccountCashForecastScopeDto scope, string kind, string id, T value,
        CancellationToken ct = default)
    {
        var path = SnapshotPath(scope, kind, id);
        var payload = JsonSerializer.Serialize(value);
        var envelope = new Envelope(1, scope, kind, id, Sha256Digest.ComputeUtf8(payload), payload);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var started = Stopwatch.StartNew();
        FileStream lease;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); break; }
            catch (IOException) when (started.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(30, ct).ConfigureAwait(false); }
        }
        await using (lease.ConfigureAwait(false))
        {
            if (File.Exists(path))
            {
                var retained = await ReadEnvelopeAsync(path, scope, kind, id, ct).ConfigureAwait(false);
                if (!Sha256Digest.FixedEquals(retained.ContentHash, envelope.ContentHash))
                    throw new InvalidOperationException("This retained workflow identity already names different content.");
                return;
            }
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(envelope), ct).ConfigureAwait(false);
        }
    }

    public async Task<T?> ReadAsync<T>(AccountCashForecastScopeDto scope, string kind, string id, CancellationToken ct = default)
    {
        var path = SnapshotPath(scope, kind, id);
        if (!File.Exists(path))
            return default;
        var envelope = await ReadEnvelopeAsync(path, scope, kind, id, ct).ConfigureAwait(false);
        try
        { return JsonSerializer.Deserialize<T>(envelope.Payload) ?? throw new InvalidDataException("Retained workflow payload is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Retained workflow payload is corrupt.", ex); }
    }

    private string SnapshotPath(AccountCashForecastScopeDto scope, string kind, string id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (new[] { scope.TenantId, scope.CompanyId, scope.AccountId, scope.BookId, scope.BaseCurrency, kind, id }
            .Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Exact workflow owner scope and identity are required.");
        return Path.Combine(_root, Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(scope)),
            Sha256Digest.ComputeUtf8(kind + "\n" + id) + ".json");
    }

    private static async Task<Envelope> ReadEnvelopeAsync(string path, AccountCashForecastScopeDto scope,
        string kind, string id, CancellationToken ct)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var envelope = await JsonSerializer.DeserializeAsync<Envelope>(file, cancellationToken: ct).ConfigureAwait(false);
            if (envelope is null || envelope.SchemaVersion != 1 || envelope.Scope != scope || envelope.Kind != kind
                || envelope.Id != id || !Sha256Digest.FixedEquals(envelope.ContentHash, Sha256Digest.ComputeUtf8(envelope.Payload)))
                throw new InvalidDataException("Retained workflow owner or content identity is invalid.");
            return envelope;
        }
        catch (JsonException ex) { throw new InvalidDataException("Retained workflow history is corrupt.", ex); }
    }

    private sealed record Envelope(int SchemaVersion, AccountCashForecastScopeDto Scope, string Kind,
        string Id, string ContentHash, string Payload);
}
