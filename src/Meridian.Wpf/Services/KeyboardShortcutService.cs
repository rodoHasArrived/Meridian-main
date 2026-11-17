using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Workstation.Controls;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for managing global keyboard shortcuts.
/// Handles keyboard input and maps key combinations to action identifiers.
/// </summary>
public sealed class KeyboardShortcutService
{
    private static readonly Lazy<KeyboardShortcutService> _instance = new(() => new KeyboardShortcutService());

    /// <summary>
    /// Actions whose bindings an operator override may never reduce to a bare key or a
    /// Shift-only chord: they start or stop collection, drop the provider connection, or delete
    /// data, so an accidental keystroke must not trigger them. Default bindings are unaffected.
    /// </summary>
    private static readonly HashSet<string> ModifierRequiredActions = new(StringComparer.Ordinal)
    {
        "StartCollector",
        "StopCollector",
        "ToggleConnection",
        "DeleteSelected"
    };

    private readonly Dictionary<string, ShortcutAction> _shortcuts = new();
    private readonly Dictionary<string, DefaultBinding> _defaultBindings = new(StringComparer.Ordinal);
    private Action<IReadOnlyDictionary<string, DesktopShortcutOverride>>? _saveOverrides;
    private FrameworkElement? _rootElement;
    private bool _isEnabled = true;

    /// <summary>
    /// Gets the singleton instance of the KeyboardShortcutService.
    /// </summary>
    public static KeyboardShortcutService Instance => _instance.Value;

    private KeyboardShortcutService()
    {
        InitializeDefaultShortcuts();
        foreach (var (actionId, action) in _shortcuts)
        {
            _defaultBindings[actionId] = new DefaultBinding(action.Key, action.Modifiers, action.IsEnabled);
        }
    }

    /// <summary>
    /// Creates an isolated instance with the default shortcuts, for tests that must not mutate
    /// the process-wide singleton.
    /// </summary>
    internal static KeyboardShortcutService CreateIsolatedForTests() => new();

    /// <summary>
    /// Loads the operator's persisted shortcut overrides, applies them, and saves every later
    /// <see cref="UpdateShortcut"/>, <see cref="SetShortcutEnabled"/>, and
    /// <see cref="ResetToDefaults"/> change through <paramref name="saveOverrides"/>.
    /// </summary>
    public void AttachPreferenceStore(
        IReadOnlyDictionary<string, DesktopShortcutOverride> persistedOverrides,
        Action<IReadOnlyDictionary<string, DesktopShortcutOverride>> saveOverrides)
    {
        ArgumentNullException.ThrowIfNull(persistedOverrides);
        ArgumentNullException.ThrowIfNull(saveOverrides);

        _saveOverrides = null;
        ApplyOverrides(persistedOverrides);
        _saveOverrides = saveOverrides;
    }

    /// <summary>
    /// Restores every default shortcut to its default binding, then applies the valid entries of
    /// <paramref name="overrides"/>. An entry is skipped when its action is not a default
    /// shortcut, its key or modifiers do not parse, it would leave a modifier-required action
    /// without Ctrl or Alt, or it collides with another shortcut's binding.
    /// </summary>
    /// <returns>The action ids whose override was skipped.</returns>
    public IReadOnlyList<string> ApplyOverrides(IReadOnlyDictionary<string, DesktopShortcutOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        RestoreDefaultBindings();
        var skipped = new List<string>();
        var rebound = new List<string>();

        // Phase 1: apply every well-formed, allowed override without checking collisions, so a
        // saved swap of two shortcuts (A takes B's chord and B takes A's) applies as a whole
        // instead of the first half colliding with the not-yet-moved second.
        foreach (var (actionId, shortcutOverride) in overrides)
        {
            if (!TryApplyOverride(actionId, shortcutOverride, out var rebinds))
            {
                skipped.Add(actionId);
            }
            else if (rebinds)
            {
                rebound.Add(actionId);
            }
        }

        // Phase 2: revert rebound actions whose final chord collides with another shortcut, one at
        // a time in ordinal order, until no rebound action collides. Collisions that exist only
        // between default bindings (for example Escape, shared by Cancel Backfill and Close Dialog)
        // are left as they were.
        rebound.Sort(StringComparer.Ordinal);
        while (rebound.FirstOrDefault(id =>
                   HasConflict(_shortcuts[id].Key, _shortcuts[id].Modifiers, excludeActionId: id)) is { } colliding)
        {
            RestoreDefaultBinding(colliding);
            rebound.Remove(colliding);
            skipped.Add(colliding);
        }

        return skipped;
    }

    /// <summary>
    /// Rebinds <paramref name="actionId"/> to the chord after checking it is allowed and free,
    /// and saves the change when a preference store is attached.
    /// </summary>
    /// <returns>Null on success; otherwise an operator-facing reason the chord was refused.</returns>
    public string? TryUpdateShortcut(string actionId, Key key, ModifierKeys modifiers)
    {
        if (!_shortcuts.TryGetValue(actionId, out var action))
        {
            return "This shortcut is no longer registered.";
        }

        if (key == Key.None || IsModifierKey(key))
        {
            return "Press a key together with any modifiers.";
        }

        if (!IsBindingAllowed(actionId, key, modifiers))
        {
            return $"{action.Description} must use Ctrl or Alt so it cannot be triggered by accident.";
        }

        var conflict = FindConflict(key, modifiers, excludeActionId: actionId);
        if (conflict is not null)
        {
            return $"{FormatShortcut(key, modifiers)} is already used by {conflict.Description}.";
        }

        UpdateShortcut(actionId, key, modifiers);
        return null;
    }

    /// <summary>
    /// Restores one default shortcut's binding and enabled state and saves the change.
    /// </summary>
    public void ResetShortcut(string actionId)
    {
        if (RestoreDefaultBinding(actionId))
        {
            PersistOverrides();
        }
    }

    /// <summary>True when the action's current binding or enabled state differs from its default.</summary>
    public bool IsOverridden(string actionId) =>
        _defaultBindings.TryGetValue(actionId, out var defaults)
        && _shortcuts.TryGetValue(actionId, out var action)
        && (action.Key != defaults.Key || action.Modifiers != defaults.Modifiers || action.IsEnabled != defaults.IsEnabled);

    /// <summary>Gets the default chord of a default shortcut, formatted for display.</summary>
    public string? GetDefaultShortcutText(string actionId) =>
        _defaultBindings.TryGetValue(actionId, out var defaults) ? FormatShortcut(defaults.Key, defaults.Modifiers) : null;

    /// <summary>
    /// Turns a key-down event into a capturable chord. Alt chords arrive as <see cref="Key.System"/>
    /// with the real key in <paramref name="systemKey"/>; a press of a modifier alone is not a chord.
    /// </summary>
    public static bool TryGetChord(Key key, Key systemKey, ModifierKeys modifiers, out Key chordKey)
    {
        chordKey = key == Key.System ? systemKey : key;
        return chordKey != Key.None && !IsModifierKey(chordKey) && (modifiers & ModifierKeys.Windows) == ModifierKeys.None;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift
        or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System
        or Key.ImeProcessed or Key.DeadCharProcessed;

    private ShortcutAction? FindConflict(Key key, ModifierKeys modifiers, string excludeActionId)
    {
        foreach (var (actionId, action) in _shortcuts)
        {
            if (actionId != excludeActionId && action.Key == key && action.Modifiers == modifiers)
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the current differences from the default bindings, in the form persisted to the
    /// operator's desktop preferences.
    /// </summary>
    public IReadOnlyDictionary<string, DesktopShortcutOverride> GetOverrides()
    {
        var overrides = new Dictionary<string, DesktopShortcutOverride>(StringComparer.Ordinal);
        foreach (var (actionId, defaults) in _defaultBindings)
        {
            if (!_shortcuts.TryGetValue(actionId, out var action))
            {
                continue;
            }

            var bindingChanged = action.Key != defaults.Key || action.Modifiers != defaults.Modifiers;
            var enabledChanged = action.IsEnabled != defaults.IsEnabled;
            if (!bindingChanged && !enabledChanged)
            {
                continue;
            }

            overrides[actionId] = new DesktopShortcutOverride(
                bindingChanged ? action.Key.ToString() : null,
                bindingChanged ? action.Modifiers.ToString() : null,
                enabledChanged ? action.IsEnabled : null);
        }

        return overrides;
    }

    /// <summary>
    /// Restores every default shortcut's binding and enabled state and clears the persisted
    /// overrides.
    /// </summary>
    public void ResetToDefaults()
    {
        RestoreDefaultBindings();
        PersistOverrides();
    }

    /// <summary>
    /// True when an operator may bind <paramref name="actionId"/> to this chord: actions that
    /// start or stop collection, drop the connection, or delete data must keep Ctrl or Alt.
    /// </summary>
    public static bool IsBindingAllowed(string actionId, Key key, ModifierKeys modifiers)
    {
        if (key == Key.None)
        {
            return false;
        }

        return !ModifierRequiredActions.Contains(actionId)
               || (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != ModifierKeys.None;
    }

    private bool TryApplyOverride(string actionId, DesktopShortcutOverride? shortcutOverride, out bool rebinds)
    {
        rebinds = false;
        if (shortcutOverride is null
            || !_defaultBindings.ContainsKey(actionId)
            || !_shortcuts.TryGetValue(actionId, out var action))
        {
            return false;
        }

        var key = action.Key;
        var modifiers = action.Modifiers;
        if (shortcutOverride.Key is not null || shortcutOverride.Modifiers is not null)
        {
            if (shortcutOverride.Key is not null
                && !Enum.TryParse(shortcutOverride.Key, ignoreCase: true, out key))
            {
                return false;
            }

            if (shortcutOverride.Modifiers is not null
                && !Enum.TryParse(shortcutOverride.Modifiers, ignoreCase: true, out modifiers))
            {
                return false;
            }

            const ModifierKeys knownModifiers =
                ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows;
            if (!Enum.IsDefined(key)
                || IsModifierKey(key)
                || (modifiers & ~knownModifiers) != ModifierKeys.None
                || !IsBindingAllowed(actionId, key, modifiers))
            {
                return false;
            }

            rebinds = key != action.Key || modifiers != action.Modifiers;
        }

        action.Key = key;
        action.Modifiers = modifiers;
        if (shortcutOverride.IsEnabled is { } isEnabled)
        {
            action.IsEnabled = isEnabled;
        }

        return true;
    }

    private void RestoreDefaultBindings()
    {
        foreach (var (actionId, defaults) in _defaultBindings)
        {
            if (_shortcuts.TryGetValue(actionId, out var action))
            {
                action.Key = defaults.Key;
                action.Modifiers = defaults.Modifiers;
                action.IsEnabled = defaults.IsEnabled;
            }
        }
    }

    private bool RestoreDefaultBinding(string actionId)
    {
        if (!_defaultBindings.TryGetValue(actionId, out var defaults)
            || !_shortcuts.TryGetValue(actionId, out var action))
        {
            return false;
        }

        action.Key = defaults.Key;
        action.Modifiers = defaults.Modifiers;
        action.IsEnabled = defaults.IsEnabled;
        return true;
    }

    private void PersistOverrides() => _saveOverrides?.Invoke(GetOverrides());

    private readonly record struct DefaultBinding(Key Key, ModifierKeys Modifiers, bool IsEnabled);

    /// <summary>
    /// Gets or sets whether keyboard shortcuts are enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// Gets all registered shortcuts.
    /// </summary>
    public IReadOnlyDictionary<string, ShortcutAction> Shortcuts => _shortcuts;

    /// <summary>
    /// Event raised when a shortcut is invoked.
    /// </summary>
    public event EventHandler<ShortcutInvokedEventArgs>? ShortcutInvoked;

    /// <summary>
    /// Initializes the keyboard shortcut service with the root element.
    /// </summary>
    /// <param name="element">The root framework element to attach keyboard handling to.</param>
    public void Initialize(FrameworkElement element)
    {
        if (_rootElement != null)
        {
            _rootElement.PreviewKeyDown -= OnPreviewKeyDown;
        }

        _rootElement = element ?? throw new ArgumentNullException(nameof(element));
        _rootElement.PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Handles a key down event and triggers the appropriate shortcut.
    /// </summary>
    /// <param name="e">The key event arguments.</param>
    public void HandleKeyDown(KeyEventArgs e)
    {
        if (!_isEnabled || e.Handled)
            return;

        var key = e.Key;
        var modifiers = Keyboard.Modifiers;

        // Handle system key (Alt combinations)
        if (key == Key.System)
        {
            key = e.SystemKey;
        }

        // Context-specific gestures defer to the focused element: this tunneling handler runs
        // before any control's own routed-command binding, so consuming Ctrl+C, Ctrl+F, or
        // Escape here would starve the dense grids' copy, filter-focus, and close-details
        // bindings (and a text editor's native copy) whenever a global registration shares the
        // gesture. When the focused element can serve the matching routed command the event is
        // left unhandled and routes to it; the registrations remain for the shortcut catalog
        // and as the behavior everywhere nothing focused can serve the gesture.
        if (DefersToFocusedRoutedCommand(key, modifiers))
            return;

        foreach (var kvp in _shortcuts)
        {
            var action = kvp.Value;
            if (!action.IsEnabled)
                continue;

            if (action.Key == key && action.Modifiers == modifiers)
            {
                e.Handled = true;
                OnShortcutInvoked(kvp.Key, action);
                return;
            }
        }
    }

    /// <summary>
    /// True when this gesture belongs to the focused element rather than the global handler:
    /// dense grids carry routed bindings for copy (<see cref="ApplicationCommands.Copy"/>),
    /// filter focus (<see cref="DenseGridKeyboardCommands.FocusFilter"/>), and close-details
    /// (<see cref="DenseGridKeyboardCommands.CloseDetails"/>), and text editors serve the
    /// routed Copy natively — all of which this service's tunneling PreviewKeyDown would
    /// otherwise consume before they ever see the key.
    /// </summary>
    internal static bool DefersToFocusedRoutedCommand(Key key, ModifierKeys modifiers)
    {
        RoutedCommand? routed = (key, modifiers) switch
        {
            (Key.C, ModifierKeys.Control) => ApplicationCommands.Copy,
            (Key.F, ModifierKeys.Control) => DenseGridKeyboardCommands.FocusFilter,
            (Key.Escape, ModifierKeys.None) => DenseGridKeyboardCommands.CloseDetails,
            _ => null
        };

        if (routed is null || Keyboard.FocusedElement is not { } focused)
        {
            return false;
        }

        if (routed.CanExecute(null, focused))
        {
            return true;
        }

        // The dense grid mirrors its chrome gestures onto external composition surfaces (the
        // filter target, the inspector rail) as key bindings whose CommandTarget is the grid,
        // because those surfaces are siblings of the grid and a query routed from the focused
        // element can never reach the grid's command bindings. The deferral must honor the same
        // compensation, or this tunneling handler would consume the gesture before the mirrored
        // binding ever runs: a matching binding on the focused element's chain whose target can
        // execute serves the gesture.
        return FindMirroredRoutedCommandTarget(focused as DependencyObject, key, modifiers, routed) is { } target
               && routed.CanExecute(null, target);
    }

    /// <summary>
    /// The <see cref="KeyBinding.CommandTarget"/> of the nearest key binding for this gesture
    /// and routed command on the focused element's ancestor chain — the same chain WPF's own
    /// input processing consults, nearest binding first — or null when no such binding exists.
    /// </summary>
    private static IInputElement? FindMirroredRoutedCommandTarget(
        DependencyObject? element, Key key, ModifierKeys modifiers, RoutedCommand routed)
    {
        while (element is not null)
        {
            if (element is UIElement scope)
            {
                foreach (InputBinding binding in scope.InputBindings)
                {
                    if (binding is KeyBinding { CommandTarget: { } target } keyBinding
                        && keyBinding.Key == key
                        && keyBinding.Modifiers == modifiers
                        && ReferenceEquals(keyBinding.Command, routed))
                    {
                        return target;
                    }
                }
            }

            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleKeyDown(e);
    }

    /// <summary>
    /// Registers default keyboard shortcuts.
    /// </summary>
    private void InitializeDefaultShortcuts()
    {
        // Navigation shortcuts
        RegisterShortcut("NavigateDashboard", Key.D, ModifierKeys.Control,
            "Navigate to Strategy Workspace", ShortcutCategory.Navigation);

        RegisterShortcut("NavigateSymbols", Key.Y, ModifierKeys.Control,
            "Navigate to Symbols", ShortcutCategory.Navigation);

        RegisterShortcut("NavigateBackfill", Key.B, ModifierKeys.Control,
            "Navigate to Backfill", ShortcutCategory.Navigation);

        RegisterShortcut("NavigateSettings", Key.D0, ModifierKeys.Control,
            "Open Settings", ShortcutCategory.Navigation);

        RegisterShortcut("NavigateWatchlist", Key.W, ModifierKeys.Control,
            "Navigate to Watchlist", ShortcutCategory.Navigation);

        RegisterShortcut("NavigateDataQuality", Key.Q, ModifierKeys.Control,
            "Navigate to Data Quality", ShortcutCategory.Navigation);

        // Collector shortcuts
        RegisterShortcut("StartCollector", Key.S, ModifierKeys.Control | ModifierKeys.Shift,
            "Start Collector", ShortcutCategory.Collector);

        RegisterShortcut("StopCollector", Key.Q, ModifierKeys.Control | ModifierKeys.Shift,
            "Stop Collector", ShortcutCategory.Collector);

        RegisterShortcut("RefreshStatus", Key.F5, ModifierKeys.None,
            "Refresh Status", ShortcutCategory.Collector);

        RegisterShortcut("ToggleConnection", Key.C, ModifierKeys.Control | ModifierKeys.Shift,
            "Toggle Connection", ShortcutCategory.Collector);

        // Backfill shortcuts
        RegisterShortcut("RunBackfill", Key.R, ModifierKeys.Control,
            "Run Backfill", ShortcutCategory.Backfill);

        RegisterShortcut("PauseBackfill", Key.P, ModifierKeys.Control | ModifierKeys.Shift,
            "Pause/Resume Backfill", ShortcutCategory.Backfill);

        RegisterShortcut("CancelBackfill", Key.Escape, ModifierKeys.None,
            "Cancel Backfill", ShortcutCategory.Backfill);

        // Symbol shortcuts
        RegisterShortcut("AddSymbol", Key.N, ModifierKeys.Control,
            "Add New Symbol", ShortcutCategory.Symbols);

        RegisterShortcut("SearchSymbols", Key.F, ModifierKeys.Control,
            "Search Symbols", ShortcutCategory.Symbols);

        RegisterShortcut("DeleteSelected", Key.Delete, ModifierKeys.None,
            "Delete Selected", ShortcutCategory.Symbols);

        RegisterShortcut("SelectAll", Key.A, ModifierKeys.Control,
            "Select All", ShortcutCategory.Symbols);

        // View shortcuts
        RegisterShortcut("ToggleTheme", Key.T, ModifierKeys.Control | ModifierKeys.Shift,
            "Toggle Theme", ShortcutCategory.View);

        RegisterShortcut("ViewLogs", Key.L, ModifierKeys.Control,
            "View Logs", ShortcutCategory.View);

        RegisterShortcut("ZoomIn", Key.Add, ModifierKeys.Control,
            "Zoom In", ShortcutCategory.View);

        RegisterShortcut("ZoomOut", Key.Subtract, ModifierKeys.Control,
            "Zoom Out", ShortcutCategory.View);

        RegisterShortcut("ToggleFullscreen", Key.F11, ModifierKeys.None,
            "Toggle Fullscreen", ShortcutCategory.View);

        // General shortcuts
        RegisterShortcut("Save", Key.S, ModifierKeys.Control,
            "Save", ShortcutCategory.General);

        RegisterShortcut("Help", Key.F1, ModifierKeys.None,
            "Show Help", ShortcutCategory.General);

        RegisterShortcut("QuickCommand", Key.K, ModifierKeys.Control,
            "Quick Command", ShortcutCategory.General);

        RegisterShortcut("CloseDialog", Key.Escape, ModifierKeys.None,
            "Close Dialog", ShortcutCategory.General);

        RegisterShortcut("Copy", Key.C, ModifierKeys.Control,
            "Copy", ShortcutCategory.General);

        RegisterShortcut("Paste", Key.V, ModifierKeys.Control,
            "Paste", ShortcutCategory.General);

        RegisterShortcut("Undo", Key.Z, ModifierKeys.Control,
            "Undo", ShortcutCategory.General);

        RegisterShortcut("Redo", Key.Y, ModifierKeys.Control,
            "Redo", ShortcutCategory.General);
    }

    /// <summary>
    /// Registers a keyboard shortcut.
    /// </summary>
    /// <param name="actionId">The unique action identifier.</param>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys.</param>
    /// <param name="description">The shortcut description.</param>
    /// <param name="category">The shortcut category.</param>
    public void RegisterShortcut(
        string actionId,
        Key key,
        ModifierKeys modifiers,
        string description,
        ShortcutCategory category = ShortcutCategory.General)
    {
        var action = new ShortcutAction
        {
            ActionId = actionId,
            Key = key,
            Modifiers = modifiers,
            Description = description,
            Category = category,
            IsEnabled = true
        };

        _shortcuts[actionId] = action;
    }

    /// <summary>
    /// Updates an existing shortcut's key binding.
    /// </summary>
    /// <param name="actionId">The action identifier.</param>
    /// <param name="key">The new key.</param>
    /// <param name="modifiers">The new modifier keys.</param>
    public void UpdateShortcut(string actionId, Key key, ModifierKeys modifiers)
    {
        if (_shortcuts.TryGetValue(actionId, out var action))
        {
            action.Key = key;
            action.Modifiers = modifiers;
            PersistOverrides();
        }
    }

    /// <summary>
    /// Enables or disables a specific shortcut.
    /// </summary>
    /// <param name="actionId">The action identifier.</param>
    /// <param name="enabled">Whether the shortcut should be enabled.</param>
    public void SetShortcutEnabled(string actionId, bool enabled)
    {
        if (_shortcuts.TryGetValue(actionId, out var action))
        {
            action.IsEnabled = enabled;
            PersistOverrides();
        }
    }

    /// <summary>
    /// Removes a shortcut registration.
    /// </summary>
    /// <param name="actionId">The action identifier to remove.</param>
    public void UnregisterShortcut(string actionId)
    {
        _shortcuts.Remove(actionId);
    }

    private void OnShortcutInvoked(string actionId, ShortcutAction action)
    {
        ShortcutInvoked?.Invoke(this, new ShortcutInvokedEventArgs
        {
            ActionId = actionId,
            Action = action
        });

    }

    /// <summary>
    /// Gets a formatted shortcut string (e.g., "Ctrl+S").
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys.</param>
    /// <returns>A formatted string representation of the shortcut.</returns>
    public static string FormatShortcut(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(FormatKey(key));

        return string.Join("+", parts);
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.NumPad0 => "Num0",
            Key.NumPad1 => "Num1",
            Key.NumPad2 => "Num2",
            Key.NumPad3 => "Num3",
            Key.NumPad4 => "Num4",
            Key.NumPad5 => "Num5",
            Key.NumPad6 => "Num6",
            Key.NumPad7 => "Num7",
            Key.NumPad8 => "Num8",
            Key.NumPad9 => "Num9",
            Key.Add => "+",
            Key.Subtract => "-",
            Key.Multiply => "*",
            Key.Divide => "/",
            Key.Escape => "Esc",
            Key.Delete => "Del",
            Key.Back => "Backspace",
            Key.Return => "Enter",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            _ => key.ToString()
        };
    }

    /// <summary>
    /// Gets all shortcuts grouped by category.
    /// </summary>
    /// <returns>A dictionary of shortcuts grouped by category.</returns>
    public Dictionary<ShortcutCategory, List<ShortcutAction>> GetShortcutsByCategory()
    {
        var result = new Dictionary<ShortcutCategory, List<ShortcutAction>>();

        foreach (var shortcut in _shortcuts.Values)
        {
            if (!result.ContainsKey(shortcut.Category))
            {
                result[shortcut.Category] = new List<ShortcutAction>();
            }
            result[shortcut.Category].Add(shortcut);
        }

        return result;
    }

    /// <summary>
    /// Gets a shortcut by action ID.
    /// </summary>
    /// <param name="actionId">The action identifier.</param>
    /// <returns>The shortcut action, or null if not found.</returns>
    public ShortcutAction? GetShortcut(string actionId)
    {
        return _shortcuts.TryGetValue(actionId, out var action) ? action : null;
    }

    /// <summary>
    /// Checks if a shortcut with the given key combination is already registered.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys.</param>
    /// <param name="excludeActionId">Optional action ID to exclude from the check.</param>
    /// <returns>True if a conflict exists.</returns>
    public bool HasConflict(Key key, ModifierKeys modifiers, string? excludeActionId = null)
    {
        foreach (var kvp in _shortcuts)
        {
            if (excludeActionId != null && kvp.Key == excludeActionId)
                continue;

            if (kvp.Value.Key == key && kvp.Value.Modifiers == modifiers)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detaches the keyboard event handler.
    /// </summary>
    public void Detach()
    {
        if (_rootElement != null)
        {
            _rootElement.PreviewKeyDown -= OnPreviewKeyDown;
            _rootElement = null;
        }
    }
}

/// <summary>
/// Shortcut action definition.
/// </summary>
public sealed class ShortcutAction
{
    /// <summary>
    /// Gets or sets the unique action identifier.
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public Key Key { get; set; }

    /// <summary>
    /// Gets or sets the modifier keys.
    /// </summary>
    public ModifierKeys Modifiers { get; set; }

    /// <summary>
    /// Gets or sets the shortcut description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shortcut category.
    /// </summary>
    public ShortcutCategory Category { get; set; }

    /// <summary>
    /// Gets or sets whether the shortcut is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets the formatted shortcut string.
    /// </summary>
    public string FormattedShortcut => KeyboardShortcutService.FormatShortcut(Key, Modifiers);
}

/// <summary>
/// Shortcut categories.
/// </summary>
public enum ShortcutCategory : byte
{
    /// <summary>
    /// General application shortcuts.
    /// </summary>
    General,

    /// <summary>
    /// Navigation shortcuts.
    /// </summary>
    Navigation,

    /// <summary>
    /// Collector control shortcuts.
    /// </summary>
    Collector,

    /// <summary>
    /// Backfill operation shortcuts.
    /// </summary>
    Backfill,

    /// <summary>
    /// Symbol management shortcuts.
    /// </summary>
    Symbols,

    /// <summary>
    /// View and display shortcuts.
    /// </summary>
    View
}

/// <summary>
/// Shortcut invoked event args.
/// </summary>
public sealed class ShortcutInvokedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the action identifier.
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shortcut action.
    /// </summary>
    public ShortcutAction? Action { get; set; }
}
