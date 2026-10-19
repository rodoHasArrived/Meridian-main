using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.AssetOperations;

/// <summary>
/// Atomic append-only forecast history partitioned per exact scope and serialized across processes
/// sharing the data root.
/// </summary>
/// <remarks>
/// Layout under <c>asset-operations/account-cash-forecasts</c>: the <c>initialized</c> provisioning
/// marker, a <c>scope-index.json</c> registry of retained scopes (rewritten only when a scope first
/// appears, so a deleted scope file still fails closed), one <c>scopes/&lt;sha256&gt;.json</c> history
/// per scope with its own <c>.lock</c>, and the store-wide <c>snapshots.lock</c> used for provisioning,
/// scope registration and the lossless split of a legacy single-file <c>snapshots.json</c>, which is
/// renamed to <c>snapshots.json.migrated</c> and never deleted. The data root stays host-local;
/// multi-host deployments need the PostgreSQL follow-on rather than a shared file layout.
/// </remarks>
public sealed class FileAccountCashForecastStore : IAccountCashForecastStore
{
    private const string Marker = "meridian-account-cash-forecast-v1";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private readonly string _directory;
    private string LegacyStatePath => Path.Combine(_directory, "snapshots.json");
    private string MigratedLegacyStatePath => LegacyStatePath + ".migrated";
    private string IndexPath => Path.Combine(_directory, "scope-index.json");
    private string ScopeDirectory => Path.Combine(_directory, "scopes");
    private string StoreLockPath => Path.Combine(_directory, "snapshots.lock");
    private string MarkerPath => Path.Combine(_directory, "initialized");

    public FileAccountCashForecastStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _directory = Path.Combine(Path.GetFullPath(dataRoot), "asset-operations", "account-cash-forecasts");
    }

    /// <summary>Canonical file-system-safe identity of one scope; the envelope re-states the scope so a misplaced file fails closed.</summary>
    public static string ScopeFileName(AccountCashForecastScopeDto scope)
    {
        ValidateScope(scope);
        return Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(scope, AccountCashForecastJsonContext.Default.AccountCashForecastScopeDto));
    }

    /// <summary>Explicit provisioning. Loss of an initialized state never resets retained history.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireAsync(StoreLockPath, ct).ConfigureAwait(false);
        if (File.Exists(MarkerPath))
        {
            await RequireMarkerAsync(ct).ConfigureAwait(false);
            await SplitLegacyAsync(ct).ConfigureAwait(false);
            await ValidateAllScopesAsync(ct).ConfigureAwait(false);
            return;
        }
        if (File.Exists(LegacyStatePath) || File.Exists(IndexPath) || HasScopeFiles())
        {
            // Unmarked retained history is validated completely before the marker adopts it;
            // scope files without their index fail closed rather than being adopted silently.
            await SplitLegacyAsync(ct).ConfigureAwait(false);
            await ValidateAllScopesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await RefuseRetainedMigratedHistoryAsync(ct).ConfigureAwait(false);
            Directory.CreateDirectory(ScopeDirectory);
            await WriteIndexAsync([], ct).ConfigureAwait(false);
        }
        await AtomicFileWriter.WriteAsync(MarkerPath, Marker, ct).ConfigureAwait(false);
    }

    public async Task<AccountCashForecastSnapshotDto> AppendAsync(AccountCashForecastSnapshotDto snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var copy = Copy(snapshot);
        ValidateSnapshot(copy);
        var scope = copy.Inputs.Scope;
        await EnsureLegacySplitAsync(ct).ConfigureAwait(false);
        await using var lease = await AcquireAsync(ScopeLockPath(scope), ct).ConfigureAwait(false);
        var (indexed, history) = await ReadScopeAsync(scope, ct).ConfigureAwait(false);
        var existing = history.FirstOrDefault(row => row.Inputs.RunId == copy.Inputs.RunId);
        if (existing is not null)
        {
            if (JsonSerializer.Serialize(existing, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto)
                != JsonSerializer.Serialize(copy, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto))
                throw new InvalidOperationException("The account forecast run identity is already retained with different source content.");
            return Copy(existing);
        }
        ValidateVersionBindings(copy, history);
        ValidatePredecessor(copy, history);
        // Past this point the append is committed. Registration and the first scope-file write are two
        // atomic writes; honouring a request cancellation between them would retain an indexed scope
        // without its file, which then fails closed on every later read. The commit therefore ignores
        // the request token, and the lock timeout still bounds it.
        if (!indexed)
            await RegisterScopeAsync(scope, CancellationToken.None).ConfigureAwait(false);
        await WriteScopeFileAsync(scope, [.. history, copy], CancellationToken.None).ConfigureAwait(false);
        return Copy(copy);
    }

    public async Task<IReadOnlyList<AccountCashForecastSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default)
    {
        ValidateScope(scope);
        await EnsureLegacySplitAsync(ct).ConfigureAwait(false);
        await using var lease = await AcquireAsync(ScopeLockPath(scope), ct).ConfigureAwait(false);
        var (_, history) = await ReadScopeAsync(scope, ct).ConfigureAwait(false);
        return history.Select(Copy).ToArray();
    }

    private async Task RequireMarkerAsync(CancellationToken ct)
    {
        if (!File.Exists(MarkerPath) || await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false) != Marker)
            throw new InvalidOperationException("The durable account forecast initialization marker is unavailable or invalid.");
    }

    /// <summary>Splits a legacy single-file history before any scoped read or append can observe a partial layout.</summary>
    private async Task EnsureLegacySplitAsync(CancellationToken ct)
    {
        if (!File.Exists(LegacyStatePath))
            return;
        await using var lease = await AcquireAsync(StoreLockPath, ct).ConfigureAwait(false);
        // Provisioning alone may adopt unmarked legacy history; scoped use requires the marker first.
        await RequireMarkerAsync(ct).ConfigureAwait(false);
        await SplitLegacyAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Caller holds the store lock. Lossless and idempotent: scope files are written before the legacy file is renamed.</summary>
    private async Task SplitLegacyAsync(CancellationToken ct)
    {
        if (!File.Exists(LegacyStatePath))
            return;
        if (File.Exists(MigratedLegacyStatePath))
            throw new InvalidDataException("Both the legacy account forecast history and its migrated copy exist; restore one retained history before retrying.");
        var legacy = await ReadLegacyAsync(LegacyStatePath, ct).ConfigureAwait(false);
        var index = File.Exists(IndexPath) ? (await ReadIndexAsync(ct).ConfigureAwait(false)).ToList() : [];
        Directory.CreateDirectory(ScopeDirectory);
        foreach (var group in legacy.GroupBy(static row => row.Inputs.Scope))
        {
            var snapshots = group.ToArray();
            var path = ScopeStatePath(group.Key);
            if (File.Exists(path))
            {
                var retained = await ReadScopeFileAsync(group.Key, path, ct).ConfigureAwait(false);
                if (!Sha256Digest.FixedEquals(Hash(retained), Hash(snapshots)))
                    throw new InvalidDataException("A partially split legacy account forecast history conflicts with a retained scope file; restore retained history before retrying.");
            }
            else
            {
                await WriteScopeFileAsync(group.Key, snapshots, ct).ConfigureAwait(false);
            }
            if (!index.Contains(group.Key))
                index.Add(group.Key);
        }
        await WriteIndexAsync([.. index], ct).ConfigureAwait(false);
        File.Move(LegacyStatePath, MigratedLegacyStatePath);
    }

    /// <summary>Caller holds the store lock. Every registered scope and every scope file must validate.</summary>
    private async Task ValidateAllScopesAsync(CancellationToken ct)
    {
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var registered = index.Select(ScopeFileName).ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(ScopeDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(ScopeDirectory, "*.json"))
            {
                if (!registered.Contains(Path.GetFileNameWithoutExtension(file)))
                    throw new InvalidDataException("An unregistered account forecast scope file exists; restore retained history before retrying.");
            }
        }
        foreach (var scope in index)
            _ = await ReadScopeFileAsync(scope, ScopeStatePath(scope), ct).ConfigureAwait(false);
    }

    /// <summary>Caller holds the scope lock. Registered scopes must have their file; unregistered files are refused.</summary>
    private async Task<(bool Indexed, AccountCashForecastSnapshotDto[] History)> ReadScopeAsync(AccountCashForecastScopeDto scope, CancellationToken ct)
    {
        await RequireMarkerAsync(ct).ConfigureAwait(false);
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var path = ScopeStatePath(scope);
        if (!index.Contains(scope))
        {
            if (File.Exists(path))
                throw new InvalidDataException("The durable account forecast state is unavailable or invalid; restore retained history before retrying.");
            return (false, []);
        }
        if (!File.Exists(path))
            throw new InvalidDataException("The durable account forecast state is unavailable or invalid; restore retained history before retrying.");
        return (true, await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false));
    }

    private static async Task<AccountCashForecastSnapshotDto[]> ReadScopeFileAsync(AccountCashForecastScopeDto scope, string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, AccountCashForecastStoreJsonContext.Default.AccountCashForecastScopeEnvelope)
                ?? throw new InvalidDataException("The retained account forecast scope document is empty.");
            if (envelope.SchemaVersion != 1 || envelope.Scope != scope || envelope.Snapshots is null || !Sha256Digest.IsCanonical(envelope.ContentHash)
                || !Sha256Digest.FixedEquals(envelope.ContentHash, Hash(envelope.Snapshots))
                || envelope.Snapshots.Any(row => row.Inputs.Scope != scope))
                throw new InvalidDataException("The retained account forecast scope document failed integrity or schema validation.");
            return ValidateHistory(envelope.Snapshots);
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidDataException("The durable account forecast state is unavailable or invalid; restore retained history before retrying.", exception);
        }
    }

    /// <summary>
    /// Caller holds the store lock. A migrated legacy file is retained evidence: when the marker, index
    /// and scope files are lost beside it, provisioning fails closed instead of restarting history.
    /// </summary>
    private async Task RefuseRetainedMigratedHistoryAsync(CancellationToken ct)
    {
        if (!File.Exists(MigratedLegacyStatePath))
            return;
        var migrated = await ReadLegacyAsync(MigratedLegacyStatePath, ct).ConfigureAwait(false);
        if (migrated.Length != 0)
            throw new InvalidOperationException("Migrated account forecast history exists without its provisioned scope layout; restore the complete retained history before provisioning.");
    }

    private static async Task<AccountCashForecastSnapshotDto[]> ReadLegacyAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, AccountCashForecastStoreJsonContext.Default.AccountCashForecastStoreEnvelope)
                ?? throw new InvalidDataException("The retained account forecast document is empty.");
            if (envelope.SchemaVersion != 1 || envelope.Snapshots is null || !Sha256Digest.IsCanonical(envelope.ContentHash)
                || !Sha256Digest.FixedEquals(envelope.ContentHash, Hash(envelope.Snapshots)))
                throw new InvalidDataException("The retained account forecast document failed integrity or schema validation.");
            return ValidateHistory(envelope.Snapshots);
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidDataException("The durable account forecast state is unavailable or invalid; restore retained history before retrying.", exception);
        }
    }

    private static AccountCashForecastSnapshotDto[] ValidateHistory(AccountCashForecastSnapshotDto[] snapshots)
    {
        var seen = new List<AccountCashForecastSnapshotDto>();
        foreach (var snapshot in snapshots)
        {
            ValidateSnapshot(snapshot);
            if (seen.Any(row => row.SnapshotId == snapshot.SnapshotId || (row.Inputs.Scope == snapshot.Inputs.Scope && row.Inputs.RunId == snapshot.Inputs.RunId)))
                throw new InvalidDataException("The retained account forecast contains duplicate snapshot or run identities.");
            ValidateVersionBindings(snapshot, seen);
            ValidatePredecessor(snapshot, seen);
            seen.Add(snapshot);
        }
        return snapshots;
    }

    private async Task<AccountCashForecastScopeDto[]> ReadIndexAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await ReadSharedAsync(IndexPath, ct).ConfigureAwait(false);
            var index = JsonSerializer.Deserialize(bytes, AccountCashForecastStoreJsonContext.Default.AccountCashForecastScopeIndex)
                ?? throw new InvalidDataException("The retained account forecast scope index is empty.");
            if (index.SchemaVersion != 1 || index.Scopes is null || !Sha256Digest.IsCanonical(index.ContentHash)
                || !Sha256Digest.FixedEquals(index.ContentHash, Hash(index.Scopes))
                || index.Scopes.Distinct().Count() != index.Scopes.Length)
                throw new InvalidDataException("The retained account forecast scope index failed integrity or schema validation.");
            foreach (var scope in index.Scopes)
                ValidateScope(scope);
            return index.Scopes;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidDataException("The durable account forecast scope index is unavailable or invalid; restore retained history before retrying.", exception);
        }
    }


    /// <summary>
    /// Index readers run under their own scope lock while a first-time registration under the store
    /// lock replaces the file atomically; sharing delete lets that rename proceed on Windows instead
    /// of failing the registration with a sharing violation. The rename is atomic, so a reader sees
    /// either the complete old or the complete new index, and the content hash is verified anyway.
    /// </summary>
    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>Caller holds the scope lock; registration takes the store lock so scopes only serialize on first appearance.</summary>
    private async Task RegisterScopeAsync(AccountCashForecastScopeDto scope, CancellationToken ct)
    {
        await using var lease = await AcquireAsync(StoreLockPath, ct).ConfigureAwait(false);
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        if (index.Contains(scope))
            return;
        await WriteIndexAsync([.. index, scope], ct).ConfigureAwait(false);
    }

    private Task WriteIndexAsync(AccountCashForecastScopeDto[] scopes, CancellationToken ct)
    {
        var ordered = scopes.OrderBy(ScopeFileName, StringComparer.Ordinal).ToArray();
        var index = new AccountCashForecastScopeIndex(1, Hash(ordered), ordered);
        return AtomicFileWriter.WriteAsync(IndexPath,
            JsonSerializer.SerializeToUtf8Bytes(index, AccountCashForecastStoreJsonContext.Default.AccountCashForecastScopeIndex), ct);
    }

    private Task WriteScopeFileAsync(AccountCashForecastScopeDto scope, AccountCashForecastSnapshotDto[] snapshots, CancellationToken ct)
    {
        var envelope = new AccountCashForecastScopeEnvelope(1, scope, Hash(snapshots), snapshots);
        return AtomicFileWriter.WriteAsync(ScopeStatePath(scope),
            JsonSerializer.SerializeToUtf8Bytes(envelope, AccountCashForecastStoreJsonContext.Default.AccountCashForecastScopeEnvelope), ct);
    }

    private bool HasScopeFiles() => Directory.Exists(ScopeDirectory) && Directory.EnumerateFiles(ScopeDirectory, "*.json").Any();
    private string ScopeStatePath(AccountCashForecastScopeDto scope) => Path.Combine(ScopeDirectory, ScopeFileName(scope) + ".json");
    private string ScopeLockPath(AccountCashForecastScopeDto scope) => Path.Combine(ScopeDirectory, ScopeFileName(scope) + ".lock");

    private async Task<FileStream> AcquireAsync(string lockPath, CancellationToken ct)
    {
        if (!Directory.Exists(_directory))
            throw new InvalidOperationException("The durable account forecast store is unavailable or has not been provisioned.");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 11 or 32 or 33)
            {
                if (timer.Elapsed > LockTimeout)
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
    private static string Hash(AccountCashForecastScopeDto[] scopes) =>
        Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(scopes, AccountCashForecastStoreJsonContext.Default.AccountCashForecastScopeDtoArray));
    private static AccountCashForecastSnapshotDto Copy(AccountCashForecastSnapshotDto snapshot) =>
        JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(snapshot, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto),
            AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto)!;
}

/// <summary>Legacy single-file layout, read only for the lossless split into scope files.</summary>
internal sealed record AccountCashForecastStoreEnvelope(int SchemaVersion, string ContentHash, AccountCashForecastSnapshotDto[] Snapshots);
internal sealed record AccountCashForecastScopeEnvelope(int SchemaVersion, AccountCashForecastScopeDto Scope, string ContentHash, AccountCashForecastSnapshotDto[] Snapshots);
internal sealed record AccountCashForecastScopeIndex(int SchemaVersion, string ContentHash, AccountCashForecastScopeDto[] Scopes);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AccountCashForecastStoreEnvelope))]
[JsonSerializable(typeof(AccountCashForecastScopeEnvelope))]
[JsonSerializable(typeof(AccountCashForecastScopeIndex))]
[JsonSerializable(typeof(AccountCashForecastScopeDto[]))]
internal partial class AccountCashForecastStoreJsonContext : JsonSerializerContext;
