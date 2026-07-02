using System.Text.Json;
using Meridian.Storage.Archival;
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
public sealed class FileStatementMappingProfileStore : IStatementMappingProfileStore
{
    private const int SnapshotVersion = 1;

    private readonly string _snapshotPath;
    private readonly ILogger<FileStatementMappingProfileStore>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileStatementMappingProfileStore(string dataRoot, ILogger<FileStatementMappingProfileStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger;
        var directory = Path.Combine(dataRoot, "reconciliation");
        Directory.CreateDirectory(directory);
        _snapshotPath = Path.Combine(directory, "statement-mapping-profiles.json");
    }

    public async Task<IReadOnlyList<StatementMappingProfileDocument>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            return snapshot.Profiles;
        }
        finally
        {
            _gate.Release();
        }
    }

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

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            var retained = snapshot.Profiles
                .Where(existing => !string.Equals(existing.ProfileId, normalized.ProfileId, StringComparison.OrdinalIgnoreCase))
                .Append(normalized)
                .OrderBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await PersistAsync(new StatementMappingProfileSnapshot(SnapshotVersion, retained), ct).ConfigureAwait(false);
            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string profileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        EnsureNotBuiltIn(profileId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(ct).ConfigureAwait(false);
            var retained = snapshot.Profiles
                .Where(existing => !string.Equals(existing.ProfileId, profileId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (retained.Length == snapshot.Profiles.Count)
            {
                return false;
            }

            await PersistAsync(new StatementMappingProfileSnapshot(SnapshotVersion, retained), ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void EnsureNotBuiltIn(string profileId)
    {
        if (StatementBuiltInProfiles.All.Any(builtIn =>
                string.Equals(builtIn.ProfileId, profileId?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Statement mapping profile '{profileId}' is built in and cannot be modified. Clone it under a new profile id instead.");
        }
    }

    private async Task<StatementMappingProfileSnapshot> LoadCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new StatementMappingProfileSnapshot(SnapshotVersion, []);
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            var snapshot = await JsonSerializer.DeserializeAsync(
                    stream,
                    StatementMappingProfileJsonContext.Default.StatementMappingProfileSnapshot,
                    ct)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                return new StatementMappingProfileSnapshot(SnapshotVersion, []);
            }

            if (snapshot.Version != SnapshotVersion)
            {
                throw new InvalidOperationException(
                    $"Statement mapping profile snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {_snapshotPath}");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Statement mapping profile snapshot is not valid JSON: {Path}", _snapshotPath);
            throw new InvalidOperationException($"Statement mapping profile snapshot is invalid: {_snapshotPath}", ex);
        }
    }

    private async Task PersistAsync(StatementMappingProfileSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(snapshot, StatementMappingProfileJsonContext.Default.StatementMappingProfileSnapshot);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }
}
