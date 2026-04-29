using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class TimeSeriesAlignmentPage : Page
{
    public TimeSeriesAlignmentPage()
        : this(new TimeSeriesAlignmentViewModel())
    {
    }

    internal TimeSeriesAlignmentPage(TimeSeriesAlignmentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
