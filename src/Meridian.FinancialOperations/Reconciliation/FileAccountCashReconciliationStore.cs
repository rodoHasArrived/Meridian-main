using System.Diagnostics;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Explicitly provisioned atomic state partitioned per exact account scope, with a cross-process
/// lease spanning each scoped mutation.
/// </summary>
/// <remarks>
/// Layout under <c>reconciliation/account-cash-continuity</c>: the <c>initialized</c> provisioning
/// marker, a <c>scope-index.json</c> registry of retained scopes (rewritten only when a scope first
/// appears, so a deleted scope file still fails closed), one <c>scopes/&lt;sha256&gt;.json</c> document
/// per scope with its own <c>.lock</c>, and the store-wide <c>state.lock</c> used for provisioning,
/// scope registration and the lossless split of a legacy single-file <c>state.json</c>, which is
/// renamed to <c>state.json.migrated</c> and never deleted. The data root stays host-local;
/// multi-host deployments need the PostgreSQL follow-on rather than a shared file layout.
/// </remarks>
public sealed class FileAccountCashReconciliationStore
{
    private const string Marker = "meridian-account-cash-reconciliation-v1";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private readonly string _directory;
    private string LegacyStatePath => Path.Combine(_directory, "state.json");
    private string MigratedLegacyStatePath => LegacyStatePath + ".migrated";
    private string IndexPath => Path.Combine(_directory, "scope-index.json");
    private string ScopeDirectory => Path.Combine(_directory, "scopes");
    private string StoreLockPath => Path.Combine(_directory, "state.lock");
    private string MarkerPath => Path.Combine(_directory, "initialized");

    public FileAccountCashReconciliationStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _directory = Path.Combine(Path.GetFullPath(dataRoot), "reconciliation", "account-cash-continuity");
    }

    /// <summary>Canonical file-system-safe identity of one scope; the envelope re-states the scope so a misplaced file fails closed.</summary>
    public static string ScopeFileName(AccountCashReconciliationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(scope, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScope));
    }

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
        // An interrupted provisioning leaves a valid empty layout (empty legacy document or empty scope
        // index) without its marker. Adopt only that; retained history without a marker still requires
        // explicit recovery, and corrupt unmarked state is refused as corrupt rather than adopted.
        if (File.Exists(LegacyStatePath))
        {
            var unmarked = await ReadLegacyAsync(LegacyStatePath, ct).ConfigureAwait(false);
            if (unmarked.Runs.Count != 0 || unmarked.Cases.Count != 0 || File.Exists(IndexPath) || HasScopeFiles())
                throw new InvalidOperationException("Unprovisioned continuity state requires explicit recovery.");
            await SplitLegacyAsync(ct).ConfigureAwait(false);
        }
        else if (File.Exists(IndexPath))
        {
            var unmarked = await ReadIndexAsync(ct).ConfigureAwait(false);
            if (unmarked.Length != 0 || HasScopeFiles())
                throw new InvalidOperationException("Unprovisioned continuity state requires explicit recovery.");
            await RefuseRetainedMigratedHistoryAsync(ct).ConfigureAwait(false);
        }
        else if (HasScopeFiles())
        {
            throw new InvalidOperationException("Unprovisioned continuity state requires explicit recovery.");
        }
        else
        {
            await RefuseRetainedMigratedHistoryAsync(ct).ConfigureAwait(false);
            Directory.CreateDirectory(ScopeDirectory);
            await WriteIndexAsync([], ct).ConfigureAwait(false);
        }
        await AtomicFileWriter.WriteAsync(MarkerPath, Marker, ct).ConfigureAwait(false);
    }

    internal async Task<T> WithStateAsync<T>(
        AccountCashReconciliationScope scope,
        Func<AccountCashReconciliationDocument, (AccountCashReconciliationDocument Document, T Result)> action,
        bool write,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Directory.Exists(_directory))
            throw new InvalidOperationException("Account cash continuity has not been provisioned.");
        await EnsureLegacySplitAsync(ct).ConfigureAwait(false);
        await using var lease = await AcquireAsync(ScopeLockPath(scope), ct).ConfigureAwait(false);
        var (indexed, original) = await ReadScopeAsync(scope, ct).ConfigureAwait(false);
        var (updated, result) = action(original);
        if (write && !ReferenceEquals(original, updated))
        {
            // Past this point the mutation is committed. Registration and the first scope-file write are
            // two atomic writes; honouring a request cancellation between them would retain an indexed
            // scope without its file, which then fails closed on every later read. The commit therefore
            // ignores the request token, and the lock timeout still bounds it.
            if (indexed)
                await WriteScopeFileAsync(scope, updated, CancellationToken.None).ConfigureAwait(false);
            else
                await RegisterAndWriteScopeAsync(scope, updated).ConfigureAwait(false);
        }
        return result;
    }

    private async Task RequireMarkerAsync(CancellationToken ct)
    {
        if (!File.Exists(MarkerPath) || await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false) != Marker)
            throw new InvalidOperationException("Account cash continuity initialization is missing or invalid.");
    }

    /// <summary>Splits a legacy single-file document before any scoped operation can observe a partial layout.</summary>
    private async Task EnsureLegacySplitAsync(CancellationToken ct)
    {
        if (!File.Exists(LegacyStatePath))
            return;
        await using var lease = await AcquireAsync(StoreLockPath, ct).ConfigureAwait(false);
        // Scoped use never splits unprovisioned legacy state; that requires explicit recovery.
        await RequireMarkerAsync(ct).ConfigureAwait(false);
        await SplitLegacyAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Caller holds the store lock. Lossless and idempotent: scope files are written before the legacy file is renamed.</summary>
    private async Task SplitLegacyAsync(CancellationToken ct)
    {
        if (!File.Exists(LegacyStatePath))
            return;
        if (File.Exists(MigratedLegacyStatePath))
            throw new InvalidDataException("Both the legacy account cash continuity state and its migrated copy exist; restore one retained state before retrying.");
        var legacy = await ReadLegacyAsync(LegacyStatePath, ct).ConfigureAwait(false);
        var index = File.Exists(IndexPath) ? (await ReadIndexAsync(ct).ConfigureAwait(false)).ToList() : [];
        Directory.CreateDirectory(ScopeDirectory);
        var scopes = legacy.Runs.Select(item => item.Input.Scope).Concat(legacy.Cases.Select(item => item.Scope)).Distinct().ToArray();
        foreach (var scope in scopes)
        {
            var document = new AccountCashReconciliationDocument(1,
                legacy.Runs.Where(item => item.Input.Scope == scope).ToArray(),
                legacy.Cases.Where(item => item.Scope == scope).ToArray());
            var path = ScopeStatePath(scope);
            if (File.Exists(path))
            {
                var retained = await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false);
                if (!Sha256Digest.FixedEquals(Hash(retained), Hash(document)))
                    throw new InvalidDataException("A partially split legacy account cash continuity state conflicts with a retained scope file; restore retained state before retrying.");
            }
            else
            {
                await WriteScopeFileAsync(scope, document, ct).ConfigureAwait(false);
            }
            if (!index.Contains(scope))
                index.Add(scope);
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
                    throw new InvalidDataException("An unregistered account cash continuity scope file exists; restore retained state before retrying.");
            }
        }
        foreach (var scope in index)
            _ = await ReadScopeFileAsync(scope, ScopeStatePath(scope), ct).ConfigureAwait(false);
    }

    /// <summary>Caller holds the scope lock. Registered scopes must have their file; unregistered files are refused.</summary>
    private async Task<(bool Indexed, AccountCashReconciliationDocument Document)> ReadScopeAsync(AccountCashReconciliationScope scope, CancellationToken ct)
    {
        await RequireMarkerAsync(ct).ConfigureAwait(false);
        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var path = ScopeStatePath(scope);
        if (!index.Contains(scope))
        {
            if (File.Exists(path))
                throw new InvalidDataException("Retained account cash continuity state is unavailable or corrupt; restore it before retrying.");
            return (false, new AccountCashReconciliationDocument(1, [], []));
        }
        if (!File.Exists(path))
            throw new InvalidDataException("Retained account cash continuity state is unavailable or corrupt; restore it before retrying.");
        return (true, await ReadScopeFileAsync(scope, path, ct).ConfigureAwait(false));
    }

    private static async Task<AccountCashReconciliationDocument> ReadScopeFileAsync(AccountCashReconciliationScope scope, string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeEnvelope)
                ?? throw new InvalidDataException("Account cash continuity scope state is empty.");
            var document = envelope.Document;
            if (envelope.Scope != scope || !IsValid(document) || !Sha256Digest.IsCanonical(envelope.ContentHash)
                || !Sha256Digest.FixedEquals(envelope.ContentHash, Hash(document))
                || document.Runs.Any(item => item.Input.Scope != scope) || document.Cases.Any(item => item.Scope != scope))
                throw new InvalidDataException("Account cash continuity scope state failed integrity validation.");
            return document;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidDataException("Retained account cash continuity state is unavailable or corrupt; restore it before retrying.", ex);
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
        if (migrated.Runs.Count != 0 || migrated.Cases.Count != 0)
            throw new InvalidOperationException("Unprovisioned continuity state requires explicit recovery: migrated history exists without its provisioned scope layout.");
    }

    private static async Task<AccountCashReconciliationDocument> ReadLegacyAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationEnvelope)
                ?? throw new InvalidDataException("Account cash continuity state is empty.");
            var document = envelope.Document;
            if (!IsValid(document) || !Sha256Digest.IsCanonical(envelope.ContentHash) || !Sha256Digest.FixedEquals(envelope.ContentHash, Hash(document)))
                throw new InvalidDataException("Account cash continuity state failed integrity validation.");
            return document;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidDataException("Retained account cash continuity state is unavailable or corrupt; restore it before retrying.", ex);
        }
    }

    private static bool IsValid(AccountCashReconciliationDocument? document)
        => document is { SchemaVersion: 1, Runs: not null, Cases: not null }
            && document.Cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() == document.Cases.Count;

    private async Task<AccountCashReconciliationScope[]> ReadIndexAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await ReadSharedAsync(IndexPath, ct).ConfigureAwait(false);
            var index = JsonSerializer.Deserialize(bytes, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeIndex)
                ?? throw new InvalidDataException("Account cash continuity scope index is empty.");
            if (index.SchemaVersion != 1 || index.Scopes is null || index.Scopes.Any(static scope => scope is null)
                || !Sha256Digest.IsCanonical(index.ContentHash) || !Sha256Digest.FixedEquals(index.ContentHash, Hash(index.Scopes))
                || index.Scopes.Distinct().Count() != index.Scopes.Length)
                throw new InvalidDataException("Account cash continuity scope index failed integrity validation.");
            return index.Scopes;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidDataException("Retained account cash continuity scope index is unavailable or corrupt; restore it before retrying.", ex);
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

    /// <summary>
    /// Caller holds the scope lock. The store lease spans registration and the first scope-file write, so
    /// scopes serialize only on first appearance and an initializer validating every registered scope
    /// under the same lease never observes an indexed scope whose file is not yet written.
    /// </summary>
    private async Task RegisterAndWriteScopeAsync(AccountCashReconciliationScope scope, AccountCashReconciliationDocument document)
    {
        await using var lease = await AcquireAsync(StoreLockPath, CancellationToken.None).ConfigureAwait(false);
        var index = await ReadIndexAsync(CancellationToken.None).ConfigureAwait(false);
        if (!index.Contains(scope))
            await WriteIndexAsync([.. index, scope], CancellationToken.None).ConfigureAwait(false);
        await WriteScopeFileAsync(scope, document, CancellationToken.None).ConfigureAwait(false);
    }

    private Task WriteIndexAsync(AccountCashReconciliationScope[] scopes, CancellationToken ct)
    {
        var ordered = scopes.OrderBy(ScopeFileName, StringComparer.Ordinal).ToArray();
        return AtomicFileWriter.WriteAsync(IndexPath, JsonSerializer.SerializeToUtf8Bytes(
            new AccountCashReconciliationScopeIndex(1, Hash(ordered), ordered), AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeIndex), ct);
    }

    private Task WriteScopeFileAsync(AccountCashReconciliationScope scope, AccountCashReconciliationDocument document, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(ScopeStatePath(scope), JsonSerializer.SerializeToUtf8Bytes(
            new AccountCashReconciliationScopeEnvelope(Hash(document), scope, document), AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeEnvelope), ct);

    private bool HasScopeFiles() => Directory.Exists(ScopeDirectory) && Directory.EnumerateFiles(ScopeDirectory, "*.json").Any();
    private string ScopeStatePath(AccountCashReconciliationScope scope) => Path.Combine(ScopeDirectory, ScopeFileName(scope) + ".json");
    private string ScopeLockPath(AccountCashReconciliationScope scope) => Path.Combine(ScopeDirectory, ScopeFileName(scope) + ".lock");

    private static async Task<FileStream> AcquireAsync(string lockPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                if (timer.Elapsed > LockTimeout)
                    throw new TimeoutException("Timed out acquiring the account cash continuity writer lock.", ex);
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private static string Hash(AccountCashReconciliationDocument document)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(document, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationDocument));
    private static string Hash(AccountCashReconciliationScope[] scopes)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(scopes, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationScopeArray));
}
