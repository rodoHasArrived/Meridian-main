using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Command palette window providing quick navigation and action execution via Ctrl+K.
/// Supports fuzzy search across all registered pages and actions.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteService _paletteService;

    /// <summary>
    /// Gets the selected command's action ID after the dialog closes.
    /// </summary>
    public string? SelectedActionId { get; private set; }

    /// <summary>
    /// Gets the selected command's category.
    /// </summary>
    public PaletteCommandCategory SelectedCategory { get; private set; }

    public CommandPaletteWindow(CommandPaletteService paletteService)
    {
        _paletteService = paletteService;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        UpdateResults(string.Empty);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        PlaceholderText.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateResults(query);
    }

    private void UpdateResults(string query)
    {
        var results = _paletteService.Search(query);
        ResultsList.ItemsSource = results;

        CategoryLabel.Text = string.IsNullOrWhiteSpace(query)
            ? "RECENT"
            : $"RESULTS ({results.Count})";

        if (results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
        }
    }

    private void ExecuteSelectedCommand()
    {
        if (ResultsList.SelectedItem is PaletteCommand command)
        {
            SelectedActionId = command.ActionId;
            SelectedCategory = command.Category;
            _paletteService.Execute(command.Id);
            DialogResult = true;
            Close();
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                DialogResult = false;
                Close();
                e.Handled = true;
                break;

            case Key.Enter:
                ExecuteSelectedCommand();
                e.Handled = true;
                break;

            case Key.Down:
                if (ResultsList.Items.Count > 0)
                {
                    var newIndex = (ResultsList.SelectedIndex + 1) % ResultsList.Items.Count;
                    ResultsList.SelectedIndex = newIndex;
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (ResultsList.Items.Count > 0)
                {
                    var newIndex = ResultsList.SelectedIndex <= 0
                        ? ResultsList.Items.Count - 1
                        : ResultsList.SelectedIndex - 1;
                    ResultsList.SelectedIndex = newIndex;
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                }
                e.Handled = true;
                break;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Close when window loses focus
        if (IsVisible)
        {
            DialogResult = false;
            Close();
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedCommand();
    }

    private void OnResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelectedCommand();
            e.Handled = true;
        }
    }
}
