using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for managing global keyboard shortcuts.
/// Handles keyboard input and maps key combinations to action identifiers.
/// </summary>
public sealed class KeyboardShortcutService
{
    private static readonly Lazy<KeyboardShortcutService> _instance = new(() => new KeyboardShortcutService());

    private readonly Dictionary<string, ShortcutAction> _shortcuts = new();
    private FrameworkElement? _rootElement;
    private bool _isEnabled = true;

    /// <summary>
    /// Gets the singleton instance of the KeyboardShortcutService.
    /// </summary>
    public static KeyboardShortcutService Instance => _instance.Value;

    private KeyboardShortcutService()
    {
        InitializeDefaultShortcuts();
    }

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
        if (!_isEnabled || e.Handled) return;

        var key = e.Key;
        var modifiers = Keyboard.Modifiers;

        // Handle system key (Alt combinations)
        if (key == Key.System)
        {
            key = e.SystemKey;
        }

        foreach (var kvp in _shortcuts)
        {
            var action = kvp.Value;
            if (!action.IsEnabled) continue;

            if (action.Key == key && action.Modifiers == modifiers)
            {
                e.Handled = true;
                OnShortcutInvoked(kvp.Key, action);
                return;
            }
        }
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
            "Navigate to Dashboard", ShortcutCategory.Navigation);

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
