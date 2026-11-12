using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>Durable per-operator last visit to each workstation surface (C3).</summary>
public interface IOperatorSurfaceVisitStore
{
    /// <summary>The last recorded visit, without recording one.</summary>
    Task<OperatorSurfaceVisitDto> GetAsync(string operatorKey, string surface, CancellationToken ct = default);

    /// <summary>Records a visit now and returns the visit it replaced.</summary>
    Task<OperatorSurfaceVisitDto> RecordVisitAsync(string operatorKey, string surface, CancellationToken ct = default);
}

/// <summary>
/// JSON-backed store beside the operator scope preferences, keyed by tenant, company, user and
/// surface. Writes go through the snapshot store's atomic replace.
/// </summary>
public sealed partial class FileOperatorSurfaceVisitStore
    : JsonFileSnapshotStore<OperatorSurfaceVisitSnapshot>, IOperatorSurfaceVisitStore
{
    private const int SnapshotVersion = 1;

    private readonly ILogger<FileOperatorSurfaceVisitStore> _logger;
    private readonly TimeProvider _timeProvider;

    public FileOperatorSurfaceVisitStore(
        string dataRoot,
        ILogger<FileOperatorSurfaceVisitStore> logger,
        TimeProvider? timeProvider = null)
        : base(ResolveSnapshotPath(dataRoot), OperatorSurfaceVisitJsonContext.Default.OperatorSurfaceVisitSnapshot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<OperatorSurfaceVisitDto> GetAsync(string operatorKey, string surface, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        var normalizedSurface = NormalizeSurface(surface);
        return ReadSnapshotAsync(
            snapshot => new OperatorSurfaceVisitDto(normalizedSurface, Find(snapshot, operatorKey, normalizedSurface)?.VisitedAt, null),
            ct);
    }

    public async Task<OperatorSurfaceVisitDto> RecordVisitAsync(string operatorKey, string surface, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        var normalizedSurface = NormalizeSurface(surface);
        var visitedAt = _timeProvider.GetUtcNow();

        return await UpdateSnapshotAsync(
                snapshot =>
                {
                    var previous = Find(snapshot, operatorKey, normalizedSurface)?.VisitedAt;
                    var next = (snapshot.Visits ?? [])
                        .Where(item => !IsMatch(item, operatorKey, normalizedSurface))
                        .Append(new OperatorSurfaceVisitRecord(operatorKey, normalizedSurface, visitedAt))
                        .ToArray();
                    return (new OperatorSurfaceVisitSnapshot(SnapshotVersion, next), new OperatorSurfaceVisitDto(normalizedSurface, previous, visitedAt));
                },
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>Surface ids are short lowercase slugs, such as <c>reconciliation-breaks</c>.</summary>
    public static string NormalizeSurface(string? surface)
    {
        var trimmed = surface?.Trim() ?? string.Empty;
        if (!SurfacePattern().IsMatch(trimmed))
        {
            throw new ArgumentException("Surface must be 1 to 64 lowercase letters, digits or hyphens, starting with a letter or digit.", nameof(surface));
        }

        return trimmed;
    }

    private static OperatorSurfaceVisitRecord? Find(OperatorSurfaceVisitSnapshot snapshot, string operatorKey, string surface)
        => snapshot.Visits?.FirstOrDefault(item => IsMatch(item, operatorKey, surface));

    private static bool IsMatch(OperatorSurfaceVisitRecord record, string operatorKey, string surface)
        => string.Equals(record.OperatorKey, operatorKey, StringComparison.Ordinal)
            && string.Equals(record.Surface, surface, StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SurfacePattern();

    protected override OperatorSurfaceVisitSnapshot CreateEmptySnapshot() => new(SnapshotVersion, []);

    protected override OperatorSurfaceVisitSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger.LogWarning(exception, "Operator surface visit snapshot is not valid JSON: {Path}", SnapshotPath);
        throw new InvalidOperationException($"Operator surface visit snapshot is invalid: {SnapshotPath}", exception);
    }

    protected override OperatorSurfaceVisitSnapshot OnSnapshotLoaded(OperatorSurfaceVisitSnapshot snapshot)
    {
        if (snapshot.Version != SnapshotVersion)
        {
            throw new InvalidOperationException(
                $"Operator surface visit snapshot version {snapshot.Version} is not supported. Expected version {SnapshotVersion}: {SnapshotPath}");
        }

        return snapshot;
    }

    private static string ResolveSnapshotPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var directory = Path.Combine(dataRoot, "workstation", "operators");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "surface-visits.json");
    }
}

public sealed record OperatorSurfaceVisitRecord(string OperatorKey, string Surface, DateTimeOffset VisitedAt);

public sealed record OperatorSurfaceVisitSnapshot(int Version, IReadOnlyList<OperatorSurfaceVisitRecord> Visits);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(OperatorSurfaceVisitSnapshot))]
internal sealed partial class OperatorSurfaceVisitJsonContext : JsonSerializerContext;
