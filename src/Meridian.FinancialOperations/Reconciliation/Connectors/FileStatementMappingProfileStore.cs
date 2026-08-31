using System.Text.Json;
using Meridian.Storage.Archival;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

public interface IStatementMappingProfileStore
{
    Task<IReadOnlyList<StatementMappingProfileDocument>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates or replaces an operator-owned profile document. Built-in ids are rejected.</summary>
    Task<StatementMappingProfileDocument> UpsertAsync(StatementMappingProfileDocument document, CancellationToken ct = default);

    Task<bool> DeleteAsync(string profileId, CancellationToken ct = default);
}

/// <summary>
/// File-backed profile store using the versioned-snapshot pattern: a single JSON document
/// persisted through <see cref="AtomicFileWriter"/> so a crash mid-write never corrupts
/// operator-authored mapping profiles.
/// </summary>
public sealed class FileStatementMappingProfileStore
    : JsonFileSnapshotStore<StatementMappingProfileSnapshot>, IStatementMappingProfileStore
{
    private const int SnapshotVersion = 1;

    private readonly ILogger<FileStatementMappingProfileStore>? _logger;

    public FileStatementMappingProfileStore(string dataRoot, ILogger<FileStatementMappingProfileStore>? logger = null)
        : base(GetSnapshotPath(dataRoot), StatementMappingProfileJsonContext.Default.StatementMappingProfileSnapshot)
    {
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
    }

    public Task<IReadOnlyList<StatementMappingProfileDocument>> ListAsync(CancellationToken ct = default)
        => ReadSnapshotAsync(static snapshot => snapshot.Profiles, ct);

    public async Task<StatementMappingProfileDocument> UpsertAsync(StatementMappingProfileDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureNotBuiltIn(document.ProfileId);

        var normalized = document with
        {
            ProfileId = document.ProfileId.Trim(),
            IsBuiltIn = false
        };
        var errors = StatementMappingProfileLoader.Validate(normalized);
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Statement mapping profile '{normalized.ProfileId}' is invalid: {string.Join(" ", errors)}");
        }

        return await UpdateSnapshotAsync(snapshot =>
        {
            var retained = snapshot.Profiles
                .Where(existing => !string.Equals(existing.ProfileId, normalized.ProfileId, StringComparison.OrdinalIgnoreCase))
                .Append(normalized)
                .OrderBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return (new StatementMappingProfileSnapshot(SnapshotVersion, retained), normalized);
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string profileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        EnsureNotBuiltIn(profileId);

        return await UpdateSnapshotAsync(snapshot =>
        {
            var retained = snapshot.Profiles
                .Where(existing => !string.Equals(existing.ProfileId, profileId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return retained.Length == snapshot.Profiles.Count
                ? (snapshot, false)
                : (new StatementMappingProfileSnapshot(SnapshotVersion, retained), true);
        }, ct).ConfigureAwait(false);
    }

    protected override StatementMappingProfileSnapshot CreateEmptySnapshot() => new(SnapshotVersion, []);

    protected override StatementMappingProfileSnapshot OnSnapshotLoaded(StatementMappingProfileSnapshot snapshot)
    {
        if (snapshot.Version != SnapshotVersion)
        {
            throw new InvalidOperationException(
                $"Statement mapping profile snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {SnapshotPath}");
        }

        return snapshot;
    }

    protected override StatementMappingProfileSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger?.LogWarning(exception, "Statement mapping profile snapshot is not valid JSON: {Path}", SnapshotPath);
        throw new InvalidOperationException($"Statement mapping profile snapshot is invalid: {SnapshotPath}", exception);
    }

    private static void EnsureNotBuiltIn(string profileId)
    {
        if (StatementBuiltInProfiles.All.Any(builtIn =>
                string.Equals(builtIn.ProfileId, profileId?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Statement mapping profile '{profileId}' is built in and cannot be modified. Clone it under a new profile id instead.");
        }
    }

    private static string GetSnapshotPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return Path.Combine(dataRoot, "reconciliation", "statement-mapping-profiles.json");
    }
}
