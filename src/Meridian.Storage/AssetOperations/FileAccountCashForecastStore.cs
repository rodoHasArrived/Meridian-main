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
    private string MigrationBaselineDirectory => Path.Combine(_directory, "migration-baselines");
    private string MigrationBaselinePath(AccountCashForecastScopeDto scope) => Path.Combine(MigrationBaselineDirectory, ScopeFileName(scope) + ".json");
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
        if (indexed)
            await WriteScopeFileAsync(scope, [.. history, copy], CancellationToken.None).ConfigureAwait(false);
        else
            await RegisterAndWriteScopeAsync(scope, [.. history, copy]).ConfigureAwait(false);
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
        if (!File.Exists(MarkerPath) || System.Text.Encoding.UTF8.GetString(await ReadSharedAsync(MarkerPath, ct).ConfigureAwait(false)) != Marker)
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
        // File-first commits cannot leave a registered scope without its file. A legacy snapshot
        // cannot prove that no later appends were lost, so refuse before writing any recovery state.
        if (index.Any(scope => !File.Exists(ScopeStatePath(scope))))
            throw new InvalidDataException("A registered account forecast scope file is missing; restore retained history before retrying.");
        var histories = legacy.GroupBy(static row => row.Inputs.Scope).ToDictionary(group => group.Key, group => group.ToArray());
        if (Directory.Exists(MigrationBaselineDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(MigrationBaselineDirectory, "*.json"))
            {
                var scope = await ReadUnregisteredScopeAsync(file, Path.GetFileNameWithoutExtension(file), ct).ConfigureAwait(false);
                var baseline = await ReadScopeFileAsync(scope, file, ct).ConfigureAwait(false);
                // Baselines are committed after their active file. They are independent evidence of
                // retained history even when its index entry was also lost in a partial restore.
                if ((baseline.Length != 0) && !File.Exists(ScopeStatePath(scope)))
                    throw new InvalidDataException("A retained migration baseline has no active scope file; restore the complete backup set before retrying.");
                ValidateMigratedPrefix(baseline, histories.GetValueOrDefault(scope, []));
            }
        }
        foreach (var (scope, snapshots) in histories)
        {
            var path = ScopeStatePath(scope);
            if (File.Exists(path))
                ValidateMigratedPrefix(snapshots, await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false));
        }
        Directory.CreateDirectory(ScopeDirectory);
        foreach (var (scope, snapshots) in histories)
        {
            if (!File.Exists(ScopeStatePath(scope)))
                await WriteScopeFileAsync(scope, snapshots, ct).ConfigureAwait(false);
            await WriteMigrationBaselineAsync(scope, snapshots, ct).ConfigureAwait(false);
            if (!index.Contains(scope))
                index.Add(scope);
        }
        await WriteIndexAsync([.. index], ct).ConfigureAwait(false);
        File.Move(LegacyStatePath, MigratedLegacyStatePath);
    }

    /// <summary>
    /// Caller holds the store lock. Every registered scope and every scope file must validate. A scope file
    /// without its index entry is an interrupted first commit (the file is committed before its
    /// registration): it must name its own scope and validate for it, and is then registered.
    /// </summary>
    private async Task ValidateAllScopesAsync(CancellationToken ct)
    {
        var index = (await ReadIndexAsync(ct).ConfigureAwait(false)).ToList();
        var registered = index.Select(ScopeFileName).ToHashSet(StringComparer.Ordinal);
        var recovered = false;
        if (Directory.Exists(ScopeDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(ScopeDirectory, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (registered.Contains(name))
                    continue;
                index.Add(await ReadUnregisteredScopeAsync(file, name, ct).ConfigureAwait(false));
                registered.Add(name);
                recovered = true;
            }
        }
        var migrated = File.Exists(MigratedLegacyStatePath)
            ? await ReadLegacyAsync(MigratedLegacyStatePath, ct).ConfigureAwait(false) : [];
        foreach (var scope in migrated.Select(static row => row.Inputs.Scope).Distinct())
        {
            if (!index.Contains(scope))
                throw new InvalidDataException("The migrated account forecast history retains a scope that is no longer registered; restore retained history before retrying.");
        }
        // Per-scope baselines remain retained evidence even if the global migrated file is lost.
        if (Directory.Exists(MigrationBaselineDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(MigrationBaselineDirectory, "*.json"))
            {
                var scope = await ReadUnregisteredScopeAsync(file, Path.GetFileNameWithoutExtension(file), ct).ConfigureAwait(false);
                var baseline = await ReadScopeFileAsync(scope, file, ct).ConfigureAwait(false);
                if ((baseline.Length != 0) && !index.Contains(scope))
                    throw new InvalidDataException("Retained migration evidence has no active scope; restore retained history before retrying.");
            }
        }
        var baselines = new Dictionary<AccountCashForecastScopeDto, AccountCashForecastSnapshotDto[]>();
        var hasMigrated = File.Exists(MigratedLegacyStatePath);
        foreach (var scope in index)
        {
            var retained = await ReadScopeFileAsync(scope, ScopeStatePath(scope), ct).ConfigureAwait(false);
            var migratedScope = migrated.Where(row => row.Inputs.Scope == scope).ToArray();
            ValidateMigratedPrefix(migratedScope, retained);
            var baselinePath = MigrationBaselinePath(scope);
            if (File.Exists(baselinePath))
            {
                var baseline = await ReadScopeFileAsync(scope, baselinePath, ct).ConfigureAwait(false);
                ValidateMigratedPrefix(baseline, retained);
                if (hasMigrated)
                    ValidateMigratedPrefix(baseline, migratedScope);
            }
            if (hasMigrated)
                baselines.Add(scope, migratedScope);
        }
        foreach (var (scope, baseline) in baselines)
            await WriteMigrationBaselineAsync(scope, baseline, ct).ConfigureAwait(false);
        if (recovered)
            await WriteIndexAsync([.. index], ct).ConfigureAwait(false);
    }

    /// <summary>Retained migration evidence is an immutable prefix, including snapshot outputs.</summary>
    private static void ValidateMigratedPrefix(AccountCashForecastSnapshotDto[] migrated, AccountCashForecastSnapshotDto[] retained)
    {
        if (retained.Length < migrated.Length
            || !Sha256Digest.FixedEquals(Hash(migrated), Hash(retained.Take(migrated.Length).ToArray())))
            throw new InvalidDataException("The retained account forecast history conflicts with its migrated prefix; restore retained history before retrying.");
    }

    /// <summary>
    /// Scoped requests consult only their frozen migration baseline. Older partitioned layouts lazily
    /// materialize that evidence under the store lease after checking the active history; provisioning
    /// still checks the complete migrated artifact against every scope and baseline.
    /// </summary>
    private async Task ValidateMigratedScopeAsync(AccountCashForecastScopeDto scope, AccountCashForecastSnapshotDto[] retained, CancellationToken ct)
    {
        var baselinePath = MigrationBaselinePath(scope);
        if (File.Exists(baselinePath))
        {
            ValidateMigratedPrefix(await ReadScopeFileAsync(scope, baselinePath, ct).ConfigureAwait(false), retained);
            return;
        }
        if (!File.Exists(MigratedLegacyStatePath))
            return;
        await using var lease = await AcquireAsync(StoreLockPath, ct).ConfigureAwait(false);
        if (File.Exists(baselinePath))
        {
            ValidateMigratedPrefix(await ReadScopeFileAsync(scope, baselinePath, ct).ConfigureAwait(false), retained);
            return;
        }
        var migrated = await ReadLegacyAsync(MigratedLegacyStatePath, ct).ConfigureAwait(false);
        var baseline = migrated.Where(row => row.Inputs.Scope == scope).ToArray();
        ValidateMigratedPrefix(baseline, retained);
        await WriteMigrationBaselineAsync(scope, baseline, ct).ConfigureAwait(false);
    }

    /// <summary>A scope retained in migration evidence cannot restart as new.</summary>
    private Task RequireNotMigratedScopeAsync(AccountCashForecastScopeDto scope, CancellationToken ct)
        => ValidateMigratedScopeAsync(scope, [], ct);

    /// <summary>An unregistered scope file must name the scope its file name was derived from and validate for it.</summary>
    private static async Task<AccountCashForecastScopeDto> ReadUnregisteredScopeAsync(string path, string name, CancellationToken ct)
    {
        AccountCashForecastScopeDto? scope;
        try
        {
            scope = JsonSerializer.Deserialize(await ReadSharedAsync(path, ct).ConfigureAwait(false), AccountCashForecastStoreJsonContext.Default.AccountCashForecastScopeEnvelope)?.Scope;
            if (scope is not null)
                ValidateScope(scope);
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException)
        {
            throw new InvalidDataException("An unregistered account forecast scope file is unreadable; restore retained history before retrying.", exception);
        }
        if (scope is null || ScopeFileName(scope) != name)
            throw new InvalidDataException("An unregistered account forecast scope file does not name its own scope; restore retained history before retrying.");
        _ = await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false);
        return scope;
    }

    /// <summary>
    /// Caller holds the scope lock. Registered scopes must have their file. A file for this exact scope
    /// without its index entry is an interrupted first commit and is registered here; any other
    /// unregistered file fails closed.
    /// </summary>
    private async Task<(bool Indexed, AccountCashForecastSnapshotDto[] History)> ReadScopeAsync(AccountCashForecastScopeDto scope, CancellationToken ct)
    {
        await RequireMarkerAsync(ct).ConfigureAwait(false);
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var path = ScopeStatePath(scope);
        if (index.Contains(scope))
        {
            if (!File.Exists(path))
                throw new InvalidDataException("The durable account forecast state is unavailable or invalid; restore retained history before retrying.");
            var retained = await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false);
            await ValidateMigratedScopeAsync(scope, retained, ct).ConfigureAwait(false);
            return (true, retained);
        }
        if (!File.Exists(path))
        {
            await RequireNotMigratedScopeAsync(scope, ct).ConfigureAwait(false);
            return (false, []);
        }
        var history = await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false);
        await ValidateMigratedScopeAsync(scope, history, ct).ConfigureAwait(false);
        await RegisterScopeAsync(scope).ConfigureAwait(false);
        return (true, history);
    }

    private static async Task<AccountCashForecastSnapshotDto[]> ReadScopeFileAsync(AccountCashForecastScopeDto scope, string path, CancellationToken ct)
    {
        try
        {
            var bytes = await ReadSharedAsync(path, ct).ConfigureAwait(false);
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
    /// Every migration baseline is the same evidence, even an empty one: it is written only once a
    /// scope was used after migration, so the scope may have acquired durable history since.
    /// </summary>
    private async Task RefuseRetainedMigratedHistoryAsync(CancellationToken ct)
    {
        if (File.Exists(MigratedLegacyStatePath))
        {
            var migrated = await ReadLegacyAsync(MigratedLegacyStatePath, ct).ConfigureAwait(false);
            if (migrated.Length != 0)
                throw new InvalidOperationException("Unprovisioned migrated history requires explicit recovery.");
        }
        if (Directory.Exists(MigrationBaselineDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(MigrationBaselineDirectory, "*.json"))
            {
                var scope = await ReadUnregisteredScopeAsync(file, Path.GetFileNameWithoutExtension(file), ct).ConfigureAwait(false);
                await ReadScopeFileAsync(scope, file, ct).ConfigureAwait(false);
                throw new InvalidOperationException("Unprovisioned migration baseline evidence requires explicit recovery.");
            }
        }
    }

    private static async Task<AccountCashForecastSnapshotDto[]> ReadLegacyAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await ReadSharedAsync(path, ct).ConfigureAwait(false);
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
    /// Every store read opens with delete sharing. Index readers run under their own scope lock while a
    /// first-time registration under the store lock replaces the index atomically, and provisioning
    /// validates every scope file under the store lock while a writer holding only that scope's lock may
    /// replace it; sharing delete lets those renames proceed on Windows instead of failing the writer
    /// with a sharing violation. Each rename is atomic, so a reader sees either the complete old or the
    /// complete new file, and the content hash is verified anyway.
    /// </summary>
    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Caller holds the scope lock. The store lease spans the first scope-file write and its registration,
    /// so scopes serialize only on first appearance and an initializer validating every scope under the
    /// same lease sees either nothing or the complete commit.
    /// </summary>
    private async Task RegisterAndWriteScopeAsync(AccountCashForecastScopeDto scope, AccountCashForecastSnapshotDto[] snapshots)
    {
        await using var lease = await AcquireAsync(StoreLockPath, CancellationToken.None).ConfigureAwait(false);
        // The scope file is committed first: a crash or I/O failure between the two writes leaves a valid
        // unregistered file that the next provisioning or scoped use registers, never an indexed scope
        // without its file.
        await WriteScopeFileAsync(scope, snapshots, CancellationToken.None).ConfigureAwait(false);
        await RegisterLockedAsync(scope).ConfigureAwait(false);
    }

    /// <summary>Caller holds the scope lock. Completes an interrupted first commit by registering its scope file.</summary>
    private async Task RegisterScopeAsync(AccountCashForecastScopeDto scope)
    {
        await using var lease = await AcquireAsync(StoreLockPath, CancellationToken.None).ConfigureAwait(false);
        await RegisterLockedAsync(scope).ConfigureAwait(false);
    }

    /// <summary>Caller holds the store lock.</summary>
    private async Task RegisterLockedAsync(AccountCashForecastScopeDto scope)
    {
        var index = await ReadIndexAsync(CancellationToken.None).ConfigureAwait(false);
        if (!index.Contains(scope))
            await WriteIndexAsync([.. index, scope], CancellationToken.None).ConfigureAwait(false);
    }

    private Task WriteIndexAsync(AccountCashForecastScopeDto[] scopes, CancellationToken ct)
    {
        var ordered = scopes.OrderBy(ScopeFileName, StringComparer.Ordinal).ToArray();
        var index = new AccountCashForecastScopeIndex(1, Hash(ordered), ordered);
        return AtomicFileWriter.WriteAsync(IndexPath,
            JsonSerializer.SerializeToUtf8Bytes(index, AccountCashForecastStoreJsonContext.Default.AccountCashForecastScopeIndex), ct);
    }

    /// <summary>Caller holds the store lease. Baselines only advance and unchanged evidence is not rewritten.</summary>
    private async Task WriteMigrationBaselineAsync(AccountCashForecastScopeDto scope, AccountCashForecastSnapshotDto[] baseline, CancellationToken ct)
    {
        var path = MigrationBaselinePath(scope);
        if (File.Exists(path))
        {
            var previous = await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false);
            ValidateMigratedPrefix(previous, baseline);
            if (Sha256Digest.FixedEquals(Hash(previous), Hash(baseline)))
                return;
        }
        await WriteScopeFileAsync(scope, baseline, ct, path).ConfigureAwait(false);
    }

    private Task WriteScopeFileAsync(AccountCashForecastScopeDto scope, AccountCashForecastSnapshotDto[] snapshots, CancellationToken ct, string? destinationPath = null)
    {
        var envelope = new AccountCashForecastScopeEnvelope(1, scope, Hash(snapshots), snapshots);
        return AtomicFileWriter.WriteAsync(destinationPath ?? ScopeStatePath(scope),
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
