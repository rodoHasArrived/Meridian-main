using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class WorkflowLibraryPage : Page
{
    public WorkflowLibraryPage(WorkflowLibraryViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }
}
