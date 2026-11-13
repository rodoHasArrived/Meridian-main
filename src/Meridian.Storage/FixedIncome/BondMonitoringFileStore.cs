using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.FixedIncome;

/// <summary>Atomic host-local history; explicit capture initializes storage and never rewrites old snapshots.</summary>
public sealed class BondMonitoringFileStore(string dataRoot) : IBondMonitoringStore
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(dataRoot), "fixed-income", "bond-monitoring");
    private string StatePath => Path.Combine(_directory, "history.json");
    private string MarkerPath => Path.Combine(_directory, "initialized");

    public async Task<BondMonitoringSnapshotDto> AppendAsync(BondMonitoringSnapshotDto snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Inputs?.Scope is null || snapshot.Issues.Count != 0
            || snapshot.SnapshotId != Sha256Digest.ComputeUtf8($"{snapshot.EngineVersion}|{Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(snapshot.Inputs, BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest))}"))
            throw new InvalidDataException("Only valid hash-bound bond monitoring snapshots can be retained.");
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var all = await ReadAsync(ct).ConfigureAwait(false);
        var sameRun = all.FirstOrDefault(row => row.Inputs.Scope == snapshot.Inputs.Scope && row.Inputs.RunId == snapshot.Inputs.RunId);
        if (sameRun is not null)
        {
            if (sameRun.SnapshotId != snapshot.SnapshotId)
                throw new InvalidOperationException("A monitoring run identity cannot be reused with different inputs.");
            return sameRun;
        }
        var previous = all.LastOrDefault(row => row.Inputs.Scope == snapshot.Inputs.Scope);
        if (snapshot.Inputs.PreviousSnapshotId != previous?.SnapshotId)
            throw new InvalidOperationException("Monitoring revisions must link the current retained account/book snapshot.");
        if (previous is not null && snapshot.Inputs.RecordedAtUtc < previous.Inputs.RecordedAtUtc)
            throw new InvalidOperationException("Monitoring revisions cannot precede their retained parent.");
        // Previously observed cash cannot disappear from a correction or be silently rewritten.
        if (previous is not null)
        {
            foreach (var payment in previous.Inputs.Payments)
            {
                var retained = snapshot.Inputs.Payments.SingleOrDefault(row => row.VersionId == payment.VersionId);
                if (retained is null || JsonSerializer.Serialize(payment, AccountCashForecastJsonContext.Default.AccountCashFlowDto)
                    != JsonSerializer.Serialize(retained, AccountCashForecastJsonContext.Default.AccountCashFlowDto))
                    throw new InvalidOperationException("Monitoring corrections must preserve earlier observed cash versions.");
            }
            foreach (var observation in previous.Inputs.Observations)
                if (!snapshot.Inputs.Observations.Contains(observation))
                    throw new InvalidOperationException("Monitoring corrections must retain earlier source observations and explicit supersession.");
        }
        var rows = all.Append(snapshot).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(rows, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDtoArray);
        var envelope = new BondMonitoringEnvelope(1, Sha256Digest.Compute(bytes), rows);
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(MarkerPath))
            await AtomicFileWriter.WriteAsync(MarkerPath, "bond-monitoring-v1", CancellationToken.None).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(StatePath,
            JsonSerializer.Serialize(envelope, BondMonitoringStoreJsonContext.Default.BondMonitoringEnvelope), CancellationToken.None).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<IReadOnlyList<BondMonitoringSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Directory.Exists(_directory))
            return [];
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        return (await ReadAsync(ct).ConfigureAwait(false)).Where(row => row.Inputs.Scope == scope).ToArray();
    }

    private async Task<BondMonitoringSnapshotDto[]> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(StatePath) && !File.Exists(MarkerPath))
            return [];
        if (!File.Exists(StatePath) || !File.Exists(MarkerPath) || await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false) != "bond-monitoring-v1")
            throw new InvalidDataException("Initialized bond monitoring history is incomplete; restore retained evidence.");
        try
        {
            var envelope = JsonSerializer.Deserialize(await File.ReadAllTextAsync(StatePath, ct).ConfigureAwait(false),
                BondMonitoringStoreJsonContext.Default.BondMonitoringEnvelope);
            if (envelope is null || envelope.SchemaVersion != 1 || envelope.Snapshots is null
                || !Sha256Digest.FixedEquals(envelope.ContentHash, Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(
                    envelope.Snapshots, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDtoArray))))
                throw new InvalidDataException("Retained bond monitoring history failed its content hash.");
            return envelope.Snapshots;
        }
        catch (JsonException error) { throw new InvalidDataException("Retained bond monitoring history is unreadable.", error); }
    }

    private async Task<FileStream> AcquireAsync(CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(Path.Combine(_directory, "history.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(30)) { await Task.Delay(25, ct).ConfigureAwait(false); }
        }
    }
}

internal sealed record BondMonitoringEnvelope(int SchemaVersion, string ContentHash, BondMonitoringSnapshotDto[] Snapshots);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondMonitoringEnvelope))]
internal partial class BondMonitoringStoreJsonContext : JsonSerializerContext;
