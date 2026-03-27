using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Batch backtest page code-behind. Provides minimal initialization; all logic is in the ViewModel.
/// </summary>
public partial class BatchBacktestPage : Page
{
    public BatchBacktestPage()
    {
        InitializeComponent();
    }

    public BatchBacktestPage(BatchBacktestViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
