using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>Durable per-operator presentation preferences (theme, density, time zone, descriptions, shortcuts, notifications).</summary>
public interface IOperatorPreferenceStore
{
    Task<OperatorPreferencesDto> GetAsync(string operatorKey, CancellationToken ct = default);

    Task<OperatorPreferencesDto> SetAsync(string operatorKey, OperatorPreferenceValuesDto? preferences, CancellationToken ct = default);
}

/// <summary>
/// JSON-backed store under Meridian's data root, keyed by tenant, company and user so one operator's
/// preferences never reach another. Writes replace the operator's whole set through the snapshot
/// store's atomic replace, like the default operating scope. The one exception is
/// <see cref="OperatorPreferenceValuesDto.DesktopKeyBindings"/>: the desktop owns it, so a save that
/// leaves it null keeps the stored set and a browser save never clears it. Values are validated here
/// so a client cannot store arbitrary text; clients still decode defensively because older hosts or
/// other clients may hold values they do not recognise.
/// </summary>
public sealed partial class FileOperatorPreferenceStore
    : JsonFileSnapshotStore<OperatorPreferenceSnapshot>, IOperatorPreferenceStore
{
    private const int SnapshotVersion = 1;
    private const int MaxTimeZoneLength = 64;
    private const int MaxKeyBindings = 32;
    private const int MaxKeyBindingIdLength = 64;
    private const int MaxKeyBindingComboLength = 64;
    private const int MaxDesktopKeyBindings = 64;
    private const int MaxDesktopActionIdLength = 64;
    private const int MaxDesktopKeyLength = 32;
    private const int MaxDesktopModifiersLength = 64;

    private static readonly HashSet<string> Appearances = new(OperatorPreferenceVocabulary.Appearances, StringComparer.Ordinal);
    private static readonly HashSet<string> Densities = new(OperatorPreferenceVocabulary.Densities, StringComparer.Ordinal);

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
                    var operators = snapshot.Operators ?? [];
                    var existing = operators.FirstOrDefault(item => string.Equals(item.OperatorKey, operatorKey, StringComparison.Ordinal));
                    var values = KeepStoredDesktopKeyBindings(normalized, existing?.Preferences);
                    var others = operators
                        .Where(item => !string.Equals(item.OperatorKey, operatorKey, StringComparison.Ordinal));
                    var next = values is null
                        ? others.ToArray()
                        : others.Append(new OperatorPreferenceRecord(operatorKey, values, updatedAt)).ToArray();
                    var result = new OperatorPreferencesDto(
                        values ?? new OperatorPreferenceValuesDto(),
                        values is null ? null : updatedAt);
                    return (new OperatorPreferenceSnapshot(SnapshotVersion, next), result);
                },
                ct)
            .ConfigureAwait(false);
    }

    // A save without desktop shortcut overrides (every browser save) keeps the desktop's stored set.
    private static OperatorPreferenceValuesDto? KeepStoredDesktopKeyBindings(
        OperatorPreferenceValuesDto? incoming,
        OperatorPreferenceValuesDto? stored)
    {
        if (incoming?.DesktopKeyBindings is not null || stored?.DesktopKeyBindings is not { } kept)
        {
            return incoming;
        }

        return (incoming ?? new OperatorPreferenceValuesDto()) with { DesktopKeyBindings = kept };
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
        var desktopKeyBindings = DesktopKeyBindings(preferences.DesktopKeyBindings);
        var notifications = Notifications(preferences.Notifications);

        if (appearance is null && density is null && timeZone is null
            && preferences.PageDescriptionsHidden is null && keyBindings is null && desktopKeyBindings is null
            && notifications is null)
        {
            return null;
        }

        return new OperatorPreferenceValuesDto(
            appearance,
            density,
            timeZone,
            preferences.PageDescriptionsHidden,
            keyBindings,
            desktopKeyBindings,
            notifications);
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

    // Null stays null ("not sent"); an empty set is kept, because it means "every desktop shortcut on
    // its default". Entries with no key, modifiers or enabled state carry nothing and are dropped.
    private static IReadOnlyDictionary<string, OperatorDesktopShortcutDto>? DesktopKeyBindings(
        IReadOnlyDictionary<string, OperatorDesktopShortcutDto>? bindings)
    {
        const string field = nameof(OperatorPreferenceValuesDto.DesktopKeyBindings);
        if (bindings is null)
        {
            return null;
        }

        if (bindings.Count > MaxDesktopKeyBindings)
        {
            throw new ArgumentException($"{field} holds at most {MaxDesktopKeyBindings} entries.", field);
        }

        var normalized = new SortedDictionary<string, OperatorDesktopShortcutDto>(StringComparer.Ordinal);
        foreach (var (id, shortcut) in bindings)
        {
            var trimmedId = id?.Trim() ?? string.Empty;
            if (trimmedId.Length is 0 or > MaxDesktopActionIdLength || !DesktopActionIdPattern().IsMatch(trimmedId))
            {
                throw new ArgumentException(
                    $"{field} ids must be letters and digits starting with a letter, at most {MaxDesktopActionIdLength} characters.",
                    field);
            }

            var key = string.IsNullOrWhiteSpace(shortcut?.Key) ? null : shortcut.Key.Trim();
            if (key is not null && (key.Length > MaxDesktopKeyLength || !DesktopKeyPattern().IsMatch(key)))
            {
                throw new ArgumentException($"{field} keys must be a key name of at most {MaxDesktopKeyLength} letters and digits.", field);
            }

            var modifiers = string.IsNullOrWhiteSpace(shortcut?.Modifiers) ? null : shortcut.Modifiers.Trim();
            if (modifiers is not null && (modifiers.Length > MaxDesktopModifiersLength || !DesktopModifiersPattern().IsMatch(modifiers)))
            {
                throw new ArgumentException(
                    $"{field} modifiers must be modifier names separated by commas, at most {MaxDesktopModifiersLength} characters.",
                    field);
            }

            if (key is null && modifiers is null && shortcut?.IsEnabled is null)
            {
                continue;
            }

            normalized[trimmedId] = new OperatorDesktopShortcutDto(key, modifiers, shortcut?.IsEnabled);
        }

        return normalized;
    }

    private static OperatorNotificationPreferencesDto? Notifications(OperatorNotificationPreferencesDto? notifications)
    {
        if (notifications is null)
        {
            return null;
        }

        var normalized = notifications with
        {
            QuietHoursStart = ClockTime(notifications.QuietHoursStart, nameof(OperatorNotificationPreferencesDto.QuietHoursStart)),
            QuietHoursEnd = ClockTime(notifications.QuietHoursEnd, nameof(OperatorNotificationPreferencesDto.QuietHoursEnd))
        };

        return normalized == new OperatorNotificationPreferencesDto() ? null : normalized;
    }

    private static string? ClockTime(string? value, string part)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (!ClockTimePattern().IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"Notifications.{part} must be a 24-hour time written HH:mm, such as 22:00.",
                nameof(OperatorPreferenceValuesDto.Notifications));
        }

        return trimmed;
    }

    [GeneratedRegex(@"^([01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ClockTimePattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DesktopActionIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DesktopKeyPattern();

    [GeneratedRegex(@"^[A-Za-z]+(\s*,\s*[A-Za-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex DesktopModifiersPattern();

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
