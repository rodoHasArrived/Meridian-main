using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class InstitutionalCommandPaletteControl : UserControl
{
    public InstitutionalCommandPaletteControl()
    {
        InitializeComponent();
    }

    public void FocusInput()
    {
        CommandPaletteTextBox.Focus();
        CommandPaletteTextBox.SelectAll();
    }

    public bool TryHandleDirectionalKey(Key key)
    {
        if (DataContext is not MainPageViewModel viewModel)
        {
            return false;
        }

        var offset = key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0
        };

        if (offset == 0 || viewModel.CommandPalettePages.Count == 0)
        {
            return false;
        }

        var selectedIndex = viewModel.SelectedCommandPalettePage is null
            ? -1
            : viewModel.CommandPalettePages.IndexOf(viewModel.SelectedCommandPalettePage);

        var nextIndex = selectedIndex < 0
            ? offset > 0 ? 0 : viewModel.CommandPalettePages.Count - 1
            : Math.Clamp(selectedIndex + offset, 0, viewModel.CommandPalettePages.Count - 1);

        var selectedPage = viewModel.CommandPalettePages[nextIndex];
        viewModel.SelectedCommandPalettePage = selectedPage;
        CommandPaletteResults.ScrollIntoView(selectedPage);
        return true;
    }

    private void CommandPaletteOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainPageViewModel viewModel)
        {
            viewModel.HideCommandPaletteCommand.Execute(null);
        }
    }

    private void CommandPaletteBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void CommandPaletteResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed || DataContext is not MainPageViewModel viewModel)
        {
            return;
        }

        viewModel.OpenSelectedCommandPalettePageCommand.Execute(null);
    }

    private void CommandPaletteTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (TryHandleDirectionalKey(e.Key))
        {
            e.Handled = true;
        }
    }
}

