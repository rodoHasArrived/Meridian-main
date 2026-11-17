using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Keyboard shortcuts reference page that dynamically loads shortcuts
/// from the KeyboardShortcutService, ensuring the display always reflects
/// the current registered shortcuts.
/// </summary>
public partial class KeyboardShortcutsPage : Page
{
    private readonly KeyboardShortcutService _keyboardShortcutService;
    private string? _capturingActionId;
    private bool _globalShortcutsWereEnabled = true;

    public KeyboardShortcutsPage()
    {
        InitializeComponent();
        _keyboardShortcutService = KeyboardShortcutService.Instance;
        Focusable = true;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        PreviewKeyDown += OnPagePreviewKeyDown;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        PopulateDynamicShortcuts();
        UpdateOverrideStatus();
    }

    private void OnResetShortcutsClick(object sender, RoutedEventArgs e)
    {
        EndCapture(status: null);
        _keyboardShortcutService.ResetToDefaults();
        ShowEditStatus("All shortcuts are back to their defaults.");
        Refresh();
    }

    private void UpdateOverrideStatus()
    {
        var overrideCount = _keyboardShortcutService.GetOverrides().Count;
        ShortcutOverrideStatusText.Text = overrideCount switch
        {
            0 => "All shortcuts use their default bindings.",
            1 => "1 shortcut differs from its default and is saved for this machine.",
            _ => $"{overrideCount} shortcuts differ from their defaults and are saved for this machine."
        };
        ResetShortcutsButton.IsEnabled = overrideCount > 0;
    }

    /// <summary>
    /// Populates the dynamic shortcuts panel by reading all registered
    /// shortcuts from KeyboardShortcutService, grouped by category.
    /// </summary>
    private void PopulateDynamicShortcuts()
    {
        DynamicShortcutsPanel.Children.Clear();

        var grouped = _keyboardShortcutService.GetShortcutsByCategory();

        // Define display order and metadata for categories
        var categoryInfo = new Dictionary<ShortcutCategory, (string Label, string Icon, string Description)>
        {
            [ShortcutCategory.Navigation] = ("Navigation", "\uE80F", "Move between pages and sections of the application."),
            [ShortcutCategory.Collector] = ("Data Collection", "\uE774", "Control data collection and live streaming."),
            [ShortcutCategory.Backfill] = ("Backfill", "\uE896", "Manage historical data backfill operations."),
            [ShortcutCategory.Symbols] = ("Symbol Management", "\uE71D", "Add, remove, and manage tracked symbols."),
            [ShortcutCategory.View] = ("View Controls", "\uE7B3", "Adjust the interface, search content, and access tools."),
            [ShortcutCategory.General] = ("General", "\uE713", "Application-wide actions and common operations.")
        };

        var orderedCategories = new[]
        {
            ShortcutCategory.Navigation,
            ShortcutCategory.Collector,
            ShortcutCategory.Backfill,
            ShortcutCategory.Symbols,
            ShortcutCategory.View,
            ShortcutCategory.General
        };

        // Create a two-column grid
        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var col = 0;
        var row = 0;

        foreach (var category in orderedCategories)
        {
            if (!grouped.TryGetValue(category, out var shortcuts) || shortcuts.Count == 0)
                continue;

            var (label, icon, description) = categoryInfo.TryGetValue(category, out var info)
                ? info
                : (category.ToString(), "\uE713", string.Empty);

            var card = BuildCategoryCard(label, icon, description, shortcuts);

            // Determine grid position (two columns layout)
            var gridCol = col % 2 == 0 ? 0 : 2;
            var gridRow = col / 2;

            // Ensure enough rows exist
            while (outerGrid.RowDefinitions.Count <= gridRow * 2)
            {
                outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            }

            Grid.SetColumn(card, gridCol);
            Grid.SetRow(card, gridRow * 2);
            outerGrid.Children.Add(card);

            col++;
            row = gridRow;
        }

        DynamicShortcutsPanel.Children.Add(outerGrid);
    }

    private Border BuildCategoryCard(string label, string icon, string description, List<ShortcutAction> shortcuts)
    {
        var cardPanel = new StackPanel();

        // Header with icon
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = (FontFamily)FindResource("SymbolThemeFontFamily"),
            FontSize = 18,
            Foreground = (Brush)FindResource("InfoColorBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)FindResource("CardHeaderStyle"),
            Margin = new Thickness(8, 0, 0, 0)
        });
        cardPanel.Children.Add(headerPanel);

        // Description
        if (!string.IsNullOrEmpty(description))
        {
            cardPanel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = (Brush)FindResource("ConsoleTextMutedBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 16),
                TextWrapping = TextWrapping.Wrap
            });
        }

        // Shortcuts grid
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < shortcuts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var shortcut = shortcuts[i];

            var isCapturing = shortcut.ActionId == _capturingActionId;

            // Key badge
            var keyBorder = new Border
            {
                Style = (Style)FindResource("KeyboardShortcutStyle")
            };
            keyBorder.Child = new TextBlock
            {
                Text = isCapturing ? "Press keys…" : shortcut.FormattedShortcut,
                Style = (Style)FindResource("ShortcutKeyTextStyle")
            };
            if (!isCapturing && _keyboardShortcutService.IsOverridden(shortcut.ActionId))
            {
                keyBorder.ToolTip = $"Default: {_keyboardShortcutService.GetDefaultShortcutText(shortcut.ActionId)}";
            }

            Grid.SetRow(keyBorder, i);
            Grid.SetColumn(keyBorder, 0);
            grid.Children.Add(keyBorder);

            // Description
            var descText = new TextBlock
            {
                Text = shortcut.Description,
                Style = (Style)FindResource("ShortcutDescriptionStyle")
            };

            // Gray out disabled shortcuts
            if (!shortcut.IsEnabled)
            {
                descText.Foreground = (Brush)FindResource("ConsoleTextMutedBrush");
                descText.FontStyle = FontStyles.Italic;
            }

            Grid.SetRow(descText, i);
            Grid.SetColumn(descText, 1);
            grid.Children.Add(descText);

            var actions = BuildShortcutActions(shortcut, isCapturing);
            Grid.SetRow(actions, i);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
        }

        cardPanel.Children.Add(grid);

        return new Border
        {
            Style = (Style)FindResource("CardStyle"),
            Child = cardPanel
        };
    }

    private StackPanel BuildShortcutActions(ShortcutAction shortcut, bool isCapturing)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        if (isCapturing)
        {
            panel.Children.Add(CreateRowButton(
                "Cancel", $"Cancel changing the shortcut for {shortcut.Description}",
                $"KeyboardShortcutCancel_{shortcut.ActionId}", () => EndCapture("Change cancelled.")));
            return panel;
        }

        panel.Children.Add(CreateRowButton(
            "Change", $"Change the shortcut for {shortcut.Description}",
            $"KeyboardShortcutChange_{shortcut.ActionId}", () => BeginCapture(shortcut)));
        panel.Children.Add(CreateRowButton(
            shortcut.IsEnabled ? "Disable" : "Enable",
            $"{(shortcut.IsEnabled ? "Disable" : "Enable")} the shortcut for {shortcut.Description}",
            $"KeyboardShortcutToggle_{shortcut.ActionId}",
            () =>
            {
                _keyboardShortcutService.SetShortcutEnabled(shortcut.ActionId, !shortcut.IsEnabled);
                ShowEditStatus($"{shortcut.Description} is now {(shortcut.IsEnabled ? "enabled" : "disabled")}.");
                Refresh();
            }));

        if (_keyboardShortcutService.IsOverridden(shortcut.ActionId))
        {
            panel.Children.Add(CreateRowButton(
                "Reset", $"Reset the shortcut for {shortcut.Description} to {_keyboardShortcutService.GetDefaultShortcutText(shortcut.ActionId)}",
                $"KeyboardShortcutReset_{shortcut.ActionId}",
                () =>
                {
                    _keyboardShortcutService.ResetShortcut(shortcut.ActionId);
                    ShowEditStatus($"{shortcut.Description} is back to {shortcut.FormattedShortcut}.");
                    Refresh();
                }));
        }

        return panel;
    }

    private Button CreateRowButton(string content, string accessibleName, string automationId, System.Action onClick)
    {
        var button = new Button
        {
            Content = content,
            Style = (Style)FindResource("GhostButtonStyle"),
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = _capturingActionId is null || content == "Cancel"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, accessibleName);
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void BeginCapture(ShortcutAction shortcut)
    {
        _capturingActionId = shortcut.ActionId;

        // The global handler tunnels through the window before this page sees the key, so pause
        // it while capturing; otherwise pressing an existing chord would run that command.
        _globalShortcutsWereEnabled = _keyboardShortcutService.IsEnabled;
        _keyboardShortcutService.IsEnabled = false;

        ShowEditStatus($"Press the new shortcut for {shortcut.Description}, or Esc to cancel.");
        Refresh();
        Focus();
    }

    private void EndCapture(string? status)
    {
        if (_capturingActionId is null)
        {
            return;
        }

        _capturingActionId = null;
        _keyboardShortcutService.IsEnabled = _globalShortcutsWereEnabled;
        if (status is not null)
        {
            ShowEditStatus(status);
        }

        Refresh();
    }

    private void OnPagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingActionId is not { } actionId)
        {
            return;
        }

        // While capturing, every key belongs to the editor: nothing reaches page controls.
        e.Handled = true;
        var modifiers = e.KeyboardDevice.Modifiers;
        if (e.Key == Key.Escape && modifiers == ModifierKeys.None)
        {
            EndCapture("Change cancelled.");
            return;
        }

        if (!KeyboardShortcutService.TryGetChord(e.Key, e.SystemKey, modifiers, out var chordKey))
        {
            return; // A modifier on its own: keep waiting for the rest of the chord.
        }

        var description = _keyboardShortcutService.GetShortcut(actionId)?.Description ?? actionId;
        var refusal = _keyboardShortcutService.TryUpdateShortcut(actionId, chordKey, modifiers);
        EndCapture(refusal ?? $"{description} is now {KeyboardShortcutService.FormatShortcut(chordKey, modifiers)}.");
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => EndCapture(status: null);

    private void ShowEditStatus(string message)
    {
        ShortcutEditStatusText.Text = message;
        ShortcutEditStatusText.Visibility = Visibility.Visible;
    }

    private void Refresh()
    {
        PopulateDynamicShortcuts();
        UpdateOverrideStatus();
    }
}
