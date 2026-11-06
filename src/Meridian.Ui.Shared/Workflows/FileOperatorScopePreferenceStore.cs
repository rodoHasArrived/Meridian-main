using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>Durable per-operator default operating scope (C1).</summary>
public interface IOperatorScopePreferenceStore
{
    Task<OperatorScopePreferenceDto> GetAsync(string operatorKey, CancellationToken ct = default);

    Task<OperatorScopePreferenceDto> SetAsync(string operatorKey, OperatingScopeValuesDto? defaultScope, CancellationToken ct = default);
}

/// <summary>
/// JSON-backed store under Meridian's data root, keyed by tenant, company and user so one
/// operator's default never leaks to another. Writes go through the snapshot store's atomic
/// replace, like workflow presets.
/// </summary>
public sealed class FileOperatorScopePreferenceStore
    : JsonFileSnapshotStore<OperatorScopePreferenceSnapshot>, IOperatorScopePreferenceStore
{
    private const int SnapshotVersion = 1;
    private const int MaxValueLength = 128;

    private readonly ILogger<FileOperatorScopePreferenceStore> _logger;
    private readonly TimeProvider _timeProvider;

    public FileOperatorScopePreferenceStore(
        string dataRoot,
        ILogger<FileOperatorScopePreferenceStore> logger,
        TimeProvider? timeProvider = null)
        : base(ResolveSnapshotPath(dataRoot), OperatorScopePreferenceJsonContext.Default.OperatorScopePreferenceSnapshot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<OperatorScopePreferenceDto> GetAsync(string operatorKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        return ReadSnapshotAsync(
            snapshot =>
            {
                var record = snapshot.Operators?.FirstOrDefault(item => string.Equals(item.OperatorKey, operatorKey, StringComparison.Ordinal));
                return new OperatorScopePreferenceDto(record?.DefaultScope, record?.UpdatedAt);
            },
            ct);
    }

    public async Task<OperatorScopePreferenceDto> SetAsync(
        string operatorKey,
        OperatingScopeValuesDto? defaultScope,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        var normalized = Normalize(defaultScope);
        var updatedAt = _timeProvider.GetUtcNow();

        return await UpdateSnapshotAsync(
                snapshot =>
                {
                    var others = (snapshot.Operators ?? [])
                        .Where(item => !string.Equals(item.OperatorKey, operatorKey, StringComparison.Ordinal));
                    var next = normalized is null
                        ? others.ToArray()
                        : others.Append(new OperatorScopePreferenceRecord(operatorKey, normalized, updatedAt)).ToArray();
                    var result = new OperatorScopePreferenceDto(normalized, normalized is null ? null : updatedAt);
                    return (new OperatorScopePreferenceSnapshot(SnapshotVersion, next), result);
                },
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>Trims each value, drops blanks, and returns null for a scope with no values.</summary>
    public static OperatingScopeValuesDto? Normalize(OperatingScopeValuesDto? scope)
    {
        if (scope is null)
        {
            return null;
        }

        var normalized = new OperatingScopeValuesDto(
            Clean(scope.Symbol, nameof(scope.Symbol)),
            Clean(scope.FundAccountId, nameof(scope.FundAccountId)),
            Clean(scope.RunId, nameof(scope.RunId)),
            Clean(scope.Provider, nameof(scope.Provider)),
            Clean(scope.From, nameof(scope.From)),
            Clean(scope.To, nameof(scope.To)),
            Clean(scope.Date, nameof(scope.Date)),
            Clean(scope.AsOf, nameof(scope.AsOf)));
        return normalized == new OperatingScopeValuesDto() ? null : normalized;
    }

    private static string? Clean(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxValueLength || trimmed.Any(char.IsControl))
        {
            throw new ArgumentException($"Default scope {field} must be at most {MaxValueLength} printable characters.", field);
        }

        return trimmed;
    }

    protected override OperatorScopePreferenceSnapshot CreateEmptySnapshot() => new(SnapshotVersion, []);

    protected override OperatorScopePreferenceSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger.LogWarning(exception, "Operator scope preference snapshot is not valid JSON: {Path}", SnapshotPath);
        throw new InvalidOperationException($"Operator scope preference snapshot is invalid: {SnapshotPath}", exception);
    }

    protected override OperatorScopePreferenceSnapshot OnSnapshotLoaded(OperatorScopePreferenceSnapshot snapshot)
    {
        if (snapshot.Version != SnapshotVersion)
        {
            throw new InvalidOperationException(
                $"Operator scope preference snapshot version {snapshot.Version} is not supported. Expected version {SnapshotVersion}: {SnapshotPath}");
        }

        return snapshot;
    }

    private static string ResolveSnapshotPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var directory = Path.Combine(dataRoot, "workstation", "operators");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "scope-preferences.json");
    }
}

public sealed record OperatorScopePreferenceRecord(string OperatorKey, OperatingScopeValuesDto DefaultScope, DateTimeOffset UpdatedAt);

public sealed record OperatorScopePreferenceSnapshot(int Version, IReadOnlyList<OperatorScopePreferenceRecord> Operators);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(OperatorScopePreferenceSnapshot))]
internal sealed partial class OperatorScopePreferenceJsonContext : JsonSerializerContext;
