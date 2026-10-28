using System;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provides the desktop page to create fund-structure entities, ownership links, and account handoff drafts.
/// </summary>
public partial class FundStructureSetupPage : Page
{
    public FundStructureSetupPage(FundStructureSetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
