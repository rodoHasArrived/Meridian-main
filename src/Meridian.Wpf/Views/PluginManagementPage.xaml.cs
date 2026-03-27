using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for <see cref="PluginManagementPage"/>.
/// Thin shell — all business logic is in <see cref="PluginManagementViewModel"/>.
/// </summary>
public partial class PluginManagementPage : Page
{
    public PluginManagementPage(PluginManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
