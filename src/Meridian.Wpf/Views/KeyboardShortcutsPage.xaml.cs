using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

    public KeyboardShortcutsPage()
    {
        InitializeComponent();
        _keyboardShortcutService = KeyboardShortcutService.Instance;
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        PopulateDynamicShortcuts();
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

        for (var i = 0; i < shortcuts.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var shortcut = shortcuts[i];

            // Key badge
            var keyBorder = new Border
            {
                Style = (Style)FindResource("KeyboardShortcutStyle")
            };
            keyBorder.Child = new TextBlock
            {
                Text = shortcut.FormattedShortcut,
                Style = (Style)FindResource("ShortcutKeyTextStyle")
            };

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
        }

        cardPanel.Children.Add(grid);

        return new Border
        {
            Style = (Style)FindResource("CardStyle"),
            Child = cardPanel
        };
    }
}
