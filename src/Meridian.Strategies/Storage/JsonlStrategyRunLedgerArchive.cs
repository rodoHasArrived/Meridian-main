using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Core.IO;
using Meridian.Backtesting.Sdk;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Serialization;

namespace Meridian.Strategies.Storage;

/// <summary>
/// Write-once, content-addressed JSONL journals for completed simulation runs. Lifecycle history
/// selects the authoritative content hash; rejected candidates cannot poison a run identity. This research archive
/// is separate from governed accounting books and grants no posting authority.
/// </summary>
public sealed class JsonlStrategyRunLedgerArchive(string dataRoot, IAtomicFileWriter writer) : IStrategyRunLedgerArchive
{
    private readonly string _root = Path.Combine(Path.GetFullPath(dataRoot), "strategies", "run-ledgers");
    private readonly IAtomicFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public Task<StrategyRunLedgerReference> SaveAsync(string runId, IReadOnlyLedger ledger, CancellationToken ct = default)
        => SaveAsync(runId, ledger, null, ct);

    public async Task<StrategyRunLedgerReference> SaveAsync(
        string runId, IReadOnlyLedger ledger, PortfolioSnapshot? finalSnapshot, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(ledger);
        // Snapshot/validate every entry before publishing anything. There is no partial journal
        // after cancellation or a bad entry, and posted line identities cannot be duplicated.
        var journal = ledger.Journal.ToArray();
        var verified = new Meridian.Ledger.Ledger();
        var content = new StringBuilder();
        content.Append(JsonSerializer.Serialize(
            new StrategyRunLedgerHeader("meridian.run-ledger.v1", runId, journal.Length,
                finalSnapshot is null ? null : JsonSerializer.Serialize(finalSnapshot, StrategyRunPersistenceJson.Options)),
            StrategyRunLedgerJsonContext.Default.StrategyRunLedgerHeader)).Append('\n');
        foreach (var entry in journal)
        {
            ct.ThrowIfCancellationRequested();
            verified.Post(entry);
            content.Append(JsonSerializer.Serialize(entry, StrategyRunPersistenceJson.Options)).Append('\n');
        }

        if (verified.JournalEntryCount != ledger.JournalEntryCount)
            throw new InvalidDataException("The run ledger changed while it was being archived.");
        var bytes = Encoding.UTF8.GetBytes(content.ToString());
        var reference = new StrategyRunLedgerReference(runId, verified.JournalEntryCount, Hash(bytes));
        Directory.CreateDirectory(_root);
        var path = GetPath(runId, reference.Sha256);
        // FileShare.None protects the check and atomic publish across store instances/processes.
        // The sidecar remains present; its open handle, not its existence, is the lease.
        await using var lease = await AcquireLeaseAsync(path + ".lock", ct).ConfigureAwait(false);
        if (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException($"Run '{runId}' immutable ledger archive content does not match its address.");
            return reference;
        }

        await _writer.WriteAsync(path, bytes, ct).ConfigureAwait(false);
        return reference;
    }

    public async Task<IReadOnlyLedger> LoadAsync(StrategyRunLedgerReference reference, CancellationToken ct = default)
        => (await LoadSnapshotAsync(reference, ct).ConfigureAwait(false)).Ledger;

    public async Task<StrategyRunLedgerSnapshot> LoadSnapshotAsync(StrategyRunLedgerReference reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.RunId);
        if (!Sha256Digest.IsCanonical(reference.Sha256))
            throw new InvalidDataException("Run ledger archive has an invalid content address.");
        var bytes = await File.ReadAllBytesAsync(GetPath(reference.RunId, reference.Sha256), ct).ConfigureAwait(false);
        if (!Sha256Digest.FixedEquals(Hash(bytes), reference.Sha256))
            throw new InvalidDataException($"Run '{reference.RunId}' ledger archive checksum does not match retained history.");

        using var reader = new StringReader(Encoding.UTF8.GetString(bytes));
        try
        {
            var header = JsonSerializer.Deserialize(
                reader.ReadLine() ?? "", StrategyRunLedgerJsonContext.Default.StrategyRunLedgerHeader);
            if (header is null || header.Schema != "meridian.run-ledger.v1" ||
                header.RunId != reference.RunId || header.JournalEntryCount != reference.JournalEntryCount)
                throw new InvalidDataException("Run ledger archive identity/count does not match retained history.");
            var ledger = new Meridian.Ledger.Ledger();
            while (reader.ReadLine() is { } line)
            {
                ct.ThrowIfCancellationRequested();
                ledger.Post(JsonSerializer.Deserialize<JournalEntry>(line, StrategyRunPersistenceJson.Options)
                    ?? throw new InvalidDataException("Run ledger archive contains a null journal."));
            }
            if (ledger.JournalEntryCount != reference.JournalEntryCount)
                throw new InvalidDataException("Run ledger archive is incomplete.");
            var snapshot = header.FinalSnapshotJson is null ? null :
                JsonSerializer.Deserialize<PortfolioSnapshot>(header.FinalSnapshotJson, StrategyRunPersistenceJson.Options)
                ?? throw new InvalidDataException("Run ledger archive contains a null final snapshot.");
            return new StrategyRunLedgerSnapshot(ledger, snapshot);
        }
        catch (Exception exception) when (exception is JsonException or LedgerValidationException)
        {
            throw new InvalidDataException("Run ledger archive contains invalid accounting evidence.", exception);
        }
    }

    private string GetPath(string runId, string hash) => Path.Combine(_root, Hash(Encoding.UTF8.GetBytes(runId)) + "-" + hash + ".jsonl");
    private static string Hash(byte[] bytes) => Sha256Digest.Compute(bytes);

    private static async Task<FileStream> AcquireLeaseAsync(string path, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (started.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }
}

internal sealed record StrategyRunLedgerHeader(string Schema, string RunId, int JournalEntryCount, string? FinalSnapshotJson = null);
