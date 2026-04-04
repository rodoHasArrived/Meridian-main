using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class FundAccountsPage : Page
{
    public FundAccountsPage(FundAccountsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
