using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to configure analysis export steps with guided prompts.
/// </summary>
public partial class AnalysisExportWizardPage : Page
{
    private readonly AnalysisExportWizardViewModel _viewModel = new();

    public AnalysisExportWizardPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
    }
}
