using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// ClusterStatusPage displays the current state of the multi-instance collector mesh.
/// </summary>
public partial class ClusterStatusPage : System.Windows.Controls.UserControl
{
    public ClusterStatusPage()
    {
        InitializeComponent();
    }

    public ClusterStatusPage(ClusterStatusViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
