using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Execution.Services;

/// <summary>
/// Durable read-side activity inbox. Immutable provider activity IDs are accepted before the
/// checkpoint advances; a crash between those writes rereads and verifies the same records.
/// It retains source evidence and grants no ledger-posting or corrective-trade authority.
/// </summary>
public sealed class BrokerActivityImportService(string rootDirectory)
{
    private readonly string _root = Path.GetFullPath(rootDirectory);

    /// <summary>
    /// Resumes from a complete retained watermark, rereading its timestamp inclusively so IDs
    /// sharing a timestamp cannot be skipped. An explicit lower bound requests a deliberate rescan.
    /// Providers without completeness evidence always rescan; earlier late arrivals require a rescan.
    /// </summary>
    public async Task<BrokerageActivitySnapshotDto> ImportAsync(
        IBrokerageActivitySync provider,
        string accountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
        => (await ImportWithHistoryAsync(provider, accountId, since, ct).ConfigureAwait(false)).Retained;

    /// <summary>
    /// Returns the current provider observation separately from accepted historical activity.
    /// Consumers can publish the provider's current window without relabeling historical rows,
    /// while recovering a downstream history projection from the complete durable inbox.
    /// </summary>
    public async Task<BrokerActivityImportResult> ImportWithHistoryAsync(
        IBrokerageActivitySync provider,
        string accountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var directory = Path.Combine(_root, Hash(provider.ProviderId.ToLowerInvariant() + "\n" + accountId));
        Directory.CreateDirectory(directory);
        // Account-scoped, process-independent exclusion also protects callers using new service instances.
        await using var writerLease = await AcquireWriterAsync(Path.Combine(directory, "writer.lock"), ct).ConfigureAwait(false);
        var checkpointPath = Path.Combine(directory, "checkpoint.json");
        BrokerActivityImportCheckpoint? checkpoint = null;
        if (File.Exists(checkpointPath))
        {
            var json = await File.ReadAllTextAsync(checkpointPath, ct).ConfigureAwait(false);
            checkpoint = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.BrokerActivityImportCheckpoint)
                ?? throw new InvalidDataException("The broker activity checkpoint is empty.");
            if (!string.Equals(checkpoint.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(checkpoint.AccountId, accountId, StringComparison.Ordinal))
                throw new InvalidDataException("The broker activity checkpoint has a different account or provider.");
        }

        var retained = new Dictionary<string, BrokerActivityImportRecord>(StringComparer.Ordinal);
        if (checkpoint is not null && (checkpoint.ActivityHashes is null
            || checkpoint.Cursor?.HighWatermark is not null && checkpoint.ActivityHashes.Count == 0))
            throw new InvalidDataException("The broker activity checkpoint has no accepted activity index.");
        foreach (var pair in checkpoint?.ActivityHashes ?? new Dictionary<string, string>())
        {
            var id = pair.Key;
            if (string.IsNullOrWhiteSpace(id) || !Sha256Digest.IsCanonical(pair.Value))
                throw new InvalidDataException("The broker activity checkpoint has an invalid activity checksum.");
            var recordPath = Path.Combine(directory, Hash(id) + ".json");
            var json = await File.ReadAllTextAsync(recordPath, ct).ConfigureAwait(false);
            if (Hash(json) != pair.Value)
                throw new InvalidDataException("Retained broker activity does not match its accepted checksum.");
            var row = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.BrokerActivityImportRecord)
                ?? throw new InvalidDataException("Retained broker activity is empty.");
            if (row.EventId != id || !retained.TryAdd(id, row))
                throw new InvalidDataException("The broker activity checkpoint contains conflicting identities.");
        }

        if (checkpoint?.Cursor is { HighWatermark: { } storedWatermark } storedCursor)
        {
            var timestamps = retained.Values.SelectMany(row =>
                new DateTimeOffset?[] { row.Fill?.FilledAt, row.Cash?.PostedAt, row.Activity?.EffectiveAt })
                .OfType<DateTimeOffset>().ToArray();
            if (timestamps.Length == 0 || storedWatermark > timestamps.Max()
                || storedCursor.LastEventId is { } storedLastId && !retained.ContainsKey(storedLastId))
                throw new InvalidDataException("The broker checkpoint cursor is not backed by accepted activity.");
        }

        var resumeSince = since;
        if (resumeSince is null && checkpoint?.Cursor is { IsComplete: true, HighWatermark: { } watermark })
            resumeSince = watermark == DateTimeOffset.MinValue ? watermark : watermark.AddTicks(-1);
        var incoming = await provider.GetActivitySnapshotAsync(accountId, resumeSince, ct).ConfigureAwait(false);
        if (!string.Equals(incoming.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(incoming.AccountId, accountId, StringComparison.Ordinal))
            throw new InvalidDataException("The broker activity response has a different account or provider.");

        var candidates = BuildRecords(incoming);
        if (incoming.Cursor is { IsComplete: true, HighWatermark: { } reportedWatermark } cursorEvidence)
        {
            var timestamps = incoming.Fills.Select(row => row.FilledAt)
                .Concat(incoming.CashTransactions.Select(row => row.PostedAt))
                .Concat((incoming.Activities ?? []).Select(row => row.EffectiveAt)).ToArray();
            if (timestamps.Length == 0 || reportedWatermark > timestamps.Max()
                || reportedWatermark > incoming.RetrievedAt.AddMinutes(5)
                || cursorEvidence.LastEventId is { } lastId && !candidates.ContainsKey(lastId))
                throw new InvalidDataException("The complete broker cursor is not backed by accepted source activity evidence.");
        }
        // Preflight every collision before accepting new rows. Changed economics under the same
        // ID require explicit provider correction lineage, not a silent overwrite.
        foreach (var row in candidates.Values)
        {
            if (retained.TryGetValue(row.EventId, out var previous) && Serialize(previous) != Serialize(row))
                throw new InvalidDataException("A retained broker activity ID has conflicting source evidence.");
            var path = Path.Combine(directory, Hash(row.EventId) + ".json");
            if (File.Exists(path) && await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) != Serialize(row))
                throw new InvalidDataException("A broker activity ID conflicts with an already accepted immutable record.");
        }
        foreach (var row in candidates.Values)
        {
            var path = Path.Combine(directory, Hash(row.EventId) + ".json");
            if (!File.Exists(path))
                await AtomicFileWriter.WriteAsync(path, Serialize(row), ct).ConfigureAwait(false);
            retained[row.EventId] = row;
        }
        // Incomplete retrievals retain their rows but cannot advance the resume point.
        var cursor = incoming.Cursor is { IsComplete: true, HighWatermark: not null } ? incoming.Cursor : checkpoint?.Cursor;
        if (cursor?.HighWatermark < checkpoint?.Cursor?.HighWatermark)
            cursor = checkpoint!.Cursor;
        var accepted = new BrokerActivityImportCheckpoint(provider.ProviderId, accountId,
            cursor, retained.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => Hash(Serialize(pair.Value)), StringComparer.Ordinal));
        await AtomicFileWriter.WriteAsync(checkpointPath,
            JsonSerializer.Serialize(accepted, ExecutionJsonContext.Default.BrokerActivityImportCheckpoint), ct).ConfigureAwait(false);

        // Retained history survives an empty incremental fetch; it is distinct from the current
        // observation and can rebuild downstream history after an interrupted projection write.
        var rows = retained.Values.OrderBy(row => row.EventId, StringComparer.Ordinal).ToArray();
        var history = incoming with
        {
            Fills = rows.Where(row => row.Fill is not null).Select(row => row.Fill!).ToArray(),
            CashTransactions = rows.Where(row => row.Cash is not null).Select(row => row.Cash!).ToArray(),
            CorporateActions = rows.Where(row => row.CorporateAction is not null).Select(row => row.CorporateAction!).ToArray(),
            Activities = rows.Where(row => row.Activity is not null).Select(row => row.Activity!).ToArray(),
            // Retrieval completeness describes this fetch; never relabel an incomplete response.
            Cursor = incoming.Cursor
        };
        return new BrokerActivityImportResult(incoming, history);
    }

    private static Dictionary<string, BrokerActivityImportRecord> BuildRecords(BrokerageActivitySnapshotDto snapshot)
    {
        var records = new Dictionary<string, BrokerActivityImportRecord>(StringComparer.Ordinal);
        void Add(string id, Func<BrokerActivityImportRecord, BrokerActivityImportRecord> merge)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("A broker activity record has no durable provider ID.");
            records[id] = merge(records.GetValueOrDefault(id) ?? new(id));
        }
        foreach (var fill in snapshot.Fills)
            Add(fill.FillId, row => row.Fill is null || row.Fill == fill ? row with { Fill = fill }
                : throw new InvalidDataException("Duplicate fill ID contains conflicting economics."));
        foreach (var cash in snapshot.CashTransactions)
            Add(cash.TransactionId, row => row.Cash is null || row.Cash == cash ? row with { Cash = cash }
                : throw new InvalidDataException("Duplicate cash activity ID contains conflicting economics."));
        foreach (var action in snapshot.CorporateActions ?? [])
            Add(action.EventId, row => row.CorporateAction is null || row.CorporateAction == action ? row with { CorporateAction = action }
                : throw new InvalidDataException("Duplicate corporate action ID contains conflicting economics."));
        foreach (var source in snapshot.Activities ?? [])
        {
            var activity = source with
            {
                Metadata = source.Metadata?.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };
            Add(activity.EventId, row => row.Activity is null || Serialize(row with { Activity = activity }) == Serialize(row)
                ? row with { Activity = activity } : throw new InvalidDataException("Duplicate activity ID contains conflicting evidence."));
        }
        return records;
    }

    private static async Task<FileStream> AcquireWriterAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 40)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }

    private static string Hash(string value) => Sha256Digest.ComputeUtf8(value);
    private static string Serialize(BrokerActivityImportRecord row) =>
        JsonSerializer.Serialize(row, ExecutionJsonContext.Default.BrokerActivityImportRecord);
}

/// <summary>The current provider observation and the checksum-verified accepted activity history.</summary>
public sealed record BrokerActivityImportResult(
    BrokerageActivitySnapshotDto Current,
    BrokerageActivitySnapshotDto Retained);

internal sealed record BrokerActivityImportRecord(
    string EventId,
    BrokerageFillSnapshotDto? Fill = null,
    BrokerageCashTransactionDto? Cash = null,
    BrokerageCorporateActionSnapshotDto? CorporateAction = null,
    BrokerageActivityEventDto? Activity = null);

internal sealed record BrokerActivityImportCheckpoint(
    string ProviderId,
    string AccountId,
    BrokerageActivityCursorDto? Cursor,
    IReadOnlyDictionary<string, string> ActivityHashes);
