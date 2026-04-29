using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for creating and managing context menus (right-click menus) throughout the WPF application.
/// Provides reusable menu patterns with keyboard gestures for common actions.
/// </summary>
public sealed class ContextMenuService
{
    private static readonly Lazy<ContextMenuService> _instance = new(() => new ContextMenuService());
    public static ContextMenuService Instance => _instance.Value;

    private ContextMenuService() { }


    /// <summary>
    /// Creates a menu item with an optional keyboard gesture.
    /// </summary>
    public MenuItem CreateMenuItem(
        string text,
        string iconGlyph,
        RoutedEventHandler clickHandler,
        Key? acceleratorKey = null,
        ModifierKeys modifiers = ModifierKeys.None,
        object? tag = null)
    {
        var item = new MenuItem
        {
            Header = text,
            Tag = tag
        };

        item.Click += clickHandler;

        if (acceleratorKey.HasValue)
        {
            item.InputGestureText = FormatGesture(acceleratorKey.Value, modifiers);
        }

        return item;
    }

    /// <summary>
    /// Creates a submenu with child items.
    /// </summary>
    public MenuItem CreateSubMenu(string text, string iconGlyph, params object[] items)
    {
        var subItem = new MenuItem
        {
            Header = text
        };

        foreach (var item in items)
        {
            subItem.Items.Add(item);
        }

        return subItem;
    }

    /// <summary>
    /// Creates a separator line.
    /// </summary>
    public Separator CreateSeparator() => new();



    /// <summary>
    /// Creates a context menu for symbol items (watchlist, symbols page, etc.)
    /// </summary>
    public ContextMenu CreateSymbolContextMenu(
        string symbol,
        bool isFavorite,
        Func<string, Task> onToggleFavorite,
        Func<string, Task> onViewDetails,
        Func<string, Task> onViewLiveData,
        Func<string, Task> onRunBackfill,
        Func<string, Task> onCopySymbol,
        Func<string, Task> onRemove,
        Func<string, Task>? onEdit = null,
        Func<string, Task>? onAddNote = null)
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateMenuItem(
            "View Details", "\uE8A5",
            async (s, e) => await onViewDetails(symbol),
            Key.Enter, tag: symbol));

        menu.Items.Add(CreateMenuItem(
            "View Live Data", "\uE9D9",
            async (s, e) => await onViewLiveData(symbol),
            Key.L, ModifierKeys.Control, tag: symbol));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem(
            isFavorite ? "Remove from Favorites" : "Add to Favorites",
            isFavorite ? "\uE735" : "\uE734",
            async (s, e) => await onToggleFavorite(symbol),
            Key.F, ModifierKeys.Control, tag: symbol));

        if (onEdit != null)
        {
            menu.Items.Add(CreateMenuItem(
                "Edit Symbol", "\uE70F",
                async (s, e) => await onEdit(symbol),
                Key.E, ModifierKeys.Control, tag: symbol));
        }

        if (onAddNote != null)
        {
            menu.Items.Add(CreateMenuItem(
                "Add Note", "\uE70B",
                async (s, e) => await onAddNote(symbol), tag: symbol));
        }

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem(
            "Run Backfill", "\uE896",
            async (s, e) => await onRunBackfill(symbol),
            Key.B, ModifierKeys.Control, tag: symbol));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem(
            "Copy Symbol", "\uE8C8",
            async (s, e) => await onCopySymbol(symbol),
            Key.C, ModifierKeys.Control, tag: symbol));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem(
            "Remove", "\uE74D",
            async (s, e) => await onRemove(symbol),
            Key.Delete, tag: symbol));

        return menu;
    }



    /// <summary>
    /// Creates a context menu for subscription items on the symbols page.
    /// </summary>
    public ContextMenu CreateSubscriptionContextMenu(
        string symbol,
        bool tradesEnabled,
        bool depthEnabled,
        Func<string, Task> onEdit,
        Func<string, bool, Task> onToggleTrades,
        Func<string, bool, Task> onToggleDepth,
        Func<string, Task> onViewLiveData,
        Func<string, Task> onRunBackfill,
        Func<string, Task> onCopySymbol,
        Func<string, Task> onDelete)
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateMenuItem(
            "Edit Subscription", "\uE70F",
            async (s, e) => await onEdit(symbol),
            Key.E, ModifierKeys.Control, tag: symbol));

        menu.Items.Add(CreateSeparator());

        var subscriptionMenu = CreateSubMenu("Subscription", "\uE9D9",
            CreateMenuItem(
                tradesEnabled ? "Disable Trades" : "Enable Trades",
                tradesEnabled ? "\uE73B" : "\uE73A",
                async (s, e) => await onToggleTrades(symbol, !tradesEnabled), tag: symbol),
            CreateMenuItem(
                depthEnabled ? "Disable Depth" : "Enable Depth",
                depthEnabled ? "\uE73B" : "\uE73A",
                async (s, e) => await onToggleDepth(symbol, !depthEnabled), tag: symbol));

        menu.Items.Add(subscriptionMenu);
        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem(
            "View Live Data", "\uE9D9",
            async (s, e) => await onViewLiveData(symbol),
            Key.L, ModifierKeys.Control, tag: symbol));

        menu.Items.Add(CreateMenuItem(
            "Run Backfill", "\uE896",
            async (s, e) => await onRunBackfill(symbol),
            Key.B, ModifierKeys.Control, tag: symbol));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem(
            "Copy Symbol", "\uE8C8",
            async (s, e) => await onCopySymbol(symbol),
            Key.C, ModifierKeys.Control, tag: symbol));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem(
            "Delete", "\uE74D",
            async (s, e) => await onDelete(symbol),
            Key.Delete, tag: symbol));

        return menu;
    }



    /// <summary>
    /// Creates a context menu for schedule items (backfill/maintenance).
    /// </summary>
    public ContextMenu CreateScheduleContextMenu(
        string scheduleId,
        string scheduleName,
        bool isEnabled,
        Func<string, Task> onRunNow,
        Func<string, Task> onEdit,
        Func<string, bool, Task> onToggleEnabled,
        Func<string, Task> onViewHistory,
        Func<string, Task> onClone,
        Func<string, Task> onDelete)
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateMenuItem("Run Now", "\uE768",
            async (s, e) => await onRunNow(scheduleId),
            Key.R, ModifierKeys.Control, tag: scheduleId));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem("Edit Schedule", "\uE70F",
            async (s, e) => await onEdit(scheduleId),
            Key.E, ModifierKeys.Control, tag: scheduleId));

        menu.Items.Add(CreateMenuItem(
            isEnabled ? "Disable Schedule" : "Enable Schedule",
            isEnabled ? "\uE8FB" : "\uE768",
            async (s, e) => await onToggleEnabled(scheduleId, !isEnabled), tag: scheduleId));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem("View History", "\uE81C",
            async (s, e) => await onViewHistory(scheduleId),
            Key.H, ModifierKeys.Control, tag: scheduleId));

        menu.Items.Add(CreateMenuItem("Clone Schedule", "\uE8C8",
            async (s, e) => await onClone(scheduleId), tag: scheduleId));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem("Delete", "\uE74D",
            async (s, e) => await onDelete(scheduleId),
            Key.Delete, tag: scheduleId));

        return menu;
    }



    /// <summary>
    /// Creates a generic context menu for list items with common actions.
    /// </summary>
    public ContextMenu CreateGenericListItemMenu(
        object item,
        Func<object, Task>? onView = null,
        Func<object, Task>? onEdit = null,
        Func<object, Task>? onCopy = null,
        Func<object, Task>? onDelete = null,
        Func<object, Task>? onRefresh = null,
        IEnumerable<(string text, string icon, Func<object, Task> action)>? customActions = null)
    {
        var menu = new ContextMenu();

        if (onView != null)
            menu.Items.Add(CreateMenuItem("View Details", "\uE8A5",
                async (s, e) => await onView(item), Key.Enter, tag: item));

        if (onEdit != null)
            menu.Items.Add(CreateMenuItem("Edit", "\uE70F",
                async (s, e) => await onEdit(item), Key.E, ModifierKeys.Control, tag: item));

        if (customActions != null && customActions.Any())
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(CreateSeparator());
            foreach (var (text, icon, action) in customActions)
                menu.Items.Add(CreateMenuItem(text, icon,
                    async (s, e) => await action(item), tag: item));
        }

        if (onRefresh != null)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateMenuItem("Refresh", "\uE72C",
                async (s, e) => await onRefresh(item), Key.F5, tag: item));
        }

        if (onCopy != null)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateMenuItem("Copy", "\uE8C8",
                async (s, e) => await onCopy(item), Key.C, ModifierKeys.Control, tag: item));
        }

        if (onDelete != null)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateMenuItem("Delete", "\uE74D",
                async (s, e) => await onDelete(item), Key.Delete, tag: item));
        }

        return menu;
    }



    /// <summary>
    /// Creates a context menu for data/archive file items.
    /// </summary>
    public ContextMenu CreateDataFileContextMenu(
        string filePath,
        Func<string, Task> onView,
        Func<string, Task> onExport,
        Func<string, Task> onCompress,
        Func<string, Task> onVerifyIntegrity,
        Func<string, Task> onDelete,
        Func<string, Task>? onReplay = null)
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateMenuItem("View Contents", "\uE8A5",
            async (s, e) => await onView(filePath), Key.Enter, tag: filePath));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem("Export", "\uEDE1",
            async (s, e) => await onExport(filePath),
            Key.S, ModifierKeys.Control | ModifierKeys.Shift, tag: filePath));

        if (onReplay != null)
            menu.Items.Add(CreateMenuItem("Replay Events", "\uE768",
                async (s, e) => await onReplay(filePath), tag: filePath));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem("Compress", "\uE7B8",
            async (s, e) => await onCompress(filePath), tag: filePath));

        menu.Items.Add(CreateMenuItem("Verify Integrity", "\uE9D5",
            async (s, e) => await onVerifyIntegrity(filePath), tag: filePath));

        menu.Items.Add(CreateSeparator());

        menu.Items.Add(CreateMenuItem("Delete", "\uE74D",
            async (s, e) => await onDelete(filePath), Key.Delete, tag: filePath));

        return menu;
    }



    /// <summary>
    /// Creates a context menu for bulk operations on selected items.
    /// </summary>
    public ContextMenu CreateBulkActionsMenu(
        int selectedCount,
        Func<Task>? onSelectAll = null,
        Func<Task>? onDeselectAll = null,
        Func<Task>? onEnableSelected = null,
        Func<Task>? onDisableSelected = null,
        Func<Task>? onDeleteSelected = null,
        Func<Task>? onExportSelected = null)
    {
        var menu = new ContextMenu();

        if (onSelectAll != null)
            menu.Items.Add(CreateMenuItem("Select All", "\uE8B3",
                async (s, e) => await onSelectAll(), Key.A, ModifierKeys.Control));

        if (onDeselectAll != null)
            menu.Items.Add(CreateMenuItem("Deselect All", "\uE8E6",
                async (s, e) => await onDeselectAll(), Key.D, ModifierKeys.Control | ModifierKeys.Shift));

        if ((onSelectAll != null || onDeselectAll != null) &&
            (onEnableSelected != null || onDisableSelected != null || onDeleteSelected != null || onExportSelected != null))
            menu.Items.Add(CreateSeparator());

        if (onEnableSelected != null)
            menu.Items.Add(CreateMenuItem($"Enable Selected ({selectedCount})", "\uE73A",
                async (s, e) => await onEnableSelected()));

        if (onDisableSelected != null)
            menu.Items.Add(CreateMenuItem($"Disable Selected ({selectedCount})", "\uE739",
                async (s, e) => await onDisableSelected()));

        if (onExportSelected != null)
            menu.Items.Add(CreateMenuItem($"Export Selected ({selectedCount})", "\uEDE1",
                async (s, e) => await onExportSelected()));

        if (onDeleteSelected != null)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(CreateSeparator());
            menu.Items.Add(CreateMenuItem($"Delete Selected ({selectedCount})", "\uE74D",
                async (s, e) => await onDeleteSelected(), Key.Delete, ModifierKeys.Shift));
        }

        return menu;
    }



    /// <summary>
    /// Copies text to the clipboard.
    /// </summary>
    public void CopyToClipboard(string text)
    {
        Clipboard.SetText(text);
    }

    /// <summary>
    /// Shows a context menu at the mouse position relative to an element.
    /// </summary>
    public void ShowAtPointer(ContextMenu menu, UIElement element, MouseButtonEventArgs e)
    {
        menu.PlacementTarget = element;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Shows a context menu relative to a specific element.
    /// </summary>
    public void ShowAt(ContextMenu menu, FrameworkElement element)
    {
        menu.PlacementTarget = element;
        menu.IsOpen = true;
    }

    private static string FormatGesture(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

}
