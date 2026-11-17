using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>Durable per-operator presentation preferences (theme, density, time zone, descriptions, shortcuts).</summary>
public interface IOperatorPreferenceStore
{
    Task<OperatorPreferencesDto> GetAsync(string operatorKey, CancellationToken ct = default);

    Task<OperatorPreferencesDto> SetAsync(string operatorKey, OperatorPreferenceValuesDto? preferences, CancellationToken ct = default);
}

/// <summary>
/// JSON-backed store under Meridian's data root, keyed by tenant, company and user so one operator's
/// preferences never reach another. Writes replace the operator's whole set through the snapshot
/// store's atomic replace, like the default operating scope. Values are validated here so a client
/// cannot store arbitrary text; clients still decode defensively because older hosts or other
/// clients may hold values they do not recognise.
/// </summary>
public sealed partial class FileOperatorPreferenceStore
    : JsonFileSnapshotStore<OperatorPreferenceSnapshot>, IOperatorPreferenceStore
{
    private const int SnapshotVersion = 1;
    private const int MaxTimeZoneLength = 64;
    private const int MaxKeyBindings = 32;
    private const int MaxKeyBindingIdLength = 64;
    private const int MaxKeyBindingComboLength = 64;

    private static readonly HashSet<string> Appearances = new(StringComparer.Ordinal) { "system", "light", "dark" };
    private static readonly HashSet<string> Densities = new(StringComparer.Ordinal) { "compact", "default", "spacious" };

    private readonly ILogger<FileOperatorPreferenceStore> _logger;
    private readonly TimeProvider _timeProvider;

    public FileOperatorPreferenceStore(
        string dataRoot,
        ILogger<FileOperatorPreferenceStore> logger,
        TimeProvider? timeProvider = null)
        : base(ResolveSnapshotPath(dataRoot), OperatorPreferenceJsonContext.Default.OperatorPreferenceSnapshot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<OperatorPreferencesDto> GetAsync(string operatorKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        return ReadSnapshotAsync(
            snapshot =>
            {
                var record = snapshot.Operators?.FirstOrDefault(item => string.Equals(item.OperatorKey, operatorKey, StringComparison.Ordinal));
                return new OperatorPreferencesDto(record?.Preferences ?? new OperatorPreferenceValuesDto(), record?.UpdatedAt);
            },
            ct);
    }

    public async Task<OperatorPreferencesDto> SetAsync(
        string operatorKey,
        OperatorPreferenceValuesDto? preferences,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        var normalized = Normalize(preferences);
        var updatedAt = _timeProvider.GetUtcNow();

        return await UpdateSnapshotAsync(
                snapshot =>
                {
                    var others = (snapshot.Operators ?? [])
                        .Where(item => !string.Equals(item.OperatorKey, operatorKey, StringComparison.Ordinal));
                    var next = normalized is null
                        ? others.ToArray()
                        : others.Append(new OperatorPreferenceRecord(operatorKey, normalized, updatedAt)).ToArray();
                    var result = new OperatorPreferencesDto(
                        normalized ?? new OperatorPreferenceValuesDto(),
                        normalized is null ? null : updatedAt);
                    return (new OperatorPreferenceSnapshot(SnapshotVersion, next), result);
                },
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Validates and trims each value and returns null when nothing is set. Throws
    /// <see cref="ArgumentException"/> naming the field for a value outside its allowed set or shape.
    /// </summary>
    public static OperatorPreferenceValuesDto? Normalize(OperatorPreferenceValuesDto? preferences)
    {
        if (preferences is null)
        {
            return null;
        }

        var appearance = OneOf(preferences.Appearance, Appearances, nameof(preferences.Appearance));
        var density = OneOf(preferences.Density, Densities, nameof(preferences.Density));
        var timeZone = TimeZoneName(preferences.DisplayTimeZone);
        var keyBindings = KeyBindings(preferences.KeyBindings);

        if (appearance is null && density is null && timeZone is null
            && preferences.PageDescriptionsHidden is null && keyBindings is null)
        {
            return null;
        }

        return new OperatorPreferenceValuesDto(appearance, density, timeZone, preferences.PageDescriptionsHidden, keyBindings);
    }

    private static string? OneOf(string? value, HashSet<string> allowed, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (!allowed.Contains(trimmed))
        {
            throw new ArgumentException($"{field} must be one of: {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.", field);
        }

        return trimmed;
    }

    private static string? TimeZoneName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // Shape check only: IANA names such as "UTC", "Europe/London" or "America/Argentina/Buenos_Aires".
        // The browser confirms the zone with its own Intl data and falls back to UTC for one it lacks.
        if (trimmed.Length > MaxTimeZoneLength || !TimeZonePattern().IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"DisplayTimeZone must be an IANA time zone name of at most {MaxTimeZoneLength} characters.",
                nameof(OperatorPreferenceValuesDto.DisplayTimeZone));
        }

        return trimmed;
    }

    private static IReadOnlyDictionary<string, string>? KeyBindings(IReadOnlyDictionary<string, string>? bindings)
    {
        if (bindings is null || bindings.Count == 0)
        {
            return null;
        }

        if (bindings.Count > MaxKeyBindings)
        {
            throw new ArgumentException($"KeyBindings holds at most {MaxKeyBindings} entries.", nameof(OperatorPreferenceValuesDto.KeyBindings));
        }

        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, combo) in bindings)
        {
            var trimmedId = id?.Trim() ?? string.Empty;
            var trimmedCombo = combo?.Trim() ?? string.Empty;
            if (trimmedId.Length is 0 or > MaxKeyBindingIdLength || !KeyBindingIdPattern().IsMatch(trimmedId))
            {
                throw new ArgumentException(
                    $"KeyBindings ids must be lowercase words joined by hyphens, at most {MaxKeyBindingIdLength} characters.",
                    nameof(OperatorPreferenceValuesDto.KeyBindings));
            }

            if (trimmedCombo.Length is 0 or > MaxKeyBindingComboLength || trimmedCombo.Any(char.IsControl))
            {
                throw new ArgumentException(
                    $"KeyBindings combos must be 1 to {MaxKeyBindingComboLength} printable characters.",
                    nameof(OperatorPreferenceValuesDto.KeyBindings));
            }

            normalized[trimmedId] = trimmedCombo;
        }

        return normalized;
    }

    [GeneratedRegex(@"^(UTC|[A-Za-z]+(/[A-Za-z0-9_+\-]+){1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZonePattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyBindingIdPattern();

    protected override OperatorPreferenceSnapshot CreateEmptySnapshot() => new(SnapshotVersion, []);

    protected override OperatorPreferenceSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger.LogWarning(exception, "Operator preference snapshot is not valid JSON: {Path}", SnapshotPath);
        throw new InvalidOperationException($"Operator preference snapshot is invalid: {SnapshotPath}", exception);
    }

    protected override OperatorPreferenceSnapshot OnSnapshotLoaded(OperatorPreferenceSnapshot snapshot)
    {
        if (snapshot.Version != SnapshotVersion)
        {
            throw new InvalidOperationException(
                $"Operator preference snapshot version {snapshot.Version} is not supported. Expected version {SnapshotVersion}: {SnapshotPath}");
        }

        return snapshot;
    }

    private static string ResolveSnapshotPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var directory = Path.Combine(dataRoot, "workstation", "operators");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "preferences.json");
    }
}

public sealed record OperatorPreferenceRecord(string OperatorKey, OperatorPreferenceValuesDto Preferences, DateTimeOffset UpdatedAt);

public sealed record OperatorPreferenceSnapshot(int Version, IReadOnlyList<OperatorPreferenceRecord> Operators);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(OperatorPreferenceSnapshot))]
internal sealed partial class OperatorPreferenceJsonContext : JsonSerializerContext;
