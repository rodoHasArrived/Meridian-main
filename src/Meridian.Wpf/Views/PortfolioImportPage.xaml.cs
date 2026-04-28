using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class PortfolioImportPage : Page
{
    private readonly PortfolioImportViewModel _viewModel = new();

    public PortfolioImportPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }
}
