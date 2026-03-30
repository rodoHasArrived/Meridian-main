using System.Collections.Generic;
using System.Windows;

namespace Meridian.Wpf.Views;

/// <summary>
/// Dialog for saving or updating a watchlist.
/// Replaces the former imperative <c>SaveWatchlistDialog</c> Window class
/// that was embedded in <c>SymbolsPage.xaml.cs</c>.
/// </summary>
public partial class SaveWatchlistDialog : Window
{
    /// <summary>Gets the watchlist name typed by the user.</summary>
    public string WatchlistName => NameBox.Text;

    /// <summary>Gets whether the user chose to create a new watchlist.</summary>
    public bool SaveAsNew => SaveAsNewCheck.IsChecked ?? true;

    /// <summary>Gets the selected existing watchlist name (null when SaveAsNew is true).</summary>
    public string? SelectedWatchlistId => ExistingCombo.SelectedItem as string;

    public SaveWatchlistDialog(IEnumerable<string> existingWatchlists)
    {
        InitializeComponent();

        foreach (var name in existingWatchlists)
            ExistingCombo.Items.Add(name);

        UpdateUIState();
    }

    private void SaveAsNewCheck_Changed(object sender, RoutedEventArgs e) => UpdateUIState();

    private void UpdateUIState()
    {
        var isNew = SaveAsNewCheck.IsChecked ?? true;
        NameBox.IsEnabled = isNew;
        ExistingCombo.IsEnabled = !isNew && ExistingCombo.Items.Count > 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SaveAsNew && string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Please enter a watchlist name.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SaveAsNew && ExistingCombo.SelectedItem is null)
        {
            MessageBox.Show("Please select an existing watchlist.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
