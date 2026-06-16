using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class OperationalExceptionTriagePage : Page
{
    public OperationalExceptionTriagePage(OperationalExceptionTriageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
