using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Maps the shared operator preference vocabulary (<see cref="OperatorPreferenceVocabulary"/>) to the
/// desktop shell's own types. The browser has three densities and the desktop shell two, so
/// <c>default</c> and <c>spacious</c> both show as <see cref="ShellDensityMode.Standard"/>, and a
/// desktop save never narrows an operator's browser <c>spacious</c> to <c>default</c>.
/// </summary>
public static class DesktopOperatorPreferenceMapping
{
    /// <summary>The desktop theme for a host appearance, or null for a value the desktop does not know.</summary>
    public static AppTheme? ToAppTheme(string? appearance) => appearance switch
    {
        OperatorPreferenceVocabulary.AppearanceLight => AppTheme.Light,
        OperatorPreferenceVocabulary.AppearanceDark => AppTheme.Dark,
        OperatorPreferenceVocabulary.AppearanceSystem => AppTheme.System,
        _ => null
    };

    public static string ToAppearance(AppTheme theme) => theme switch
    {
        AppTheme.Dark => OperatorPreferenceVocabulary.AppearanceDark,
        AppTheme.System => OperatorPreferenceVocabulary.AppearanceSystem,
        _ => OperatorPreferenceVocabulary.AppearanceLight
    };

    /// <summary>The desktop density for a host density, or null for a value the desktop does not know.</summary>
    public static ShellDensityMode? ToShellDensity(string? density) => density switch
    {
        OperatorPreferenceVocabulary.DensityCompact => ShellDensityMode.Compact,
        OperatorPreferenceVocabulary.DensityDefault or OperatorPreferenceVocabulary.DensitySpacious => ShellDensityMode.Standard,
        _ => null
    };

    /// <summary>
    /// The host density to save for a desktop density. <see cref="ShellDensityMode.Standard"/> keeps a
    /// stored <c>default</c> or <c>spacious</c>, because both already show as Standard on the desktop.
    /// </summary>
    public static string ToHostDensity(ShellDensityMode density, string? currentHostDensity) => density switch
    {
        ShellDensityMode.Compact => OperatorPreferenceVocabulary.DensityCompact,
        _ when currentHostDensity is OperatorPreferenceVocabulary.DensityDefault or OperatorPreferenceVocabulary.DensitySpacious
            => currentHostDensity,
        _ => OperatorPreferenceVocabulary.DensityDefault
    };

    /// <summary>
    /// Resolves an IANA display time zone with this machine's time zone data. False for a missing or
    /// unknown zone, in which case the desktop keeps showing times as it does today.
    /// </summary>
    public static bool TryResolveTimeZone(string? ianaZone, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(ianaZone))
        {
            return false;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaZone.Trim(), out var found))
        {
            zone = found;
            return true;
        }

        return false;
    }
}

/// <summary>Reads and replaces the signed-in operator's presentation preferences on the host.</summary>
public interface IOperatorPreferencesApi
{
    /// <summary>The stored set, or null when the host could not be read (no session, older host, offline).</summary>
    Task<OperatorPreferencesDto?> GetAsync(CancellationToken ct = default);

    /// <summary>Replaces the stored set. Returns the saved set, or null when the save failed.</summary>
    Task<OperatorPreferencesDto?> SaveAsync(OperatorPreferenceValuesDto preferences, CancellationToken ct = default);
}

/// <summary>Desktop client for <see cref="UiApiRoutes.WorkstationOperatorPreferences"/>.</summary>
public sealed class OperatorPreferencesApiClient : IOperatorPreferencesApi
{
    private readonly ApiClientService _apiClient;

    public OperatorPreferencesApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<OperatorPreferencesDto?> GetAsync(CancellationToken ct = default)
    {
        var response = await _apiClient
            .GetWithResponseAsync<OperatorPreferencesDto>(UiApiRoutes.WorkstationOperatorPreferences, ct)
            .ConfigureAwait(false);
        return response.Success ? response.Data : null;
    }

    public async Task<OperatorPreferencesDto?> SaveAsync(OperatorPreferenceValuesDto preferences, CancellationToken ct = default)
    {
        var response = await _apiClient
            .PutWithResponseAsync<OperatorPreferencesDto>(
                UiApiRoutes.WorkstationOperatorPreferences,
                new OperatorPreferencesSaveRequest(preferences),
                ct)
            .ConfigureAwait(false);
        return response.Success ? response.Data : null;
    }
}

/// <summary>
/// The desktop presentation preferences the host sync reads and writes: the shell theme and density.
/// Implemented by the WPF shell over its theme service and desktop shell preferences.
/// </summary>
public interface IDesktopPreferenceSurface
{
    AppTheme Theme { get; }

    ShellDensityMode Density { get; }

    void ApplyTheme(AppTheme theme);

    void ApplyDensity(ShellDensityMode density);

    /// <summary>Raised after the operator changes the theme or density on this desktop.</summary>
    event EventHandler? PreferenceChanged;
}

/// <summary>
/// Keeps the desktop shell's theme and density in step with the operator's host preferences, the
/// same set the browser workstation syncs (<see cref="OperatorPreferencesDto"/>).
/// <list type="bullet">
/// <item>On start, the values the host holds are applied. Nothing is saved: the desktop does not
/// persist its theme, so its startup theme is a default, not an operator choice.</item>
/// <item>When the operator changes the theme or density here, the latest host set is re-read and
/// saved with only the appearance and density replaced, so browser-only values (time zone,
/// page descriptions, shortcut overrides, a <c>spacious</c> density) are kept.</item>
/// <item>When the host cannot be read at start (no session, an older host, offline), the desktop
/// stays local for the run and saves nothing. A failed save keeps the local change.</item>
/// </list>
/// The display time zone is applied to <see cref="Meridian.Ui.Services.DisplayTimeZone"/>, which desktop
/// timestamps are formatted in; it is read at start and again whenever the desktop saves.
/// </summary>
public sealed class DesktopOperatorPreferenceSync : IDisposable
{
    private readonly IOperatorPreferencesApi _api;
    private readonly IDesktopPreferenceSurface _surface;
    private readonly Action<TimeZoneInfo?> _applyDisplayTimeZone;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _applyingHostValues;
    private bool _syncing;
    private int _disposed;

    /// <param name="applyDisplayTimeZone">Where the resolved display zone goes; defaults to the shared
    /// <see cref="Meridian.Ui.Services.DisplayTimeZone"/> that desktop timestamps read. Tests pass their own.</param>
    public DesktopOperatorPreferenceSync(
        IOperatorPreferencesApi api,
        IDesktopPreferenceSurface surface,
        Action<TimeZoneInfo?>? applyDisplayTimeZone = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _applyDisplayTimeZone = applyDisplayTimeZone ?? Meridian.Ui.Services.DisplayTimeZone.Set;
    }

    /// <summary>The operator's display time zone from the host, when it resolves on this machine (else null, meaning UTC).</summary>
    public TimeZoneInfo? DisplayTimeZone { get; private set; }

    /// <summary>True once the host was read and local changes are being saved.</summary>
    public bool IsSyncing => _syncing;

    /// <summary>Reads the host set, applies it, and starts saving operator changes. Safe to call once.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        OperatorPreferencesDto? host;
        try
        {
            host = await _api.GetAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            host = null;
        }

        if (host is null || linked.IsCancellationRequested)
        {
            return;
        }

        ApplyHostValues(host.Preferences);
        _syncing = true;
        _surface.PreferenceChanged += OnPreferenceChanged;
    }

    private void ApplyHostValues(OperatorPreferenceValuesDto values)
    {
        _applyingHostValues = true;
        try
        {
            if (DesktopOperatorPreferenceMapping.ToAppTheme(values.Appearance) is { } theme && theme != _surface.Theme)
            {
                _surface.ApplyTheme(theme);
            }

            if (DesktopOperatorPreferenceMapping.ToShellDensity(values.Density) is { } density && density != _surface.Density)
            {
                _surface.ApplyDensity(density);
            }

            ApplyDisplayTimeZone(values.DisplayTimeZone);
        }
        finally
        {
            _applyingHostValues = false;
        }
    }

    // Desktop timestamps (FormatHelpers.FormatDisplayTimestamp) read the shared display zone; a zone
    // this machine cannot resolve leaves them in UTC.
    private void ApplyDisplayTimeZone(string? ianaZone)
    {
        DisplayTimeZone = DesktopOperatorPreferenceMapping.TryResolveTimeZone(ianaZone, out var zone) ? zone : null;
        _applyDisplayTimeZone(DisplayTimeZone);
    }

    private void OnPreferenceChanged(object? sender, EventArgs e)
    {
        if (_applyingHostValues || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _ = SaveLocalChangeAsync(_lifetime.Token);
    }

    /// <summary>
    /// Saves the current desktop theme and density into the operator's host set, keeping every other
    /// stored value. Exposed for tests; the sync calls it after each operator change.
    /// </summary>
    public async Task SaveLocalChangeAsync(CancellationToken ct = default)
    {
        try
        {
            await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var latest = await _api.GetAsync(ct).ConfigureAwait(false);
            if (latest is null)
            {
                return; // Keep the local change; the next change retries.
            }

            var current = latest.Preferences;
            ApplyDisplayTimeZone(current.DisplayTimeZone);
            var next = current with
            {
                Appearance = DesktopOperatorPreferenceMapping.ToAppearance(_surface.Theme),
                Density = DesktopOperatorPreferenceMapping.ToHostDensity(_surface.Density, current.Density)
            };
            if (next.Appearance == current.Appearance && next.Density == current.Density && latest.UpdatedAt is not null)
            {
                return;
            }

            await _api.SaveAsync(next, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The desktop is closing.
        }
        catch (Exception)
        {
            // Keep the local change; the next change retries.
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _surface.PreferenceChanged -= OnPreferenceChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
