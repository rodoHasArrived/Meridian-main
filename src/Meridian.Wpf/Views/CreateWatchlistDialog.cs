using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Meridian.Wpf.Views;

/// <summary>
/// Dialog for creating a new watchlist.
/// </summary>
public sealed class CreateWatchlistDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _symbolsBox;
    private readonly ComboBox _colorCombo;

    public string WatchlistName => _nameBox.Text;
    public string InitialSymbols => _symbolsBox.Text;
    public string? SelectedColor => (_colorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static readonly (string Name, string Hex)[] Colors =
    [
        ("Green",  "#4CAF50"),
        ("Blue",   "#2196F3"),
        ("Purple", "#9C27B0"),
        ("Orange", "#FF9800"),
        ("Red",    "#F44336"),
        ("Teal",   "#009688"),
        ("Pink",   "#E91E63"),
        ("Gray",   "#607D8B"),
    ];

    public CreateWatchlistDialog()
    {
        Title = "Create Watchlist";
        Width = 450;
        Height = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(grid, "Watchlist Name:", 0);
        _nameBox = CreateTextBox();
        Grid.SetRow(_nameBox, 1);
        grid.Children.Add(_nameBox);

        AddLabel(grid, "Color:", 2);
        _colorCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 16),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
            Foreground = Brushes.White,
        };
        foreach (var (name, hex) in Colors)
        {
            _colorCombo.Items.Add(new ComboBoxItem
            {
                Content = name,
                Tag = hex,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            });
        }
        _colorCombo.SelectedIndex = 0;
        Grid.SetRow(_colorCombo, 3);
        grid.Children.Add(_colorCombo);

        AddLabel(grid, "Initial Symbols (comma-separated):", 4);
        _symbolsBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 16),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A4E")),
            Padding = new Thickness(8),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(_symbolsBox, 5);
        grid.Children.Add(_symbolsBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(buttonPanel, 6);

        var cancelButton = CreateButton("Cancel", "#3A3A4E");
        cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        buttonPanel.Children.Add(cancelButton);

        var createButton = CreateButton("Create", "#4CAF50");
        createButton.Margin = new Thickness(8, 0, 0, 0);
        createButton.Click += OnCreateClick;
        buttonPanel.Children.Add(createButton);

        grid.Children.Add(buttonPanel);
        Content = grid;
    }

    private void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock { Text = text, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private static TextBox CreateTextBox() => new()
    {
        Margin = new Thickness(0, 4, 0, 16),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E")),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A4E")),
        Padding = new Thickness(8, 6, 8, 6),
    };

    private static Button CreateButton(string content, string bgColor) => new()
    {
        Content = content,
        Width = 100,
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 8, 12, 8),
    };

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Please enter a watchlist name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
