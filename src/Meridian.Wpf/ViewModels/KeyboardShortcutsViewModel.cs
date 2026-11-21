using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Keyboard Shortcuts page: the grouped shortcut catalog plus in-app
/// rebinding (key-chord capture), per-shortcut enable/disable and reset, and reset-all.
/// Every change goes through <see cref="KeyboardShortcutService"/>, which saves the operator's
/// overrides to the desktop shell preferences once its preference store is attached.
/// </summary>
public sealed partial class KeyboardShortcutsViewModel : BindableBase
{
    private static readonly ShortcutCategory[] CategoryOrder =
    {
        ShortcutCategory.Navigation,
        ShortcutCategory.Collector,
        ShortcutCategory.Backfill,
        ShortcutCategory.Symbols,
        ShortcutCategory.View,
        ShortcutCategory.General
    };

    private static readonly Dictionary<ShortcutCategory, (string Label, string Icon, string Description)> CategoryInfo = new()
    {
        [ShortcutCategory.Navigation] = ("Navigation", "", "Move between pages and sections of the application."),
        [ShortcutCategory.Collector] = ("Data Collection", "", "Control data collection and live streaming."),
        [ShortcutCategory.Backfill] = ("Backfill", "", "Manage historical data backfill operations."),
        [ShortcutCategory.Symbols] = ("Symbol Management", "", "Add, remove, and manage tracked symbols."),
        [ShortcutCategory.View] = ("View Controls", "", "Adjust the interface, search content, and access tools."),
        [ShortcutCategory.General] = ("General", "", "Application-wide actions and common operations.")
    };

    private readonly KeyboardShortcutService _service;
    private readonly ILoggingService? _logger;
    private bool _globalShortcutsWereEnabled;

    public KeyboardShortcutsViewModel(KeyboardShortcutService service, ILoggingService? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
        Refresh();
    }

    /// <summary>Shortcut groups in display order.</summary>
    public ObservableCollection<ShortcutCategoryGroupViewModel> Categories { get; } = new();

    [ObservableProperty]
    private string _overrideStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAllCommand))]
    private bool _hasOverrides;

    /// <summary>The row currently waiting for a key chord, or null.</summary>
    public ShortcutBindingRowViewModel? CapturingRow { get; private set; }

    /// <summary>True while a row is waiting for a key chord.</summary>
    public bool IsCapturing => CapturingRow is not null;

    /// <summary>
    /// Rebuilds the groups and rows from the service's current shortcuts.
    /// </summary>
    public void Refresh()
    {
        CancelCapture();
        Categories.Clear();

        var grouped = _service.GetShortcutsByCategory();
        foreach (var category in CategoryOrder.Concat(grouped.Keys.Except(CategoryOrder)))
        {
            if (!grouped.TryGetValue(category, out var shortcuts) || shortcuts.Count == 0)
            {
                continue;
            }

            var (label, icon, description) = CategoryInfo.TryGetValue(category, out var info)
                ? info
                : (category.ToString(), "", string.Empty);

            var rows = shortcuts.Select(shortcut => new ShortcutBindingRowViewModel(this, shortcut.ActionId));
            Categories.Add(new ShortcutCategoryGroupViewModel(category, label, icon, description, rows));
        }

        foreach (var row in AllRows())
        {
            row.SyncFromService();
        }

        UpdateOverrideStatus();
    }

    /// <summary>Gets the row for an action id, or null.</summary>
    public ShortcutBindingRowViewModel? FindRow(string actionId)
        => AllRows().FirstOrDefault(row => string.Equals(row.ActionId, actionId, StringComparison.Ordinal));

    [RelayCommand(CanExecute = nameof(HasOverrides))]
    private void ResetAll()
    {
        CancelCapture();
        _service.ResetToDefaults();
        _logger?.LogInfo("Keyboard shortcuts reset to defaults.");
        foreach (var row in AllRows())
        {
            row.ValidationMessage = string.Empty;
            row.SyncFromService();
        }

        UpdateOverrideStatus();
    }

    /// <summary>
    /// Starts waiting for a key chord for <paramref name="row"/>. Global shortcut handling is
    /// suspended until the capture ends, so the captured chord is not also run as a command.
    /// </summary>
    internal void BeginCapture(ShortcutBindingRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (ReferenceEquals(CapturingRow, row))
        {
            return;
        }

        CancelCapture();
        _globalShortcutsWereEnabled = _service.IsEnabled;
        _service.IsEnabled = false;
        CapturingRow = row;
        row.ValidationMessage = string.Empty;
        row.IsCapturing = true;
        OnPropertyChanged(nameof(CapturingRow));
        OnPropertyChanged(nameof(IsCapturing));
    }

    /// <summary>
    /// Ends any chord capture without changing the binding and resumes global shortcut handling.
    /// </summary>
    public void CancelCapture()
    {
        var row = CapturingRow;
        if (row is null)
        {
            return;
        }

        CapturingRow = null;
        row.IsCapturing = false;
        _service.IsEnabled = _globalShortcutsWereEnabled;
        OnPropertyChanged(nameof(CapturingRow));
        OnPropertyChanged(nameof(IsCapturing));
    }

    /// <summary>
    /// Feeds a key press to the active chord capture. Bare modifier presses are ignored and
    /// keep the capture waiting. Any other key ends the capture: the chord is applied when it
    /// is allowed for the action and free, and otherwise rejected with an inline reason.
    /// </summary>
    /// <param name="key">The pressed key, already resolved from <c>SystemKey</c> for Alt chords.</param>
    /// <param name="modifiers">The modifiers held with it.</param>
    /// <returns>True when the key press was consumed by the capture.</returns>
    public bool HandleCapturedKey(Key key, ModifierKeys modifiers)
    {
        var row = CapturingRow;
        if (row is null)
        {
            return false;
        }

        if (IsModifierOrPlaceholderKey(key))
        {
            return true;
        }

        CancelCapture();
        var current = _service.GetShortcut(row.ActionId);
        if (current is null)
        {
            row.ValidationMessage = "This shortcut is no longer registered.";
            return true;
        }

        var chord = KeyboardShortcutService.FormatShortcut(key, modifiers);
        if (current.Key == key && current.Modifiers == modifiers)
        {
            row.ValidationMessage = string.Empty;
            return true;
        }

        if (!KeyboardShortcutService.IsBindingAllowed(row.ActionId, key, modifiers))
        {
            row.ValidationMessage =
                $"{chord} can't be used: {row.Description} must keep Ctrl or Alt so a stray keystroke can't trigger it.";
            return true;
        }

        if (_service.HasConflict(key, modifiers, excludeActionId: row.ActionId))
        {
            var holder = _service.FindConflictingShortcut(key, modifiers, excludeActionId: row.ActionId);
            row.ValidationMessage = holder is null
                ? $"{chord} is already in use."
                : $"{chord} is already used by {holder.Description}.";
            return true;
        }

        _service.UpdateShortcut(row.ActionId, key, modifiers);
        _logger?.LogInfo(
            "Keyboard shortcut rebound.",
            ("actionId", row.ActionId),
            ("chord", chord));
        row.ValidationMessage = string.Empty;
        row.SyncFromService();
        UpdateOverrideStatus();
        return true;
    }

    internal void SetShortcutEnabled(ShortcutBindingRowViewModel row, bool enabled)
    {
        _service.SetShortcutEnabled(row.ActionId, enabled);
        _logger?.LogInfo(
            "Keyboard shortcut enabled state changed.",
            ("actionId", row.ActionId),
            ("enabled", enabled ? "true" : "false"));
        row.SyncFromService();
        UpdateOverrideStatus();
    }

    internal void ResetShortcut(ShortcutBindingRowViewModel row)
    {
        if (ReferenceEquals(CapturingRow, row))
        {
            CancelCapture();
        }

        if (_service.TryResetShortcut(row.ActionId, out var conflictingActionId))
        {
            _logger?.LogInfo("Keyboard shortcut reset to default.", ("actionId", row.ActionId));
            row.ValidationMessage = string.Empty;
        }
        else if (conflictingActionId is not null)
        {
            var defaults = _service.GetDefaultBinding(row.ActionId);
            var chord = defaults is { } d ? KeyboardShortcutService.FormatShortcut(d.Key, d.Modifiers) : "The default chord";
            var holder = _service.GetShortcut(conflictingActionId)?.Description ?? conflictingActionId;
            row.ValidationMessage = $"Can't restore {chord}: {holder} now uses it. Rebind {holder} first.";
        }

        row.SyncFromService();
        UpdateOverrideStatus();
    }

    internal ShortcutAction? GetShortcut(string actionId) => _service.GetShortcut(actionId);

    internal bool IsCustomized(string actionId) => _service.GetOverrides().ContainsKey(actionId);

    /// <summary>
    /// The key a chord capture should see: Alt chords arrive as <see cref="Key.System"/> with the
    /// real key in <paramref name="systemKey"/>, and IME-processed keys carry it in
    /// <paramref name="imeProcessedKey"/>.
    /// </summary>
    public static Key ResolveCapturedKey(Key key, Key systemKey, Key imeProcessedKey = Key.None) => key switch
    {
        Key.System => systemKey,
        Key.ImeProcessed => imeProcessedKey,
        _ => key
    };

    /// <summary>
    /// True for keys that cannot finish a chord on their own: Ctrl, Shift, Alt, and Windows
    /// presses, plus placeholder keys that carry no real key.
    /// </summary>
    public static bool IsModifierOrPlaceholderKey(Key key) => key is
        Key.None or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin or
        Key.System or Key.ImeProcessed or Key.DeadCharProcessed;

    private IEnumerable<ShortcutBindingRowViewModel> AllRows() => Categories.SelectMany(group => group.Rows);

    private void UpdateOverrideStatus()
    {
        var overrideCount = _service.GetOverrides().Count;
        OverrideStatusText = overrideCount switch
        {
            0 => "All shortcuts use their default bindings.",
            1 => "1 shortcut differs from its default and is saved for this machine.",
            _ => $"{overrideCount} shortcuts differ from their defaults and are saved for this machine."
        };
        HasOverrides = overrideCount > 0;
    }
}

/// <summary>
/// One shortcut category card on the Keyboard Shortcuts page.
/// </summary>
public sealed class ShortcutCategoryGroupViewModel
{
    public ShortcutCategoryGroupViewModel(
        ShortcutCategory category,
        string label,
        string icon,
        string description,
        IEnumerable<ShortcutBindingRowViewModel> rows)
    {
        Category = category;
        Label = label;
        Icon = icon;
        Description = description;
        Rows = new ObservableCollection<ShortcutBindingRowViewModel>(rows);
    }

    public ShortcutCategory Category { get; }

    public string Label { get; }

    public string Icon { get; }

    public string Description { get; }

    public bool HasDescription => !string.IsNullOrEmpty(Description);

    public ObservableCollection<ShortcutBindingRowViewModel> Rows { get; }
}

/// <summary>
/// One editable shortcut row: its chord, enabled state, capture state, and inline validation.
/// </summary>
public sealed partial class ShortcutBindingRowViewModel : BindableBase
{
    private readonly KeyboardShortcutsViewModel _owner;
    private bool _syncing;

    internal ShortcutBindingRowViewModel(KeyboardShortcutsViewModel owner, string actionId)
    {
        _owner = owner;
        ActionId = actionId;
    }

    public string ActionId { get; }

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _formattedShortcut = string.Empty;

    [ObservableProperty]
    private bool _isShortcutEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private bool _isCustomized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChordDisplayText))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    /// <summary>True when an inline rejection reason is showing.</summary>
    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>The chord badge text, or the capture prompt while waiting for a chord.</summary>
    public string ChordDisplayText => IsCapturing ? "Press keys…" : FormattedShortcut;

    partial void OnFormattedShortcutChanged(string value) => OnPropertyChanged(nameof(ChordDisplayText));

    partial void OnIsShortcutEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            _owner.SetShortcutEnabled(this, value);
        }
    }

    [RelayCommand]
    private void BeginCapture() => _owner.BeginCapture(this);

    [RelayCommand]
    private void CancelCapture()
    {
        if (IsCapturing)
        {
            _owner.CancelCapture();
        }
    }

    [RelayCommand(CanExecute = nameof(IsCustomized))]
    private void Reset() => _owner.ResetShortcut(this);

    /// <summary>Reloads the row's displayed state from the shortcut service.</summary>
    internal void SyncFromService()
    {
        var shortcut = _owner.GetShortcut(ActionId);
        if (shortcut is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            Description = shortcut.Description;
            FormattedShortcut = shortcut.FormattedShortcut;
            IsShortcutEnabled = shortcut.IsEnabled;
            IsCustomized = _owner.IsCustomized(ActionId);
        }
        finally
        {
            _syncing = false;
        }
    }
}
