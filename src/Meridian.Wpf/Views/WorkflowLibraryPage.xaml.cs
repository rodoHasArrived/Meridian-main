using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Hosts discoverable workflows and their navigation actions.
/// </summary>
public partial class WorkflowLibraryPage : Page
{
    public WorkflowLibraryPage(WorkflowLibraryViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }
}
