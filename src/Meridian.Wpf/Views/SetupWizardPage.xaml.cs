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
        try
        {
            if (DataContext is SetupWizardViewModel viewModel)
            {
                await viewModel.LoadAsync();
            }
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Setup Wizard page failed to load.", ex);
        }
    }
}
