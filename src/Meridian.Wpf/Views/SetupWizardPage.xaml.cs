using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class SetupWizardPage : Page
{
    public SetupWizardPage()
    {
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
