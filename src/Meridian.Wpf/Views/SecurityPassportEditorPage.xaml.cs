using System;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Hosts governed instrument-passport field editing and review.
/// </summary>
public partial class SecurityPassportEditorPage : Page
{
    public SecurityPassportEditorPage(SecurityPassportEditorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
