using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class ShellRailControl : UserControl
{
    public ShellRailControl()
    {
        InitializeComponent();
    }

    private void WorkspaceNavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainPageViewModel viewModel ||
            sender is not ListBox { SelectedItem: ShellNavigationItem item })
        {
            return;
        }

        if (string.Equals(viewModel.CurrentPageTag, item.PageTag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        viewModel.NavigateToPageCommand.Execute(item.PageTag);
    }
}

