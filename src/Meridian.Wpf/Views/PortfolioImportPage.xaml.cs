using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to import external portfolio snapshots for reconciliation.
/// </summary>
public partial class PortfolioImportPage : Page
{
    private readonly PortfolioImportViewModel _viewModel = new();

    public PortfolioImportPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }
}
