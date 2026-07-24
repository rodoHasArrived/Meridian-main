using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Services;

/// <summary>
/// JSONL-backed implementation of <see cref="IPositionSnapshotStore"/>.
/// <para>
/// Owned path convention:
/// <c>{StorageRoot}/portfolios/owned/{tenant}/{company}/{fund}/{book}/{entity}/{runId}/{accountId}/snapshots.jsonl</c>.
/// Legacy unowned snapshots retain <c>{StorageRoot}/portfolios/{runId}/{accountId}/snapshots.jsonl</c>
/// and are never returned by an owned lookup.
/// This path falls under <c>StorageOptions.RootPath</c> so the
/// <see cref="LifecyclePolicyEngine"/> automatically picks it up for tiered-storage
/// lifecycle enforcement (ADR-002).  Each snapshot is a single JSON line appended
/// atomically (ADR-007). A per-partition file lock serializes the conditional
/// same-timestamp check and append across store instances and cooperating processes.
/// Recovery streams the file and selects the greatest recorded <c>AsOf</c> timestamp
/// rather than trusting append order, so a delayed writer cannot make an older
/// observation authoritative.
/// </para>
/// </summary>
[ImplementsAdr("ADR-007", "WAL/atomic write pattern for crash-safe position snapshot persistence")]
[ImplementsAdr("ADR-002", "Snapshot files stored under StorageRoot so LifecyclePolicyEngine governs retention")]
[ImplementsAdr("ADR-014", "Serialisation via ExecutionSnapshotJsonContext — no reflection")]
public sealed class JsonlPositionSnapshotStore : IPositionSnapshotStore
{
    private readonly string _rootPath;
    private readonly ILogger<JsonlPositionSnapshotStore> _logger;

    // Per-instance locks avoid spinning multiple local callers on the cross-process lock file.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _fileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public JsonlPositionSnapshotStore(StorageOptions options, ILogger<JsonlPositionSnapshotStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rootPath = options.RootPath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─── IPositionSnapshotStore ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task SaveSnapshotAsync(
        AccountSnapshotRecord snapshot,
        CancellationToken ct = default)
    {
        _ = await SaveSnapshotConditionallyAsync(snapshot, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PositionSnapshotSaveOutcome> SaveSnapshotConditionallyAsync(
        AccountSnapshotRecord snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.AccountId);

        var ownerScope = GetOwnerScope(snapshot);
        var path = ownerScope is null
            ? GetSnapshotPath(snapshot.RunId, snapshot.AccountId)
            : GetSnapshotPath(snapshot.RunId, snapshot.AccountId, ownerScope);
        EnsureDirectory(path);

        var json = JsonSerializer.Serialize(snapshot, SnapshotJsonContext.Default.AccountSnapshotRecord);

        var fileLock = _fileLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var partitionLock = await AcquirePartitionLockAsync(path, ct).ConfigureAwait(false);
            var existingOutcome = await InspectLogicalTimestampAsync(path, snapshot, ownerScope, ct)
                .ConfigureAwait(false);
            if (existingOutcome.HasValue)
            {
                _logger.LogDebug(
                    "Skipped equivalent position snapshot for run={RunId} account={AccountId} asOf={AsOf}",
                    snapshot.RunId,
                    snapshot.AccountId,
                    snapshot.AsOf.ToUniversalTime());
                return existingOutcome.Value;
            }

            await AtomicFileWriter.AppendLinesAsync(path, [json], ct).ConfigureAwait(false);

            _logger.LogDebug("Saved position snapshot for run={RunId} account={AccountId} to {Path}",
                snapshot.RunId, snapshot.AccountId, path);
            return PositionSnapshotSaveOutcome.Appended;
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccountSnapshotRecord?> GetLatestSnapshotAsync(
        string runId,
        string accountId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var path = GetSnapshotPath(runId, accountId);
        if (!File.Exists(path))
            return null;

        return await ReadLatestAsync(
                path,
                snapshot => IsInUnownedScope(snapshot, runId, accountId),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AccountSnapshotRecord?> GetLatestSnapshotAsync(
        string runId,
        string accountId,
        PositionSnapshotOwnerScope ownerScope,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(ownerScope);
        ValidateOwnerScope(ownerScope);

        var path = GetSnapshotPath(runId, accountId, ownerScope);
        if (!File.Exists(path))
            return null;

        return await ReadLatestAsync(
                path,
                snapshot => IsInOwnedScope(snapshot, runId, accountId, ownerScope),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AccountSnapshotRecord> GetSnapshotHistoryAsync(
        string runId,
        string accountId,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var path = GetSnapshotPath(runId, accountId);
        if (!File.Exists(path))
            yield break;

        await foreach (var line in ReadLinesForwardAsync(path, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var snapshot = TryDeserialize(line);
            if (snapshot is null || !IsInUnownedScope(snapshot, runId, accountId))
                continue;

            if (snapshot.AsOf >= from && snapshot.AsOf <= to)
                yield return snapshot;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AccountSnapshotRecord> GetSnapshotHistoryAsync(
        string runId,
        string accountId,
        PositionSnapshotOwnerScope ownerScope,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(ownerScope);
        ValidateOwnerScope(ownerScope);

        var path = GetSnapshotPath(runId, accountId, ownerScope);
        if (!File.Exists(path))
            yield break;

        await foreach (var line in ReadLinesForwardAsync(path, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var snapshot = TryDeserialize(line);
            if (snapshot is not null &&
                IsInOwnedScope(snapshot, runId, accountId, ownerScope) &&
                snapshot.AsOf >= from &&
                snapshot.AsOf <= to)
            {
                yield return snapshot;
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<PositionSnapshotSaveOutcome?> InspectLogicalTimestampAsync(
        string path,
        AccountSnapshotRecord proposed,
        PositionSnapshotOwnerScope? ownerScope,
        CancellationToken ct)
    {
        if (!File.Exists(path))
            return null;

        var proposedAsOf = proposed.AsOf.ToUniversalTime();
        var foundEquivalent = false;
        await foreach (var line in ReadLinesForwardAsync(path, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var retained = TryDeserialize(line);
            if (retained is null || retained.AsOf.ToUniversalTime() != proposedAsOf)
                continue;

            var isSameScope = ownerScope is null
                ? IsInUnownedScope(retained, proposed.RunId, proposed.AccountId)
                : IsInOwnedScope(retained, proposed.RunId, proposed.AccountId, ownerScope);
            if (!isSameScope)
                continue;

            if (!PositionSnapshotEquivalence.AreEquivalent(retained, proposed))
            {
                throw new PositionSnapshotConflictException(
                    proposed.RunId,
                    proposed.AccountId,
                    proposedAsOf);
            }

            foundEquivalent = true;
        }

        return foundEquivalent
            ? PositionSnapshotSaveOutcome.EquivalentAlreadyExists
            : null;
    }

    private async Task<AccountSnapshotRecord?> ReadLatestAsync(
        string path,
        Func<AccountSnapshotRecord, bool> isInScope,
        CancellationToken ct)
    {
        AccountSnapshotRecord? latest = null;
        await foreach (var line in ReadLinesForwardAsync(path, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var snapshot = TryDeserialize(line);
            if (snapshot is null || !isInScope(snapshot))
                continue;

            if (latest is null || snapshot.AsOf.ToUniversalTime() > latest.AsOf.ToUniversalTime())
            {
                latest = snapshot;
                continue;
            }

            if (snapshot.AsOf.ToUniversalTime() == latest.AsOf.ToUniversalTime() &&
                !PositionSnapshotEquivalence.AreEquivalent(snapshot, latest))
            {
                throw new PositionSnapshotConflictException(
                    snapshot.RunId,
                    snapshot.AccountId,
                    snapshot.AsOf);
            }
        }

        return latest;
    }

    private static async Task<FileStream> AcquirePartitionLockAsync(
        string snapshotPath,
        CancellationToken ct)
    {
        var lockPath = snapshotPath + ".lock";
        var retryDelay = TimeSpan.FromMilliseconds(5);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(retryDelay, ct).ConfigureAwait(false);
                if (retryDelay < TimeSpan.FromMilliseconds(100))
                    retryDelay += retryDelay;
            }
        }
    }

    private string GetSnapshotPath(string runId, string accountId)
    {
        var safeRunId = SanitisePathSegment(runId);
        var safeAccountId = SanitisePathSegment(accountId);
        return Path.Combine(_rootPath, "portfolios", safeRunId, safeAccountId, "snapshots.jsonl");
    }

    private string GetSnapshotPath(
        string runId,
        string accountId,
        PositionSnapshotOwnerScope ownerScope)
    {
        var safeTenantId = SanitisePathSegment(ownerScope.TenantId.Trim());
        var safeCompanyId = SanitisePathSegment(ownerScope.CompanyId.Trim());
        var safeFundProfileId = SanitisePathSegment(ownerScope.FundProfileId.Trim());
        var safeLedgerBookId = ownerScope.LedgerBookId.ToString("N");
        var safeEntityId = SanitisePathSegment(ownerScope.EntityId.Trim());
        var safeRunId = SanitisePathSegment(runId);
        var safeAccountId = SanitisePathSegment(accountId);
        return Path.Combine(
            _rootPath,
            "portfolios",
            "owned",
            safeTenantId,
            safeCompanyId,
            safeFundProfileId,
            safeLedgerBookId,
            safeEntityId,
            safeRunId,
            safeAccountId,
            "snapshots.jsonl");
    }

    private static PositionSnapshotOwnerScope? GetOwnerScope(AccountSnapshotRecord snapshot)
    {
        var ownershipFields = new[]
        {
            snapshot.TenantId,
            snapshot.CompanyId,
            snapshot.FundProfileId,
            snapshot.EntityId
        };
        var populatedCount = ownershipFields.Count(static value => !string.IsNullOrWhiteSpace(value));
        if (populatedCount == 0 && !snapshot.LedgerBookId.HasValue)
            return null;
        if (populatedCount != ownershipFields.Length ||
            !snapshot.LedgerBookId.HasValue ||
            snapshot.LedgerBookId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Position snapshot ownership must include tenant, company, fund profile, ledger book, and entity together.");
        }

        var ownerScope = new PositionSnapshotOwnerScope(
            snapshot.TenantId!.Trim(),
            snapshot.CompanyId!.Trim(),
            snapshot.FundProfileId!.Trim(),
            snapshot.LedgerBookId.Value,
            snapshot.EntityId!.Trim());
        ValidateOwnerScope(ownerScope);
        return ownerScope;
    }

    private static void ValidateOwnerScope(PositionSnapshotOwnerScope ownerScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerScope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerScope.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerScope.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerScope.EntityId);
        if (ownerScope.LedgerBookId == Guid.Empty)
            throw new ArgumentException("Position snapshot ledger-book owner is required.", nameof(ownerScope));
    }

    private static bool IsOwnedBy(AccountSnapshotRecord snapshot, PositionSnapshotOwnerScope ownerScope)
        => string.Equals(snapshot.TenantId?.Trim(), ownerScope.TenantId.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(snapshot.CompanyId?.Trim(), ownerScope.CompanyId.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(snapshot.FundProfileId?.Trim(), ownerScope.FundProfileId.Trim(), StringComparison.OrdinalIgnoreCase) &&
           snapshot.LedgerBookId == ownerScope.LedgerBookId &&
           string.Equals(snapshot.EntityId?.Trim(), ownerScope.EntityId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsInUnownedScope(AccountSnapshotRecord snapshot, string runId, string accountId)
        => string.Equals(snapshot.RunId?.Trim(), runId.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(snapshot.AccountId?.Trim(), accountId.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.IsNullOrWhiteSpace(snapshot.TenantId) &&
           string.IsNullOrWhiteSpace(snapshot.CompanyId) &&
           string.IsNullOrWhiteSpace(snapshot.FundProfileId) &&
           !snapshot.LedgerBookId.HasValue &&
           string.IsNullOrWhiteSpace(snapshot.EntityId);

    private static bool IsInOwnedScope(
        AccountSnapshotRecord snapshot,
        string runId,
        string accountId,
        PositionSnapshotOwnerScope ownerScope)
        => string.Equals(snapshot.RunId?.Trim(), runId.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(snapshot.AccountId?.Trim(), accountId.Trim(), StringComparison.OrdinalIgnoreCase) &&
           IsOwnedBy(snapshot, ownerScope);

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static string SanitisePathSegment(string segment)
    {
        // Replace path separators and other invalid characters.
        foreach (var c in Path.GetInvalidFileNameChars())
            segment = segment.Replace(c, '_');
        return segment;
    }

    private AccountSnapshotRecord? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.AccountSnapshotRecord);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Skipping malformed snapshot line: {Json}", json.Length > 200 ? json[..200] : json);
            return null;
        }
    }

    /// <summary>
    /// Streams lines from a file in forward (oldest-first) order.
    /// </summary>
    private static async IAsyncEnumerable<string> ReadLinesForwardAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            yield return line;
    }

}

/// <summary>
/// Source-generated JSON context for position snapshot types (ADR-014).
/// </summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(AccountSnapshotRecord))]
[System.Text.Json.Serialization.JsonSerializable(typeof(PositionRecord))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<PositionRecord>))]
internal sealed partial class SnapshotJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
