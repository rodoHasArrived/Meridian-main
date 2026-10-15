using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.AssetOperations;

/// <summary>Atomic append-only forecast history serialized across processes sharing the data root.</summary>
public sealed class FileAccountCashForecastStore : IAccountCashForecastStore
{
    private const string Marker = "meridian-account-cash-forecast-v1";
    private readonly string _directory;
    private string StatePath => Path.Combine(_directory, "snapshots.json");
    private string MarkerPath => Path.Combine(_directory, "initialized");

    public FileAccountCashForecastStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _directory = Path.Combine(Path.GetFullPath(dataRoot), "asset-operations", "account-cash-forecasts");
    }

    /// <summary>Explicit provisioning. Loss of an initialized state never resets retained history.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        if (File.Exists(MarkerPath))
        {
            _ = await ReadAsync(ct).ConfigureAwait(false);
            return;
        }
        if (File.Exists(StatePath))
            _ = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        else
            await WriteAsync([], ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(MarkerPath, Marker, ct).ConfigureAwait(false);
    }

    public async Task<AccountCashForecastSnapshotDto> AppendAsync(AccountCashForecastSnapshotDto snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var copy = Copy(snapshot);
        ValidateSnapshot(copy);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var history = await ReadAsync(ct).ConfigureAwait(false);
        var existing = history.FirstOrDefault(row => row.Inputs.Scope == copy.Inputs.Scope && row.Inputs.RunId == copy.Inputs.RunId);
        if (existing is not null)
        {
            if (JsonSerializer.Serialize(existing, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto)
                != JsonSerializer.Serialize(copy, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto))
                throw new InvalidOperationException("The account forecast run identity is already retained with different source content.");
            return Copy(existing);
        }
        ValidateVersionBindings(copy, history);
        ValidatePredecessor(copy, history);
        await WriteAsync([.. history, copy], ct).ConfigureAwait(false);
        return Copy(copy);
    }

    public async Task<IReadOnlyList<AccountCashForecastSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default)
    {
        ValidateScope(scope);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var history = await ReadAsync(ct).ConfigureAwait(false);
        return history.Where(row => row.Inputs.Scope == scope).Select(Copy).ToArray();
    }

    private async Task<AccountCashForecastSnapshotDto[]> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(MarkerPath) || await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false) != Marker)
            throw new InvalidOperationException("The durable account forecast initialization marker is unavailable or invalid.");
        return await ReadSnapshotAsync(ct).ConfigureAwait(false);
    }

    private async Task<AccountCashForecastSnapshotDto[]> ReadSnapshotAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(StatePath, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, AccountCashForecastStoreJsonContext.Default.AccountCashForecastStoreEnvelope)
                ?? throw new InvalidDataException("The retained account forecast document is empty.");
            if (envelope.SchemaVersion != 1 || envelope.Snapshots is null || !Sha256Digest.IsCanonical(envelope.ContentHash)
                || !Sha256Digest.FixedEquals(envelope.ContentHash, Hash(envelope.Snapshots)))
                throw new InvalidDataException("The retained account forecast document failed integrity or schema validation.");
            var seen = new List<AccountCashForecastSnapshotDto>();
            foreach (var snapshot in envelope.Snapshots)
            {
                ValidateSnapshot(snapshot);
                if (seen.Any(row => row.SnapshotId == snapshot.SnapshotId || (row.Inputs.Scope == snapshot.Inputs.Scope && row.Inputs.RunId == snapshot.Inputs.RunId)))
                    throw new InvalidDataException("The retained account forecast contains duplicate snapshot or run identities.");
                ValidateVersionBindings(snapshot, seen);
                ValidatePredecessor(snapshot, seen);
                seen.Add(snapshot);
            }
            return envelope.Snapshots;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidDataException("The durable account forecast state is unavailable or invalid; restore retained history before retrying.", exception);
        }
    }

    private async Task WriteAsync(AccountCashForecastSnapshotDto[] snapshots, CancellationToken ct)
    {
        var envelope = new AccountCashForecastStoreEnvelope(1, Hash(snapshots), snapshots);
        await AtomicFileWriter.WriteAsync(StatePath,
            JsonSerializer.SerializeToUtf8Bytes(envelope, AccountCashForecastStoreJsonContext.Default.AccountCashForecastStoreEnvelope), ct).ConfigureAwait(false);
    }

    private async Task<FileStream> AcquireAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory))
            throw new InvalidOperationException("The durable account forecast store is unavailable or has not been provisioned.");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(Path.Combine(_directory, "snapshots.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 11 or 32 or 33)
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(30))
                    throw new TimeoutException("Timed out acquiring the account forecast history writer lock.", exception);
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private static void ValidateSnapshot(AccountCashForecastSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateScope(snapshot.Inputs.Scope);
        var requestHash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(snapshot.Inputs, AccountCashForecastJsonContext.Default.AccountCashForecastRequestDto));
        var expected = Sha256Digest.ComputeUtf8($"{snapshot.EngineVersion}|{requestHash}");
        if (string.IsNullOrWhiteSpace(snapshot.Inputs.RunId) || snapshot.Inputs.RecordedAtUtc == default || snapshot.Inputs.RecordedAtUtc.Offset != TimeSpan.Zero
            || !Sha256Digest.IsCanonical(snapshot.SnapshotId) || snapshot.SnapshotId != snapshot.EvidenceHash || !Sha256Digest.FixedEquals(snapshot.EvidenceHash, expected))
            throw new InvalidDataException("Retained forecast identity, source fingerprint or UTC recording time is invalid.");
    }

    private static void ValidateVersionBindings(AccountCashForecastSnapshotDto snapshot, IReadOnlyList<AccountCashForecastSnapshotDto> history)
    {
        var retained = history.Where(row => row.Inputs.Scope == snapshot.Inputs.Scope
            && row.Inputs.PeriodStart == snapshot.Inputs.PeriodStart && row.Inputs.PeriodEnd == snapshot.Inputs.PeriodEnd)
            .SelectMany(static row => row.Inputs.Flows)
            .ToLookup(static flow => flow.VersionId, StringComparer.Ordinal);
        foreach (var flow in snapshot.Inputs.Flows)
        {
            // Version identity belongs to the full retained period, even when the latest run omitted
            // the flow or a caller attempts to rebind its status or stable flow identity.
            if (retained[flow.VersionId].Any(prior => prior != flow))
                throw new InvalidOperationException("A retained source revision identity cannot be reused with changed flow content.");
        }
    }

    private static void ValidatePredecessor(AccountCashForecastSnapshotDto snapshot, IReadOnlyList<AccountCashForecastSnapshotDto> history)
    {
        var latest = history.LastOrDefault(row => row.Inputs.Scope == snapshot.Inputs.Scope
            && row.Inputs.PeriodStart == snapshot.Inputs.PeriodStart && row.Inputs.PeriodEnd == snapshot.Inputs.PeriodEnd);
        if (latest is null)
        {
            if (snapshot.Inputs.PreviousSnapshotId is not null || snapshot.Inputs.Flows.Any(static flow => flow.SupersedesVersionId is not null))
                throw new InvalidOperationException("The predecessor forecast is absent or belongs to another account, book or cash period.");
            return;
        }
        if (snapshot.Inputs.PreviousSnapshotId != latest.SnapshotId || string.IsNullOrWhiteSpace(snapshot.Inputs.CorrectionRationale)
            || snapshot.Inputs.RecordedAtUtc < latest.Inputs.RecordedAtUtc)
            throw new InvalidOperationException("A forecast correction must link the latest retained account/book/period snapshot and retain its rationale.");
        var retainedFlows = history.Where(row => row.Inputs.Scope == snapshot.Inputs.Scope
            && row.Inputs.PeriodStart == snapshot.Inputs.PeriodStart && row.Inputs.PeriodEnd == snapshot.Inputs.PeriodEnd)
            .SelectMany(static row => row.Inputs.Flows)
            .ToLookup(static flow => (flow.FlowId, flow.Status));
        foreach (var revised in snapshot.Inputs.Flows)
        {
            // Omitting a flow from a selected run does not discard its revision lineage.
            // Expected and observed versions remain separate histories under the stable flow ID.
            var prior = retainedFlows[(revised.FlowId, revised.Status)].LastOrDefault();
            if (prior is not null && prior.VersionId == revised.VersionId)
            {
                if (prior != revised)
                    throw new InvalidOperationException("Changed flow content requires a new source revision identity.");
                // The immutable revision keeps its original supersession pointer on later snapshots.
                continue;
            }
            if (prior is not null && prior.VersionId != revised.VersionId && revised.SupersedesVersionId != prior.VersionId)
                throw new InvalidOperationException("A changed prediction or observation must link the prior retained source revision.");
            if (revised.SupersedesVersionId is not null
                && (prior is null || prior.VersionId != revised.SupersedesVersionId || prior.VersionId == revised.VersionId))
                throw new InvalidOperationException("A restated flow must link its prior retained prediction with the same stable flow identity and status.");
        }
    }

    private static void ValidateScope(AccountCashForecastScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (new[] { scope.TenantId, scope.CompanyId, scope.AccountId, scope.BookId }.Any(static value => string.IsNullOrWhiteSpace(value) || value != value.Trim())
            || scope.BaseCurrency is not { Length: 3 } || !scope.BaseCurrency.All(static ch => ch is >= 'A' and <= 'Z'))
            throw new ArgumentException("Explicit tenant, company, account, book and base currency scope is required.", nameof(scope));
    }

    private static string Hash(AccountCashForecastSnapshotDto[] snapshots) =>
        Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(snapshots, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDtoArray));
    private static AccountCashForecastSnapshotDto Copy(AccountCashForecastSnapshotDto snapshot) =>
        JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(snapshot, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto),
            AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto)!;
}

internal sealed record AccountCashForecastStoreEnvelope(int SchemaVersion, string ContentHash, AccountCashForecastSnapshotDto[] Snapshots);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AccountCashForecastStoreEnvelope))]
internal partial class AccountCashForecastStoreJsonContext : JsonSerializerContext;
